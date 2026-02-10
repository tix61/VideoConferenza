Imports System
Imports System.ComponentModel
Imports System.Drawing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Emgu.CV
Imports Emgu.CV.CvEnum
Imports Emgu.CV.Structure

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
        Private _frameQuality As Integer = 50 ' Qualità JPEG (1-100)
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
                _capture.Set(CapProp.Fps, 30)

                _isCapturing = True

                ' Avvia timer per catturare frame
                _timer = New Threading.Timer(AddressOf CaptureFrame, Nothing, 0, 33) ' ~30 FPS

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
                    ' Cattura un frame
                    If _capture.Read(frame) AndAlso Not frame.IsEmpty Then
                        ProcessFrame(frame)
                    End If
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

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _isDisposed Then
                Debug.Print("Disposing Video Manager...")
                StopVideoCapture()
                _isDisposed = True
                Debug.Print("Video Manager disposed successfully")
            End If
        End Sub

        Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
        End Sub
    End Class
End Namespace