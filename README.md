# OnlyTweakWindows 2.0

Toolkit portatile per tecnici e amministratori Windows aziendali. Interfaccia italiana Windows Forms, profili cliente separati, diagnostica AD e postazioni, interventi guidati e preparazione RSAT.

**Stato: implementazione locale da qualificare in laboratorio.** Build e fixture non equivalgono a validazione su domini reali. Firma, Windows puliti, credenziali tra foreste senza trust, WinRM/JEA e pilota sono gate aperti in [PROJECT_STATUS.json](PROJECT_STATUS.json). Nessuna pubblicazione automatica.

## Primo utilizzo

Distribuire soltanto **dist/OnlyTweakWindows.exe**, autonomo x64 con .NET 10 incorporato e senza runtime separato da installare. Circa 111 MiB nella build locale; il bundle può estrarre librerie native nella cache privata Windows. La UI parte senza UAC obbligatorio; le operazioni locali privilegiate usano un processo elevato distinto.

1. Aprire **Postazione tecnico** e verificare Windows PowerShell 5.1, moduli e console.
2. Inventariare RSAT e installare solo AD DS/LDS, GPO, DNS, DFS o DHCP necessari. Origine aziendale predefinita oppure cartella FoD locale compatibile; policy WSUS/Windows Update invariate.
3. Aprire **Ambienti aziendali**, creare il cliente con dominio DNS, DC esplicito e destinazioni FQDN consentite.
4. Connettere l'identità corrente oppure acquisire credenziali di rete per la sola sessione dal dialogo Windows. L'avvio del processo non valida la password: controllare l'autenticazione sul target.
5. Selezionare destinazione e procedura, leggere evidenze e limiti prima degli interventi.
6. Esportare volontariamente JSON/HTML dopo aver controllato anteprima e destinazione.

Conservate stampa, rete, manutenzione locale, strumenti Windows e letture delle protezioni. Rimossi tweak estetici, ripristino HKCU e account personali; non vengono annullate impostazioni applicate da versioni precedenti.

## Funzioni aziendali

| Area | Funzioni |
|---|---|
| Problemi comuni | Dominio/DC, DNS, orario/autenticazione, replica AD, GPO, SYSVOL, salute postazione. |
| Tecnico e RSAT | Inventario moduli/componenti/policy, installazione selettiva, console Microsoft. |
| Active Directory | Ricerca paginata utenti/gruppi/computer, confronto oggetto tra DC, stato account, sblocco, abilitazione/disabilitazione, reset password di singoli utenti ordinari. |
| DFS e replica | Replica AD su tutte le partizioni/partner, namespace/target SMB, campioni direzionali backlog DFSR, SYSVOL/NETLOGON. |
| Interventi postazione | Criteri computer, cache DNS, spooler, canale sicuro membri, reset rete, SFC/DISM; letture prima/dopo e audit. |
| Predisposizione remota | Esportazione script Inspect/WhatIf/Apply/Undo, istruzioni GPO e modulo JEA. Nessuna configurazione remota implicita. |

Le console esterne mantengono identità e ciclo di vita propri. Non ereditano le credenziali temporanee del worker: per queste usare una sessione Windows amministrativa dedicata al cliente.

## Sicurezza e dati

- Processi/sessioni separati per cliente, target espliciti e catalogo chiuso; niente shell libera, script scaricati o aggiramento ExecutionPolicy/App Control/AppLocker.
- Diagnostica con timeout/annullamento; modifiche con conferma, audit preventivo e verifica. Nessun riavvio automatico o arresto forzato generico delle riparazioni.
- Esito incerto: rilettura prima di ripetere la modifica nella sessione. Dopo perdita completa della sessione rileggere esplicitamente prima di riprovare.
- AD: GUID/DC/DN espliciti, OU utenti ordinari revisionate, gruppi privilegiati noti e account protetti/con SPN esclusi. Il responsabile deve escludere anche servizi senza SPN e ruoli privilegiati personalizzati dalle OU ammesse.
- ACL e deleghe Windows/AD restano il confine delle autorizzazioni. Usare account delegati e PAW adeguate, evitando credenziali Tier 0 sui normali PC di assistenza.
- Profili in LOCALAPPDATA/OnlyTweak/profiles.json; audit in LOCALAPPDATA/OnlyTweak/audit/id-profilo, ACL utente/SYSTEM, nessuna password. L'audit locale non è immutabile né sostituisce i log centrali.
- Password nuove protette con DPAPI nel trasporto locale; buffer nativi azzerati appena possibile. Nessun salvataggio automatico negli appunti o nei report. WinForms può lasciare copie temporanee nella memoria gestita.
- Report e output contengono dati aziendali: identità, nomi, indirizzi e percorsi. Nessuna telemetria automatica. Conservazione/eliminazione secondo policy del cliente.

Pulizia spooler solo SPL/SHD al primo livello del percorso standard verificato: niente ricorsione, rimozione driver o stampanti. Protezioni Windows esclusivamente in lettura.

## Compatibilità

Host tecnico Windows 10 dalla build 17763 e Windows 11 x64 Intel/AMD. Server, ARM64 e x86 esclusi dall'app. RSAT per le edizioni qualificate e verificato per componente. La qualificazione aziendale riguarda Windows 11 supportati e Windows 10 aziendali esplicitamente coperti da LTSC/ESU: avvio tecnico e supporto Microsoft sono distinti.

Server remoti previsti 2019/2022/2025; Server 2016 da qualificare separatamente. Richiesti rete/VPN, DNS, moduli e deleghe. WinRM con Kerberos, opzionalmente HTTPS con certificato valido; nessun fallback automatico a CredSSP, Basic, traffico non cifrato o TrustedHosts wildcard.

La riparazione del canale sicuro è soltanto locale, con UAC: avviare OnlyTweak sul membro interessato con un account delegato appropriato. Non viene esposta tramite WinRM/JEA per evitare il secondo salto. Le GPO utente richiedono raccolta dalla sessione dell'utente interessato. Il conteggio totale DFSR rimane nell'evidenza Verbose originale quando manca un dato numerico indipendente dalla lingua.

## Build e verifiche

Sviluppo: SDK .NET 10, Windows x64, accesso NuGet per la dipendenza esplicita Microsoft System.ServiceProcess.ServiceController. Runtime/librerie inclusi nell'EXE; PowerShell 7 non è un prerequisito dell'app.

    build.cmd
    test.cmd --artifact --ui --read-only --render

La build pubblica l'EXE autonomo in dist. Il banco di test .NET 10 usa fixture per dominio e modifiche; non contatta AD reali. Il flag --read-only aggiunge letture locali; --ui e --render richiedono desktop. Render in artifacts/ui: i campioni scalati non certificano DPI fisici. Render 1.x preesistenti sono storici.

CI su Windows Server con SDK .NET 10 e test.cmd --artifact --read-only. I push sul branch principale avviano il workflow; lo stato remoto è consultabile in [GitHub Actions](https://github.com/dennidalpos/OnlyTweak/actions). Non sostituisce laboratorio AD o Windows client puliti.

Verifica locale 24 ottobre 2026: build autonoma riuscita, **83 test superati, 0 falliti** con suite completa. Rapporto artifacts/enterprise-full.log; build artifacts/enterprise-build.log. Audit advisory NuGet in artifacts/dependency-audit.json, senza vulnerabilità segnalate per i pacchetti enumerati: non certifica il sistema o l'applicazione.

## Consegna

    pwsh -NoProfile -File scripts/prepare-release.ps1

Prepara copia locale e manifest con hash, dipendenze/runtime pack e stato Authenticode. Il flag -RequireSigned blocca una consegna senza firma valida; firma opzionale con certificato aziendale e percorso esplicito SignTool. Firmare ps1/psm1 prima della build perché le risorse incorporate conservino la firma. Le policy possono bloccare script non approvati: l'app lo segnala senza aggirarle.

La build attuale è **non firmata**, destinata alla qualificazione in laboratorio. Documentazione tecnica e fonti Microsoft: [docs/ENTERPRISE.md](docs/ENTERPRISE.md). Storico UI: [docs/UI-UX.md](docs/UI-UX.md). Gate residui: [PROJECT_STATUS.json](PROJECT_STATUS.json).


## Ripresa nella prossima sessione

La fonte delle attività rimane [PROJECT_STATUS.json](PROJECT_STATUS.json), con stati e storico conservati. Le correzioni OT-AUDIT-08/09/10 sono completate; OT-AUDIT-11 è superato per rimozione dei tweak. Restano aperti OT-VM-01..04 e i gate OT-ENT-LAB-01, OT-ENT-SIGN-01, OT-ENT-PILOT-01. Ripartire da host/VM e laboratorio AD; firma e pilota richiedono i prerequisiti indicati nel tracker. Il salvataggio Git dei sorgenti non equivale a una distribuzione o a validazione in produzione.
