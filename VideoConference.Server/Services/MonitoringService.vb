Imports VideoConference.Shared.Models
Imports System.Collections.Concurrent
Imports System.Threading

Public Class MonitoringService
    Private Shared ReadOnly _rooms As New ConcurrentDictionary(Of String, StanzaInfo)()
    Private Shared ReadOnly _logMessages As New ConcurrentQueue(Of String)
    Private Shared ReadOnly _maxLogMessages As Integer = 1000
    Private Shared ReadOnly _lock As New Object()

    Public Sub AddOrUpdateRoom(roomId As String, connectionId As String, userName As String)
        SyncLock _lock
            Dim stanza = _rooms.GetOrAdd(roomId, Function(key) New StanzaInfo With {
                .RoomId = key,
                .CreatedAt = DateTime.Now,
                .Users = New List(Of UserInfo)()
            })

            ' Rimuovi utente se già presente (per aggiornamento)
            Dim existingUser = stanza.Users.FirstOrDefault(Function(u) u.ConnectionId = connectionId)
            If existingUser IsNot Nothing Then
                stanza.Users.Remove(existingUser)
            End If

            ' Aggiungi nuovo utente
            stanza.Users.Add(New UserInfo With {
                .ConnectionId = connectionId,
                .UserName = userName,
                .JoinedAt = DateTime.Now
            })

            stanza.UserCount = stanza.Users.Count
        End SyncLock
    End Sub

    Public Sub RemoveUser(connectionId As String, roomId As String)
        SyncLock _lock
            Dim stanza As StanzaInfo
            If _rooms.TryGetValue(roomId, stanza) Then
                Dim user = stanza.Users.FirstOrDefault(Function(u) u.ConnectionId = connectionId)
                If user IsNot Nothing Then
                    stanza.Users.Remove(user)
                    stanza.UserCount = stanza.Users.Count
                End If

                ' Rimuovi stanza se vuota
                If stanza.UserCount = 0 Then
                    _rooms.TryRemove(roomId, Nothing)
                End If
            End If
        End SyncLock
    End Sub

    Public Sub UpdateUserStatus(connectionId As String, roomId As String, hasVideo As Boolean, hasAudio As Boolean, isScreenSharing As Boolean)
        SyncLock _lock
            Dim stanza As StanzaInfo
            If _rooms.TryGetValue(roomId, stanza) Then
                Dim user = stanza.Users.FirstOrDefault(Function(u) u.ConnectionId = connectionId)
                If user IsNot Nothing Then
                    user.HasVideo = hasVideo
                    user.HasAudio = hasAudio
                    user.IsScreenSharing = isScreenSharing
                End If
            End If
        End SyncLock
    End Sub

    Public Function GetDashboardData() As DashboardViewModel
        SyncLock _lock
            Return New DashboardViewModel With {
            .Stanze = _rooms.Values.Select(Function(r) New StanzaInfo With {
                .RoomId = r.RoomId,
                .UserCount = r.UserCount,
                .CreatedAt = r.CreatedAt,
                .Users = r.Users.Select(Function(u) New UserInfo With {
                    .ConnectionId = u.ConnectionId,
                    .UserName = u.UserName,
                    .HasVideo = u.HasVideo,
                    .HasAudio = u.HasAudio,
                    .IsScreenSharing = u.IsScreenSharing,
                    .JoinedAt = u.JoinedAt
                }).ToList()
            }).OrderByDescending(Function(r) r.CreatedAt).ToList(),
            .LogMessages = _logMessages.Reverse().ToList(),
            .TotalRooms = _rooms.Count,
            .TotalUsers = _rooms.Values.Sum(Function(r) r.UserCount)
            }
            'Return New DashboardViewModel With {
            '    .Stanze = _rooms.Values.OrderByDescending(Function(r) r.CreatedAt).ToList(),
            '    .LogMessages = _logMessages.Reverse().ToList(),
            '    .TotalRooms = _rooms.Count,
            '    .TotalUsers = _rooms.Values.Sum(Function(r) r.UserCount)
            '}
        End SyncLock
    End Function

    Public Sub AddLogMessage(message As String)
        _logMessages.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}")
        While _logMessages.Count > _maxLogMessages
            _logMessages.TryDequeue(Nothing)
        End While
    End Sub
End Class
