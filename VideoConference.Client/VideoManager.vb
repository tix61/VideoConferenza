Imports System
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports Emgu.CV
Imports Emgu.CV.CvEnum
Imports Emgu.CV.Structure
Imports System.Drawing

Namespace VideoConference.Client
    Public Class VideoManager
        Implements INotifyPropertyChanged, IDisposable

        Private _capture As VideoCapture
        Private _isDisposed As Boolean = False
        Private _isCapturing As Boolean = False
        Private _timer As Threading.Timer

        Private _localVideoSourceProperty As ImageSource

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
        Public Event OnVideoError As Action(Of String)
        Public Event OnVideoStarted As Action
        Public Event OnVideoStopped As Action
        Public Event OnLocalFrameUpdated As Action(Of WriteableBitmap)

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

        Public Sub New()
            ' Inizializzazione lazy nel metodo Start
        End Sub

        Public Function StartVideoCapture() As Boolean
            If _isCapturing Then
                Console.WriteLine("Video capture already started")
                Return True
            End If

            Try
                Console.WriteLine("Starting video capture with Emgu.CV...")

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
                Console.WriteLine("Video capture started successfully")
                Return True

            Catch ex As Exception
                Dim errorMsg = $"Error starting video capture: {ex.Message}"
                Console.WriteLine(errorMsg)
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
                Console.WriteLine($"Error capturing frame: {ex.Message}")
            End Try
        End Sub

        Private Sub ProcessFrame(frame As Mat)
            Try
                Application.Current.Dispatcher.Invoke(
                    Sub()
                        Try
                            ' Converti Mat di Emgu.CV in WriteableBitmap di WPF
                            Dim bitmap = ConvertMatToBitmap(frame)
                            If bitmap IsNot Nothing Then
                                RaiseEvent OnLocalFrameUpdated(bitmap)
                            End If
                        Catch innerEx As Exception
                            Console.WriteLine($"Error processing frame UI: {innerEx.Message}")
                        End Try
                    End Sub)

            Catch ex As Exception
                Console.WriteLine($"Error processing frame: {ex.Message}")
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
                Console.WriteLine($"Error converting Mat to Bitmap: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Public Sub StopVideoCapture()
            Try
                If _isCapturing Then
                    Console.WriteLine("Stopping video capture...")

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
                    Console.WriteLine("Video capture stopped successfully")
                End If
            Catch ex As Exception
                Dim errorMsg = $"Error stopping video: {ex.Message}"
                Console.WriteLine(errorMsg)
                RaiseEvent OnVideoError(errorMsg)
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _isDisposed Then
                Console.WriteLine("Disposing Video Manager...")
                StopVideoCapture()
                _isDisposed = True
                Console.WriteLine("Video Manager disposed successfully")
            End If
        End Sub

        Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
        End Sub
    End Class
End Namespace