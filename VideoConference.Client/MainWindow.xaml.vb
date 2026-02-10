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
            ' Per ora usiamo solo video locale
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

            Console.WriteLine("Video Manager initialized with Emgu.CV")

        Catch ex As Exception
            MessageBox.Show($"Errore nell'inizializzazione video: {ex.Message}",
                          "Errore Inizializzazione", MessageBoxButton.OK, MessageBoxImage.Error)
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
                MessageBox.Show("Webcam attivata con successo! Il tuo video è visibile nel pannello 'Video Locale'.",
                              "Successo", MessageBoxButton.OK, MessageBoxImage.Information)

                ' Dopo 2 secondi, mostra lo stato
                Task.Delay(2000).ContinueWith(
                    Sub(t)
                        Dispatcher.Invoke(Sub()
                                              If _isVideoStarted Then
                                                  txtStatus.Text = "Connesso - Video attivo"
                                              End If
                                          End Sub)
                    End Sub)
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
        Try
            If _videoManager IsNot Nothing Then
                _videoManager.StopVideoCapture()
                MessageBox.Show("Webcam disattivata", "Info",
                              MessageBoxButton.OK, MessageBoxImage.Information)
            Else
                MessageBox.Show("Video Manager non inizializzato", "Errore",
                              MessageBoxButton.OK, MessageBoxImage.Error)
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

                                       ' Aggiorna i binding delle proprietà
                                       OnPropertyChanged(NameOf(LocalVideoSource))
                                       OnPropertyChanged(NameOf(RemoteVideoSource))

                                   Catch ex As Exception
                                       Console.WriteLine($"Error in UpdateUI: {ex.Message}")
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
            Console.WriteLine($"Error during cleanup: {ex.Message}")
        Finally
            MyBase.OnClosing(e)
        End Try
    End Sub
End Class