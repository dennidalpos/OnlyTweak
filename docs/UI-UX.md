# Revisione UI/UX — 1.2.0

## Contrasto e banco UI — 19 ottobre 2026

- Bordi `#758AA3` (prima `#2E3E50`); cursori scrollbar `#6F849D` (prima `#475D74`). Sfondi, testo e accento restano invariati. Il focus interno dei pulsanti pieni verdi usa il colore scuro dello sfondo per restare distinguibile dal riempimento.
- Rapporti sui token sRGB (tabella arrotondata; soglia verificata sui valori completi):

| Coppia | Prima | Dopo |
|---|---:|---:|
| Bordo / superficie | 1,47:1 | 4,51:1 |
| Bordo / hover | — | 3,89:1 |
| Bordo / selezione | — | 3,14:1 |
| Cursore / binario scrollbar | 2,55:1 | 4,51:1 |
| Focus scuro / pulsante verde | — | 11,46:1 |

- Riferimento di progetto: almeno 3:1 per gli indicatori controllati, secondo [W3C, contrasto non testuale](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html). Queste misure non costituiscono una certificazione complessiva di accessibilità; linee sottili e antialiasing richiedono anche ispezione visiva.
- Individuato e corretto nel banco un problema di ciclo di vita: le prove rotella/protezioni partivano dopo la chiusura del loop principale. Tutte le prove UI girano ora nello stesso `Application.Run`, con finestra host viva fino alla fine. Il nuovo percorso asincrono di errore per singola scheda riproduceva timeout ed errori `Dispose durante CreateHandle` con il vecchio banco e passa con il nuovo. Riferimenti .NET Framework: [Application.Run](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.run?view=netframework-4.8.1) e [WindowsFormsSynchronizationContext.AutoInstall](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.windowsformssynchronizationcontext.autoinstall?view=netframework-4.8.1).
- Verifica OT-03: build e suite completa **77/0**, inclusi errore asincrono per singola scheda e controlli esistenti di focus/scorrimento (`artifacts/ot-03-verification.log`). Misure in `artifacts/ot-03-contrast.log`; ispezionati home, protezioni e output al 100%. I render a 150/200% restano simulati; validazione DPI reali e tooltip interattivi in attesa di VM.

## Conferme con richiamo di avviso — 19 ottobre 2026

- Conferma ha testo, bordo e indicatore di focus nel colore di avviso su superficie scura; non usa più il riempimento verde. Hover e pressione mantengono il testo leggibile.
- Annulla resta selezionato all'apertura e predefinito sia per Invio sia per Esc. Una sola conferma precede l'avvio automatico dell'operazione.
- Verifica OT-02: build e suite completa **77/0** (`artifacts/ot-02-verification.log`); test di annullamento tramite pulsante predefinito, esecuzione singola simulata e ispezione del render della conferma al 100%.

## Stati verificabili — 19 ottobre 2026

- I tweak di Esplora file mostrano la configurazione in colore neutro: il registro non certifica l'effetto visivo del menu classico.
- Nelle protezioni, il verde indica PUA in modalità blocco con Defender attivo oppure HVCI/stack kernel osservati in esecuzione. PUA Audit, stati sconosciuti e sola configurazione restano neutri; protezione inattiva, errore e riavvio pendente usano il colore di avviso. Le descrizioni testuali continuano a distinguere ogni caso.
- Durante una nuova lettura il risultato precedente diventa neutro; gli errori sostituiscono anche il colore precedente, compreso il fallimento asincrono della lettura di una singola scheda.
- Verifica OT-01: build e suite completa, **77 superati, 0 falliti**, con due nuove prove su runtime e transizioni UI; rapporto locale `artifacts/ot-01-verification.log`. Restano necessarie le prove interattive in VM.

## Meno testo e icone delle aree — 18 ottobre 2026

- Icone dal font di sistema [Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font), incluso in Windows 11, con Segoe MDL2 Assets come alternativa. Nessun file aggiunto all'EXE; se nessuno dei due font è presente le icone vengono omesse.
- Dove compaiono le icone:
  - tessere della home e titolo di ogni finestra (Stampanti, Aspetto, Rete, Manutenzione, Protezioni, Account, Strumenti);
  - sezioni dei menu;
  - card delle azioni, per tipo: strumento esterno, lettura, intervento con conferma.
- Le card mostrano una riga breve, i sottotitoli una frase; avvertenze e contesto sono nel tooltip dell'icona Info, focalizzabile da tastiera. Il testo completo resta:
  - nelle conferme delle azioni distruttive;
  - nella finestra dell'operazione;
  - nella descrizione accessibile per i lettori di schermo.
- L'eyebrow «ONLYTWEAK / WINDOWS 11» resta solo nella home.
- Caratteri visibili, prima → dopo:

| Elemento | Prima | Dopo |
|---|---|---|
| Card | 2557 | 1141 |
| Sottotitoli | 1185 | 437 |
| Schede protezioni | 1052 | 298 |

- Colori e palette invariati; le icone usano i token esistenti (accento per le aree, testo secondario per i tipi, avviso per le conferme).
- Verifica: `test.cmd --artifact --ui --read-only --render`, 74 test superati. Coprono:
  - testi brevi (massimo 70 caratteri, una frase);
  - glifi dei menu;
  - tooltip e descrizione accessibile completi;
  - assenza del font icone senza errori.
- Correzione dei tooltip che restavano visibili sullo schermo:
  - **Causa:** l'icona Info era il primo controllo tabulabile, quindi riceveva il focus all'apertura e mostrava un tooltip senza scadenza. In più, i tooltip al passaggio del mouse duravano 30 s e non si chiudevano alla disattivazione della finestra.
  - **Ora:**
    - l'icona Info segue contenuti e pulsanti nell'ordine di tabulazione;
    - il dettaglio si apre solo con clic, Invio, Spazio o F1 e dura 10 s;
    - tutti i tooltip si chiudono quando la finestra si sposta, si ridimensiona, si disattiva o si chiude.
  - Il test con la finestra tooltip reale (`EnumThreadWindows`) fallisce sul codice precedente e passa con la correzione.
- Il tooltip non compare nei render statici. Al 200% simulato l'icona Info risulta più bassa del sottotitolo perché il banco non scala i caratteri; va verificata con DPI reali.

## Correzioni di layout del 18 ottobre 2026

Misure sui render al 100%, prima → dopo, con le stesse condizioni (`test.cmd --artifact --ui --read-only --render`):

- Home: finestra 960×760 e righe della griglia da 88 px. La nota sulla persistenza delle modifiche è visibile senza scorrere; prima finiva fuori dall'area visibile e compariva una scrollbar.
- Strumenti Windows: il margine destro della colonna non si applica più alla tessera su due colonne. Il bordo destro passa da x=906 a x=912, come la riga sopra.
- Titoli: `HeadingLabel` disegna il titolo a 22pt con il margine GDI del carattere del contenitore. Il bordo sinistro passa da 47-50 px a 42-45 px, allineato a sottotitolo (42-43) e intestazione (41); lo scarto residuo dipende dalla forma della prima lettera.
- Protezioni: il motivo vuoto non riserva più una riga sopra i pulsanti.
- Operazione: margine laterale 26 come le altre finestre. Le etichette passano da x=40 a x=42; l'output resta a x=37, allineato alla lista stampanti.

Colori e palette sono invariati. I render a 150/200% scalano le dimensioni ma non i caratteri: non provano l'assenza di testo tagliato con DPI reali.

## Aggiornamento del 16 ottobre 2026

- Home a sette categorie: aggiunti Compatibilità e protezioni e Account, mantenendo due colonne e la riga Strumenti Windows a larghezza intera.
- Un filtro messaggi instrada la rotella al contenitore sotto il puntatore nella finestra attiva. Pulsanti, etichette e pannelli annidati non interrompono più lo scorrimento. Delta piccoli accumulati, righe/pagine Windows rispettate, assi indipendenti e nessun cambio di focus. Trascinamento e navigazione da tastiera restano disponibili.
- Stampanti espone Vista classica accanto a Stampanti e scanner. Gestione computer, Pannello di controllo e Programmi e funzionalità sono nel catalogo degli strumenti. La richiesta di apertura non certifica la vista risultante né l'assenza di reindirizzamenti Windows.
- Le sette schede protezioni distinguono configurazione, stato effettivo quando leggibile, disponibilità dei comandi e riavvio pendente. Ogni scheda conserva l'accesso alla pagina Windows. Modifica diretta con una sola conferma; Annulla predefinito; risultato automatico, rilettura e ripristino di sessione.
- Isolamento core raggruppa HVCI, stack kernel e blocklist, senza un interruttore generale. SAC e blocklist si modificano nella pagina Windows. I motivi che impediscono le altre azioni sono mostrati nel pannello.
- Account distingue collegamenti del PC, amministrazione utenti, portale personale e chiusura online. Le conseguenze della rimozione locale e della chiusura online sono descritte prima dell'accesso agli strumenti nativi.
- Le pagine personali e il browser usano token e ambiente della shell desktop. La gestione effettiva degli account e la conferma di chiusura restano nelle procedure Windows/Microsoft.

Verifica 1.2.0: `build.cmd` e `test.cmd --artifact --ui --read-only --render`. **71 test superati** con backend simulati per le scritture e letture reali di protezioni/CIM/Defender. Rendering aggiuntivi `10-*.png` (protezioni) e `11-*.png` (account) in `artifacts/ui`, a 100/150/200%. Le verifiche del routing dei messaggi usano destinazione e finestra attiva simulate per non dipendere dal focus del desktop durante la suite.

Restano da validare in VM: effetti delle modifiche dopo il riavvio, policy/UEFI reali, comportamento delle viste classiche sulle build target, UAC con un altro amministratore e DPI reali. Nessuna modifica alle protezioni o agli account reali è stata eseguita sulla postazione di sviluppo.

## Revisione precedente — 1.1.0

Analisi e implementazione del 16 ottobre 2026. Obiettivo: eliminare i passaggi intermedi mantenendo conferme specifiche per gli interventi con effetti rilevanti. Rimangono C# 5, Windows Forms e un solo EXE senza componenti da installare.

## Percorsi abbreviati

Conteggi dal punto in cui la categoria è aperta e, per la stampa, la stampante è già selezionata. Le chiusure delle finestre non rientrano nei conteggi.

| Operazione | Prima | Dopo |
|---|---|---|
| Proprietà/preferenze/coda stampante | Pulsante → schermata intermedia → Esegui → dialogo Windows | Pulsante → dialogo Windows; proprietà anche con doppio clic o Invio |
| Strumenti Windows e impostazioni | Pulsante → schermata intermedia → Esegui | Pulsante → strumento esterno, esito nel modale corrente |
| Tweak Esplora file | Sottosezione → pulsante modifica → schermata intermedia → Esegui | Pulsante modifica direttamente nel pannello con stato e ripristino |
| Informazioni rete | Sottosezione → azione → Esegui | Azione nel gruppo della categoria → risultato automatico |
| Reset rete/riparazione | Sottosezione → azione → Esegui → conferma | Azione nel gruppo della categoria → una conferma → risultato automatico |
| Annullamento lavori selezionata | Azione → Esegui → conferma | Azione → una conferma → risultato automatico |

Le modifiche reversibili aggiornano lo stato nello stesso pannello. L'output delle operazioni lunghe rimane visibile. I launcher non creano un altro modale per segnalare il successo; gli errori compaiono nel modale corrente e nella cronologia di sessione. Annulla resta il pulsante predefinito nelle conferme; dopo la conferma non è richiesto un altro clic.

## Gerarchia e colori

- Home a cinque riquadri, in due colonne; intestazione più compatta e informazioni del computer separate dalle azioni.
- Stampanti: elenco con nome e tipo, predefinita riconoscibile, selezione esplicita, comandi adiacenti e strumenti generali nel footer.
- Aspetto: ogni tweak raccoglie titolo, spiegazione breve, stato corrente e tre comandi. I pulsanti si adattano al testo e vanno a capo quando necessario.
- Palette centralizzata: sfondo `#10161F`, superficie `#19222F`, bordi `#2E3E50`, testo `#EBF1F7`, secondario `#9EAFC3`, accento `#68E2C3`, avviso `#FFC677`.
- Hover, focus, selezione e controllo disabilitato hanno stati distinguibili. Pulsanti arrotondati, bordi sottili e spaziatura comune. Le frecce non simulano ulteriori schermate quando il comando apre direttamente uno strumento.
- Scrollbar disegnate dall'app per gruppi, stampanti e output; nessuna barra chiara di sistema dentro i controlli dell'app. Output testuale selezionabile/copiabile con Rich Edit di Windows.
- Barra del titolo e contorno esterno impostati con gli attributi DWM documentati di Windows 11. I dialoghi del driver o degli strumenti esterni restano sotto il controllo di Windows.

## Verifica

Comandi: `build.cmd`, quindi `test.cmd --artifact --ui --read-only --render`.

47 verifiche, incluse prove UI con azioni simulate per apertura diretta, errori, avvio automatico, annullamento, unica conferma, chiusura bloccata durante l'esecuzione, scorrimento dell'output su entrambi gli assi, focus nei menu, lista lunga e selezione accessibile delle stampanti. Le letture reali non modificano registro o servizi.

Anteprime realizzate con finestre di test e dati stampante simulati, catturate tramite PrintWindow in `artifacts/ui` ai fattori 100/150/200%. Ispezionati home, stampanti, tweak, conferma, output e scrollbar. Il ridimensionamento simulato non certifica cambio DPI fra monitor reali. Le prove su Windows pulito, i dialoghi effettivi dei driver e gli interventi distruttivi reali restano da effettuare in VM sacrificabile come indicato nel README.


## Icona e profili Windows 10/11 — 19 ottobre 2026

Icona propria: tre cursori verde menta #68E2C3 su #121B26, angoli trasparenti, sorgente SVG e ICO in otto taglie. Risorsa nativa dell’EXE, estrazione tramite shell e assegnazione a tutte le finestre verificate. La home usa l’eyebrow neutro WINDOWS e il riepilogo del profilo effettivo; su Windows 10 non mostra il menu classico né le protezioni esclusive di Windows 11.

Le schede Protezioni contengono solo stato e accesso alla pagina Windows. La riduzione dei pulsanti ha richiesto di ricalcolare le altezze di etichette e barra azioni; i test controllano che stato e pulsante siano visibili, contenuti nella scheda e non sovrapposti. Valori assenti restano sconosciuti. Sono state ispezionate home 1809, Esplora file 1809 e protezioni Windows 10/11.

Render aggiuntivi: `artifacts/ui/os-<build>-MainForm.png`, `AppearanceDialog.png`, `SecurityDialog.png`, per build 17763, 19044, 19045, 22000, 22621 e 26100. Sono simulazioni della selezione delle funzioni sul desktop Windows 11 della postazione; non provano cornice, font, DPI o disponibilità reale dei componenti Windows 10. La cattura attende il repaint e rifiuta immagini nere. Il test per i tooltip e il loop UI condiviso restano attivi.

Build locale riuscita; suite completa 69/0 e comando CI locale 47/0. Nessun nuovo run remoto. Le sezioni precedenti documentano i cicli storici; le modifiche dirette alle protezioni descritte nelle versioni precedenti sono state rimosse. Rimangono necessarie le VM e le prove reali della shell/taskbar.
