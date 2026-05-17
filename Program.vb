Imports System.Windows.Forms
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Net.Http
Imports System.Text.Json
Imports Microsoft.Toolkit.Uwp.Notifications

Module Program

    ' --- CONFIGURAZIONI CENTRALIZZATE ---
    Private BASE_URL As String = ""
    Private NTFY_TOKEN As String = ""
    Private CURRENT_TOPIC As String = ""
    Private SCADENZA_TOPIC As String = "mai"
    Private ULTIMO_AGGIORNAMENTO As String = ""

    ' Preferenze utente (impostate di default, modificabili a piacimento)
    Private IsCopyMode As Boolean = True
    Private IsSoundEnabled As Boolean = False

    ' Regex e Percorsi
    Private ReadOnly OTP_REGEX As New Regex("(?:^|\s)(\S{8})(?=\s|$)", RegexOptions.Compiled)
    Private ReadOnly LogPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt")
    Private ReadOnly ConfigPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt")
    Private AppMutex As Threading.Mutex = Nothing

    ' Array predefinito per la creazione del config (Incluso di parametri di scadenza)
    Private ReadOnly DefaultConfigLines As String() = {
        "BASE_URL=https://ntfy.dati-web.it",
        "TOKEN=tk_inserisci_qui",
        "TOPIC=inserisci_topic_qui",
        "SCADENZA_TOPIC=mai",
        "ULTIMO_AGGIORNAMENTO="
    }

    ' Componenti grafici di sistema
    Private WithEvents TrayIcon As NotifyIcon
    Private WithEvents TrayMenu As ContextMenuStrip

    ' 1. CORREZIONE: Aggiunto attributo per forzare il Thread Principale in modalità STA
    <STAThread()>
    Sub Main()
        ' --- 1. CONTROLLO DOPPIO AVVIO (MUTEX) ---
        Dim createdNew As Boolean
        AppMutex = New Threading.Mutex(True, "NtfyOtpMonitor", createdNew)

        If Not createdNew Then
            MessageBox.Show("Applicazione già in esecuzione", "Attenzione", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' --- 2. INIZIALIZZAZIONE AMBIENTE ---
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        ' Carica tutto dal file config
        LoadConfig()

        ' --- 3. CONTROLLO SCADENZA TOPIC ALL'AVVIO ---
        VerificaScadenzaTopic()

        ' --- 4. CONFIGURAZIONE INTERFACCIA BARRA DI SISTEMA ---
        SetupTrayMenu()

        WriteLog("=== Applicazione Avviata ===")

        ' Chiede di impostare il topic solo se è vuoto, se è quello di default ("inserisci_topic_qui") o se è disattivato ("---")
        If String.IsNullOrEmpty(CURRENT_TOPIC) OrElse CURRENT_TOPIC = "inserisci_topic_qui" OrElse CURRENT_TOPIC = "---" Then
            ChangeTopic()
        End If

        ' --- 5. AVVIO MONITORAGGIO (BACKGROUND THREAD) ---
        ' 2. CORREZIONE: Questa è la sezione che non trovavi. Inserito anche il consiglio extra (STA)
        Dim t As New Threading.Thread(AddressOf StartSseListening) With {
            .IsBackground = True
        }
        t.SetApartmentState(Threading.ApartmentState.STA) ' <--- Consiglio Extra applicato
        t.Start()

        ' Mantiene in vita l'applicazione gestendo i messaggi di Windows
        Application.Run()

        ' Mantiene il Mutex attivo
        GC.KeepAlive(AppMutex)
    End Sub

    Private Sub LoadConfig()
        ' Se il file non esiste, lo crea usando l'array statico
        If Not File.Exists(ConfigPath) Then
            File.WriteAllLines(ConfigPath, DefaultConfigLines)
            MessageBox.Show("File config.txt creato. Configuralo con i tuoi dati e riavvia", "Configurazione", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Environment.Exit(0)
        End If

        ' Legge i parametri dal file di testo
        For Each line In File.ReadAllLines(ConfigPath)
            If line.StartsWith("BASE_URL=") Then BASE_URL = line.Replace("BASE_URL=", "").Trim()
            If line.StartsWith("TOKEN=") Then NTFY_TOKEN = line.Replace("TOKEN=", "").Trim()
            If line.StartsWith("TOPIC=") Then CURRENT_TOPIC = line.Replace("TOPIC=", "").Trim()
            If line.StartsWith("SCADENZA_TOPIC=") Then SCADENZA_TOPIC = line.Replace("SCADENZA_TOPIC=", "").Trim()
            If line.StartsWith("ULTIMO_AGGIORNAMENTO=") Then ULTIMO_AGGIORNAMENTO = line.Replace("ULTIMO_AGGIORNAMENTO=", "").Trim()
        Next
    End Sub

    ' Controlla a freddo se il tempo limite per il topic è scaduto
    Private Sub VerificaScadenzaTopic()
        ' Saltiamo il controllo se il topic è vuoto, finto, di default o è già impostato sul valore di reset "Topic-esempio"
        If String.IsNullOrEmpty(CURRENT_TOPIC) OrElse
           CURRENT_TOPIC = "inserisci_topic_qui" OrElse
           CURRENT_TOPIC = "---" OrElse
           CURRENT_TOPIC = "Topic-esempio" Then
            Return
        End If

        If SCADENZA_TOPIC.ToLower() <> "mai" Then
            Dim oreScadenza As Integer = EstraiOre(SCADENZA_TOPIC)

            If oreScadenza > 0 AndAlso Not String.IsNullOrEmpty(ULTIMO_AGGIORNAMENTO) Then
                Dim dataUltimo As DateTime
                If DateTime.TryParse(ULTIMO_AGGIORNAMENTO, dataUltimo) Then
                    ' Calcola le ore passate dall'ultimo inserimento ad ora
                    Dim orePassate As Double = (DateTime.Now - dataUltimo).TotalHours

                    If orePassate >= oreScadenza Then
                        ' Il Topic è scaduto! Lo impostiamo sul valore predefinito automatico richiesto
                        CURRENT_TOPIC = "Topic-esempio"
                        ULTIMO_AGGIORNAMENTO = ""
                        SaveTopicToConfig("Topic-esempio", resettaData:=True)
                        WriteLog(String.Format("Topic resettato a 'Topic-esempio' perché scaduto (Limite: {0}, Passate: {1:F1}h)", SCADENZA_TOPIC, orePassate))
                        MessageBox.Show("Il topic impostato è scaduto ed è stato ripristinato a 'Topic-esempio'.", "Topic Scaduto", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End If
                End If
            End If
        End If
    End Sub

    ' Converte stringhe tipo "4h" o "3" nell'intero corrispondente (4 o 3)
    Private Function EstraiOre(testoScadenza As String) As Integer
        Try
            Dim pulito As String = testoScadenza.ToLower().Replace("h", "").Trim()
            Dim ore As Integer
            If Integer.TryParse(pulito, ore) Then
                Return ore
            End If
        Catch
        End Try
        Return 0
    End Function

    ' Salva le modifiche del topic e della data direttamente nel file config.txt
    Private Sub SaveTopicToConfig(newTopic As String, Optional resettaData As Boolean = False)
        Try
            Dim lines As New List(Of String)

            ' Determina il valore della data di aggiornamento da scrivere
            If newTopic = "---" OrElse newTopic = "inserisci_topic_qui" OrElse newTopic = "Topic-esempio" OrElse resettaData Then
                ULTIMO_AGGIORNAMENTO = ""
            End If

            If File.Exists(ConfigPath) Then
                For Each line In File.ReadAllLines(ConfigPath)
                    If line.StartsWith("TOPIC=") Then
                        lines.Add("TOPIC=" & newTopic)
                    ElseIf line.StartsWith("ULTIMO_AGGIORNAMENTO=") Then
                        ' Ignoriamo la vecchia riga per non duplicarla
                    Else
                        lines.Add(line)
                    End If
                Next
                ' Aggiungiamo sempre alla fine la riga aggiornata
                lines.Add("ULTIMO_AGGIORNAMENTO=" & ULTIMO_AGGIORNAMENTO)
            Else
                ' Fallback di sicurezza
                lines.Add("BASE_URL=" & BASE_URL)
                lines.Add("TOKEN=" & NTFY_TOKEN)
                lines.Add("TOPIC=" & newTopic)
                lines.Add("SCADENZA_TOPIC=" & SCADENZA_TOPIC)
                lines.Add("ULTIMO_AGGIORNAMENTO=" & ULTIMO_AGGIORNAMENTO)
            End If

            File.WriteAllLines(ConfigPath, lines.ToArray())
        Catch ex As Exception
            WriteLog("Impossibile salvare il topic nel config: " & ex.Message)
        End Try
    End Sub

    Private Sub SetupTrayMenu()
        TrayMenu = New ContextMenuStrip()

        ' Opzione: Copia negli appunti
        Dim itemCopy As New ToolStripMenuItem("Copia negli appunti") With {
            .CheckOnClick = True,
            .Checked = IsCopyMode
        }
        AddHandler itemCopy.CheckedChanged, Sub(s, e)
                                                IsCopyMode = itemCopy.Checked
                                            End Sub

        ' Opzione: Riproduci suono
        Dim itemSound As New ToolStripMenuItem("Riproduci suono") With {
            .CheckOnClick = True,
            .Checked = IsSoundEnabled
        }
        AddHandler itemSound.CheckedChanged, Sub(s, e)
                                                 IsSoundEnabled = itemSound.Checked
                                             End Sub

        ' Composizione Menu
        TrayMenu.Items.Add(itemCopy)
        TrayMenu.Items.Add(itemSound)
        TrayMenu.Items.Add("-")
        TrayMenu.Items.Add("Cambia Topic", Nothing, AddressOf ChangeTopic)
        TrayMenu.Items.Add("-")
        TrayMenu.Items.Add("Esci", Nothing, AddressOf OnEsci)

        ' Istanza finale del NotifyIcon
        TrayIcon = New NotifyIcon() With {
            .ContextMenuStrip = TrayMenu,
            .Text = "Ntfy OTP Monitor (" & CURRENT_TOPIC & ")",
            .Visible = True
        }

        ' Caricamento Icona Personale o di Fallback
        Try
            TrayIcon.Icon = New Icon("Dtafalonso.ico")
        Catch
            Try
                TrayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            Catch
            End Try
        End Try
    End Sub

    Private Sub ChangeTopic()
        Dim promptTopic As String = If(CURRENT_TOPIC = "inserisci_topic_qui" OrElse CURRENT_TOPIC = "---" OrElse CURRENT_TOPIC = "Topic-esempio", "", CURRENT_TOPIC)
        Dim newTopic As String = InputBox("Inserisci il nuovo Topic di ntfy:", "Imposta Topic", promptTopic)

        If Not String.IsNullOrEmpty(newTopic) AndAlso newTopic <> CURRENT_TOPIC Then
            CURRENT_TOPIC = newTopic

            ' Salva nel file config registrando l'orario di inserimento attuale
            SaveTopicToConfig(newTopic, resettaData:=False)
            WriteLog("Topic cambiato in: " & newTopic)

            If TrayIcon IsNot Nothing Then
                TrayIcon.Visible = False
                TrayIcon.Dispose()
            End If
            Application.Restart()
            Environment.Exit(0)
        End If
    End Sub

    Private Sub StartSseListening()
        ' Se il topic è disattivato ("---") o di default, non avviare l'ascolto HTTP
        If String.IsNullOrEmpty(CURRENT_TOPIC) OrElse CURRENT_TOPIC = "inserisci_topic_qui" OrElse CURRENT_TOPIC = "---" Then Return
        Dim url As String = String.Format("{0}/{1}/json", BASE_URL, CURRENT_TOPIC)

        While True
            Try
                Using handler As New HttpClientHandler()
                    Using client As New HttpClient(handler)
                        client.DefaultRequestHeaders.Add("Authorization", "Bearer " & NTFY_TOKEN)
                        client.Timeout = Threading.Timeout.InfiniteTimeSpan

                        Using response As HttpResponseMessage = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result
                            Using stream As Stream = response.Content.ReadAsStreamAsync().Result
                                Using reader As New StreamReader(stream, Encoding.UTF8)
                                    WriteLog("Connesso al topic: " & CURRENT_TOPIC)

                                    While Not reader.EndOfStream
                                        Dim line As String = reader.ReadLine()
                                        If Not String.IsNullOrEmpty(line) Then ProcessJson(line)
                                    End While

                                End Using
                            End Using
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                WriteLog("Errore di connessione: " & ex.Message)
                Threading.Thread.Sleep(5000)
            End Try
        End While
    End Sub

    Private Sub ProcessJson(json As String)
        Try
            Dim doc As JsonDocument = JsonDocument.Parse(json)
            Dim root As JsonElement = doc.RootElement

            If root.TryGetProperty("event", Nothing) AndAlso root.GetProperty("event").GetString() = "message" Then
                If root.TryGetProperty("message", Nothing) Then
                    Dim message As String = root.GetProperty("message").GetString()
                    Dim match As Match = OTP_REGEX.Match(message)
                    If match.Success Then
                        HandleOTP(match.Groups(1).Value)
                    End If
                End If
            End If
        Catch
            ' Ignora i messaggi di keep-alive vuoti del server ntfy
        End Try
    End Sub

    Private Sub HandleOTP(code As String)
        ' 3. CORREZIONE: Sostituito .Invoke con .BeginInvoke per un passaggio asincrono e pulito al thread UI
        If TrayMenu IsNot Nothing AndAlso TrayMenu.InvokeRequired Then
            TrayMenu.BeginInvoke(Sub() HandleOTP(code))
            Return
        End If

        ' --- ORA SEI SICURAMENTE NEL THREAD PRINCIPALE (STA) ---

        ' 1. Riproduzione suono
        If IsSoundEnabled Then
            System.Media.SystemSounds.Asterisk.Play()
        End If

        ' 2. Esecuzione azione negli Appunti o Digitazione
        If IsCopyMode Then
            Try
                Clipboard.SetText(code)
            Catch ex As Exception
                WriteLog("Errore critico Clipboard: " & ex.Message)
                TrayIcon.ShowBalloonTip(3000, "Errore Clipboard", ex.Message, ToolTipIcon.Warning)
            End Try
        Else
            Threading.Thread.Sleep(300)
            SendKeys.SendWait(code)
        End If

        WriteLog("OTP Ricevuto: " & code & " [" & If(IsCopyMode, "COPIATO", "DIGITATO") & "]")

        ' 3. Mostra la notifica Toast
        Try
            Dim toast As New ToastContentBuilder()
            toast.AddText("OTP Ricevuto")
            toast.AddText(code & If(IsCopyMode, "  -  Copiato", "  -  Digitato"))
            toast.Show()
        Catch ex As Exception
            WriteLog("Errore visualizzazione Toast: " & ex.Message)
            TrayIcon.ShowBalloonTip(5000, "OTP Ricevuto", "Codice: " & code, ToolTipIcon.Info)
        End Try
    End Sub

    Private Sub WriteLog(msg As String)
        Try
            Dim logLine As String = String.Format("[{0}] {1}", DateTime.Now.ToString("G"), msg)
            Dim lines As New List(Of String)
            If File.Exists(LogPath) Then lines.AddRange(File.ReadAllLines(LogPath))
            lines.Add(logLine)
            If lines.Count > 50 Then lines = lines.Skip(lines.Count - 50).ToList()
            File.WriteAllLines(LogPath, lines)
        Catch : End Try
    End Sub

    ' --- GESTIONE EVENTI MENU ---
    Private Sub OnEsci(sender As Object, e As EventArgs)
        If TrayIcon IsNot Nothing Then
            TrayIcon.Visible = False
            TrayIcon.Dispose()
        End If
        Application.Exit()
    End Sub

    Private Sub TrayIcon_Click(sender As Object, e As EventArgs) Handles TrayIcon.Click
        ' Lasciato vuoto per evitare azioni indesiderate al click singolo.
    End Sub

End Module
