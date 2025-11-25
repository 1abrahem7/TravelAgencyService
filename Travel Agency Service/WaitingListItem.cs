using System;

namespace Travel_Agency_Service.Models
{
    public class WaitingListItem
    {
        public int Id { get; set; }
        public int TripId { get; set; }
        public string UserId { get; set; }
        public DateTime JoinedAt { get; set; }
        public bool Notified { get; set; } // true when an email was sent
        public Trip Trip { get; set; }
    }
}
