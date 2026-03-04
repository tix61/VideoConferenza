Public Class DashboardViewModel
    Public Property Stanze As List(Of StanzaInfo)
    Public Property LogMessages As List(Of String)
    Public Property TotalUsers As Integer
    Public Property TotalRooms As Integer
End Class

Public Class StanzaInfo
    Public Property RoomId As String
    Public Property UserCount As Integer
    Public Property Users As List(Of UserInfo)
    Public Property CreatedAt As DateTime
End Class

Public Class UserInfo
    Public Property ConnectionId As String
    Public Property UserName As String
    Public Property HasVideo As Boolean
    Public Property HasAudio As Boolean
    Public Property IsScreenSharing As Boolean
    Public Property JoinedAt As DateTime
End Class
