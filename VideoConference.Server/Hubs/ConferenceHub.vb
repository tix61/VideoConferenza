Imports Microsoft.AspNetCore.SignalR
Imports System.Collections.Concurrent

Namespace Hubs
    Public Class ConferenceHub
        Inherits Hub

        Private Shared ReadOnly _connections As New ConcurrentDictionary(Of String, String)()
        Private Shared ReadOnly _rooms As New ConcurrentDictionary(Of String, String)()

        Public Overrides Async Function OnConnectedAsync() As Task
            Console.WriteLine($"Client connected: {Context.ConnectionId}")
            Await MyBase.OnConnectedAsync()
        End Function

        Public Overrides Async Function OnDisconnectedAsync(exception As Exception) As Task
            _connections.TryRemove(Context.ConnectionId, Nothing)

            Dim roomId As String = Nothing
            If _rooms.TryGetValue(Context.ConnectionId, roomId) Then
                Await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId)
                _rooms.TryRemove(Context.ConnectionId, Nothing)

                ' Notifica agli altri nella stanza
                Await Clients.OthersInGroup(roomId).SendAsync("UserLeft", Context.ConnectionId)
            End If

            Await MyBase.OnDisconnectedAsync(exception)
        End Function

        Public Async Function JoinRoom(roomId As String, userName As String) As Task
            ' Salva l'utente e la stanza
            _connections(Context.ConnectionId) = userName
            _rooms(Context.ConnectionId) = roomId

            Await Groups.AddToGroupAsync(Context.ConnectionId, roomId)

            ' Notifica agli altri nella stanza
            Await Clients.OthersInGroup(roomId).SendAsync("UserJoined", Context.ConnectionId, userName)

            ' Restituisci gli utenti già presenti
            Dim usersInRoom = _connections.
                Where(Function(c) _rooms.ContainsKey(c.Key) AndAlso
                                   _rooms(c.Key) = roomId AndAlso
                                   c.Key <> Context.ConnectionId).
                Select(Function(c) New With {
                    .ConnectionId = c.Key,
                    .UserName = c.Value
                }).ToList()

            Await Clients.Caller.SendAsync("ExistingUsers", usersInRoom)
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
    End Class
End Namespace