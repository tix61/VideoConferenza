Imports System
Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Timers
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Emgu.CV
Imports Emgu.CV.CvEnum
Imports Emgu.CV.Structure
Imports NAudio.Dsp
Imports NAudio.Wave
Imports Windows.UI.Input

Namespace VideoConference.Client
    Public Class VideoManager
        Implements INotifyPropertyChanged, IDisposable

        Private _capture As VideoCapture
        Private _isDisposed As Boolean = False
        Private _isCapturing As Boolean = False
        Private _timer As Threading.Timer

        Private _localVideoSourceProperty As ImageSource
        Private _frameCounter As Integer = 0

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Public Event OnVideoError As Action(Of String)
        Public Event OnVideoStarted As Action
        Public Event OnVideoStopped As Action
        Public Event OnLocalFrameUpdated As Action(Of WriteableBitmap)
        Public Event OnRemoteFrameUpdated As Action(Of WriteableBitmap)

        ' variabili per audio
        Private _waveIn As WaveInEvent
        Private _waveOut As WaveOutEvent
        Private _bufferedWaveProvider As BufferedWaveProvider
        Private _isAudioCapturing As Boolean = False
        Private _isAudioPlaying As Boolean = False
        Private _audioTimer As System.Threading.Timer

        ' eventi per audio
        Public Event OnAudioStarted As Action
        Public Event OnAudioStopped As Action
        Public Event OnAudioError As Action(Of String)
        Public Event OnAudioDataReady As Action(Of Byte())

        Private _isSpeaking As Boolean = False
        Private _silenceThreshold As Integer = 500
        Private _speakingTimeout As Integer = 1000 ' ms
        Private _lastSpeechTime As DateTime = DateTime.Now

        Public ReadOnly Property IsAudioCapturing As Boolean
            Get
                Return _isAudioCapturing
            End Get
        End Property

        Public ReadOnly Property IsAudioPlaying As Boolean
            Get
                Return _isAudioPlaying
            End Get
        End Property

        Public Property LocalVideoSource As ImageSource
            Get
                Return _localVideoSourceProperty
            End Get
            Set(value As ImageSource)
                _localVideoSourceProperty = value
                OnPropertyChanged()
            End Set
        End Property

        Public ReadOnly Property IsCapturing As Boolean
            Get
                Return _isCapturing
            End Get
        End Property

        ' Aggiungi queste proprietà
        Private _frameQuality As Integer = 30 ' Qualità JPEG (1-100)
        Private _maxFrameSize As Integer = 100000 ' 100KB max per frame

        ' Evento per notificare quando c'è un nuovo frame da inviare
        Public Event OnFrameReadyToSend As Action(Of Byte(), Integer, Integer)

        Private _remoteVideoSourceProperty As ImageSource

        Public Property RemoteVideoSource As ImageSource
            Get
                Return _remoteVideoSourceProperty
            End Get
            Set(value As ImageSource)
                _remoteVideoSourceProperty = value
                OnPropertyChanged()
            End Set
        End Property

        ' Modifica il metodo ProcessFrame per comprimere e inviare
        Private Sub ProcessFrame(frame As Mat)
            Try
                ' Salta frame se l'UI è occupata
                Static lastFrameTime As DateTime = DateTime.Now
                If (DateTime.Now - lastFrameTime).TotalMilliseconds < 66 Then ' Max 15fps
                    Return
                End If
                lastFrameTime = DateTime.Now

                Application.Current.Dispatcher.Invoke(
                Sub()
                    Try
                        ' Converti Mat in WriteableBitmap
                        Dim bitmap = ConvertMatToBitmap(frame)
                        If bitmap IsNot Nothing Then
                            LocalVideoSource = bitmap
                            RaiseEvent OnLocalFrameUpdated(bitmap)

                            ' Prepara il frame per l'invio
                            PrepareFrameForSending(frame)
                        End If
                    Catch innerEx As Exception
                        Debug.Print($"Error processing frame UI: {innerEx.Message}")
                    End Try
                End Sub)

            Catch ex As Exception
                Debug.Print($"Error processing frame: {ex.Message}")
            End Try
        End Sub

        Private Sub PrepareFrameForSending(frame As Mat)
            Try
                ' Converti il frame in JPEG per ridurre la dimensione
                Dim compressedFrame As Byte() = CompressFrame(frame)

                If compressedFrame IsNot Nothing AndAlso compressedFrame.Length > 0 Then
                    ' Notifica che il frame è pronto per l'invio
                    RaiseEvent OnFrameReadyToSend(compressedFrame, frame.Width, frame.Height)
                End If

            Catch ex As Exception
                Debug.Print($"Error preparing frame for sending: {ex.Message}")
            End Try
        End Sub

        Private Function CompressFrame(frame As Mat) As Byte()
            Try
                ' Converte il frame in JPEG
                Using image = frame.ToImage(Of Bgr, Byte)()
                    Return image.ToJpegData(_frameQuality)
                End Using

            Catch ex As Exception
                Debug.Print($"Error compressing frame: {ex.Message}")

                ' Fallback: crea dati di test
                Return CreateTestFrameData(frame.Width, frame.Height)
            End Try
        End Function

        Private Function CreateTestFrameData(width As Integer, height As Integer) As Byte()
            '' Crea dati di test per debug
            'Dim testData = New Byte(99) {}
            '_random.NextBytes(testData)
            'Return testData
        End Function

        ' Metodo per ricevere e visualizzare frame remoti
        Public Sub ReceiveRemoteFrame(frameData As Byte(), width As Integer, height As Integer)
            'Try
            '    Application.Current.Dispatcher.Invoke(
            '    Sub()
            '        Try
            '            Dim bitmap = ConvertJpegToBitmap(frameData, width, height)
            '            If bitmap IsNot Nothing Then
            '                RemoteVideoSource = bitmap
            '                RaiseEvent OnRemoteFrameUpdated(bitmap)
            '            End If
            '        Catch innerEx As Exception
            '            debug.print($"Error processing remote frame UI: {innerEx.Message}")
            '        End Try
            '    End Sub)

            'Catch ex As Exception
            '    debug.print($"Error receiving remote frame: {ex.Message}")
            'End Try
            Try
                Debug.Print($"DEBUG: Ricevuto frame {width}x{height}, {frameData.Length} bytes")

                'Application.Current.Dispatcher.Invoke(
                '    Sub()
                '        Try
                '            Dim bitmap = ConvertJpegToBitmap(frameData, width, height)
                '            If bitmap IsNot Nothing Then
                '                ' CORREZIONE: Usa la proprietà, non la variabile
                '                Me.RemoteVideoSource = bitmap
                '                RaiseEvent OnRemoteFrameUpdated(bitmap)
                '            End If
                '        Catch innerEx As Exception
                '            Debug.Print($"Error processing remote frame UI: {innerEx.Message}")
                '        End Try
                '    End Sub)

                Application.Current.Dispatcher.Invoke(
                            Sub()
                                Try
                                    ' Prova a convertire i dati JPEG in bitmap
                                    Dim bitmap = ConvertJpegToBitmap(frameData)

                                    If bitmap IsNot Nothing Then
                                        Debug.Print($"DEBUG: Bitmap creata: {bitmap.PixelWidth}x{bitmap.PixelHeight}")
                                        Me.RemoteVideoSource = bitmap
                                        RaiseEvent OnRemoteFrameUpdated(bitmap)
                                    Else
                                        Debug.Print($"DEBUG: Fallita conversione JPEG, uso fallback")
                                        ' Fallback: crea bitmap di test
                                        Dim testBitmap = CreateTestBitmap(width, height, True)
                                        Me.RemoteVideoSource = testBitmap
                                        RaiseEvent OnRemoteFrameUpdated(testBitmap)
                                    End If

                                Catch innerEx As Exception
                                    Debug.Print($"DEBUG: Errore in ReceiveRemoteFrame: {innerEx.Message}")
                                    ' Crea bitmap di errore
                                    Dim errorBitmap = CreateErrorBitmap(width, height)
                                    Me.RemoteVideoSource = errorBitmap
                                    RaiseEvent OnRemoteFrameUpdated(errorBitmap)
                                End Try
                            End Sub)

            Catch ex As Exception
                Debug.Print($"Error receiving remote frame: {ex.Message}")
            End Try

        End Sub

        Private Function CreateTestBitmap(width As Integer, height As Integer, Optional isRemote As Boolean = False) As WriteableBitmap
            Try
                Dim bitmap = New WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, Nothing)

                bitmap.Lock()
                Try
                    Dim pixelData = New Byte(width * height * 3 - 1) {}

                    ' Pattern diverso per locale e remoto
                    For y As Integer = 0 To height - 1
                        For x As Integer = 0 To width - 1
                            Dim index = (y * width + x) * 3

                            If isRemote Then
                                ' Pattern per video REMOTO (blu/verde)
                                pixelData(index) = CByte((x + _frameCounter) Mod 256)       ' B
                                pixelData(index + 1) = CByte((y + _frameCounter) Mod 256)   ' G
                                pixelData(index + 2) = 50                                   ' R (basso)
                            Else
                                ' Pattern per video LOCALE (rosso/verde)
                                pixelData(index) = 50                                       ' B (basso)
                                pixelData(index + 1) = CByte((y + _frameCounter) Mod 256)   ' G
                                pixelData(index + 2) = CByte((x + _frameCounter) Mod 256)   ' R
                            End If
                        Next
                    Next

                    System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bitmap.BackBuffer, pixelData.Length)
                    bitmap.AddDirtyRect(New Int32Rect(0, 0, width, height))
                Finally
                    bitmap.Unlock()
                End Try

                Return bitmap

            Catch ex As Exception
                Debug.Print($"Error creating test bitmap: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Private Function ConvertJpegToBitmap(jpegData As Byte()) As WriteableBitmap
            Try
                'If jpegData Is Nothing OrElse jpegData.Length = 0 Then
                '    Debug.Print("DEBUG: Dati JPEG nulli o vuoti")
                '    Return Nothing
                'End If

                '' Crea un MemoryStream dai dati JPEG
                'Using stream As New System.IO.MemoryStream(jpegData)
                '    ' Crea una BitmapImage dal stream
                '    Dim bitmapImage = New BitmapImage()

                '    bitmapImage.BeginInit()
                '    bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                '    bitmapImage.StreamSource = stream
                '    bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache
                '    bitmapImage.EndInit()

                '    ' Assicurati che l'immagine sia congelata per l'uso in altri thread
                '    bitmapImage.Freeze()

                '    ' Converti BitmapImage in WriteableBitmap
                '    Dim writeableBitmap = New WriteableBitmap(bitmapImage)

                '    Debug.Print($"DEBUG: JPEG convertito in bitmap: {writeableBitmap.PixelWidth}x{writeableBitmap.PixelHeight}")
                '    Return writeableBitmap

                'End Using
                If jpegData Is Nothing OrElse jpegData.Length = 0 Then
                    Debug.Print("DEBUG: Dati JPEG nulli o vuoti")
                    Return Nothing
                End If

                Debug.Print($"DEBUG: Tentativo conversione JPEG di {jpegData.Length} bytes")

                ' Crea una copia locale dei dati per evitare problemi di riferimento
                Dim localData = jpegData.ToArray()

                ' Crea un MemoryStream
                Dim stream As New System.IO.MemoryStream(localData)

                ' Crea una BitmapImage dal stream
                Dim bitmapImage As New BitmapImage()

                bitmapImage.BeginInit()
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat
                bitmapImage.StreamSource = stream
                bitmapImage.EndInit()

                ' IMPORTANTE: Non chiudere lo stream qui! BitmapImage lo gestirà
                ' Freeze l'immagine per renderla thread-safe
                If bitmapImage.CanFreeze Then
                    bitmapImage.Freeze()
                End If

                ' Converti BitmapImage in WriteableBitmap
                Dim writeableBitmap As New WriteableBitmap(bitmapImage)

                Debug.Print($"DEBUG: JPEG convertito: {writeableBitmap.PixelWidth}x{writeableBitmap.PixelHeight}")
                Return writeableBitmap

            Catch ex As Exception
                Debug.Print($"DEBUG: Errore conversione JPEG: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Private Function CreateErrorBitmap(width As Integer, height As Integer) As WriteableBitmap
            Try
                Dim bitmap = New WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, Nothing)

                bitmap.Lock()
                Try
                    ' Crea un pattern a scacchi rosso/nero per segnalare errore
                    Dim pixelData = New Byte(width * height * 3 - 1) {}

                    For y As Integer = 0 To height - 1
                        For x As Integer = 0 To width - 1
                            Dim index = (y * width + x) * 3

                            ' Pattern a scacchi
                            If (x \ 32 + y \ 32) Mod 2 = 0 Then
                                ' Quadrato rosso
                                pixelData(index) = 255       ' B
                                pixelData(index + 1) = 0     ' G
                                pixelData(index + 2) = 0     ' R
                            Else
                                ' Quadrato nero
                                pixelData(index) = 0         ' B
                                pixelData(index + 1) = 0     ' G
                                pixelData(index + 2) = 0     ' R
                            End If
                        Next
                    Next

                    System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, bitmap.BackBuffer, pixelData.Length)
                    bitmap.AddDirtyRect(New Int32Rect(0, 0, width, height))
                Finally
                    bitmap.Unlock()
                End Try

                Return bitmap

            Catch ex As Exception
                Debug.Print($"Error creating error bitmap: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Private Function ConvertJpegToBitmap(jpegData As Byte(), width As Integer, height As Integer) As WriteableBitmap
            Try
                If jpegData Is Nothing OrElse jpegData.Length = 0 Then Return Nothing

                ' Decodifica i dati JPEG
                Dim stream = New MemoryStream(jpegData)
                Dim bitmapImage = New BitmapImage()

                bitmapImage.BeginInit()
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                bitmapImage.StreamSource = stream
                bitmapImage.EndInit()
                bitmapImage.Freeze()

                ' Converti in WriteableBitmap
                Dim writeableBitmap = New WriteableBitmap(bitmapImage)
                Return writeableBitmap

            Catch ex As Exception
                Debug.Print($"Error converting JPEG to bitmap: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Public Sub New()
            ' Inizializzazione lazy nel metodo Start
        End Sub

        Public Function StartVideoCapture() As Boolean
            If _isCapturing Then
                Debug.Print("Video capture already started")
                Return True
            End If

            Try
                Debug.Print("Starting video capture with Emgu.CV...")

                ' Crea VideoCapture per la webcam (0 = prima webcam)
                _capture = New VideoCapture(0)

                If Not _capture.IsOpened Then
                    RaiseEvent OnVideoError("Impossibile aprire la webcam")
                    Return False
                End If

                ' Configura risoluzione
                _capture.Set(CapProp.FrameWidth, 640)
                _capture.Set(CapProp.FrameHeight, 480)
                _capture.Set(CapProp.Fps, 15)

                _isCapturing = True

                ' Avvia timer per catturare frame
                _timer = New Threading.Timer(AddressOf CaptureFrame, Nothing, 0, 66) ' ~15 FPS

                RaiseEvent OnVideoStarted()
                Debug.Print("Video capture started successfully")
                Return True

            Catch ex As Exception
                Dim errorMsg = $"Error starting video capture: {ex.Message}"
                Debug.Print(errorMsg)
                RaiseEvent OnVideoError(errorMsg)
                Return False
            End Try
        End Function

        Private Sub CaptureFrame(state As Object)
            If Not _isCapturing OrElse _capture Is Nothing Then Return

            Try
                Using frame As New Mat()
                    Try
                        ' Cattura un frame
                        If _capture.Read(frame) AndAlso Not frame.IsEmpty Then
                            ProcessFrame(frame)
                        End If
                    Catch ex As Exception
                        Debug.Print($"Error reading frame: {ex.Message}")
                    End Try
                End Using

            Catch ex As Exception
                Debug.Print($"Error capturing frame: {ex.Message}")
            End Try
        End Sub

        Private Function ConvertMatToBitmap(mat As Mat) As WriteableBitmap
            Try
                If mat Is Nothing OrElse mat.IsEmpty Then Return Nothing

                ' Converti Mat in Bitmap di System.Drawing
                Using bitmap = mat.ToBitmap()
                    ' Converti Bitmap in WriteableBitmap di WPF
                    Dim stream = New System.IO.MemoryStream()
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp)
                    stream.Seek(0, System.IO.SeekOrigin.Begin)

                    Dim bitmapImage = New BitmapImage()
                    bitmapImage.BeginInit()
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad
                    bitmapImage.StreamSource = stream
                    bitmapImage.EndInit()
                    bitmapImage.Freeze()

                    ' Converti in WriteableBitmap
                    Dim writeableBitmap = New WriteableBitmap(bitmapImage)
                    Return writeableBitmap
                End Using

            Catch ex As Exception
                Debug.Print($"Error converting Mat to Bitmap: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Public Sub StopVideoCapture()
            Try
                If _isCapturing Then
                    Debug.Print("Stopping video capture...")

                    _isCapturing = False

                    If _timer IsNot Nothing Then
                        _timer.Dispose()
                        _timer = Nothing
                    End If

                    If _capture IsNot Nothing Then
                        _capture.Dispose()
                        _capture = Nothing
                    End If

                    RaiseEvent OnVideoStopped()
                    Debug.Print("Video capture stopped successfully")
                End If
            Catch ex As Exception
                Dim errorMsg = $"Error stopping video: {ex.Message}"
                Debug.Print(errorMsg)
                RaiseEvent OnVideoError(errorMsg)
            End Try
        End Sub

        'Public Sub Dispose() Implements IDisposable.Dispose
        '    If Not _isDisposed Then
        '        Debug.Print("Disposing Video Manager...")
        '        StopVideoCapture()
        '        _isDisposed = True
        '        Debug.Print("Video Manager disposed successfully")
        '    End If
        'End Sub

        ' ========== METODO DISPOSE ==========

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _isDisposed Then
                Debug.Print("Disposing Video Manager...")

                ' Ferma audio
                StopAudioCapture()
                StopAudioPlayback()

                ' ... [resto del codice dispose per video] ...
            End If
        End Sub

        Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
        End Sub

#Region "Audio Capture (NAudio)"
        ' ========== METODI PER AUDIO ==========

        Public Function StartAudioCapture() As Boolean
            If _isAudioCapturing Then
                Debug.Print("Audio capture already started")
                Return True
            End If

            Try
                Debug.Print("Starting audio capture...")

                ' VERIFICA MICROFONI DISPONIBILI
                Debug.Print($"WaveIn devices: {WaveIn.DeviceCount}")
                For i As Integer = 0 To WaveIn.DeviceCount - 1
                    Dim caps = WaveIn.GetCapabilities(i)
                    Debug.Print($"Device {i}: {caps.ProductName}")
                Next

                If WaveIn.DeviceCount = 0 Then
                    RaiseEvent OnAudioError("Nessun microfono trovato!")
                    Return False
                End If

                ' Configura cattura audio
                _waveIn = New WaveInEvent()
                _waveIn.DeviceNumber = 0 ' Forza l'uso del primo microfono
                '_waveIn.WaveFormat = New WaveFormat(44100, 16, 1) ' 44.1kHz, 16-bit, mono
                _waveIn.WaveFormat = New WaveFormat(16000, 16, 1) ' 16kHz invece di 44.1kHz
                AddHandler _waveIn.DataAvailable, AddressOf OnAudioDataAvailable
                AddHandler _waveIn.RecordingStopped, AddressOf OnAudioRecordingStopped

                Debug.Print($"WaveIn configurato: Device={_waveIn.DeviceNumber}, Format={_waveIn.WaveFormat.SampleRate}Hz, Buffer={_waveIn.BufferMilliseconds}ms")

                ' Buffer per ricezione audio
                _bufferedWaveProvider = New BufferedWaveProvider(_waveIn.WaveFormat)
                _bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(2)

                _waveIn.StartRecording()
                _isAudioCapturing = True

                Debug.Print("Audio capture started successfully - DOVREBBE INIZIARE A RICEVERE DATI")

                ' Timer per invio audio (riduce frame rate)
                _audioTimer = New System.Threading.Timer(AddressOf SendAudioData, Nothing, 0, 100)

                RaiseEvent OnAudioStarted()

                Debug.Print("Audio capture started successfully")
                Return True

            Catch ex As Exception
                Dim errorMsg = $"Error starting audio capture: {ex.Message}"
                Debug.Print(errorMsg)
                RaiseEvent OnAudioError(errorMsg)
                Return False
            End Try
        End Function

        Public Sub StopAudioCapture()
            Try
                If _isAudioCapturing Then
                    Debug.Print("Stopping audio capture...")

                    If _audioTimer IsNot Nothing Then
                        _audioTimer.Dispose()
                        _audioTimer = Nothing
                    End If

                    If _waveIn IsNot Nothing Then
                        _waveIn.StopRecording()
                        _waveIn.Dispose()
                        _waveIn = Nothing
                    End If

                    _isAudioCapturing = False
                    RaiseEvent OnAudioStopped()
                    Debug.Print("Audio capture stopped successfully")
                End If
            Catch ex As Exception
                Dim errorMsg = $"Error stopping audio: {ex.Message}"
                Debug.Print(errorMsg)
                RaiseEvent OnAudioError(errorMsg)
            End Try
        End Sub

        Private Sub OnAudioDataAvailable(sender As Object, e As WaveInEventArgs)
            Try

                If Not _isAudioCapturing OrElse e.BytesRecorded = 0 Then Return

                ' Calcola volume
                Dim sum As Long = 0
                For i As Integer = 0 To e.BytesRecorded - 1 Step 2
                    If i + 1 < e.BytesRecorded Then
                        Dim sample = BitConverter.ToInt16(e.Buffer, i)
                        sum += Math.Abs(sample)
                    End If
                Next
                Dim avgVolume = CSng(sum) / (e.BytesRecorded / 2)

                ' Rileva se qualcuno sta parlando
                If avgVolume > _silenceThreshold Then
                    _isSpeaking = True
                    _lastSpeechTime = DateTime.Now
                End If

                ' I dati audio sono pronti per essere inviati o riprodotti
                If _isAudioCapturing AndAlso e.BytesRecorded > 0 Then

                    ' Invia audio solo se:
                    ' 1. Il volume è sopra la soglia (qualcuno parla)
                    ' 2. Non stiamo già riproducendo audio (per evitare eco)
                    ' 3. Non è passato troppo tempo dall'ultima volta che qualcuno ha parlato
                    If avgVolume > _silenceThreshold AndAlso Not _isAudioPlaying Then
                        ' SAMPLE RATE CONVERSION: riduci ulteriormente se necessario
                        ' Per ora inviamo solo 1 pacchetto ogni 2 per ridurre traffico
                        Static skipCounter As Integer = 0
                        skipCounter += 1
                        If skipCounter Mod 2 = 0 Then ' Invia solo metà pacchetti

                            ' Copia i dati
                            Dim audioData = New Byte(e.BytesRecorded - 1) {}
                            Array.Copy(e.Buffer, audioData, e.BytesRecorded)

                            ' Invia in background
                            Task.Run(Sub() RaiseEvent OnAudioDataReady(audioData))
                        End If

                        ' Buffer locale (sempre attivo per sentire se stai parlando)
                        If _isAudioPlaying AndAlso _bufferedWaveProvider IsNot Nothing Then
                            _bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded)
                        End If
                    End If

                    ' Reset stato parlato dopo un periodo di silenzio
                    If _isSpeaking AndAlso (DateTime.Now - _lastSpeechTime).TotalMilliseconds > _speakingTimeout Then
                        _isSpeaking = False
                        Debug.Print("AUDIO: Fine conversazione")
                    End If

                End If

            Catch ex As Exception
                Debug.Print($"Error processing audio data: {ex.Message}")
            End Try
        End Sub

        Private Sub OnAudioRecordingStopped(sender As Object, e As StoppedEventArgs)
            Debug.Print($"Audio recording stopped: {e.Exception?.Message}")
            _isAudioCapturing = False
        End Sub

        Private Sub SendAudioData(state As Object)
            ' Questo metodo viene chiamato dal timer per inviare dati audio
            ' L'invio effettivo è già gestito da OnAudioDataAvailable
            ' Qui possiamo aggiungere logica di compressione se necessario
        End Sub

        ' Metodo per ricevere e riprodurre audio remoto
        Public Sub ReceiveRemoteAudio(audioData As Byte())
            Try
                If audioData Is Nothing OrElse audioData.Length = 0 Then
                    Debug.Print("AUDIO DEBUG: Dati audio nulli o vuoti")
                    Return
                End If

                Debug.Print($"AUDIO DEBUG: Ricevuti {audioData.Length} bytes")

                ' Se non abbiamo ancora inizializzato la riproduzione, fallo
                If Not _isAudioPlaying Then
                    Debug.Print("AUDIO DEBUG: Inizializzo riproduzione audio")
                    InitializeAudioPlayback()
                End If

                ' Aggiungi i dati audio al buffer per la riproduzione
                If _bufferedWaveProvider IsNot Nothing Then
                    _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length)
                    Debug.Print($"DEBUG: Added {audioData.Length} bytes to audio buffer")
                Else
                    Debug.Print("AUDIO DEBUG: _bufferedWaveProvider è null!")
                End If

            Catch ex As Exception
                Debug.Print($"Error receiving remote audio: {ex.Message}")
            End Try
        End Sub

        Private Sub InitializeAudioPlayback()
            Try
                If _waveOut Is Nothing AndAlso _bufferedWaveProvider IsNot Nothing Then
                    Debug.Print("AUDIO DEBUG: Creazione WaveOut...")

                    _waveOut = New WaveOutEvent()
                    _waveOut.Init(_bufferedWaveProvider)
                    _waveOut.Play()
                    _isAudioPlaying = True
                    Debug.Print($"AUDIO DEBUG: WaveOut creato e avviato. Volume: {_waveOut.Volume}")

                    Debug.Print("Audio playback initialized")

                    ' Verifica dispositivo audio
                    For i As Integer = 0 To WaveOut.DeviceCount - 1
                        Dim caps = WaveOut.GetCapabilities(i)
                        Debug.Print($"AUDIO DEBUG: Device {i}: {caps.ProductName}")
                    Next

                End If
            Catch ex As Exception
                Debug.Print($"Error initializing audio playback: {ex.Message}")
                RaiseEvent OnAudioError($"Cannot initialize audio playback: {ex.Message}")
            End Try
        End Sub

        Public Function CheckMicrophone() As String
            Dim result As String = ""

            Try
                result &= $"WaveIn devices found: {WaveIn.DeviceCount}" & vbCrLf

                For i As Integer = 0 To WaveIn.DeviceCount - 1
                    Dim caps = WaveIn.GetCapabilities(i)
                    result &= $"Device {i}: {caps.ProductName}" & vbCrLf
                    result &= $"  - Channels: {caps.Channels}" & vbCrLf
                    result &= $"  - Supported: Yes" & vbCrLf
                Next

                If WaveIn.DeviceCount = 0 Then
                    result &= "NESSUN MICROFONO TROVATO!" & vbCrLf
                    result &= "Verifica che il microfono sia collegato e i driver installati."
                End If

            Catch ex As Exception
                result &= $"ERRORE: {ex.Message}"
            End Try

            Return result
        End Function

        Public Sub TestLocalAudio()
            Try
                Debug.Print("AUDIO DEBUG: Test locale audio...")

                ' Notifica all'UI
                Application.Current.Dispatcher.Invoke(Sub()
                                                          MessageBox.Show("Test audio in corso - Dovresti sentire un tono per 1 secondo",
                          "Test Audio", MessageBoxButton.OK, MessageBoxImage.Information)
                                                      End Sub)

                ' Crea WaveOut
                Dim waveOut = New WaveOutEvent()

                ' Crea provider che genera un tono di 440Hz per 1 secondo
                Dim toneProvider = New ToneWaveProvider(440, 1.0)

                ' Inizializza e riproduci
                waveOut.Init(toneProvider)
                waveOut.Play()

                Debug.Print("AUDIO DEBUG: Tono 440Hz avviato")

                ' Ferma dopo 1 secondo
                Dim stopTime = DateTime.Now.AddSeconds(1)
                Task.Run(
            Sub()
                While DateTime.Now < stopTime
                    Thread.Sleep(100)
                End While

                waveOut.Stop()
                waveOut.Dispose()
                Debug.Print("AUDIO DEBUG: Tono fermato")

                Application.Current.Dispatcher.Invoke(Sub()
                                                          Debug.Print("Test audio completato")
                                                      End Sub)
            End Sub)

            Catch ex As Exception
                Debug.Print($"AUDIO DEBUG: Errore test locale: {ex.Message}")

                ' Fallback: beep di Windows
                Try
                    Console.Beep(440, 500)
                Catch
                    System.Media.SystemSounds.Asterisk.Play()
                End Try
            End Try
        End Sub

        Public Sub StopAudioPlayback()
            Try
                If _waveOut IsNot Nothing Then
                    _waveOut.Stop()
                    _waveOut.Dispose()
                    _waveOut = Nothing
                    _isAudioPlaying = False
                    Debug.Print("Audio playback stopped")
                End If
            Catch ex As Exception
                Debug.Print($"Error stopping audio playback: {ex.Message}")
            End Try
        End Sub

#End Region

    End Class

    ' Classe helper per generare un tono semplice
    Public Class ToneWaveProvider
        Inherits WaveProvider32

        Private _frequency As Double
        Private _amplitude As Double
        Private _phase As Double
        Private _sampleRate As Integer = 44100

        Public Sub New(frequency As Double, durationSeconds As Double)
            MyBase.New(44100, 1) ' 44.1kHz, mono
            _frequency = frequency
            _amplitude = 0.3 ' Volume 30%
            Debug.Print($"ToneWaveProvider creato: {frequency}Hz per {durationSeconds}s")
        End Sub

        Public Overrides Function Read(buffer() As Single, offset As Integer, sampleCount As Integer) As Integer
            For i As Integer = 0 To sampleCount - 1
                ' Genera onda sinusoidale
                buffer(offset + i) = CSng(_amplitude * Math.Sin(_phase))

                ' Aggiorna fase
                _phase += 2 * Math.PI * _frequency / _sampleRate

                ' Mantieni fase tra 0 e 2PI
                If _phase > 2 * Math.PI Then
                    _phase -= 2 * Math.PI
                End If
            Next

            ' Log ogni secondo circa
            Static sampleCounter As Integer = 0
            sampleCounter += sampleCount
            If sampleCounter >= _sampleRate Then
                Debug.Print($"ToneWaveProvider: generati {sampleCounter} samples")
                sampleCounter = 0
            End If

            Return sampleCount
        End Function
    End Class
End Namespace