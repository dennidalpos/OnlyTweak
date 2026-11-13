using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private sealed class FakeAudit : IOperationAudit {
            internal bool Fail;
            internal bool FailFinal;
            internal readonly List<string> Entries=new();
            public void Record(OperationRequest request,string identity,string phase,StepResult result=null) {
                if(Fail || FailFinal && phase=="finished") throw new IOException("fixture audit unavailable");Entries.Add(phase);
            }
        }
        private sealed class FakePreferences : IPreferenceBackend {
            internal readonly Dictionary<string,string> Values=new() {["client"]="1",["menu"]="0",["combo"]="1",["power"]="11111111-1111-1111-1111-111111111111"};
            internal int Writes,FailAt;
            public string Read(string key)=>Values[key];
            public void Write(string key,string value) {Writes++;if(Writes==FailAt) throw new IOException("fixture preference failed");Values[key]=value;}
            public PowerPlan[] Plans()=>new[]{new PowerPlan("11111111-1111-1111-1111-111111111111","Bilanciato fixture"),new PowerPlan("22222222-2222-2222-2222-222222222222","Prestazioni fixture")};
        }
        private sealed class FakeAwake : IAwakeBackend {
            internal readonly List<(int Thread,bool Active,bool Display)> Calls=new();
            internal bool Fail;
            public void Set(bool active,bool display) {lock(Calls) Calls.Add((Environment.CurrentManagedThreadId,active,display));if(Fail && active) throw new IOException("fixture awake failed");}
        }
        private static void ConsumerTests()
        {
            Test("desktop: errore sul secondo valore conserva checkpoint incompleto",()=>WithTweaks((store,service,reopen,folder)=> {
                var definition=service.Definitions.Single(d=>d.Id=="desktop.recycle");store.FailAt=2;
                try {service.Apply(definition.Id,true,Ignore);throw new Exception("Errore ignorato.");} catch(IOException) {}
                Assert(store.Read(definition.Paths[0],definition.Name).ValueExists && !store.Read(definition.Paths[1],definition.Name).ValueExists,"Fixture non parziale.");
                store.FailAt=0;int writes=store.Writes;Throws(()=>reopen().Apply(definition.Id,null,Ignore),"incompleto");Assert(store.Writes==writes,"Checkpoint parziale usato.");
            }));
            Test("operazioni locali: prelettura e audit mancanti non certificano l'effetto",()=> {
                bool executed=false;var action=new ActionSpec("fixture.local","Fixture","Fixture","Fixture",_=>executed=true) {
                    Observe=(_,_)=>new LocalObservation(false,null,null)
                };
                var audit=new FakeAudit();var result=new LocalOperationExecutor(audit).Execute(action,Ignore,CancellationToken.None);
                Assert(!executed && result.State==StepState.Unverified && audit.Entries.SequenceEqual(new[]{"requested","finished"}),"Prelettura fallita ha consentito la modifica.");
                action.Observe=(_,_)=>new LocalObservation(true,true,new {Known=true});audit=new FakeAudit {FailFinal=true};
                result=new LocalOperationExecutor(audit).Execute(action,Ignore,CancellationToken.None);
                Assert(executed && result.State==StepState.Unverified && result.ErrorCode=="FinalAuditUnavailable","Audit finale mancante certificato.");
            });
            Test("protocollo locale: frame fuori limite e risposte PowerShell incoerenti rifiutati",()=> {
                foreach(int size in new[]{-1,PipeChannel.MaximumBytes+1}) {
                    using var stream=new MemoryStream(BitConverter.GetBytes(size));using var channel=new PipeChannel(stream);
                    bool rejected=false;try {channel.Receive().GetAwaiter().GetResult();} catch(IOException) {rejected=true;}
                    Assert(rejected,"Frame non valido accettato.");
                }
                var request=new OperationRequest {ActionId="winrm.inspect",Target=Environment.MachineName};
                var result=new StepResult {State=StepState.Success,Summary="fixture",Data=System.Text.Json.JsonSerializer.SerializeToElement(new {
                    ReadComplete=true,Configured=false,Action="winrm.inspect",Computer="foreign-computer"})};
                Throws(()=>OperationResultContract.Read(Wire.Write(result),request),"insufficienti");
                Throws(()=>OperationResultContract.Read("{}",request),"incompleto");
            });
            Test("desktop: checkpoint composto, originali distinti e conflitto sul secondo valore",()=>WithTweaks((store,service,reopen,folder)=> {
                var definition=service.Definitions.Single(d=>d.Id=="desktop.thispc");
                store.Set(definition.Paths[0],definition.Name,7,RegistryValueKind.DWord);
                store.Set(definition.Paths[1],definition.Name,"custom",RegistryValueKind.String);
                service.Apply(definition.Id,true,Ignore);
                Assert(definition.Paths.All(p=>(int)store.Read(p,definition.Name).Data==0),"Modifica composta incompleta.");
                reopen().Apply(definition.Id,null,Ignore);
                Assert((int)store.Read(definition.Paths[0],definition.Name).Data==7 && (string)store.Read(definition.Paths[1],definition.Name).Data=="custom","Originali composti persi.");
                service.Apply(definition.Id,false,Ignore);store.Set(definition.Paths[1],definition.Name,8,RegistryValueKind.DWord);
                int writes=store.Writes;Throws(()=>reopen().Apply(definition.Id,null,Ignore),"esterna");Assert(store.Writes==writes,"Ripristino parziale dopo conflitto.");
            }));
            Test("preferenze: animazioni composte, piano esistente e ripristino persistente",()=> {
                string root=FixtureDirectory();var backend=new FakePreferences();
                UserPreferences Create()=>new(backend,root,()=>{},"fixture","sid");
                try {
                    Create().Apply("animations","reduce",Ignore);Assert(new[]{"client","menu","combo"}.All(k=>backend.Values[k]=="0"),"Animazioni non ridotte.");
                    Create().Apply("animations",null,Ignore);Assert(backend.Values["client"]=="1" && backend.Values["menu"]=="0" && backend.Values["combo"]=="1","Stato originale perso.");
                    Create().Apply("power",backend.Plans()[1].Id,Ignore);Create().Apply("power",null,Ignore);Assert(backend.Read("power")==backend.Plans()[0].Id,"Piano originale perso.");
                    Throws(()=>Create().Apply("power","unknown",Ignore),"consentita");
                    Create().Apply("animations","reduce",Ignore);backend.Values["client"]="1";int writes=backend.Writes;
                    Throws(()=>Create().Apply("animations",null,Ignore),"esterna");Assert(backend.Writes==writes,"Preferenza esterna sovrascritta.");
                } finally {DeleteFixtureDirectory(root);}
            });
            Test("preferenze: errore parziale e checkpoint alterato bloccano nuove scritture",()=> {
                string root=FixtureDirectory();var backend=new FakePreferences {FailAt=2};
                UserPreferences Create()=>new(backend,root,()=>{},"fixture","sid");
                try {
                    try {Create().Apply("animations","reduce",Ignore);throw new Exception("Errore ignorato.");} catch(IOException) {}
                    int writes=backend.Writes;Throws(()=>Create().Apply("animations",null,Ignore),"incompleto");Assert(writes==backend.Writes,"Checkpoint incompleto usato.");
                    File.WriteAllText(Path.Combine(root,"animations.json"),"{\"Content\":\"{}\",\"Sha256\":\"invalid\"}");
                    Throws(()=>Create().Apply("animations",null,Ignore),"alterato");Assert(writes==backend.Writes,"Checkpoint alterato usato.");
                } finally {DeleteFixtureDirectory(root);}
            });
            Test("awake: acquisizione/rilascio sullo stesso thread, scadenza e chiusura",()=> {
                var backend=new FakeAwake();using var service=new AwakeService(backend);
                service.Start(TimeSpan.FromMilliseconds(100),true);Assert(service.Active,"Awake non attivo.");
                Assert(SpinWait.SpinUntil(()=>!service.Active,3000),"Scadenza ignorata.");service.Stop();
                Assert(backend.Calls.Count==2 && backend.Calls[0].Thread==backend.Calls[1].Thread && backend.Calls[0].Display && !backend.Calls[1].Active,"Rilascio su thread diverso o display errato.");
                service.Start(TimeSpan.FromMinutes(15),false);service.Dispose();Assert(!service.Active && !backend.Calls.Last().Active,"Awake sopravvive alla chiusura.");
                var failed=new FakeAwake {Fail=true};using var failure=new AwakeService(failed);Throws(()=>failure.Start(TimeSpan.FromMinutes(15),false),"failed");Assert(!failure.Active,"Errore convertito in attività.");
            });
            Test("gaming: livelli cumulativi, azioni guidate incomplete e destinazioni chiuse",()=> {
                Assert(ConsumerCatalog.Gaming.Select(i=>i.Id).Distinct().Count()==ConsumerCatalog.Gaming.Length,"ID duplicati.");
                for(int level=1;level<=3;level++) {
                    var items=ConsumerCatalog.Gaming.Where(i=>i.Level<=level).ToArray();Assert(items.Length>0 && items.All(i=>i.State.Contains("Da completare")),"Guida certificata automaticamente.");
                    Assert(items.All(i=>ProcessService.AllowedTargets.Contains(i.Target)),"Destinazione non ammessa.");
                }
                Assert(ConsumerCatalog.Gaming.Single(i=>i.Id=="game.windowed").MinimumBuild==22000,"Opzione Windows 11 esposta su 10.");
                foreach(string invalid in new[]{"winrm.enable.0","winrm.enable.1;whoami","winrm.enable.-1","winrm.connect","ad.search"})
                    Throws(()=>LocalRemoting.InterfaceIndex(invalid),"consentita");
                Throws(()=>LocalPrivilege.Find(TestApp(),"ad.unlock"),"consentita");
                Throws(()=>WorkerHost.TryRun(new[]{"--worker","OnlyTweak-fixture","123","network"}),"non valido");
            });
            Test("WinRM: procedure reali con fixture rete, proprietà e fallimenti isolati",()=> {
                string output=RunInspection("powershell","-NoProfile","-NonInteractive","-File",Path.GetFullPath("tests/fixtures/LocalRemoting.fixture.ps1"));
                Assert(output.Contains("WINRM_FIXTURES_PASSED"),output);
            });
        }
        private static string FixtureDirectory() {string path=Path.Combine(Path.GetTempPath(),"OnlyTweak-consumer-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        private static void DeleteFixtureDirectory(string path) {foreach(string file in Directory.GetFiles(path)) File.Delete(file);Directory.Delete(path,false);}
        private static void ConsumerUiTests() {
            Test("UI privata: home senza dominio e livelli gaming selezionabili",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var titles=Descendants(host).OfType<DarkButton>().Where(b=>b.GlyphColor==Theme.Accent && b.Glyph!=null).Select(b=>b.Text).ToArray();
                Assert(new[]{"Personalizzazione","Gaming","Rete","Manutenzione","Stampanti","Strumenti avanzati"}.All(t=>titles.Contains(t)),"Home privata incompleta.");
                host.Navigation.Push(new GamingPage(app));var levels=Descendants(host.Navigation.Current).OfType<ComboBox>().Single(b=>b.AccessibleName=="Livello gaming");
                levels.SelectedIndex=2;Assert(Descendants(host.Navigation.Current).OfType<Button>().Any(b=>b.Text=="Ripristina precedente"),"Opzioni automatiche assenti.");
                Assert(Descendants(host.Navigation.Current).OfType<Label>().Any(l=>l.Text.Contains("Da completare")),"Opzioni guidate senza stato.");
                host.ClientSize=new System.Drawing.Size(660,540);Application.DoEvents();
                var panel=Descendants(host.Navigation.Current).OfType<StackPanel>().Single();
                var apply=Descendants(host.Navigation.Current).OfType<Button>().Last(b=>b.Text=="Ripristina precedente");
                panel.EnsureVisible(apply);Application.DoEvents();
                Assert(panel.ScrollMaximum>0 && panel.RectangleToScreen(panel.ClientRectangle).IntersectsWith(apply.RectangleToScreen(apply.ClientRectangle)),"Azione gaming non raggiungibile alla dimensione minima.");
                Await(host.Navigation.BackAsync());Assert(host.Navigation.Depth==1,"Navigazione privata incoerente.");ClosePage(host);
            });
        }
    }
}
