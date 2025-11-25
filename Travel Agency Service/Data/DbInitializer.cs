using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Data
{
    public static class DbInitializer
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // If Trips already seeded – exit
            if (context.Trips.Any())
                return;

            var trips = new Trip[]
            {
                new Trip
                {
                    Title = "Rome Spring Escape",
                    Destination = "Rome",
                    Country = "Italy",
                    StartDate = new DateTime(2026, 4, 10),
                    EndDate = new DateTime(2026, 4, 17),
                    Price = 899.99m,
                    AvailableRooms = 10,
                    PackageType = "City Break",
                    ShortDescription = "Explore historic Rome.",
                    FullDescription = "A full guided week in Rome with museums, Vatican tour, and food experiences.",
                    ImageUrl = "/images/rome.jpg",
                    AgeLimit = 0
                },
                new Trip
                {
                    Title = "Desert Nights Adventure",
                    Destination = "Negev Desert",
                    Country = "Israel",
                    StartDate = new DateTime(2026, 6, 5),
                    EndDate = new DateTime(2026, 6, 8),
                    Price = 299.50m,
                    AvailableRooms = 20,
                    PackageType = "Adventure",
                    ShortDescription = "Camping and stargazing.",
                    FullDescription = "A unique desert experience with local guides, hiking, and stargazing nights.",
                    ImageUrl = "/images/negev.jpg",
                    AgeLimit = 12
                }
            };

            // Add the first two “real” trips
            context.Trips.AddRange(trips);
            context.SaveChanges();

            // Add 23 more automatic trips so total = 25
            for (int i = 0; i < 23; i++)
            {
                var t = new Trip
                {
                    Title = $"Sample Trip {i + 3}",
                    Destination = i % 2 == 0 ? "Paris" : "Barcelona",
                    Country = i % 2 == 0 ? "France" : "Spain",
                    StartDate = DateTime.Today.AddDays(20 + i),
                    EndDate = DateTime.Today.AddDays(25 + i),
                    Price = 450 + (i * 20),
                    PreviousPrice = 500 + (i * 20),
                    DiscountActive = (i % 5 == 0),       // every 5th trip has discount
                    DiscountEndDate = (i % 5 == 0) ? DateTime.Today.AddDays(7) : null,
                    AvailableRooms = 5 + (i % 8),
                    PackageType = (i % 3 == 0) ? "Family" : "Honeymoon",
                    AgeLimit = 0,
                    ShortDescription = "A sample generated trip.",
                    FullDescription = "Automatically generated trip for database seeding.",
                    ImageUrl = "/images/sample.jpg"
                };

                context.Trips.Add(t);
            }

            context.SaveChanges();
        }
    }
}
