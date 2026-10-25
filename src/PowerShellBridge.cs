using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OnlyTweakWindows
{
    internal static class EmbeddedScripts
    {
        public static void WriteTo(string name,string path)
        {
            using (Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak."+name) ??
                throw new FileNotFoundException("Procedura incorporata assente."))
            using (FileStream output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                input.CopyTo(output); // Preserve exact signed bytes, BOM and line endings.
        }
        public static string Read(string name)
        {
            using (Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak."+name) ??
                throw new FileNotFoundException("Procedura incorporata assente."))
            using (StreamReader reader=new StreamReader(input,Encoding.UTF8)) return reader.ReadToEnd();
        }
    }
    internal sealed class PowerShellBridge
    {
        private readonly string scriptOverride;
        internal PowerShellBridge(string scriptOverride=null) { this.scriptOverride=scriptOverride; }
        private static async Task Drain(StreamReader reader,BoundedText output)
        {
            char[] buffer=new char[4096]; int count;
            while ((count=await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))!=0)
                output.Append(new string(buffer,0,count));
        }
        public async Task<StepResult> ExecuteAsync(OperationRequest request,ActionDefinition action,CancellationToken cancellation)
        {
            StepResult result=new StepResult {OperationId=request.OperationId,ActionId=action.Id,
                ProfileId=request.Profile.Id,Target=action.Elevated || action.Id=="technician.inspect" ? Environment.MachineName : request.Target,Started=DateTimeOffset.UtcNow};
            string folder=Path.Combine(Path.GetTempPath(),"OnlyTweak-"+Guid.NewGuid().ToString("N"));
            SecureFiles.EnsureDirectory(folder);
            string script=Path.Combine(folder,"Operations.ps1");
            try
            {
                if (scriptOverride==null) EmbeddedScripts.WriteTo("Operations.ps1",script);
                else File.WriteAllText(script,scriptOverride,new UTF8Encoding(true));
                string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
                ProcessStartInfo start=new ProcessStartInfo(Path.Combine(system,@"WindowsPowerShell\v1.0\powershell.exe")) {
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
                    RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),
                    StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=system
                };
                foreach (string argument in new[] {"-NoLogo","-NoProfile","-NonInteractive","-File",script}) start.ArgumentList.Add(argument);
                start.Environment["PSModulePath"]=Path.Combine(system,@"WindowsPowerShell\v1.0\Modules");
                start.Environment["PATH"]=system+";"+Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                using (Process process=new Process {StartInfo=start})
                {
                    cancellation.ThrowIfCancellationRequested();
                    process.Start();
                    BoundedText stdout=new BoundedText(),stderr=new BoundedText(8192);
                    Task output=Drain(process.StandardOutput,stdout),error=Drain(process.StandardError,stderr);
                    await process.StandardInput.WriteLineAsync(Wire.Write(request)).ConfigureAwait(false);
                    process.StandardInput.Close();
                    using (CancellationTokenSource timeout=action.IsChange ? new CancellationTokenSource() :
                        new CancellationTokenSource(TimeSpan.FromSeconds(action.TimeoutSeconds)))
                    using (CancellationTokenSource linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,timeout.Token))
                    {
                        try { await process.WaitForExitAsync(action.IsChange ? CancellationToken.None : linked.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException)
                        {
                            if (!process.HasExited) process.Kill(true);
                            await process.WaitForExitAsync().ConfigureAwait(false);
                            await Task.WhenAll(output,error).ConfigureAwait(false);
                            result.State=cancellation.IsCancellationRequested ? StepState.Cancelled : StepState.Inconclusive;
                            result.Summary=cancellation.IsCancellationRequested ? "Diagnostica annullata." : "Timeout diagnostico; esito sconosciuto.";
                            result.ErrorCode="DiagnosticTimeoutOrCancellation";
                            return result;
                        }
                    }
                    await Task.WhenAll(output,error).ConfigureAwait(false);
                    result.Truncated=stdout.Truncated || stderr.Truncated;
                    if (result.Truncated) {
                        result.State=action.IsChange ? StepState.Unverified : StepState.Inconclusive;
                        result.Summary="Risultato oltre il budget: impossibile certificare l'esito.";
                        result.ErrorCode="OutputBudgetExceeded";
                    } else {
                        try {
                            StepResult parsed=OperationResultContract.Read(stdout.Read(false).Trim(),request);
                            result.State=parsed.State; result.Summary=parsed.Summary; result.Evidence=parsed.Evidence??Array.Empty<string>();
                            result.Data=parsed.Data; result.AuthenticationVerified=parsed.AuthenticationVerified;
                            result.ErrorCode=parsed.ErrorCode;
                        } catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is FormatException) {
                            result.State=action.IsChange ? StepState.Unverified : StepState.Unavailable;
                            result.Summary="Windows PowerShell non ha prodotto un risultato strutturato. Verifica policy, firma e disponibilità dei moduli.";
                            result.ErrorCode="PowerShellBridgeUnavailable";
                        }
                    }
                    if (process.ExitCode!=0 && result.State==StepState.Success) {
                        result.State=action.IsChange ? StepState.Unverified : StepState.Failed;
                        result.ErrorCode="PowerShellExitCode"+process.ExitCode;
                    }
                }
                return result;
            }
            finally
            {
                result.Finished=DateTimeOffset.UtcNow;
                // This lease contains exactly the single embedded script created above.
                if (File.Exists(script)) File.Delete(script);
                if (Directory.Exists(folder)) Directory.Delete(folder,false);
            }
        }
    }
    internal sealed class EnterpriseExecutor
    {
        private readonly IOperationAudit audit;
        private readonly Func<OperationRequest,ActionDefinition,CancellationToken,Task<StepResult>> run;
        internal EnterpriseExecutor(IOperationAudit audit=null,
            Func<OperationRequest,ActionDefinition,CancellationToken,Task<StepResult>> run=null)
        { this.audit=audit??new FileOperationAudit(); this.run=run??new PowerShellBridge().ExecuteAsync; }
        public async Task<StepResult> ExecuteAsync(OperationRequest request,string identity,CancellationToken cancellation)
        {
            ActionDefinition action=EnterpriseCatalog.Get(request.ActionId);
            action.Validate(request);
            if (action.IsChange) audit.Record(request,identity,"requested"); // fail closed before invoking anything
            StepResult result;
            try { result=await run(request,action,cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                result=new StepResult {OperationId=request.OperationId,ProfileId=request.Profile.Id,ActionId=action.Id,
                    Target=request.Target,Started=DateTimeOffset.UtcNow,Finished=DateTimeOffset.UtcNow,
                    State=action.IsChange ? StepState.Unverified : StepState.Cancelled,
                    Summary=action.IsChange ? "Intervento non verificato: rileggere lo stato." : "Diagnostica annullata prima dell'avvio.",
                    ErrorCode="OperationCancelled"};
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is InvalidOperationException || ex is FormatException || ex is System.ComponentModel.Win32Exception)
            {
                result=new StepResult {OperationId=request.OperationId,ProfileId=request.Profile.Id,ActionId=action.Id,
                    Target=request.Target,Started=DateTimeOffset.UtcNow,Finished=DateTimeOffset.UtcNow,
                    State=action.IsChange ? StepState.Unverified : StepState.Unavailable,
                    Summary="Esecuzione non verificabile. Non ripetere modifiche senza rileggere lo stato.",ErrorCode=ex.GetType().Name};
            }
            result.Identity=identity;
            if (action.AuthenticationRequired && !result.AuthenticationVerified && result.State==StepState.Success) {
                result.State=action.IsChange ? StepState.Unverified : StepState.Inconclusive;
                result.Summary="Autenticazione target non verificata: nessuna conclusione positiva.";
            }
            if (action.IsChange)
                try { audit.Record(request,identity,"finished",result); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                { result.State=StepState.Unverified; result.Summary="Audit finale non disponibile: verifica manuale richiesta."; }
            return result;
        }
    }
}
