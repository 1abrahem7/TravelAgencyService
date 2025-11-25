using Microsoft.AspNetCore.Identity;

namespace Travel_Agency_Service.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Add extra fields if needed (FullName, DateOfBirth, etc.)
        public string FullName { get; set; }
    }
}
