using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class Review
    {
        public int Id { get; set; }

        [Required]
        public int TripId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(500)]
        public string Comment { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public Trip? Trip { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
