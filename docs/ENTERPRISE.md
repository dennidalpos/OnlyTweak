# OnlyTweak 2.0 — architettura e qualificazione

## Flusso operativo

La UI Windows Forms gira asInvoker. Il motore usa .NET 10 per catalogo, validazione, storage, audit ed esecuzione asincrona; i moduli Windows/RSAT rimangono nel processo Windows PowerShell 5.1. Nessun servizio o agente proprietario sui target.

EnvironmentProfile contiene identificatore cliente, dominio, DC, FQDN consentiti, OU ammesse/escluse, SID aggiuntivi protetti, revisione esplicita del perimetro utenti e modalità WinRM/JEA. È copiato e vincolato alla sessione; modificarlo richiede una nuova connessione. Il profilo è un dato, mai codice.

ActionDefinition stabilisce ID, parametri consentiti, rischio, privilegi, timeout e fonte Microsoft. RunbookDefinition concatena solo letture con interpretazione e arresto se il bind preliminare non riesce. StepResult conserva target, profilo, identità locale/rete dichiarata, autenticazione verificata, stato, tempi, evidenze, dati e troncamento.

La UI usa un processo dello stesso EXE per cliente; le credenziali alternative creano un logon Windows separato LOGON_NETCREDENTIALS_ONLY. Nessun argomento o file contiene password. L'identità di rete resta dichiarata: il bind o la sessione remota verifica l'accesso al target, non dimostra il nome canonico dell'account in ogni protocollo.

Il trasporto usa named pipe con ACL utente/Administrators/SYSTEM, rifiuto Network SID, ID peer e percorso eseguibile chiamante verificati. Massimo 2 MiB per messaggio. Il worker elevato accetta solo inventario/installazione RSAT e un elenco chiuso di manutenzioni locali; conferma gli interventi nella propria UI. Nessun comando o script arbitrario.

Operations.ps1 viene estratto dalla risorsa incorporata in una directory temporanea privata. Percorsi PowerShell e moduli espliciti; parametri JSON via stdin. Output/errori hanno budget in memoria; il lease viene rimosso a fine operazione. Crash esterni possono lasciare un lease da verificare, senza pulizia indiscriminata.

## Risultati e dati

- Success: lettura/esecuzione completata, non certificazione complessiva del sistema.
- ObservedIssue: problema nelle evidenze, ad esempio errore di replica o canale membro non valido.
- Inconclusive/Unavailable: evidenze, permessi, moduli o canale insufficienti; mai stato sano.
- Unverified: modifica o effetto finale non accertato; nessuna ripetizione automatica. Rilettura della stessa azione/oggetto richiesta nella sessione.
- Cancelled: diagnostica/conferma annullata.

L'annullamento chiude solo il processo diagnostico e i suoi figli. Le modifiche non usano questo percorso: si attende la terminazione e si tenta la verifica. Dopo disconnessione il worker lascia terminare un intervento iniziato, poi chiude la sessione. Se Windows o il processo vengono terminati esternamente, l'esito richiede verifica manuale.

Anche il catalogo locale ActionSpec distingue diagnostiche e modifiche. Entrambe le viste di rete usano ipconfig /all; stato spooler usa sc query Spooler: tutte queste letture hanno un limite di 60 secondi. SFC /verifyonly e DISM /ScanHealth hanno un limite di 1800 secondi. Annullamento e timeout raggiungono il processo locale o il worker elevato; solo le diagnostiche possono terminare il proprio albero di processi. L'output viene drenato e limitato anche dopo l'annullamento. Le modifiche ignorano questi token e non ricevono un kill generico.

LocalOperationExecutor esegue audit preventivo, lettura prima/dopo e risultato strutturato: RequestAccepted, ExecutionCompleted, EffectVerified, AuditComplete, codice nativo e osservazioni disponibili. SFC/DISM/reset rete rimangono Unverified quando il solo codice o la rilettura non dimostrano l'effetto; 3010 richiede riavvio manuale. Spooler e coda selezionata usano condizioni osservabili; EnumJobs campiona la presenza di lavori senza certificare il dispositivo fisico. Un errore nell'audit finale o nel trasporto mantiene l'esito incerto. Nella sessione UI il retry resta bloccato fino a una rilettura esplicita riuscita mediante Rileggi stato; una lettura riuscita non equivale a riparazione riuscita. La rilettura ha a sua volta annullamento e timeout. Audit e azione elevati usano l'identita effettiva del worker, mentre il contesto desktop resta distinto.

JSON malformato, output troncato o codici nativi ambigui non diventano successi. La localizzazione dell'output non è usata come criterio generale di parsing.

`domain.auth`, `domain.sysvol` ed `endpoint.health` isolano le raccolte indipendenti. I dati già ottenuti restano nel risultato, anche se una raccolta successiva o una fonte opzionale fallisce. `Checks`, indicizzato per sottocontrollo, espone `Status` e `ErrorCode` sanitizzato: `Completed`, `Empty`, `NotApplicable`, `Denied`, `Unavailable`, `TimedOut`, `Failed`. Non contiene messaggi originali delle eccezioni. L'assenza di eventi corrispondenti è `Empty`, distinta da permessi negati o log/provider assenti, senza dedurre salute. Array parziali emessi prima di un errore sono conservati con lo stato incompleto.

Una raccolta incompleta rende l'esito `Inconclusive` con `PartialDiagnosticRead`; un canale sicuro esplicitamente falso conserva `ObservedIssue` anche in presenza di altre letture incomplete. `Success` richiede metadati coerenti e sole raccolte completate, vuote valide o non applicabili. Se scade l'intero processo diagnostico rimane l'esito sconosciuto del bridge: non vengono ricostruiti dati mai ricevuti. SYSVOL conserva separatamente le share lette dal tecnico sul DC del profilo e servizi/cartelle/eventi letti sul target remoto.

Il modulo JEA revisionato espone gli stessi sottocontrolli e gli eventi delle tre diagnostiche; i ticket del contesto JEA sono esplicitamente `NotApplicable/JeaNotExposed`. Il test del canale sicuro non viene eseguito su DC o computer fuori dominio; se il ruolo non è leggibile resta indisponibile. Un modulo precedente privo di `Checks` non può produrre un successo accettato. Aggiornamento e firma del modulo restano operazioni amministrative tramite canale approvato: nessun Apply o allentamento delle policy automatico.

Il confine PowerShell valida presenza e tipo di stato, riepilogo, evidenze, autenticazione, codice errore e dati. Uno stato assente/sconosciuto, campi duplicati o dati essenziali mancanti per l'azione non producono Success; una ricerca o cache legittimamente vuota rimane valida. Questo contratto è distinto dalla costruzione dei risultati interni e dal trasporto IPC.

`endpoint.verify` usa un risultato osservativo comune a WinRM e JEA: operazione, computer, ReadComplete, NativeExitCode, Output e Data. Le letture native richiedono un codice zero e output presente; errori o codice assente mantengono Inconclusive e il blocco retry. La lettura riuscita descrive lo stato osservato, senza certificare l'effetto di SFC/DISM/reset. Il modulo JEA aggiornato espone `Get-OnlyTweakEndpointEvidence -Kind verify -Operation <voce consentita>`; un modulo precedente che non supporta il contratto viene rifiutato, senza fallback ottimistico. Aggiornarlo tramite il canale amministrativo approvato prima della qualifica, senza Apply implicite dall'app.

Profili e audit locali sono sotto LOCALAPPDATA/OnlyTweak, con ACL utente/SYSTEM. Audit prima e dopo le modifiche, senza dump della richiesta o segreti. L'audit locale non è immutabile né sostituisce i log centrali. I report sono volontari, con anteprima e HTML codificato, e contengono dati aziendali da conservare secondo policy.

## AD e replica

Query con server/target espliciti e verifica del naming context. Replica: tutte le partizioni, entrambe le direzioni, target espliciti e repadmin /showrepl. Nessun syncall, KCC forzato o ripristino autorevole.

DCDiag gira sul DC selezionato via WinRM con Connectivity, Advertising, SysVolCheck, NetLogons e Replications. Esclusi /c, aggiornamenti DNS, CutoffServers, Intersite e opzioni di riparazione. Permessi, RPC, ICMP e disponibilità WinRM possono rendere il controllo inconcludente.

Ricerca AD: filtro LDAP con escaping, massimo 1000 oggetti, pagine UI da 50, GUID stabile per selezione. Ricerca limitata ripetuta per pagina, non snapshot transazionale: cambiamenti directory possono cambiare i risultati. LastLogonDate è approssimato; lockout letto sul DC scelto, senza ricostruzione di tutta la cronologia accessi.

`ad.search` e `session.authenticate` usano `Server=Target` per bind e query. Prima della raccolta confrontano il naming context RootDSE con il dominio del profilo, `dnsHostName` con il FQDN selezionato e l'identità/dominio restituiti da Get-ADDomainController interrogato sullo stesso server. Membri, identità discordanti e domini diversi vengono rifiutati senza fallback; alias diversi dal FQDN del DC non sono accettati. I dati includono `Server`, che il contratto confronta con il target; la verifica sessione confronta anche DNSRoot. Cambiare destinazione azzera pagina, righe e selezione AD; i risultati storici conservano il proprio target nei report. Le scritture restano limitate al DC del profilo e richiedono nuova rilettura di GUID/DN ed eleggibilità.

Prima di una modifica utente si rileggono stato e percorso. Al confine della scrittura: conferma, OU revisionate per persone, GUID/DC/DN espliciti; adminCount, SID speciali, primaryGroupID, SPN e gruppi di autorizzazione privilegiati bloccano l'azione. Errori di eleggibilità impediscono la scrittura. Verificata anche la delimitazione delle OU nei DN con caratteri escape.

Non si possono riconoscere automaticamente tutti i servizi senza SPN o i ruoli privilegiati personalizzati. Le OU ammesse devono contenere soltanto utenti ordinari; OU escluse e SID protetti vanno revisionati dal responsabile del cliente. Il controllo integra ACL/deleghe AD, senza sostituirle o eliminare la concorrenza tra lettura e scrittura.

Reset password: nessun login di prova, ripristino password precedente o ulteriore proprietà modificata implicitamente. Verifica tramite pwdLastSet sul DC selezionato. Segreto DPAPI CurrentUser nel trasporto tra processi dello stesso account locale; buffer nativi azzerati, ma WinForms non garantisce assenza di copie residue nella memoria gestita.

## GPO, DFS e postazioni

GPO computer: RSOP/gpresult sul target e versioni AD/SYSVOL via GroupPolicy dal tecnico. Il contesto dell'utente interattivo richiede raccolta dalla sua sessione, senza sostituirlo con l'amministratore remoto.

La raccolta GPO standard/JEA espone NativeExitCode, Result, ReadComplete e Checks RSOP/Gpresult. gpresult richiede codice esplicito zero e output presente; codice diverso/assente, output vuoto o RSOP fallito producono Inconclusive/PartialGpoRead. RSOP, output nativo e versioni rimangono indipendenti: anche un errore nel confronto delle versioni conserva le evidenze endpoint. I testi localizzati non vengono interpretati per dedurre successo.

SYSVOL/NETLOGON: SMB e segnali DFSR distinti dalla replica AD. Namespace DFS: target/referral configurati e accessibilità SMB dal tecnico; non è una ricostruzione del referral effettivamente usato da un altro client. Target scoperti fuori dal profilo non contattati.

DFSR: due campioni direzionali a distanza di tre secondi, stato del membro e primi 100 file al massimo. Il totale è conservato nell'evidenza Verbose originale se disponibile; TotalCount resta nullo finché non esiste un dato numerico indipendente dalla lingua. Campione e backlog non nullo non diventano guasti. Confrontare orari e pianificazione su intervalli operativi appropriati.

Interventi endpoint vietati sui DC e con ruolo macchina sconosciuto. Lettura prima/dopo, conferma, audit e nessun riavvio implicito. SFC/DISM e reset mantengono esito da verificare quando il codice nativo non prova l'effetto. Il canale sicuro viene riparato soltanto sul membro locale con UAC e identità appropriata: avviare l’app sul PC interessato. Non viene riparato sui DC né esposto tramite WinRM/JEA per evitare il secondo salto.

## Predisposizione remota

Provision-Remoting.ps1 viene esportato con modulo JEA e istruzioni GPO; Inspect è default e Apply/Undo supportano WhatIf. Serve distribuzione locale/GPO/canale esistente: non apre da remoto un PC senza accesso preesistente.

Apply accetta membri di dominio, CIDR espliciti, gruppo dedicato e assenza di conflitti/GPO WinRM. Crea modulo con funzioni esportate nominate, ruolo JEA, endpoint RestrictedRemoteServer con account virtuale, trascrizioni e regola firewall Domain limitata alle reti indicate. HTTPS richiede chiave privata, validità temporale, EKU Server Authentication, FQDN esatto e catena attendibile; il client non usa opzioni Skip*.

Inspect espone listener e compatibilita richiesta. Prima di qualsiasi scrittura Apply rifiuta listener multipli o con indirizzo, porta, Enabled, trasporto o URLPrefix diversi dal contratto; HTTPS richiede anche thumbprint e Hostname/FQDN corrispondenti. Il preflight viene ripetuto dopo la conferma. Non vengono sostituiti listener preesistenti, ne creati checkpoint in presenza di questi conflitti.

Non modifica TrustedHosts, Basic, CredSSP, AllowUnencrypted o regole preesistenti. Una regola ristretta non chiude esposizioni più ampie già presenti. Mai distribuire account virtuali amministrativi sui DC: lo script li rifiuta.

Snapshot in ProgramData/OnlyTweak/Remoting, ACL Administrators/SYSTEM, con stato servizio e impronte degli artefatti. Undo verifica file, endpoint, listener, firewall e nuove configurazioni condivise prima di rimuovere soltanto gli artefatti registrati. Snapshot/trascrizioni restano. Una configurazione cambiata o un'installazione parziale richiedono recupero manuale dal checkpoint, senza cancellazioni automatiche.

I nuovi checkpoint hanno SchemaVersion 2, Generation, identita macchina e CheckpointHash. Dopo Undo verificato un nuovo Apply copia lo stato precedente in History/Generation.json e ne verifica i byte prima di creare una nuova generazione. Undo ripetuto su uno stato gia ripristinato e un'operazione senza scritture; WhatIf non modifica stato o storico. Stati attivi, Partial, alterati o di altra macchina restano bloccanti. Gli snapshot legacy senza schema 2 richiedono revisione manuale: nessuna migrazione o cancellazione implicita. Hash e ACL non rendono lo storico immutabile contro un amministratore che possa alterare stato e hash.

Per App Control/AllSigned firmare ps1/psm1 prima della build/esportazione. Le configurazioni generate possono richiedere approvazione/firma aziendale: non si allenta ExecutionPolicy. Le GPO vanno ripristinate dalla stessa fonte centrale.

## Manutenzione e gate

Ripubblicare il bundle con le patch di .NET 10. Il lock NuGet registra dipendenze; l'inventario identifica runtime pack, dipendenze, hash e firma. L'audit advisory NuGet copre i pacchetti enumerati, senza provare assenza di vulnerabilità in Windows, codice o runtime.

Build e consegna richiedono PowerShell 7.4+; il prodotto continua a usare Windows PowerShell 5.1 per le procedure Windows/RSAT. MSBuild incorpora prima della compilazione metadati sanitizzati: hash degli input, hash assets, pacchetti NuGet risolti e runtime pack selezionati da RuntimePackAsset. La ricevuta del publish associa tali evidenze all'hash dell'EXE. prepare-release.ps1 legge la modalità non modificante --release-inspect dell'EXE registrato, confronta risorse e sorgenti, controlla Authenticode sui byte incorporati e produce il manifest v2 dalla build effettiva. Non ricostruisce l'inventario usando assets eventualmente successivi; una divergenza blocca la consegna. La ricevuta locale non è una attestazione immutabile né sostituisce firma aziendale o qualifica App Control/AllSigned.

Prima della consegna aziendale: firma script/EXE, App Control/AllSigned, avvio senza runtime installato, UAC multiutente, due domini senza trust, due DC nello stesso dominio, SYSVOL/DFSR, errori rete/deleghe, confronto con strumenti Microsoft e DPI fisici. Prove modificanti solo in VM ripristinabili. Gate nel tracker, distinti dalle fixture locali.

Chiusura applicativa del 26 ottobre 2026: per richiesta dell'utente le prove su VM/domini e le qualifiche ambientali sono fuori perimetro. I task applicativi si chiudono con fixture, build e controlli locali; i gate ambientali non vengono dichiarati superati o cancellati per questa scelta.

Fonti rivalidate per questi percorsi: [listener WinRM](https://learn.microsoft.com/en-us/windows/win32/winrm/installation-and-configuration-for-windows-remote-management), [attesa asincrona .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-10.0), [EnumJobs](https://learn.microsoft.com/en-us/windows/win32/printdocs/enumjobs). Le API e le fixture locali non certificano comportamento di driver, server o policy reali.

## Fonti primarie

Consultate il 24 ottobre 2026; la documentazione corrente non certifica ogni build/server legacy.

Rivalidate il 25 ottobre 2026 per il lotto 14/15: [Get-WinEvent, Windows PowerShell 5.1](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.diagnostics/get-winevent?view=powershell-5.1), [Get-ADRootDSE](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-adrootdse?view=windowsserver2025-ps), [Get-ADDomainController](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-addomaincontroller?view=windowsserver2025-ps). I riferimenti AD descrivono il modulo corrente; le fixture eseguono Windows PowerShell 5.1, senza qualificare tutte le versioni RSAT/server o un dominio reale. Gli identificatori eventi sono verificati nelle fixture, senza usare testi localizzati come discriminante.

- [Supporto .NET](https://dotnet.microsoft.com/en-us/platform/support/policy), [Windows supportati](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [single file](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).
- [RSAT](https://learn.microsoft.com/en-us/windows-server/administration/install-remote-server-administration-tools), [FoD e WSUS/UUP](https://learn.microsoft.com/en-us/windows/deployment/update/fod-and-lang-packs).
- [CreateProcessWithLogonW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithlogonw), [accessi privilegiati](https://learn.microsoft.com/en-us/security/privileged-access-workstations/privileged-access-strategy), [App Control e script](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/script-enforcement).
- [DC Locator](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/troubleshoot-domain-controller-location-issues), [DCDiag](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/dcdiag).
- [Metadati replica](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-adreplicationpartnermetadata), [modulo AD](https://learn.microsoft.com/en-us/powershell/module/activedirectory/), [reset password](https://learn.microsoft.com/en-us/powershell/module/activedirectory/set-adaccountpassword).
- [DFSR backlog](https://learn.microsoft.com/en-us/powershell/module/dfsr/get-dfsrbacklog), [stato DFSR](https://learn.microsoft.com/en-us/powershell/module/dfsr/get-dfsrstate), [namespace DFS](https://learn.microsoft.com/en-us/windows-server/storage/dfs-namespaces/dfs-overview).
- [WinRM](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/winrm-security), [JEA](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/jea/session-configurations), [secondo salto](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/ps-remoting-second-hop).
- [Defender](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-windows), [setup-dotnet per CI](https://github.com/actions/setup-dotnet).
- Contratti e consegna, rivalidati il 25 ottobre 2026: [campi JSON obbligatori](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties), [ordine target MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/target-build-order), [bundle single-file](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [firma dei byte con PowerShell 7.4+](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature?view=powershell-7.5).

## Navigazione, tweak e diagnostiche locali — 26 ottobre 2026

La finestra principale ospita pagine UserControl; la navigazione controlla abbandono, annullamento e chiusura prima di rilasciare i controlli. I worker cliente appartengono allo spazio di lavoro conservato, non alla singola sottopagina. Le conferme delle modifiche sono acquisite nella UI prima dell'invio al worker associato tramite IPC; il worker continua a validare catalogo, profilo, tipo di elevazione e conferma esplicita. Le richieste locali modificanti senza conferma vengono rifiutate. L'UAC rimane il dialogo Windows per l'elevazione.

I quattro tweak ammessi scrivono soltanto HKCU dell'account desktop verificato. Prima di ogni scrittura sono richiesti audit e checkpoint persistente atomico; lo stato Pending blocca il recupero automatico se l'operazione o il salvataggio finale falliscono. Ripristino con confronto dell'ultimo stato scritto, conservazione del tipo originale e pulizia delle sole chiavi create, vuote e non condivise. Il checksum non certifica immutabilità contro amministratori o utenti capaci di riscrivere il checkpoint. Nessuna migrazione automatica dei tweak storici.

Le otto diagnostiche locali usano esclusivamente eseguibili/azioni fissi, input tipizzati e identità dell'app. I processi nativi ricevono argomenti separati; LocalDiagnostics.ps1 è incorporato e inventariato tra le risorse firmabili del bundle. Windows PowerShell 5.1 viene avviato senza profilo, con input JSON e policy invariate. Moduli/cmdlet assenti producono indisponibilità; timeout, output troncato ed errori restano inconcludenti. DNS vuoto e TCP negativo sono osservazioni distinte da un successo positivo. Nessuna diagnosi eredita le credenziali temporanee dei clienti.

Fonti primarie: [opzioni Esplora file](https://support.microsoft.com/en-gb/windows/experience/fileexplorer/file-explorer-in-windows), [menu Windows 11 e Mostra altre opzioni](https://blogs.windows.com/blog/2021/07/19/extending-the-context-menu-and-share-dialog-in-windows-11/), [Resolve-DnsName](https://learn.microsoft.com/en-us/powershell/module/dnsclient/resolve-dnsname?view=windowsserver2025-ps), [Test-NetConnection](https://learn.microsoft.com/en-us/powershell/module/nettcpip/test-netconnection?view=windowsserver2025-ps), [proxy WinHTTP](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-winhttp). Le pagine dei cmdlet descrivono il modulo corrente e non qualificano tutte le versioni Windows client: le fixture eseguono Windows PowerShell 5.1 senza connessioni reali. Il workaround permanente del menu classico non è un contratto Microsoft garantito; la rilettura verifica soltanto il registro.
