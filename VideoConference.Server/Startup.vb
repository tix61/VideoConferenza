Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Logging
Imports VideoConference.Server.Hubs

Public Class Startup
    Public Sub ConfigureServices(services As IServiceCollection)
        ' SignalR con ottimizzazioni
        services.AddSignalR(Function(options)
                                options.EnableDetailedErrors = True
                                options.MaximumReceiveMessageSize = 1024 * 1024 ' 1MB
                                options.StreamBufferCapacity = 10
                            End Function)

        ' MVC per dashboard
        services.AddControllersWithViews()

        ' CORS per client esterni
        services.AddCors(Function(options)
                             options.AddPolicy("AllowAll",
                                 Function(policy)
                                     policy.AllowAnyHeader()
                                     policy.AllowAnyMethod()
                                     policy.SetIsOriginAllowed(Function(origin) True)
                                     policy.AllowCredentials()
                                 End Function)
                         End Function)

        ' Sessioni e caching
        services.AddDistributedMemoryCache()
        services.AddSession(Function(options)
                                options.IdleTimeout = TimeSpan.FromMinutes(20)
                                options.Cookie.HttpOnly = True
                                options.Cookie.IsEssential = True
                            End Function)

        ' Servizi personalizzati
        services.AddSingleton(Of MonitoringService)()

        ' Health checks per IIS
        services.AddHealthChecks()
    End Sub

    Public Sub Configure(app As IApplicationBuilder, env As IWebHostEnvironment, logger As ILogger(Of Startup))
        If env.IsDevelopment() Then
            app.UseDeveloperExceptionPage()
        Else
            app.UseExceptionHandler("/Home/Error")
            app.UseHsts() ' HTTP Strict Transport Security
        End If

        ' Middleware in ordine corretto
        app.UseHttpsRedirection()
        app.UseStaticFiles() ' Per file CSS/JS della dashboard

        app.UseRouting()

        ' CORS deve essere tra Routing e Authorization
        app.UseCors("AllowAll")

        app.UseSession()

        app.UseAuthorization()

        ' Health checks endpoint
        app.UseHealthChecks("/health")

        app.UseEndpoints(Sub(endpoints)
                             ' Hub SignalR
                             endpoints.MapHub(Of ConferenceHub)("/conferencehub")

                             ' Dashboard MVC
                             endpoints.MapControllerRoute(
                                 name:="default",
                                 pattern:="{controller=Dashboard}/{action=Index}/{id?}")

                             ' Endpoint per monitoring
                             endpoints.MapGet("/api/info", Async Function(context) As Task
                                                               Await context.Response.WriteAsJsonAsync(New With {
                                                                   .Server = "VideoConference Server",
                                                                   .Version = "1.0.0",
                                                                   .Time = DateTime.Now,
                                                                   .Machine = Environment.MachineName
                                                               })
                                                           End Function)
                         End Sub)

        logger.LogInformation("✅ Server avviato correttamente")
    End Sub
End Class
