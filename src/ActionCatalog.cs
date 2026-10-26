using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;

namespace OnlyTweakWindows
{
    internal sealed class MenuDefinition
    {
        // Description è il sottotitolo breve; Detail contiene avvertenze e contesto mostrati nel tooltip Info.
        public string Title, Description, Glyph, Detail;
        public readonly List<ActionSpec> Actions = new List<ActionSpec>();
        public readonly List<MenuDefinition> Children = new List<MenuDefinition>();
        public MenuDefinition(string title, string description, string glyph, string detail) { Title=title; Description=description; Glyph=glyph; Detail=detail; }
        public void Prune()
        {
            Actions.RemoveAll(a=>!a.Applicable);
            foreach (MenuDefinition child in Children) child.Prune();
            Children.RemoveAll(c=>!System.Linq.Enumerable.Any(c.AllActions()));
        }
        public IEnumerable<ActionSpec> AllActions()
        {
            foreach (ActionSpec action in Actions) yield return action;
            foreach (MenuDefinition child in Children) foreach (ActionSpec action in child.AllActions()) yield return action;
        }
    }
    internal sealed class ActionCatalog
    {
        private readonly AppServices app;
        public readonly MenuDefinition Network, Maintenance, Tools, Spool;
        public readonly ActionSpec ClassicPrinterView;
        public ActionCatalog(AppServices app)
        {
            this.app=app;
            ClassicPrinterView=Target("printers.classic","Dispositivi e stampanti — vista classica","Apre la vista classica disponibile su questa build. In alternativa usa Stampanti e scanner.",ProcessService.ClassicPrinters);
            Spool = new MenuDefinition("Spooler di stampa", "Stato, riavvio e pulizia delle code locali.", Glyphs.Print, "Interventi sul servizio locale. La pulizia globale elimina i lavori di tutte le stampanti locali, anche di altri utenti.");
            Spool.Actions.Add(new ActionSpec("spool.status", "Leggi stato spooler", "Legge lo stato del servizio di stampa senza modificarlo.", "Computer locale", delegate(Action<string> log) { log("Spooler: " + app.Spool.Status()); }));
            Spool.Actions.Add(new ActionSpec("spool.restart", "Riavvia spooler", "Arresta e riavvia il servizio senza cancellare i file delle code. Il tipo di avvio resta quello configurato.", "Computer locale", app.Spool.Restart) {
                RequiresElevation=true, Confirmation="Le stampe in corso possono interrompersi. Il servizio verrà avviato anche se inizialmente fermo. Procedere?" });
            Spool.Actions.Add(new ActionSpec("spool.clean", "Svuota tutte le code locali", "Arresta lo spooler, elimina solo i file .SPL/.SHD dalla cartella standard e tenta sempre il riavvio. Non rimuove stampanti o driver.", "Tutte le code locali / tutti gli utenti", app.Spool.Clean) {
                RequiresElevation=true, Confirmation="Tutti i lavori nelle code locali saranno eliminati definitivamente, inclusi quelli di altri utenti. Non è possibile annullare. Procedere?",
                Unavailable=app.Spool.CleanupUnavailable });
            Network = new MenuDefinition("Rete", "Diagnostica, ripristino e impostazioni di rete.", Glyphs.Network, null);
            MenuDefinition networkInfo = new MenuDefinition("Informazioni di rete", "Lettura delle interfacce e della configurazione attuale.", Glyphs.Diagnostic, null);
            networkInfo.Actions.Add(new ActionSpec("network.info", "Schede, indirizzi IP e DNS", "Elenca interfacce, stato, indirizzi IP, gateway e DNS.", "Computer locale · sola lettura", ProcessService.ReadNetwork));
            networkInfo.Actions.Add(Command("network.ipconfig", "Configurazione completa", "Mostra l'output di ipconfig /all.", "ipconfig.exe", "/all", false));
            Network.Children.Add(networkInfo);
            MenuDefinition resets = new MenuDefinition("Ripristino rete", "Reset indipendenti della rete.", Glyphs.Repair, "Ogni intervento è indipendente. I reset possono interrompere la connessione e richiedere il riavvio manuale.");
            resets.Actions.Add(Command("network.dns", "Svuota cache DNS", "Elimina la cache locale delle risoluzioni DNS. Non cambia i server DNS configurati.", "ipconfig.exe", "/flushdns", false));
            resets.Actions[0].Confirmation="La cache DNS del computer verrà svuotata. Procedere?";
            ActionSpec winsock = Command("network.winsock", "Ripristina Winsock", "Ripristina il catalogo Winsock tramite netsh winsock reset.", "netsh.exe", "winsock reset", false);
            winsock.Confirmation="Il reset può interrompere la rete e influire su software VPN o filtri di rete. Sarà necessario riavviare manualmente il PC. Procedere?";
            winsock.Restart="Riavvia manualmente il PC per completare il reset Winsock.";
            resets.Actions.Add(winsock);
            ActionSpec tcp = Command("network.tcpip", "Ripristina TCP/IP", "Esegue netsh int ip reset. Può rimuovere configurazioni IP personalizzate.", "netsh.exe", "int ip reset", false);
            tcp.Confirmation="La connessione può interrompersi; annota prima IP statici, gateway e DNS personalizzati. Queste impostazioni potrebbero dover essere riconfigurate. È richiesto un riavvio manuale. Procedere?";
            tcp.Restart="Riavvia manualmente il PC e verifica la configurazione IP/DNS.";
            resets.Actions.Add(tcp);
            Network.Children.Add(resets);
            MenuDefinition networkSettings = new MenuDefinition("Impostazioni rete", "Strumenti di Windows, senza modifiche automatiche.", Glyphs.Settings, null);
            networkSettings.Actions.Add(SystemTool("network.connections", "Connessioni di rete", "Gestione delle schede e dei protocolli di rete.", "control.exe", "ncpa.cpl"));
            networkSettings.Actions.Add(Settings("network.proxy", "Impostazioni proxy", "Configurazione proxy dell'account.", "ms-settings:network-proxy"));
            Network.Children.Add(networkSettings);

            Maintenance = new MenuDefinition("Manutenzione Windows", "Verifiche, riparazioni e spazio su disco.", Glyphs.Repair, "Controlli e riparazioni separati. Durante l'esecuzione l'app resta aperta; non è previsto l'arresto forzato delle riparazioni.");
            MenuDefinition checks = new MenuDefinition("Verifica e riparazione", "Controlli SFC e DISM.", Glyphs.Diagnostic, "SFC e DISM possono richiedere diversi minuti. Leggi sempre il riepilogo dello strumento.");
            checks.Actions.Add(Command("sfc.verify", "SFC · verifica", "Controlla i file protetti di Windows senza ripararli (sfc /verifyonly).", "sfc.exe", "/verifyonly", true));
            ActionSpec sfc = Command("sfc.repair", "SFC · ripara", "Controlla e ripara i file protetti (sfc /scannow). Se DISM rileva problemi, esegui prima la sua riparazione e poi SFC.", "sfc.exe", "/scannow", true);
            sfc.Confirmation="SFC può sostituire file di sistema danneggiati. Lascia completare l'operazione. Procedere?";
            checks.Actions.Add(sfc);
            checks.Actions.Add(Command("dism.scan", "DISM · analizza componenti", "Analizza l'archivio componenti Windows (DISM /Online /Cleanup-Image /ScanHealth).", "dism.exe", "/Online /Cleanup-Image /ScanHealth /NoRestart", false));
            ActionSpec dism = Command("dism.repair", "DISM · ripara componenti", "Ripara l'archivio componenti. Può scaricare file tramite Windows Update: può essere necessario Internet. Non riavvia automaticamente il PC.", "dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart", false);
            dism.Confirmation="DISM può modificare i componenti di Windows e scaricare file di riparazione. Lascia completare l'operazione. Procedere?";
            checks.Actions.Add(dism);
            Maintenance.Children.Add(checks);
            MenuDefinition cleanup = new MenuDefinition("Spazio su disco", "La scelta di cosa eliminare resta negli strumenti Windows.", Glyphs.Delete, null);
            cleanup.Actions.Add(SystemTool("storage.cleanup", "Apri Pulizia disco", "Apre Pulizia disco: seleziona lì gli elementi da rimuovere.", "cleanmgr.exe", ""));
            cleanup.Actions.Add(Settings("storage.settings", "Impostazioni archiviazione", "Spazio occupato, file temporanei e Sensore memoria.", "ms-settings:storagesense"));
            Maintenance.Children.Add(cleanup);

            Tools = new MenuDefinition("Strumenti Windows", "Strumenti di sistema in finestre separate.", Glyphs.Apps, "Accesso diretto agli strumenti di sistema. Le modifiche si eseguono nelle rispettive finestre.");
            Tools.Actions.Add(ConsoleTool("tools.computer","Gestione computer","Dischi, eventi, servizi e strumenti amministrativi.","compmgmt.msc"));
            Tools.Actions.Add(SystemTool("tools.control","Pannello di controllo","Apre il Pannello di controllo classico.","control.exe",""));
            Tools.Actions.Add(SystemTool("tools.programs","Programmi e funzionalità","Gestione classica delle applicazioni desktop. Per le altre app usa Applicazioni installate.","control.exe","appwiz.cpl"));
            Tools.Actions.Add(ConsoleTool("tools.devices", "Gestione dispositivi", "Dispositivi, driver e stato hardware.","devmgmt.msc"));
            Tools.Actions.Add(ConsoleTool("tools.services", "Servizi", "Gestione dei servizi Windows.","services.msc"));
            Tools.Actions.Add(ConsoleTool("tools.events", "Visualizzatore eventi", "Registri di sistema e applicazioni.","eventvwr.msc"));
            Tools.Actions.Add(SystemTool("tools.tasks", "Gestione attività", "Processi, prestazioni e applicazioni.", "Taskmgr.exe", ""));
            Tools.Actions.Add(Settings("tools.apps", app.OS.IsWindows11 ? "Applicazioni installate" : "App e funzionalità", "Elenco e gestione delle applicazioni.", "ms-settings:appsfeatures"));
            Tools.Actions.Add(Settings("tools.startup", "Applicazioni all'avvio", "Gestione dei programmi avviati all'accesso.", "ms-settings:startupapps"));
            Tools.Actions.Add(Settings("tools.update", "Windows Update", "Stato aggiornamenti e opzioni di Windows Update.", "ms-settings:windowsupdate"));
            Tools.Actions.Add(SystemTool("tools.system", "Informazioni di sistema", "Configurazione hardware e software del PC.", "msinfo32.exe", ""));
            foreach (ActionSpec action in Spool.Actions) {
                action.IsApplicable=delegate { return app.Availability.File("spoolsv.exe"); };
                action.IsDiagnostic=LocalOperationExecutor.IsDiagnostic(action.Id);
                bool clean=action.Id=="spool.clean";
                action.Observe=(log,token)=>app.Spool.Observe(clean);
            }
            networkInfo.Actions[0]=Command("network.info","Schede, indirizzi IP e DNS","Elenca interfacce, indirizzi IP, gateway e DNS tramite ipconfig /all.","ipconfig.exe","/all",false);
            Spool.Actions[0].ExecuteResult=(log,token)=> {
                NativeObservation status=app.Processes.RunObserved("sc.exe","query Spooler",log,false,true,token,60);
                return new StepResult {State=status.ExitCode==0 && !status.Truncated && !String.IsNullOrWhiteSpace(status.Output) ? StepState.Success : StepState.Inconclusive,
                    Truncated=status.Truncated,Data=JsonSerializer.SerializeToElement(status)};
            };
            MenuDefinition diagnostics=new MenuDefinition("Diagnostica tecnica","Test dalla postazione corrente, senza modifiche.",Glyphs.Diagnostic,null);
            foreach(var entry in new[] {("local.ping","Ping","Quattro richieste ICMP verso un FQDN."),("local.dns","Risoluzione DNS","Query DNS A, AAAA o CNAME."),
                ("local.tcp","Test porta TCP","Connessione verso una singola porta."),("local.connections","Connessioni locali","Connessioni e porte con PID."),
                ("local.routes","Tabella di routing","Route IPv4 e IPv6."),("local.proxy","Proxy WinHTTP","Configurazione proxy WinHTTP in lettura.")})
                diagnostics.Actions.Add(LocalDiagnostics.Entry(app,entry.Item1,entry.Item2,entry.Item3));
            Network.Children.Add(diagnostics);
            Tools.Actions.Add(LocalDiagnostics.Entry(app,"local.identity","Identità e gruppi","Account, gruppi e privilegi dell'app."));
            Tools.Actions.Add(LocalDiagnostics.Entry(app,"local.gpo","Criteri utente applicati","Riepilogo gpresult per l'utente dell'app."));
            ApplySummaries();
            foreach (MenuDefinition menu in new[] { Spool,Network,Maintenance,Tools }) menu.Prune();
        }
        // Righe brevi delle card; il testo completo resta in Description (tooltip, accessibilità, dettaglio operazione).
        private static readonly Dictionary<string,string> Summaries=new Dictionary<string,string> {
            { "spool.status","Stato del servizio, senza modifiche." },{ "spool.restart","Riavvia il servizio senza cancellare le code." },
            { "spool.clean","Elimina i lavori di tutte le code locali." },
            { "network.info","Interfacce, IP, gateway e DNS." },{ "network.ipconfig","Output di ipconfig /all." },
            { "network.dns","Svuota la cache locale delle risoluzioni." },{ "network.winsock","netsh winsock reset; richiede il riavvio." },
            { "network.tcpip","netsh int ip reset; richiede il riavvio." },{ "network.connections","Schede e protocolli di rete." },
            { "network.proxy","Proxy dell'account." },{ "sfc.verify","Controlla i file protetti senza ripararli." },
            { "sfc.repair","Ripara i file protetti (sfc /scannow)." },{ "dism.scan","Analizza l'archivio componenti." },
            { "dism.repair","Ripara l'archivio componenti." },{ "storage.cleanup","Scegli in Windows cosa eliminare." },
            { "storage.settings","Spazio, file temporanei e Sensore memoria." },{ "tools.computer","Dischi, eventi e servizi." },
            { "tools.control","Pannello di controllo classico." },{ "tools.programs","App desktop, vista classica." },
            { "tools.devices","Dispositivi e driver." },{ "tools.services","Servizi di Windows." },{ "tools.events","Registri di sistema e app." },
            { "tools.tasks","Processi e prestazioni." },{ "tools.apps","Elenco e gestione delle app." },{ "tools.startup","Programmi avviati all'accesso." },
            { "tools.update","Aggiornamenti di Windows." },{ "tools.system","Hardware e software del PC." },
        };
        private void ApplySummaries()
        {
            List<ActionSpec> actions=new List<ActionSpec>();
            foreach (MenuDefinition menu in new[] { Spool,Network,Maintenance,Tools }) actions.AddRange(menu.AllActions());
            foreach (ActionSpec action in actions) { string summary; if (Summaries.TryGetValue(action.Id,out summary)) action.Summary=summary; }
        }
        public ActionSpec ConsoleTool(string id,string title,string description,string console)
        {
            ActionSpec action=SystemTool(id,title,description,"mmc.exe",ProcessService.QuoteArgument(app.Processes.SystemFile(console)));
            action.Unavailable=delegate { return app.Processes.FileUnavailable("mmc.exe")??app.Processes.FileUnavailable(console); };
            action.IsApplicable=delegate { return app.Availability.File("mmc.exe") && app.Availability.File(console); }; return action;
        }
        public ActionSpec Target(string id,string title,string description,string target)
        { return new ActionSpec(id,title,description,"Utente della sessione desktop",delegate(Action<string> log) { app.Processes.LaunchTarget(app.Availability.ResolveTarget(target),log); })
            { OpensWindow=true,IsApplicable=delegate { return app.Availability.Target(target); },Unavailable=delegate { return app.Processes.TargetUnavailable(app.Availability.ResolveTarget(target)); } }; }
        private ActionSpec Command(string id, string title, string description, string file, string arguments, bool unicode)
        {
            ActionSpec action=new ActionSpec(id,title,description,"Computer locale",log=>app.Processes.Run(file,arguments,log,unicode)) {
                RequiresElevation=id!="network.ipconfig" && id!="network.info",IsDiagnostic=LocalOperationExecutor.IsDiagnostic(id),TimeoutSeconds=id.StartsWith("sfc.") || id.StartsWith("dism.") ? 1800 : 60,
                IsApplicable=()=>app.Availability.File(file),Unavailable=()=>app.Processes.FileUnavailable(file)
            };
            action.ExecuteResult=(log,token)=> {
                NativeObservation native=app.Processes.RunObserved(file,arguments,log,unicode,action.IsDiagnostic,token,action.TimeoutSeconds);
                return new StepResult {State=(native.ExitCode==0 || (!action.IsDiagnostic && native.ExitCode==3010)) && !native.Truncated ? StepState.Success : StepState.Inconclusive,
                    Truncated=native.Truncated,Data=JsonSerializer.SerializeToElement(new {NativeExitCode=native.ExitCode,Output=native.Output,RestartRequired=native.ExitCode==3010})};
            };
            action.Observe=(log,token)=> {
                (string readFile,string readArgs,bool readUnicode)=id switch {
                    "network.dns"=>("ipconfig.exe","/displaydns",false),"network.winsock"=>("netsh.exe","winsock show catalog",false),
                    "network.tcpip"=>("ipconfig.exe","/all",false),"sfc.repair"=>("sfc.exe","/verifyonly",true),
                    "dism.repair"=>("dism.exe","/Online /Cleanup-Image /CheckHealth /NoRestart",false),_=>(file,arguments,unicode)
                };
                NativeObservation read=app.Processes.RunObserved(readFile,readArgs,log,readUnicode,true,token,action.TimeoutSeconds);
                return new LocalObservation(read.ExitCode==0 && !read.Truncated && !String.IsNullOrWhiteSpace(read.Output),null,read);
            };
            return action;
        }
        public ActionSpec SystemTool(string id, string title, string description, string file, string arguments)
        {
            return new ActionSpec(id, title, description, "Strumento Windows esterno", delegate(Action<string> log) { app.Processes.LaunchSystem(file, arguments, log); })
                { OpensWindow=true, IsApplicable=delegate { return app.Availability.File(file) && (!arguments.EndsWith(".cpl",StringComparison.OrdinalIgnoreCase) || app.Availability.File(arguments)); },Unavailable=delegate { return app.Processes.FileUnavailable(file)??(arguments.EndsWith(".cpl",StringComparison.OrdinalIgnoreCase) ? app.Processes.FileUnavailable(arguments) : null); } };
        }
        public ActionSpec Settings(string id, string title, string description, string uri)
        { return Target(id,title,description,uri); }
        public ActionSpec PrinterDialog(string mode, string title, string printer)
        {
            return new ActionSpec("printer" + mode, title, "Apre il dialogo Windows per: " + printer, "Stampante selezionata · account dell'app",
                delegate(Action<string> log) { app.Processes.PrintUi(mode, printer, log); }) { OpensWindow=true,IsApplicable=delegate { return app.Availability.File("rundll32.exe") && app.Availability.File("printui.dll"); } };
        }
        public ActionSpec PurgePrinter(string printer)
        {
            return new ActionSpec("printer.purge", "Annulla lavori della selezionata", "Richiede l'annullamento dei lavori nella sola coda selezionata, senza fermare lo spooler locale.", printer,
                delegate(Action<string> log) { app.Printers.Purge(printer, log); }) {
                    Observe=(log,token)=>app.Printers.ObserveQueue(printer),
                    Confirmation="Eliminare definitivamente tutti i lavori nella coda di «" + printer + "»? Per stampanti condivise possono essere coinvolti lavori di altri utenti; servono i permessi sul server. Un lavoro già in stampa può proseguire." };
        }
    }
}
