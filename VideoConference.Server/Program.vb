Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Razor.Language
Imports Microsoft.Extensions.DependencyInjection
Imports VideoConference.Server.Hubs

Module Program
    'Sub Main(args As String())
    '    Dim builder = WebApplication.CreateBuilder(args)

    '    ' Configurazione per usare la porta 5000
    '    builder.WebHost.UseUrls("http://192.168.1.56:5000;https://192.168.1.56:5001")

    '    ' Configurazione CORS
    '    builder.Services.AddCors(
    '        Sub(options)
    '            options.AddPolicy("AllowAll",
    '                Sub(policy)
    '                    policy.AllowAnyOrigin().
    '                           AllowAnyMethod().
    '                           AllowAnyHeader()
    '                End Sub)
    '        End Sub)

    '    ' Configurazione SignalR con tutte le opzioni
    '    builder.Services.AddSignalR(Sub(options)
    '                                    ' Limite dimensione messaggi (20 MB)
    '                                    options.MaximumReceiveMessageSize = 20 * 1024 * 1024

    '                                    ' Capacità buffer streaming
    '                                    options.StreamBufferCapacity = 50

    '                                    ' Timeout keep-alive
    '                                    options.KeepAliveInterval = TimeSpan.FromSeconds(10)

    '                                    ' Timeout client
    '                                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(120)

    '                                    ' Abilita compressione (se disponibile)
    '                                    options.EnableDetailedErrors = False
    '                                End Sub)

    '    Dim app = builder.Build()

    '    app.UseCors("AllowAll")
    '    app.UseRouting()

    '    app.MapHub(Of ConferenceHub)("/conferencehub")
    '    app.MapGet("/", Function() "Video Conference Server is running!")

    '    app.Run()
    'End Sub

    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        ' Configurazione per usare la porta 5000
        builder.WebHost.UseUrls("http://192.168.1.4:5000;https://192.168.1.4:5001")

        ' Aggiungi servizi MVC e monitoraggio
        'builder.Services.AddControllersWithViews()
        builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation()
        builder.Services.AddSingleton(Of MonitoringService)()
        builder.Services.AddMvc().AddRazorOptions(Sub(options)
                                                      ' Aggiungi l'estensione .vbhtml alle location cercate
                                                      options.ViewLocationFormats.Add("/Views/{1}/{0}.vbhtml")
                                                      options.ViewLocationFormats.Add("/Views/Shared/{0}.vbhtml")
                                                  End Sub)

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

        ' Configura pipeline HTTP
        app.UseStaticFiles()  ' Per servire file CSS, JS, immagini
        app.UseCors("AllowAll")
        app.UseRouting()

        app.UseAuthorization()

        app.MapControllers()

        ' Mappa gli endpoint
        app.MapHub(Of ConferenceHub)("/conferencehub")

        ' Route specifica per dashboard
        app.MapControllerRoute(
                                name:="Dashboard",
                                pattern:="Dashboard/{action=Index}/{id?}",
                                defaults:=New With {.controller = "Dashboard"}
                            )

        ' Endpoint di test
        app.MapGet("/", Function() "Video Conference Server is running! Vai a /Dashboard per la console di monitoraggio")

        app.MapGet("/test", Function() "Server funziona!")

        app.MapGet("/routes", Function() As String
                                  Dim routes = String.Join(", ", app.Urls)
                                  Return $"URLs attive: {routes}"
                              End Function)

        ' Log di avvio
        Dim separatore As String = New String("="c, 60)
        Console.WriteLine(separatore)
        Console.WriteLine("🎥 VideoConference Server")
        Console.WriteLine(separatore)
        Console.WriteLine($"🌐 Server avviato su:")
        Console.WriteLine($"   - http://192.168.1.56:5000")
        Console.WriteLine($"   - https://192.168.1.56:5001")
        Console.WriteLine()
        Console.WriteLine($"📊 Dashboard monitoraggio:")
        Console.WriteLine($"   - http://192.168.1.56:5000/dashboard")
        Console.WriteLine($"   - https://192.168.1.56:5001/dashboard")
        Console.WriteLine()
        Console.WriteLine($"🔌 SignalR Hub:")
        Console.WriteLine($"   - http://192.168.1.56:5000/conferencehub")
        Console.WriteLine($"   - https://192.168.1.56:5001/conferencehub")
        Console.WriteLine(separatore)

        app.Run()

    End Sub

End Module