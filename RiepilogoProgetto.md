# VideoConference - Stato del Progetto (Marzo 2026)

## 📁 STRUTTURA DEL PROGETTO

VideoConference/
├── VideoConference.Server/ # Server ASP.NET Core
│ ├── Program.vb
│ ├── Hubs/ConferenceHub.vb
│ ├── Services/MonitoringService.vb
│ ├── Controllers/DashboardController.vb
│ ├── Models/DashboardModels.cs
│ └── Views/Dashboard/Index.vbhtml
│
└── VideoConference.Client/ # Client WPF
├── MainWindow.xaml / .vb
├── VideoManager.vb
├── ScreenShareManager.vb
└── WebRTCManager.vb (in sviluppo)


### 2. **🛠 TECNOLOGIE UTILIZZATE**
- **.NET**: 8.0 (Server e Client)
- **SignalR**: Comunicazione real-time
- **Emgu.CV**: Cattura video (webcam e screen)
- **NAudio**: Gestione audio
- **WPF**: Interfaccia client
- **ASP.NET Core MVC**: Dashboard server

### 3. **✨ FUNZIONALITÀ IMPLEMENTATE**

#### Server
- [x] Hub SignalR con gestione stanze
- [x] MonitoringService per tracciamento utenti
- [x] Dashboard real-time (/dashboard)
- [x] API REST per statistiche (/api/stats)

#### Client
- [x] Connessione SignalR stabile
- [x] Video locale (Emgu.CV, 320x240 @15fps)
- [x] Video remoto funzionante
- [x] Screen sharing con cursore
- [x] Audio bidirezionale con noise gate
- [x] Chat testuale
- [x] Lista partecipanti con stati (video/audio/screen)
- [x] Layout a 3 colonne con collasso chat
- [x] Pulsanti unificati in alto

### 4. **🐛 PROBLEMI APERTI / DA RISOLVERE**

1. **Audio**: Quando si ferma l'audio, resetta anche lo stato screen sharing
2. **Echo cancellation**: Da migliorare (attualmente half-duplex)
3. **Icone screen**: Problema di visibilità risolto ma da verificare
4. **Filtri BiQuad**: Da perfezionare (producono NaN)
5. **Performance**: Ottimizzare CPU su inattività

### 5. **🎯 PROSSIMI PASSI (Priorità)**
1. ✅ Fix stato screen sharing quando ferma audio
2. ✅ Icone screen sempre visibili
3. [ ] Echo cancellation definitiva
4. [ ] Shortcut tastiera (Ctrl+D, Ctrl+M)
5. [ ] Selezione schermo/finestra per screen sharing
6. [ ] Messaggi privati in chat
7. [ ] Tema chiaro/scuro
8. [ ] Registrazione chiamate

### 6. **🔧 CODICE CRITICO DA CONDIVIDERE**

Client - ScreenShareManager (da migliorare)
' Aggiungere selezione finestra/monitor
Public Function StartScreenShare(Optional monitorIndex As Integer = 0) As Boolean

7. 📊 STATO PROGRESSI

    Core funzionalità: 80%

    UI/UX: 60%

    Audio: 70%

    Video: 80%

    Screen sharing: 85%

    Chat: 70%

    Server/Dashboard: 90%

8. 🔗 LINK UTILI

    Repository: https://github.com/tix61/VideoConferenza

    Issue aperte: [link]

    Documentazione API: /api/stats
	
	

