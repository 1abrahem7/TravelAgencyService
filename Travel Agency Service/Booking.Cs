using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class Booking
    {
        public int Id { get; set; }
        [Required] public string UserId { get; set; }
        [Required] public int TripId { get; set; }
        public DateTime BookingDate { get; set; }
        public bool Paid { get; set; }
        public DateTime? PaymentDate { get; set; }

        // store minimal payment reference (no card number)
        public string PaymentReference { get; set; }

        public bool Cancelled { get; set; }
        public DateTime? CancelledDate { get; set; }

        // Navigation
        public Trip Trip { get; set; }
    }
}
