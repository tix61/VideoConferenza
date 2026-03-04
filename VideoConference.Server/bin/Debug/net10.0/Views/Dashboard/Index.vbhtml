@using VideoConference.Shared.Models
@model DashboardViewModel
<!DOCTYPE html>
<html lang="it">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>VideoConference Server Dashboard</title>
    <link rel="stylesheet" href="/css/dashboard.css">
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
</head>
<body>
    <div class="dashboard">
        <header>
            <h1>📊 VideoConference Server Dashboard</h1>
            <div class="stats">
                <div class="stat-card">
                    <span class="stat-label">Stanze attive</span>
                    <span class="stat-value" id="totalRooms">@Model.TotalRooms</span>
                </div>
                <div class="stat-card">
                    <span class="stat-label">Utenti connessi</span>
                    <span class="stat-value" id="totalUsers">@Model.TotalUsers</span>
                </div>
            </div>
        </header>

        <div class="main-content">
            <div class="rooms-section">
                <h2>🏠 Stanze attive</h2>
                <div id="roomsContainer" class="rooms-grid">
                    @foreach (var stanza in Model.Stanze)
                    {
                        <div class="room-card" data-room="@stanza.RoomId">
                            <div class="room-header">
                                <h3>@stanza.RoomId</h3>
                                <span class="user-count">👥 @stanza.UserCount</span>
                            </div>
                            <div class="users-list">
                                @foreach (var user in stanza.Users)
                                {
                                    <div class="user-item">
                                        <span class="user-name">@user.UserName</span>
                                        <div class="user-status">
                                            @if (user.HasVideo)
                                            {
                                                <span class="status-badge video-active" title="Video attivo">🎥</span>
                                            }
                                            else
                                            {
                                                <span class="status-badge" title="Video disattivo">🎥</span>
                                            }
                                            @if (user.HasAudio)
                                            {
                                                <span class="status-badge audio-active" title="Audio attivo">🎤</span>
                                            }
                                            else
                                            {
                                                <span class="status-badge" title="Audio disattivo">🎤</span>
                                            }
                                            @if (user.IsScreenSharing)
                                            {
                                                <span class="status-badge screen-active" title="Condivisione schermo">🖥️</span>
                                            }
                                            else
                                            {
                                                <span class="status-badge" title="Condivisione schermo">🖥️</span>
                                            }
                                       </div>
                                    </div>
                                }
                            </div>
                        </div>
                    }
                </div>
            </div>

            <div class="logs-section">
                <h2>📋 Log Console</h2>
                <div id="logContainer" class="log-container">
                    @foreach (var log in Model.LogMessages)
                    {
                        <div class="log-entry">@log</div>
                    }
                </div>
            </div>
        </div>
    </div>

    <script src="/js/dashboard.js"></script>
</body>
</html>