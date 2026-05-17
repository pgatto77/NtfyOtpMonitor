Ntfy OTP Monitor (Windows)
Ntfy OTP Monitor è un'applicazione "portable" per Windows (7/10/11) progettata per ricevere notifiche push istantanee contenenti codici OTP (in particolare Infocert) direttamente sul desktop, automatizzando la copia o la digitazione del codice.

🚀 Funzionamento
Il sistema si basa su una catena di automazione:

Ricezione: Un SMS OTP arriva sul tuo smartphone Android.

Inoltro: Un flusso dell'app Automate (disponibile nella community di Automate) intercetta l'SMS e lo invia al tuo server ntfy (self-hosted o cloud).

Ascolto: Questo programma mantiene una connessione persistente SSE (Server-Sent Events) con il server ntfy.

Output: Alla ricezione, il programma estrae l'OTP, emette un suono e gestisce il codice secondo la modalità scelta (Copia o Digitazione).

🛠️ Configurazione Iniziale
Al primo avvio, l'applicazione genera nella propria cartella i file necessari:

1. Config.txt
Deve essere compilato manualmente per stabilire la connessione:

Riga 1 (BASE_URL): L'indirizzo del server (es. [https://ntfy.dati-web.it](https://ntfy.dati-web.it)).

Riga 2 (TOKEN): Il Bearer Token per l'autenticazione (necessario per topic privati).

2. Impostazione Topic
All'avvio, l'applicazione chiederà l'inserimento del Topic (il nome del canale ntfy). Questa impostazione è persistente e viene salvata nelle configurazioni utente di Windows.

🖱️ Funzionalità Tray Bar
L'icona nell'area di notifica (vicino all'orologio) permette di gestire l'app in tempo reale:

Copia negli appunti: Se attivo, l'OTP viene copiato automaticamente nella clipboard.

Riproduci suono: Attiva/disattiva il feedback acustico alla ricezione.

Cambia Topic: Permette di modificare il canale di ascolto senza riavviare manualmente.

Log Rotativo: Viene generato un file log.txt che conserva le ultime n operazioni per monitorare lo stato della connessione e le ricezioni.

📦 Caratteristiche Tecniche
Portable: Non richiede installazione.

Single Instance: Il programma impedisce l'avvio di istanze multiple per evitare notifiche doppie.

Leggero: Sviluppato in .NET Framework 4.8, consuma minime risorse di sistema.

Sicurezza: Supporta topic privati tramite autenticazione con Token.

⚠️ Requisiti
Windows 10 o superiore.

.NET 8.0 (runtime) installato.

Smartphone Android con app Automate configurata.

------------  CONFIGURAZIONE AUTOMATE PER ANDROID --------------------

📱 Configurazione Automate (Android)
Nella cartella /Automate di questo repository trovi il file OTP_Forwarder.flo. Questo flusso è essenziale per il funzionamento dell'intero sistema.

Istruzioni per l'uso:

Scarica e installa Automate dal Play Store.

Copia il file .flo sul tuo telefono e importalo nell'app.

Configura i blocchi all'interno del flusso:

SMS Received: Imposta il filtro sul mittente (es. "InfoCert").

HTTP Post:

URL: Deve corrispondere al tuo server [https://ntfy.dati-web.it/TUO_TOPIC](https://ntfy.dati-web.it/TUO_TOPIC).

Headers: Aggiungi Authorization: Bearer tk_tuo_token.

Content: Inserisci il corpo del messaggio ricevuto.

Avvia il flusso e concedi i permessi per la lettura degli SMS.

PER CHIARIMENTI CONTATTARE 
autore Paolo Gatto: paolo.gatto@mit.gov.it



