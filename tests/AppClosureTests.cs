using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static int NativeFixture(string[] args)
        {
            if(args[1]=="tree") {
                using Process child=Process.Start(FixtureStart("sleep"));
                Console.WriteLine("CHILD="+child.Id);Console.Out.Flush();Thread.Sleep(30000);return 0;
            }
            if(args[1]=="sleep") Thread.Sleep(30000);
            Console.WriteLine("Fixture output");Console.Error.WriteLine("Fixture error output");
            return args[1]=="3010" ? 3010 : args[1]=="failed" ? 5 : 0;
        }
        private static ProcessStartInfo FixtureStart(string mode)
        {
            ProcessStartInfo start=new ProcessStartInfo(Environment.ProcessPath) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add("--native-fixture");start.ArgumentList.Add(mode);return start;
        }
        private static NativeObservation RunPowerShellFixture(string fixture)
        {
            string folder=Path.Combine(Path.GetTempPath(),"OnlyTweak-provision-test-"+Guid.NewGuid().ToString("N"));SecureFiles.EnsureDirectory(folder);
            string path=Path.Combine(folder,"fixture.ps1");
            try {
                File.WriteAllText(path,fixture,new UTF8Encoding(true));
                ProcessStartInfo start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")) {
                    UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
                };
                foreach(string arg in new[]{"-NoProfile","-NonInteractive","-File",path}) start.ArgumentList.Add(arg);
                return ProcessService.RunProcess(start,Ignore,true,default,30);
            } finally {if(File.Exists(path))File.Delete(path);Directory.Delete(folder,false);}
        }
        private static void AppClosureTests()
        {
            Test("predisposizione: preflight bloccante, WhatIf e ciclo Apply-Undo-Apply con storico",()=> {
                string fixture=File.ReadAllText(Path.Combine("tests","fixtures","Provisioning.fixture.ps1"))
                    .Replace("__PROVISION_SOURCE_BASE64__",Convert.ToBase64String(Encoding.UTF8.GetBytes(EmbeddedScripts.Read("Provision-Remoting.ps1"))));
                NativeObservation result=RunPowerShellFixture(fixture);
                Assert(result.ExitCode==0 && result.Output.Contains("PROVISION_FIXTURE_PASS"),"Fixture predisposizione fallita: "+result.Output);
            });
            Test("processi locali: drenaggio, timeout e annullamento solo delle diagnostiche innocue",()=> {
                AppServices catalog=TestApp();
                foreach(ActionSpec diagnostic in new[]{catalog.Catalog.Network,catalog.Catalog.Maintenance,catalog.Catalog.Spool}.SelectMany(menu=>menu.AllActions()).Where(a=>a.IsDiagnostic))
                    Assert(diagnostic.ExecuteResult!=null,"Diagnostica rimasta sul delegate senza token: "+diagnostic.Id);
                foreach(string scenario in new[]{"fast","timeout","cancel"}) {
                    List<string> chunks=new List<string>();void Log(string text){lock(chunks) chunks.Add(text);}
                    using CancellationTokenSource cancellation=new CancellationTokenSource();
                    if(scenario=="cancel") cancellation.CancelAfter(800);
                    bool interrupted=false;
                    try {
                        NativeObservation result=ProcessService.RunProcess(FixtureStart(scenario=="fast" ? "fast" : "tree"),Log,true,cancellation.Token,scenario=="timeout" ? 1 : 20);
                        Assert(scenario=="fast" && result.ExitCode==0 && result.Output.Contains("Fixture error output") && result.Output.Contains("Fixture output"),"Output non drenato.");
                    } catch(OperationCanceledException) {Assert(scenario=="cancel","Annullamento inatteso.");interrupted=true;}
                    catch(TimeoutException) {Assert(scenario=="timeout","Timeout inatteso.");interrupted=true;}
                    if(scenario!="fast") {
                        Assert(interrupted,"Diagnostica non interrotta.");
                        string output=String.Join("",chunks);
                        string line=output.Split('\n').FirstOrDefault(l=>l.StartsWith("CHILD="));
                        Assert(line!=null,"Fixture figlio non avviata.");
                        int pid=Int32.Parse(line.Substring(6).Trim());
                        bool alive=false;try{using Process child=Process.GetProcessById(pid);alive=!child.HasExited;}catch(ArgumentException){}
                        Assert(!alive,"Figlio diagnostico residuo.");
                    }
                }
                using CancellationTokenSource cancelled=new CancellationTokenSource();cancelled.Cancel();
                NativeObservation repair=ProcessService.RunProcess(FixtureStart("3010"),Ignore,false,cancelled.Token,1);
                Assert(repair.ExitCode==3010,"Il token ha interrotto una fixture classificata come intervento.");
                Assert(ProcessService.RunProcess(FixtureStart("failed"),Ignore,true,default,5).ExitCode==5,"Codice nativo di errore perso.");
            });
            Test("interventi locali: esiti, rilettura esplicita, purge parziale e audit fallito",()=> {
                foreach(string id in new[]{"sfc.repair","network.winsock","printer.purge"}) {
                    FakeAudit audit=new FakeAudit();LocalOperationExecutor executor=new LocalOperationExecutor(audit);int reads=0,writes=0;
                    ActionSpec action=new ActionSpec(id,"Fixture","Fixture","Fixture",log=>writes++) {
                        Observe=(log,token)=>{reads++;return new LocalObservation(true,id=="printer.purge" ? false : null,new {Fixture=true});},
                        ExecuteResult=(log,token)=>{writes++;return new StepResult {State=StepState.Success,Data=JsonSerializer.SerializeToElement(new {NativeExitCode=3010,RestartRequired=true})};}
                    };
                    StepResult uncertain=executor.Execute(action,Ignore,default);
                    Assert(uncertain.State==StepState.Unverified && reads==2 && writes==1 && audit.Entries.SequenceEqual(new[]{"requested","finished"}),"Effetto non accertato dichiarato positivo.");
                    Throws(()=>executor.Authorize(action),"Rileggi stato");
                    StepResult observed=executor.Read(action,Ignore,default);executor.Authorize(action);
                    Assert(observed.State==StepState.Success && reads==3,"Rilettura osservativa non riconosciuta.");
                    action.Observe=(log,token)=>new LocalObservation(false,null,null);
                    StepResult blocked=executor.Execute(action,Ignore,default);
                    Assert(blocked.State==StepState.Unverified && writes==1,"Intervento avviato senza lettura preventiva.");
                }
                ActionSpec verified=new ActionSpec("spool.restart","Fixture","Fixture","Fixture",log=>{}) {Observe=(log,token)=>new LocalObservation(true,true,new {Status="Running"})};
                LocalOperationExecutor finalAudit=new LocalOperationExecutor(new FailingFinalAudit());
                StepResult auditFailed=finalAudit.Execute(verified,Ignore,default);
                Assert(auditFailed.State==StepState.Unverified && auditFailed.ErrorCode=="FinalAuditUnavailable","Audit finale fallito convertito in successo.");
                Assert(!auditFailed.Data.Value.GetProperty("EffectVerified").GetBoolean() && !auditFailed.Data.Value.GetProperty("AuditComplete").GetBoolean(),"Metadati ottimistici dopo errore audit.");
                Throws(()=>finalAudit.Authorize(verified),"Rileggi stato");
                bool invoked=false;
                verified.Execute=log=>invoked=true;
                StepResult prevented=new LocalOperationExecutor(new FakeAudit {Fail=true}).Execute(verified,Ignore,default);
                Assert(!invoked && prevented.State==StepState.Unverified,"Audit preventivo aggirato.");
            });
            GpoClosureTests();
        }
        private sealed class FailingFinalAudit : IOperationAudit
        {
            public void Record(OperationRequest request,string identity,string phase,StepResult result=null) {if(phase=="finished")throw new IOException("Fixture audit");}
        }
        private static void GpoClosureTests()
        {
            foreach(bool jea in new[]{false,true}) Test("GPO: codice nativo e RSOP/versioni indipendenti "+(jea ? "JEA" : "WinRM"),()=> {
                foreach(string scenario in new[]{"valid","failed","missing","empty","rsop","versions","rsop-empty"}) {
                    string mocks="""
                        function New-PSSessionOption {}
                        function Invoke-Command {param($ScriptBlock,$ArgumentList) & $ScriptBlock @ArgumentList}
                        function Get-ADRootDSE {[pscustomobject]@{defaultNamingContext='DC=corp,DC=example'}}
                        function Get-CimInstance {[CmdletBinding()]param([Parameter(Position=0)]$ClassName,$Namespace)
                            if($ClassName -eq 'RSOP_GPO') {if($global:scenario -eq 'rsop'){throw 'Fixture RSOP'};if($global:scenario -ne 'rsop-empty'){[pscustomobject]@{name='Fixture';guidName='{256da506-400f-4edf-b145-b7c353084231}';version=1;enabled=$true;accessDenied=$false}}}
                            else {[pscustomobject]@{Domain='corp.example'}}
                        }
                        function Get-GPO {[CmdletBinding()]param($Guid,$Domain,$Server) if($global:scenario -eq 'versions'){throw 'Fixture versions'};[pscustomobject]@{DisplayName='Fixture';Id=$Guid}}
                        function Invoke-FixtureGpo {$global:LASTEXITCODE=if($global:scenario -eq 'missing'){$null}elseif($global:scenario -eq 'failed'){5}else{0};if($global:scenario -ne 'empty'){'Fixture gpresult output'}}
                        """;
                    mocks="$global:scenario='"+scenario+"'\n"+mocks+"\n";
                    string ReplaceNative(string text)=>text.Replace("& \"$env:windir\\System32\\gpresult.exe\"","Invoke-FixtureGpo");
                    string prefix=mocks;
                    if(jea) prefix+="$m=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+Convert.ToBase64String(Encoding.UTF8.GetBytes(mocks+ReplaceNative(EmbeddedScripts.Read("OnlyTweakEndpoint.psm1")) ))+"'));New-Module -ScriptBlock ([scriptblock]::Create($m)) | Import-Module -Force\n";
                    OperationRequest request=Request("endpoint.gpo");request.Profile.JeaEndpoint=jea ? "OnlyTweak" : "";
                    StepResult result=new PowerShellBridge(prefix+ReplaceNative(WithoutModuleImport(EmbeddedScripts.Read("Operations.ps1"))))
                        .ExecuteAsync(request,EnterpriseCatalog.Get(request.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                    bool complete=scenario is "valid" or "rsop-empty";
                    Assert(result.State==(complete ? StepState.Success : StepState.Inconclusive),"GPO errata: "+scenario+" "+Wire.Write(result));
                    JsonElement data=result.Data.Value,endpoint=data.GetProperty("Endpoint");
                    if(scenario=="failed") Assert(endpoint.GetProperty("NativeExitCode").GetInt32()==5 && data.GetProperty("Versions").GetArrayLength()==1,"Codice o versioni persi.");
                    if(scenario=="rsop") Assert(endpoint.GetProperty("Result").GetString().Contains("Fixture"),"gpresult perso dopo errore RSOP.");
                    if(scenario=="versions") Assert(endpoint.GetProperty("Applied").GetArrayLength()==1,"RSOP perso dopo errore versioni.");
                }
            });
        }
        private static void AppClosureUiTests()
        {
            Test("UI aziendale: sei schede raggiungibili al minimo e su desktop piccolo",()=> {
                foreach(Size size in new[]{new Size(660,540),new Size(800,600)}) {
                    using EnterpriseDialog form=new EnterpriseDialog(false,false);form.MinimumSize=new Size(660,540);form.Size=size;form.Show();Application.DoEvents();
                    TabControl tabs=Descendants(form).OfType<TabControl>().Single();
                    foreach(TabPage page in tabs.TabPages) {
                        tabs.SelectedTab=page;Application.DoEvents();
                        foreach(Control control in Descendants(page).Where(c=>c is Button || c is DataGridView || c is ListBox)) {
                            for(int pass=0;pass<2;pass++) for(Control parent=control.Parent;parent!=null;parent=parent.Parent)
                                if(parent is ScrollableControl scroll && scroll.AutoScroll) {scroll.ScrollControlIntoView(control);Application.DoEvents();}
                            Rectangle visible=control.RectangleToScreen(control.ClientRectangle);
                            for(Control parent=control.Parent;parent!=null;parent=parent.Parent) visible=Rectangle.Intersect(visible,parent.RectangleToScreen(parent.ClientRectangle));
                            Assert(visible.Width>0 && visible.Height>0,"Comando irraggiungibile: "+page.Text+"/"+control.Text+" "+size);
                            if(control is Button) Assert(visible.Width>=control.ClientSize.Width-2 && visible.Height>=control.ClientSize.Height-2,"Pulsante parzialmente nascosto: "+control.Text);
                        }
                    }
                    tabs.SelectedIndex=2;
                    using Bitmap screenshot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(screenshot,new Rectangle(Point.Empty,form.Size));
                    Directory.CreateDirectory("artifacts/app-close-2026-10-25");screenshot.Save("artifacts/app-close-2026-10-25/enterprise-"+size.Width+".png");form.Close();
                }
                using ProfileEditor editor=new ProfileEditor(EnterpriseProfile());editor.MinimumSize=new Size(660,540);editor.Size=new Size(660,540);editor.Show();Application.DoEvents();
                foreach(TextBox field in Descendants(editor).OfType<TextBox>()) {
                    for(Control parent=field.Parent;parent!=null;parent=parent.Parent) if(parent is StackPanel stack) {stack.EnsureVisible(field);Application.DoEvents();}
                    Rectangle visible=field.RectangleToScreen(field.ClientRectangle);
                    for(Control parent=field.Parent;parent!=null;parent=parent.Parent) visible=Rectangle.Intersect(visible,parent.RectangleToScreen(parent.ClientRectangle));
                    Assert(visible.Width>0 && visible.Height>0,"Campo editor irraggiungibile.");
                }
                Assert(Descendants(editor).OfType<TextBox>().Count()>5,"Editor incompleto.");editor.Close();
            });
            Test("UI diagnostica locale: annullamento libera il coordinatore",()=> {
                AppServices app=TestApp();
                ActionSpec action=new ActionSpec("fixture.diagnostic","Fixture","Fixture","Fixture",log=>{}) {IsDiagnostic=true,ExecuteResult=(log,token)=>{
                    ProcessService.RunProcess(FixtureStart("sleep"),log,true,token,10);return new StepResult {State=StepState.Success};
                }};
                using OperationDialog form=new OperationDialog(app,action,true);form.Show();PumpUntil(()=>app.Coordinator.IsBusy);
                Descendants(form).OfType<Button>().Single(b=>b.Text=="Annulla diagnosi").PerformClick();PumpUntil(()=>!app.Coordinator.IsBusy);
                Assert(app.History.Read().Contains("ERRORE"),"Annullamento non registrato.");form.Close();
            });
        }
    }
}
