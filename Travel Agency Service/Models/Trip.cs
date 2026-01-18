using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Travel_Agency_Service.Models
{
    public class Trip
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Trip Name")]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Destination { get; set; } = string.Empty;   // city or place

        [Required]
        public string Country { get; set; } = string.Empty;

        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Display(Name = "Available Rooms")]
        [Range(0, 1000)]
        public int AvailableRooms { get; set; }

        [Required]
        [Range(0, 999999)]
        [DataType(DataType.Currency)]
        public decimal Price { get; set; }        // current (maybe discounted)

        [Display(Name = "Old Price")]
        [DataType(DataType.Currency)]
        public decimal? OldPrice { get; set; }    // previous price, for strike-through

        [Display(Name = "Discount Active")]
        public bool IsDiscountActive { get; set; }

        [Display(Name = "Discount Expiry Date")]
        [DataType(DataType.Date)]
        public DateTime? DiscountExpiryDate { get; set; } // Max 1 week from activation

        [Display(Name = "Package Type")]
        public string PackageType { get; set; } = string.Empty;   // Family / Honeymoon / Adventure / Cruise / Luxury...

        [Display(Name = "Age Limit (min)")]
        public int? AgeLimit { get; set; }

        [Display(Name = "Short Description")]
        public string ShortDescription { get; set; } = string.Empty;

        [Display(Name = "Image URL")]
        public string ImageUrl { get; set; } = string.Empty;      // later: gallery

        [Range(0, 100000)]
        [Display(Name = "Popularity Score")]
        public int PopularityScore { get; set; }  // for “most popular”

        [Range(1900, 2100)]
        [Display(Name = "Departure Year")]
        public int DepartureYear { get; set; }

        [Display(Name = "Is Visible")]
        public bool IsVisible { get; set; } = true;  // Admin can hide/show trips from catalog

        // for concurrency (you already configured this in ApplicationDbContext)
        [Timestamp]
        public byte[]? RowVersion { get; set; }
        [NotMapped]
        public double? AverageRating { get; set; }

        [NotMapped]
        public int ReviewCount { get; set; }
    }
}
