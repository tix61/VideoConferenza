Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Razor.Language
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports VideoConference.Server.Hubs
Imports System.Net
' Alias per System.Console per evitare ambiguità
Imports Console = System.Console
Imports Microsoft.AspNetCore.Http

Module Program
    Sub Main(args As String())
        Try
            Dim builder = WebApplication.CreateBuilder(args)

            ' 🚀 CONFIGURAZIONE PER IIS
            builder.WebHost.UseIIS()  ' Abilita IIS
            builder.WebHost.UseIISIntegration()  ' Integrazione IIS

            ' Usa URL diverse per sviluppo
            If builder.Environment.IsDevelopment() Then
                ' Usa porte diverse in sviluppo per evitare conflitti con IIS
                builder.WebHost.UseUrls("http://localhost:6000;https://localhost:6001")
                Console.WriteLine("🔧 MODALITÀ SVILUPPO: porte 6000/6001")
            End If

            ' Aggiungi servizi MVC e monitoraggio
            builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation().  ' Per sviluppo, in produzione puoi rimuovere
                            AddRazorOptions(Sub(options)
                                                ' Aggiungi l'estensione .vbhtml alle location cercate
                                                options.ViewLocationFormats.Add("/Views/{1}/{0}.vbhtml")
                                                options.ViewLocationFormats.Add("/Views/Shared/{0}.vbhtml")
                                            End Sub)

            ' Servizi personalizzati
            builder.Services.AddSingleton(Of MonitoringService)()

            ' Health checks per IIS (utile per monitoring)
            builder.Services.AddHealthChecks()

            ' Sessioni e caching (opzionale)
            builder.Services.AddDistributedMemoryCache()
            builder.Services.AddSession(Sub(options)
                                            options.IdleTimeout = TimeSpan.FromMinutes(20)
                                            options.Cookie.HttpOnly = True
                                            options.Cookie.IsEssential = True
                                        End Sub)

            ' Configurazione CORS - Più restrittiva per produzione
            builder.Services.AddCors(Sub(options)
                                         options.AddPolicy("AllowSpecificOrigins",
                                             Sub(policy)
                                                 ' In produzione, specifica i domini consentiti
                                                 If builder.Environment.IsDevelopment() Then
                                                     policy.AllowAnyOrigin()
                                                     policy.AllowAnyMethod()
                                                     policy.AllowAnyHeader()
                                                 Else
                                                     policy.WithOrigins(
                                                         "http://localhost:8080",
                                                         "http://192.168.1.56:8080",
                                                         "https://tuodominio.com"
                                                     )
                                                     policy.AllowAnyMethod()
                                                     policy.AllowAnyHeader()
                                                     policy.AllowCredentials()
                                                 End If
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

                                            ' Abilita errori dettagliati solo in development
                                            options.EnableDetailedErrors = builder.Environment.IsDevelopment()
                                        End Sub)

            ' Configura logging per IIS
            builder.Logging.ClearProviders()
            builder.Logging.AddConsole()
            builder.Logging.AddDebug()
            ' questo funziona solo su windows e se l'app ha i permessi per scrivere su Event Log
            builder.Logging.AddEventLog(Sub(options)
                                            options.SourceName = "VideoConference Server"
                                            options.LogName = "Application"
                                            options.MachineName = Environment.MachineName
                                        End Sub)

            ' Aggiungi Application Insights se disponibile (opzionale)
            ' builder.Services.AddApplicationInsightsTelemetry();

            Dim app = builder.Build()

            ' 🛡️ PIPELINE HTTP - Ordine corretto per IIS
            If app.Environment.IsDevelopment() Then
                app.UseDeveloperExceptionPage()
            Else
                app.UseExceptionHandler("/Home/Error")
                app.UseHsts() ' HTTP Strict Transport Security
            End If

            ' Middleware in ordine corretto
            app.UseHttpsRedirection()
            app.UseStaticFiles()  ' Per servire file CSS, JS, immagini
            app.UseRouting()

            app.UseCors("AllowSpecificOrigins")  ' CORS dopo routing

            app.UseSession()  ' Sessioni

            app.UseAuthorization()

            ' Health checks endpoint (utile per IIS)
            app.MapHealthChecks("/health")

            ' Endpoint API e MVC
            app.MapControllers()

            ' Mappa gli endpoint SignalR
            app.MapHub(Of ConferenceHub)("/conferencehub")

            ' Route specifica per dashboard
            app.MapControllerRoute(
                name:="Dashboard",
                pattern:="Dashboard/{action=Index}/{id?}",
                defaults:=New With {.controller = "Dashboard"}
            )

            ' Endpoint di test/info
            app.MapGet("/", Function() "Video Conference Server is running! Vai a /Dashboard per la console di monitoraggio")

            app.MapGet("/test", Function() "Server funziona!")

            app.MapGet("/routes", Function() As String
                                      Dim routes = String.Join(", ", app.Urls)
                                      Return $"URLs attive: {routes}"
                                  End Function)

            ' ENDPOINT CORRETTO - con HttpContext tipizzato
            app.MapGet("/api/info", Async Function(context As HttpContext) As Task
                                        Await context.Response.WriteAsJsonAsync(New With {
                                                    .Server = "VideoConference Server",
                                                    .Version = "1.0.0",
                                                    .Environment = app.Environment.EnvironmentName,
                                                    .Time = DateTime.Now,
                                                    .Machine = Environment.MachineName,
                                                    .OSVersion = Environment.OSVersion.ToString(),
                                                    .Processors = Environment.ProcessorCount,
                                                    .MemoryMB = GC.GetTotalMemory(False) / 1024 / 1024
                                                })
                                    End Function)

            ' Log di avvio (solo in console/development)
            If Not app.Environment.IsProduction() Then
                Dim separatore As String = New String("="c, 60)
                Console.WriteLine(separatore)
                Console.WriteLine("🎥 VideoConference Server")
                Console.WriteLine(separatore)
                Console.WriteLine($"🌍 Environment: {app.Environment.EnvironmentName}")
                Console.WriteLine($"💻 Machine Name: {Environment.MachineName}")
                Console.WriteLine($"🕐 Avviato il: {DateTime.Now}")
                Console.WriteLine()

                ' Mostra URL solo in development, in produzione le gestisce IIS
                Console.WriteLine($"🌐 URL in ascolto:")
                For Each url In app.Urls
                    Console.WriteLine($"   - {url}")
                Next

                Console.WriteLine()
                Console.WriteLine($"📊 Dashboard monitoraggio: /dashboard")
                Console.WriteLine($"🔌 SignalR Hub: /conferencehub")
                Console.WriteLine($"❤️ Health Check: /health")
                Console.WriteLine($"ℹ️ Info API: /api/info")
                Console.WriteLine(separatore)
            End If

            ' Avvia l'applicazione
            app.Run()

        Catch ex As Exception
            ' Log errori fatali
            Console.WriteLine($"❌ ERRORE FATALE: {ex.Message}")
            Console.WriteLine($"Stack Trace: {ex.StackTrace}")

            ' Scrivi su file di errore
            Try
                Dim errorLog = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt")
                IO.File.WriteAllText(errorLog,
                    $"Data: {DateTime.Now}{Environment.NewLine}" &
                    $"Errore: {ex.Message}{Environment.NewLine}" &
                    $"Stack: {ex.StackTrace}{Environment.NewLine}")
            Catch
                ' Ignora errori di scrittura log
            End Try

            ' In IIS, lascia che il modulo gestisca l'errore
            Throw
        End Try
    End Sub
End Module
