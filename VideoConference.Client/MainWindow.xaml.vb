Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Security
Imports System.Runtime.CompilerServices
Imports System.Security.Cryptography.X509Certificates
Imports System.Windows
Imports System.Windows.Controls
'Imports System.Windows.Forms
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports Microsoft.AspNetCore.SignalR.Client
Imports Microsoft.Extensions.Logging
Imports VideoConference.Client.VideoConference.Client

Class MainWindow
    Implements INotifyPropertyChanged

    Private WithEvents _connection As HubConnection
    Private _isConnected As Boolean = False
    Private _localConnectionId As String = ""
    Private _remoteConnectionId As String = ""
    Private _videoManager As VideoManager
    Private _isVideoStarted As Boolean = False
    Private _frameSendTimer As Timers.Timer
    Private _isAudioStarted As Boolean = False
    Private _isMicMuted As Boolean = False
    Private _screenShareManager As ScreenShareManager
    Private _isSharingScreen As Boolean = False

    ' Variabile per tenere traccia del cursore remoto
    Private _cursorHighlight As System.Windows.Shapes.Shape
    'Private _remoteCursorElement As System.Windows.Shapes.Path
    Private _remoteCursorElement As System.Windows.Controls.Image
    Private _remoteCursorPosition As Point
    Private _cursorDot As System.Windows.Shapes.Ellipse
    Private _cursorShadow As System.Windows.Shapes.Path

    Private _remoteScreenWidth As Integer = 1920  ' Default
    Private _remoteScreenHeight As Integer = 1080 ' Default

    Private _participants As New ObservableCollection(Of Participant)
    Private _chatMessages As New ObservableCollection(Of ChatMessage)
    Private _remoteVideoSources As New Dictionary(Of String, WriteableBitmap)

    Private _isChatCollapsed As Boolean = False
    Private _originalChatWidth As Double = 3 ' Valore originale della colonna chat (3*)

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

        InitializeScreenShare()

        InitializeParticipantsAndChat()

        ' Inizializza UI
        UpdateUI()

        ' Aggiungi gli handler degli eventi dei bottoni
        AddHandler btnConnect.Click, AddressOf btnConnect_Click
        AddHandler btnDisconnect.Click, AddressOf btnDisconnect_Click
        AddHandler btnStartVideo.Click, AddressOf btnStartVideo_Click
        AddHandler btnStopVideo.Click, AddressOf btnStopVideo_Click
        AddHandler btnStartAudio.Click, AddressOf btnStartAudio_Click
        AddHandler btnStopAudio.Click, AddressOf btnStopAudio_Click
        'AddHandler btnTestAudio.Click, AddressOf btnTestAudio_Click
        'AddHandler btnTestAudioSend.Click, AddressOf btnTestAudioSend_Click
        'AddHandler btnTestCursor.Click, AddressOf btnTestCursor_Click

        ' Handler per screen share
        AddHandler btnShareScreen.Click, AddressOf btnShareScreen_Click
        AddHandler btnStopShare.Click, AddressOf btnStopShare_Click

        ' Handler per i controlli chat
        AddHandler btnSendChat.Click, AddressOf btnSendChat_Click
        AddHandler txtChatMessage.KeyDown, AddressOf txtChatMessage_KeyDown
        'AddHandler cmbScreenStretch.SelectionChanged, AddressOf cmbScreenStretch_SelectionChanged

    End Sub

    Private Sub InitializeParticipantsAndChat()
        ' Inizializza le liste
        lstParticipants.ItemsSource = _participants
        lstChat.ItemsSource = _chatMessages

        Debug.Print("Participants and Chat initialized")
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
                If _isVideoStarted AndAlso IsConnected Then
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

            ' Configura eventi audio
            AddHandler _videoManager.OnAudioStarted,
                Sub()
                    Dispatcher.Invoke(Sub()
                                          _isAudioStarted = True
                                          txtStatus.Text = "Connesso - Video e Audio attivi"
                                          txtAudioStatus.Text = "Audio: Attivo"
                                          txtAudioStatus.Foreground = System.Windows.Media.Brushes.Green
                                          UpdateUI()
                                      End Sub)
                End Sub

            AddHandler _videoManager.OnAudioStopped,
                Sub()
                    Dispatcher.Invoke(Sub()
                                          _isAudioStarted = False
                                          txtAudioStatus.Text = "Audio: Disattivo"
                                          txtAudioStatus.Foreground = System.Windows.Media.Brushes.Red
                                          UpdateUI()
                                      End Sub)
                End Sub

            AddHandler _videoManager.OnAudioError,
                Sub(errorMessage)
                    Dispatcher.Invoke(Sub()
                                          MessageBox.Show($"Errore audio: {errorMessage}",
                                                        "Errore Audio", MessageBoxButton.OK, MessageBoxImage.Error)
                                      End Sub)
                End Sub

            AddHandler _videoManager.OnAudioDataReady,
                Sub(audioData As Byte())
                    Debug.Print($"*** MAIN: OnAudioDataReady ricevuto - {audioData.Length} bytes")

                    ' Invia agli altri client - USA DISPATCHER PER ACCEDERE A PROPRIETÀ UI
                    Dispatcher.Invoke(Sub()
                                          If IsConnected AndAlso Not String.IsNullOrEmpty(_remoteConnectionId) Then
                                              Debug.Print($"*** MAIN: Chiamo SendAudioData, remoteId: {_remoteConnectionId}")
                                              SendAudioData(audioData)
                                          Else
                                              Debug.Print($"*** MAIN: Non posso inviare audio - IsConnected={IsConnected}, RemoteId={_remoteConnectionId}")
                                          End If
                                      End Sub)
                End Sub

        Catch ex As Exception
            MessageBox.Show($"Errore nell'inizializzazione video: {ex.Message}",
                          "Errore Inizializzazione", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub InitializeScreenShare()
        Try
            If _screenShareManager Is Nothing Then
                _screenShareManager = New ScreenShareManager()
                Debug.Print("ScreenShareManager CREATO ex-novo")
            End If

            AddHandler _screenShareManager.OnScreenError,
            Sub(errorMessage)
                Dispatcher.Invoke(Sub()
                                      MessageBox.Show($"Errore condivisione schermo: {errorMessage}",
                                  "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
                                  End Sub)
            End Sub

            AddHandler _screenShareManager.OnScreenShareStarted,
            Sub()
                Dispatcher.Invoke(Sub()
                                      _isSharingScreen = True
                                      txtStatus.Text = "Condivisione schermo attiva"
                                      'txtScreenSharePlaceholder.Visibility = Visibility.Collapsed
                                      UpdateUI()
                                  End Sub)
            End Sub

            AddHandler _screenShareManager.OnScreenShareStopped,
            Sub()
                Dispatcher.Invoke(Sub()
                                      _isSharingScreen = False
                                      txtStatus.Text = "Condivisione schermo fermata"
                                      txtScreenSharePlaceholder.Visibility = Visibility.Visible
                                      screenShareImage.Source = Nothing
                                      UpdateUI()
                                  End Sub)
            End Sub

            AddHandler _screenShareManager.OnScreenFrameReady,
            Sub(frameData As Byte(), width As Integer, height As Integer)
                If IsConnected AndAlso Not String.IsNullOrEmpty(_remoteConnectionId) Then
                    SendScreenFrame(frameData, width, height)
                End If
            End Sub

            '' Aggiornamento preview locale dello schermo
            'AddHandler _screenShareManager.PropertyChanged,
            'Sub(sender As Object, e As PropertyChangedEventArgs)
            '    If e.PropertyName = "ScreenPreview" Then
            '        Dispatcher.Invoke(Sub()
            '                              screenShareImage.Source = _screenShareManager.ScreenPreview
            '                          End Sub)
            '    End If
            'End Sub

            ' Handler per posizione cursore
            AddHandler _screenShareManager.OnCursorPositionChanged,
            Sub(x As Integer, y As Integer)
                If IsConnected AndAlso _isSharingScreen Then
                    ' Invia coordinate al server
                    SendCursorPosition(x, y)
                End If
            End Sub

            AddHandler _screenShareManager.OnScreenDimensionsReady,
                Sub(width As Integer, height As Integer)
                    If IsConnected Then
                        _connection.InvokeAsync("SendScreenDimensions", txtRoomId.Text, width, height)
                    End If
                End Sub

            ' Inizializza il cursore remoto
            InitializeRemoteCursor()

            Debug.Print("ScreenShareManager initialized")

        Catch ex As Exception
            MessageBox.Show($"Errore inizializzazione screen share: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Async Sub SendVideoFrame(frameData As Byte(), width As Integer, height As Integer)
        Try
            If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
                Dim currentRoomId As String = ""
                Dispatcher.Invoke(Sub() currentRoomId = txtRoomId.Text)

                Await _connection.InvokeAsync("SendVideoFrameToAll", currentRoomId, frameData, width, height)
            End If
        Catch ex As Exception
            Debug.Print($"Error sending video frame: {ex.Message}")
        End Try
    End Sub

    'Private Async Sub SendAudioData(audioData As Byte())
    '    Try
    '        If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
    '            Debug.Print($"*** MAIN: Invio audio a tutti, {audioData.Length} bytes")

    '            ' Usa lo stesso pattern dello screen share
    '            Await _connection.InvokeAsync("SendAudioDataToAll", txtRoomId.Text, audioData)

    '            Debug.Print("*** MAIN: Audio inviato con successo")
    '        Else
    '            Debug.Print("*** MAIN: Connessione non attiva per invio audio")
    '        End If
    '    Catch ex As Exception
    '        Debug.Print($"*** MAIN: Errore invio audio: {ex.Message}")
    '    End Try
    'End Sub

    'Private Async Sub SendAudioData(audioData As Byte())
    '    Try
    '        ' Variabili per memorizzare lo stato della connessione (thread-safe)
    '        Dim canSend As Boolean = False
    '        Dim currentRoomId As String = ""

    '        ' Accedi agli oggetti UI in modo thread-safe
    '        Dispatcher.Invoke(Sub()
    '                              If _connection IsNot Nothing Then
    '                                  canSend = (_connection.State = HubConnectionState.Connected)
    '                                  currentRoomId = txtRoomId.Text
    '                                  Debug.Print($"UI Thread - Stato connessione: {_connection.State}, Room: {currentRoomId}")
    '                              Else
    '                                  Debug.Print("UI Thread - _connection è null")
    '                              End If
    '                          End Sub)

    '        ' Ora possiamo usare currentConnection in modo sicuro
    '        If canSend Then
    '            Debug.Print($"*** MAIN: Invio audio a tutti, {audioData.Length} bytes, room: {currentRoomId}")

    '            ' La chiamata a InvokeAsync non richiede Dispatcher
    '            Await _connection.InvokeAsync("SendAudioDataToAll", currentRoomId, audioData)

    '            Debug.Print("*** MAIN: Audio inviato con successo")
    '        Else
    '            Debug.Print("*** MAIN: Connessione non attiva per invio audio")
    '        End If

    '    Catch ex As Exception
    '        Debug.Print($"*** MAIN: Errore invio audio: {ex.Message}")
    '        Debug.Print($"StackTrace: {ex.StackTrace}")
    '    End Try
    'End Sub

    Private Async Sub SendAudioData(audioData As Byte())
        Try
            Debug.Print($"📤 SendAudioData: {audioData.Length} bytes")

            Dim canSend As Boolean = False
            Dim currentRoomId As String = ""

            Dispatcher.Invoke(Sub()
                                  canSend = (_connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected)
                                  currentRoomId = txtRoomId.Text
                              End Sub)

            If canSend Then
                Debug.Print($"📤 Invio a server: room='{currentRoomId}'")
                Await _connection.InvokeAsync("SendAudioDataToAll", currentRoomId, audioData)
                Debug.Print($"📤 Invio completato")
            End If

        Catch ex As Exception
            Debug.Print($"❌ Errore SendAudioData: {ex.Message}")
        End Try
    End Sub

    Private Sub DiagnosticaAudio()
        Dispatcher.Invoke(Sub()
                              Debug.Print("=== DIAGNOSTICA AUDIO ===")
                              Debug.Print($"IsConnected: {IsConnected}")
                              Debug.Print($"RemoteConnectionId: {_remoteConnectionId}")
                              Debug.Print($"Connection State: {If(_connection IsNot Nothing, _connection.State.ToString(), "NULL")}")
                              Debug.Print($"Room ID: {txtRoomId.Text}")
                              Debug.Print($"VideoManager IsCapturing: {If(_videoManager IsNot Nothing, _videoManager.IsAudioCapturing.ToString(), "NULL")}")
                              Debug.Print("==========================")
                          End Sub)
    End Sub

    Private Async Sub btnConnect_Click(sender As Object, e As RoutedEventArgs)
        Try

            ' Crea un handler HTTP che accetta qualsiasi certificato
            Dim httpHandler As New HttpClientHandler()
            httpHandler.ServerCertificateCustomValidationCallback =
            Function(senderObj As Object, certificate As X509Certificate2, chain As X509Chain, sslPolicyErrors As SslPolicyErrors)
                Console.WriteLine($"SSL Policy Errors: {sslPolicyErrors}")
                Console.WriteLine($"Certificato: {certificate?.Subject}")
                ' ACCETTA TUTTO PER DEBUG
                Return True
            End Function

            ' Opzioni per la connessione
            Dim connectionOptions = New Action(Of Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions)(
            Sub(options)
                options.HttpMessageHandlerFactory = Function(inner) httpHandler
                ' Aumenta timeout per debug
                options.CloseTimeout = TimeSpan.FromSeconds(60)
            End Sub)

            txtStatus.Text = "Connessione in corso..."

            ' Crea la connessione con le opzioni custom
            _connection = New HubConnectionBuilder().
                                WithUrl(txtServerUrl.Text.Trim() & "/conferencehub", connectionOptions).
                                WithAutomaticReconnect().
                                ConfigureLogging(Sub(logging As ILoggingBuilder)
                                                     logging.AddDebug()
                                                     logging.SetMinimumLevel(LogLevel.Warning)  ' Solo warning ed errori
                                                 End Sub).
                                WithServerTimeout(TimeSpan.FromSeconds(60)).          ' Timeout server
                                WithKeepAliveInterval(TimeSpan.FromSeconds(15)).      ' Keep-alive
                                Build()

            ' Configura gli handler degli eventi dal server
            _connection.On("UserJoined",
                Sub(connectionId As String, userName As String)
                    Dispatcher.Invoke(Sub()
                                          _remoteConnectionId = connectionId
                                          txtStatus.Text = $"{userName} si è unito alla stanza"
                                          'MessageBox.Show($"Benvenuto {userName}!", "Nuovo Utente", MessageBoxButton.OK, MessageBoxImage.Information)
                                      End Sub)
                End Sub)

            _connection.On("UserLeft",
                Sub(connectionId As String)
                    Dispatcher.Invoke(Sub()
                                          If _remoteConnectionId = connectionId Then
                                              _remoteConnectionId = ""
                                              txtStatus.Text = "Utente remoto disconnesso"
                                              'MessageBox.Show("L'utente remoto ha lasciato la stanza.", "Utente Disconnesso", MessageBoxButton.OK, MessageBoxImage.Warning)
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

            ' ===== HANDLER PER SCREEN SHARE - CON LOG DETTAGLIATO =====
            _connection.On(Of String, Byte(), Integer, Integer)("ReceiveScreenFrame",
                        Sub(senderConnectionId, frameData, width, height)
                            Debug.Print($"*** SCREEN SHARE DEBUG: Ricevuto frame da {senderConnectionId}, {width}x{height}, {frameData.Length} bytes")

                            Dispatcher.Invoke(Sub()
                                                  Try
                                                      If frameData IsNot Nothing AndAlso frameData.Length > 0 Then
                                                          ' Converti JPEG in BitmapImage
                                                          Dim bitmapImage As New BitmapImage()
                                                          Using stream = New IO.MemoryStream(frameData)
                                                              bitmapImage.BeginInit()
                                                              bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                                              bitmapImage.StreamSource = stream
                                                              bitmapImage.EndInit()
                                                              bitmapImage.Freeze()
                                                          End Using

                                                          ' Mostra nell'immagine remota
                                                          screenShareImage.Source = bitmapImage
                                                          UpdateScreenShareContainer(width, height)
                                                          txtScreenSharePlaceholder.Visibility = Visibility.Collapsed

                                                          Debug.Print($"*** SCREEN SHARE DEBUG: Immagine mostrata con successo")
                                                      Else
                                                          Debug.Print($"*** SCREEN SHARE DEBUG: frameData nullo o vuoto")
                                                      End If
                                                  Catch ex As Exception
                                                      Debug.Print($"*** SCREEN SHARE DEBUG: Errore visualizzazione: {ex.Message}")
                                                  End Try
                                              End Sub)
                        End Sub)

            '' Aggiungi handler per ricevere frame video
            '_connection.On("ReceiveVideoFrame",
            'Sub(senderConnectionId As String, frameData As Byte(), width As Integer, height As Integer)
            '    Debug.Print($"DEBUG: Ricevuto frame da {senderConnectionId}, dimensione: {frameData?.Length} bytes")
            '    Dispatcher.Invoke(Sub()
            '                          ' Aggiorna il video remoto
            '                          If _videoManager IsNot Nothing Then
            '                              _videoManager.ReceiveRemoteFrame(frameData, width, height)
            '                              txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed

            '                              ' Forza l'aggiornamento dell'UI
            '                              remoteVideoImage.InvalidateVisual()
            '                              Debug.Print("DEBUG: ReceiveRemoteFrame called")
            '                          Else
            '                              Debug.Print("DEBUG: VideoManager è null!")
            '                          End If

            '                          ' Aggiorna lo stato
            '                          If String.IsNullOrEmpty(_remoteConnectionId) Then
            '                              _remoteConnectionId = senderConnectionId
            '                          End If
            '                      End Sub)
            'End Sub)

            ' Handler per ReceiveVideoFrame - AGGIORNA ANTEPRIME
            _connection.On(Of String, Byte(), Integer, Integer)("ReceiveVideoFrame",
                            Sub(senderConnectionId, frameData, width, height)
                                Dispatcher.Invoke(Sub()
                                                      Try
                                                          Debug.Print($"Ricevuto video frame da {senderConnectionId}, {width}x{height}, {frameData.Length} bytes")
                                                          If frameData IsNot Nothing AndAlso frameData.Length > 0 Then
                                                              ' Converti in BitmapImage
                                                              Dim bitmapImage As New BitmapImage()
                                                              Using stream = New IO.MemoryStream(frameData)
                                                                  bitmapImage.BeginInit()
                                                                  bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                                                  bitmapImage.StreamSource = stream
                                                                  bitmapImage.EndInit()
                                                                  bitmapImage.Freeze()
                                                              End Using

                                                              ' Salva il sender come remoteConnectionId se non già impostato
                                                              'If String.IsNullOrEmpty(_remoteConnectionId) Then
                                                              _remoteConnectionId = senderConnectionId
                                                              'End If

                                                              ' IMPORTANTE: Aggiorna remoteVideoImage (colonna destra)
                                                              remoteVideoImage.Source = bitmapImage
                                                              txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed

                                                              '' Se è il remote principale, aggiorna remoteVideoImage
                                                              'If senderConnectionId = _remoteConnectionId Then
                                                              '    'remoteVideoImage.Source = bitmapImage
                                                              '    'txtRemoteVideoPlaceholder.Visibility = Visibility.Collapsed

                                                              '    ' Aggiorna anche nella lista partecipanti
                                                              '    Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = senderConnectionId)
                                                              '    If participant IsNot Nothing Then
                                                              '        participant.VideoSource = bitmapImage
                                                              '        participant.HasVideo = True
                                                              '    End If
                                                              'End If

                                                              ' Se vuoi anche aggiornare l'anteprima nella lista partecipanti
                                                              Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = senderConnectionId)
                                                              If participant IsNot Nothing Then
                                                                  participant.VideoSource = bitmapImage
                                                                  participant.HasVideo = True
                                                              End If

                                                              ' AGGIUNGI ALLA LISTA ANTEPRIME (per icRemoteWebcams)
                                                              ' Qui puoi implementare un ItemsControl separato per le anteprime
                                                              UpdateRemoteWebcamPreview(senderConnectionId, bitmapImage)
                                                          End If
                                                      Catch ex As Exception
                                                          Debug.Print($"Error processing remote video: {ex.Message}")
                                                      End Try
                                                  End Sub)
                            End Sub)

            ' Handler per ricevere audio
            _connection.On(Of String, Byte())("ReceiveAudioData",
                                    Sub(senderConnectionId As String, audioData As Byte())
                                        Debug.Print($"*** AUDIO DEBUG: Ricevuti {audioData?.Length} bytes da {senderConnectionId}")

                                        ' IMPORTANTE: Usa Dispatcher per aggiornare UI
                                        Dispatcher.Invoke(Sub()
                                                              If _videoManager IsNot Nothing Then
                                                                  Debug.Print("*** AUDIO DEBUG: Chiamo ReceiveRemoteAudio")
                                                                  _videoManager.ReceiveRemoteAudio(audioData)
                                                              Else
                                                                  Debug.Print("*** AUDIO DEBUG: _videoManager è null!")
                                                              End If
                                                          End Sub)
                                    End Sub)

            ' Handler per UserJoined - AGGIORNA LISTA PARTECIPANTI
            _connection.On(Of String, String)("UserJoined",
                                    Sub(connectionId, userName)
                                        Debug.Print($"UserJoined: {userName} ({connectionId})")

                                        Dispatcher.Invoke(Sub()
                                                              ' IMPORTANTE: Se non ho ancora un remote, questo è il mio remote
                                                              'If String.IsNullOrEmpty(_remoteConnectionId) Then
                                                              _remoteConnectionId = connectionId
                                                              'End If

                                                              ' Aggiungi alla lista partecipanti
                                                              Dim participant As New Participant With {
                                                                                                        .ConnectionId = connectionId,
                                                                                                        .UserName = userName,
                                                                                                        .HasVideo = False,
                                                                                                        .HasAudio = False
                                                                                                        }
                                                              _participants.Add(participant)

                                                              ' Aggiorna GroupBox header
                                                              UpdateParticipantsHeader()
                                                          End Sub)
                                    End Sub)

            ' Handler per UserLeft - RIMUOVI DALLA LISTA
            _connection.On(Of String)("UserLeft",
                        Sub(connectionId)
                            Debug.Print($"UserLeft: {connectionId}")
                            Dispatcher.Invoke(Sub()
                                                  'Se era il remote principale, pulisci
                                                  'If connectionId = _remoteConnectionId Then
                                                  localVideoImage.Source = Nothing
                                                  remoteVideoImage.Source = Nothing
                                                  txtRemoteVideoPlaceholder.Visibility = Visibility.Visible
                                                  _remoteConnectionId = ""
                                                  'End If

                                                  ' Rimuovi dalla lista
                                                  Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = connectionId)
                                                  If participant IsNot Nothing Then
                                                      _participants.Remove(participant)
                                                  End If

                                                  ' Rimuovi anche dalle anteprime
                                                  If _remoteVideoSources.ContainsKey(connectionId) Then
                                                      _remoteVideoSources.Remove(connectionId)
                                                  End If

                                                  ' Aggiorna GroupBox header
                                                  UpdateParticipantsHeader()

                                                  '' Se era il remote principale, pulisci
                                                  'If _remoteConnectionId = connectionId Then
                                                  '    _remoteConnectionId = ""
                                                  '    remoteVideoImage.Source = Nothing
                                                  'End If
                                              End Sub)
                        End Sub)

            ' Handler per ExistingUsers
            '_connection.On(Of Object)("ExistingUsers",
            '                Sub(users)
            '                    Dispatcher.Invoke(Sub()
            '                                          _participants.Clear()

            '                                          ' Converte e aggiunge tutti gli utenti esistenti
            '                                          If users IsNot Nothing Then
            '                                              For Each user In users
            '                                                  Dim connectionId = user.GetType().GetProperty("ConnectionId")?.GetValue(user)?.ToString()
            '                                                  Dim userName = user.GetType().GetProperty("UserName")?.GetValue(user)?.ToString()

            '                                                  If Not String.IsNullOrEmpty(connectionId) Then
            '                                                      Dim participant As New Participant With {
            '                                        .ConnectionId = connectionId,
            '                                        .UserName = If(userName IsNot Nothing, userName, "Utente"),
            '                                        .HasVideo = False,
            '                                        .HasAudio = False
            '                                    }
            '                                                      _participants.Add(participant)
            '                                                  End If
            '                                              Next
            '                                          End If

            '                                          UpdateParticipantsHeader()
            '                                      End Sub)
            '                End Sub)

            _connection.On(Of System.Text.Json.JsonElement)("ExistingUsers",
                            Sub(jsonElement)
                                Dispatcher.Invoke(Sub()
                                                      Dim usersList As New List(Of UserInfo)()

                                                      ' Verifica che sia un array
                                                      If jsonElement.ValueKind = System.Text.Json.JsonValueKind.Array Then
                                                          Debug.Print($"📋 È un array con {jsonElement.GetArrayLength()} elementi")

                                                          For Each item In jsonElement.EnumerateArray()
                                                              Try
                                                                  Dim user As New UserInfo()

                                                                  ' Estrai le proprietà in modo sicuro
                                                                  Dim prop As System.Text.Json.JsonElement
                                                                  If item.TryGetProperty("connectionId", prop) Then
                                                                      user.ConnectionId = prop.GetString()
                                                                  End If

                                                                  If item.TryGetProperty("userName", prop) Then
                                                                      user.UserName = prop.GetString()
                                                                  End If

                                                                  If item.TryGetProperty("hasVideo", prop) Then
                                                                      user.HasVideo = prop.GetBoolean()
                                                                  End If

                                                                  If item.TryGetProperty("hasAudio", prop) Then
                                                                      user.HasAudio = prop.GetBoolean()
                                                                  End If

                                                                  If item.TryGetProperty("isScreenSharing", prop) Then
                                                                      user.IsScreenSharing = prop.GetBoolean()
                                                                  End If

                                                                  usersList.Add(user)
                                                                  Debug.Print($"   - Aggiunto: {user.UserName}")

                                                              Catch ex As Exception
                                                                  Debug.Print($"   ❌ Errore su un elemento: {ex.Message}")
                                                              End Try
                                                          Next
                                                      Else
                                                          Debug.Print($"❌ JsonElement non è un array: {jsonElement.ValueKind}")
                                                      End If

                                                      _participants.Clear()

                                                      If usersList IsNot Nothing Then
                                                          For Each user In usersList
                                                              Dim participant As New Participant With {
                                                                                                        .ConnectionId = user.ConnectionId,
                                                                                                        .UserName = If(user.UserName IsNot Nothing, user.UserName, "Utente"),
                                                                                                        .HasVideo = user.HasAudio,
                                                                                                        .HasAudio = user.HasAudio,
                                                                                                        .IsScreenSharing = user.IsScreenSharing
                                                                                                    }
                                                              _participants.Add(participant)

                                                              ' IMPORTANTE: Se non ho remote e questo non sono io, impostalo
                                                              'If String.IsNullOrEmpty(_remoteConnectionId) AndAlso user.ConnectionId <> _localConnectionId Then
                                                              _remoteConnectionId = user.ConnectionId
                                                              'Debug.Print($"✅ Impostato _remoteConnectionId da ExistingUsers: {user.ConnectionId}")
                                                              'End If
                                                          Next
                                                      End If

                                                      UpdateParticipantsHeader()
                                                  End Sub)
                            End Sub)

            ' Handler per ricevere messaggi chat
            _connection.On(Of String, String, DateTime)("ReceiveChatMessage",
                            Sub(userName, message, timestamp)
                                Dispatcher.Invoke(Sub()
                                                      Dim chatMsg As New ChatMessage With {
                                                                                                .Sender = userName,
                                                                                                .Message = message,
                                                                                                .Timestamp = Format(timestamp, "dd/MM/yyyy HH:mm:ss")
                                                                                            }
                                                      _chatMessages.Add(chatMsg)

                                                      ' Scroll in fondo
                                                      If lstChat.Items.Count > 0 Then
                                                          lstChat.ScrollIntoView(lstChat.Items(lstChat.Items.Count - 1))
                                                      End If
                                                  End Sub)
                            End Sub)

            _connection.On(Of List(Of UserInfo))("ParticipantsList",
                            Sub(participantsList)
                                Dispatcher.Invoke(Sub()
                                                      Try
                                                          Debug.Print($"ParticipantsList ricevuto con {participantsList?.Count} utenti")

                                                          ' Pulisci la lista
                                                          _participants.Clear()

                                                          ' Processa la lista
                                                          If participantsList IsNot Nothing Then
                                                              For Each user In participantsList
                                                                  Dim participant As New Participant With {
                                                                                                            .ConnectionId = user.ConnectionId,
                                                                                                            .UserName = If(user.UserName IsNot Nothing, user.UserName, "Utente"),
                                                                                                            .HasVideo = user.HasVideo,
                                                                                                            .HasAudio = user.HasAudio,
                                                                                                            .IsScreenSharing = user.IsScreenSharing
                                                                                                        }
                                                                  _participants.Add(participant)
                                                                  Debug.Print($"  - Aggiunto: {user.UserName} ({user.ConnectionId})")
                                                              Next
                                                          End If

                                                          UpdateParticipantsHeader()
                                                          Debug.Print($"Lista partecipanti aggiornata: {_participants.Count} utenti")

                                                      Catch ex As Exception
                                                          Debug.Print($"Errore in ParticipantsList: {ex.Message}")
                                                      End Try
                                                  End Sub)
                            End Sub)

            ' Handler per aggiornamento stato partecipanti
            _connection.On(Of String, Boolean, Boolean)("ParticipantStatusChanged",
                            Sub(connectionId, hasVideo, hasAudio)
                                Dispatcher.Invoke(Sub()
                                                      Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = connectionId)
                                                      If participant IsNot Nothing Then
                                                          participant.HasVideo = hasVideo
                                                          participant.HasAudio = hasAudio
                                                      End If
                                                  End Sub)
                            End Sub)

            ' Handler per aggiornamento stato partecipanti
            _connection.On(Of String, Boolean, Boolean)("ParticipantStatusChanged",
                            Sub(connectionId, hasVideo, hasAudio)
                                Dispatcher.Invoke(Sub()
                                                      Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = connectionId)
                                                      If participant IsNot Nothing Then
                                                          participant.HasVideo = hasVideo
                                                          participant.HasAudio = hasAudio

                                                          ' Aggiorna UI se necessario (es. icona video/audio)
                                                          Debug.Print($"Stato aggiornato per {participant.UserName}: Video={hasVideo}, Audio={hasAudio}")
                                                      End If
                                                  End Sub)
                            End Sub)

            ' Handler per aggiornamento stato screen share
            _connection.On(Of String, Boolean)("ScreenSharingStatusChanged",
                        Sub(connectionId, isSharing)
                            Dispatcher.Invoke(Sub()
                                                  Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = connectionId)
                                                  If participant IsNot Nothing Then
                                                      participant.IsScreenSharing = isSharing

                                                      ' Se sta condividendo lo schermo, possiamo evidenziarlo nella lista
                                                      Debug.Print($"{participant.UserName} {(If(isSharing, "sta condividendo lo schermo", "ha fermato la condivisione"))}")
                                                  End If
                                              End Sub)
                        End Sub)

            ' Handler per messaggi privati (opzionale)
            _connection.On(Of String, String, DateTime)("ReceivePrivateMessage",
                        Sub(userName, message, timestamp)
                            Dispatcher.Invoke(Sub()
                                                  Dim chatMsg As New ChatMessage With {
                                    .Sender = $"🔒 {userName} (privato)",
                                    .Message = message,
                                    .Timestamp = timestamp
                                }
                                                  _chatMessages.Add(chatMsg)

                                                  ' Evidenzia in qualche modo che è un messaggio privato
                                              End Sub)
                        End Sub)

            ' Handler per quando qualcuno ferma lo screenshare (REMOTO)
            _connection.On(Of String)("ScreenShareStopped",
                        Sub(senderConnectionId)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print($"ScreenShareStopped ricevuto da {senderConnectionId}")

                                                  If senderConnectionId = _localConnectionId Then
                                                      ' IO ho fermato - la mia finestra era già minimizzata, la ripristino?
                                                      ' Opzionale: RestoreWindow() se vuoi

                                                      ' Io ho fermato - nascondo il mio cursore remoto
                                                      HideRemoteCursor()

                                                      Debug.Print("IO ho fermato")
                                                  Else
                                                      ' Qualcun altro ha fermato - ripristino le colonne
                                                      RestoreSideColumns()

                                                      ' Nascondi il cursore
                                                      HideRemoteCursor()

                                                      Debug.Print("Altri hanno fermato - ripristino colonne")
                                                  End If

                                                  ' Svuota l'immagine dello screenshare
                                                  screenShareImage.Source = Nothing
                                                  txtScreenSharePlaceholder.Visibility = Visibility.Visible

                                                  ' Aggiorna lo stato nella lista partecipanti
                                                  Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = senderConnectionId)
                                                  If participant IsNot Nothing Then
                                                      participant.IsScreenSharing = False
                                                  End If
                                              End Sub)
                        End Sub)

            ' Handler per quando qualcuno inizia a condividere lo schermo
            _connection.On(Of String)("ScreenShareStarted",
                        Sub(sharerConnectionId)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print($"ScreenShareStarted da {sharerConnectionId}")

                                                  ' IMPORTANTE: Salva chi sta condividendo
                                                  _remoteConnectionId = sharerConnectionId

                                                  If sharerConnectionId = _localConnectionId Then
                                                      ' IO sono il condividitore - minimizzo la mia finestra
                                                      MinimizeWindow()
                                                      Debug.Print("IO condivido - minimizzo finestra")
                                                  Else
                                                      ' Qualcun altro condivide - collasso le colonne laterali
                                                      CollapseSideColumns(True)

                                                      _connection.InvokeAsync("RequestScreenDimensions", txtRoomId.Text, sharerConnectionId)

                                                      ' Nascondi il cursore remoto
                                                      HideRemoteCursor()

                                                      Debug.Print("Altri condividono - collasso colonne")
                                                  End If

                                              End Sub)
                        End Sub)

            ' Aggiungi handler per ricevere dimensioni schermo
            _connection.On(Of Integer, Integer)("ScreenDimensions",
                        Sub(width, height)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print($"Ricevute dimensioni schermo: {width}x{height}")
                                                  ' Salva le dimensioni per usarle nel calcolo del cursore
                                                  _remoteScreenWidth = width
                                                  _remoteScreenHeight = height
                                              End Sub)
                        End Sub)

            '' Handler per quando qualcuno ferma lo schermo
            '_connection.On(Of String)("ScreenShareStopped",
            '            Sub(sharerConnectionId)
            '                Dispatcher.Invoke(Sub()
            '                                      Debug.Print($"ScreenShareStopped da {sharerConnectionId}")

            '                                      ' Se NON sono io a fermare, ripristina le colonne
            '                                      If sharerConnectionId <> _localConnectionId Then
            '                                          RestoreSideColumns()
            '                                      End If

            '                                      ' Pulisci l'immagine dello screenshare
            '                                      screenShareImage.Source = Nothing
            '                                      txtScreenSharePlaceholder.Visibility = Visibility.Visible
            '                                  End Sub)
            '            End Sub)

            ' Handler per quando non puoi condividere perché qualcun altro già condivide
            _connection.On(Of String)("ScreenShareBlocked",
                        Sub(message)
                            Dispatcher.Invoke(Sub()
                                                  MessageBox.Show(message, "Condivisione bloccata", MessageBoxButton.OK, MessageBoxImage.Warning)
                                                  btnShareScreen.IsEnabled = True
                                              End Sub)
                        End Sub)

            ' Handler per posizione cursore
            _connection.On(Of String, Integer, Integer)("CursorPosition",
                        Sub(senderConnectionId, x, y)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print("=== DEBUG COORDINATE RICEVUTE ===")
                                                  Debug.Print($"Coordinate originali dal server: ({x}, {y})")
                                                  Debug.Print($"screenShareImage ActualWidth: {screenShareImage.ActualWidth}")
                                                  Debug.Print($"screenShareImage ActualHeight: {screenShareImage.ActualHeight}")
                                                  Debug.Print($"screenShareImage Source: {screenShareImage.Source IsNot Nothing}")
                                                  Debug.Print($"_remoteScreenWidth: {_remoteScreenWidth}")
                                                  Debug.Print($"_remoteScreenHeight: {_remoteScreenHeight}")

                                                  ' Aggiorna la posizione del cursore solo se lo screen share è attivo
                                                  ' e proviene dalla persona che sta condividendo
                                                  'If _isSharingScreen AndAlso senderConnectionId = _remoteConnectionId Then
                                                  '    UpdateRemoteCursorPosition(x, y)
                                                  '    Debug.Print($"Remote cursor: ({x}, {y})")
                                                  'End If
                                                  If Not _isSharingScreen AndAlso senderConnectionId = _remoteConnectionId Then
                                                      UpdateRemoteCursorPosition(x, y)
                                                      Debug.Print($"✅ CURSOR: Aggiornato a ({x}, {y})")
                                                  Else
                                                      Debug.Print($"⏭️ CURSOR: Ignorato - isSharingScreen={_isSharingScreen}, sender={senderConnectionId}, remote={_remoteConnectionId}")
                                                  End If
                                              End Sub)
                        End Sub)

            _connection.On(Of String)("SendScreenDimensions",
                        Sub(requestorConnectionId)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print("Richiesto invio dimensioni schermo")
                                                  If _screenShareManager IsNot Nothing Then
                                                      _screenShareManager.SendMyScreenDimensions()
                                                  End If
                                              End Sub)
                        End Sub)

            ' Handler per quando qualcuno ferma il video
            _connection.On(Of String)("VideoStopped",
                        Sub(senderConnectionId)
                            Dispatcher.Invoke(Sub()
                                                  Debug.Print($"📥 Ricevuto VideoStopped da {senderConnectionId}")

                                                  ' Se è il video remoto principale che viene fermato
                                                  'If senderConnectionId = _remoteConnectionId Then
                                                  remoteVideoImage.Source = Nothing
                                                  txtRemoteVideoPlaceholder.Visibility = Visibility.Visible
                                                  Debug.Print("🧹 Pulito video remoto principale")
                                                  'End If

                                                  ' Aggiorna anche nella lista partecipanti
                                                  Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = senderConnectionId)
                                                  If participant IsNot Nothing Then
                                                      participant.HasVideo = False
                                                      participant.VideoSource = Nothing
                                                      Debug.Print($"👤 Aggiornato stato video per {participant.UserName}")
                                                  End If

                                                  ' Se stai usando icRemoteWebcams, aggiorna anche quello
                                                  ' Forza aggiornamento ItemsControl
                                                  'icRemoteWebcams.Items.Refresh()
                                              End Sub)
                        End Sub)


            ' Connessione al server
            Await _connection.StartAsync()

            IsConnected = True
            _localConnectionId = _connection.ConnectionId
            Debug.Print($"_localConnectionId: {_localConnectionId}")
            txtStatus.Text = "Connesso - ID: " & _localConnectionId

            ' Unisciti alla stanza
            Await _connection.InvokeAsync("JoinRoom", txtRoomId.Text, txtUserName.Text)

            'MessageBox.Show($"Connesso al server! Il tuo ID: {_localConnectionId}",
            '              "Connessione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information)

        Catch ex As Exception
            MessageBox.Show($"Errore di connessione: {ex.Message}", "Errore",
                          MessageBoxButton.OK, MessageBoxImage.Error)
            txtStatus.Text = "Errore di connessione"
            IsConnected = False
        End Try
    End Sub

    Private Sub UpdateParticipantsHeader()
        Dim groupBox = TryCast(lstParticipants.Parent, GroupBox)
        If groupBox IsNot Nothing Then
            groupBox.Header = $"Partecipanti ({_participants.Count})"
        End If
    End Sub

    Private Sub UpdateRemoteWebcamPreview(connectionId As String, imageSource As ImageSource)
        ' Trova il participant
        Dim participant = _participants.FirstOrDefault(Function(p) p.ConnectionId = connectionId)
        If participant IsNot Nothing Then
            participant.VideoSource = imageSource
        End If

        ' Se vuoi usare un ItemsControl separato, puoi gestirlo qui
        ' icRemoteWebcams.ItemsSource = _participants.Where(Function(p) p.HasVideo)
    End Sub

    Private Sub UpdateScreenShareContainer(width As Integer, height As Integer)
        ' Calcola le proporzioni
        Dim aspectRatio = CDbl(width) / CDbl(height)

        ' Ottieni il GroupBox padre
        Dim parentGroupBox = TryCast(screenShareImage.Parent, GroupBox)
        If parentGroupBox IsNot Nothing Then
            ' Imposta dimensioni minime per mantenere le proporzioni
            parentGroupBox.MinWidth = 200
            parentGroupBox.MinHeight = 200 / aspectRatio
        End If
    End Sub

    'Private Sub cmbScreenStretch_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
    '    If screenShareImage Is Nothing Then Return

    '    Select Case cmbScreenStretch.SelectedIndex
    '        Case 0
    '            screenShareImage.Stretch = Stretch.Uniform
    '        Case 1
    '            screenShareImage.Stretch = Stretch.Fill
    '        Case 2
    '            screenShareImage.Stretch = Stretch.None
    '        Case 3
    '            screenShareImage.Stretch = Stretch.UniformToFill
    '    End Select
    'End Sub

    Private Sub OnFrameSendTimerElapsed(sender As Object, e As Timers.ElapsedEventArgs)
        ' Questo timer assicura che inviamo frame periodicamente
        ' anche se non ci sono nuovi frame dalla webcam
        If _isVideoStarted AndAlso _videoManager IsNot Nothing Then
            ' Forza l'invio di un frame (se disponibile)
            ' Il VideoManager emetterà l'evento OnFrameReadyToSend se ha un frame
        End If
    End Sub

    Private Async Sub btnDisconnect_Click(sender As Object, e As RoutedEventArgs)
        If _connection IsNot Nothing Then

            ' Ferma video se attivo
            If _videoManager IsNot Nothing Then
                _videoManager.StopVideoCapture()
            End If

            ' Ferma screen share se attivo
            If _screenShareManager IsNot Nothing Then
                _screenShareManager.StopScreenShare()
            End If

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
        ' Pulisci le liste
        _participants.Clear()
        _chatMessages.Clear()
        _remoteVideoSources.Clear()
        IsConnected = False
        txtStatus.Text = "Disconnesso"
        _localConnectionId = ""
        _remoteConnectionId = ""
        localVideoImage.Source = Nothing
        remoteVideoImage.Source = Nothing

        'MessageBox.Show("Disconnesso dal server.", "Disconnessione",
        '              MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    Private Async Sub btnStartVideo_Click(sender As Object, e As RoutedEventArgs)
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
            'MessageBox.Show("Sto cercando di accedere alla webcam..." & vbCrLf &
            '              "Assicurati di aver concesso i permessi per la webcam." & vbCrLf &
            '              "Potrebbe essere visualizzata una richiesta di autorizzazione.",
            '              "Accesso Webcam", MessageBoxButton.OK, MessageBoxImage.Information)

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
                _isVideoStarted = True
                txtStatus.Text = "Video attivo"
                txtVideoStatus.Text = "Video: Attivo"
                txtVideoStatus.Foreground = System.Windows.Media.Brushes.Green

                _frameSendTimer.Start()

                Await UpdateVideoStatus(True)

                'MessageBox.Show("Webcam attivata con successo! Il video verrà inviato agli altri utenti.",
                '              "Successo", MessageBoxButton.OK, MessageBoxImage.Information)
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

    Private Async Sub btnStopVideo_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _videoManager IsNot Nothing Then
                _videoManager.StopVideoCapture()

                ' Ferma l'invio video
                _isVideoStarted = False
                _frameSendTimer.Stop()

                ' sbianco l'immagine locale (nel caso rimanga l'ultimo frame)
                localVideoImage.Source = Nothing

                ' NOTIFICA IL SERVER CHE IL VIDEO È STATO FERMATO
                If IsConnected AndAlso _connection IsNot Nothing Then
                    Await _connection.InvokeAsync("StopVideo", txtRoomId.Text)
                    Debug.Print("📤 Notifica StopVideo inviata al server")
                End If

                Await UpdateVideoStatus(False)

                UpdateUI()

                'MessageBox.Show("Webcam disattivata", "Info",
                '              MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        Catch ex As Exception
            MessageBox.Show($"Errore nella fermata del video: {ex.Message}",
                          "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Async Sub btnStartAudio_Click(sender As Object, e As RoutedEventArgs)
        If _videoManager Is Nothing Then
            MessageBox.Show("Video Manager non inizializzato", "Errore",
                      MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        If _videoManager.IsAudioCapturing Then
            MessageBox.Show("Audio già attivo", "Info",
                      MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Try
            btnStartAudio.IsEnabled = False
            txtAudioStatus.Text = "Audio: Avvio..."

            '' Mostra messaggio informativo
            'MessageBox.Show("Sto cercando di accedere al microfono..." & vbCrLf &
            '          "Assicurati di aver concesso i permessi per il microfono.",
            '          "Accesso Microfono", MessageBoxButton.OK, MessageBoxImage.Information)

            Dim success = _videoManager.StartAudioCapture()

            If success Then
                Await UpdateAudioStatus(True)

                'MessageBox.Show("Microfono attivato con successo! L'audio verrà inviato agli altri utenti.",
                '          "Successo", MessageBoxButton.OK, MessageBoxImage.Information)
            Else
                MessageBox.Show("Impossibile avviare il microfono. Controlla:" & vbCrLf &
                          "1. I permessi del microfono" & vbCrLf &
                          "2. Che il microfono sia collegato e funzionante" & vbCrLf &
                          "3. Che non sia già in uso da un'altra applicazione",
                          "Errore Microfono", MessageBoxButton.OK, MessageBoxImage.Error)
            End If

        Catch ex As Exception
            MessageBox.Show($"Errore nell'avvio dell'audio: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Async Sub btnStopAudio_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _videoManager IsNot Nothing Then
                _videoManager.StopAudioCapture()

                Await UpdateAudioStatus(False)

                'MessageBox.Show("Microfono disattivato", "Info",
                '          MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        Catch ex As Exception
            MessageBox.Show($"Errore nella fermata dell'audio: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Sub btnTestAudio_Click(sender As Object, e As RoutedEventArgs)
        If _videoManager IsNot Nothing Then
            _videoManager.TestLocalAudio()
        End If
    End Sub

    Private Async Sub btnTestAudioSend_Click(sender As Object, e As RoutedEventArgs)
        Try
            DiagnosticaAudio()

            ' Crea dati audio di test
            Dim testAudio As Byte() = New Byte(999) {}
            Dim rnd As New Random()
            rnd.NextBytes(testAudio)

            Debug.Print("*** TEST: Invio audio di test")

            If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
                Await _connection.InvokeAsync("SendAudioDataToAll", txtRoomId.Text, testAudio)
                MessageBox.Show("Audio di test inviato!", "Test", MessageBoxButton.OK, MessageBoxImage.Information)
            Else
                MessageBox.Show("Non connesso!", "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
            End If

        Catch ex As Exception
            Debug.Print($"Errore test audio: {ex.Message}")
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    'Private Sub btnMuteMic_Click(sender As Object, e As RoutedEventArgs)
    '    _isMicMuted = Not _isMicMuted

    '    If _isMicMuted Then
    '        btnMuteMic.Content = "🎤 Microfono MUTO"
    '        btnMuteMic.Background = System.Windows.Media.Brushes.Red
    '        ' Ferma cattura audio
    '        If _videoManager IsNot Nothing Then
    '            _videoManager.StopAudioCapture()
    '        End If
    '    Else
    '        btnMuteMic.Content = "🎤 Microfono ATTIVO"
    '        btnMuteMic.Background = System.Windows.Media.Brushes.LightGreen
    '        ' Riavvia cattura audio
    '        If _videoManager IsNot Nothing Then
    '            _videoManager.StartAudioCapture()
    '        End If
    '    End If
    'End Sub

    Private Sub btnToggleChat_Click(sender As Object, e As RoutedEventArgs)
        Try
            _isChatCollapsed = Not _isChatCollapsed

            If _isChatCollapsed Then
                ' Collassa la chat
                MainContentGrid.ColumnDefinitions(2).Width = New GridLength(0)
                btnToggleChat.Content = "💬 Mostra Chat"
                btnToggleChat.Background = System.Windows.Media.Brushes.LightGreen
                Debug.Print("Chat collassata")
            Else
                ' Ripristina la chat
                MainContentGrid.ColumnDefinitions(2).Width = New GridLength(3, GridUnitType.Star)
                btnToggleChat.Content = "💬 Chat"
                btnToggleChat.Background = System.Windows.Media.Brushes.LightGray
                Debug.Print("Chat ripristinata")
            End If

            ' Forza aggiornamento layout
            MainContentGrid.UpdateLayout()

        Catch ex As Exception
            Debug.Print($"Errore in btnToggleChat_Click: {ex.Message}")
        End Try
    End Sub


    Private Async Sub btnShareScreen_Click(sender As Object, e As RoutedEventArgs)
        If _screenShareManager Is Nothing Then
            MessageBox.Show("ScreenShareManager non inizializzato", "Errore",
                      MessageBoxButton.OK, MessageBoxImage.Error)
            Return
        End If

        If _screenShareManager.IsSharing Then
            MessageBox.Show("Condivisione già attiva", "Info",
                      MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Try
            btnShareScreen.IsEnabled = False
            txtStatus.Text = "Avvio condivisione schermo..."

            ' Chiedi al server se può iniziare la condivisione
            Dim canShare = Await _connection.InvokeAsync(Of Boolean)("StartScreenShare", txtRoomId.Text)

            If canShare Then

                '' Chiedi conferma
                'Dim result = MessageBox.Show(
                '                            "Stai per condividere l'intero schermo con gli altri utenti." & vbCrLf &
                '                            "Tutte le finestre e notifiche saranno visibili." & vbCrLf &
                '                            "Procedere?",
                '                            "Condivisione Schermo",
                '                            MessageBoxButton.YesNo,
                '                            MessageBoxImage.Warning)

                'If result = MessageBoxResult.Yes Then
                Dim success = _screenShareManager.StartScreenShare()

                If success Then

                    _isSharingScreen = True

                    ' minimizzo per me
                    MinimizeWindow()


                    Await UpdateScreenSharingStatus(True)

                    ' Aggiorna UI
                    UpdateUI()
                    txtStatus.Text = "Condivisione schermo attiva"
                    Debug.Print("Screen share avviato con successo")


                    'MessageBox.Show("Condivisione schermo avviata!", "Successo", MessageBoxButton.OK, MessageBoxImage.Information)
                Else
                    MessageBox.Show("Condivisione schermo non avviata!", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Exclamation)
                End If
                'End If

            End If

        Catch ex As Exception
            MessageBox.Show($"Errore avvio condivisione: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
            btnShareScreen.IsEnabled = True
        Finally
            UpdateUI()
        End Try
    End Sub

    Private Async Sub btnStopShare_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _screenShareManager IsNot Nothing Then
                _screenShareManager.StopScreenShare()

                _isSharingScreen = False

                ' NOTIFICA IL SERVER CHE LO SCREENSHARE È TERMINATO
                If IsConnected Then
                    Await _connection.InvokeAsync("StopScreenShare", txtRoomId.Text)
                End If

                ' Ripristina le colonne laterali
                RestoreSideColumns()

                ' Pulisci anche localmente (nel caso)
                screenShareImage.Source = Nothing
                txtScreenSharePlaceholder.Visibility = Visibility.Visible

                Await UpdateScreenSharingStatus(False)

                txtStatus.Text = "Condivisione fermata"
                UpdateUI()

                Debug.Print("Screen share fermato")
                'MessageBox.Show("Condivisione schermo fermata", "Info",        MessageBoxButton.OK, MessageBoxImage.Information)
            End If
        Catch ex As Exception
            MessageBox.Show($"Errore fermata condivisione: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            UpdateUI()
        End Try
    End Sub

    'Private Async Sub SendScreenFrame(frameData As Byte(), width As Integer, height As Integer)
    '    Try
    '        If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
    '            Debug.Print($"Invio screen frame: {width}x{height}, {frameData.Length} bytes")

    '            ' Invia a tutti gli altri nella stanza
    '            Await _connection.InvokeAsync("SendScreenFrameToAll", txtRoomId.Text, frameData, width, height)
    '            Debug.Print("Screen frame inviato con successo")
    '        Else
    '            Debug.Print("Impossibile inviare: connessione non attiva")
    '        End If
    '    Catch ex As Exception
    '        Debug.Print($"Errore invio screen frame: {ex.Message}")
    '    End Try
    'End Sub

    Private Async Sub SendScreenFrame(frameData As Byte(), width As Integer, height As Integer)
        Try
            ' IMPORTANTE: Accedi a _connection e _connection.State in modo thread-safe
            Dim canSend As Boolean = False
            Dim connectionState As String = ""

            ' Usa Dispatcher per accedere alla connessione
            Dispatcher.Invoke(Sub()
                                  If _connection IsNot Nothing Then
                                      connectionState = _connection.State.ToString()
                                      canSend = (_connection.State = HubConnectionState.Connected)
                                      Debug.Print($"Stato connessione: {connectionState}")
                                  Else
                                      Debug.Print("_connection è null")
                                  End If
                              End Sub)

            If canSend Then
                Debug.Print($"Invio screen frame: {width}x{height}, {frameData.Length} bytes")

                ' Ottieni roomId in modo thread-safe
                Dim currentRoomId As String = ""
                Dispatcher.Invoke(Sub()
                                      currentRoomId = txtRoomId.Text
                                  End Sub)

                ' La chiamata a InvokeAsync non richiede Dispatcher
                Await _connection.InvokeAsync("SendScreenFrameToAll", currentRoomId, frameData, width, height)

                Debug.Print("Screen frame inviato con successo")
            Else
                Debug.Print($"Impossibile inviare: connessione non attiva ({connectionState})")
            End If

        Catch ex As Exception
            Debug.Print($"Errore invio screen frame: {ex.Message}")
            Debug.Print($"StackTrace: {ex.StackTrace}")
        End Try
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

                                       ' Controlli audio
                                       btnStartAudio.IsEnabled = Not _isAudioStarted AndAlso IsConnected
                                       btnStopAudio.IsEnabled = _isAudioStarted AndAlso IsConnected

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

                                       ' Controlli screen share
                                       btnShareScreen.IsEnabled = IsConnected AndAlso Not _isSharingScreen
                                       btnStopShare.IsEnabled = IsConnected AndAlso _isSharingScreen

                                       ' Aggiorna i binding delle proprietà
                                       OnPropertyChanged(NameOf(LocalVideoSource))
                                       OnPropertyChanged(NameOf(localVideoImage))
                                       OnPropertyChanged(NameOf(remoteVideoImage))
                                       OnPropertyChanged(NameOf(RemoteVideoSource))
                                       OnPropertyChanged(NameOf(screenShareImage))

                                   Catch ex As Exception
                                       Debug.Print($"Error in UpdateUI: {ex.Message}")
                                   End Try
                               End Sub)
    End Sub

    Private Function ConvertJpegToBitmap(jpegData As Byte(), width As Integer, height As Integer) As BitmapImage
        Try
            If jpegData Is Nothing OrElse jpegData.Length = 0 Then Return Nothing

            Dim bitmapImage = New BitmapImage()
            Using stream = New MemoryStream(jpegData)
                bitmapImage.BeginInit()
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                bitmapImage.StreamSource = stream
                bitmapImage.EndInit()
                bitmapImage.Freeze()
            End Using

            Return bitmapImage

        Catch ex As Exception
            Debug.Print($"Error converting JPEG: {ex.Message}")
            Return Nothing
        End Try
    End Function

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
            _isVideoStarted = False

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

    ' Aggiorna stato video quando avvii/fermi la webcam
    Private Async Function UpdateVideoStatus(hasVideo As Boolean) As Task
        Try
            If IsConnected Then
                ' Ottieni roomId in modo thread-safe
                Dim currentRoomId As String = ""
                Dispatcher.Invoke(Sub() currentRoomId = txtRoomId.Text)
                Await _connection.InvokeAsync("UpdateParticipantStatus", currentRoomId, hasVideo, _isAudioStarted)
            End If
        Catch ex As Exception
            Debug.Print($"Errore aggiornamento stato video: {ex.Message}")
        End Try
    End Function

    ' Aggiorna stato audio quando avvii/fermi il microfono
    Private Async Function UpdateAudioStatus(hasAudio As Boolean) As Task
        Try
            If IsConnected Then
                Dim currentRoomId As String = ""
                Dispatcher.Invoke(Sub()
                                      currentRoomId = txtRoomId.Text
                                  End Sub)
                Await _connection.InvokeAsync("UpdateParticipantStatus", currentRoomId, _isVideoStarted, hasAudio)
            End If
        Catch ex As Exception
            Debug.Print($"Errore aggiornamento stato audio: {ex.Message}")
        End Try
    End Function

    ' Aggiorna stato screen share
    Private Async Function UpdateScreenSharingStatus(isSharing As Boolean) As Task
        Try
            If IsConnected Then
                Await _connection.InvokeAsync("UpdateScreenSharingStatus", txtRoomId.Text, isSharing)
            End If
        Catch ex As Exception
            Debug.Print($"Errore aggiornamento stato screen share: {ex.Message}")
        End Try
    End Function

    ' ========== GESTIONE CHAT ==========

    Private Async Sub btnSendChat_Click(sender As Object, e As RoutedEventArgs)
        Await SendChatMessage()
    End Sub

    Private Async Sub txtChatMessage_KeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Enter Then
            Await SendChatMessage()
            e.Handled = True
        End If
    End Sub

    Private Async Function SendChatMessage() As Task
        Dim message = txtChatMessage.Text.Trim()
        If String.IsNullOrEmpty(message) Then Return

        Try
            If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
                Await _connection.InvokeAsync("SendChatMessage", txtRoomId.Text, txtUserName.Text, message)
                txtChatMessage.Clear()

                ' Aggiungi anche localmente (per vedere subito il messaggio)
                AddLocalChatMessage(txtUserName.Text, message)
            End If
        Catch ex As Exception
            Debug.Print($"Error sending chat: {ex.Message}")
        End Try
    End Function

    Private Sub AddLocalChatMessage(sender As String, message As String)
        Dispatcher.Invoke(Sub()
                              Dim chatMsg As New ChatMessage With {
                                  .Sender = sender,
                                  .Message = message,
                                  .Timestamp = DateTime.Now
                              }
                              _chatMessages.Add(chatMsg)

                              ' Scroll in fondo
                              If lstChat.Items.Count > 0 Then
                                  lstChat.ScrollIntoView(lstChat.Items(lstChat.Items.Count - 1))
                              End If
                          End Sub)
    End Sub

    Private Sub CollapseSideColumns(collapse As Boolean)
        ' Ottieni il Grid principale che contiene le colonne
        Dim mainGrid = TryCast(FindName("MainContentGrid"), Grid)

        If mainGrid IsNot Nothing AndAlso mainGrid.ColumnDefinitions.Count >= 3 Then
            If collapse Then
                ' Collassa colonne sinistra e destra
                mainGrid.ColumnDefinitions(0).Width = New GridLength(0)
                mainGrid.ColumnDefinitions(2).Width = New GridLength(0)
                ' La colonna centrale prende tutto
                mainGrid.ColumnDefinitions(1).Width = New GridLength(1, GridUnitType.Star)
                Debug.Print("Colonne laterali collassate")
            Else
                ' Ripristina le dimensioni originali
                mainGrid.ColumnDefinitions(0).Width = New GridLength(250)
                mainGrid.ColumnDefinitions(1).Width = New GridLength(5, GridUnitType.Star)
                mainGrid.ColumnDefinitions(2).Width = New GridLength(3, GridUnitType.Star)
                Debug.Print("Colonne laterali ripristinate")
            End If
        End If
    End Sub

    Private Sub RestoreSideColumns()
        CollapseSideColumns(False)
    End Sub

    Private Sub MinimizeWindow()
        ' Minimizza la finestra
        Me.WindowState = WindowState.Minimized
        Debug.Print("Finestra minimizzata")
    End Sub

    Private Sub HighlightScreenShare(enabled As Boolean)
        Dim border = TryCast(screenShareImage.Parent, Border)
        If border IsNot Nothing Then
            If enabled Then
                border.BorderBrush = System.Windows.Media.Brushes.Red
                border.BorderThickness = New Thickness(3)
            Else
                border.BorderBrush = System.Windows.Media.Brushes.Gray
                border.BorderThickness = New Thickness(1)
            End If
        End If
    End Sub

    'Private Sub InitializeRemoteCursor()
    '    ' Crea un elemento visivo per il cursore remoto
    '    _remoteCursorElement = New System.Windows.Shapes.Ellipse With {
    '        .Width = 20,
    '        .Height = 20,
    '        .Fill = New SolidColorBrush(Colors.Red) With {.Opacity = 0.7},
    '        .Stroke = New SolidColorBrush(Colors.White),
    '        .StrokeThickness = 2,
    '        .Visibility = Visibility.Collapsed
    '    }

    '    ' Aggiungi al canvas
    '    cursorCanvas.Children.Add(_remoteCursorElement)

    '    ' Posiziona inizialmente
    '    Canvas.SetLeft(_remoteCursorElement, 0)
    '    Canvas.SetTop(_remoteCursorElement, 0)

    '    Debug.Print("Remote cursor initialized")
    'End Sub

    'Private Sub InitializeRemoteCursor()
    '    ' Crea un cursore a forma di freccia
    '    Dim cursorPath As New System.Windows.Shapes.Path()

    '    ' Definisci la geometria di una freccia
    '    Dim geometry As New StreamGeometry()
    '    Using context = geometry.Open()
    '        context.BeginFigure(New Point(0, 0), True, True)
    '        context.LineTo(New Point(10, 16), True, False)
    '        context.LineTo(New Point(6, 16), True, False)
    '        context.LineTo(New Point(6, 24), True, False)
    '        context.LineTo(New Point(14, 24), True, False)
    '        context.LineTo(New Point(14, 16), True, False)
    '        context.LineTo(New Point(20, 16), True, False)
    '        context.LineTo(New Point(10, 0), True, False)
    '    End Using

    '    cursorPath.Data = geometry
    '    cursorPath.Fill = New SolidColorBrush(Colors.Red) With {.Opacity = 0.8}
    '    cursorPath.Stroke = New SolidColorBrush(Colors.White)
    '    cursorPath.StrokeThickness = 1

    '    cursorCanvas.Children.Add(cursorPath)
    '    _remoteCursorElement = cursorPath ' Salva come Object
    'End Sub
    Private Sub InitializeRemoteCursor()
        ' Pulisci canvas
        cursorCanvas.Children.Clear()

        ' Crea un'immagine per il cursore
        Dim cursorImage As New System.Windows.Controls.Image()

        ' Carica l'immagine dalle risorse
        Dim bitmapImage As New BitmapImage()
        bitmapImage.BeginInit()
        bitmapImage.UriSource = New Uri("pack://application:,,,/cursor.png")
        bitmapImage.EndInit()

        cursorImage.Source = bitmapImage
        cursorImage.Width = 32
        cursorImage.Height = 32
        cursorImage.Stretch = Stretch.Uniform
        cursorImage.Tag = "RemoteCursor"

        ' nascosto all'avvio
        cursorImage.Visibility = Visibility.Collapsed

        cursorCanvas.Children.Add(cursorImage)
        _remoteCursorElement = cursorImage

        Debug.Print($"🎯 Cursore da immagine inizializzato")
    End Sub

    'Private Sub InitializeRemoteCursorSimple()
    '    ' Combina una freccia bianca con bordo nero
    '    Dim cursorPath As New System.Windows.Shapes.Path()

    '    ' Geometria semplice ma efficace
    '    Dim geometry As New StreamGeometry()
    '    Using context = geometry.Open()
    '        context.BeginFigure(New Point(0, 0), True, True)
    '        context.LineTo(New Point(18, 22), True, False)
    '        context.LineTo(New Point(14, 22), True, False)
    '        context.LineTo(New Point(14, 28), True, False)
    '        context.LineTo(New Point(22, 28), True, False)
    '        context.LineTo(New Point(22, 22), True, False)
    '        context.LineTo(New Point(18, 22), True, False)
    '    End Using

    '    cursorPath.Data = geometry
    '    cursorPath.Fill = New SolidColorBrush(Colors.White)
    '    cursorPath.Stroke = New SolidColorBrush(Colors.Black)
    '    cursorPath.StrokeThickness = 1.5

    '    cursorCanvas.Children.Add(cursorPath)
    '    _remoteCursorElement = cursorPath

    '    Debug.Print($"🎯 Cursore semplice inizializzato")
    'End Sub

    'Private Sub InitializeRemoteCursorRealistic()
    '    ' Pulisci canvas
    '    cursorCanvas.Children.Clear()

    '    ' === Crea la FRECCIA principale (stile Windows preciso) ===
    '    Dim cursorPath As New System.Windows.Shapes.Path()

    '    ' Geometria della freccia standard di Windows
    '    Dim geometry As New StreamGeometry()
    '    Using context = geometry.Open()
    '        ' Punta della freccia
    '        context.BeginFigure(New Point(0, 0), True, True)

    '        ' Lato destro inclinato
    '        context.LineTo(New Point(19, 25), True, False)

    '        ' Base destra
    '        context.LineTo(New Point(15, 25), True, False)
    '        context.LineTo(New Point(15, 31), True, False)
    '        context.LineTo(New Point(23, 31), True, False)
    '        context.LineTo(New Point(23, 25), True, False)

    '        ' Base sinistra (ritorno)
    '        context.LineTo(New Point(19, 25), True, False)
    '    End Using

    '    geometry.Freeze()
    '    cursorPath.Data = geometry

    '    ' Riempimento con gradiente metallico
    '    Dim gradientStopCollection As New GradientStopCollection()
    '    gradientStopCollection.Add(New GradientStop(Color.FromRgb(255, 255, 255), 0.0))
    '    gradientStopCollection.Add(New GradientStop(Color.FromRgb(240, 240, 240), 0.3))
    '    gradientStopCollection.Add(New GradientStop(Color.FromRgb(220, 220, 220), 0.7))
    '    gradientStopCollection.Add(New GradientStop(Color.FromRgb(200, 200, 200), 1.0))

    '    Dim gradientBrush As New LinearGradientBrush(gradientStopCollection, New Point(0, 0), New Point(0, 1))

    '    cursorPath.Fill = gradientBrush
    '    cursorPath.Stroke = New SolidColorBrush(Colors.Black)
    '    cursorPath.StrokeThickness = 1
    '    cursorPath.Tag = "RemoteCursor"

    '    ' === UNICO EFFETTO OMBRA (integrato nella freccia) ===
    '    Dim shadowEffect As New System.Windows.Media.Effects.DropShadowEffect()
    '    shadowEffect.Color = Colors.Black
    '    shadowEffect.Direction = 315
    '    shadowEffect.ShadowDepth = 3
    '    shadowEffect.Opacity = 0.4
    '    shadowEffect.BlurRadius = 3

    '    cursorPath.Effect = shadowEffect

    '    ' === Punto di precisione (opzionale, piccolo) ===
    '    Dim dot As New System.Windows.Shapes.Ellipse With {
    '    .Width = 3,
    '    .Height = 3,
    '    .Fill = New SolidColorBrush(Colors.Red),
    '    .Stroke = Nothing,  ' Senza bordo per non creare linee extra
    '    .Tag = "CursorDot"
    '}

    '    ' Aggiungi al Canvas
    '    cursorCanvas.Children.Add(cursorPath)
    '    cursorCanvas.Children.Add(dot)

    '    ' Salva riferimenti
    '    _remoteCursorElement = cursorPath
    '    _cursorDot = dot
    '    _cursorShadow = Nothing  ' Non usiamo più l'ombra separata

    '    Debug.Print($"🎯 Cursore realistico con ombra singola inizializzato")
    'End Sub

    'Private Sub InitializeRemoteCursorSmooth()
    '    Dim cursorPath As New System.Windows.Shapes.Path()

    '    ' Usa una geometria più fluida con curve di Bezier
    '    Dim geometry As New StreamGeometry()
    '    Using context = geometry.Open()
    '        ' Inizia dalla punta
    '        context.BeginFigure(New Point(0, 0), True, True)

    '        ' Curva destra (invece di linea retta)
    '        context.BezierTo(
    '        New Point(10, 10),
    '        New Point(15, 18),
    '        New Point(18, 23),
    '        True, False)

    '        ' Base destra
    '        context.LineTo(New Point(14, 23), True, False)
    '        context.LineTo(New Point(14, 28), True, False)
    '        context.LineTo(New Point(22, 28), True, False)
    '        context.LineTo(New Point(22, 23), True, False)

    '        ' Curva sinistra (torna alla punta)
    '        context.BezierTo(
    '        New Point(15, 18),
    '        New Point(10, 10),
    '        New Point(0, 0),
    '        True, False)
    '    End Using

    '    geometry.Freeze()
    '    cursorPath.Data = geometry

    '    ' Imposta proprietà per un aspetto più professionale
    '    cursorPath.Fill = New SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
    '    cursorPath.Stroke = New SolidColorBrush(Colors.Black)
    '    cursorPath.StrokeThickness = 1
    '    cursorPath.SnapsToDevicePixels = True
    '    cursorPath.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased)

    '    ' Aggiungi un piccolo punto al centro per maggiore precisione
    '    Dim dot As New System.Windows.Shapes.Ellipse With {
    '    .Width = 3,
    '    .Height = 3,
    '    .Fill = New SolidColorBrush(Colors.Red),
    '    .Stroke = New SolidColorBrush(Colors.White),
    '    .StrokeThickness = 1,
    '    .Tag = "CursorDot"
    '}

    '    cursorCanvas.Children.Add(cursorPath)
    '    cursorCanvas.Children.Add(dot)

    '    _remoteCursorElement = cursorPath
    '    _cursorDot = dot

    '    Debug.Print($"🎯 Cursore smooth inizializzato")
    'End Sub

    Private Sub btnTestCursor_Click(sender As Object, e As RoutedEventArgs)
        ' Forza il cursore al centro dell'immagine
        Dim centerX = screenShareImage.ActualWidth / 2
        Dim centerY = screenShareImage.ActualHeight / 2

        Canvas.SetLeft(_remoteCursorElement, centerX - 15)
        Canvas.SetTop(_remoteCursorElement, centerY - 15)
        _remoteCursorElement.Visibility = Visibility.Visible

        Debug.Print($"🧪 Test: Cursore forzato a ({centerX}, {centerY})")
    End Sub

    'Private Sub UpdateRemoteCursorPosition(x As Integer, y As Integer)
    '    Try
    '        ' Ottieni le dimensioni effettive dell'immagine
    '        If screenShareImage.Source Is Nothing Then Return

    '        ' Calcola le coordinate relative all'immagine
    '        Dim imageWidth = screenShareImage.ActualWidth
    '        Dim imageHeight = screenShareImage.ActualHeight

    '        If imageWidth = 0 OrElse imageHeight = 0 Then Return

    '        ' Ottieni le dimensioni dello schermo originale
    '        Dim screen = System.Windows.Forms.Screen.PrimaryScreen
    '        Dim screenWidth = screen.Bounds.Width
    '        Dim screenHeight = screen.Bounds.Height

    '        ' Calcola le coordinate proporzionali
    '        Dim relX = x / screenWidth
    '        Dim relY = y / screenHeight

    '        ' Converti in coordinate del canvas
    '        Dim canvasX = relX * imageWidth
    '        Dim canvasY = relY * imageHeight

    '        ' Applica la posizione
    '        Canvas.SetLeft(_remoteCursorElement, canvasX - 10) ' Centra il cursore
    '        Canvas.SetTop(_remoteCursorElement, canvasY - 10)

    '        ' Mostra il cursore
    '        If _remoteCursorElement.Visibility = Visibility.Collapsed Then
    '            _remoteCursorElement.Visibility = Visibility.Visible
    '        End If

    '    Catch ex As Exception
    '        Debug.Print($"Error updating cursor position: {ex.Message}")
    '    End Try
    'End Sub

    'Private Sub UpdateRemoteCursorPosition(x As Integer, y As Integer)
    '    Try
    '        ' Verifica che l'immagine dello screen share sia visibile
    '        If screenShareImage.Source Is Nothing OrElse screenShareImage.ActualWidth = 0 Then
    '            Debug.Print("❌ UpdateRemoteCursorPosition: Nessuna immagine visibile")
    '            HideRemoteCursor()
    '            Return
    '        End If

    '        If _remoteCursorElement Is Nothing Then
    '            Debug.Print("❌ UpdateRemoteCursorPosition: _remoteCursorElement null")
    '            Return
    '        End If

    '        ' Ottieni le dimensioni dell'immagine visualizzata
    '        Dim imageWidth = screenShareImage.ActualWidth
    '        Dim imageHeight = screenShareImage.ActualHeight

    '        ' Ottieni le dimensioni dello schermo originale del mittente
    '        Dim screenWidth = _remoteScreenWidth
    '        Dim screenHeight = _remoteScreenHeight

    '        ' Validazione input
    '        If x < 0 OrElse x > screenWidth OrElse y < 0 OrElse y > screenHeight Then
    '            Debug.Print($"⚠️ Coordinate fuori schermo: ({x}, {y}) max {screenWidth}x{screenHeight}")
    '            ' Normalizza
    '            x = Math.Max(0, Math.Min(x, screenWidth))
    '            y = Math.Max(0, Math.Min(y, screenHeight))
    '        End If

    '        ' Calcola le proporzioni
    '        Dim imageAspect = imageWidth / imageHeight
    '        Dim screenAspect = screenWidth / screenHeight

    '        Dim renderX As Double
    '        Dim renderY As Double
    '        Dim scale As Double

    '        If imageAspect > screenAspect Then
    '            ' Letterbox (bande sopra/sotto)
    '            scale = imageWidth / screenWidth
    '            renderX = 0
    '            renderY = (imageHeight - (screenHeight * scale)) / 2
    '            Debug.Print($"📐 Caso letterbox: scale={scale:F3}, offsetY={renderY:F2}")
    '        Else
    '            ' Pillarbox (bande ai lati)
    '            scale = imageHeight / screenHeight
    '            renderX = (imageWidth - (screenWidth * scale)) / 2
    '            renderY = 0
    '            Debug.Print($"📐 Caso pillarbox: scale={scale:F3}, offsetX={renderX:F2}")
    '        End If

    '        ' Calcola posizione finale
    '        Dim cursorX = renderX + (x * scale)
    '        Dim cursorY = renderY + (y * scale)

    '        Debug.Print($"📍 Calcolato: ({cursorX:F2}, {cursorY:F2})")

    '        ' Validazione coordinate finali
    '        If cursorX < 0 OrElse cursorX > imageWidth OrElse cursorY < 0 OrElse cursorY > imageHeight Then
    '            Debug.Print($"⚠️ Coordinate finali fuori immagine: ({cursorX:F2}, {cursorY:F2}) max {imageWidth:F2}x{imageHeight:F2}")
    '            ' Clamping
    '            cursorX = Math.Max(0, Math.Min(cursorX, imageWidth))
    '            cursorY = Math.Max(0, Math.Min(cursorY, imageHeight))
    '        End If

    '        ' Posiziona il cursore (la punta della freccia è in alto a sinistra)
    '        ' La freccia è larga 20px, alta 24px. La punta è in alto a sinistra (0,0)
    '        Canvas.SetLeft(_remoteCursorElement, cursorX)
    '        Canvas.SetTop(_remoteCursorElement, cursorY - 24) ' Sottrai l'altezza per avere la punta al punto di click

    '        Debug.Print($"🎯 Posizionato a: Left={Canvas.GetLeft(_remoteCursorElement):F2}, Top={Canvas.GetTop(_remoteCursorElement):F2}")

    '        ' Mostra cursore
    '        If _remoteCursorElement.Visibility = Visibility.Collapsed Then
    '            _remoteCursorElement.Visibility = Visibility.Visible
    '            Debug.Print("👆 Cursore reso visibile")
    '        End If

    '        ' Porta in primo piano
    '        Canvas.SetZIndex(_remoteCursorElement, 1000)

    '    Catch ex As Exception
    '        Debug.Print($"Error updating cursor position: {ex.Message}")
    '    End Try
    'End Sub

    Private Sub UpdateRemoteCursorPosition(x As Integer, y As Integer)
        Try
            Debug.Print("=== UPDATE CURSOR POSITION ===")
            Debug.Print($"🖱️ INPUT: ({x}, {y})")

            ' Verifica che l'immagine sia visibile
            If screenShareImage.Source Is Nothing OrElse screenShareImage.ActualWidth = 0 Then
                Debug.Print("❌ USCITA: Nessuna immagine")
                Return
            End If

            ' Ottieni dimensioni del Canvas (contenitore)
            Dim canvasWidth = cursorCanvas.ActualWidth
            Dim canvasHeight = cursorCanvas.ActualHeight

            ' Ottieni dimensioni dello schermo originale
            Dim screenWidth = _remoteScreenWidth
            Dim screenHeight = _remoteScreenHeight

            Debug.Print($"📏 Canvas: {canvasWidth:F2} x {canvasHeight:F2}")
            Debug.Print($"📏 Screen originale: {screenWidth} x {screenHeight}")
            Debug.Print($"📏 Image Actual: {screenShareImage.ActualWidth:F2} x {screenShareImage.ActualHeight:F2}")

            ' Calcola le proporzioni
            Dim canvasAspect = canvasWidth / canvasHeight
            Dim screenAspect = screenWidth / screenHeight

            Debug.Print($"📐 Canvas aspect: {canvasAspect:F4}")
            Debug.Print($"📐 Screen aspect: {screenAspect:F4}")

            Dim renderWidth As Double
            Dim renderHeight As Double
            Dim offsetX As Double
            Dim offsetY As Double

            If canvasAspect > screenAspect Then
                ' Il Canvas è più largo delle proporzioni dello schermo
                ' => l'immagine avrà bande laterali (pillarbox)
                renderHeight = canvasHeight
                renderWidth = screenWidth * (canvasHeight / screenHeight)
                offsetX = (canvasWidth - renderWidth) / 2
                offsetY = 0
                Debug.Print($"📐 PILLARBOX: render={renderWidth:F2}x{renderHeight:F2}, offsetX={offsetX:F2}")
            Else
                ' Il Canvas è più alto delle proporzioni dello schermo
                ' => l'immagine avrà bande sopra/sotto (letterbox)
                renderWidth = canvasWidth
                renderHeight = screenHeight * (canvasWidth / screenWidth)
                offsetX = 0
                offsetY = (canvasHeight - renderHeight) / 2
                Debug.Print($"📐 LETTERBOX: render={renderWidth:F2}x{renderHeight:F2}, offsetY={offsetY:F2}")
            End If

            ' Calcola posizione del cursore nel Canvas
            Dim relX = x / screenWidth
            Dim relY = y / screenHeight

            Dim cursorX = offsetX + (relX * renderWidth)
            Dim cursorY = offsetY + (relY * renderHeight)

            ' blocco il cursore allì'interno dello schermo condiviso
            If cursorX < offsetX Then cursorX = offsetX
            If cursorX > offsetX + renderWidth Then cursorX = offsetX + renderWidth
            If cursorY < offsetY Then cursorY = offsetY
            If cursorY > offsetY + renderHeight Then cursorY = offsetY + renderHeight

            Debug.Print($"📍 Posizione Canvas: ({cursorX:F2}, {cursorY:F2})")

            ' Posiziona l'immagine del cursore
            If _remoteCursorElement IsNot Nothing Then
                ' Centra l'hotspot (assumendo che la punta sia in alto a sinistra)
                Canvas.SetLeft(_remoteCursorElement, cursorX - 13)
                Canvas.SetTop(_remoteCursorElement, cursorY)
                _remoteCursorElement.Visibility = Visibility.Visible
            End If

            Debug.Print("=== FINE UPDATE ===")

        Catch ex As Exception
            Debug.Print($"❌ ERRORE: {ex.Message}")
        End Try
    End Sub

    Private Sub DebugImageDimensions()
        Dispatcher.Invoke(Sub()
                              Debug.Print("=== DEBUG DIMENSIONI IMMAGINE ===")
                              Debug.Print($"screenShareImage ActualWidth: {screenShareImage.ActualWidth}")
                              Debug.Print($"screenShareImage ActualHeight: {screenShareImage.ActualHeight}")
                              Debug.Print($"screenShareImage Source: {screenShareImage.Source IsNot Nothing}")

                              If screenShareImage.Source IsNot Nothing Then
                                  Dim bitmap = TryCast(screenShareImage.Source, BitmapSource)
                                  If bitmap IsNot Nothing Then
                                      Debug.Print($"Bitmap PixelWidth: {bitmap.PixelWidth}")
                                      Debug.Print($"Bitmap PixelHeight: {bitmap.PixelHeight}")
                                      Debug.Print($"Bitmap DPI: {bitmap.DpiX}x{bitmap.DpiY}")
                                  End If
                              End If

                              Debug.Print($"cursorCanvas ActualWidth: {cursorCanvas.ActualWidth}")
                              Debug.Print($"cursorCanvas ActualHeight: {cursorCanvas.ActualHeight}")
                              Debug.Print("================================")
                          End Sub)
    End Sub

    Private Sub HideRemoteCursor()
        If _remoteCursorElement IsNot Nothing Then
            _remoteCursorElement.Visibility = Visibility.Collapsed
        End If
    End Sub

    'Private Async Sub SendCursorPosition(x As Integer, y As Integer)
    '    Try
    '        If _connection IsNot Nothing AndAlso _connection.State = HubConnectionState.Connected Then
    '            Await _connection.InvokeAsync("SendCursorPosition", txtRoomId.Text, x, y)
    '        End If
    '    Catch ex As Exception
    '        Debug.Print($"Error sending cursor position: {ex.Message}")
    '    End Try
    'End Sub
    Private Async Sub SendCursorPosition(x As Integer, y As Integer)
        Try
            ' Variabili thread-safe
            Dim canSend As Boolean = False
            Dim currentRoomId As String = ""

            ' Accedi agli oggetti UI in modo thread-safe
            Dispatcher.Invoke(Sub()
                                  canSend = (IsConnected AndAlso
                                            _connection IsNot Nothing AndAlso
                                            _connection.State = HubConnectionState.Connected)
                                  currentRoomId = txtRoomId.Text
                              End Sub)

            If canSend Then
                ' La chiamata asincrona può essere fatta fuori dal Dispatcher
                Await _connection.InvokeAsync("SendCursorPosition", currentRoomId, x, y)
                Debug.Print($"Cursore inviato: ({x}, {y})") ' Commentato per non intasare i log
            End If

        Catch ex As Exception
            Debug.Print($"Errore invio posizione cursore: {ex.Message}")
        End Try
    End Sub

    Private Async Sub btnTestDirect_Click(sender As Object, e As RoutedEventArgs)
        Try
            Debug.Print("🧪 TEST: Invio diretto al server")

            Dim testX = 100
            Dim testY = 200

            Await _connection.InvokeAsync("SendCursorPosition", txtRoomId.Text, testX, testY)

            Debug.Print($"🧪 TEST: Inviato ({testX}, {testY})")

        Catch ex As Exception
            Debug.Print($"🧪 TEST Errore: {ex.Message}")
        End Try
    End Sub

End Class

' Classe per rappresentare un partecipante
Public Class Participant
    Implements INotifyPropertyChanged

    Private _connectionId As String
    Private _userName As String
    Private _videoSource As ImageSource
    Private _isScreenSharing As Boolean
    Private _hasVideo As Boolean
    Private _hasAudio As Boolean

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Public Property ConnectionId As String
        Get
            Return _connectionId
        End Get
        Set(value As String)
            _connectionId = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property UserName As String
        Get
            Return _userName
        End Get
        Set(value As String)
            _userName = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property VideoSource As ImageSource
        Get
            Return _videoSource
        End Get
        Set(value As ImageSource)
            _videoSource = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property IsScreenSharing As Boolean
        Get
            Return _isScreenSharing
        End Get
        Set(value As Boolean)
            _isScreenSharing = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property HasVideo As Boolean
        Get
            Return _hasVideo
        End Get
        Set(value As Boolean)
            _hasVideo = value
            OnPropertyChanged()
        End Set
    End Property

    Public Property HasAudio As Boolean
        Get
            Return _hasAudio
        End Get
        Set(value As Boolean)
            _hasAudio = value
            OnPropertyChanged()
        End Set
    End Property

    Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
    End Sub


End Class

' Classe per i messaggi chat
Public Class ChatMessage
    Public Property Sender As String
    Public Property Message As String
    Public Property Timestamp As String
End Class


Public Class UserInfo
    Public Property ConnectionId As String
    Public Property UserName As String
    Public Property HasVideo As Boolean
    Public Property HasAudio As Boolean
    Public Property IsScreenSharing As Boolean
End Class
