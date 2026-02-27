using System;
using System.Collections.Generic;

namespace VideoConference.Shared.Models
{
    public class DashboardViewModel
    {
        public List<StanzaInfo> Stanze { get; set; }
        public List<string> LogMessages { get; set; }
        public int TotalUsers { get; set; }
        public int TotalRooms { get; set; }
    }

    public class StanzaInfo
    {
        public string RoomId { get; set; }
        public int UserCount { get; set; }
        public List<UserInfo> Users { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UserInfo
    {
        public string ConnectionId { get; set; }
        public string UserName { get; set; }
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public bool IsScreenSharing { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}