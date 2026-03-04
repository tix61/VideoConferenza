const connection = new signalR.HubConnectionBuilder()
    .withUrl("/conferencehub")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();

connection.start().then(() => {
    console.log("Connected to SignalR");
});

// Aggiorna dashboard ogni 2 secondi
setInterval(async () => {
    try {
        const response = await fetch('/api/stats');
        const data = await response.json();
        updateDashboard(data);
    } catch (error) {
        console.error('Error fetching stats:', error);
    }
}, 2000);

function updateDashboard(data) {
    // Aggiorna stats generali
    document.getElementById('totalRooms').textContent = data.totalRooms;
    document.getElementById('totalUsers').textContent = data.totalUsers;

    // Aggiorna stanze
    const roomsContainer = document.getElementById('roomsContainer');
    roomsContainer.innerHTML = '';

    data.rooms.forEach(room => {
        const roomCard = createRoomCard(room);
        roomsContainer.appendChild(roomCard);
    });

    // Aggiorna log
    const logContainer = document.getElementById('logContainer');
    logContainer.innerHTML = '';
    data.logs.slice(-50).forEach(log => {
        const logEntry = document.createElement('div');
        logEntry.className = 'log-entry';
        logEntry.textContent = log;
        logContainer.appendChild(logEntry);
    });

    // Auto-scroll in fondo
    logContainer.scrollTop = logContainer.scrollHeight;
}

function createRoomCard(room) {
    const div = document.createElement('div');
    div.className = 'room-card';
    div.setAttribute('data-room', room.roomId);

    let usersHtml = '';
    room.users.forEach(user => {
        usersHtml += `
            <div class="user-item">
                <span class="user-name">${user.name}</span>
                <div class="user-status">
                    <span class="status-badge ${user.video ? 'video-active' : ''}" title="Video">🎥</span>
                    <span class="status-badge ${user.audio ? 'audio-active' : ''}" title="Audio">🎤</span>
                    <span class="status-badge ${user.screen ? 'screen-active' : ''}" title="Screen">🖥️</span>

                </div>
            </div>
        `;
    });

    div.innerHTML = `
        <div class="room-header">
            <h3>${room.roomId}</h3>
            <span class="user-count">👥 ${room.userCount}</span>
        </div>
        <div class="users-list">
            ${usersHtml}
        </div>
    `;

    return div;
}