Imports Microsoft.AspNetCore.Mvc

Namespace VideoConference.Server.Controllers
    <Route("api/test")>
    <ApiController>
    Public Class TestController
        Inherits ControllerBase

        <HttpGet>
        Public Function GetTest() As IActionResult
            Return Ok(New With {
                .message = "Funziona!",
                .timestamp = DateTime.Now
            })
        End Function
    End Class
End Namespace