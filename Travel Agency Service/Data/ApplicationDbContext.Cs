using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Travel_Agency_Service.Models;

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
        public DbSet<ServiceReview> ServiceReviews { get; set; }
        public DbSet<AdminSettings> AdminSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Trip>()
                .Property(t => t.RowVersion)
                .IsRowVersion();
        }
    }
}
