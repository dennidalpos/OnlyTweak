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
            Test("tweak: catalogo privato, tipi originali e COM personalizzata",()=> WithTweaks((store,service,reopen,folder)=> {
                Assert(service.Definitions.Length==11,"Catalogo Windows 11 incompleto.");
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
                    var request=new OperationRequest {ActionId=id,Target="fixture.example.com",Parameters=new() {["port"]="443",["recordType"]="A"}};
                    var definition=new ActionDefinition(id,"Fixture",ActionRisk.ReadOnly,false,timeout);
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
            AreaThemeTests();

            Test("UI: F5 rilegge lo stato ed Esc rispetta la conferma",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var page=new TweakPage(app);
                int refreshes=0;
                page.RefreshButton.Click+=delegate {refreshes++;};
                host.Navigation.Push(page);
                Assert(PreprocessKey(page.ActiveControl,Keys.F5) && refreshes==1,"F5 non esegue la rilettura della pagina attiva.");

                foreach(Keys key in new[]{Keys.Escape,Keys.Enter})
                {
                    using var confirm=new ConfirmDialog("Conferma fixture","Nessuna operazione reale.");
                    var pending=host.Navigation.PresentAsync(confirm);
                    Assert(confirm.BackButton.AccessibilityObject.Name=="Annulla","Nome accessibile diverso dal comando di annullamento.");
                    PreprocessKey(confirm.BackButton,Keys.F5);
                    Assert(refreshes==1 && !pending.IsCompleted,"F5 ha agito dietro la conferma.");
                    Assert(PreprocessKey(confirm.BackButton,key),"Il tasto non ha raggiunto la conferma.");
                    Await(pending);
                    Assert(pending.Result!=DialogResult.Yes && ReferenceEquals(host.Navigation.Current,page),"La conferma non è stata annullata.");
                }
                Assert(PreprocessKey(page.ActiveControl,Keys.Escape) && host.Navigation.Depth==1,"Esc non torna alla Home.");

                var gaming=new GamingPage(app);host.Navigation.Push(gaming);
                var levels=Descendants(gaming).OfType<ComboBox>().Single();
                levels.Focus();levels.DroppedDown=true;
                Assert(!PreprocessKey(levels,Keys.Escape) && host.Navigation.Current==gaming,"Esc su un elenco aperto non deve lasciare la pagina.");
                levels.DroppedDown=false;
                Assert(PreprocessKey(levels,Keys.Escape) && host.Navigation.Depth==1,"Esc su un elenco chiuso non torna alla Home.");
                ClosePage(host);
            });

            Test("UI: gruppo radio raggiungibile, nome e stato accessibili",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var page=new TweakPage(app);host.Navigation.Push(page);
                var options=Descendants(page).OfType<DarkRadio>().Take(3).ToArray();
                var first=options[0];var second=options[1];var restore=options[2];
                var group=first.Parent.AccessibilityObject;
                Assert(group.Role==AccessibleRole.Grouping && group.Name=="Questo PC","Gruppo radio senza ruolo o nome.");
                Assert(first.TabStop && !first.Checked && !restore.Enabled,"Gruppo senza selezione irraggiungibile da Tab.");
                Assert(!first.Parent.Controls.OfType<Button>().Single(button=>button.Text=="Applica").Enabled,
                    "Apply must stay disabled until the user chooses an option.");
                Assert(first.AccessibilityObject.Name=="Questo PC: Mostra icona" && first.AccessibilityObject.Role==AccessibleRole.RadioButton,
                    "Opzione radio senza nome o ruolo accessibile.");
                first.Focus();
                Assert(PreprocessKey(first,Keys.Right) && second.Focused && second.Checked && !first.Checked,"Freccia destra non seleziona la seconda opzione.");
                Assert(second.AccessibilityObject.State.HasFlag(AccessibleStates.Checked),"Stato selezionato assente dall'oggetto accessibile.");
                Assert(PreprocessKey(second,Keys.Right) && first.Focused && first.Checked,"Le frecce escono dal gruppo o selezionano l'opzione disabilitata.");
                ClosePage(host);
            });

            Test("UI: Personalization uses current state and requires a checkpoint for Restore",()=> WithTweaks((registry,service,_,folder)=> {
                const string valueName="HideFileExt";
                registry.Set(RegistryTweakService.ExplorerPath,valueName,1,RegistryValueKind.DWord);
                int writes=registry.Writes;
                var app=TestApp(registry:registry,tweakFolder:folder);
                using var host=new MainForm(app);
                ShowPage(host);
                var page=new TweakPage(app);
                host.Navigation.Push(page);

                var row=Descendants(page).OfType<FlowLayoutPanel>().Single(panel=>panel.AccessibleName=="Estensioni dei file");
                var choices=row.Controls.OfType<DarkRadio>().ToArray();
                var show=choices[0];var hide=choices[1];var restore=choices[2];
                var apply=row.Controls.OfType<Button>().Single(button=>button.Text=="Applica");
                Assert(hide.Checked && !show.Checked && !apply.Enabled,"The current registry value should be selected without enabling Apply.");
                Assert(!restore.Enabled,"Restore is available without a checkpoint.");

                hide.Focus();
                Assert(PreprocessKey(hide,Keys.Left) && show.Focused && show.Checked && !hide.Checked && apply.Enabled,
                    "Choosing a different state with the arrow key should enable Apply.");
                Assert(PreprocessKey(show,Keys.Right) && hide.Focused && hide.Checked && !apply.Enabled,
                    "Selecting the current state again should disable Apply.");
                Assert(registry.Writes==writes,"Changing a radio selection wrote to the registry.");

                service.Apply("extensions",true,Ignore);
                Assert(PreprocessKey(hide,Keys.F5) && restore.Enabled,"Refresh did not expose the available checkpoint.");
                Assert(PreprocessKey(hide,Keys.Right) && restore.Focused && restore.Checked && !hide.Checked && apply.Enabled,
                    "Selecting Restore with the arrow key should enable Apply within the same row.");
                ClosePage(host);
            }));

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

            Test("navigazione: Indietro annulla diagnosi e attende rilascio",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var action=new ActionSpec("fixture.cancel","Fixture","Fixture","Fixture",_=>{}) {IsDiagnostic=true,ExecuteResult=(log,token)=> {token.WaitHandle.WaitOne(4000);token.ThrowIfCancellationRequested();return new StepResult {State=StepState.Success};}};
                host.Navigation.Push(new OperationDialog(app,action,true));PumpUntil(()=>app.Coordinator.IsBusy);
                Await(host.Navigation.BackAsync());Assert(!app.Coordinator.IsBusy && host.Navigation.Depth==1,"Navigazione prima del rilascio.");ClosePage(host);
            });
        }
        private static bool PreprocessKey(Control control,Keys key)
        {
            Message message=Message.Create(control.Handle,0x0100,(IntPtr)key,IntPtr.Zero);
            return control.PreProcessMessage(ref message);
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
        internal int FailAt;
        public RegistryValueState Read(string path,string name) {
            var result=new RegistryValueState {KeyExists=keys.ContainsKey(path)};
            if(keys.TryGetValue(path,out var values) && values.TryGetValue(name,out var value)) {result.ValueExists=true;result.Kind=value.Kind;result.Data=value.Value;}
            return result;
        }
        public void Set(string path,string name,object data,RegistryValueKind kind) {
            if(FailWrite || FailAt>0 && Writes+1==FailAt) throw new IOException("fixture write failed");Writes++;
            for(string current=path;current.Length>0;) {if(!keys.ContainsKey(current)) keys.Add(current,new(StringComparer.OrdinalIgnoreCase));int index=current.LastIndexOf('\\');if(index<0) break;current=current[..index];}
            keys[path][name]=(data,kind);
        }
        public void DeleteValue(string path,string name) {if(FailWrite) throw new IOException("fixture delete failed");Writes++;if(keys.TryGetValue(path,out var values)) values.Remove(name);}
        public void DeleteKeyIfEmpty(string path) {if(keys.TryGetValue(path,out var values) && values.Count==0 && !keys.Keys.Any(k=>k.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase))) keys.Remove(path);}
        public void Notify() { }
    }
}
