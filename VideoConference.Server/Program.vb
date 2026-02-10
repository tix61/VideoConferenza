Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.Extensions.DependencyInjection
Imports VideoConference.Server.Hubs

Module Program
    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        ' Configurazione per usare la porta 5000
        builder.WebHost.UseUrls("http://localhost:5000;https://localhost:5001")

        ' Configurazione CORS
        builder.Services.AddCors(
            Sub(options)
                options.AddPolicy("AllowAll",
                    Sub(policy)
                        policy.AllowAnyOrigin().
                               AllowAnyMethod().
                               AllowAnyHeader()
                    End Sub)
            End Sub)

        builder.Services.AddSignalR()

        Dim app = builder.Build()

        app.UseCors("AllowAll")
        app.UseRouting()

        app.MapHub(Of ConferenceHub)("/conferencehub")
        app.MapGet("/", Function() "Video Conference Server is running!")

        app.Run()
    End Sub
End Module