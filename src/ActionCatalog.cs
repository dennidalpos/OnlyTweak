using System;
using System.Collections.Generic;

namespace OnlyTweakWindows
{
    internal sealed class MenuDefinition
    {
        public string Title, Description;
        public readonly List<ActionSpec> Actions = new List<ActionSpec>();
        public readonly List<MenuDefinition> Children = new List<MenuDefinition>();
        public MenuDefinition(string title, string description) { Title=title; Description=description; }
    }
    internal sealed class ActionCatalog
    {
        private readonly AppServices app;
        public readonly MenuDefinition Network, Maintenance, Tools, Spool, Accounts;
        public readonly ActionSpec ClassicPrinterView;
        public readonly ActionSpec ExplorerRestart;
        public ActionCatalog(AppServices app)
        {
            this.app=app;
            ClassicPrinterView=Target("printers.classic","Dispositivi e stampanti — vista classica","Apre la vista classica disponibile su questa build. In alternativa usa Stampanti e scanner.",ProcessService.ClassicPrinters);
            Spool = new MenuDefinition("Spooler di stampa", "Interventi sul servizio locale. La pulizia globale elimina i lavori di tutte le stampanti locali, anche di altri utenti.");
            Spool.Actions.Add(new ActionSpec("spool.status", "Leggi stato spooler", "Legge lo stato del servizio di stampa senza modificarlo.", "Computer locale", delegate(Action<string> log) { log("Spooler: " + app.Spool.Status()); }));
            Spool.Actions.Add(new ActionSpec("spool.restart", "Riavvia spooler", "Arresta e riavvia il servizio senza cancellare i file delle code. Il tipo di avvio resta quello configurato.", "Computer locale", app.Spool.Restart) {
                Confirmation="Le stampe in corso possono interrompersi. Il servizio verrà avviato anche se inizialmente fermo. Procedere?" });
            Spool.Actions.Add(new ActionSpec("spool.clean", "Svuota tutte le code locali", "Arresta lo spooler, elimina solo i file .SPL/.SHD dalla cartella standard e tenta sempre il riavvio. Non rimuove stampanti o driver.", "Tutte le code locali / tutti gli utenti", app.Spool.Clean) {
                Confirmation="Tutti i lavori nelle code locali saranno eliminati definitivamente, inclusi quelli di altri utenti. Non è possibile annullare. Procedere?",
                Unavailable=app.Spool.CleanupUnavailable });
            ExplorerRestart = new ActionSpec("explorer.restart", "Riavvia Esplora risorse", "Riavvia la shell del desktop nella sessione corrente con il suo token originale. Disponibile solo se la shell appartiene all'account dell'app.", "Desktop dell'account corrente", ShellService.Restart) {
                Confirmation="La barra delle applicazioni scomparirà temporaneamente e le finestre della shell potranno chiudersi. Completa prima copie e spostamenti. Procedere?",
                Unavailable=ShellService.Unavailable };

            Network = new MenuDefinition("Rete", "Diagnostica, interventi separati e collegamenti alle impostazioni di rete.");
            MenuDefinition networkInfo = new MenuDefinition("Informazioni di rete", "Lettura delle interfacce e della configurazione attuale.");
            networkInfo.Actions.Add(new ActionSpec("network.info", "Schede, indirizzi IP e DNS", "Elenca interfacce, stato, indirizzi IP, gateway e DNS.", "Computer locale · sola lettura", ProcessService.ReadNetwork));
            networkInfo.Actions.Add(Command("network.ipconfig", "Configurazione completa", "Mostra l'output di ipconfig /all.", "ipconfig.exe", "/all", false));
            Network.Children.Add(networkInfo);
            MenuDefinition resets = new MenuDefinition("Ripristino rete", "Ogni intervento è indipendente. I reset possono interrompere la connessione e richiedere il riavvio manuale.");
            resets.Actions.Add(Command("network.dns", "Svuota cache DNS", "Elimina la cache locale delle risoluzioni DNS. Non cambia i server DNS configurati.", "ipconfig.exe", "/flushdns", false));
            ActionSpec winsock = Command("network.winsock", "Ripristina Winsock", "Ripristina il catalogo Winsock tramite netsh winsock reset.", "netsh.exe", "winsock reset", false);
            winsock.Confirmation="Il reset può interrompere la rete e influire su software VPN o filtri di rete. Sarà necessario riavviare manualmente il PC. Procedere?";
            winsock.Restart="Riavvia manualmente il PC per completare il reset Winsock.";
            resets.Actions.Add(winsock);
            ActionSpec tcp = Command("network.tcpip", "Ripristina TCP/IP", "Esegue netsh int ip reset. Può rimuovere configurazioni IP personalizzate.", "netsh.exe", "int ip reset", false);
            tcp.Confirmation="La connessione può interrompersi; annota prima IP statici, gateway e DNS personalizzati. Queste impostazioni potrebbero dover essere riconfigurate. È richiesto un riavvio manuale. Procedere?";
            tcp.Restart="Riavvia manualmente il PC e verifica la configurazione IP/DNS.";
            resets.Actions.Add(tcp);
            Network.Children.Add(resets);
            MenuDefinition networkSettings = new MenuDefinition("Impostazioni rete", "Apre gli strumenti di Windows senza applicare modifiche automaticamente.");
            networkSettings.Actions.Add(SystemTool("network.connections", "Connessioni di rete", "Gestione delle schede e dei protocolli di rete.", "control.exe", "ncpa.cpl"));
            networkSettings.Actions.Add(Settings("network.proxy", "Impostazioni proxy", "Configurazione proxy dell'account.", "ms-settings:network-proxy"));
            Network.Children.Add(networkSettings);

            Maintenance = new MenuDefinition("Manutenzione Windows", "Controlli e riparazioni separati. Durante l'esecuzione l'app resta aperta; non è previsto l'arresto forzato delle riparazioni.");
            MenuDefinition checks = new MenuDefinition("Verifica e riparazione", "SFC e DISM possono richiedere diversi minuti. Leggi sempre il riepilogo dello strumento.");
            checks.Actions.Add(Command("sfc.verify", "SFC · verifica", "Controlla i file protetti di Windows senza ripararli (sfc /verifyonly).", "sfc.exe", "/verifyonly", true));
            ActionSpec sfc = Command("sfc.repair", "SFC · ripara", "Controlla e ripara i file protetti (sfc /scannow). Se DISM rileva problemi, esegui prima la sua riparazione e poi SFC.", "sfc.exe", "/scannow", true);
            sfc.Confirmation="SFC può sostituire file di sistema danneggiati. Lascia completare l'operazione. Procedere?";
            checks.Actions.Add(sfc);
            checks.Actions.Add(Command("dism.scan", "DISM · analizza componenti", "Analizza l'archivio componenti Windows (DISM /Online /Cleanup-Image /ScanHealth).", "dism.exe", "/Online /Cleanup-Image /ScanHealth /NoRestart", false));
            ActionSpec dism = Command("dism.repair", "DISM · ripara componenti", "Ripara l'archivio componenti. Può scaricare file tramite Windows Update: può essere necessario Internet. Non riavvia automaticamente il PC.", "dism.exe", "/Online /Cleanup-Image /RestoreHealth /NoRestart", false);
            dism.Confirmation="DISM può modificare i componenti di Windows e scaricare file di riparazione. Lascia completare l'operazione. Procedere?";
            checks.Actions.Add(dism);
            Maintenance.Children.Add(checks);
            MenuDefinition cleanup = new MenuDefinition("Spazio su disco", "La scelta di cosa eliminare resta negli strumenti Windows.");
            cleanup.Actions.Add(SystemTool("storage.cleanup", "Apri Pulizia disco", "Apre Pulizia disco: seleziona lì gli elementi da rimuovere.", "cleanmgr.exe", ""));
            cleanup.Actions.Add(Settings("storage.settings", "Impostazioni archiviazione", "Spazio occupato, file temporanei e Sensore memoria.", "ms-settings:storagesense"));
            Maintenance.Children.Add(cleanup);

            Tools = new MenuDefinition("Strumenti Windows", "Accesso diretto agli strumenti di sistema. Le modifiche si eseguono nelle rispettive finestre.");
            Tools.Actions.Add(ConsoleTool("tools.computer","Gestione computer","Dischi, eventi, servizi e strumenti amministrativi.","compmgmt.msc"));
            Tools.Actions.Add(SystemTool("tools.control","Pannello di controllo","Apre il Pannello di controllo classico.","control.exe",""));
            Tools.Actions.Add(SystemTool("tools.programs","Programmi e funzionalità","Gestione classica delle applicazioni desktop. Per le altre app usa Applicazioni installate.","control.exe","appwiz.cpl"));
            Tools.Actions.Add(ConsoleTool("tools.devices", "Gestione dispositivi", "Dispositivi, driver e stato hardware.","devmgmt.msc"));
            Tools.Actions.Add(ConsoleTool("tools.services", "Servizi", "Gestione dei servizi Windows.","services.msc"));
            Tools.Actions.Add(ConsoleTool("tools.events", "Visualizzatore eventi", "Registri di sistema e applicazioni.","eventvwr.msc"));
            Tools.Actions.Add(SystemTool("tools.tasks", "Gestione attività", "Processi, prestazioni e applicazioni.", "Taskmgr.exe", ""));
            Tools.Actions.Add(Settings("tools.apps", "Applicazioni installate", "Elenco e gestione delle applicazioni.", "ms-settings:appsfeatures"));
            Tools.Actions.Add(Settings("tools.startup", "Applicazioni all'avvio", "Gestione dei programmi avviati all'accesso.", "ms-settings:startupapps"));
            Tools.Actions.Add(Settings("tools.update", "Windows Update", "Stato aggiornamenti e opzioni di Windows Update.", "ms-settings:windowsupdate"));
            Tools.Actions.Add(SystemTool("tools.system", "Informazioni di sistema", "Configurazione hardware e software del PC.", "msinfo32.exe", ""));
            Accounts=new MenuDefinition("Account","Passaggio ad account locale, collegamenti e utenti del PC si gestiscono in Windows. Rimuovere un utente dal PC può eliminare i suoi dati locali; la chiusura online riguarda l'intero account Microsoft.");
            Accounts.Actions.Add(Settings("accounts.info","Le tue informazioni / passa ad account locale","Apri Le tue informazioni e scegli Accedi con un account locale. Conserva un metodo di accesso al PC.","ms-settings:yourinfo"));
            Accounts.Actions.Add(Settings("accounts.apps","Account usati dalle app","Gestisci o rimuovi i collegamenti usati da posta e altre applicazioni.","ms-settings:emailandaccounts"));
            Accounts.Actions.Add(Settings("accounts.work","Accesso lavoro o scuola","Gestisci e scollega account aziendali o scolastici nella procedura Windows.","ms-settings:workplace"));
            Accounts.Actions.Add(Settings("accounts.users","Altri utenti del PC","Aggiungi o rimuovi utenti. La rimozione dal PC non chiude l'account Microsoft online.","ms-settings:otherusers"));
            Accounts.Actions.Add(SystemTool("accounts.classic","Account utente — vista classica","Gestione classica degli utenti del computer.","netplwiz.exe",""));
            ActionSpec local=ConsoleTool("accounts.local","Utenti e gruppi locali","Proprietà e disabilitazione degli utenti locali; disponibile nelle edizioni che includono questa console.","lusrmgr.msc");
            Func<string> localCheck=local.Unavailable;
            local.Unavailable=delegate { return WindowsSecurityBackend.PolicyEdition ? localCheck() : "Questa edizione non include Utenti e gruppi locali. Usa Altri utenti del PC."; };
            Accounts.Actions.Add(local);
            Accounts.Actions.Add(Target("accounts.portal","Portale account Microsoft","Gestisci profilo, dispositivi e servizi. Verifica l'account connesso nel browser.","https://account.microsoft.com/"));
            Accounts.Actions.Add(Target("accounts.close","Procedura di chiusura dell'account Microsoft","Apre la guida ufficiale alla chiusura online: riguarda anche i servizi e i dati associati. Accesso, scelta dell'account e conferma finale avvengono sul sito Microsoft.","https://support.microsoft.com/it-it/accounts-billing/manage/how-to-close-your-microsoft-account"));
        }
        public ActionSpec ConsoleTool(string id,string title,string description,string console)
        {
            ActionSpec action=SystemTool(id,title,description,"mmc.exe",ProcessService.QuoteArgument(app.Processes.SystemFile(console)));
            action.Unavailable=delegate { return app.Processes.FileUnavailable("mmc.exe")??app.Processes.FileUnavailable(console); }; return action;
        }
        public ActionSpec Target(string id,string title,string description,string target)
        { return new ActionSpec(id,title,description,"Utente della sessione desktop",delegate(Action<string> log) { app.Processes.LaunchTarget(target,log); })
            { OpensWindow=true,Unavailable=delegate { return app.Processes.TargetUnavailable(target); } }; }
        private ActionSpec Command(string id, string title, string description, string file, string arguments, bool unicode)
        {
            return new ActionSpec(id, title, description, "Computer locale", delegate(Action<string> log) { app.Processes.Run(file, arguments, log, unicode); })
                { Unavailable=delegate { return app.Processes.FileUnavailable(file); } };
        }
        public ActionSpec SystemTool(string id, string title, string description, string file, string arguments)
        {
            return new ActionSpec(id, title, description, "Strumento Windows esterno", delegate(Action<string> log) { app.Processes.LaunchSystem(file, arguments, log); })
                { OpensWindow=true, Unavailable=delegate { return app.Processes.FileUnavailable(file)??(arguments.EndsWith(".cpl",StringComparison.OrdinalIgnoreCase) ? app.Processes.FileUnavailable(arguments) : null); } };
        }
        public ActionSpec Settings(string id, string title, string description, string uri)
        { return Target(id,title,description,uri); }
        public ActionSpec PrinterDialog(string mode, string title, string printer)
        {
            return new ActionSpec("printer" + mode, title, "Apre il dialogo Windows per: " + printer, "Stampante selezionata · account dell'app",
                delegate(Action<string> log) { app.Processes.PrintUi(mode, printer, log); }) { OpensWindow=true };
        }
        public ActionSpec PurgePrinter(string printer)
        {
            return new ActionSpec("printer.purge", "Annulla lavori della selezionata", "Richiede l'annullamento dei lavori nella sola coda selezionata, senza fermare lo spooler locale.", printer,
                delegate(Action<string> log) { app.Printers.Purge(printer, log); }) {
                    Confirmation="Eliminare definitivamente tutti i lavori nella coda di «" + printer + "»? Per stampanti condivise possono essere coinvolti lavori di altri utenti; servono i permessi sul server. Un lavoro già in stampa può proseguire." };
        }
    }
}
