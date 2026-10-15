# Revisione UI/UX — 1.2.0

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
