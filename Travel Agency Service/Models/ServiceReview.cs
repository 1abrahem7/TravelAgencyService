using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    /// <summary>
    /// Feedback about the overall booking/purchasing experience on the website
    /// (not tied to a specific trip).
    /// </summary>
    public class ServiceReview
    {
        public int Id { get; set; }

        [Required]
        [Range(1, 5)]
        [Display(Name = "Rating (1-5)")]
        public int Rating { get; set; }

        [Required]
        [MaxLength(500)]
        [Display(Name = "Comment")]
        public string Comment { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public ApplicationUser? User { get; set; }
    }
}