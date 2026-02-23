Imports System
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Diagnostics
Imports Emgu.CV
Imports Emgu.CV.CvEnum
Imports Emgu.CV.Structure

Public Class ScreenShareManager
    Implements INotifyPropertyChanged, IDisposable

    Private _isSharing As Boolean = False
    Private _timer As Timer
    Private _screenBitmap As WriteableBitmap
    Private _frameQuality As Integer = 40 ' Qualità JPEG
    Private _screenWidth As Integer = 1280
    Private _screenHeight As Integer = 720
    Private _frameLock As New Object()

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
    Public Event OnScreenShareStarted As Action
    Public Event OnScreenShareStopped As Action
    Public Event OnScreenError As Action(Of String)
    Public Event OnScreenFrameReady As Action(Of Byte(), Integer, Integer)

    ' qualità e dimensioni per condivisione (possono essere configurabili)
    Private _shareQuality As Integer = 75          ' Qualità JPEG (1-100)
    Private _shareMaxWidth As Integer = 1280       ' Larghezza massima di invio
    Private _shareMaxHeight As Integer = 720       ' Altezza massima di invio

    Public ReadOnly Property IsSharing As Boolean
        Get
            Return _isSharing
        End Get
    End Property

    Public Property ScreenPreview As ImageSource
        Get
            Return _screenBitmap
        End Get
        Set(value As ImageSource)
            _screenBitmap = TryCast(value, WriteableBitmap)
            OnPropertyChanged()
        End Set
    End Property

    Public Sub New()
        Debug.Print("ScreenShareManager initialized")
    End Sub

    Public Function StartScreenShare() As Boolean
        If _isSharing Then Return True

        Try
            Debug.Print("Starting screen share...")

            ' Ottieni dimensioni schermo primario
            Dim primaryScreen = System.Windows.Forms.Screen.PrimaryScreen
            _screenWidth = primaryScreen.Bounds.Width
            _screenHeight = primaryScreen.Bounds.Height

            '' Riduci se troppo grande
            'If _screenWidth > 1280 Then
            '    _screenWidth = 1280
            '    _screenHeight = CInt(_screenHeight * 1280 / primaryScreen.Bounds.Width)
            'End If

            Debug.Print($"Screen size: {_screenWidth}x{_screenHeight}")

            _isSharing = True

            ' Timer per cattura schermo (5 FPS per risparmiare CPU)
            _timer = New Timer(AddressOf CaptureScreen, Nothing, 0, 200) ' 5 FPS

            RaiseEvent OnScreenShareStarted()
            Debug.Print("Screen share started")
            Return True

        Catch ex As Exception
            Dim errorMsg = $"Error starting screen share: {ex.Message}"
            Debug.Print(errorMsg)
            RaiseEvent OnScreenError(errorMsg)
            Return False
        End Try
    End Function

    'Private Sub CaptureScreen(state As Object)
    '    If Not _isSharing Then Return

    '    SyncLock _frameLock
    '        Try
    '            Using bitmap As New System.Drawing.Bitmap(_screenWidth, _screenHeight)
    '                Using graphics = System.Drawing.Graphics.FromImage(bitmap)
    '                    graphics.CopyFromScreen(0, 0, 0, 0,
    '                    New System.Drawing.Size(_screenWidth, _screenHeight))
    '                End Using

    '                ' Usa BitmapExtension per convertire
    '                Dim imageFrame = bitmap.ToImage(Of Bgr, Byte)()

    '                Try
    '                    ' Riduci per invio direttamente con Resize
    '                    Using resized = imageFrame.Resize(320, 240, Inter.Linear)
    '                        Dim compressedFrame As Byte() = resized.ToJpegData(_frameQuality)

    '                        If compressedFrame IsNot Nothing AndAlso compressedFrame.Length > 0 Then
    '                            ' Aggiorna preview solo per DEBUG
    '                            'UpdatePreviewSimple(resized)

    '                            ' Invia
    '                            RaiseEvent OnScreenFrameReady(compressedFrame, 320, 240)
    '                        End If
    '                    End Using
    '                Finally
    '                    imageFrame.Dispose()
    '                End Try
    '            End Using

    '        Catch ex As Exception
    '            Debug.Print($"Error capturing screen: {ex.Message}")
    '        End Try
    '    End SyncLock
    'End Sub

    Private Sub CaptureScreen(state As Object)
        If Not _isSharing Then Return

        SyncLock _frameLock
            Try
                Using bitmap As New System.Drawing.Bitmap(_screenWidth, _screenHeight)
                    Using graphics = System.Drawing.Graphics.FromImage(bitmap)
                        graphics.CopyFromScreen(0, 0, 0, 0,
                    New System.Drawing.Size(_screenWidth, _screenHeight))
                    End Using

                    ' Converti Bitmap in Image di Emgu.CV
                    Dim imageFrame = bitmap.ToImage(Of Bgr, Byte)()

                    Try
                        ' Definisci la larghezza massima desiderata (es. 1280)
                        Dim maxWidth As Integer = 1280

                        ' Calcola l'altezza mantenendo le proporzioni
                        Dim targetWidth As Integer = Math.Min(_screenWidth, maxWidth)
                        Dim targetHeight As Integer = CInt(_screenHeight * targetWidth / _screenWidth)

                        Debug.Print($"Ridimensionamento: {_screenWidth}x{_screenHeight} -> {targetWidth}x{targetHeight}")

                        Using resized = imageFrame.Resize(targetWidth, targetHeight, Inter.Linear)
                            ' Usa una qualità JPEG più alta per preservare i dettagli
                            Dim compressedFrame As Byte() = resized.ToJpegData(75) ' Aumenta qualità

                            If compressedFrame IsNot Nothing AndAlso compressedFrame.Length > 0 Then
                                ' Invia con le dimensioni effettive del frame ridimensionato
                                RaiseEvent OnScreenFrameReady(compressedFrame, targetWidth, targetHeight)
                            End If
                        End Using
                    Finally
                        imageFrame.Dispose()
                    End Try
                End Using

            Catch ex As Exception
                Debug.Print($"Error capturing screen: {ex.Message}")
            End Try
        End SyncLock
    End Sub

    ' Versione sicura per l'aggiornamento della preview
    Private Sub UpdatePreviewSafe(image As Image(Of Bgr, Byte))
        Try
            If image Is Nothing Then Return

            ' Crea una copia dei dati dell'immagine
            Dim imageData As Byte() = image.Bytes
            Dim imageWidth As Integer = image.Width
            Dim imageHeight As Integer = image.Height

            If imageData Is Nothing OrElse imageData.Length = 0 Then Return

            Application.Current.Dispatcher.BeginInvoke(
            New Action(Sub()
                           Try
                               Dim bitmap = New WriteableBitmap(320, 240, 96, 96,
                                                   PixelFormats.Bgr24, Nothing)

                               ' Riduci per preview
                               Using originalMat = New Mat(New System.Drawing.Size(imageWidth, imageHeight),
                                                DepthType.Cv8U, 3)
                                   ' Copia i dati nel Mat
                                   System.Runtime.InteropServices.Marshal.Copy(imageData, 0, originalMat.DataPointer, imageData.Length)

                                   Using resized As New Mat()
                                       CvInvoke.Resize(originalMat, resized, New System.Drawing.Size(320, 240))

                                       bitmap.Lock()
                                       Try
                                           ' CORREZIONE: Converti Total da IntPtr a Integer
                                           Dim totalPixels As Integer = CInt(resized.Total)
                                           Dim channels As Integer = resized.NumberOfChannels
                                           Dim bufferSize As Integer = totalPixels * channels

                                           ' Assicurati che bufferSize sia valido
                                           If bufferSize > 0 Then
                                               Dim resizedData(bufferSize - 1) As Byte

                                               ' Ottieni i dati ridimensionati
                                               System.Runtime.InteropServices.Marshal.Copy(resized.DataPointer, resizedData, 0, bufferSize)

                                               ' Calcola quanti byte copiare (minimo tra bufferSize e spazio bitmap)
                                               Dim copyBytes As Integer = Math.Min(bufferSize, 320 * 240 * 3)
                                               System.Runtime.InteropServices.Marshal.Copy(resizedData, 0, bitmap.BackBuffer, copyBytes)
                                               bitmap.AddDirtyRect(New Int32Rect(0, 0, 320, 240))
                                           End If
                                       Finally
                                           bitmap.Unlock()
                                       End Try

                                       resized.Dispose()
                                   End Using
                               End Using

                               ScreenPreview = bitmap

                           Catch innerEx As Exception
                               Debug.Print($"Preview error: {innerEx.Message}")
                           End Try
                       End Sub),
            System.Windows.Threading.DispatcherPriority.Background)

        Catch ex As Exception
            Debug.Print($"UpdatePreviewSafe error: {ex.Message}")
        End Try
    End Sub

    'Private Sub UpdateLocalPreview(frame As Mat)
    '    Try
    '        Application.Current.Dispatcher.BeginInvoke(
    '            Sub()
    '                Try
    '                    Dim bitmap = New WriteableBitmap(frame.Width, frame.Height, 96, 96,
    '                                                   PixelFormats.Bgr24, Nothing)

    '                    bitmap.Lock()
    '                    Try
    '                        ' Riduci per preview (320x240)
    '                        Dim resized As New Mat()
    '                        CvInvoke.Resize(frame, resized, New Size(320, 240))

    '                        Dim data As Byte() = resized.Bytes
    '                        If data IsNot Nothing AndAlso data.Length > 0 Then
    '                            System.Runtime.InteropServices.Marshal.Copy(data, 0,
    '                                bitmap.BackBuffer, Math.Min(data.Length, 320 * 240 * 3))
    '                            bitmap.AddDirtyRect(New Int32Rect(0, 0, 320, 240))
    '                        End If

    '                        resized.Dispose()
    '                    Finally
    '                        bitmap.Unlock()
    '                    End Try

    '                    ScreenPreview = bitmap

    '                Catch innerEx As Exception
    '                    Debug.Print($"Error updating preview: {innerEx.Message}")
    '                End Try
    '            End Sub, System.Windows.Threading.DispatcherPriority.Background)

    '    Catch ex As Exception
    '        Debug.Print($"Error in UpdateLocalPreview: {ex.Message}")
    '    End Try
    'End Sub

    Private Sub UpdateLocalPreview(frame As Mat)
        Try
            Application.Current.Dispatcher.InvokeAsync(
            Sub()
                Try
                    ' Crea bitmap per preview
                    Dim bitmap = New WriteableBitmap(320, 240, 96, 96,
                                                   PixelFormats.Bgr24, Nothing)

                    ' Riduci il frame per preview
                    Dim resized As New Mat()
                    CvInvoke.Resize(frame, resized, New System.Drawing.Size(320, 240))

                    ' Converti in Image(Of Bgr, Byte) per accedere ai dati
                    Using resizedImage = resized.ToImage(Of Bgr, Byte)()

                        bitmap.Lock()
                        Try
                            ' Ottieni i dati come array di byte
                            Dim data = resizedImage.Bytes

                            ' Copia nella bitmap
                            If data IsNot Nothing AndAlso data.Length > 0 Then
                                System.Runtime.InteropServices.Marshal.Copy(data, 0, bitmap.BackBuffer,
                                    Math.Min(data.Length, 320 * 240 * 3))
                                bitmap.AddDirtyRect(New Int32Rect(0, 0, 320, 240))
                            End If
                        Finally
                            bitmap.Unlock()
                        End Try

                    End Using

                    resized.Dispose()

                    ' Aggiorna la proprietà
                    ScreenPreview = bitmap

                Catch innerEx As Exception
                    Debug.Print($"Error in preview update: {innerEx.Message}")
                End Try
            End Sub,
            System.Windows.Threading.DispatcherPriority.Background)

        Catch ex As Exception
            Debug.Print($"Error in UpdateLocalPreview: {ex.Message}")
        End Try
    End Sub

    Private Sub UpdatePreviewSimple(image As Image(Of Bgr, Byte))
        Try
            If image Is Nothing Then Return

            Application.Current.Dispatcher.BeginInvoke(
            New Action(Sub()
                           Try
                               ' Ridimensiona direttamente
                               Using resized = image.Resize(320, 240, Inter.Linear)
                                   Dim bytes = resized.Bytes

                                   If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                                       Dim bitmap = New WriteableBitmap(320, 240, 96, 96, PixelFormats.Bgr24, Nothing)

                                       bitmap.Lock()
                                       Try
                                           System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bitmap.BackBuffer, Math.Min(bytes.Length, 320 * 240 * 3))
                                           bitmap.AddDirtyRect(New Int32Rect(0, 0, 320, 240))
                                       Finally
                                           bitmap.Unlock()
                                       End Try

                                       ScreenPreview = bitmap
                                   End If
                               End Using

                           Catch innerEx As Exception
                               Debug.Print($"Preview error: {innerEx.Message}")
                           End Try
                       End Sub),
            System.Windows.Threading.DispatcherPriority.Background)

        Catch ex As Exception
            Debug.Print($"UpdatePreviewSimple error: {ex.Message}")
        End Try
    End Sub

    'Private Sub PrepareScreenForSending(frame As Mat)
    '    Try
    '        ' Riduci ulteriormente per invio (640x360)
    '        Dim resized As New Mat()
    '        CvInvoke.Resize(frame, resized, New System.Drawing.Size(640, 360))

    '        Using image = resized.ToImage(Of Bgr, Byte)()
    '            Dim compressedFrame As Byte() = image.ToJpegData(_frameQuality)

    '            If compressedFrame IsNot Nothing AndAlso compressedFrame.Length > 0 Then
    '                RaiseEvent OnScreenFrameReady(compressedFrame, 640, 360)
    '            End If
    '        End Using

    '        resized.Dispose()

    '    Catch ex As Exception
    '        Debug.Print($"Error preparing screen frame: {ex.Message}")
    '    End Try
    'End Sub

    Public Sub StopScreenShare()
        Try
            If _isSharing Then
                Debug.Print("Stopping screen share...")

                _isSharing = False

                If _timer IsNot Nothing Then
                    _timer.Dispose()
                    _timer = Nothing
                End If

                RaiseEvent OnScreenShareStopped()
                Debug.Print("Screen share stopped")
            End If

        Catch ex As Exception
            Dim errorMsg = $"Error stopping screen share: {ex.Message}"
            Debug.Print(errorMsg)
            RaiseEvent OnScreenError(errorMsg)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        StopScreenShare()
    End Sub

    Protected Sub OnPropertyChanged(<CallerMemberName> Optional memberName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(memberName))
    End Sub
End Class
