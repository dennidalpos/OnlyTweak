# OnlyTweakWindows

Toolkit portatile per **Windows 11 x64 Intel/AMD**, con interfaccia italiana scura e categorie organizzate in finestre modali. Il file da distribuire è soltanto `dist\OnlyTweakWindows.exe`.

## Avvio

Apri `OnlyTweakWindows.exe` e autorizza la richiesta amministratore di Windows. Non occorrono installazione, Visual Studio, SDK, pacchetti NuGet o download di runtime. L'app utilizza **.NET Framework 4.x già incluso in Windows 11**, distinto dal moderno .NET Desktop Runtime.

L'eseguibile non è firmato digitalmente. Windows o le policy aziendali possono quindi richiedere un'ulteriore verifica dell'editore o impedirne l'avvio. L'app non modifica automaticamente tali protezioni: i comandi della categoria Compatibilità e protezioni sono utilizzabili solo dopo l'avvio e richiedono conferma.

La finestra iniziale indica versione/build e account effettivo. I tweak personali e le connessioni stampante riguardano **l'account che esegue l'app**: se nell'UAC inserisci credenziali di un altro amministratore, riguardano quell'amministratore. Gli interventi su servizi, rete e sistema riguardano il computer.

Proprietà, preferenze, code stampante, impostazioni e strumenti Windows si aprono **direttamente dal relativo pulsante**, senza un'altra schermata Esegui. Doppio clic o Invio sulla stampante selezionata apre le proprietà. Le sottosezioni brevi di Rete e Manutenzione sono gruppi nello stesso modale; i tweak di Esplora file mostrano stato e comandi nello stesso pannello.

I tweak reversibili si applicano direttamente, con esito inline. Diagnostica e manutenzione aprono il risultato e partono automaticamente. Le operazioni che eliminano lavori o ripristinano componenti richiedono una sola conferma esplicita, con Annulla predefinito. Un'operazione interna alla volta; durante l'esecuzione non è possibile chiudere il suo dialogo. Gli strumenti Windows aperti in finestre esterne mantengono il proprio ciclo di vita.

## Funzioni

| Categoria | Operazioni |
|---|---|
| Stampanti | Elenco aggiornabile, stampante predefinita, proprietà, preferenze, coda, annullamento dei lavori della selezionata, impostazioni Stampanti e scanner. |
| Spooler | Lettura stato, riavvio senza cancellazione dei file, pulizia globale dei file di coda locali. |
| Aspetto ed Esplora file | Menu contestuale classico/Windows 11, estensioni, file nascosti, caselle di selezione, riavvio esplicito della shell. |
| Rete | Schede, IP, gateway e DNS, `ipconfig /all`, pulizia cache DNS, reset Winsock, reset TCP/IP, connessioni di rete e proxy. |
| Manutenzione | `sfc /verifyonly`, `sfc /scannow`, `DISM /Online /Cleanup-Image /ScanHealth /NoRestart`, `DISM /Online /Cleanup-Image /RestoreHealth /NoRestart`, Pulizia disco e Archiviazione. |
| Strumenti Windows | Gestione dispositivi, Servizi, Visualizzatore eventi, Gestione attività, applicazioni installate, applicazioni all'avvio, Windows Update e Informazioni di sistema. |
| Compatibilità e protezioni | Stato e gestione separata di Smart App Control, SmartScreen, PUA, origine delle app, integrità della memoria, protezione stack kernel e blocklist driver vulnerabili. |
| Account | Passaggio ad account locale, account delle app, lavoro/scuola, altri utenti, netplwiz, Utenti e gruppi locali dove disponibile, portale Microsoft e guida alla chiusura online. |

Stampanti offre **Vista classica** (Dispositivi e stampanti) e, separatamente, **Stampanti e scanner** nelle Impostazioni moderne. Strumenti Windows include **Gestione computer**, **Pannello di controllo** e **Programmi e funzionalità** prima dell'alternativa moderna Applicazioni installate. Componenti, console e destinazioni Shell sono controllati prima dell'avvio; Windows può comunque reindirizzare una vista su build differenti. La sola richiesta di apertura non certifica la finestra effettivamente mostrata.

Ogni area ha un'icona del font di sistema Segoe Fluent Icons, incluso in Windows 11. Le card mostrano una riga breve; avvertenze e dettagli sono nel tooltip, anche tramite l'icona Info (clic, oppure Invio, Spazio o F1 da tastiera). I tooltip si chiudono da soli dopo 10 secondi e quando la finestra si sposta, perde il primo piano o si chiude. I testi completi restano nelle conferme e per i lettori di schermo.

Titolo e contorni della finestra, pulsanti, conferme, riquadri, selezioni, avanzamento e scrollbar dell'app condividono una palette scura con accento verde acqua. Le scrollbar verticali dei menu e della lista stampanti e quelle verticali/orizzontali dell'output sono disegnate dall'app. I dialoghi esterni Windows e dei driver mantengono il proprio tema di sistema.

Tab e Maiusc+Tab spostano il focus e mantengono visibili i comandi nei gruppi scorrevoli. Invio attiva il pulsante selezionato/predefinito ed Esc chiude il modale quando non è in corso un'operazione. La lista stampanti supporta frecce, Home/Fine, Pagina su/giù e gli elementi accessibili per lettori di schermo. Le finestre sono ridimensionabili.

La rotella agisce sul contenitore sotto il puntatore nella finestra attiva, anche sopra pulsanti, etichette e pannelli annidati, senza spostare il focus o scorrere due volte. Rispetta il numero di righe/pagine impostato in Windows e accumula i piccoli delta. Nell'output, Maiusc+rotella e la rotella orizzontale scorrono sul secondo asse.

### Compatibilità e protezioni

Tutti i comandi diretti di questa categoria riguardano **il computer e tutti gli utenti**. Ogni modifica ha una conferma con Annulla predefinito, viene eseguita in background e viene riletta. Configurazione ed esecuzione effettiva sono distinte: il pannello non presenta un valore di registro come prova che una protezione sia già attiva o disattiva.

| Voce | Gestione |
|---|---|
| Smart App Control | Stato configurato e pagina ufficiale Windows; nessuna modifica diretta al registro. La riattivazione dipende da aggiornamenti e disponibilità del dispositivo. |
| SmartScreen «Controlla app e file» | Criterio locale: `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`, `EnableSmartScreen` 0/1 e `ShellSmartScreenLevel` `Warn`. L'attivazione consente quindi l'avviso superabile, non imposta il blocco senza esclusione. |
| PUA | Cmdlet Windows Defender `Get-MpPreference` / `Set-MpPreference -PUAProtection`; preserva anche il valore Audit nel ripristino. Il filtro dei download del browser resta separato. |
| Origine delle app | Criterio locale in `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen`: `ConfigureAppInstallControlEnabled` 1 e `ConfigureAppInstallControl` `Anywhere`/`StoreOnly`. Non modifica la modalità S. |
| Integrità della memoria | `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity`, `Enabled` 0/1. Non modifica automaticamente VBS o altre protezioni. |
| Stack kernel | Criterio locale `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard`, `ConfigureKernelShadowStacksLaunch` 3/1 (disattivato/attivato). La riattivazione diretta richiede VBS/HVCI in esecuzione e supporto stack già osservato in esecuzione da Windows nella sessione. |
| Driver vulnerabili | Stato configurato e pagina Isolamento core; segnala che HVCI, SAC o modalità S possono imporre la blocklist anche quando il valore locale è disattivato. |

I criteri diretti richiedono un'edizione supportata e il relativo modello ADMX presente. In assenza dei requisiti resta disponibile la pagina Windows. Dispositivi rilevati come gestiti da dominio/organizzazione, verifica di gestione non riuscita, criteri preesistenti esterni alla sessione, blocchi UEFI e antimanomissione impediscono le modifiche pertinenti: l'app non li rimuove. Se una configurazione di criterio cambia fuori dall'app dopo un intervento, non viene sovrascritta dal successivo comando o ripristino.

Il servizio usa esplicitamente **HKLM, vista x64**, separato dai tweak personali HKCU. Prima del primo intervento conserva in memoria lo stato originale, tipi, valori e assenze; il ripristino conserva chiavi preesistenti e dati estranei. In caso di errore tenta il recupero della configurazione precedente al tentativo e riporta separatamente un eventuale recupero incompleto. Per PUA conserva il valore effettivamente restituito dal cmdlet. Non produce backup persistenti delle impostazioni.

HVCI e stack indicano un riavvio manuale pendente dopo i comandi; la riattivazione HVCI senza una precedente esecuzione verificata o configurazione VBS disponibile viene rimandata a Windows. Nessun riavvio è automatico. **Chiudendo l'app si perdono gli snapshot**: i criteri persistenti creati diventano esterni alla nuova sessione e vanno gestiti con gli strumenti Windows. Non è promesso il ripristino di ogni protezione con un singolo pulsante fra riavvii.

La diagnostica e i comandi PUA usano esclusivamente **Windows PowerShell incluso nel sistema**, senza PowerShell 7, script scaricati o modifiche a ExecutionPolicy. Se il componente non è disponibile, l'azione resta non disponibile.

### Account e contesto della sessione

Le pagine personali, Sicurezza Windows, le destinazioni Shell e i link Microsoft vengono richiesti con il token e l'ambiente dell'utente della shell desktop della stessa sessione, senza elevare il browser. Se il contesto non è accessibile, l'app mostra l'errore. Questo comportamento non cambia l'attribuzione dei tweak HKCU già descritta sopra.

Passaggio ad account locale, scollegamento degli account delle app/lavoro e rimozione di utenti si completano nelle interfacce Windows. La rimozione dal PC può eliminare dati locali e non chiude l'account Microsoft online. Netplwiz e Utenti e gruppi locali sono strumenti amministrativi del computer; quest'ultimo è controllato anche per edizione e presenza della console.

**Procedura di chiusura dell'account Microsoft** apre la guida ufficiale nel browser dell'utente: riguarda anche servizi e dati associati. Accesso, verifica dell'account, eventuali requisiti e conferma finale restano sul sito Microsoft. L'app non raccoglie credenziali, non elimina profili automaticamente e non effettua richieste di cancellazione online.

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

Lo script usa `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (con fallback al percorso Framework), linguaggio **C# 5**, target Windows GUI x64 e manifest incorporato con `requireAdministrator` e dichiarazione DPI. Produce soltanto `dist\OnlyTweakWindows.exe`. Nessun file `.config`, DLL o script è necessario accanto all'EXE. Per ricompilare, chiudi eventuali copie dell'eseguibile di destinazione.

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
- Protezioni simulate: criteri e snapshot HKLM, tipi/assenze, PUA Audit, scritture ignorate o fallite, recupero parziale, gestione/policy/UEFI/antimanomissione, prerequisiti e stato runtime distinto. Launcher interattivo sostituito da un fake nei test.
- `--artifact`: PE x64/Windows GUI, manifest UAC/DPI incorporato, sole dipendenze framework e singolo file nella cartella dist.
- `--ui`: aperture dirette senza modali intermedi, avvio automatico, singola conferma e annullamento, scorrimento/focus, output con entrambe le scrollbar, lista lunga stampanti e accessibilità; successo/errore con azioni simulate, chiusura bloccata e controlli riabilitati.
- `--read-only`: lettura reale di registro, protezioni/CIM/Defender, stampanti, spooler e schede di rete. Non modifica il sistema.
- `--render`: anteprime delle finestre a fattori di scala 100%, 150% e 200% in `artifacts\ui`. Apre brevemente le finestre del banco di prova, senza applicare tweak.

La CI GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) esegue `build.cmd` e `test.cmd --artifact --read-only` su `windows-2025`, che include .NET Framework 4.8, a ogni push su `master` e pull request. `--ui` e `--render` restano locali perché richiedono una sessione desktop. Il runner è Windows Server, non sostituisce le prove su Windows 11.

### Stato verificato il 16 ottobre 2026

Compilazione senza avvisi con il compilatore Windows .NET Framework **14.8.10001.0**, compatibile C# 5. Ambiente reale: **Windows 11 26H2, build 26300.9711 x64**. Lettura riuscita delle cinque stampanti installate, identificazione della predefinita, stato spooler, preflight della cartella standard e diagnostica rete.

Versione **1.2.0**: rotella centralizzata, viste classiche, Gestione computer, protezioni e account. La suite completa ha **71 verifiche superate**. Le anteprime delle finestre reali, incluse le nuove categorie, sono acquisite con PrintWindow e ispezionate; il ridimensionamento a scala simulata non equivale a una prova su tre monitor con DPI reali differenti. Dettagli in [docs/UI-UX.md](docs/UI-UX.md).

Da completare in una VM Windows 11 x64 pulita e sacrificabile: avvio del solo EXE con UAC, navigazione e DPI reali 100/150/200%, apertura dei dialoghi driver, stampante di rete offline, spooler fermo/disabilitato, annullamento di lavori campione, pulizia reale, riavvio shell, reset rete, SFC/DISM e confronto visivo del menu classico prima/dopo. Nessuna di queste operazioni modificanti è stata eseguita sulla postazione di sviluppo. Compatibilità delle build stabili 24H2/25H2 non ancora verificata direttamente; ARM64 escluso.

Restano da verificare nella VM le modifiche effettive alle protezioni e il confronto dopo il riavvio, i blocchi gestiti/UEFI, l'apertura e i reindirizzamenti delle viste classiche, le pagine account e il lancio interattivo con UAC tramite un altro amministratore. Non eseguire una chiusura reale di account Microsoft come test.

Il repository Git conserva sorgenti, test e documentazione. `dist/` e `artifacts/` sono esclusi: eseguibile, rendering, log, backup e pacchetti di consegna vengono generati localmente. Anche `AGENTS.md`, distribuito da SecondBrain con i percorsi di questa postazione, e le configurazioni personali degli editor sono esclusi.

Per la distribuzione basta `dist\OnlyTweakWindows.exe`; non copiare test, log o backup accanto al file. La consegna 1.2.0 viene preparata anche come archivio ZIP con il solo EXE e checksum SHA-256 separati. I controlli locali non sostituiscono le prove modificanti in VM elencate sopra.

## Fonti primarie

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
- [Protezione stack kernel](https://learn.microsoft.com/en-us/windows-server/security/kernel-mode-hardware-stack-protection) e modello `DeviceGuard.admx` presente nel sistema; i requisiti citano Windows 11 2022 Update o successivo.
- [Smart App Control: disponibilità e riattivazione](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
- [Blocklist driver e dipendenze](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules)
- [Pagine ufficiali Controllo app/browser](https://support.microsoft.com/en-gb/windows/security/windows-security/app-browser-control-in-the-windows-security-app?nochrome=true) e [Sicurezza dispositivi](https://support.microsoft.com/en-us/windows/security/windows-security/device-security-in-the-windows-security-app), fonte dei collegamenti `windowsdefender:`.
- [Gestione account Windows](https://support.microsoft.com/en-us/windows/security/identity-signin/manage-user-accounts-in-windows) e [chiusura account Microsoft](https://support.microsoft.com/en-us/accounts-billing/manage/how-to-close-your-microsoft-account)
