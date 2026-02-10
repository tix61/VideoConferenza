Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports Microsoft.AspNetCore.SignalR.Client
Imports VideoConference.Client.VideoConference.Client

Class MainWindow
    Implements INotifyPropertyChanged

    Private WithEvents _connection As HubConnection
    Private _isConnected As Boolean = False
    Private _localConnectionId As String = ""
    Private _remoteConnectionId As String = ""
    Private _videoManager As VideoManager
    Private _isVideoStarted As Boolean = False
    Private _isSendingVideo As Boolean = False
    Private _frameSendTimer As Timers.Timer

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Public Property IsConnected As Boolean
        Get
            Return _isConnected
        End Get
        Set(value As Boolean)
            If _isConnected <> value Then
                _isConnected = value
                OnPropertyChanged()
                UpdateUI()
            End If
        End Set
    End Property

    Public ReadOnly Property LocalVideoSource As ImageSource
        Get
            If _videoManager IsNot Nothing Then
                Return _videoManager.LocalVideoSource
            End If
            Return Nothing
        End Get
    End Property

    Public ReadOnly Property RemoteVideoSource As ImageSource
        Get
            If _videoManager IsNot Nothing Then
                Return _videoManager.RemoteVideoSource
            End If
            Return Nothing
        End Get
    End Property

    Public Sub New()
        ' IMPORTANTE: InitializeComponent deve essere la PRIMA chiamata
        InitializeComponent()

        ' Inizializza DataContext DOPO InitializeComponent
        Me.DataContext = Me

        InitializeVideo()

        ' Inizializza UI
        UpdateUI()

        ' Aggiungi gli handler degli eventi dei bottoni
        AddHandler btnConnect.Click, AddressOf btnConnect_Click
        AddHandler btnDisconnect.Click, AddressOf btnDisconnect_Click
        AddHandler btnStartVideo.Click, AddressOf btnStartVideo_Click
        AddHandler btnStopVideo.Click, AddressOf btnStopVideo_Click
        AddHandler btnStartAudio.Click, AddressOf btnStartAudio_Click
        AddHandler btnStopAudio.Click, AddressOf btnStopAudio_Click
    End Sub

    Private Sub InitializeVideo()
        Try
            _videoManager = New VideoManager()

            ' Configura gestione errori video
            AddHandler _videoManager.OnVideoError,
                Sub(errorMessage)
                    Dispatcher.Invoke(Sub()
                                          MessageBox.Show($"Errore video: {errorMessage}",
                                                        "Errore Video", MessageBoxButton.OK, MessageBoxImage.Error)
                                          _isVideoStarted = False
                                          UpdateUI()
                                      End Sub)
                End Sub

            ' Configura eventi di avvio/arresto video
            AddHandler _videoManager.OnVideoStarted,
                Sub()
                    Dispatcher.Invoke(Sub()
                                          _isVideoStarted = True
                                          txtStatus.Text = "Connesso - Video attivo"
                                          txtVideoStatus.Text = "Video: Attivo"
                                          txtVideoStatus.Foreground = System.Windows.Media.Brushes.Green
                                          UpdateUI()
                                      End Sub)
                End Sub

            AddHandler _videoManager.OnVideoStopped,
                Sub()
                    Dispatcher.Invoke(Sub()
                                          _isVideoStarted = False
                                          txtStatus.Text = "Connesso - Video fermato"
                                          txtVideoStatus.Text = "Video: Disattivo"
                                          txtVideoStatus.Foreground = System.Windows.Media.Brushes.Red
                                          UpdateUI()
                                      End Sub)
                End Sub

            ' Configura aggiornamento frame video
            AddHandler _videoManager.OnLocalFrameUpdated,
                Sub(bitmap)
                    Dispatcher.Invoke(Sub()
                                          If bitmap IsNot Nothing Then
                                              localVideoImage.Source = bitmap
                                              txtLocalVideoPlaceholder.Visibility = Visibility.Collapsed
                                          End If
                                      End Sub)
                End Sub

            ' Configura evento per invio frame
            AddHandler _videoManager.OnFrameReadyToSend,
            Sub(frameData As Byte(), width As Integer, height As Integer)
                If _isSendingVideo AndAlso IsConnected AndAlso Not String.IsNullOrEmpty(_remoteConnectionId) Then
                    SendVideoFrame(frameData, width, height)
                End If
            End Sub

            AddHandler _videoManager.OnRemoteFrameUpdated,
            Sub(bitmap)
                Dispatcher.Invoke(Sub()
                                      Debug.Print($"DEBUG: OnRemoteFrameUpdated event fired, bitmap is Nothing: {bitmap Is Nothing}")

                                      If bitmap IsNot Nothing Then
                                          ' Imposta direttamente l'immagine (bypassa il binding temporaneamente)
                                          remoteVideoImage.Source = bitmap
                                          txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed

                                          ' Notifica anche il cambio della proprietà per il binding
                                          OnPropertyChanged(NameOf(RemoteVideoSource))
                                          Debug.Print("DEBUG: Remote frame set directly and property notified")
                                      Else
                                          Debug.Print("DEBUG: Remote bitmap is Nothing!")
                                      End If
                                  End Sub)
            End Sub

            ' Configura timer per invio frame periodico
            _frameSendTimer = New Timers.Timer(100) ' 10 FPS per l'invio
            _frameSendTimer.AutoReset = True
            AddHandler _frameSendTimer.Elapsed, AddressOf OnFrameSendTimerElapsed

            Debug.Print("Video Manager initialized with Emgu.CV")

        Catch ex As Exception
            MessageBox.Show($"Errore nell'inizializzazione video: {ex.Message}",
                          "Errore Inizializzazione", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Async Sub SendVideoFrame(frameData As Byte(), width As Integer, height As Integer)
        Try
            If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
                Await _connection.InvokeAsync("SendVideoFrameToAll", txtRoomId.Text, frameData, width, height)
            End If
        Catch ex As Exception
            Debug.Print($"Error sending video frame: {ex.Message}")
        End Try
    End Sub

    Private Async Sub btnConnect_Click(sender As Object, e As RoutedEventArgs)
        Try
            txtStatus.Text = "Connessione in corso..."

            _connection = New HubConnectionBuilder().
                WithUrl(txtServerUrl.Text.Trim() & "/conferencehub").
                WithAutomaticReconnect().
                Build()

            ' Configura gli handler degli eventi dal server
            _connection.On("UserJoined",
                Sub(connectionId As String, userName As String)
                    Dispatcher.Invoke(Sub()
                                          _remoteConnectionId = connectionId
                                          txtStatus.Text = $"{userName} si è unito alla stanza"
                                          MessageBox.Show($"Benvenuto {userName}!", "Nuovo Utente",
                                                        MessageBoxButton.OK, MessageBoxImage.Information)
                                      End Sub)
                End Sub)

            _connection.On("UserLeft",
                Sub(connectionId As String)
                    Dispatcher.Invoke(Sub()
                                          If _remoteConnectionId = connectionId Then
                                              _remoteConnectionId = ""
                                              txtStatus.Text = "Utente remoto disconnesso"
                                              MessageBox.Show("L'utente remoto ha lasciato la stanza.",
                                                            "Utente Disconnesso",
                                                            MessageBoxButton.OK, MessageBoxImage.Warning)
                                          End If
                                      End Sub)
                End Sub)

            _connection.On("ExistingUsers",
                Sub(users As Object)
                    Dispatcher.Invoke(Sub()
                                          txtStatus.Text = "Connesso alla stanza"
                                          Debug.Print($"Utenti esistenti: {users}")
                                      End Sub)
                End Sub)

            ' Aggiungi anche handler per errori di connessione
            AddHandler _connection.Closed,
                Async Function(err)
                    Await Dispatcher.InvokeAsync(Sub()
                                                     IsConnected = False
                                                     txtStatus.Text = "Connessione chiusa"
                                                     If err IsNot Nothing Then
                                                         Debug.Print($"Errore di connessione: {err.Message}")
                                                     End If
                                                 End Sub)
                    Return Task.CompletedTask
                End Function


            ' Aggiungi handler per ricevere frame video
            _connection.On("ReceiveVideoFrame",
            Sub(senderConnectionId As String, frameData As Byte(), width As Integer, height As Integer)
                Debug.Print($"DEBUG: Ricevuto frame da {senderConnectionId}, dimensione: {frameData?.Length} bytes")
                Dispatcher.Invoke(Sub()
                                      ' Aggiorna il video remoto
                                      If _videoManager IsNot Nothing Then
                                          _videoManager.ReceiveRemoteFrame(frameData, width, height)
                                          txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed

                                          ' Forza l'aggiornamento dell'UI
                                          remoteVideoImage.InvalidateVisual()
                                          Debug.Print("DEBUG: ReceiveRemoteFrame called")
                                      Else
                                          Debug.Print("DEBUG: VideoManager è null!")
                                      End If

                                      ' Aggiorna lo stato
                                      If String.IsNullOrEmpty(_remoteConnectionId) Then
                                          _remoteConnectionId = senderConnectionId
                                      End If
                                  End Sub)
            End Sub)

            ' Connessione al server
            Await _connection.StartAsync()

            IsConnected = True
            _localConnectionId = _connection.ConnectionId
            txtStatus.Text = "Connesso - ID: " & _localConnectionId

            ' Unisciti alla stanza
            Await _connection.InvokeAsync("JoinRoom", txtRoomId.Text, txtUserName.Text)

            MessageBox.Show($"Connesso al server! Il tuo ID: {_localConnectionId}",
                          "Connessione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information)

        Catch ex As Exception
            MessageBox.Show($"Errore di connessione: {ex.Message}", "Errore",
                          MessageBoxButton.OK, MessageBoxImage.Error)
            txtStatus.Text = "Errore di connessione"
            IsConnected = False
        End Try
    End Sub

    Private Sub OnFrameSendTimerElapsed(sender As Object, e As Timers.ElapsedEventArgs)
        ' Questo timer assicura che inviamo frame periodicamente
        ' anche se non ci sono nuovi frame dalla webcam
        If _isSendingVideo AndAlso _videoManager IsNot Nothing Then
            ' Forza l'invio di un frame (se disponibile)
            ' Il VideoManager emetterà l'evento OnFrameReadyToSend se ha un frame
        End If
    End Sub

    Private Async Sub btnDisconnect_Click(sender As Object, e As RoutedEventArgs)
        If _connection IsNot Nothing Then
            Try
                Await _connection.StopAsync()
                Await _connection.DisposeAsync()
            Catch ex As Exception
                ' Ignora errori in disconnessione
                Debug.Print($"Errore in disconnessione: {ex.Message}")
            Finally
                _connection = Nothing
            End Try
        End If

        IsConnected = False
        txtStatus.Text = "Disconnesso"
        _localConnectionId = ""
        _remoteConnectionId = ""

        MessageBox.Show("Disconnesso dal server.", "Disconnessione",
                      MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    Private Sub btnStartVideo_Click(sender As Object, e As RoutedEventArgs)
        If _videoManager Is Nothing Then
            MessageBox.Show("Video Manager non inizializzato", "Errore",
                          MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        If _videoManager.IsCapturing Then
            MessageBox.Show("Video già attivo", "Info",
                          MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Try
            ' Disabilita il bottone durante l'avvio
            btnStartVideo.IsEnabled = False
            txtStatus.Text = "Avvio webcam in corso..."
            txtVideoStatus.Text = "Video: Avvio..."

            ' Mostra un messaggio informativo
            MessageBox.Show("Sto cercando di accedere alla webcam..." & vbCrLf &
                          "Assicurati di aver concesso i permessi per la webcam." & vbCrLf &
                          "Potrebbe essere visualizzata una richiesta di autorizzazione.",
                          "Accesso Webcam", MessageBoxButton.OK, MessageBoxImage.Information)

            ' Avvia la cattura video (NON async - Emgu.CV è sincrono)
            Dim success = _videoManager.StartVideoCapture()

            If success Then
                'MessageBox.Show("Webcam attivata con successo! Il tuo video è visibile nel pannello 'Video Locale'.",
                '              "Successo", MessageBoxButton.OK, MessageBoxImage.Information)

                '' Dopo 2 secondi, mostra lo stato
                'Task.Delay(2000).ContinueWith(
                '    Sub(t)
                '        Dispatcher.Invoke(Sub()
                '                              If _isVideoStarted Then
                '                                  txtStatus.Text = "Connesso - Video attivo"
                '                              End If
                '                          End Sub)
                '    End Sub)
                ' Avvia l'invio video
                _isSendingVideo = True
                _frameSendTimer.Start()

                MessageBox.Show("Webcam attivata con successo! Il video verrà inviato agli altri utenti.",
                              "Successo", MessageBoxButton.OK, MessageBoxImage.Information)
            Else
                MessageBox.Show("Impossibile avviare la webcam. Controlla:" & vbCrLf &
                              "1. I permessi della webcam" & vbCrLf &
                              "2. Che la webcam sia collegata e funzionante" & vbCrLf &
                              "3. Che non sia già in uso da un'altra applicazione" & vbCrLf &
                              "4. Che i driver siano installati correttamente",
                              "Errore Webcam", MessageBoxButton.OK, MessageBoxImage.Error)
            End If

        Catch ex As Exception
            MessageBox.Show($"Errore nell'avvio del video: {ex.Message}",
                          "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
            txtStatus.Text = "Errore video"
            txtVideoStatus.Text = "Video: Errore"
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Sub btnStopVideo_Click(sender As Object, e As RoutedEventArgs)
        'Try
        '    If _videoManager IsNot Nothing Then
        '        _videoManager.StopVideoCapture()
        '        MessageBox.Show("Webcam disattivata", "Info",
        '                      MessageBoxButton.OK, MessageBoxImage.Information)
        '    Else
        '        MessageBox.Show("Video Manager non inizializzato", "Errore",
        '                      MessageBoxButton.OK, MessageBoxImage.Error)
        '    End If
        'Catch ex As Exception
        '    MessageBox.Show($"Errore nella fermata del video: {ex.Message}",
        '                  "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        'Finally
        '    UpdateUI()
        'End Try
        Try
            If _videoManager IsNot Nothing Then
                _videoManager.StopVideoCapture()

                ' Ferma l'invio video
                _isSendingVideo = False
                _frameSendTimer.Stop()

                MessageBox.Show("Webcam disattivata", "Info",
                              MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        Catch ex As Exception
            MessageBox.Show($"Errore nella fermata del video: {ex.Message}",
                          "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Sub btnStartAudio_Click(sender As Object, e As RoutedEventArgs)
        MessageBox.Show("Funzionalità audio non implementata nel prototipo base.",
                       "Info", MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    Private Sub btnStopAudio_Click(sender As Object, e As RoutedEventArgs)
        ' TODO: Implementare quando si aggiungerà l'audio
    End Sub

    Private Sub UpdateUI()
        Dispatcher.BeginInvoke(Sub()
                                   Try
                                       ' Controlli connessione
                                       btnConnect.IsEnabled = Not IsConnected
                                       btnDisconnect.IsEnabled = IsConnected
                                       txtServerUrl.IsEnabled = Not IsConnected
                                       txtUserName.IsEnabled = Not IsConnected
                                       txtRoomId.IsEnabled = Not IsConnected

                                       ' Controlli video
                                       btnStartVideo.IsEnabled = Not _isVideoStarted AndAlso IsConnected
                                       btnStopVideo.IsEnabled = _isVideoStarted AndAlso IsConnected

                                       ' Controlli audio (disabilitati per ora)
                                       btnStartAudio.IsEnabled = False
                                       btnStopAudio.IsEnabled = False

                                       ' Aggiorna il colore dello stato
                                       If IsConnected Then
                                           txtStatus.Foreground = System.Windows.Media.Brushes.Green
                                       Else
                                           txtStatus.Foreground = System.Windows.Media.Brushes.Red
                                       End If

                                       ' Aggiorna placeholder video remoto
                                       If remoteVideoImage.Source IsNot Nothing Then
                                           txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed
                                       Else
                                           txtRemoteVideoPlaceholder.Visibility = Visibility.Visible
                                       End If

                                       ' Aggiorna i binding delle proprietà
                                       OnPropertyChanged(NameOf(LocalVideoSource))
                                       OnPropertyChanged(NameOf(RemoteVideoSource))

                                   Catch ex As Exception
                                       Debug.Print($"Error in UpdateUI: {ex.Message}")
                                   End Try
                               End Sub)
    End Sub

    Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
    End Sub

    Protected Overrides Sub OnClosing(e As ComponentModel.CancelEventArgs)
        'If _connection IsNot Nothing Then
        '    Try
        '        ' Prova a disconnettere in modo asincrono
        '        Dim task = _connection.StopAsync()
        '        task.Wait(TimeSpan.FromSeconds(2))
        '    Catch
        '        ' Ignora errori in chiusura
        '    End Try
        'End If
        'MyBase.OnClosing(e)
        Try
            ' Ferma l'invio video
            _isSendingVideo = False

            If _frameSendTimer IsNot Nothing Then
                _frameSendTimer.Stop()
                _frameSendTimer.Dispose()
            End If

            ' Pulisci le risorse video
            If _videoManager IsNot Nothing Then
                _videoManager.Dispose()
                _videoManager = Nothing
            End If

            ' Pulisci la connessione SignalR
            If _connection IsNot Nothing Then
                Dim task = _connection.StopAsync()
                task.Wait(TimeSpan.FromSeconds(2))
                '_connection.DisposeAsync().Wait(TimeSpan.FromSeconds(1))
            End If

        Catch ex As Exception
            Debug.Print($"Error during cleanup: {ex.Message}")
        Finally
            MyBase.OnClosing(e)
        End Try

    End Sub
End Class