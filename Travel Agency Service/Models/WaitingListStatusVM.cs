using System;

namespace Travel_Agency_Service.Models
{
    public class WaitingListStatusVM
    {
        public int TripId { get; set; }
        public string TripTitle { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;

        public DateTime JoinedAt { get; set; }
        public bool Notified { get; set; }
        public DateTime? NotifiedAt { get; set; } // When the user was notified

        public int Position { get; set; }        // 1 = next
        public int TotalWaiting { get; set; }
    }
}
