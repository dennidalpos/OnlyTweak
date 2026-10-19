using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static SecurityDefinition Definition(SecurityService service,string id) { return service.Definitions.Single(d=>d.Id==id); }
        private static void SecurityTests()
        {
            Test("protezioni: runtime distinto da configurazione e Audit",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                backend.Store.Set(SecurityService.Hvci,"Enabled",1,RegistryValueKind.DWord);
                SecurityDefinition hvci=Definition(service,"hvci"),pua=Definition(service,"pua");
                backend.Environment.DeviceGuardKnown=false;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Unknown,"Registro scambiato per runtime.");
                backend.Environment.DeviceGuardKnown=true; backend.Environment.HvciRunning=false;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Inactive,"Runtime inattivo ignorato.");
                backend.Environment.HvciRunning=true;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Active,"Runtime attivo ignorato.");
                backend.Environment.Pua=2;
                Assert(service.Read(pua,backend.Environment).Protection==ProtectionState.Audit,"Audit scambiato per blocco.");
                backend.Environment.DefenderKnown=false;
                Assert(service.Read(pua,backend.Environment).Protection==ProtectionState.Unknown,"Errore scambiato per protezione attiva.");
                Assert(service.Read(Definition(service,"smartscreen"),backend.Environment).Protection==ProtectionState.Unknown,"Criterio scambiato per effetto.");
            });
            Test("protezioni: API e backend esclusivamente di lettura",delegate {
                Assert(typeof(ISecurityBackend).GetMethods().All(m=>m.Name=="get_Registry" || m.Name=="Probe"),"API di modifica residua.");
                Assert(typeof(MachineRegistryStore).GetInterfaces().SequenceEqual(new[] { typeof(IRegistryReader) }),"Registro macchina scrivibile.");
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                foreach (SecurityDefinition d in service.Definitions) { ProcessService.ValidateTarget(d.Page); service.Read(d,backend.Probe()); }
                Assert(backend.ReadCount>0 && backend.Store.WriteCount==0,"Letture con effetti collaterali.");
                foreach (string target in new[] { "https://account.microsoft.com/","https://support.microsoft.com/it-it/accounts-billing/manage/how-to-close-your-microsoft-account","ms-settings:yourinfo & calc.exe","shell:::arbitrary" })
                    Throws(delegate { ProcessService.ValidateTarget(target); },"non consentita");
            });
            Test("protezioni: fresh install senza valori resta sconosciuta",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                foreach (SecurityDefinition d in service.Definitions.Where(d=>d.Id=="smartscreen" || d.Id=="store" || d.Id=="sac")) {
                    SecurityStatus state=service.Read(d,backend.Probe());
                    Assert(state.Protection==ProtectionState.Unknown && state.Effective.Contains("non verificabile"),"Predefinito inventato: "+d.Id);
                }
                backend.Environment.DefenderActive=false;
                Assert(service.Read(Definition(service,"pua"),backend.Probe()).Protection==ProtectionState.Inactive,"Defender inattivo ignorato.");
            });
            Test("protezioni: campi Defender mancanti non diventano stato inattivo",delegate {
                foreach (string output in new[] { "Pua=1", "Active=1", "Pua=9\nActive=1", "Pua=1\nActive=Unknown" })
                    Assert(!WindowsSecurityBackend.ParseProbe(output,new SecurityEnvironment()).DefenderKnown,"Lettura parziale considerata completa.");
                SecurityEnvironment parsed=WindowsSecurityBackend.ParseProbe("DG=1\nHR=0\nKR=0\nPua=2\nActive=0",new SecurityEnvironment());
                Assert(parsed.DefenderKnown && !parsed.DefenderActive && parsed.Pua==2 && parsed.DeviceGuardKnown,"Output valido perso.");
            });
            Test("protezioni: blocklist configurata distinta da HVCI",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Store.Set(SecurityService.CiConfig,"VulnerableDriverBlocklistEnable",0,RegistryValueKind.DWord);
                SecurityService service=new SecurityService(backend);
                Assert(service.Read(Definition(service,"drivers"),backend.Probe()).Effective.Contains("imposta"),"Vincolo HVCI omesso.");
                backend.Environment.HvciRunning=false; backend.Environment.SMode=false;
                Assert(service.Read(Definition(service,"drivers"),backend.Probe()).Effective.Contains("verifica in Windows"),"Valore locale presentato come runtime.");
            });
            Test("launcher: usa il backend interattivo per destinazioni approvate",delegate {
                // I gestori reali dipendono dal sistema (per esempio windowsdefender: manca su Windows Server):
                // le destinazioni senza gestore devono essere rifiutate senza raggiungere il backend.
                List<string> targets=new List<string>(); ProcessService process=new ProcessService(targets.Add);
                List<string> expected=new List<string>();
                foreach (string target in new[] { "ms-settings:yourinfo",ProcessService.ClassicPrinters,"windowsdefender://smartapp/" })
                {
                    if (process.TargetUnavailable(target)==null) { process.LaunchTarget(target,Ignore); expected.Add(target); }
                    else Throws(delegate { process.LaunchTarget(target,Ignore); },"non disponibile");
                }
                Assert(targets.SequenceEqual(expected),"Contesto interattivo aggirato.");
                Throws(delegate { process.LaunchTarget("https://evil.test/",Ignore); },"non consentita");
                Assert(targets.Count==expected.Count,"Destinazione vietata inoltrata.");
            });
        }
        private static void WheelTests()
        {
            Test("rotella: accumulo delta piccoli, pagina e impostazione zero",delegate {
                WheelSteps steps=new WheelSteps();
                Assert(steps.Pixels(30,false,10,100,3)==0 && steps.Pixels(30,false,10,100,3)==0 && steps.Pixels(60,false,10,100,3)==30,"Delta piccoli persi.");
                Assert(steps.Pixels(-120,false,10,100,-1)==-100,"Scorrimento pagina errato.");
                Assert(steps.Pixels(120,false,10,100,0)==0,"Impostazione zero ignorata.");
                Assert(steps.Pixels(60,true,10,100,3)==0 && steps.Pixels(60,false,10,100,3)==0,"Assi non indipendenti.");
            });
            Test("rotella: pulsanti, etichette, pannelli annidati e scrollbar una sola volta",delegate {
                using (Form host=new Form { Size=new Size(540,260),ShowInTaskbar=false })
                {
                    StackPanel stack=new StackPanel(); host.Controls.Add(stack);
                    Panel nested=new Panel { Height=80 }; Button button=new Button { Text="Test" }; Label label=new Label { Top=40,Text="Test" };
                    nested.Controls.Add(button); nested.Controls.Add(label); stack.Controls.Add(nested);
                    for (int i=0;i<12;i++) stack.Controls.Add(Theme.Card("Azione "+i,"Dettaglio",null));
                    host.Show(); Application.DoEvents();
                    Assert(WheelRouter.Route(button,host,-120,false,false) && stack.ScrollOffset>0,"Pulsante non inoltra.");
                    int first=stack.ScrollOffset; WheelRouter.Route(label,host,-120,false,false);
                    Assert(stack.ScrollOffset==first*2,"Evento consumato due volte o etichetta ignorata.");
                    stack.ScrollTo(0); WheelRouter.Route(button,host,120,false,false); Assert(stack.ScrollOffset==0,"Limite iniziale superato.");
                    DarkScrollBar bar=Descendants(host).OfType<DarkScrollBar>().First(); WheelRouter.Route(bar,host,-120,false,false);
                    Assert(stack.ScrollOffset==first,"Barra applica velocità o doppio scorrimento diverso.");
                    stack.ScrollTo(Int32.MaxValue); int maximum=stack.ScrollMaximum; WheelRouter.Route(label,host,-120,false,false);
                    Assert(stack.ScrollOffset==maximum,"Limite finale superato."); host.Close();
                }
            });
            Test("rotella: instradamento messaggio e isolamento del modale",delegate {
                using (Form owner=new Form { ShowInTaskbar=false,Size=new Size(500,250) })
                using (Form modal=new Form { ShowInTaskbar=false,Size=new Size(500,250) })
                {
                    StackPanel stack=new StackPanel(); owner.Controls.Add(stack);
                    for (int i=0;i<10;i++) stack.Controls.Add(Theme.Card("Azione","Test",null));
                    owner.Show(); modal.Show(owner); Application.DoEvents();
                    Assert(!WheelRouter.Route(stack.Controls[0],modal,-120,false,false) && stack.ScrollOffset==0,"Owner scorre sotto modale.");
                    owner.Enabled=false; Assert(!WheelRouter.Route(stack.Controls[0],owner,-120,false,false),"Finestra disabilitata riceve rotella."); owner.Enabled=true;
                    modal.Close(); owner.Activate(); stack.Controls[0].Focus(); Application.DoEvents();
                    Point point=stack.Controls[0].PointToScreen(new Point(10,10));
                    Message message=Message.Create(stack.Controls[0].Handle,0x020a,new IntPtr(-120*65536),new IntPtr(((point.Y&0xffff)<<16)|(point.X&0xffff)));
                    Assert(WheelRouter.RouteMessage(ref message,stack.Controls[0],owner,false) && stack.ScrollOffset>0,"WM_MOUSEWHEEL non gestito."); owner.Close();
                }
            });
            Test("rotella: output verticale, Shift/orizzontale e lista stampanti",delegate {
                using (Form host=new Form { Size=new Size(500,260),ShowInTaskbar=false })
                {
                    DarkOutput output=Theme.Output(); host.Controls.Add(output); host.Show();
                    output.Text=String.Join("\n",Enumerable.Range(0,180).Select(i=>"Riga "+i+new string('x',160)).ToArray()); Application.DoEvents();
                    RichTextBox editor=Descendants(output).OfType<RichTextBox>().Single();
                    WheelRouter.Route(editor,host,-120,false,false); WheelRouter.Route(editor,host,-120,true,false);
                    Point position=new Point(); ScrollPosition(editor.Handle,0x04dd,IntPtr.Zero,ref position);
                    Assert(position.Y>0 && position.X>0,"Output non scorre sui due assi.");
                    WheelRouter.Route(editor,host,120,true,true); Point next=new Point(); ScrollPosition(editor.Handle,0x04dd,IntPtr.Zero,ref next);
                    Assert(next.X>position.X,"Verso WM_MOUSEHWHEEL errato."); host.Close();
                }
                using (Form host=new Form { Size=new Size(500,220),ShowInTaskbar=false })
                {
                    DarkPrinterList list=new DarkPrinterList { Dock=DockStyle.Fill }; host.Controls.Add(list);
                    for (int i=0;i<30;i++) list.Items.Add("Stampante "+i);
                    host.Show(); Application.DoEvents(); DarkScrollBar bar=Descendants(list).OfType<DarkScrollBar>().Single();
                    WheelRouter.Route(list,host,-120,false,false); Assert(bar.Value>0,"Lista stampanti non scorre.");
                    list.Focus(); host.Close();
                }
            });
        }
        private static void SecurityUiTests()
        {
            Test("UI protezioni: colori aggiornati fra runtime, dubbio ed errore",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=TestApp(backend);
                using (SecurityDialog form=new SecurityDialog(app,false))
                {
                    form.ShowInTaskbar=false; form.Show(); Application.DoEvents();
                    SecurityPane pane=Descendants(form).OfType<SecurityPane>().Single(p=>p.Definition.Id=="hvci");
                    pane.ShowState(new SecurityStatus { Configured="attivata",Effective="HVCI in esecuzione",Protection=ProtectionState.Active });
                    Label state=Descendants(pane).OfType<Label>().Single(l=>l.Text.StartsWith("Configurazione:"));
                    Assert(state.ForeColor==Theme.Accent,"Runtime attivo senza accento.");
                    pane.SetBusy(); Assert(state.ForeColor==Theme.Muted,"Lettura conserva il colore precedente.");
                    pane.ShowState(new SecurityStatus { Configured="attivata",Effective="sconosciuto" });
                    Assert(state.ForeColor==Theme.Muted,"Stato incerto verde.");
                    pane.ShowState(new SecurityStatus { Protection=ProtectionState.Inactive });
                    Assert(state.ForeColor==Theme.Warning,"Protezione inattiva senza avviso.");
                    pane.ShowState(new SecurityStatus { Protection=ProtectionState.Audit });
                    Assert(state.ForeColor==Theme.Muted,"Audit presentato come blocco.");
                    backend.FailRead=true;
                    Descendants(form).OfType<Button>().Single(b=>b.Text=="Aggiorna stato").PerformClick();
                    PumpUntil(delegate { return !app.Coordinator.IsBusy; });
                    Assert(state.ForeColor==Theme.Warning && state.Text.Contains("lettura non riuscita"),"Errore asincrono della singola scheda senza avviso.");
                    pane.ShowFailure("Errore simulato");
                    Assert(state.ForeColor==Theme.Warning && state.Text.Contains("sconosciuto"),"Errore conserva il colore precedente.");
                    form.Close();
                }
            });
            Test("UI protezioni: solo pagine Windows, nessun comando di modifica",delegate {
                foreach (int build in new[] { 17763,19044,19045,22000,22621,26100 }) {
                    FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=TestApp(backend,build);
                    using (SecurityDialog form=new SecurityDialog(app,false)) {
                        form.ShowInTaskbar=false; form.Show(); form.DisplayStates(backend.Environment); Application.DoEvents();
                        SecurityPane[] panes=Descendants(form).OfType<SecurityPane>().ToArray();
                        Assert(panes.Length==(build>=22621 ? 7 : 5),"Filtro pannelli errato: "+build);
                        foreach (SecurityPane pane in panes) {
                            Button button=Descendants(pane).OfType<Button>().Single();
                            Assert(button.Text=="Apri pagina Windows","Comandi di modifica residui.");
                            Label state=Descendants(pane).OfType<Label>().Single(l=>l.Text.StartsWith("Configurazione:"));
                            Assert(state.Visible && state.Bottom<=state.Parent.ClientSize.Height,"Stato tagliato nella scheda.");
                            Assert(button.Visible && button.Parent.Top>=state.Bottom && button.Parent.Bottom<=button.Parent.Parent.ClientSize.Height && button.Bottom<=button.Parent.ClientSize.Height,"Pulsante fuori dalla scheda o sovrapposto allo stato.");
                        }
                        StackPanel stack=Descendants(form).OfType<StackPanel>().Single(); stack.ScrollTo(Int32.MaxValue);
                        Assert(stack.ScrollOffset==stack.ScrollMaximum,"Ultima voce non raggiungibile.");
                        Assert(backend.Store.WriteCount==0,"La UI ha scritto nel registro."); form.Close();
                    }
                }
            });
        }
    }
    internal sealed class FakeSecurityBackend : ISecurityBackend,IRegistryReader
    {
        public readonly FakeRegistry Store=new FakeRegistry();
        public readonly SecurityEnvironment Environment=new SecurityEnvironment {
            DeviceGuardKnown=true,HvciRunning=true,
            KernelRunning=true,DefenderKnown=true,DefenderActive=true,Pua=1 };
        public bool FailRead;
        public int ReadCount;
        public IRegistryReader Registry { get { return this; } }
        public SecurityEnvironment Probe() { return Environment; }
        public RegistryValueState Read(string path,string name) { ReadCount++; if (FailRead) throw new IOException("Errore lettura simulata."); return Store.Read(path,name); }
    }
}
