using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace OnlyTweakWindows
{
    internal sealed class WorkerMessage
    {
        public string Kind { get; set; } = "";
        public OperationRequest Request { get; set; }
        public EnvironmentProfile Profile { get; set; }
        public StepResult Result { get; set; }
        public string Identity { get; set; } = "";
        public string LocalAction { get; set; } = "";
        public bool LocalReadOnly { get; set; }
        public bool LocalConfirmed {get;set;}
    }
    internal sealed class PipeChannel : IDisposable
    {
        private readonly Stream stream;
        private readonly SemaphoreSlim writes=new SemaphoreSlim(1,1);
        internal const int MaximumBytes=2097152;
        public PipeChannel(Stream stream) { this.stream=stream; }
        public async Task Send(WorkerMessage message)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(Wire.Write(message));
            if (bytes.Length>MaximumBytes) throw new IOException("Messaggio oltre il budget.");
            await writes.WaitAsync().ConfigureAwait(false);
            try { await stream.WriteAsync(BitConverter.GetBytes(bytes.Length)).ConfigureAwait(false);
                await stream.WriteAsync(bytes).ConfigureAwait(false); await stream.FlushAsync().ConfigureAwait(false); }
            finally { writes.Release(); }
        }
        public async Task<WorkerMessage> Receive(CancellationToken token=default)
        {
            byte[] size=new byte[4]; await stream.ReadExactlyAsync(size,token).ConfigureAwait(false);
            int count=BitConverter.ToInt32(size);
            if (count<1 || count>MaximumBytes) throw new IOException("Dimensione messaggio non ammessa.");
            byte[] bytes=new byte[count]; await stream.ReadExactlyAsync(bytes,token).ConfigureAwait(false);
            return Wire.Read<WorkerMessage>(Encoding.UTF8.GetString(bytes));
        }
        public void Dispose() { stream.Dispose(); writes.Dispose(); }
    }
    internal sealed class CustomerSession : IDisposable
    {
        private NamedPipeServerStream server;
        private PipeChannel channel;
        private Process process;
        private readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public EnvironmentProfile Profile { get; private set; }
        public string Identity { get; private set; }
        public bool Elevated { get; private set; }
        private bool disposed;
        public static async Task<CustomerSession> Start(EnvironmentProfile profile,bool alternate,bool elevated=false,IntPtr owner=default)
        {
            profile.Validate();
            CustomerSession session=new CustomerSession {Profile=profile.Copy(),Elevated=elevated};
            string pipe="OnlyTweak-"+Guid.NewGuid().ToString("N");
            PipeSecurity security=new PipeSecurity();
            security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            foreach (SecurityIdentifier sid in new[] {WindowsIdentity.GetCurrent().User,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
            session.server=NamedPipeServerStreamAcl.Create(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,0,0,security);
            try
            {
                string executable=Environment.ProcessPath;
                string args="--worker "+pipe+" "+Environment.ProcessId+" "+(elevated ? "local" : "network");
                string identity=EnvironmentInfo.Account;
                if (alternate) session.process=WindowsCredentials.Start(executable,args,owner,out identity);
                else session.process=Process.Start(new ProcessStartInfo(executable,args) {
                    UseShellExecute=elevated,Verb=elevated ? "runas" : "",CreateNoWindow=!elevated,
                    WorkingDirectory=Path.GetDirectoryName(executable)
                });
                using (CancellationTokenSource timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                    await session.server.WaitForConnectionAsync(timeout.Token);
                if (!GetNamedPipeClientProcessId(session.server.SafePipeHandle,out uint client) || client!=session.process.Id)
                    throw new IOException("Processo sessione non riconosciuto.");
                session.channel=new PipeChannel(session.server);
                await session.channel.Send(new WorkerMessage {Kind="bind",Profile=session.Profile,Identity=identity});
                using (CancellationTokenSource timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15))) {
                    WorkerMessage ready=await session.channel.Receive(timeout.Token);
                    if (ready.Kind!="ready") throw new IOException("Sessione non inizializzata.");
                    session.Identity=ready.Identity;
                }
                return session;
            }
            catch { session.Dispose(); throw; }
        }
        public async Task<StepResult> Execute(OperationRequest request,CancellationToken token=default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CustomerSession));
            if (Wire.Write(request.Profile)!=Wire.Write(Profile)) throw new InvalidOperationException("Il profilo è cambiato: riconnetti la sessione.");
            await gate.WaitAsync(token);
            try
            {
                await channel.Send(new WorkerMessage {Kind="execute",Request=request});
                using (token.Register(()=> { if (!EnterpriseCatalog.Get(request.ActionId).IsChange) _=Cancel(); }))
                {
                    WorkerMessage response=await channel.Receive();
                    if (response.Kind!="result" || response.Result==null || response.Result.OperationId!=request.OperationId)
                        throw new IOException("Risposta sessione non valida.");
                    return response.Result;
                }
            }
            finally { gate.Release(); }
        }
        private async Task Cancel()
        { try { await channel.Send(new WorkerMessage {Kind="cancel"}); } catch (IOException) { } catch (ObjectDisposedException) { } }
        public async Task<StepResult> ExecuteLocal(string actionId,bool readOnly=false,CancellationToken cancellation=default)
        {
            if (!Elevated) throw new InvalidOperationException("Sessione locale elevata richiesta.");
            bool diagnostic=readOnly || LocalOperationExecutor.IsDiagnostic(actionId);
            if(diagnostic) cancellation.ThrowIfCancellationRequested();
            await channel.Send(new WorkerMessage {Kind="local",LocalAction=actionId,LocalReadOnly=readOnly,LocalConfirmed=!readOnly && !diagnostic});
            using(cancellation.Register(()=> {if(diagnostic) _=Cancel();})) {
                WorkerMessage response=await channel.Receive();
                if (response.Kind!="result" || response.Result==null || response.Result.ActionId!=actionId) throw new IOException("Risposta locale non valida.");
                return response.Result;
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed=true;
            // Closing the transport cancels read-only work. Worker waits for any change already started.
            channel?.Dispose(); server?.Dispose(); process?.Dispose(); gate.Dispose();
        }
        [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint processId);
    }
    internal static class WorkerHost
    {
        public static bool TryRun(string[] args)
        {
            if (args.Length==0 || args[0]!="--worker") return false;
            if (args.Length!=4 || !args[1].StartsWith("OnlyTweak-",StringComparison.Ordinal) ||
                !Int32.TryParse(args[2],out int parent) || (args[3]!="network" && args[3]!="local"))
                throw new ArgumentException("Avvio worker non valido.");
            Run(args[1],parent,args[3]=="local").GetAwaiter().GetResult();
            return true;
        }
        private static async Task Run(string name,int parent,bool elevated)
        {
            if (elevated && !LocalPrivilege.IsAdministrator) throw new InvalidOperationException("Elevazione locale non disponibile.");
            using (NamedPipeClientStream pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(15000).ConfigureAwait(false);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint server) || server!=parent) throw new IOException("Processo chiamante non valido.");
                using (Process caller=Process.GetProcessById(parent)) {
                    if (!String.Equals(caller.MainModule.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Eseguibile chiamante non valido.");
                }
                using (PipeChannel channel=new PipeChannel(pipe))
                {
                    WorkerMessage bind=await channel.Receive().ConfigureAwait(false);
                    if (bind.Kind!="bind" || bind.Profile==null) throw new IOException("Profilo sessione mancante.");
                    bind.Profile.Validate();
                    string profile=Wire.Write(bind.Profile);
                    string identity="Locale: "+EnvironmentInfo.Account+"; rete dichiarata: "+bind.Identity;
                    await channel.Send(new WorkerMessage {Kind="ready",Identity=identity}).ConfigureAwait(false);
                    Task active=null;
                    CancellationTokenSource cancellation=null;
                    bool change=false;
                    SessionSafety safety=new SessionSafety();
                    try
                    {
                        while (true)
                        {
                            WorkerMessage message=await channel.Receive().ConfigureAwait(false);
                            if (message.Kind=="cancel") { if (!change) cancellation?.Cancel(); continue; }
                            if (active!=null) await active.ConfigureAwait(false);
                            cancellation?.Dispose(); cancellation=new CancellationTokenSource();
                            if (message.Kind=="local")
                            {
                                if (!elevated) throw new InvalidOperationException("Worker locale richiesto.");
                                change=!message.LocalReadOnly && !LocalOperationExecutor.IsDiagnostic(message.LocalAction);
                                CancellationToken localToken=cancellation.Token;
                                active=Task.Run(async ()=> {
                                    StepResult result;
                                    try { result=LocalPrivilege.RunAuthorized(message.LocalAction,localToken,message.LocalReadOnly,message.LocalConfirmed); }
                                    catch (Exception ex) {
                                        result=new StepResult {ActionId=message.LocalAction,State=StepState.Unverified,
                                            Summary="Intervento locale non verificato: controllare audit e stato prima di riprovare.",
                                            ErrorCode=ex.GetType().Name,Identity=identity,Target=Environment.MachineName};
                                    }
                                    await channel.Send(new WorkerMessage {Kind="result",Result=result}).ConfigureAwait(false);
                                });
                                continue;
                            }
                            if (message.Kind!="execute" || message.Request==null || Wire.Write(message.Request.Profile)!=profile)
                                throw new IOException("Richiesta estranea alla sessione.");
                            ActionDefinition action=EnterpriseCatalog.Get(message.Request.ActionId);
                            action.Validate(message.Request);
                            try { safety.Authorize(message.Request); }
                            catch (InvalidOperationException) {
                                await channel.Send(new WorkerMessage {Kind="result",Result=new StepResult {
                                    OperationId=message.Request.OperationId,ProfileId=bind.Profile.Id,ActionId=action.Id,
                                    Target=message.Request.Target,Identity=identity,State=StepState.Unverified,
                                    Summary="Rileggi prima lo stato: l'intervento precedente non è verificato.",ErrorCode="ReadBeforeRetryRequired"}});
                                continue;
                            }
                            if (action.Elevated!=elevated) throw new InvalidOperationException("Tipo di worker non valido per questa operazione.");
                            change=action.IsChange;
                            if (elevated && change && !message.Request.Confirmed)
                            {
                                await channel.Send(new WorkerMessage {Kind="result",Result=new StepResult {
                                    OperationId=message.Request.OperationId,ProfileId=bind.Profile.Id,ActionId=action.Id,State=StepState.Cancelled,
                                    Target=message.Request.Target,Identity=identity,Summary="Operazione annullata." }}).ConfigureAwait(false);
                                continue;
                            }
                            CancellationToken token=cancellation.Token;
                            active=Task.Run(async ()=> {
                                StepResult result;
                                try { result=await new EnterpriseExecutor().ExecuteAsync(message.Request,identity,token).ConfigureAwait(false); }
                                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is InvalidOperationException)
                                { result=new StepResult {OperationId=message.Request.OperationId,ProfileId=bind.Profile.Id,ActionId=action.Id,
                                    Target=message.Request.Target,Identity=identity,State=action.IsChange ? StepState.Unverified : StepState.Unavailable,
                                    Summary="Operazione non verificata: audit o prerequisiti non disponibili.",ErrorCode=ex.GetType().Name}; }
                                safety.Observe(message.Request,result);
                                await channel.Send(new WorkerMessage {Kind="result",Result=result}).ConfigureAwait(false);
                            });
                        }
                    }
                    catch (IOException) { if (!change) cancellation?.Cancel(); }
                    finally {
                        if (active!=null) try { await active.ConfigureAwait(false); } catch (IOException) { }
                        cancellation?.Dispose();
                    }
                }
            }
        }
        internal static string Preview(OperationRequest request) =>
            "Cliente: "+request.Profile.Customer+"\nDominio: "+request.Profile.Domain+"\nTarget: "+
            (EnterpriseCatalog.Get(request.ActionId).Elevated ? Environment.MachineName+" (computer locale)" : request.Target)+
            "\nOperazione: "+EnterpriseCatalog.Get(request.ActionId).Title+
            "\n"+String.Join("\n",request.Parameters.Select(p=>p.Key+": "+p.Value))+
            "\nLa modifica può interrompere servizi o connessioni. Nessun riavvio automatico.";
        [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
    }
}
