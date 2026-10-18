using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static int passed, failed;
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            RegistryTests(); SpoolTests(); OtherTests(); SecurityTests();
            if (args.Contains("--render")) Test("render UI senza esecuzione azioni",Render);
            if (args.Contains("--read-only")) Test("servizi reali: sole letture",ReadOnlySmoke);
            if (args.Contains("--artifact")) Test("EXE: architettura, manifest e dipendenze",VerifyArtifact);
            if (args.Contains("--ui")) { UiTests(); WheelTests(); SecurityUiTests(); }
            Console.WriteLine("RISULTATO: " + passed + " superati, " + failed + " falliti.");
            return failed==0 ? 0 : 1;
        }
        private static void Test(string title, Action body)
        {
            try { body(); passed++; Console.WriteLine("PASS " + title); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + title + "\n" + ex); }
        }
        private static void Assert(bool condition,string message)
        { if (!condition) throw new Exception(message); }
        private static void Throws(Action body,string text)
        {
            try { body(); } catch (Exception ex) { Assert(ex.Message.Contains(text),ex.Message); return; }
            throw new Exception("Era attesa un'eccezione contenente: " + text);
        }
        private static void Ignore(string text) { }
        private static void RegistryTests()
        {
            Test("registro: ripristina tipo e valore personalizzato",delegate {
                FakeRegistry store=new FakeRegistry();
                RegistryTweakService service=new RegistryTweakService(store);
                TweakDefinition definition=service.Definitions[1];
                store.Set(definition.Path,definition.ValueName,"custom",RegistryValueKind.ExpandString);
                service.Apply(definition,true,Ignore); service.Apply(definition,false,Ignore); service.Restore(definition,Ignore);
                RegistryValueState value=store.Read(definition.Path,definition.ValueName);
                Assert(value.ValueExists && value.Kind==RegistryValueKind.ExpandString && (string)value.Data=="custom","Tipo o valore persi.");
            });
            Test("registro: valore assente in chiave esistente",delegate {
                FakeRegistry store=new FakeRegistry(); store.Set(RegistryTweakService.ExplorerPath,"altro",5,RegistryValueKind.DWord);
                RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[2];
                service.Apply(d,true,Ignore); service.Restore(d,Ignore);
                Assert(!store.Read(d.Path,d.ValueName).ValueExists,"Valore non rimosso.");
                Assert(store.Read(d.Path,"altro").ValueExists,"Dati estranei rimossi.");
            });
            Test("registro: rimuove solo chiavi create dalla sessione",delegate {
                FakeRegistry store=new FakeRegistry(); store.Ensure(@"Software\Classes\CLSID");
                RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[0];
                service.Apply(d,true,Ignore); service.Restore(d,Ignore);
                Assert(!store.Read(d.Path,"").KeyExists,"Chiave creata non rimossa.");
                Assert(store.Read(@"Software\Classes\CLSID","").KeyExists,"Chiave preesistente rimossa.");
            });
            Test("registro: conserva chiavi originariamente vuote",delegate {
                FakeRegistry store=new FakeRegistry(); store.Ensure(RegistryTweakService.ClassicPath);
                RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[0];
                service.Apply(d,true,Ignore); service.Restore(d,Ignore);
                Assert(store.Read(d.Path,"").KeyExists && !store.Read(d.Path,"").ValueExists,"Chiave originale non preservata.");
            });
            Test("registro: menu Windows 11 non cancella COM personalizzato",delegate {
                FakeRegistry store=new FakeRegistry(); store.Set(RegistryTweakService.ClassicPath,"","custom.dll",RegistryValueKind.String);
                RegistryTweakService service=new RegistryTweakService(store);
                Throws(delegate { service.Apply(service.Definitions[0],false,Ignore); },"personalizzato");
                Assert((string)store.Read(RegistryTweakService.ClassicPath,"").Data=="custom.dll","Valore estraneo cancellato.");
            });
            Test("registro: attivazione e ripristino COM preesistente",delegate {
                FakeRegistry store=new FakeRegistry(); store.Set(RegistryTweakService.ClassicPath,"","original.dll",RegistryValueKind.String);
                RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[0];
                service.Apply(d,true,Ignore); service.Apply(d,false,Ignore); service.Restore(d,Ignore);
                Assert((string)store.Read(d.Path,"").Data=="original.dll","Dati originali persi.");
            });
            Test("registro: conserva dati aggiunti durante la sessione",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[0];
                service.Apply(d,true,Ignore); store.Set(d.Path,"estraneo","keep",RegistryValueKind.String); service.Restore(d,Ignore);
                Assert((string)store.Read(d.Path,"estraneo").Data=="keep","Dati estranei persi.");
            });
            Test("registro: ripristino valore binario",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[1];
                store.Set(d.Path,d.ValueName,new byte[] { 0,17,255 },RegistryValueKind.Binary);
                service.Apply(d,true,Ignore); service.Restore(d,Ignore);
                Assert(((byte[])store.Read(d.Path,d.ValueName).Data).SequenceEqual(new byte[] { 0,17,255 }),"Binario alterato.");
            });
            Test("registro: ripristino valore MultiString",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[1];
                store.Set(d.Path,d.ValueName,new string[] { "uno","due" },RegistryValueKind.MultiString);
                service.Apply(d,true,Ignore); service.Restore(d,Ignore);
                Assert(((string[])store.Read(d.Path,d.ValueName).Data).Length==2,"MultiString alterato.");
            });
            Test("registro: errore di scrittura mantiene il backup",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[1];
                store.FailSet=true;
                Throws(delegate { service.Apply(d,true,Ignore); },"scrittura");
                Assert(service.CanRestore(d),"Backup non conservato dopo errore.");
            });
            Test("registro: rileva scrittura ignorata e non annuncia successo",delegate {
                FakeRegistry store=new FakeRegistry { IgnoreSet=true }; RegistryTweakService service=new RegistryTweakService(store);
                Throws(delegate { service.Apply(service.Definitions[1],true,Ignore); },"non corrisponde");
            });
            Test("registro: nessun ripristino senza snapshot",delegate {
                RegistryTweakService service=new RegistryTweakService(new FakeRegistry());
                Assert(!service.CanRestore(service.Definitions[0]),"Ripristino disponibile senza backup.");
                Throws(delegate { service.Restore(service.Definitions[0],Ignore); },"Nessun valore");
            });
            Test("registro: valori nascosti rispettano 1/2",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store); TweakDefinition d=service.Definitions[2];
                service.Apply(d,true,Ignore); Assert((int)store.Read(d.Path,d.ValueName).Data==1,"Hidden enabled");
                service.Apply(d,false,Ignore); Assert((int)store.Read(d.Path,d.ValueName).Data==2,"Hidden disabled");
            });
        }
        private static void SpoolTests()
        {
            Test("spool: solo SPL/SHD, senza ricorsione",delegate {
                FakeSpool backend=new FakeSpool(); backend.Entries.AddRange(new[] { backend.PathOf("001.SPL"), backend.PathOf("001.shd"),backend.PathOf("keep.tmp"),backend.PathOf("directory") });
                new SpoolService(backend).Clean(Ignore);
                Assert(backend.Deleted.Count==2,"Numero file eliminati errato.");
                Assert(backend.Calls.First()=="stop" && backend.Calls.Last()=="start","Ordine del servizio errato.");
            });
            Test("spool: percorso personalizzato blocca prima dello stop",delegate {
                FakeSpool backend=new FakeSpool(); backend.Configured.Add(@"C:\custom");
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"personalizzato");
                Assert(backend.Calls.Count==0,"Servizio modificato dopo preflight fallito.");
            });
            Test("spool: percorso UNC bloccato",delegate {
                FakeSpool backend=new FakeSpool(); backend.Configured.Add(@"\\server\spool");
                Assert(new SpoolService(backend).CleanupUnavailable()!=null,"UNC consentito.");
            });
            Test("spool: percorso relativo bloccato",delegate {
                FakeSpool backend=new FakeSpool(); backend.Configured.Add(@"spool\printers");
                Assert(new SpoolService(backend).CleanupUnavailable()!=null,"Relativo consentito.");
            });
            Test("spool: cartella reindirizzata bloccata",delegate {
                FakeSpool backend=new FakeSpool { PlainDirectory=false };
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"reindirizzata"); Assert(backend.Calls.Count==0,"Servizio toccato.");
            });
            Test("spool: configurazione cambiata dopo lo stop",delegate {
                FakeSpool backend=new FakeSpool { ChangeOnStop=true }; backend.Entries.Add(backend.PathOf("001.spl"));
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"incompleta");
                Assert(backend.Deleted.Count==0 && backend.Calls.Last()=="start","Validazione post stop / restart mancanti.");
            });
            Test("spool: stop fallito tenta comunque avvio",delegate {
                FakeSpool backend=new FakeSpool { FailStop=true }; backend.Entries.Add(backend.PathOf("001.spl"));
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"stop simulato");
                Assert(backend.Deleted.Count==0 && backend.Calls.Last()=="start","Avvio non tentato o file cancellati senza stop.");
            });
            Test("spool: errore parziale non impedisce altre rimozioni e riavvio",delegate {
                FakeSpool backend=new FakeSpool { FailDelete="001.spl" }; backend.Entries.AddRange(new[] { backend.PathOf("001.spl"),backend.PathOf("002.shd") });
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"bloccato");
                Assert(backend.Deleted.Count==1 && backend.Calls.Last()=="start","Gestione parziale errata.");
            });
            Test("spool: errori pulizia e avvio distinti",delegate {
                FakeSpool backend=new FakeSpool { FailStop=true, FailStart=true };
                List<string> lines=new List<string>();
                Throws(delegate { new SpoolService(backend).Clean(lines.Add); },"avvio simulato");
                Assert(lines.Any(l=>l.Contains("Arresto/pulizia:")) && lines.Any(l=>l.Contains("Riavvio spooler:")),"Errori non distinti.");
            });
            Test("spool: file reparse non eliminato",delegate {
                FakeSpool backend=new FakeSpool { PlainFile=false }; backend.Entries.Add(backend.PathOf("link.spl"));
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"reindirizzato"); Assert(backend.Deleted.Count==0,"Link eliminato.");
            });
            Test("spool: file esterno non eliminato",delegate {
                FakeSpool backend=new FakeSpool(); backend.Entries.Add(@"C:\outside\001.spl");
                Throws(delegate { new SpoolService(backend).Clean(Ignore); },"fuori"); Assert(backend.Deleted.Count==0 && backend.Calls.Last()=="start","File esterno eliminato.");
            });
            Test("spool: restart non cancella file",delegate {
                FakeSpool backend=new FakeSpool(); backend.Entries.Add(backend.PathOf("001.spl")); new SpoolService(backend).Restart(Ignore);
                Assert(backend.Deleted.Count==0 && backend.Calls.SequenceEqual(new[] { "stop","start" }),"Restart modifica le code.");
            });
            Test("spool: cartella vuota riavvia servizio",delegate {
                FakeSpool backend=new FakeSpool(); new SpoolService(backend).Clean(Ignore); Assert(backend.Calls.Last()=="start","Spool vuoto non gestito.");
            });
            Test("spool: confronto percorso senza distinzione maiuscole",delegate {
                FakeSpool backend=new FakeSpool(); backend.Configured.Add(backend.StandardDirectory.ToUpperInvariant()+"\\");
                Assert(new SpoolService(backend).CleanupUnavailable()==null,"Percorso equivalente rifiutato.");
            });
        }
        private static void OtherTests()
        {
            Test("coordinatore: un'operazione alla volta",delegate {
                OperationCoordinator gate=new OperationCoordinator(); Assert(gate.TryEnter(),"Primo ingresso fallito.");
                Assert(!gate.TryEnter() && gate.IsBusy,"Ingresso concorrente permesso."); gate.Leave(); Assert(gate.TryEnter(),"Gate non rilasciato."); gate.Leave();
            });
            Test("quotatura argomenti Windows: round trip",delegate {
                foreach (string value in new[] { "", "Stampante ufficio", "Caffè & copie", @"\\server\stampante", "fine\\", "a\"b", "a\\\"b", "$(literal) & %PATH%" })
                {
                    int count;
                    IntPtr args=CommandLineToArgvW("tool.exe " + ProcessService.QuoteArgument(value),out count);
                    if (args==IntPtr.Zero) throw new Exception("Parsing fallito.");
                    try { Assert(count==2 && Marshal.PtrToStringUni(Marshal.ReadIntPtr(args,IntPtr.Size))==value,"Round trip fallito: " + value); }
                    finally { LocalFree(args); }
                }
            });
            Test("PrintUI: rifiuta mode e nomi non ammessi senza avvio",delegate {
                ProcessService service=new ProcessService();
                Throws(delegate { service.PrintUi("/dl","test",Ignore); },"non consentita");
                Throws(delegate { service.PrintUi("/p","test\" /dl",Ignore); },"non compatibile");
            });
            Test("catalogo: azioni e identificatori univoci",delegate {
                AppServices app=new AppServices(); HashSet<string> ids=new HashSet<string>();
                foreach (MenuDefinition menu in new[] { app.Catalog.Network,app.Catalog.Maintenance,app.Catalog.Tools,app.Catalog.Spool,app.Catalog.Accounts }) ValidateMenu(menu,ids);
                Assert(ids.Count==35,"Catalogo incompleto: attese 35 azioni, trovate " + ids.Count + ".");
                Assert(ids.Contains("tools.computer") && ids.Contains("tools.programs") && ids.Contains("accounts.close"),"Collegamenti mancanti.");
            });
            Test("catalogo: righe brevi, glifi e testo completo conservato",delegate {
                AppServices app=new AppServices(); int brief=0,full=0;
                List<ActionSpec> actions=new List<ActionSpec> { app.Catalog.ExplorerRestart };
                foreach (MenuDefinition menu in new[] { app.Catalog.Network,app.Catalog.Maintenance,app.Catalog.Tools,app.Catalog.Spool,app.Catalog.Accounts })
                {
                    ValidateGlyphs(menu); actions.AddRange(menu.AllActions());
                }
                foreach (ActionSpec action in actions)
                {
                    AssertBrief(action.CardText,action.Id); Assert(action.Description.Length>=action.CardText.Length,"Descrizione completa persa: "+action.Id);
                    brief+=action.CardText.Length; full+=action.Description.Length;
                }
                foreach (SecurityDefinition definition in app.Security.Definitions) AssertBrief(definition.Summary??definition.Description,definition.Id);
                foreach (TweakDefinition definition in app.Tweaks.Definitions) AssertBrief(definition.Summary??definition.Description,definition.Id);
                Console.WriteLine("Testo delle card: "+brief+" caratteri visibili su "+full+" del testo completo.");
            });
            Test("cronologia: solo istanza corrente",delegate {
                SessionHistory history=new SessionHistory(); history.Add("test");
                Assert(history.Read().Contains("test") && !new SessionHistory().Read().Contains("test"),"Cronologia condivisa o mancante.");
            });
        }
        private static void ValidateMenu(MenuDefinition menu,HashSet<string> ids)
        {
            foreach (ActionSpec action in menu.Actions)
                Assert(ids.Add(action.Id) && action.Execute!=null && !String.IsNullOrWhiteSpace(action.Scope) && !String.IsNullOrWhiteSpace(action.Description),"Azione incompleta o duplicata: " + action.Id);
            foreach (MenuDefinition child in menu.Children) ValidateMenu(child,ids);
        }
        private static void ValidateGlyphs(MenuDefinition menu)
        {
            Assert(!String.IsNullOrEmpty(menu.Glyph),"Menu senza glifo: "+menu.Title);
            Assert(menu.Description.Length<=70,"Sottotitolo troppo lungo: "+menu.Title);
            foreach (MenuDefinition child in menu.Children) ValidateGlyphs(child);
        }
        // Una sola riga e una sola frase: le avvertenze estese restano nel tooltip e nelle conferme.
        private static void AssertBrief(string text,string id)
        {
            Assert(!String.IsNullOrWhiteSpace(text) && text.Length<=70,"Testo breve assente o troppo lungo ("+text.Length+"): "+id);
            Assert(text.TrimEnd('.').IndexOf(". ",StringComparison.Ordinal)<0,"Più frasi nel testo breve: "+id);
        }
        private static void ReadOnlySmoke()
        {
            AppServices app=new AppServices();
            Console.WriteLine(EnvironmentInfo.Summary);
            SecurityEnvironment security=app.Security.Probe();
            foreach (SecurityDefinition definition in app.Security.Definitions)
                Console.WriteLine(definition.Title+": "+app.Security.Read(definition,security).ToString().Replace("\n"," · "));
            foreach (TweakDefinition definition in app.Tweaks.Definitions) Console.WriteLine(definition.Title + ": " + app.Tweaks.Describe(definition));
            try { Console.WriteLine("Spooler: " + app.Spool.Status()); } catch (Exception ex) { Console.WriteLine("Spooler non leggibile: " + ex.Message); }
            string unavailable=app.Spool.CleanupUnavailable(); Console.WriteLine("Preflight sola lettura: " + (unavailable ?? "OK"));
            foreach (PrinterItem item in app.Printers.Read()) Console.WriteLine("Stampante: " + item);
            List<string> network=new List<string>(); ProcessService.ReadNetwork(network.Add);
            Console.WriteLine("Diagnostica rete: " + network.Count + " righe lette (indirizzi non riportati nel test).");
        }
        private static void VerifyArtifact()
        {
            string path=Path.GetFullPath(@"dist\OnlyTweakWindows.exe");
            using (BinaryReader reader=new BinaryReader(File.OpenRead(path)))
            {
                Assert(reader.ReadUInt16()==0x5a4d,"Firma DOS errata.");
                reader.BaseStream.Position=0x3c;
                int pe=reader.ReadInt32(); reader.BaseStream.Position=pe;
                Assert(reader.ReadUInt32()==0x4550 && reader.ReadUInt16()==0x8664,"PE non x64.");
                reader.BaseStream.Position=pe+24+68;
                Assert(reader.ReadUInt16()==2,"EXE non Windows GUI.");
            }
            IntPtr module=LoadLibraryEx(path,IntPtr.Zero,2);
            Assert(module!=IntPtr.Zero,"Impossibile leggere le risorse EXE.");
            try
            {
                IntPtr resource=FindResource(module,new IntPtr(1),new IntPtr(24));
                Assert(resource!=IntPtr.Zero,"Manifest assente.");
                int size=checked((int)SizeofResource(module,resource));
                byte[] bytes=new byte[size]; Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,size);
                string manifest=Encoding.UTF8.GetString(bytes);
                Assert(manifest.Contains("requireAdministrator") && manifest.Contains("PerMonitorV2"),"Manifest UAC/DPI incompleto.");
            }
            finally { FreeLibrary(module); }
            System.Reflection.Assembly assembly=System.Reflection.Assembly.ReflectionOnlyLoadFrom(path);
            string[] allowed={ "mscorlib","System","System.Core","System.Drawing","System.Windows.Forms","System.ServiceProcess" };
            foreach (System.Reflection.AssemblyName reference in assembly.GetReferencedAssemblies())
                Assert(allowed.Contains(reference.Name),"Dipendenza non di sistema: " + reference.Name);
            Assert(Directory.GetFiles(Path.GetDirectoryName(path)).Length==1,"La directory dist deve contenere soltanto l'EXE.");
        }
        private static void PumpUntil(Func<bool> condition)
        {
            DateTime deadline=DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow>deadline) throw new Exception("Timeout UI.");
                Application.DoEvents(); Thread.Sleep(5);
            }
            Application.DoEvents();
        }
        private static void UiTests()
        {
            // Keep a real application message loop and a root form alive for async continuations.
            using (Form host=new Form { ShowInTaskbar=false, Opacity=0, Size=new Size(1,1) })
            {
                host.Shown+=delegate { host.BeginInvoke((Action)delegate { try { UiTestCases(); } finally { host.Close(); } }); };
                Application.Run(host);
            }
        }
        private static void UiTestCases()
        {
            Test("UI: apertura diretta senza finestre intermedie",delegate {
                AppServices app=new AppServices(); int executed=0,count=Application.OpenForms.Count; string status="";
                ActionSpec action=new ActionSpec("test.launch","Proprietà simulate","Test","Simulazione",delegate(Action<string> log) { executed++; log("apertura simulata"); }) { OpensWindow=true };
                System.Threading.Tasks.Task task=ActionDispatcher.Invoke(Form.ActiveForm,app,action,delegate(string line) { status=line; });
                PumpUntil(delegate { return task.IsCompleted; }); task.GetAwaiter().GetResult();
                Assert(executed==1&&Application.OpenForms.Count==count,"Apertura duplicata o modale superfluo.");
                Assert(status.StartsWith("Aperto:")&&!app.Coordinator.IsBusy,"Esito o gate non aggiornati.");
            });
            Test("UI: launcher indisponibile mostra errore senza aprire modali",delegate {
                AppServices app=new AppServices(); bool executed=false; string status=""; int count=Application.OpenForms.Count;
                ActionSpec action=new ActionSpec("test.launch.fail","Test","Test","Simulazione",delegate(Action<string> log) { executed=true; }) { OpensWindow=true,Unavailable=delegate { return "strumento assente"; } };
                System.Threading.Tasks.Task task=ActionDispatcher.Launch(app,action,delegate(string line) { status=line; });
                PumpUntil(delegate { return task.IsCompleted; }); task.GetAwaiter().GetResult();
                Assert(!executed&&status.Contains("strumento assente")&&Application.OpenForms.Count==count,"Errore launcher non gestito inline.");
            });
            Test("UI: l'operazione parte senza un secondo clic Esegui",delegate {
                AppServices app=new AppServices(); bool executed=false;
                using (OperationDialog form=new OperationDialog(app,new ActionSpec("test.auto","Test automatico","Simulazione","Test",delegate(Action<string> log) { executed=true; }),true))
                {
                    form.ShowInTaskbar=false; form.Show();
                    PumpUntil(delegate { return executed&&!app.Coordinator.IsBusy; });
                    Assert(!Descendants(form).OfType<Button>().Any(b=>b.Text=="&Esegui"&&b.Visible),"Esegui ridondante ancora visibile.");
                    form.Close();
                }
            });
            Test("UI: annulla conferma non avvia l'azione",delegate {
                AppServices app=new AppServices(); bool executed=false;
                ActionSpec action=new ActionSpec("test.cancel","Conferma simulata","Simulazione","Test",delegate(Action<string> log) { executed=true; }) { Confirmation="Conferma di test, nessuna modifica reale." };
                using (System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=20 })
                {
                    timer.Tick+=delegate {
                        ConfirmDialog confirm=Application.OpenForms.OfType<ConfirmDialog>().FirstOrDefault();
                        if (confirm!=null) { timer.Stop(); Descendants(confirm).OfType<Button>().First(b=>b.Text=="&Annulla").PerformClick(); }
                    };
                    timer.Start(); OperationDialog.Open(Form.ActiveForm,app,action);
                }
                Assert(!executed&&!app.Coordinator.IsBusy,"Annullamento non rispettato.");
            });
            Test("UI: una sola conferma e partenza immediata",delegate {
                AppServices app=new AppServices(); int executed=0,confirmations=0;
                ActionSpec action=new ActionSpec("test.confirm","Conferma simulata","Simulazione","Test",delegate(Action<string> log) { executed++; }) { Confirmation="Conferma di test, nessuna modifica reale." };
                using (System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=20 })
                {
                    timer.Tick+=delegate {
                        ConfirmDialog confirm=Application.OpenForms.OfType<ConfirmDialog>().FirstOrDefault();
                        if (confirm!=null) { confirmations++; Descendants(confirm).OfType<Button>().First(b=>b.Text=="&Conferma").PerformClick(); }
                        OperationDialog operation=Application.OpenForms.OfType<OperationDialog>().FirstOrDefault();
                        if (operation!=null&&executed>0&&!app.Coordinator.IsBusy) { timer.Stop(); operation.Close(); }
                    };
                    timer.Start(); OperationDialog.Open(Form.ActiveForm,app,action);
                }
                Assert(executed==1&&confirmations==1,"Conferma o esecuzione duplicata.");
            });
            Test("UI: scorrimento dei gruppi e focus da tastiera",delegate {
                using (Form host=new Form { Size=new Size(600,280),ShowInTaskbar=false })
                {
                    StackPanel stack=new StackPanel(); host.Controls.Add(stack);
                    for (int i=0;i<15;i++) stack.Controls.Add(Theme.Card("Azione "+i,"Descrizione",null));
                    host.Show(); Application.DoEvents();
                    Assert(stack.ScrollMaximum>0,"Gruppo non scorrevole.");
                    stack.ScrollTo(Int32.MaxValue); Assert(stack.ScrollOffset==stack.ScrollMaximum,"Fine gruppo irraggiungibile.");
                    stack.Controls[14].Focus(); Application.DoEvents();
                    stack.Controls[0].Focus(); Application.DoEvents(); Assert(stack.ScrollOffset<30,"Il focus non riporta visibile il primo controllo.");
                    stack.Controls[14].Focus(); Application.DoEvents(); Assert(stack.ScrollOffset>0,"Ultimo controllo non raggiunto."); host.Close();
                }
            });
            Test("UI: output lungo con scrollbar verticali e orizzontali scure",delegate {
                using (Form host=new Form { Size=new Size(500,260),ShowInTaskbar=false })
                {
                    DarkOutput output=Theme.Output(); host.Controls.Add(output); host.Show();
                    output.Text=String.Join("\n",Enumerable.Range(0,180).Select(i=>"Riga "+i+" "+new string('x',160)).ToArray());
                    Application.DoEvents();
                    DarkScrollBar vertical=Descendants(output).OfType<DarkScrollBar>().First(b=>!b.Horizontal);
                    DarkScrollBar horizontal=Descendants(output).OfType<DarkScrollBar>().First(b=>b.Horizontal);
                    Assert(vertical.Visible&&horizontal.Visible,"Scrollbar mancanti.");
                    vertical.Value=vertical.Maximum; horizontal.Value=horizontal.Maximum;
                    RichTextBox editor=Descendants(output).OfType<RichTextBox>().Single(); Point position=new Point();
                    ScrollPosition(editor.Handle,0x04dd,IntPtr.Zero,ref position);
                    Assert(position.Y>100&&position.X>0,"Il trascinamento non scorre l'output.");
                    editor.SelectionStart=0; editor.SelectionLength=0; editor.ScrollToCaret(); Application.DoEvents();
                    Assert(vertical.Value<50,"La scrollbar non segue il cursore.");
                    host.Close();
                }
            });
            Test("UI: elenco lungo stampanti e selezione da tastiera",delegate {
                using (Form host=new Form { Size=new Size(500,220),ShowInTaskbar=false })
                {
                    DarkPrinterList list=new DarkPrinterList { Dock=DockStyle.Fill }; host.Controls.Add(list);
                    for (int i=0;i<30;i++) list.Items.Add(new PrinterItem { Name="Stampante "+i });
                    host.Show(); list.SelectedIndex=29; Application.DoEvents();
                    DarkScrollBar bar=Descendants(list).OfType<DarkScrollBar>().Single(); Assert(bar.Value>0,"Selezione fuori vista.");
                    typeof(DarkPrinterList).GetMethod("OnKeyDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(list,new object[] { new KeyEventArgs(Keys.Home) });
                    Assert(list.SelectedIndex==0&&bar.Value==0,"Home non seleziona/mostra la prima stampante.");
                    AccessibleObject accessible=list.AccessibilityObject;
                    Assert(accessible.GetChildCount()==30&&accessible.GetChild(29).Name=="Stampante 29","Elementi stampante non accessibili.");
                    accessible.GetChild(4).DoDefaultAction(); Assert(list.SelectedIndex==4,"Selezione accessibile non funzionante.");
                    list.Items.Clear(); Assert(list.SelectedItem==null&&list.SelectedIndex==-1,"Selezione obsoleta dopo aggiornamento."); host.Close();
                }
            });
            Test("UI: categorie piatte e launcher classificati",delegate {
                AppServices app=new AppServices();
                using (MenuDialog form=new MenuDialog(app,app.Catalog.Network))
                    Assert(Descendants(form).OfType<Button>().Count(b=>b.AccessibleDescription!=null)==7,"Rete richiede ancora sottosezioni intermedie.");
                foreach (ActionSpec action in app.Catalog.Tools.Actions) Assert(action.OpensWindow,"Strumento non diretto: "+action.Id);
                Assert(app.Catalog.PrinterDialog("/p","Proprietà","Test").OpensWindow&&!app.Catalog.PurgePrinter("Test").OpensWindow,"Classificazione stampanti errata.");
            });
            Test("UI: icone, testo completo nel tooltip ed eyebrow solo nella home",delegate {
                AppServices app=new AppServices();
                using (MenuDialog form=new MenuDialog(app,app.Catalog.Network))
                {
                    foreach (ActionSpec action in app.Catalog.Network.AllActions())
                    {
                        DarkButton card=Descendants(form).OfType<DarkButton>().Single(b=>b.Text==action.Title);
                        Assert(card.Glyph==Glyphs.ForAction(action) && card.Description==action.CardText && card.AccessibleDescription==action.Description,"Card incoerente: "+action.Id);
                        Assert(action.CardText==action.Description || form.Tips.GetToolTip(card)==action.Description,"Dettaglio non disponibile: "+action.Id);
                    }
                    Assert(!Descendants(form).OfType<Label>().Any(l=>l.Text.StartsWith("ONLYTWEAK",StringComparison.Ordinal)),"Eyebrow nelle finestre secondarie.");
                }
                using (MainForm form=new MainForm(app))
                {
                    Assert(Descendants(form).OfType<Label>().Any(l=>l.Text.StartsWith("ONLYTWEAK",StringComparison.Ordinal)),"Eyebrow assente nella home.");
                    Assert(Descendants(form).OfType<DarkButton>().Count(b=>b.Glyph!=null && b.GlyphColor==Theme.Accent)==7,"Tessere home senza icona.");
                }
                using (MenuDialog form=new MenuDialog(app,app.Catalog.Accounts))
                {
                    InfoIcon info=Descendants(form).OfType<InfoIcon>().Single();
                    Assert(info.TabStop && info.AccessibleDescription==app.Catalog.Accounts.Detail && form.Tips.GetToolTip(info)==app.Catalog.Accounts.Detail,"Dettaglio Account non accessibile.");
                }
            });
            Test("UI: dettaglio Info solo su richiesta e chiuso con finestra spostata o chiusa",delegate {
                AppServices app=new AppServices();
                MenuDialog form=new MenuDialog(app,app.Catalog.Accounts) { ShowInTaskbar=false };
                // Il banco non può portare la finestra in primo piano: ShowAlways evita il solo controllo di attivazione.
                form.Tips.ShowAlways=true;
                try
                {
                    form.Show(); Application.DoEvents();
                    InfoIcon info=Descendants(form).OfType<InfoIcon>().Single();
                    Assert(form.ActiveControl!=info && VisibleTooltips()==0,"Dettaglio aperto all'avvio della finestra.");
                    info.Focus(); Application.DoEvents(); Assert(VisibleTooltips()==0,"Dettaglio aperto dal solo focus.");
                    info.ShowDetail(); Application.DoEvents(); Assert(VisibleTooltips()>0,"Dettaglio non mostrato su richiesta.");
                    form.Left+=24; Application.DoEvents(); Assert(VisibleTooltips()==0,"Dettaglio rimasto dopo lo spostamento della finestra.");
                    info.ShowDetail(); Application.DoEvents(); Assert(VisibleTooltips()>0,"Dettaglio non riaperto.");
                    form.Close(); Application.DoEvents(); Assert(VisibleTooltips()==0,"Dettaglio rimasto dopo la chiusura.");
                }
                finally { form.Dispose(); }
            });
            Test("UI: senza font icone nessun errore e nessun glifo",delegate {
                string family=Theme.IconFamily; Theme.IconFamily=null;
                try
                {
                    Assert(Theme.IconFont(12F)==null,"Font icone non disattivato.");
                    AppServices app=new AppServices();
                    foreach (DarkForm form in new DarkForm[] { new MainForm(app),new MenuDialog(app,app.Catalog.Network) })
                        using (form) using (Bitmap bitmap=new Bitmap(form.Width,form.Height))
                        { form.CreateControl(); form.PerformLayout(); form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); }
                }
                finally { Theme.IconFamily=family; }
            });
            Test("UI: esecuzione asincrona e blocco chiusura durante il lavoro",delegate {
                AppServices app=new AppServices();
                using (ManualResetEvent release=new ManualResetEvent(false))
                using (OperationDialog form=new OperationDialog(app,new ActionSpec("test.wait","Test simulato","Nessun intervento Windows.","Simulazione",
                    delegate(Action<string> log) { log("output simulato"); release.WaitOne(3000); })))
                {
                    form.ShowInTaskbar=false; form.Show(); Application.DoEvents();
                    Button run=Descendants(form).OfType<Button>().First(b=>b.Text=="&Esegui");
                    PumpUntil(delegate { return run.Enabled; }); run.PerformClick();
                    Assert(app.Coordinator.IsBusy,"Gate non acquisito.");
                    form.Close(); Assert(!form.IsDisposed,"Chiusura consentita durante l'operazione.");
                    release.Set(); PumpUntil(delegate { return !app.Coordinator.IsBusy; });
                    Assert(Descendants(form).OfType<DarkOutput>().Any(t=>t.Text.Contains("output simulato")),"Output perso.");
                    Assert(app.History.Read().Contains("TERMINATA"),"Cronologia successo mancante.");
                    form.Close();
                }
            });
            Test("UI: errore simulato riabilita controlli e libera il gate",delegate {
                AppServices app=new AppServices();
                using (OperationDialog form=new OperationDialog(app,new ActionSpec("test.error","Errore simulato","Nessun intervento Windows.","Simulazione",
                    delegate(Action<string> log) { throw new IOException("errore controllato"); })))
                {
                    form.ShowInTaskbar=false; form.Show(); Application.DoEvents();
                    Button run=Descendants(form).OfType<Button>().First(b=>b.Text=="&Esegui"); run.PerformClick();
                    PumpUntil(delegate { return !app.Coordinator.IsBusy; });
                    Assert(run.Enabled && app.History.Read().Contains("ERRORE"),"Controlli/gate non ripristinati.");
                    Assert(Descendants(form).OfType<DarkOutput>().Any(t=>t.Text.Contains("errore controllato")),"Errore non mostrato.");
                    form.Close();
                }
            });
            Test("UI: azione indisponibile non può avviarsi",delegate {
                bool executed=false; AppServices app=new AppServices();
                ActionSpec action=new ActionSpec("test.unavailable","Non disponibile","Nessun intervento Windows.","Simulazione",delegate(Action<string> log) { executed=true; });
                action.Unavailable=delegate { return "blocco simulato"; };
                using (OperationDialog form=new OperationDialog(app,action))
                {
                    form.ShowInTaskbar=false; form.Show();
                    PumpUntil(delegate { return Descendants(form).OfType<DarkOutput>().Any(t=>t.Text.Contains("blocco simulato")); });
                    Button run=Descendants(form).OfType<Button>().First(b=>b.Text=="&Esegui"); run.PerformClick();
                    Assert(!run.Enabled && !executed,"Azione indisponibile eseguita."); form.Close();
                }
            });
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control control in parent.Controls)
            { yield return control; foreach (Control child in Descendants(control)) yield return child; }
        }
        private static void Render()
        {
            string folder=Path.GetFullPath(@"artifacts\ui"); Directory.CreateDirectory(folder);
            foreach (float scale in new[] { 1F,1.5F,2F })
            {
                FakeSecurityBackend security=new FakeSecurityBackend(); AppServices app=new AppServices(security);
                SecurityDialog protections=new SecurityDialog(app,false); protections.DisplayStates(security.Environment);
                List<DarkForm> forms=new List<DarkForm> {
                    new MainForm(app),new AppearanceDialog(app),new ConfirmDialog("Svuota tutte le code locali",app.Catalog.Spool.Actions[2].Confirmation),
                    new PrinterDialog(app,false),new MenuDialog(app,app.Catalog.Network),new MenuDialog(app,app.Catalog.Network.Children[1]),
                    new MenuDialog(app,app.Catalog.Maintenance.Children[0]),new OperationDialog(app,app.Catalog.Spool.Actions[2]),new MenuDialog(app,app.Catalog.Tools),
                    new TextDialog("Output di esempio",String.Join("\n",Enumerable.Range(1,80).Select(i=>"Riga di esempio "+i+"  "+new string('x',125)).ToArray())),
                    protections,new MenuDialog(app,app.Catalog.Accounts)
                };
                for (int i=0;i<forms.Count;i++)
                {
                    using (DarkForm form=forms[i])
                    {
                        foreach (DarkPrinterList list in Descendants(form).OfType<DarkPrinterList>())
                        {
                            list.Items.Add(new PrinterItem { Name="Stampante ufficio – Caffè & copie",IsDefault=true });
                            list.Items.Add(new PrinterItem { Name=@"\\server\Stampante reparto",IsConnection=true });
                            list.SelectedIndex=0;
                        }
                        form.ShowInTaskbar=false;
                        form.Show();
                        Application.DoEvents();
                        IntPtr handle=form.Handle;
                        form.PerformLayout();
                        Application.DoEvents();
                        if (scale!=1F) form.Scale(new SizeF(scale,scale));
                        form.PerformLayout();
                        foreach (Control child in Descendants(form)) child.PerformLayout();
                        foreach (FlowLayoutPanel panel in Descendants(form).OfType<FlowLayoutPanel>()) panel.AutoScrollPosition=Point.Empty;
                        foreach (StackPanel panel in Descendants(form).OfType<StackPanel>()) panel.ScrollTo(0);
                        form.Update();
                        CaptureForm(form,Path.Combine(folder,String.Format("{0:00}-{1:0}.png",i,scale*100)));
                        if (form is SecurityDialog || (form is MenuDialog && i==11))
                        {
                            foreach (StackPanel panel in Descendants(form).OfType<StackPanel>()) panel.ScrollTo(Int32.MaxValue);
                            form.Update(); CaptureForm(form,Path.Combine(folder,String.Format("{0:00}-bottom-{1:0}.png",i,scale*100)));
                        }
                        Assert(form.ClientSize.Width>500,"Form troppo stretto.");
                    }
                }
            }
            Console.WriteLine("Rendering: " + folder);
        }
        private static void CaptureForm(Form form,string path)
        {
            using (Bitmap bitmap=new Bitmap(form.Width,form.Height))
            {
                using (Graphics graphics=Graphics.FromImage(bitmap))
                {
                    IntPtr dc=graphics.GetHdc();
                    try { Assert(PrintWindow(form.Handle,dc,2),"Cattura della finestra di test fallita."); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                bitmap.Save(path);
            }
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CommandLineToArgvW(string command,out int count);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr FindResource(IntPtr module,IntPtr name,IntPtr type);
        [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module,IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
        private delegate bool WindowCallback(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread,WindowCallback callback,IntPtr parameter);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int length);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        // Finestre tooltip di Windows visibili nel thread dell'interfaccia.
        private static int VisibleTooltips()
        {
            int count=0;
            EnumThreadWindows(GetCurrentThreadId(),delegate(IntPtr window,IntPtr parameter) {
                StringBuilder name=new StringBuilder(256); GetClassName(window,name,name.Capacity);
                // WinForms registra la classe con un prefisso, per esempio WindowsForms10.tooltips_class32.app...
                if (name.ToString().Contains("tooltips_class32") && IsWindowVisible(window)) count++;
                return true;
            },IntPtr.Zero);
            return count;
        }
        [DllImport("user32.dll",EntryPoint="SendMessageW")] private static extern IntPtr ScrollPosition(IntPtr window,int message,IntPtr wParam,ref Point point);
    }
    internal sealed class FakeRegistry : IRegistryStore
    {
        private readonly Dictionary<string,Dictionary<string,RegistryValueState>> keys=new Dictionary<string,Dictionary<string,RegistryValueState>>(StringComparer.OrdinalIgnoreCase);
        public bool FailSet, IgnoreSet;
        public void Ensure(string path)
        {
            if (keys.ContainsKey(path)) return;
            keys.Add(path,new Dictionary<string,RegistryValueState>(StringComparer.OrdinalIgnoreCase));
            int slash=path.LastIndexOf('\\'); if (slash>0) Ensure(path.Substring(0,slash));
        }
        public RegistryValueState Read(string path,string name)
        {
            Dictionary<string,RegistryValueState> values; RegistryValueState value;
            if (!keys.TryGetValue(path,out values)) return new RegistryValueState();
            if (!values.TryGetValue(name,out value)) return new RegistryValueState { KeyExists=true };
            return new RegistryValueState { KeyExists=true,ValueExists=true,Data=value.Data,Kind=value.Kind };
        }
        public void Set(string path,string name,object data,RegistryValueKind kind)
        {
            if (FailSet) throw new IOException("Errore scrittura simulato.");
            if (IgnoreSet) return;
            Ensure(path); keys[path][name]=new RegistryValueState { KeyExists=true,ValueExists=true,Data=data,Kind=kind };
        }
        public void DeleteValue(string path,string name) { if (keys.ContainsKey(path)) keys[path].Remove(name); }
        public void DeleteKeyIfEmpty(string path)
        {
            if (keys.ContainsKey(path) && keys[path].Count==0 && !keys.Keys.Any(k=>k.StartsWith(path+"\\",StringComparison.OrdinalIgnoreCase))) keys.Remove(path);
        }
        public void Notify() { }
    }
    internal sealed class FakeSpool : ISpoolBackend
    {
        public readonly List<string> Configured=new List<string>(), Entries=new List<string>(), Deleted=new List<string>(), Calls=new List<string>();
        public bool PlainDirectory=true, PlainFile=true, FailStop, FailStart, ChangeOnStop;
        public string FailDelete;
        public string StandardDirectory { get { return @"C:\Windows\System32\spool\PRINTERS"; } }
        public string PathOf(string name) { return Path.Combine(StandardDirectory,name); }
        public IEnumerable<string> ConfiguredDirectories() { return Configured; }
        public bool DirectoryIsPlain(string path) { return PlainDirectory; }
        public IEnumerable<string> Files(string path) { return Entries; }
        public bool FileIsPlain(string path) { return PlainFile; }
        public void DeleteFile(string path)
        { if (Path.GetFileName(path)==FailDelete) throw new IOException("file bloccato"); Deleted.Add(path); Calls.Add("delete"); }
        public void Stop() { Calls.Add("stop"); if (ChangeOnStop) Configured.Add(@"C:\changed"); if (FailStop) throw new IOException("stop simulato"); }
        public void Start() { Calls.Add("start"); if (FailStart) throw new IOException("avvio simulato"); }
        public string Status() { return "fake"; }
    }
}
