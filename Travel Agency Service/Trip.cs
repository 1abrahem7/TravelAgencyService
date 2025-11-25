using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class Trip
    {
        public int Id { get; set; }

        [Required] public string Title { get; set; }
        [Required] public string Destination { get; set; }
        [Required] public string Country { get; set; }

        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }

        [Required] public decimal Price { get; set; }
        public decimal? PreviousPrice { get; set; } // for showing discount strikethrough
        public bool DiscountActive { get; set; }
        public DateTime? DiscountEndDate { get; set; } // discount limited to a week max (enforce in business logic)

        [Required] public int AvailableRooms { get; set; }
        [Required] public string PackageType { get; set; } // family, honeymoon, adventure, etc.
        public int? AgeLimit { get; set; } // optional

        public string ShortDescription { get; set; }
        public string FullDescription { get; set; }

        // Store image paths/URLs (multiple images possible)
        public string ImageUrl { get; set; } // for simplicity; can be extended to a separate TripImage entity

        // Concurrency token (byte[]) used to avoid race conditions when booking last room
        public byte[] RowVersion { get; set; }

        // Navigation properties
        public ICollection<Booking> Bookings { get; set; }
        public ICollection<Review> Reviews { get; set; }
    }
}
