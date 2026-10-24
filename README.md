# OnlyTweakWindows

Toolkit portatile per **Windows 10 dalla 1809 e Windows 11, x64 Intel/AMD**, con interfaccia italiana scura e categorie organizzate in finestre modali. Il file da distribuire è soltanto `dist\OnlyTweakWindows.exe`.

## Avvio

Apri `OnlyTweakWindows.exe` e autorizza la richiesta amministratore di Windows. Non occorrono installazione, Visual Studio, SDK, pacchetti NuGet o download di runtime. L'app utilizza **.NET Framework già incluso in Windows: API compatibili con la 4.7.2 di Windows 10 1809 e versioni successive**, distinto dal moderno .NET Desktop Runtime.

L'eseguibile non è firmato digitalmente. Windows o le policy aziendali possono quindi richiedere un'ulteriore verifica dell'editore o impedirne l'avvio. L’app non modifica queste protezioni: la categoria Compatibilità e protezioni mostra lo stato e apre le pagine Windows.

La finestra iniziale indica versione/build e account effettivo. I tweak personali e le connessioni stampante riguardano **l'account che esegue l'app**: se nell'UAC inserisci credenziali di un altro amministratore, riguardano quell'amministratore. Gli interventi su servizi, rete e sistema riguardano il computer.

Proprietà, preferenze, code stampante, impostazioni e strumenti Windows si aprono **direttamente dal relativo pulsante**, senza un'altra schermata Esegui. Doppio clic o Invio sulla stampante selezionata apre le proprietà. Le sottosezioni brevi di Rete e Manutenzione sono gruppi nello stesso modale; i tweak di Esplora file mostrano stato e comandi nello stesso pannello.

I tweak reversibili si applicano direttamente, con esito inline. Diagnostica e manutenzione aprono il risultato e partono automaticamente. Le operazioni che eliminano lavori o ripristinano componenti richiedono una sola conferma esplicita, con Annulla predefinito. Un'operazione interna alla volta; durante l'esecuzione non è possibile chiudere il suo dialogo. Gli strumenti Windows aperti in finestre esterne mantengono il proprio ciclo di vita.

## Funzioni

| Categoria | Operazioni |
|---|---|
| Stampanti | Elenco aggiornabile, stampante predefinita, proprietà, preferenze, coda, annullamento dei lavori della selezionata, impostazioni Stampanti e scanner. |
| Spooler | Lettura stato, riavvio senza cancellazione dei file, pulizia globale dei file di coda locali. |
| Aspetto ed Esplora file | Menu contestuale classico solo su Windows 11, estensioni, file nascosti, caselle di selezione, riavvio esplicito della shell. |
| Rete | Schede, IP, gateway e DNS, `ipconfig /all`, pulizia cache DNS, reset Winsock, reset TCP/IP, connessioni di rete e proxy. |
| Manutenzione | `sfc /verifyonly`, `sfc /scannow`, `DISM /Online /Cleanup-Image /ScanHealth /NoRestart`, `DISM /Online /Cleanup-Image /RestoreHealth /NoRestart`, Pulizia disco e Archiviazione. |
| Strumenti Windows | Gestione dispositivi, Servizi, Visualizzatore eventi, Gestione attività, applicazioni installate, applicazioni all'avvio, Windows Update e Informazioni di sistema. |
| Compatibilità e protezioni | Stato e pagine Windows, senza modifiche dirette, per Smart App Control, SmartScreen, PUA, origine delle app, integrità della memoria, protezione stack kernel e blocklist driver vulnerabili. |
| Account | Passaggio ad account locale, account delle app, lavoro/scuola, altri utenti, netplwiz, Utenti e gruppi locali dove disponibile. |

Stampanti offre **Vista classica** (Dispositivi e stampanti) e, separatamente, **Stampanti e scanner** nelle Impostazioni moderne. Strumenti Windows include **Gestione computer**, **Pannello di controllo** e **Programmi e funzionalità** prima dell'alternativa moderna Applicazioni installate. Componenti, console e destinazioni Shell sono controllati prima dell'avvio; Windows può comunque reindirizzare una vista su build differenti. La sola richiesta di apertura non certifica la finestra effettivamente mostrata.

L’eseguibile e tutte le finestre incorporano l’icona OnlyTweak con tre cursori verde menta. Il sorgente è `assets/OnlyTweak.svg`; anteprima PNG e ICO 16/20/24/32/48/64/128/256 px sono nello stesso percorso. `powershell -NoProfile -File scripts/create-icon.ps1` rigenera i raster con System.Drawing di Windows, senza pacchetti.

Le icone delle aree usano Segoe Fluent Icons, con Segoe MDL2 Assets come alternativa su Windows 10. Le card mostrano una riga breve; avvertenze e dettagli sono nel tooltip, anche tramite l'icona Info (clic, oppure Invio, Spazio o F1 da tastiera). I tooltip si chiudono da soli dopo 10 secondi e quando la finestra si sposta, perde il primo piano o si chiude. I testi completi restano nelle conferme e per i lettori di schermo.

Titolo e contorni della finestra, pulsanti, conferme, riquadri, selezioni, avanzamento e scrollbar dell'app condividono una palette scura con accento verde acqua. Le scrollbar verticali dei menu e della lista stampanti e quelle verticali/orizzontali dell'output sono disegnate dall'app. I dialoghi esterni Windows e dei driver mantengono il proprio tema di sistema.

Tab e Maiusc+Tab spostano il focus e mantengono visibili i comandi nei gruppi scorrevoli. Invio attiva il pulsante selezionato/predefinito ed Esc chiude il modale quando non è in corso un'operazione. La lista stampanti supporta frecce, Home/Fine, Pagina su/giù e gli elementi accessibili per lettori di schermo. Le finestre sono ridimensionabili.

La rotella agisce sul contenitore sotto il puntatore nella finestra attiva, anche sopra pulsanti, etichette e pannelli annidati, senza spostare il focus o scorrere due volte. Rispetta il numero di righe/pagine impostato in Windows e accumula i piccoli delta. Nell'output, Maiusc+rotella e la rotella orizzontale scorrono sul secondo asse.

### Compatibilità Windows 10/11

Il perimetro previsto comprende Windows 10 Home/Pro/Enterprise/Education dalla 1809 (build 17763), Enterprise e IoT Enterprise LTSC 2019/2021 e Windows 11 x64. Windows Server, ARM64 e processi a 32 bit vengono rifiutati all’avvio. La compatibilità su installazioni pulite è ancora da convalidare in VM.

Il profilo rileva versione nativa, tipo client/server, architettura nativa, build, revisione, edizione e DisplayVersion (alternativa ReleaseId). Non deduce Windows 11 dal solo ProductName. Comandi SFC, DISM, rete e spooler restano condivisi; file, console e destinazioni vengono verificati prima dell’avvio. Componenti non disponibili, funzioni di altre edizioni e sezioni vuote vengono nascosti.

| Funzione | Applicabilità |
|---|---|
| Estensioni, nascosti, caselle | Windows 10/11; account che esegue l’app. |
| Tweak menu classico | Solo Windows 11; non ufficiale e dipendente dalla build. |
| Smart App Control e stack kernel | Windows 11 dalla 22H2; disponibilità effettiva e prerequisiti si verificano nella pagina Windows. |
| Integrità memoria, SmartScreen, PUA, origine app | Stato su Windows 10/11; nessun comando di modifica nell’app. |
| Blocklist driver | Stato distinto dall’interruttore dedicato, disponibile nelle versioni Windows pertinenti; HVCI/modalità S possono imporla. |
| Utenti e gruppi locali | Solo edizioni Professional/Enterprise/Education e derivate, con console installata; nascosto su Home. |

Su Windows 10 i collegamenti più recenti al filtro app usano la pagina padre Controllo app/browser. Senza gestore Sicurezza Windows viene proposta la pagina generale `ms-settings:windowsdefender`, se disponibile. Il gestore di un protocollo non certifica la pagina visualizzata: la matrice delle destinazioni è basata sugli URI documentati; reindirizzamenti e policy richiedono verifica reale. Non si installano componenti mancanti.

### Compatibilità e protezioni

Questa categoria è **esclusivamente di lettura**: servizio e backend non espongono più scritture HKLM, cmdlet di modifica, snapshot o ripristini. Il pulsante Apri pagina Windows affida le modifiche agli strumenti di sistema; Aggiorna stato rilegge il computer.

I colori distinguono le evidenze: verde per PUA in blocco con Defender attivo e per HVCI/stack osservati in esecuzione; neutro per Audit, stato sconosciuto e sola configurazione; avviso per protezione inattiva o errore. Valori mancanti e letture incomplete non diventano protezioni attive sulla base di presunti predefiniti di una fresh install. Configurazione ed effetto restano separati.

La diagnostica usa esclusivamente **Windows PowerShell incluso nel sistema**, senza PowerShell 7, script scaricati o modifiche a ExecutionPolicy. Se una lettura non riesce, la relativa scheda resta visibile con stato sconosciuto.

Il pulsante Conferma delle altre operazioni usa testo e bordo di avviso; Annulla resta predefinito. Misure del contrasto e limiti delle anteprime sono in [docs/UI-UX.md](docs/UI-UX.md).

### Account e contesto della sessione

Le pagine personali, Sicurezza Windows e le destinazioni Shell vengono aperte con il token e l'ambiente dell'utente della shell desktop della stessa sessione. Se il contesto non è accessibile, l'app mostra l'errore. Questo comportamento non cambia l'attribuzione dei tweak HKCU già descritta sopra.

Passaggio ad account locale, scollegamento degli account delle app/lavoro e rimozione di utenti si completano nelle interfacce Windows. La rimozione dal PC può eliminare dati locali e non chiude l'account Microsoft online. Netplwiz e Utenti e gruppi locali sono strumenti amministrativi del computer; quest'ultimo è controllato anche per edizione e presenza della console.

### Registro e ripristino

Prima della prima modifica a ciascun tweak viene conservato in memoria il valore originale, compresi tipo e precedente assenza. **Ripristina valore precedente** resta disponibile nella sessione e torna sempre al valore iniziale di quella sessione. Le chiavi originariamente vuote e i dati estranei sono preservati.

Non vengono salvati automaticamente backup, preferenze o cronologia dell'app. Chiudendo l'app perdi la possibilità di ripristinare quei valori precedenti dalla sessione, mentre **le modifiche applicate a Windows persistono**. I pulsanti opposti restano disponibili agli avvii successivi; non ricostruiscono impostazioni personalizzate perdute con la sessione. Copia output e Copia testo scrivono negli appunti solo su richiesta.

Valori modificati in `HKCU`:

| Tweak | Percorso e valore | Attivo / opposto |
|---|---|---|
| Menu classico | `Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, valore predefinito | Stringa vuota / rimozione del solo valore riconosciuto |
| Estensioni | `Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt` | DWORD 0 / 1 |
| File nascosti | Stessa chiave, `Hidden` | DWORD 1 / 2 |
| Caselle di selezione | Stessa chiave, `AutoCheckSelect` | DWORD 1 / 0 |

Il **menu classico è un tweak non ufficiale**, dipendente dalla build: lo stato del registro non certifica l'effetto visivo. Dopo una modifica può servire il riavvio di Esplora risorse o una disconnessione. Se il valore COM contiene dati personalizzati, il pulsante Menu Windows 11 non li cancella; è disponibile il ripristino esatto se salvato nella sessione.

Il riavvio della shell riguarda il desktop della sessione corrente solo se appartiene all'account dell'app. Viene conservato il token originale della shell per evitare di riavviarla con privilegi elevati. Completa prima copie e spostamenti. Non vengono terminati in massa gli altri processi `explorer.exe`.

### Stampa e spooler

L'elenco usa le API di stampa Windows e la cache locale delle connessioni. Un server non raggiungibile può comunque rallentare apertura dei dialoghi o annullamento dei lavori; questi interventi non bloccano il thread grafico. Se mancano i permessi sul server, l'errore viene mostrato.

L'annullamento riguarda la sola stampante selezionata; per una stampante condivisa può coinvolgere lavori di altri utenti. Un lavoro già in stampa o trasferito al dispositivo può proseguire. Non vengono eliminati stampanti o driver.

La pulizia globale:

1. Verifica configurazione globale/per stampante e cartella standard `%WINDIR%\System32\spool\PRINTERS`.
2. Blocca percorsi personalizzati, UNC, relativi, cartelle mancanti o reindirizzate. Il valore per stampante vuoto significa eredita il percorso globale.
3. Ferma il servizio e ne attende l'arresto, con limite di 30 secondi.
4. Ricontrolla i percorsi ed elimina solo `.SPL` e `.SHD` al primo livello, senza seguire reindirizzamenti o eliminare directory.
5. Tenta sempre il riavvio dopo il tentativo di arresto, riportando separatamente errori di pulizia e riavvio.

Se il controllo iniziale fallisce, il servizio non viene toccato. I servizi dipendenti attivi bloccano l'arresto. Il tipo di avvio dello spooler resta invariato; un servizio disabilitato può quindi non riavviarsi e l'errore viene mostrato. Riavvio e pulizia avviano lo spooler anche se prima era fermo.

### Rete e manutenzione

I reset rete sono indipendenti e possono interrompere la connessione. Prima di un reset TCP/IP annota IP statici, gateway e DNS: potrebbe essere necessario riconfigurarli. Non è previsto un rollback automatico di queste operazioni.

SFC e DISM possono impiegare diversi minuti. DISM RestoreHealth può scaricare componenti da Windows Update. Leggi il riepilogo dello strumento: la terminazione del processo o il solo codice di uscita non descrivono tutti i risultati di integrità. L'app non forza l'interruzione delle riparazioni e non riavvia automaticamente il PC. I normali log di Windows (per esempio CBS e DISM) continuano a essere prodotti dagli strumenti di sistema. Il testo mostrato dall'app è limitato in memoria e può essere copiato.

## Compilazione

Da questa cartella, in Prompt dei comandi o PowerShell:

```bat
.\build.cmd
```

Lo script usa `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (con fallback al percorso Framework), linguaggio **C# 5**, target Windows GUI x64 e manifest incorporato con `requireAdministrator`, dichiarazione DPI e icona nativa/risorsa gestita. Produce soltanto `dist\OnlyTweakWindows.exe`. Nessun file `.config`, DLL o script è necessario accanto all'EXE. Per ricompilare, chiudi eventuali copie dell'eseguibile di destinazione.

Struttura: `src` contiene UI, catalogo azioni e servizi separati; `tests` contiene i fake e il banco di prova; `artifacts` e `dist` sono output generati. Le API Win32 sono richiamate tramite P/Invoke, senza wrapper esterni.

## Verifiche

```bat
.\build.cmd
.\test.cmd --artifact --ui --read-only --render
```

La suite si compila con lo stesso compilatore di sistema ed esegue:

- Ripristino registro simulato: valori personalizzati, assenti, binari e MultiString, chiavi preesistenti, dati estranei, scritture fallite e rilettura di conferma.
- Spooler simulato: percorsi ammessi/vietati, reindirizzamenti, file fuori percorso, errori parziali e tentativo di avvio anche dopo errore di arresto.
- Quoting degli argomenti, catalogo, coordinamento e cronologia.
- Profili OS simulati: build 17763/18362/18363/19041/19042/19043/19044/19045, LTSC, Windows 11, edizioni e architetture escluse; cataloghi filtrati e sezioni vuote.
- Protezioni simulate esclusivamente di lettura: configurazione/runtime, PUA Audit, letture parziali o fallite, stato sconosciuto e pagine Windows. Launcher interattivo sostituito da un fake nei test.
- `--artifact`: PE x64/Windows GUI, manifest UAC/DPI e icona incorporati, estrazione icona tramite shell, metadato Framework 4.7.2, sole dipendenze framework e singolo file nella cartella dist.
- `--ui`: aperture dirette senza modali intermedi, avvio automatico, singola conferma e annullamento, scorrimento/focus, output con entrambe le scrollbar, lista lunga stampanti e accessibilità; successo/errore con azioni simulate, chiusura bloccata e controlli riabilitati.
- `--read-only`: lettura reale di registro, protezioni/CIM/Defender, stampanti, spooler e schede di rete. Non modifica il sistema.
- `--render`: anteprime delle finestre a fattori di scala 100%, 150% e 200% in `artifacts\ui`. Include home, Esplora file e protezioni per sei profili OS simulati. Apre brevemente le finestre del banco di prova, senza applicare tweak.

La CI GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) esegue `build.cmd` e `test.cmd --artifact --read-only` su `windows-2025`, che include .NET Framework 4.8, a ogni push su `master` e pull request. `--ui` e `--render` restano locali perché richiedono una sessione desktop. Il runner è Windows Server, non sostituisce le prove su Windows 10/11.

### Attività aperte per la prossima sessione

La fonte unica delle attività aperte è [PROJECT_STATUS.json](PROJECT_STATUS.json): quattro difetti confermati `OT-AUDIT-08..11` (stato Defender vuoto/non riconosciuto, isolamento dell'errore SMode, limite dell'output, chiave vuota dopo ripristini condivisi) e le quattro verifiche ambientali `OT-VM-01..04`. Ogni voce contiene priorità, evidenze, ambito, dipendenze e criteri di completamento. Il salvataggio Git conserva questi residui aperti; le correzioni sotto sono già concluse. Lo storico dettagliato resta nel dossier SecondBrain della postazione.

### Stato verificato il 24 ottobre 2026

Audit generale e correzioni locali: il controllo di disponibilità cerca ora `explorer.exe` nella cartella Windows, rendendo visibile il riavvio shell quando il componente esiste. Restano obbligatorie conferma e verifica dell'account prima del riavvio. La lettura protezioni conserva lo stato sconosciuto per risultati CIM nulli, proprietà assenti, output incompleti e marcatori finali di errore, evitando falsi stati inattivi o evidenze residue della lettura precedente. Una lista CIM presente ma vuota indica invece nessun servizio in esecuzione.

`build.cmd` riuscito senza avvisi; `test.cmd --artifact --ui --read-only --render`: **73 superati, 0 falliti**; comando CI locale `test.cmd --artifact --read-only`: **51 superati, 0 falliti**. Rapporti: `artifacts/audit-2026-10-24-full.log` e `artifacts/audit-2026-10-24-ci.log`. Le quattro nuove regressioni coprono percorso/disponibilità Explorer, letture Device Guard incomplete, errori finali/riletture e query PowerShell con CIM simulato. Tre regressioni fallivano sul codice precedente; il caso CIM verifica anche il comando effettivo con funzioni simulate in Windows PowerShell.

Ispezionati render desktop di home, Esplora file e protezioni e campioni scalati al 150/200%; nessuna vista smartphone/tablet prevista. Restano aperte le prove `OT-VM-01..04`: installazioni pulite, Framework 4.7.2 effettivo, UAC multiutente, DPI reali, destinazioni Windows e manutenzione modificante. Nessuna prova modificante reale durante l'audit; gli esiti CI remoti del salvataggio sono consultabili in [GitHub Actions](https://github.com/dennidalpos/OnlyTweak/actions).

Riferimenti verificati: Microsoft documenta [SecurityServicesRunning e i valori HVCI/stack kernel](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity#use-win32_deviceguard-wmi-class), disponibili solo sulle versioni pertinenti; [SpecialFolder.Windows e System nel Framework 4.7.2](https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder?view=netframework-4.7.2) distinguono le directory usate dal controllo dei componenti. La documentazione non sostituisce le prove in VM sulle versioni target.

### Storico verifiche del 19 ottobre 2026

Compilazione senza avvisi con il compilatore Windows .NET Framework **14.8.10001.0**, compatibile C# 5. Ambiente reale: **Windows 11 26H2, build 26300.9711 x64**. Lettura riuscita delle cinque stampanti installate, identificazione della predefinita, stato spooler, preflight della cartella standard e diagnostica rete.

Il codice aggiornato con icona, compatibilità Windows 10 e protezioni in sola lettura supera **69 verifiche, 0 fallite** con `test.cmd --artifact --ui --read-only --render` (`artifacts/os-compat-verification.log`). Il comando CI `test.cmd --artifact --read-only` passa localmente con **47/0** (`artifacts/os-compat-ci-command.log`). Il minor conteggio rispetto al ciclo precedente deriva dalla rimozione dei test delle scritture alle protezioni, sostituiti dalle verifiche di lettura e compatibilità. Questi sono risultati locali; gli esiti remoti del commit sono consultabili in [GitHub Actions](https://github.com/dennidalpos/OnlyTweak/actions).

Ispezionati icona, home, Esplora file e protezioni nei render dei profili simulati. Risorsa nativa estraibile dalla shell e icona delle finestre verificate; l’aspetto reale di Esplora file e barra delle applicazioni sulle versioni target resta da provare. Le anteprime 150/200% scalano i controlli e non certificano DPI reali. Targeting pack 4.7.2 assente sulla postazione: la build usa il Framework installato; l’esecuzione con la 4.7.2 inclusa nella 1809 resta da convalidare.

Il tracker operativo esterno è `02-areas/products/OnlyTweak/tracker.md` nel dossier SecondBrain. Nessuna VM disponibile: restano aperte installazioni pulite Windows 10 1809/LTSC 2019, 22H2, LTSC 2021 e Windows 11, edizioni Home/Pro, UAC anche con altro amministratore, DPI reali, icone della shell/taskbar, destinazioni Windows e dialoghi driver. Reset rete, SFC/DISM, pulizia reale spooler e riavvio shell si provano esclusivamente in VM sacrificabili; sulla postazione sono state eseguite soltanto simulazioni e letture. Per le protezioni si verificano letture e pagine, senza reintrodurre test di scrittura rimossi dall’app.

Il repository Git conserva sorgenti, test e documentazione. `dist/` e `artifacts/` sono esclusi: eseguibile, rendering, log, backup e pacchetti di consegna vengono generati localmente. Anche `AGENTS.md`, distribuito da SecondBrain con i percorsi di questa postazione, e le configurazioni personali degli editor sono esclusi.

Per la distribuzione basta `dist\OnlyTweakWindows.exe`; non copiare test, log o backup accanto al file. La consegna 1.2.0 viene preparata anche come archivio ZIP con il solo EXE e checksum SHA-256 separati. I controlli locali non sostituiscono le prove modificanti in VM elencate sopra.

## Fonti primarie

- [Architettura host con IsWow64Process2](https://learn.microsoft.com/en-us/windows/win32/api/wow64apiset/nf-wow64apiset-iswow64process2), distinta dall'architettura emulata.
- [Versioni Framework incluse e requisito 4.7.2](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)
- [Framework incluso nelle versioni di Windows](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)
- [Compilatore C# e opzioni](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/)
- [PrintUIEntry: proprietà, preferenze e coda](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/rundll32-printui)
- [EnumPrinters](https://learn.microsoft.com/en-us/windows/win32/printdocs/enumprinters) e [SetPrinter](https://learn.microsoft.com/en-us/windows/win32/printdocs/setprinter)
- [Pulizia e ripristino dello spooler](https://support.microsoft.com/en-US/Windows/Hardware/printer/fix-printer-connection-and-printing-problems-in-windows)
- [Percorso spool e troubleshooting](https://learn.microsoft.com/en-us/troubleshoot/windows-server/printing/troubleshoot-printing-scenarios)
- [SFC e DISM](https://support.microsoft.com/en-us/windows/experience/backup-recovery/use-the-system-file-checker-tool-to-repair-missing-or-corrupted-system-files)
- [Reset Winsock](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-winsock) e [ripristino rete](https://support.microsoft.com/en-us/windows/fix-wi-fi-connection-issues-in-windows-9424a1f7-6a3b-65a6-4d78-7f07eee84d2c)
- [URI impostazioni Windows](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Menu contestuale Windows 11 e accesso al menu classico](https://blogs.windows.com/blog/2021/07/19/extending-the-context-menu-and-share-dialog-in-windows-11/) — non documenta né garantisce il tweak COM.
- [DPI nel manifest](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)
- [Colori della barra del titolo e dei bordi Windows 11](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Lettura posizione di scorrimento Rich Edit](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos)
- [Creazione processo con token](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw)
- [Ambiente del processo per il token utente](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock)
- [Criteri SmartScreen e origine delle app](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-smartscreen)
- [PUA e cmdlet Defender](https://learn.microsoft.com/en-us/defender-endpoint/detect-block-potentially-unwanted-apps-microsoft-defender-antivirus)
- [Integrità della memoria e Win32_DeviceGuard](https://learn.microsoft.com/en-us/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity)
- [Protezione stack kernel](https://learn.microsoft.com/en-us/windows-server/security/kernel-mode-hardware-stack-protection), disponibile da Windows 11 2022 Update.
- [Smart App Control: disponibilità e riattivazione](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
- [Blocklist driver e dipendenze](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules)
- [Pagine ufficiali Controllo app/browser](https://support.microsoft.com/en-gb/windows/security/windows-security/app-browser-control-in-the-windows-security-app?nochrome=true) e [Sicurezza dispositivi](https://support.microsoft.com/en-us/windows/security/windows-security/device-security-in-the-windows-security-app), fonte dei collegamenti `windowsdefender:`.
- [Gestione account Windows](https://support.microsoft.com/en-us/windows/security/identity-signin/manage-user-accounts-in-windows)
