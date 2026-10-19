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
            Test("protezioni: stato visivo deriva dal runtime, non dal registro",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                backend.Store.Set(SecurityService.Hvci,"Enabled",1,RegistryValueKind.DWord);
                SecurityDefinition hvci=Definition(service,"hvci"),pua=Definition(service,"pua");
                backend.Environment.DeviceGuardKnown=false;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Unknown,"Registro scambiato per runtime.");
                backend.Environment.DeviceGuardKnown=true; backend.Environment.HvciRunning=false;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Inactive,"Runtime inattivo ignorato.");
                backend.Environment.HvciRunning=true;
                Assert(service.Read(hvci,backend.Environment).Protection==ProtectionState.Active,"Runtime attivo non riconosciuto.");
                backend.Environment.Pua=2;
                Assert(service.Read(pua,backend.Environment).Protection==ProtectionState.Audit,"Audit scambiato per blocco.");
                backend.Environment.DefenderKnown=false;
                Assert(service.Read(pua,backend.Environment).Protection==ProtectionState.Unknown,"Lettura fallita scambiata per blocco.");
                Assert(service.Read(Definition(service,"smartscreen"),backend.Environment).Protection==ProtectionState.Unknown,"Criterio scambiato per effetto reale.");
            });
            Test("protezioni: catalogo completo e destinazioni fisse",delegate {
                SecurityService service=new SecurityService(new FakeSecurityBackend());
                Assert(service.Definitions.Length==7,"Protezioni mancanti.");
                foreach (SecurityDefinition d in service.Definitions) ProcessService.ValidateTarget(d.Page);
                Assert(!Definition(service,"sac").Direct && !Definition(service,"drivers").Direct,"Modifiche dirette non previste.");
                Throws(delegate { ProcessService.ValidateTarget("https://account.microsoft.com.evil.test/"); },"non consentita");
                Throws(delegate { ProcessService.ValidateTarget("ms-settings:yourinfo & calc.exe"); },"non consentita");
                Throws(delegate { ProcessService.ValidateTarget("shell:::arbitrary"); },"non consentita");
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
            Test("protezioni: criterio SmartScreen applicato e ripristinato con assenze",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"smartscreen");
                backend.Store.Ensure(SecurityService.SmartPolicy);
                service.Change(d,false,false,Ignore);
                Assert(Object.Equals(backend.Store.Read(SecurityService.SmartPolicy,"EnableSmartScreen").Data,0),"Disattivazione assente.");
                service.Change(d,true,false,Ignore); service.Change(d,false,true,Ignore);
                Assert(!backend.Store.Read(SecurityService.SmartPolicy,"EnableSmartScreen").ValueExists && backend.Store.Read(SecurityService.SmartPolicy,"").KeyExists,"Assenza/chiave iniziale perse.");
            });
            Test("protezioni: ripristino HVCI conserva tipo e valore personalizzato",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"hvci");
                backend.Store.Set(SecurityService.Hvci,"Enabled",new[] { "originale","due" },RegistryValueKind.MultiString);
                service.Change(d,false,false,Ignore); service.Change(d,false,true,Ignore);
                RegistryValueState actual=backend.Store.Read(SecurityService.Hvci,"Enabled");
                Assert(actual.Kind==RegistryValueKind.MultiString && ((string[])actual.Data).SequenceEqual(new[] { "originale","due" }),"Snapshot alterato.");
                Assert(service.Read(d,backend.Probe()).RestartPending,"Riavvio pendente mancante.");
            });
            Test("protezioni: ripristino rimuove solo chiavi create e conserva dati estranei",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"store");
                service.Change(d,false,false,Ignore); backend.Store.Set(SecurityService.InstallPolicy,"Estraneo",42,RegistryValueKind.DWord);
                service.Change(d,false,true,Ignore);
                Assert(backend.Store.Read(SecurityService.InstallPolicy,"Estraneo").ValueExists && !backend.Store.Read(SecurityService.InstallPolicy,"ConfigureAppInstallControl").ValueExists,"Dati estranei rimossi.");
            });
            Test("protezioni: errore seconda scrittura recupera anche la prima",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"store");
                backend.FailWrite=2; List<string> log=new List<string>();
                Throws(delegate { service.Change(d,false,false,log.Add); },"scrittura simulata");
                Assert(!backend.Store.Read(SecurityService.InstallPolicy,"ConfigureAppInstallControlEnabled").ValueExists,"Prima scrittura non recuperata.");
                Assert(!backend.Store.Read(SecurityService.InstallPolicy,"").KeyExists && log.Any(s=>s.Contains("Recuperata")),"Chiave creata non recuperata.");
            });
            Test("protezioni: recupero incompleto segnalato",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"store");
                backend.FailWrite=2; backend.FailDelete=true; List<string> log=new List<string>();
                Throws(delegate { service.Change(d,false,false,log.Add); },"scrittura simulata");
                Assert(log.Any(s=>s.Contains("RECUPERO INCOMPLETO")),"Errore di recupero nascosto.");
                backend.FailDelete=false; service.Change(d,false,true,Ignore);
                Assert(!backend.Store.Read(SecurityService.InstallPolicy,"ConfigureAppInstallControlEnabled").ValueExists,"Impossibile ritentare il ripristino dopo il recupero parziale.");
            });
            Test("protezioni: scrittura ignorata non dichiarata riuscita",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Store.IgnoreSet=true;
                SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"hvci");
                Throws(delegate { service.Change(d,false,false,Ignore); },"non confermata");
            });
            Test("protezioni: dominio, gestione sconosciuta, template e Home bloccano prima delle scritture",delegate {
                foreach (string scenario in new[] { "domain","unknown","template","home" })
                {
                    FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                    if (scenario=="domain") backend.Environment.Managed=true;
                    if (scenario=="unknown") backend.Environment.ManagementKnown=false;
                    if (scenario=="template") backend.Template=false;
                    if (scenario=="home") backend.Environment.PolicyEdition=false;
                    SecurityDefinition d=Definition(service,"smartscreen"); SecurityStatus status=service.Read(d,backend.Probe());
                    Assert(status.DisableUnavailable!=null,"Blocco non rilevato: "+scenario);
                    Throws(delegate { service.Change(d,false,false,Ignore); },status.DisableUnavailable);
                    Assert(backend.WriteCount==0,"Scrittura durante blocco: "+scenario);
                }
            });
            Test("protezioni: criterio preesistente e blocco UEFI rispettati",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                backend.Store.Set(SecurityService.SmartPolicy,"EnableSmartScreen",1,RegistryValueKind.DWord);
                Throws(delegate { service.Change(Definition(service,"smartscreen"),false,false,Ignore); },"criterio esterno");
                backend.Store.Set(SecurityService.Guard,"Locked",1,RegistryValueKind.DWord);
                Throws(delegate { service.Change(Definition(service,"hvci"),false,false,Ignore); },"UEFI");
                Assert(backend.WriteCount==0,"Policy o UEFI aggirati.");
            });
            Test("protezioni: cambiamenti esterni durante la sessione non sovrascritti",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"smartscreen");
                service.Change(d,false,false,Ignore); int writes=backend.WriteCount;
                backend.Store.Set(SecurityService.SmartPolicy,"EnableSmartScreen",1,RegistryValueKind.DWord);
                Throws(delegate { service.Change(d,false,false,Ignore); },"fuori da OnlyTweak");
                Throws(delegate { service.Change(d,false,true,Ignore); },"fuori da OnlyTweak");
                Assert(backend.WriteCount==writes,"Configurazione esterna sovrascritta.");
            });
            Test("protezioni: stato sconosciuto, assenza Defender e modalità S",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend);
                backend.Environment.DeviceGuardKnown=false;
                Throws(delegate { service.Change(Definition(service,"hvci"),false,false,Ignore); },"non verificabile");
                backend.Environment.DefenderKnown=false;
                Throws(delegate { service.Change(Definition(service,"pua"),false,false,Ignore); },"non disponibile");
                backend.Environment.SMode=true;
                Throws(delegate { service.Change(Definition(service,"store"),false,false,Ignore); },"Modalità S");
                Assert(backend.WriteCount==0,"Stato sconosciuto ignorato.");
            });
            Test("protezioni: configurare stack non prova il supporto hardware",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Environment.KernelConfigured=true; backend.Environment.KernelRunning=false;
                SecurityService service=new SecurityService(backend);
                Throws(delegate { service.Change(Definition(service,"kernel"),true,false,Ignore); },"supporto stack");
            });
            Test("protezioni: PUA ripristina Audit e rispetta antimanomissione",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Environment.Pua=2;
                SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"pua");
                service.Change(d,false,false,Ignore); Assert(backend.Environment.Pua==0,"PUA non disattivata.");
                service.Change(d,false,true,Ignore); Assert(backend.Environment.Pua==2,"Audit non ripristinato.");
                backend.Environment.TamperProtected=true;
                Throws(delegate { service.Change(d,false,false,Ignore); },"antimanomissione");
            });
            Test("protezioni: PUA rifiutata da Windows rilevata e recuperata",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Environment.Pua=2; backend.IgnorePua=true;
                SecurityService service=new SecurityService(backend);
                Throws(delegate { service.Change(Definition(service,"pua"),false,false,Ignore); },"non confermata");
                Assert(backend.Environment.Pua==2,"Stato PUA perso.");
            });
            Test("protezioni: HVCI non modifica dipendenze e distingue runtime",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"hvci");
                service.Change(d,false,false,Ignore); SecurityStatus status=service.Read(d,backend.Probe());
                Assert(status.Effective.Contains("in esecuzione") && status.RestartPending,"Configurazione confusa con runtime.");
                Assert(!backend.Store.Read(SecurityService.Guard,"EnableVirtualizationBasedSecurity").ValueExists && !backend.Store.Read(SecurityService.GuardPolicy,"ConfigureKernelShadowStacksLaunch").ValueExists,"Dipendenze modificate.");
            });
            Test("protezioni: stack richiede supporto Windows e HVCI runtime",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Environment.KernelConfigured=false; backend.Environment.KernelRunning=false;
                SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"kernel");
                Throws(delegate { service.Change(d,true,false,Ignore); },"supporto stack");
                backend.Environment.KernelRunning=true; service.Change(d,false,false,Ignore);
                backend.Environment.KernelRunning=false; service.Change(d,true,false,Ignore);
                backend.Environment.HvciRunning=false;
                Throws(delegate { service.Change(d,true,false,Ignore); },"HVCI");
            });
            Test("protezioni: blocklist locale disattiva ma imposta da HVCI",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); backend.Store.Set(SecurityService.CiConfig,"VulnerableDriverBlocklistEnable",0,RegistryValueKind.DWord);
                SecurityService service=new SecurityService(backend); SecurityDefinition d=Definition(service,"drivers");
                Assert(service.Read(d,backend.Probe()).Effective.Contains("imposta"),"Vincolo HVCI omesso.");
                Throws(delegate { service.Change(d,false,false,Ignore); },"pagina Windows");
                Assert(backend.WriteCount==0,"Modifica blocklist non prevista.");
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
            Test("UI protezioni: colori aggiornati fra runtime, dubbio, riavvio ed errore",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=new AppServices(backend);
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
                    pane.ShowState(new SecurityStatus { Protection=ProtectionState.Active,RestartPending=true });
                    Assert(state.ForeColor==Theme.Warning && state.Text.Contains("Riavvio"),"Riavvio pendente presentato come concluso.");
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
            Test("UI protezioni: una conferma, modifica simulata e stato riletto",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=new AppServices(backend); int confirmations=0;
                using (SecurityDialog form=new SecurityDialog(app,false))
                using (System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=20 })
                {
                    form.ShowInTaskbar=false; form.Show(); form.DisplayStates(backend.Environment); Application.DoEvents();
                    timer.Tick+=delegate {
                        ConfirmDialog confirm=Application.OpenForms.OfType<ConfirmDialog>().FirstOrDefault();
                        if (confirm!=null) { confirmations++; Descendants(confirm).OfType<Button>().First(b=>b.Text=="&Conferma").PerformClick(); }
                        OperationDialog operation=Application.OpenForms.OfType<OperationDialog>().FirstOrDefault();
                        if (operation!=null && backend.WriteCount>0 && !app.Coordinator.IsBusy) { timer.Stop(); operation.Close(); }
                    };
                    timer.Start(); SecurityPane pane=Descendants(form).OfType<SecurityPane>().Single(p=>p.Definition.Id=="hvci");
                    Descendants(pane).OfType<Button>().First(b=>b.Text=="Disattiva").PerformClick();
                    PumpUntil(delegate { return !app.Coordinator.IsBusy; });
                    Assert(confirmations==1 && backend.WriteCount==1,"Conferma/esecuzione duplicata.");
                    Assert(Descendants(pane).OfType<Label>().Any(l=>l.Text.Contains("Riavvio manuale richiesto")),"Esito non riletto."); form.Close();
                }
            });
            Test("UI protezioni: lettura simulata, stati, ultima voce e annulla",delegate {
                FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=new AppServices(backend);
                using (SecurityDialog form=new SecurityDialog(app,false))
                {
                    form.ShowInTaskbar=false; form.Show(); form.DisplayStates(backend.Environment); Application.DoEvents();
                    Assert(Descendants(form).OfType<SecurityPane>().Count()==7,"Pannelli mancanti.");
                    StackPanel stack=Descendants(form).OfType<StackPanel>().Single();
                    stack.ScrollTo(Int32.MaxValue); Assert(stack.ScrollOffset>0 && stack.ScrollOffset==stack.ScrollMaximum,"Ultima voce non raggiungibile.");
                    SecurityPane pane=Descendants(form).OfType<SecurityPane>().First(p=>p.Definition.Id=="hvci");
                    using (System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=20 })
                    {
                        timer.Tick+=delegate {
                            ConfirmDialog confirm=Application.OpenForms.OfType<ConfirmDialog>().FirstOrDefault();
                            if (confirm!=null) { timer.Stop(); Assert(((Button)confirm.AcceptButton).Text=="&Annulla","Conferma predefinita pericolosa."); ((Button)confirm.AcceptButton).PerformClick(); }
                        };
                        timer.Start(); Descendants(pane).OfType<Button>().First(b=>b.Text=="Disattiva").PerformClick();
                        PumpUntil(delegate { return !app.Coordinator.IsBusy; });
                        Assert(backend.WriteCount==0,"Annulla ha modificato una protezione.");
                    }
                    form.Close();
                }
            });
        }
    }
    internal sealed class FakeSecurityBackend : ISecurityBackend,IRegistryStore
    {
        public readonly FakeRegistry Store=new FakeRegistry();
        public readonly SecurityEnvironment Environment=new SecurityEnvironment {
            PolicyEdition=true,ManagementKnown=true,DeviceGuardKnown=true,VbsRunning=true,HvciConfigured=true,HvciRunning=true,
            KernelConfigured=true,KernelRunning=true,DefenderKnown=true,DefenderActive=true,TamperKnown=true,Pua=1 };
        public bool Template=true,FailDelete,IgnorePua,FailRead;
        public int WriteCount,FailWrite;
        public IRegistryStore Registry { get { return this; } }
        public SecurityEnvironment Probe() { return Environment; }
        public bool HasTemplate(string name,string value) { return Template; }
        public void SetPua(int mode) { if (!IgnorePua) Environment.Pua=mode; WriteCount++; }
        public RegistryValueState Read(string path,string name) { if (FailRead) throw new IOException("Errore lettura simulata."); return Store.Read(path,name); }
        public void Set(string path,string name,object data,RegistryValueKind kind)
        { WriteCount++; if (WriteCount==FailWrite) throw new IOException("Errore scrittura simulata."); Store.Set(path,name,data,kind); }
        public void DeleteValue(string path,string name) { if (FailDelete) throw new IOException("Accesso negato simulato."); Store.DeleteValue(path,name); }
        public void DeleteKeyIfEmpty(string path) { Store.DeleteKeyIfEmpty(path); }
        public void Notify() { }
    }
}
