using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class WaitingListItem
    {
        public int Id { get; set; }

        [Required]
        public int TripId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime JoinedAt { get; set; } = DateTime.Now;

        public bool Notified { get; set; } = false; // default
        
        public DateTime? NotifiedAt { get; set; } // When the user was notified

        // Navigation
        public Trip? Trip { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
