Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.AspNetCore.Hosting
Imports VideoConference.Shared.Models

'<Route("api")>
<ApiController>
Public Class DashboardController
    Inherits Controller

    Private ReadOnly _monitoring As MonitoringService
    Private ReadOnly _environment As IWebHostEnvironment

    Public Sub New(monitoring As MonitoringService)
        _monitoring = monitoring
    End Sub

    <HttpGet("stats")>
    Public Function GetStats() As IActionResult
        Try
            Dim data = _monitoring.GetDashboardData()

            Return Ok(New With {
                    .totalRooms = data.TotalRooms,
                    .totalUsers = data.TotalUsers,
                    .rooms = data.Stanze.Select(Function(r) New With {
                        .roomId = r.RoomId,
                        .userCount = r.UserCount,
                        .users = r.Users.Select(Function(u) New With {
                            .name = u.UserName,
                            .video = u.HasVideo,
                            .audio = u.HasAudio,
                            .screen = u.IsScreenSharing
                        })
                    }),
                    .logs = data.LogMessages
                })

        Catch ex As Exception
            Return StatusCode(500, New With {.error = ex.Message})
        End Try
    End Function

    <HttpGet("dashboard")>
    Public Function Index() As IActionResult
        Dim data = _monitoring.GetDashboardData()
        Return View(data)
    End Function

    <HttpGet("ping")>
    Public Function Ping() As IActionResult
        Return Ok(New With {.message = "pong", .time = DateTime.Now})
    End Function

End Class

