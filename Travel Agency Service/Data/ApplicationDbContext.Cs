using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Travel_Agency_Service.Models; // adjust namespace if your project uses TravelAgencyService

namespace Travel_Agency_Service.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Trip> Trips { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<WaitingListItem> WaitingList { get; set; }
        public DbSet<Review> Reviews { get; set; }

        // Add a simple seed method if you want initial data on migration
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Example: configure composite keys, relationships, concurrency tokens, etc.
            builder.Entity<Trip>()
                .Property(t => t.RowVersion)
                .IsRowVersion(); // concurrency token for booking race conditions
        }
    }
}
