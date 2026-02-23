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
    Private _isSendingVideo As Boolean = False
    Private _frameSendTimer As Timers.Timer
    Private _isAudioStarted As Boolean = False
    Private _isMicMuted As Boolean = False
    Private _screenShareManager As ScreenShareManager
    Private _isSharingScreen As Boolean = False

    Private _participants As New ObservableCollection(Of Participant)
    Private _chatMessages As New ObservableCollection(Of ChatMessage)
    Private _remoteVideoSources As New Dictionary(Of String, WriteableBitmap)

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
        AddHandler btnTestAudio.Click, AddressOf btnTestAudio_Click

        ' Handler per screen share
        AddHandler btnShareScreen.Click, AddressOf btnShareScreen_Click
        AddHandler btnStopShare.Click, AddressOf btnStopShare_Click

        ' Handler per i controlli chat
        AddHandler btnSendChat.Click, AddressOf btnSendChat_Click
        AddHandler txtChatMessage.KeyDown, AddressOf txtChatMessage_KeyDown
        AddHandler cmbScreenStretch.SelectionChanged, AddressOf cmbScreenStretch_SelectionChanged

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

    'Private Sub InitializeScreenShare()
    '    Try
    '        _screenShareManager = New ScreenShareManager()

    '        AddHandler _screenShareManager.OnScreenError,
    '        Sub(errorMessage)
    '            Dispatcher.Invoke(Sub()
    '                                  MessageBox.Show($"Errore condivisione schermo: {errorMessage}",
    '                              "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
    '                              End Sub)
    '        End Sub

    '        AddHandler _screenShareManager.OnScreenShareStarted,
    '        Sub()
    '            Dispatcher.Invoke(Sub()
    '                                  _isSharingScreen = True
    '                                  txtStatus.Text = "Condivisione schermo attiva"
    '                                  UpdateUI()
    '                              End Sub)
    '        End Sub

    '        AddHandler _screenShareManager.OnScreenShareStopped,
    '        Sub()
    '            Dispatcher.Invoke(Sub()
    '                                  _isSharingScreen = False
    '                                  txtStatus.Text = "Condivisione schermo fermata"
    '                                  UpdateUI()
    '                              End Sub)
    '        End Sub

    '        AddHandler _screenShareManager.OnScreenFrameReady,
    '        Sub(frameData As Byte(), width As Integer, height As Integer)
    '            If IsConnected AndAlso Not String.IsNullOrEmpty(_remoteConnectionId) Then
    '                SendScreenFrame(frameData, width, height)
    '            End If
    '        End Sub

    '        ' Aggiungi preview alla UI (opzionale)
    '        Dim screenPreviewBinding As New System.Windows.Data.Binding()
    '        screenPreviewBinding.Source = _screenShareManager
    '        screenPreviewBinding.Path = New PropertyPath("ScreenPreview")
    '        localVideoImage.SetBinding(Image.SourceProperty, screenPreviewBinding)

    '        Debug.Print("ScreenShareManager initialized")

    '    Catch ex As Exception
    '        MessageBox.Show($"Errore inizializzazione screen share: {ex.Message}",
    '                  "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
    '    End Try
    'End Sub

    Private Sub InitializeScreenShare()
        Try
            _screenShareManager = New ScreenShareManager()

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

            Debug.Print("ScreenShareManager initialized")

        Catch ex As Exception
            MessageBox.Show($"Errore inizializzazione screen share: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
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

    Private Async Sub SendAudioData(audioData As Byte())
        Try
            ' Variabili per memorizzare lo stato della connessione (thread-safe)
            Dim canSend As Boolean = False
            Dim currentRoomId As String = ""

            ' Accedi agli oggetti UI in modo thread-safe
            Dispatcher.Invoke(Sub()
                                  If _connection IsNot Nothing Then
                                      canSend = (_connection.State = HubConnectionState.Connected)
                                      currentRoomId = txtRoomId.Text
                                      Debug.Print($"UI Thread - Stato connessione: {_connection.State}, Room: {currentRoomId}")
                                  Else
                                      Debug.Print("UI Thread - _connection è null")
                                  End If
                              End Sub)

            ' Ora possiamo usare currentConnection in modo sicuro
            If canSend Then
                Debug.Print($"*** MAIN: Invio audio a tutti, {audioData.Length} bytes, room: {currentRoomId}")

                ' La chiamata a InvokeAsync non richiede Dispatcher
                Await _connection.InvokeAsync("SendAudioDataToAll", currentRoomId, audioData)

                Debug.Print("*** MAIN: Audio inviato con successo")
            Else
                Debug.Print("*** MAIN: Connessione non attiva per invio audio")
            End If

        Catch ex As Exception
            Debug.Print($"*** MAIN: Errore invio audio: {ex.Message}")
            Debug.Print($"StackTrace: {ex.StackTrace}")
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
            '_connection = New HubConnectionBuilder().
            'WithUrl(txtServerUrl.Text.Trim() & "/conferencehub", connectionOptions).
            'WithAutomaticReconnect().
            'Build()
            ''ConfigureLogging(Sub(logging) logging.AddConsole()).

            _connection = New HubConnectionBuilder().
                                WithUrl(txtServerUrl.Text.Trim() & "/conferencehub", connectionOptions).
                                WithAutomaticReconnect().
                                ConfigureLogging(Sub(logging As ILoggingBuilder)
                                                     logging.AddDebug()
                                                     logging.SetMinimumLevel(LogLevel.Debug)
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

                                                          ' Converti in BitmapImage
                                                          Dim bitmapImage As New BitmapImage()
                                                          Using stream = New IO.MemoryStream(frameData)
                                                              bitmapImage.BeginInit()
                                                              bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                                                              bitmapImage.StreamSource = stream
                                                              bitmapImage.EndInit()
                                                              bitmapImage.Freeze()
                                                          End Using

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

                                  ' Se era il remote principale, pulisci
                                  If _remoteConnectionId = connectionId Then
                                      _remoteConnectionId = ""
                                      remoteVideoImage.Source = Nothing
                                  End If
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

            _connection.On(Of List(Of UserInfo))("ExistingUsers",
                            Sub(usersList)
                                Dispatcher.Invoke(Sub()
                                                      _participants.Clear()

                                                      If usersList IsNot Nothing Then
                                                          For Each user In usersList
                                                              Dim participant As New Participant With {
                                                .ConnectionId = user.ConnectionId,
                                                .UserName = If(user.UserName IsNot Nothing, user.UserName, "Utente"),
                                                .HasVideo = False,
                                                .HasAudio = False
                                            }
                                                              _participants.Add(participant)
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
                                        .Timestamp = timestamp
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

            ' Connessione al server
            Await _connection.StartAsync()

            IsConnected = True
            _localConnectionId = _connection.ConnectionId
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

    Private Sub cmbScreenStretch_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If screenShareImage Is Nothing Then Return

        Select Case cmbScreenStretch.SelectedIndex
            Case 0
                screenShareImage.Stretch = Stretch.Uniform
            Case 1
                screenShareImage.Stretch = Stretch.Fill
            Case 2
                screenShareImage.Stretch = Stretch.None
            Case 3
                screenShareImage.Stretch = Stretch.UniformToFill
        End Select
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
        ' Pulisci le liste
        _participants.Clear()
        _chatMessages.Clear()
        _remoteVideoSources.Clear()
        IsConnected = False
        txtStatus.Text = "Disconnesso"
        _localConnectionId = ""
        _remoteConnectionId = ""

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
                _isSendingVideo = True
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

                Await UpdateVideoStatus(False)

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

    Private Sub btnMuteMic_Click(sender As Object, e As RoutedEventArgs)
        _isMicMuted = Not _isMicMuted

        If _isMicMuted Then
            btnMuteMic.Content = "🎤 Microfono MUTO"
            btnMuteMic.Background = System.Windows.Media.Brushes.Red
            ' Ferma cattura audio
            If _videoManager IsNot Nothing Then
                _videoManager.StopAudioCapture()
            End If
        Else
            btnMuteMic.Content = "🎤 Microfono ATTIVO"
            btnMuteMic.Background = System.Windows.Media.Brushes.LightGreen
            ' Riavvia cattura audio
            If _videoManager IsNot Nothing Then
                _videoManager.StartAudioCapture()
            End If
        End If
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

            ' Chiedi conferma
            Dim result = MessageBox.Show(
            "Stai per condividere l'intero schermo con gli altri utenti." & vbCrLf &
            "Tutte le finestre e notifiche saranno visibili." & vbCrLf &
            "Procedere?",
            "Condivisione Schermo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning)

            If result = MessageBoxResult.Yes Then
                Dim success = _screenShareManager.StartScreenShare()

                If success Then

                    _isSharingScreen = True
                    Await UpdateScreenSharingStatus(True)

                    'MessageBox.Show("Condivisione schermo avviata!", "Successo", MessageBoxButton.OK, MessageBoxImage.Information)
                Else
                    MessageBox.Show("Condivisione schermo non avviata!", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Exclamation)
                End If
            End If

        Catch ex As Exception
            MessageBox.Show($"Errore avvio condivisione: {ex.Message}",
                      "Errore", MessageBoxButton.OK, MessageBoxImage.Error)
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

                ' Pulisci anche localmente (nel caso)
                screenShareImage.Source = Nothing
                txtScreenSharePlaceholder.Visibility = Visibility.Visible

                Await UpdateScreenSharingStatus(False)

                txtStatus.Text = "Condivisione fermata"
                UpdateUI()

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
                                       OnPropertyChanged(NameOf(RemoteVideoSource))

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

    ' Aggiorna stato video quando avvii/fermi la webcam
    Private Async Function UpdateVideoStatus(hasVideo As Boolean) As Task
        Try
            If IsConnected Then
                Await _connection.InvokeAsync("UpdateParticipantStatus", txtRoomId.Text, hasVideo, _isAudioStarted)
            End If
        Catch ex As Exception
            Debug.Print($"Errore aggiornamento stato video: {ex.Message}")
        End Try
    End Function

    ' Aggiorna stato audio quando avvii/fermi il microfono
    Private Async Function UpdateAudioStatus(hasAudio As Boolean) As Task
        Try
            If IsConnected Then
                Await _connection.InvokeAsync("UpdateParticipantStatus", txtRoomId.Text, _isVideoStarted, hasAudio)
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
    Public Property Timestamp As DateTime
End Class


Public Class UserInfo
    Public Property ConnectionId As String
    Public Property UserName As String
    Public Property HasVideo As Boolean
    Public Property HasAudio As Boolean
    Public Property IsScreenSharing As Boolean
End Class
