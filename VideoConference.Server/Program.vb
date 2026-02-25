Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.Extensions.DependencyInjection
Imports VideoConference.Server.Hubs

Module Program
    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        ' Configurazione per usare la porta 5000
        builder.WebHost.UseUrls("http://192.168.1.56:5000;https://192.168.1.56:5001")

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

        ' Configurazione SignalR con tutte le opzioni
        builder.Services.AddSignalR(Sub(options)
                                        ' Limite dimensione messaggi (20 MB)
                                        options.MaximumReceiveMessageSize = 20 * 1024 * 1024

                                        ' Capacità buffer streaming
                                        options.StreamBufferCapacity = 50

                                        ' Timeout keep-alive
                                        options.KeepAliveInterval = TimeSpan.FromSeconds(10)

                                        ' Timeout client
                                        options.ClientTimeoutInterval = TimeSpan.FromSeconds(120)

                                        ' Abilita compressione (se disponibile)
                                        options.EnableDetailedErrors = False
                                    End Sub)

        Dim app = builder.Build()

        app.UseCors("AllowAll")
        app.UseRouting()

        app.MapHub(Of ConferenceHub)("/conferencehub")
        app.MapGet("/", Function() "Video Conference Server is running!")

        app.Run()
    End Sub
End Module