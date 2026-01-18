using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class Booking
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int TripId { get; set; }

        [Display(Name = "Booking Date")]
        public DateTime BookingDate { get; set; } = DateTime.Now;

        [Range(1, 20, ErrorMessage = "Number of people must be between 1 and 20.")]
        [Display(Name = "Number of People")]
        public int NumberOfPeople { get; set; } = 1;

        [DataType(DataType.Currency)]
        [Display(Name = "Total Price")]
        public decimal TotalPrice { get; set; }

        public bool Paid { get; set; }
        public DateTime? PaymentDate { get; set; }

        // auto-filled after payment
        public string PaymentReference { get; set; } = string.Empty;

        public bool Cancelled { get; set; }
        public DateTime? CancelledDate { get; set; }

        // Navigation
        public Trip? Trip { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
