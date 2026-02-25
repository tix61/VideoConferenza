Imports System.Collections.Concurrent
Imports Microsoft.AspNetCore
Imports Microsoft.AspNetCore.SignalR

Namespace Hubs
    Public Class ConferenceHub
        Inherits Hub

        Private Shared ReadOnly _connections As New ConcurrentDictionary(Of String, String)()
        Private Shared ReadOnly _rooms As New ConcurrentDictionary(Of String, String)()
        Private Shared ReadOnly _userStatus As New ConcurrentDictionary(Of String, UserStatus)()
        Private Shared ReadOnly _activeScreenSharer As New ConcurrentDictionary(Of String, String)() ' Key: roomId, Value: connectionId

        ' Classe per tenere traccia dello stato utente
        Private Class UserStatus
            Public Property UserName As String
            Public Property HasVideo As Boolean
            Public Property HasAudio As Boolean
            Public Property IsScreenSharing As Boolean
        End Class

        Public Overrides Async Function OnConnectedAsync() As Task
            Debug.Print($"Client connected: {Context.ConnectionId}")
            Await MyBase.OnConnectedAsync()
        End Function

        Public Overrides Async Function OnDisconnectedAsync(exception As Exception) As Task
            Try
                ' Rimuovi lo stato utente
                _userStatus.TryRemove(Context.ConnectionId, Nothing)

                ' Rimuovi dalla stanza
                Dim roomId As String = Nothing
                If _rooms.TryGetValue(Context.ConnectionId, roomId) Then
                    ' Notifica a tutti che l'utente ha fermato il video (se era attivo)
                    If _userStatus.ContainsKey(Context.ConnectionId) AndAlso _userStatus(Context.ConnectionId).HasVideo Then
                        Await Clients.OthersInGroup(roomId).SendAsync("VideoStopped", Context.ConnectionId)
                    End If

                    ' Se questo utente stava condividendo lo schermo, rimuovilo
                    Dim sharer As String = Nothing
                    If _activeScreenSharer.TryGetValue(roomId, sharer) AndAlso sharer = Context.ConnectionId Then
                        _activeScreenSharer.TryRemove(roomId, Nothing)
                    End If
                    Await Clients.Group(roomId).SendAsync("ScreenShareStopped", Context.ConnectionId)
                End If

                ' Notifica agli altri che l'utente ha lasciato
                Await Clients.OthersInGroup(roomId).SendAsync("UserLeft", Context.ConnectionId)
                _rooms.TryRemove(Context.ConnectionId, Nothing)

                ' AGGIORNA LA LISTA PER TUTTI
                Await BroadcastParticipantsList(roomId)

                _connections.TryRemove(Context.ConnectionId, Nothing)

                Debug.Print($"Client disconnected: {Context.ConnectionId}")

            Catch ex As Exception
                Debug.Print($"Errore in OnDisconnectedAsync: {ex.Message}")
            End Try

            Await MyBase.OnDisconnectedAsync(exception)
        End Function

        'Public Async Function JoinRoom(roomId As String, userName As String) As Task
        '    ' Salva l'utente e la stanza
        '    _connections(Context.ConnectionId) = userName
        '    _rooms(Context.ConnectionId) = roomId

        '    Await Groups.AddToGroupAsync(Context.ConnectionId, roomId)

        '    ' Notifica agli altri nella stanza
        '    Await Clients.OthersInGroup(roomId).SendAsync("UserJoined", Context.ConnectionId, userName)

        '    ' AGGIORNA LA LISTA PER TUTTI
        '    Await BroadcastParticipantsList(roomId)

        '    ' Restituisci gli utenti già presenti
        '    Dim usersInRoom = _connections.
        '        Where(Function(c) _rooms.ContainsKey(c.Key) AndAlso
        '                           _rooms(c.Key) = roomId AndAlso
        '                           c.Key <> Context.ConnectionId).
        '        Select(Function(c) New With {
        '            .ConnectionId = c.Key,
        '            .UserName = c.Value
        '        }).ToList()

        '    'Await Clients.Caller.SendAsync("ExistingUsers", usersInRoom)
        '    Await Clients.Caller.SendAsync("ExistingUsers", usersInRoom) ' Dove usersInRoom è List(Of UserInfo)
        'End Function

        Public Async Function JoinRoom(roomId As String, userName As String) As Task
            ' Salva l'utente e la stanza
            _connections(Context.ConnectionId) = userName
            _rooms(Context.ConnectionId) = roomId

            ' Inizializza stato utente
            _userStatus(Context.ConnectionId) = New UserStatus With {
                                                                    .UserName = userName,
                                                                    .HasVideo = False,
                                                                    .HasAudio = False,
                                                                    .IsScreenSharing = False
                                                                    }

            Await Groups.AddToGroupAsync(Context.ConnectionId, roomId)

            ' Notifica agli altri nella stanza
            Await Clients.Groups(roomId).SendAsync("UserJoined", Context.ConnectionId, userName)

            ' Ottieni la lista degli utenti escluso il nuovo
            Dim existingUsersList = GetParticipantsInRoom(roomId) ', Context.ConnectionId)

            ' AGGIORNA LA LISTA PER TUTTI
            Await BroadcastParticipantsList(roomId)

            Await Clients.Caller.SendAsync("ExistingUsers", existingUsersList)

        End Function

        Private Function GetParticipantsInRoom(roomId As String, Optional excludeConnectionId As String = Nothing) As List(Of UserInfo)
            Dim result = New List(Of UserInfo)()

            For Each conn In _rooms.Where(Function(r) r.Value = roomId AndAlso
                                         (excludeConnectionId Is Nothing OrElse r.Key <> excludeConnectionId))
                Dim status = _userStatus.GetValueOrDefault(conn.Key)
                result.Add(New UserInfo With {
            .ConnectionId = conn.Key,
            .UserName = _connections.GetValueOrDefault(conn.Key, "Utente"),
            .HasVideo = If(status IsNot Nothing, status.HasVideo, False),
            .HasAudio = If(status IsNot Nothing, status.HasAudio, False),
            .IsScreenSharing = If(status IsNot Nothing, status.IsScreenSharing, False)
        })
            Next

            Return result
        End Function

        Public Async Function SendOffer(roomId As String, targetConnectionId As String, offer As Object) As Task
            Await Clients.Client(targetConnectionId).SendAsync("ReceiveOffer", Context.ConnectionId, offer)
        End Function

        Public Async Function SendAnswer(roomId As String, targetConnectionId As String, answer As Object) As Task
            Await Clients.Client(targetConnectionId).SendAsync("ReceiveAnswer", Context.ConnectionId, answer)
        End Function

        Public Async Function SendIceCandidate(roomId As String, targetConnectionId As String, candidate As Object) As Task
            Await Clients.Client(targetConnectionId).SendAsync("ReceiveIceCandidate", Context.ConnectionId, candidate)
        End Function

        Public Async Function SendVideoFrame(roomId As String, targetConnectionId As String, frameData As Byte(), width As Integer, height As Integer) As Task
            Await Clients.Client(targetConnectionId).SendAsync("ReceiveVideoFrame", Context.ConnectionId, frameData, width, height)
        End Function

        Public Async Function SendVideoFrameToAll(roomId As String, frameData As Byte(), width As Integer, height As Integer) As Task
            Await Clients.OthersInGroup(roomId).SendAsync("ReceiveVideoFrame", Context.ConnectionId, frameData, width, height)
        End Function

        Public Async Function SendAudioData(roomId As String, targetConnectionId As String, audioData As Byte()) As Task
            'Await Clients.Client(targetConnectionId).SendAsync("ReceiveAudioData", Context.ConnectionId, audioData)

            Try
                If audioData Is Nothing OrElse audioData.Length = 0 Then
                    Debug.Print("SendAudioData: audioData nullo o vuoto")
                    Return
                End If

                Debug.Print($"SendAudioData: Invio {audioData.Length} bytes a {targetConnectionId}")

                Await Clients.Client(targetConnectionId).SendAsync("ReceiveAudioData", Context.ConnectionId, audioData)

            Catch ex As Exception
                Debug.Print($"Errore in SendAudioData: {ex.Message}")
            End Try

        End Function

        Public Async Function SendAudioDataToAll(roomId As String, audioData As Byte()) As Task
            'Await Clients.OthersInGroup(roomId).SendAsync("ReceiveAudioData", Context.ConnectionId, audioData)
            Try
                If audioData Is Nothing OrElse audioData.Length = 0 Then
                    Debug.Print("SendAudioDataToAll: audioData nullo o vuoto")
                    Return
                End If

                Debug.Print($"SendAudioDataToAll: Invio {audioData.Length} bytes a tutti nella stanza {roomId}")

                Await Clients.OthersInGroup(roomId).SendAsync("ReceiveAudioData",
                    Context.ConnectionId, audioData)

            Catch ex As Exception
                Debug.Print($"Errore in SendAudioDataToAll: {ex.Message}")
            End Try

        End Function

        Public Async Function SendScreenFrame(roomId As String, targetConnectionId As String, frameData As Byte(), width As Integer, height As Integer) As Task
            Try
                If frameData Is Nothing OrElse frameData.Length = 0 Then
                    Debug.Print("SendScreenFrame: frameData nullo o vuoto")
                    Return
                End If

                Debug.Print($"SendScreenFrame: Invio frame {width}x{height} ({frameData.Length} bytes) a {targetConnectionId}")

                Await Clients.Client(targetConnectionId).SendAsync("ReceiveScreenFrame",
                    Context.ConnectionId,
                    frameData,
                    width,
                    height)

            Catch ex As Exception
                Debug.Print($"Errore in SendScreenFrame: {ex.Message}")
            End Try
        End Function

        Public Async Function SendScreenFrameToAll(roomId As String, frameData As Byte(), width As Integer, height As Integer) As Task
            Try
                If frameData Is Nothing OrElse frameData.Length = 0 Then
                    Debug.Print("SendScreenFrameToAll: frameData nullo o vuoto")
                    Return
                End If

                Debug.Print($"SendScreenFrameToAll: Invio frame {width}x{height} ({frameData.Length} bytes) a tutti nella stanza {roomId}")

                Await Clients.OthersInGroup(roomId).SendAsync("ReceiveScreenFrame",
                    Context.ConnectionId,
                    frameData,
                    width,
                    height)

            Catch ex As Exception
                Debug.Print($"Errore in SendScreenFrameToAll: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Invia un messaggio chat a tutti gli altri nella stanza
        ''' </summary>
        Public Async Function SendChatMessage(roomId As String, userName As String, message As String) As Task
            Try
                If String.IsNullOrEmpty(message) Then
                    Debug.Print("SendChatMessage: messaggio vuoto")
                    Return
                End If

                Debug.Print($"SendChatMessage: {userName} in {roomId}: {message}")

                ' Invia a tutti gli altri nella stanza
                Await Clients.OthersInGroup(roomId).SendAsync("ReceiveChatMessage",
                    userName,
                    message,
                    DateTime.Now)

                ' Opzionale: Invia anche al mittente per conferma (echo)
                'Await Clients.Caller.SendAsync("ReceiveChatMessage", userName, message, DateTime.Now)

            Catch ex As Exception
                Debug.Print($"Errore in SendChatMessage: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Invia un messaggio privato a un utente specifico
        ''' </summary>
        Public Async Function SendPrivateMessage(targetConnectionId As String, userName As String, message As String) As Task
            Try
                If String.IsNullOrEmpty(message) Then Return

                Debug.Print($"SendPrivateMessage: {userName} -> {targetConnectionId}: {message}")

                Await Clients.Client(targetConnectionId).SendAsync("ReceivePrivateMessage",
                    userName,
                    message,
                    DateTime.Now)

            Catch ex As Exception
                Debug.Print($"Errore in SendPrivateMessage: {ex.Message}")
            End Try
        End Function

        ' ========== METODI PER STATO PARTECIPANTI ==========

        ''' <summary>
        ''' Aggiorna lo stato video/audio di un partecipante
        ''' </summary>
        Public Async Function UpdateParticipantStatus(roomId As String, hasVideo As Boolean, hasAudio As Boolean) As Task
            Try
                ' Aggiorna lo stato nella memoria
                Dim status = _userStatus.GetOrAdd(Context.ConnectionId, New UserStatus())
                status.HasVideo = hasVideo
                status.HasAudio = hasAudio
                status.UserName = _connections.GetValueOrDefault(Context.ConnectionId, "Utente")

                Debug.Print($"UpdateParticipantStatus: {Context.ConnectionId} - Video:{hasVideo}, Audio:{hasAudio}")

                '' Notifica tutti gli altri nella stanza
                'Await Clients.OthersInGroup(roomId).SendAsync("ParticipantStatusChanged",
                '    Context.ConnectionId,
                '    hasVideo,
                '    hasAudio)

                ' Notifica tutti (incluso il mittente) del cambiamento
                Await BroadcastParticipantsList(roomId)

            Catch ex As Exception
                Debug.Print($"Errore in UpdateParticipantStatus: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Aggiorna lo stato di condivisione schermo
        ''' </summary>
        Public Async Function UpdateScreenSharingStatus(roomId As String, isSharing As Boolean) As Task
            Try
                Dim status = _userStatus.GetOrAdd(Context.ConnectionId, New UserStatus())
                status.IsScreenSharing = isSharing

                Debug.Print($"UpdateScreenSharingStatus: {Context.ConnectionId} - Sharing:{isSharing}")

                ' Notifica tutti (incluso il mittente) del cambiamento
                Await BroadcastParticipantsList(roomId)

            Catch ex As Exception
                Debug.Print($"Errore in UpdateScreenSharingStatus: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Ottiene la lista di tutti i partecipanti con i loro stati
        ''' </summary>
        Public Function GetParticipants(roomId As String) As List(Of Object)
            Try
                Dim participants = New List(Of Object)()

                For Each conn In _rooms.Where(Function(r) r.Value = roomId AndAlso r.Key <> Context.ConnectionId)
                    Dim status = _userStatus.GetValueOrDefault(conn.Key)
                    participants.Add(New With {
                        Key .ConnectionId = conn.Key,
                        Key .UserName = _connections.GetValueOrDefault(conn.Key, "Utente"),
                        Key .HasVideo = If(status IsNot Nothing, status.HasVideo, False),
                        Key .HasAudio = If(status IsNot Nothing, status.HasAudio, False),
                        Key .IsScreenSharing = If(status IsNot Nothing, status.IsScreenSharing, False)
                    })
                Next

                Return participants

            Catch ex As Exception
                Debug.Print($"Errore in GetParticipants: {ex.Message}")
                Return New List(Of Object)()
            End Try
        End Function

        Private Async Function BroadcastParticipantsList(roomId As String) As Task
            Try
                Dim participants = GetParticipantsInRoom(roomId)

                ' Invia a TUTTI i client nella stanza
                Await Clients.Group(roomId).SendAsync("ParticipantsList", participants)

                Debug.Print($"BroadcastParticipantsList: {participants.Count} partecipanti in stanza {roomId}")

            Catch ex As Exception
                Debug.Print($"Errore in BroadcastParticipantsList: {ex.Message}")
            End Try
        End Function

        Public Async Function StartScreenShare(roomId As String) As Task(Of Boolean)
            Try
                ' Verifica se qualcuno sta già condividendo in questa stanza
                If _activeScreenSharer.ContainsKey(roomId) Then
                    Dim currentSharer = _activeScreenSharer(roomId)
                    Debug.Print($"StartScreenShare: {roomId} già condiviso da {currentSharer}")

                    ' Notifica al chiamante che non può condividere
                    Await Clients.Caller.SendAsync("ScreenShareBlocked", "Qualcun altro sta già condividendo lo schermo")
                    Return False
                End If

                ' Registra questo utente come screen sharer
                _activeScreenSharer(roomId) = Context.ConnectionId

                ' Aggiorna stato utente
                If _userStatus.ContainsKey(Context.ConnectionId) Then
                    _userStatus(Context.ConnectionId).IsScreenSharing = True
                End If

                Debug.Print($"StartScreenShare: {Context.ConnectionId} sta condividendo in stanza {roomId}")

                ' Notifica a tutti (incluso il mittente) che lo screenshare è iniziato
                Await Clients.Group(roomId).SendAsync("ScreenShareStarted", Context.ConnectionId)

                ' Aggiorna lista partecipanti
                Await BroadcastParticipantsList(roomId)

                Return True

            Catch ex As Exception
                Debug.Print($"Errore in StartScreenShare: {ex.Message}")
                Return False
            End Try
        End Function

        Public Async Function StopScreenShare(roomId As String) As Task
            'Try
            '    Debug.Print($"StopScreenShare: {Context.ConnectionId} ha fermato la condivisione in stanza {roomId}")

            '    ' Notifica a tutti gli altri che lo screenshare è terminato
            '    Await Clients.OthersInGroup(roomId).SendAsync("ScreenShareStopped", Context.ConnectionId)

            '    ' Aggiorna lo stato utente
            '    If _userStatus.ContainsKey(Context.ConnectionId) Then
            '        _userStatus(Context.ConnectionId).IsScreenSharing = False
            '    End If

            '    ' Opzionale: aggiorna la lista partecipanti
            '    Await BroadcastParticipantsList(roomId)

            'Catch ex As Exception
            '    Debug.Print($"Errore in StopScreenShare: {ex.Message}")
            'End Try
            Try
                ' Verifica che questo utente stia effettivamente condividendo
                Dim sharer As String = Nothing
                If _activeScreenSharer.TryGetValue(roomId, sharer) AndAlso sharer = Context.ConnectionId Then
                    _activeScreenSharer.TryRemove(roomId, Nothing)

                    Debug.Print($"StopScreenShare: {Context.ConnectionId} ha fermato condivisione in stanza {roomId}")

                    ' Aggiorna stato utente
                    If _userStatus.ContainsKey(Context.ConnectionId) Then
                        _userStatus(Context.ConnectionId).IsScreenSharing = False
                    End If

                    ' Notifica a tutti (incluso il mittente) che lo screenshare è finito
                    Await Clients.Group(roomId).SendAsync("ScreenShareStopped", Context.ConnectionId)

                    ' Aggiorna lista partecipanti
                    Await BroadcastParticipantsList(roomId)
                End If

            Catch ex As Exception
                Debug.Print($"Errore in StopScreenShare: {ex.Message}")
            End Try
        End Function

        Public Async Function SendCursorPosition(roomId As String, x As Integer, y As Integer) As Task
            Try
                ' Invia a tutti gli altri nella stanza
                Await Clients.OthersInGroup(roomId).SendAsync("CursorPosition", Context.ConnectionId, x, y)
                Debug.Print($"Cursor position sent: ({x}, {y})")
            Catch ex As Exception
                Debug.Print($"Errore in SendCursorPosition: {ex.Message}")
            End Try
        End Function

        Public Async Function RequestScreenDimensions(roomId As String, targetConnectionId As String) As Task
            Try
                Await Clients.Client(targetConnectionId).SendAsync("SendScreenDimensions", Context.ConnectionId)
            Catch ex As Exception
                Debug.Print($"Errore in RequestScreenDimensions: {ex.Message}")
            End Try
        End Function

        Public Async Function SendScreenDimensions(roomId As String, width As Integer, height As Integer) As Task
            Try
                Await Clients.OthersInGroup(roomId).SendAsync("ScreenDimensions", width, height)
            Catch ex As Exception
                Debug.Print($"Errore in SendScreenDimensions: {ex.Message}")
            End Try
        End Function

        Public Async Function StopVideo(roomId As String) As Task
            Try
                Debug.Print($"🛑 SERVER: StopVideo da {Context.ConnectionId} in stanza {roomId}")

                ' Notifica a tutti gli altri che il video è terminato
                Await Clients.OthersInGroup(roomId).SendAsync("VideoStopped", Context.ConnectionId)

                ' Aggiorna stato utente
                If _userStatus.ContainsKey(Context.ConnectionId) Then
                    _userStatus(Context.ConnectionId).HasVideo = False
                End If

                ' Opzionale: aggiorna lista partecipanti
                Await BroadcastParticipantsList(roomId)

            Catch ex As Exception
                Debug.Print($"❌ SERVER Errore in StopVideo: {ex.Message}")
            End Try
        End Function

    End Class

    Public Class UserInfo
        Public Property ConnectionId As String
        Public Property UserName As String
        Public Property HasVideo As Boolean
        Public Property HasAudio As Boolean
        Public Property IsScreenSharing As Boolean
    End Class

End Namespace