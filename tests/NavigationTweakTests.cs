using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static void ShowPage(Control control)
        {
            if(control is Form window) {window.Show();return;}
            var page=(DarkPage)control;
            var host=new MainForm(TestApp(),page) {ShowInTaskbar=false,ClientSize=page.Size};
            page.Disposed+=delegate {if(!host.IsDisposed && !host.Disposing) host.Dispose();};
            host.Show();
        }
        private static void ClosePage(Control control) {(control as Form??control.FindForm())?.Close();Application.DoEvents();}
        private static void Await(Task task) {PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
        private static void NewFeatureTests()
        {
            Test("tweak: recupero persistente e conflitti esterni",()=> WithTweaks((store,service,reopen,folder)=> {
                const string path=RegistryTweakService.ExplorerPath;
                store.Set(path,"HideFileExt",9,RegistryValueKind.DWord);
                service.Apply("extensions",true,Ignore);
                Assert((int)store.Read(path,"HideFileExt").Data==0,"Tweak non applicato.");
                reopen().Apply("extensions",null,Ignore);
                Assert((int)store.Read(path,"HideFileExt").Data==9,"Originale non recuperato dopo riapertura.");
                service.Apply("extensions",false,Ignore);
                store.Set(path,"HideFileExt",7,RegistryValueKind.DWord);
                Throws(()=>reopen().Apply("extensions",null,Ignore),"esterna");
                Assert((int)store.Read(path,"HideFileExt").Data==7,"Modifica esterna sovrascritta.");
            }));
            Test("tweak: chiavi condivise ripulite e valori estranei preservati",()=> WithTweaks((store,service,reopen,folder)=> {
                service.Apply("extensions",true,Ignore);service.Apply("hidden",true,Ignore);
                service.Apply("extensions",null,Ignore);
                Assert(store.Read(RegistryTweakService.ExplorerPath,"Hidden").ValueExists,"Chiave condivisa rimossa.");
                service.Apply("hidden",null,Ignore);
                Assert(!store.Read(RegistryTweakService.ExplorerPath,"").KeyExists,"Residuo chiave creata dall'app.");
                service.Apply("launch",true,Ignore);store.Set(RegistryTweakService.ExplorerPath,"Foreign",1,RegistryValueKind.DWord);
                service.Apply("launch",null,Ignore);
                Assert(store.Read(RegistryTweakService.ExplorerPath,"Foreign").ValueExists,"Valore estraneo rimosso.");
            }));
            Test("tweak: quattro opzioni, tipi originali e COM personalizzata",()=> WithTweaks((store,service,reopen,folder)=> {
                Assert(service.Definitions.Length==4,"Catalogo Windows 11 incompleto.");
                store.Set(RegistryTweakService.ExplorerPath,"LaunchTo",new[]{"custom","value"},RegistryValueKind.MultiString);
                service.Apply("launch",true,Ignore);reopen().Apply("launch",null,Ignore);
                Assert(((string[])store.Read(RegistryTweakService.ExplorerPath,"LaunchTo").Data).SequenceEqual(new[]{"custom","value"}),"Tipo originale perso.");
                store.Set(RegistryTweakService.ClassicPath,"","foreign.dll",RegistryValueKind.String);
                Throws(()=>service.Apply("classic",true,Ignore),"personalizzata");
                Assert(!service.HasCheckpoint("classic"),"Checkpoint per scrittura rifiutata.");
            }));
            Test("tweak: checkpoint alterati, incompleti e identità bloccati",()=> WithTweaks((store,service,reopen,folder)=> {
                service.Apply("hidden",true,Ignore);string path=Path.Combine(folder,"hidden.json");string original=File.ReadAllText(path);
                File.WriteAllText(path,original.Replace("Applied","Pending"));
                Throws(()=>reopen().Apply("hidden",null,Ignore),"alterato");
                File.WriteAllText(path,original);store.FailWrite=true;
                Throws(()=>service.Apply("hidden",false,Ignore),"fixture");store.FailWrite=false;
                Throws(()=>reopen().Apply("hidden",null,Ignore),"incompleto");
                int writes=store.Writes;
                var denied=new RegistryTweakService(store,TestProfile(),folder,()=>throw new UnauthorizedAccessException("desktop diverso"),"fixture","S-1-5-21-1");
                Throws(()=>denied.Apply("launch",true,Ignore),"desktop diverso");Assert(store.Writes==writes,"Identità non autorizzata ha scritto.");
            }));
            Test("diagnostiche: catalogo chiuso e parametri tipizzati",()=> {
                foreach(string host in new[]{"server","-x.example.com","a.example.com;whoami","$(whoami).example.com","a.example.com\r\n"})
                    Throws(()=>new DiagnosticRequest("local.tcp",host).Validate(),"DNS");
                Throws(()=>new DiagnosticRequest("local.tcp","a.example.com",0).Validate(),"Porta");
                Throws(()=>new DiagnosticRequest("local.dns","a.example.com",443,"ANY").Validate(),"DNS");
                Throws(()=>new DiagnosticRequest("local.shell").Validate(),"consentita");
                var ping=LocalDiagnostics.Native(new("local.ping","a.example.com"));
                Assert(ping.Arguments.SequenceEqual(new[]{"-n","4","-w","1000","a.example.com"}),"Argomenti ping non separati.");
                Assert(!new RegistryTweakService(new TweakRegistry(),TestProfile(19045),"unused",()=>{},"fixture","sid").Definitions.Any(d=>d.Id=="classic"),"Menu classico esposto su Windows 10.");
            });
            Test("diagnostiche: protocollo positivo richiede evidenze coerenti",()=> {
                var request=new OperationRequest {ActionId="local.tcp",Target="a.example.com",Parameters=new() {["port"]="443"}};
                string Result(bool passed,string target)=>Wire.Write(new StepResult {State=StepState.Success,Summary="Fixture",Evidence=Array.Empty<string>(),ErrorCode="",Data=JsonSerializer.SerializeToElement(new{Target=target,Port=443,ReadComplete=true,TcpTestSucceeded=passed})});
                Assert(OperationResultContract.Read(Result(true,request.Target),request).State==StepState.Success,"Risultato coerente rifiutato.");
                Throws(()=>OperationResultContract.Read(Result(false,request.Target),request),"insufficienti");
                Throws(()=>OperationResultContract.Read(Result(true,"other.example.com"),request),"insufficienti");
                var app=TestApp();var diagnostic=new ActionSpec("fixture.negative","Fixture","Fixture","Fixture",_=>{}) {IsDiagnostic=true,ExecuteResult=(log,token)=>new StepResult {State=StepState.ObservedIssue}};
                var negative=new LocalOperationExecutor(new FakeAudit()).Execute(diagnostic,Ignore,CancellationToken.None);
                Assert(negative.State==StepState.ObservedIssue && negative.Summary.Contains("negativo") && negative.Data.Value.GetProperty("ExecutionCompleted").GetBoolean(),"Test negativo o completamento perso.");
            });
            Test("tweak: backup e audit falliti precedono ogni scrittura",()=> WithTweaks((store,service,reopen,folder)=> {
                Directory.CreateDirectory(folder);string blocker=Path.Combine(folder,"extensions.json");Directory.CreateDirectory(blocker);
                try {try {service.Apply("extensions",true,Ignore);throw new Exception("Backup non richiesto.");} catch(IOException) {}Assert(store.Writes==0,"Scrittura senza backup.");}
                finally {Directory.Delete(blocker,false);}
                var action=service.Action("launch",true);var audit=new FakeAudit {Fail=true};
                var result=new LocalOperationExecutor(audit).Execute(action,Ignore,CancellationToken.None);
                Assert(result.State==StepState.Unverified && store.Writes==0,"Audit fallito ha permesso la scrittura.");
                store.Set(RegistryTweakService.ExplorerPath,"HideFileExt",new byte[150000],RegistryValueKind.Binary);
                int writes=store.Writes;
                Throws(()=>service.Apply("extensions",true,Ignore),"oltre il limite");
                Assert(store.Writes==writes && store.Read(RegistryTweakService.ExplorerPath,"HideFileExt").Kind==RegistryValueKind.Binary,"Valore modificato senza checkpoint recuperabile.");
            }));
            Test("diagnostiche: procedure PowerShell effettive con fixture isolate",()=> {
                StepResult Run(string id,bool negative=false,bool missing=false,int timeout=60,bool sleep=false) {
                    string mocks="$global:negative=$"+(negative ? "true" : "false")+";$global:missing=$"+(missing ? "true" : "false")+";$global:wait=$"+(sleep ? "true" : "false")+";\n"+"""
                        function Get-Command { param($Name,$CommandType,$ErrorAction) if(!$global:missing){[pscustomobject]@{Name=$Name}} }
                        function Resolve-DnsName { [CmdletBinding()]param($Name,$Type,[switch]$DnsOnly)
                            if($global:wait){Start-Sleep -Seconds 5}
                            if(!$global:negative){[pscustomobject]@{Name=$Name;Type=$Type;IPAddress='192.0.2.1';TTL=60}}
                        }
                        function Test-NetConnection { [CmdletBinding()]param($ComputerName,$Port,$InformationLevel)
                            [pscustomobject]@{TcpTestSucceeded=(!$global:negative);RemoteAddress='192.0.2.1';SourceAddress='192.0.2.2'}
                        }
                        """;
                    var request=new OperationRequest {Profile=LocalPrivilege.TechnicianProfile(),ActionId=id,Target="fixture.example.com",Parameters=new() {["port"]="443",["recordType"]="A"}};
                    var definition=new ActionDefinition(id,"Fixture",ActionRisk.ReadOnly,false,false,timeout,"");
                    return new PowerShellBridge(mocks+EmbeddedScripts.Read("LocalDiagnostics.ps1"),"LocalDiagnostics.ps1").ExecuteAsync(request,definition,CancellationToken.None).GetAwaiter().GetResult();
                }
                Assert(Run("local.dns").State==StepState.Success,"DNS valido rifiutato.");
                Assert(Run("local.dns",negative:true).State==StepState.ObservedIssue,"DNS vuoto non distinto.");
                Assert(Run("local.tcp").State==StepState.Success,"TCP riuscito rifiutato.");
                Assert(Run("local.tcp",negative:true).State==StepState.ObservedIssue,"TCP negativo non distinto.");
                Assert(Run("local.tcp",missing:true).State==StepState.Unavailable,"Cmdlet assente non distinto.");
                Assert(Run("local.dns",timeout:1,sleep:true).State==StepState.Inconclusive,"Timeout accettato come successo.");
            });
        }
        private static void NavigationTests()
        {
            Test("navigazione: password svuotata e consegnata una sola volta",()=> {
                using var host=new MainForm(TestApp());ShowPage(host);
                using var entry=new PasswordEntry();var pending=host.Navigation.PresentAsync(entry);
                var fields=Descendants(entry).OfType<TextBox>().ToArray();
                foreach(var field in fields) field.Text="fixture-only-secret";
                Descendants(entry).OfType<Button>().Single(b=>b.Text=="Usa password").PerformClick();Await(pending);
                string secret=entry.TakePassword();
                Assert(pending.Result==DialogResult.OK && !String.IsNullOrEmpty(secret) && !secret.Contains("fixture-only-secret"),"Password protetta persa o in chiaro.");
                Assert(fields.All(f=>f.TextLength==0) && entry.TakePassword()==null,"Password conservata nei controlli o consegnata due volte.");
                ClosePage(host);
            });
            Test("navigazione: stato aziendale e feedback nella sottopagina corrente",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var workspace=new EnterpriseDialog(false,false) {Persistent=true};host.Navigation.Push(workspace);
                var directory=workspace.Sections.ElementAt(2);workspace.OpenSection(directory);
                var search=Descendants(directory).OfType<TextBox>().Single();search.Text="fixture";
                Descendants(directory).OfType<Button>().Single(b=>b.Text=="Cerca").PerformClick();
                PumpUntil(()=>Descendants(host.Navigation.Current).OfType<Label>().Any(l=>l.Text.Contains("Connetti prima")));
                Await(host.Navigation.BackAsync());workspace.OpenSection(directory);
                Assert(search.Text=="fixture" && host.Navigation.Current.BackButton.Visible,"Stato o navigazione aziendale persi.");
                ClosePage(host);
            });
            Test("navigazione: finestra unica, ritorno e conservazione parametri",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);int count=Application.OpenForms.Count;
                host.Navigation.Push(new MenuDialog(app,app.Catalog.Network));
                Await(ActionDispatcher.Invoke(host,app,app.Catalog.Network.AllActions().Single(a=>a.Id=="local.tcp"),Ignore));
                DarkPage parameters=host.Navigation.Current;var field=Descendants(parameters).OfType<TextBox>().Single(b=>b.PlaceholderText=="server.example.com");field.Text="server.example.com";
                host.Navigation.Push(new TextDialog("Risultato fixture","Nessun comando reale"));
                Await(host.Navigation.BackAsync());Assert(ReferenceEquals(parameters,host.Navigation.Current) && field.Text=="server.example.com","Stato perso tornando indietro.");
                Assert(Application.OpenForms.Count==count,"Aperta una seconda finestra applicativa.");
                Await(host.Navigation.BackAsync());Await(host.Navigation.BackAsync());Assert(host.Navigation.Depth==1 && !host.Navigation.Current.BackButton.Visible,"Home o freccia incoerente.");ClosePage(host);
            });
            Test("navigazione: editor sporco conferma internamente e annulla mantiene stato",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                host.Navigation.Push(new ProfileEditor(EnterpriseProfile()));var editor=host.Navigation.Current;
                Descendants(editor).OfType<TextBox>().First().Text="Modificato";
                var back=host.Navigation.BackAsync();PumpUntil(()=>host.Navigation.Current is ConfirmDialog);
                Assert(!back.IsCompleted,"Editor abbandonato senza conferma.");
                Descendants(host.Navigation.Current).OfType<Button>().Single(b=>b.Text=="&Annulla").PerformClick();Await(back);
                Assert(ReferenceEquals(host.Navigation.Current,editor),"Annulla ha perso l'editor.");
                back=host.Navigation.BackAsync();PumpUntil(()=>host.Navigation.Current is ConfirmDialog);
                Descendants(host.Navigation.Current).OfType<Button>().Single(b=>b.Text=="&Conferma").PerformClick();Await(back);
                Assert(host.Navigation.Depth==1,"Editor non abbandonato.");ClosePage(host);
            });
            Test("navigazione: Indietro annulla diagnosi e attende rilascio",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var action=new ActionSpec("fixture.cancel","Fixture","Fixture","Fixture",_=>{}) {IsDiagnostic=true,ExecuteResult=(log,token)=> {token.WaitHandle.WaitOne(4000);token.ThrowIfCancellationRequested();return new StepResult {State=StepState.Success};}};
                host.Navigation.Push(new OperationDialog(app,action,true));PumpUntil(()=>app.Coordinator.IsBusy);
                Await(host.Navigation.BackAsync());Assert(!app.Coordinator.IsBusy && host.Navigation.Depth==1,"Navigazione prima del rilascio.");ClosePage(host);
            });
        }
        private static void WithTweaks(Action<TweakRegistry,RegistryTweakService,Func<RegistryTweakService>,string> test)
        {
            string folder=Path.Combine(Path.GetTempPath(),"OnlyTweak-tweak-fixture-"+Guid.NewGuid().ToString("N"));
            var registry=new TweakRegistry();
            RegistryTweakService Create()=>new(registry,TestProfile(),folder,()=>{},"fixture","S-1-5-21-1");
            try {test(registry,Create(),Create,folder);}
            finally {if(Directory.Exists(folder)) {foreach(string file in Directory.GetFiles(folder)) File.Delete(file);Directory.Delete(folder,false);}}
        }
    }
    internal sealed class TweakRegistry : IRegistryStore
    {
        private readonly Dictionary<string,Dictionary<string,(object Value,RegistryValueKind Kind)>> keys=new(StringComparer.OrdinalIgnoreCase);
        internal bool FailWrite;
        internal int Writes;
        public RegistryValueState Read(string path,string name) {
            var result=new RegistryValueState {KeyExists=keys.ContainsKey(path)};
            if(keys.TryGetValue(path,out var values) && values.TryGetValue(name,out var value)) {result.ValueExists=true;result.Kind=value.Kind;result.Data=value.Value;}
            return result;
        }
        public void Set(string path,string name,object data,RegistryValueKind kind) {
            if(FailWrite) throw new IOException("fixture write failed");Writes++;
            for(string current=path;current.Length>0;) {if(!keys.ContainsKey(current)) keys.Add(current,new(StringComparer.OrdinalIgnoreCase));int index=current.LastIndexOf('\\');if(index<0) break;current=current[..index];}
            keys[path][name]=(data,kind);
        }
        public void DeleteValue(string path,string name) {if(FailWrite) throw new IOException("fixture delete failed");Writes++;if(keys.TryGetValue(path,out var values)) values.Remove(name);}
        public void DeleteKeyIfEmpty(string path) {if(keys.TryGetValue(path,out var values) && values.Count==0 && !keys.Keys.Any(k=>k.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase))) keys.Remove(path);}
        public void Notify() { }
    }
}
