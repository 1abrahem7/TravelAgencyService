using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class Review
    {
        public int Id { get; set; }
        [Required] public int TripId { get; set; }
        [Required] public string UserId { get; set; }
        public int Rating { get; set; } // 1-5
        public string Comment { get; set; }
        public DateTime CreatedAt { get; set; }

        public Trip Trip { get; set; }
    }
}
