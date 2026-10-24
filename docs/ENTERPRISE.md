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

JSON malformato, output troncato o codici nativi ambigui non diventano successi. La localizzazione dell'output non è usata come criterio generale di parsing.

Profili e audit locali sono sotto LOCALAPPDATA/OnlyTweak, con ACL utente/SYSTEM. Audit prima e dopo le modifiche, senza dump della richiesta o segreti. L'audit locale non è immutabile né sostituisce i log centrali. I report sono volontari, con anteprima e HTML codificato, e contengono dati aziendali da conservare secondo policy.

## AD e replica

Query con server/target espliciti e verifica del naming context. Replica: tutte le partizioni, entrambe le direzioni, target espliciti e repadmin /showrepl. Nessun syncall, KCC forzato o ripristino autorevole.

DCDiag gira sul DC selezionato via WinRM con Connectivity, Advertising, SysVolCheck, NetLogons e Replications. Esclusi /c, aggiornamenti DNS, CutoffServers, Intersite e opzioni di riparazione. Permessi, RPC, ICMP e disponibilità WinRM possono rendere il controllo inconcludente.

Ricerca AD: filtro LDAP con escaping, massimo 1000 oggetti, pagine UI da 50, GUID stabile per selezione. Ricerca limitata ripetuta per pagina, non snapshot transazionale: cambiamenti directory possono cambiare i risultati. LastLogonDate è approssimato; lockout letto sul DC scelto, senza ricostruzione di tutta la cronologia accessi.

Prima di una modifica utente si rileggono stato e percorso. Al confine della scrittura: conferma, OU revisionate per persone, GUID/DC/DN espliciti; adminCount, SID speciali, primaryGroupID, SPN e gruppi di autorizzazione privilegiati bloccano l'azione. Errori di eleggibilità impediscono la scrittura. Verificata anche la delimitazione delle OU nei DN con caratteri escape.

Non si possono riconoscere automaticamente tutti i servizi senza SPN o i ruoli privilegiati personalizzati. Le OU ammesse devono contenere soltanto utenti ordinari; OU escluse e SID protetti vanno revisionati dal responsabile del cliente. Il controllo integra ACL/deleghe AD, senza sostituirle o eliminare la concorrenza tra lettura e scrittura.

Reset password: nessun login di prova, ripristino password precedente o ulteriore proprietà modificata implicitamente. Verifica tramite pwdLastSet sul DC selezionato. Segreto DPAPI CurrentUser nel trasporto tra processi dello stesso account locale; buffer nativi azzerati, ma WinForms non garantisce assenza di copie residue nella memoria gestita.

## GPO, DFS e postazioni

GPO computer: RSOP/gpresult sul target e versioni AD/SYSVOL via GroupPolicy dal tecnico. Il contesto dell'utente interattivo richiede raccolta dalla sua sessione, senza sostituirlo con l'amministratore remoto.

SYSVOL/NETLOGON: SMB e segnali DFSR distinti dalla replica AD. Namespace DFS: target/referral configurati e accessibilità SMB dal tecnico; non è una ricostruzione del referral effettivamente usato da un altro client. Target scoperti fuori dal profilo non contattati.

DFSR: due campioni direzionali a distanza di tre secondi, stato del membro e primi 100 file al massimo. Il totale è conservato nell'evidenza Verbose originale se disponibile; TotalCount resta nullo finché non esiste un dato numerico indipendente dalla lingua. Campione e backlog non nullo non diventano guasti. Confrontare orari e pianificazione su intervalli operativi appropriati.

Interventi endpoint vietati sui DC e con ruolo macchina sconosciuto. Lettura prima/dopo, conferma, audit e nessun riavvio implicito. SFC/DISM e reset mantengono esito da verificare quando il codice nativo non prova l'effetto. Il canale sicuro viene riparato soltanto sul membro locale con UAC e identità appropriata: avviare l’app sul PC interessato. Non viene riparato sui DC né esposto tramite WinRM/JEA per evitare il secondo salto.

## Predisposizione remota

Provision-Remoting.ps1 viene esportato con modulo JEA e istruzioni GPO; Inspect è default e Apply/Undo supportano WhatIf. Serve distribuzione locale/GPO/canale esistente: non apre da remoto un PC senza accesso preesistente.

Apply accetta membri di dominio, CIDR espliciti, gruppo dedicato e assenza di conflitti/GPO WinRM. Crea modulo con funzioni esportate nominate, ruolo JEA, endpoint RestrictedRemoteServer con account virtuale, trascrizioni e regola firewall Domain limitata alle reti indicate. HTTPS richiede chiave privata, validità temporale, EKU Server Authentication, FQDN esatto e catena attendibile; il client non usa opzioni Skip*.

Non modifica TrustedHosts, Basic, CredSSP, AllowUnencrypted o regole preesistenti. Una regola ristretta non chiude esposizioni più ampie già presenti. Mai distribuire account virtuali amministrativi sui DC: lo script li rifiuta.

Snapshot in ProgramData/OnlyTweak/Remoting, ACL Administrators/SYSTEM, con stato servizio e impronte degli artefatti. Undo verifica file, endpoint, listener, firewall e nuove configurazioni condivise prima di rimuovere soltanto gli artefatti registrati. Snapshot/trascrizioni restano. Una configurazione cambiata o un'installazione parziale richiedono recupero manuale dal checkpoint, senza cancellazioni automatiche.

Per App Control/AllSigned firmare ps1/psm1 prima della build/esportazione. Le configurazioni generate possono richiedere approvazione/firma aziendale: non si allenta ExecutionPolicy. Le GPO vanno ripristinate dalla stessa fonte centrale.

## Manutenzione e gate

Ripubblicare il bundle con le patch di .NET 10. Il lock NuGet registra dipendenze; l'inventario identifica runtime pack, dipendenze, hash e firma. L'audit advisory NuGet copre i pacchetti enumerati, senza provare assenza di vulnerabilità in Windows, codice o runtime.

Prima della consegna aziendale: firma script/EXE, App Control/AllSigned, avvio senza runtime installato, UAC multiutente, due domini senza trust, due DC nello stesso dominio, SYSVOL/DFSR, errori rete/deleghe, confronto con strumenti Microsoft e DPI fisici. Prove modificanti solo in VM ripristinabili. Gate nel tracker, distinti dalle fixture locali.

## Fonti primarie

Consultate il 24 ottobre 2026; la documentazione corrente non certifica ogni build/server legacy.

- [Supporto .NET](https://dotnet.microsoft.com/en-us/platform/support/policy), [Windows supportati](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [single file](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).
- [RSAT](https://learn.microsoft.com/en-us/windows-server/administration/install-remote-server-administration-tools), [FoD e WSUS/UUP](https://learn.microsoft.com/en-us/windows/deployment/update/fod-and-lang-packs).
- [CreateProcessWithLogonW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithlogonw), [accessi privilegiati](https://learn.microsoft.com/en-us/security/privileged-access-workstations/privileged-access-strategy), [App Control e script](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/script-enforcement).
- [DC Locator](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/troubleshoot-domain-controller-location-issues), [DCDiag](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/dcdiag).
- [Metadati replica](https://learn.microsoft.com/en-us/powershell/module/activedirectory/get-adreplicationpartnermetadata), [modulo AD](https://learn.microsoft.com/en-us/powershell/module/activedirectory/), [reset password](https://learn.microsoft.com/en-us/powershell/module/activedirectory/set-adaccountpassword).
- [DFSR backlog](https://learn.microsoft.com/en-us/powershell/module/dfsr/get-dfsrbacklog), [stato DFSR](https://learn.microsoft.com/en-us/powershell/module/dfsr/get-dfsrstate), [namespace DFS](https://learn.microsoft.com/en-us/windows-server/storage/dfs-namespaces/dfs-overview).
- [WinRM](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/winrm-security), [JEA](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/jea/session-configurations), [secondo salto](https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/ps-remoting-second-hop).
- [Defender](https://learn.microsoft.com/en-us/defender-endpoint/microsoft-defender-antivirus-windows), [setup-dotnet per CI](https://github.com/actions/setup-dotnet).
