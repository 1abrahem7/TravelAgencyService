using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Travel_Agency_Service.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        // Optional: simple admin flag
        [NotMapped]
        public IList<string> Roles { get; set; } = new List<string>();


    }
}
