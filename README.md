# OnlyTweakWindows 2.0

Toolkit portatile per tecnici e amministratori Windows aziendali. Interfaccia italiana Windows Forms, profili cliente separati, diagnostica AD e postazioni, interventi guidati e preparazione RSAT.

**Stato: chiusura applicativa con verifiche locali.** Per scelta dell'utente non vengono eseguiti test su VM o domini reali. Build e fixture non equivalgono a qualifica ambientale: firma, Windows puliti, WinRM/JEA e pilota restano separati in [tracker SecondBrain](../SecondBrain/02-areas/products/OnlyTweak/tracker.json). Nessuna pubblicazione automatica.

## Primo utilizzo

Distribuire soltanto **dist/OnlyTweakWindows.exe**, autonomo x64 con .NET 10 incorporato e senza runtime separato da installare. Circa 111 MiB nella build locale; il bundle può estrarre librerie native nella cache privata Windows. La UI parte senza UAC obbligatorio; le operazioni locali privilegiate usano un processo elevato distinto.

1. Aprire **Postazione tecnico** e verificare Windows PowerShell 5.1, moduli e console.
2. Inventariare RSAT e installare solo AD DS/LDS, GPO, DNS, DFS o DHCP necessari. Origine aziendale predefinita oppure cartella FoD locale compatibile; policy WSUS/Windows Update invariate.
3. Aprire **Ambienti aziendali**, creare il cliente con dominio DNS, DC esplicito e destinazioni FQDN consentite.
4. Connettere l'identità corrente oppure acquisire credenziali di rete per la sola sessione dal dialogo Windows. L'avvio del processo non valida la password: controllare l'autenticazione sul target.
5. Selezionare destinazione e procedura, leggere evidenze e limiti prima degli interventi.
6. Esportare volontariamente JSON/HTML dopo aver controllato anteprima e destinazione.

Stampa, rete, manutenzione locale, strumenti Windows e letture delle protezioni restano disponibili. La navigazione usa una sola finestra: sezioni, conferme, editor, parametri, risultati e cronologia sono pagine interne. La freccia **Indietro** e **Alt+Sinistra** tornano alla pagina precedente conservandone lo stato. Durante una diagnosi Indietro richiede l'annullamento e ne attende la conclusione; durante una modifica occorre attendere l'esito. I dialoghi Windows per credenziali/file/cartelle e gli strumenti esterni mantengono le proprie finestre. Le sessioni cliente rimangono aperte fino a disconnessione esplicita, cambio profilo o chiusura dell'app.

## Tweak e diagnostiche locali

**Tweak Windows** comprende menu contestuale classico su Windows 11, visualizzazione delle estensioni, elementi nascosti e apertura di Esplora file su Questo PC o Home/Accesso rapido. Ogni intervento presenta stato, anteprima e conferma, scrive soltanto HKCU dell'utente desktop verificato e rilegge il valore. I file protetti di sistema e le protezioni Windows restano invariati. Il menu classico è un workaround non ufficiale: il valore nel registro non dimostra l'effetto visivo su ogni build.

**Ripristina precedente** usa checkpoint persistenti in `%LOCALAPPDATA%\OnlyTweak\Tweaks`, associati a macchina e SID, con tipo e valore originali, eventuale assenza e proprietà delle chiavi create. Un errore prima/durante la scrittura mantiene il checkpoint incompleto e blocca interventi automatici; modifiche esterne o checkpoint alterati richiedono revisione. ACL e checksum proteggono accesso e integrità accidentale, senza costituire uno storico immutabile contro lo stesso utente o un amministratore. Nessun riavvio automatico di Explorer: quando necessario disconnettersi. I checkpoint delle versioni 1.x non vengono importati automaticamente.

In **Rete → Diagnostica tecnica** sono disponibili ping (quattro richieste), query DNS A/AAAA/CNAME, test di una porta TCP, connessioni/PID, routing e proxy WinHTTP. **Strumenti Windows** aggiunge identità/gruppi e criteri utente applicati (`gpresult /r /scope user`). Target FQDN e porte sono validati; le procedure hanno timeout di 60 secondi, annullamento e output limitato. Un test negativo è distinto da una lettura incompleta e non dimostra un guasto generale. Le diagnosi usano l'identità locale dell'app; i profili cliente non forniscono credenziali a questi comandi. Windows PowerShell 5.1 e i singoli cmdlet sono verificati al momento dell'uso, senza bypass delle policy.

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

Ricerca AD e verifica identità/dominio interrogano il **DC selezionato**, verificandone identità e dominio prima delle query. Risultati e report indicano il server effettivo; non viene usato automaticamente il DC del profilo come alternativa. Cambiare destinazione svuota la ricerca e la selezione precedenti. Le scritture sugli utenti rimangono vincolate al DC esplicito del profilo.

Diagnostica autenticazione, SYSVOL e salute postazione conservano le letture indipendenti quando un sottocontrollo fallisce. `Checks` distingue raccolta completata, vuota, non applicabile, negata, indisponibile, scaduta o fallita. Zero eventi non dimostra salute; una raccolta parziale resta inconcludente, mentre un problema esplicitamente osservato rimane segnalato. WinRM standard e JEA usano lo stesso contratto: aggiornare il modulo JEA tramite il canale amministrativo approvato, senza predisposizione o fallback impliciti.

Le pagine aziendali e i risultati rimangono raggiungibili tramite scorrimento anche alla dimensione minima. Le diagnostiche locali, comprese SFC verifica e DISM analisi, hanno timeout e **Annulla diagnosi**; le riparazioni attendono il completamento e non vengono terminate forzatamente. Per gli interventi, richiesta accettata, esecuzione conclusa ed effetto osservato sono distinti: un reset o un codice 3010 non dimostrano il risultato finale. Dopo un esito incerto usare **Rileggi stato** prima di ripetere. La coda stampante viene riletta, senza attestare l'arresto fisico di una stampa.

La predisposizione blocca listener incompatibili prima delle scritture. Dopo un Undo verificato, Apply conserva il precedente checkpoint nella cartella History; checkpoint attivi, parziali, legacy o alterati richiedono revisione manuale, senza cancellazioni automatiche. La diagnostica GPO conserva codice nativo gpresult, RSOP e versioni disponibili anche in caso di lettura parziale.

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

Sviluppo: SDK .NET 10, PowerShell 7.4 o successivo (`pwsh` in PATH), Windows x64, accesso NuGet per la dipendenza esplicita Microsoft System.ServiceProcess.ServiceController. Runtime/librerie inclusi nell'EXE; PowerShell 7 serve ai comandi di build/consegna e alle relative fixture, non all'app distribuita.

    build.cmd
    test.cmd --artifact --ui --read-only --render

La build pubblica l'EXE autonomo in dist e registra una ricevuta con hash in artifacts/build-evidence/publish.json. I metadati compilati nell'EXE identificano sorgenti, script, assets e runtime pack effettivamente risolti. Il banco di test .NET 10 usa fixture per dominio e modifiche; non contatta AD reali. Il flag --read-only aggiunge letture locali; --ui e --render richiedono desktop. Render in artifacts/ui: i campioni scalati non certificano DPI fisici. Render 1.x preesistenti sono storici.

CI su Windows Server con SDK .NET 10 e test.cmd --artifact --read-only. I push sul branch principale avviano il workflow; lo stato remoto è consultabile in [GitHub Actions](https://github.com/dennidalpos/OnlyTweak/actions). Non sostituisce laboratorio AD o Windows client puliti.

Cronologia delle verifiche locali e dei lotti precedenti: [storico conservato](../SecondBrain/02-areas/products/OnlyTweak/history/2026-10-26-residual-lot/imported/README.md). I gate ambientali restano aperti; nessuna VM o distribuzione avviata.

## Consegna

    pwsh -NoProfile -File scripts/prepare-release.ps1

Prepara copia locale e manifest v2 con hash, pacchetti risolti alla compilazione (inclusi strumenti di build), runtime pack e stato Authenticode. Verifica ricevuta, sorgenti/assets correnti e byte degli script del bundle; EXE precedente, sorgente firmato dopo la build o assets cambiati richiedono una nuova build. `-RequireSigned` controlla le firme dell'EXE e degli script effettivamente incorporati, senza ricodificarli. Firma opzionale con certificato aziendale e percorso esplicito SignTool; usare questa procedura anche per aggiornare la ricevuta dopo la firma. Una firma esterna che cambia l'EXE invalida la vecchia ricevuta.

Firmare ps1/psm1 **prima** di build.cmd. La modalità di sviluppo `OnlyTweakWindows.exe --release-inspect` restituisce su stdout soltanto metadati e risorse incorporate, senza UI, profili o operazioni sul sistema; non certifica la compatibilità dell'app su quel sistema. Le policy possono bloccare script non approvati: l'app lo segnala senza aggirarle.

La build attuale è **non firmata**, destinata alla qualificazione in laboratorio. Documentazione tecnica e fonti Microsoft: [docs/ENTERPRISE.md](docs/ENTERPRISE.md). Storico UI: [docs/UI-UX.md](docs/UI-UX.md). Gate residui: [tracker SecondBrain](../SecondBrain/02-areas/products/OnlyTweak/tracker.json).


## Ripresa nella prossima sessione

La fonte delle attività è [tracker SecondBrain](../SecondBrain/02-areas/products/OnlyTweak/tracker.json): **zero task applicativi aperti** dopo la chiusura verificata di OT-AUDIT-16/18/19/20/21/22/23. Schede originali, criteri ed esiti sono conservati nello storico del tracker SecondBrain OnlyTweak, insieme ai lotti precedenti. I sette gate preesistenti OT-VM-01..04 e OT-ENT-LAB-01/SIGN-01/PILOT-01 e il nuovo OT-VM-TWEAK-01 restano separati e non dichiarati superati: i test VM sono esclusi da questa chiusura per scelta dell'utente. Nessun nuovo lotto ambientale viene avviato automaticamente. Le correzioni OT-AUDIT-08/09/10 e la rimozione OT-AUDIT-11 rimangono storico; il salvataggio dei sorgenti non equivale a distribuzione o validazione in produzione.

Verifica del lotto navigazione/tweak: 121 test desktop e 91 test CI locale superati, zero fallimenti. [Rapporto](../SecondBrain/02-areas/products/OnlyTweak/history/2026-10-26-navigation-tweaks/report.md).
