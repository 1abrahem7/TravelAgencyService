using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // ✅ Apply migrations to create/update the database schema
            try
            {
                // Try to apply pending migrations
                await context.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                // Log but don't fail - we'll try to fix missing columns manually below
                // Check if it's a "table already exists" error - if so, that's OK
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage = ex.InnerException.Message;
                }
                
                if (errorMessage.Contains("already an object named") || 
                    errorMessage.Contains("already exists") ||
                    errorMessage.Contains("There is already an object") ||
                    ex.Message.Contains("already an object named") ||
                    ex.Message.Contains("already exists") ||
                    ex.Message.Contains("There is already an object"))
                {
                    Console.WriteLine($"Migration note: Some database objects already exist. This is normal if migrations were applied before.");
                }
                else
                {
                    Console.WriteLine($"Migration warning: {ex.Message}");
                }
            }

            // Always ensure missing tables exist (in case migration wasn't applied)
            try
            {
                // Check and create ServiceReviews table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ServiceReviews')
                    BEGIN
                        CREATE TABLE ServiceReviews (
                            Id int IDENTITY(1,1) PRIMARY KEY,
                            Rating int NOT NULL,
                            Comment nvarchar(500) NOT NULL,
                            UserId nvarchar(450) NOT NULL,
                            CreatedAt datetime2 NOT NULL,
                            FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE CASCADE
                        );
                        CREATE INDEX IX_ServiceReviews_UserId ON ServiceReviews(UserId);
                    END
                ");

                // Check and create AdminSettings table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AdminSettings')
                    BEGIN
                        CREATE TABLE AdminSettings (
                            Id int IDENTITY(1,1) PRIMARY KEY,
                            DaysBeforeTripLatestBooking int NOT NULL DEFAULT 7,
                            DaysBeforeTripCancellationDeadline int NOT NULL DEFAULT 5,
                            DaysBeforeTripReminder int NOT NULL DEFAULT 5,
                            MaxDiscountDurationDays int NOT NULL DEFAULT 7,
                            LastUpdated datetime2 NOT NULL
                        );
                    END
                ");

                // Check and add DiscountExpiryDate column if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Trips') AND name = 'DiscountExpiryDate')
                    BEGIN
                        ALTER TABLE Trips ADD DiscountExpiryDate datetime2 NULL;
                    END
                ");

                // Check and add NotifiedAt column to WaitingList table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WaitingList') AND name = 'NotifiedAt')
                    BEGIN
                        ALTER TABLE WaitingList ADD NotifiedAt datetime2 NULL;
                    END
                ");

                // Check and add WaitingListNotificationExpirationDays column to AdminSettings table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'AdminSettings')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AdminSettings') AND name = 'WaitingListNotificationExpirationDays')
                    BEGIN
                        ALTER TABLE AdminSettings ADD WaitingListNotificationExpirationDays int NOT NULL DEFAULT 3;
                    END
                ");

                // Check and add IsVisible column to Trips table if it doesn't exist
                await context.Database.ExecuteSqlRawAsync(@"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Trips')
                    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Trips') AND name = 'IsVisible')
                    BEGIN
                        ALTER TABLE Trips ADD IsVisible bit NOT NULL DEFAULT 1;
                    END
                ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not create missing tables/columns: {ex.Message}");
                // Continue - tables/columns may already exist or will be created by migration
            }

            // 1) Ensure roles exist
            string[] roleNames = { "Admin", "User" };

            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2) Ensure a default admin user exists
            string adminEmail = "admin@travel.com";
            string adminPassword = "Admin123!";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
                else
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    Console.WriteLine("Admin creation failed: " + errors);
                }
            }
            else
            {
                if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // 3) Seed trips - At least 25 trips required
            // Add trips that don't exist yet (to avoid duplicates if database already has some trips)
            var existingTripTitles = await context.Trips.Select(t => t.Title).ToListAsync();
            
            var tripsToAdd = new List<Trip>();
            
            // Define all trips
            var allTrips = new[]
            {
                    // Honeymoon Packages
                    new Trip {
                        Title = "Paris Honeymoon Package",
                        Destination = "Paris",
                        Country = "France",
                        StartDate = new DateTime(2026, 5, 10),
                        EndDate = new DateTime(2026, 5, 17),
                        AvailableRooms = 5,
                        Price = 1299.99m,
                        OldPrice = 1499.99m,
                        IsDiscountActive = true,
                        PackageType = "Honeymoon",
                        AgeLimit = 18,
                        ShortDescription = "Romantic week in Paris with city tours and Seine cruise.",
                        ImageUrl = "https://images.unsplash.com/photo-1502602898657-3e91760cbb34?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 90,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Venice Romantic Getaway",
                        Destination = "Venice",
                        Country = "Italy",
                        StartDate = new DateTime(2026, 6, 1),
                        EndDate = new DateTime(2026, 6, 8),
                        AvailableRooms = 8,
                        Price = 1499.00m,
                        OldPrice = 1699.00m,
                        IsDiscountActive = true,
                        PackageType = "Honeymoon",
                        AgeLimit = 18,
                        ShortDescription = "Gondola rides, romantic dinners, and beautiful canals.",
                        ImageUrl = "https://images.unsplash.com/photo-1514890547357-a9ee288728e0?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 85,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Maldives Luxury Honeymoon",
                        Destination = "Maldives",
                        Country = "Maldives",
                        StartDate = new DateTime(2026, 8, 15),
                        EndDate = new DateTime(2026, 8, 22),
                        AvailableRooms = 4,
                        Price = 3499.99m,
                        IsDiscountActive = false,
                        PackageType = "Honeymoon",
                        AgeLimit = 18,
                        ShortDescription = "Private overwater villa with all-inclusive dining and spa.",
                        ImageUrl = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 95,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Santorini Sunset Experience",
                        Destination = "Santorini",
                        Country = "Greece",
                        StartDate = new DateTime(2026, 7, 20),
                        EndDate = new DateTime(2026, 7, 27),
                        AvailableRooms = 6,
                        Price = 1799.50m,
                        OldPrice = 1999.00m,
                        IsDiscountActive = true,
                        PackageType = "Honeymoon",
                        AgeLimit = 18,
                        ShortDescription = "Breathtaking sunsets, white-washed buildings, and wine tasting.",
                        ImageUrl = "https://images.unsplash.com/photo-1570077188670-e3a8d69ac5ff?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 88,
                        DepartureYear = 2026
                    },
                    // Family Packages
                    new Trip {
                        Title = "Family Fun in Antalya",
                        Destination = "Antalya",
                        Country = "Turkey",
                        StartDate = new DateTime(2026, 7, 1),
                        EndDate = new DateTime(2026, 7, 8),
                        AvailableRooms = 0,
                        Price = 899.50m,
                        IsDiscountActive = false,
                        PackageType = "Family",
                        ShortDescription = "All-inclusive family resort with water park and activities.",
                        ImageUrl = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 120,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Disney World Family Adventure",
                        Destination = "Orlando",
                        Country = "United States",
                        StartDate = new DateTime(2026, 6, 15),
                        EndDate = new DateTime(2026, 6, 22),
                        AvailableRooms = 10,
                        Price = 2199.00m,
                        OldPrice = 2499.00m,
                        IsDiscountActive = true,
                        PackageType = "Family",
                        ShortDescription = "7-day Disney World tickets with hotel and breakfast included.",
                        ImageUrl = "https://images.unsplash.com/photo-1511994298241-608e28f14fde?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 150,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Costa Del Sol Family Resort",
                        Destination = "Marbella",
                        Country = "Spain",
                        StartDate = new DateTime(2026, 8, 1),
                        EndDate = new DateTime(2026, 8, 8),
                        AvailableRooms = 12,
                        Price = 1099.00m,
                        IsDiscountActive = false,
                        PackageType = "Family",
                        ShortDescription = "Beachfront resort with kids club, pools, and entertainment.",
                        ImageUrl = "https://images.unsplash.com/photo-1505142468610-359e7d316be0?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 110,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Croatia Family Beach Holiday",
                        Destination = "Dubrovnik",
                        Country = "Croatia",
                        StartDate = new DateTime(2026, 7, 10),
                        EndDate = new DateTime(2026, 7, 17),
                        AvailableRooms = 7,
                        Price = 999.99m,
                        OldPrice = 1199.99m,
                        IsDiscountActive = true,
                        PackageType = "Family",
                        ShortDescription = "Beautiful beaches, old town exploration, and family activities.",
                        ImageUrl = "https://images.unsplash.com/photo-1555993536-0e6c0c0b0b0b?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 105,
                        DepartureYear = 2026
                    },
                    // Adventure Packages
                    new Trip {
                        Title = "Desert Adventure in Negev",
                        Destination = "Negev",
                        Country = "Israel",
                        StartDate = new DateTime(2026, 3, 15),
                        EndDate = new DateTime(2026, 3, 18),
                        AvailableRooms = 3,
                        Price = 399.00m,
                        OldPrice = 450.00m,
                        IsDiscountActive = true,
                        PackageType = "Adventure",
                        AgeLimit = 16,
                        ShortDescription = "Jeep tours, camping under the stars and Bedouin hospitality.",
                        ImageUrl = "https://images.unsplash.com/photo-1509316785289-025f5b846b35?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 70,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Nepal Mountain Trekking",
                        Destination = "Kathmandu",
                        Country = "Nepal",
                        StartDate = new DateTime(2026, 10, 1),
                        EndDate = new DateTime(2026, 10, 14),
                        AvailableRooms = 5,
                        Price = 1899.00m,
                        IsDiscountActive = false,
                        PackageType = "Adventure",
                        AgeLimit = 18,
                        ShortDescription = "14-day trekking adventure in the Himalayas with experienced guides.",
                        ImageUrl = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 75,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Iceland Northern Lights Tour",
                        Destination = "Reykjavik",
                        Country = "Iceland",
                        StartDate = new DateTime(2026, 11, 15),
                        EndDate = new DateTime(2026, 11, 22),
                        AvailableRooms = 6,
                        Price = 2299.00m,
                        OldPrice = 2599.00m,
                        IsDiscountActive = true,
                        PackageType = "Adventure",
                        AgeLimit = 12,
                        ShortDescription = "Northern lights viewing, glacier hiking, and hot springs.",
                        ImageUrl = "https://images.unsplash.com/photo-1539650116574-75c0c6d73a6e?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 80,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "New Zealand Adventure Tour",
                        Destination = "Queenstown",
                        Country = "New Zealand",
                        StartDate = new DateTime(2026, 12, 1),
                        EndDate = new DateTime(2026, 12, 15),
                        AvailableRooms = 4,
                        Price = 3299.00m,
                        IsDiscountActive = false,
                        PackageType = "Adventure",
                        AgeLimit = 18,
                        ShortDescription = "Bungee jumping, skydiving, and stunning landscapes.",
                        ImageUrl = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 82,
                        DepartureYear = 2026
                    },
                    // Cruise Packages
                    new Trip {
                        Title = "Mediterranean Cruise",
                        Destination = "Barcelona",
                        Country = "Spain",
                        StartDate = new DateTime(2026, 9, 1),
                        EndDate = new DateTime(2026, 9, 8),
                        AvailableRooms = 15,
                        Price = 2499.00m,
                        OldPrice = 2799.00m,
                        IsDiscountActive = true,
                        PackageType = "Cruise",
                        ShortDescription = "7-day cruise visiting Spain, France, and Italy ports.",
                        ImageUrl = "https://images.unsplash.com/photo-1518546305927-5a555bb7020d?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 100,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Caribbean Cruise Adventure",
                        Destination = "Miami",
                        Country = "United States",
                        StartDate = new DateTime(2026, 8, 20),
                        EndDate = new DateTime(2026, 8, 27),
                        AvailableRooms = 20,
                        Price = 1999.00m,
                        IsDiscountActive = false,
                        PackageType = "Cruise",
                        ShortDescription = "7-day Caribbean cruise with stops in Bahamas and Jamaica.",
                        ImageUrl = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 115,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Norwegian Fjords Cruise",
                        Destination = "Bergen",
                        Country = "Norway",
                        StartDate = new DateTime(2026, 7, 15),
                        EndDate = new DateTime(2026, 7, 22),
                        AvailableRooms = 8,
                        Price = 2799.00m,
                        IsDiscountActive = false,
                        PackageType = "Cruise",
                        ShortDescription = "Explore breathtaking Norwegian fjords and charming coastal towns.",
                        ImageUrl = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 92,
                        DepartureYear = 2026
                    },
                    // Luxury Packages
                    new Trip {
                        Title = "Dubai Luxury Experience",
                        Destination = "Dubai",
                        Country = "United Arab Emirates",
                        StartDate = new DateTime(2026, 10, 10),
                        EndDate = new DateTime(2026, 10, 17),
                        AvailableRooms = 6,
                        Price = 3499.00m,
                        OldPrice = 3999.00m,
                        IsDiscountActive = true,
                        PackageType = "Luxury",
                        ShortDescription = "5-star hotel, desert safari, and exclusive shopping experiences.",
                        ImageUrl = "https://images.unsplash.com/photo-1512453979798-5ea266f8880c?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 98,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Swiss Alps Luxury Retreat",
                        Destination = "Zermatt",
                        Country = "Switzerland",
                        StartDate = new DateTime(2026, 12, 20),
                        EndDate = new DateTime(2026, 12, 27),
                        AvailableRooms = 5,
                        Price = 3799.00m,
                        IsDiscountActive = false,
                        PackageType = "Luxury",
                        ShortDescription = "Luxury mountain resort with spa, skiing, and fine dining.",
                        ImageUrl = "https://images.unsplash.com/photo-1506905925346-21bda4d32df4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 87,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Tokyo Luxury Experience",
                        Destination = "Tokyo",
                        Country = "Japan",
                        StartDate = new DateTime(2026, 11, 1),
                        EndDate = new DateTime(2026, 11, 10),
                        AvailableRooms = 4,
                        Price = 4199.00m,
                        IsDiscountActive = false,
                        PackageType = "Luxury",
                        ShortDescription = "Luxury hotel, Michelin-starred restaurants, and cultural tours.",
                        ImageUrl = "https://images.unsplash.com/photo-1540959733332-eab4deabeeaf?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 89,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Seychelles Luxury Escape",
                        Destination = "Mahe",
                        Country = "Seychelles",
                        StartDate = new DateTime(2026, 9, 15),
                        EndDate = new DateTime(2026, 9, 22),
                        AvailableRooms = 3,
                        Price = 4499.00m,
                        IsDiscountActive = false,
                        PackageType = "Luxury",
                        AgeLimit = 16,
                        ShortDescription = "Private beach villa, world-class spa, and exclusive dining.",
                        ImageUrl = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 91,
                        DepartureYear = 2026
                    },
                    // Additional Popular Destinations
                    new Trip {
                        Title = "Barcelona City Break",
                        Destination = "Barcelona",
                        Country = "Spain",
                        StartDate = new DateTime(2026, 6, 10),
                        EndDate = new DateTime(2026, 6, 15),
                        AvailableRooms = 12,
                        Price = 799.00m,
                        OldPrice = 949.00m,
                        IsDiscountActive = true,
                        PackageType = "Family",
                        ShortDescription = "Explore Gaudi's architecture, beaches, and vibrant culture.",
                        ImageUrl = "https://images.unsplash.com/photo-1539037116277-4db20889f2d4?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 108,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Prague Historical Journey",
                        Destination = "Prague",
                        Country = "Czech Republic",
                        StartDate = new DateTime(2026, 5, 20),
                        EndDate = new DateTime(2026, 5, 25),
                        AvailableRooms = 10,
                        Price = 699.00m,
                        IsDiscountActive = false,
                        PackageType = "Family",
                        ShortDescription = "Medieval architecture, castle tours, and rich history.",
                        ImageUrl = "https://images.unsplash.com/photo-1541849546-216549ae216d?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 95,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Amsterdam Canal Tour",
                        Destination = "Amsterdam",
                        Country = "Netherlands",
                        StartDate = new DateTime(2026, 8, 5),
                        EndDate = new DateTime(2026, 8, 10),
                        AvailableRooms = 9,
                        Price = 849.00m,
                        OldPrice = 999.00m,
                        IsDiscountActive = true,
                        PackageType = "Family",
                        ShortDescription = "Canal cruises, museums, and Dutch culture experience.",
                        ImageUrl = "https://images.unsplash.com/photo-1534351590666-13e3e96b5017?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 102,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Rio de Janeiro Carnival",
                        Destination = "Rio de Janeiro",
                        Country = "Brazil",
                        StartDate = new DateTime(2027, 2, 20),
                        EndDate = new DateTime(2027, 2, 28),
                        AvailableRooms = 8,
                        Price = 1899.00m,
                        IsDiscountActive = false,
                        PackageType = "Adventure",
                        AgeLimit = 18,
                        ShortDescription = "Experience the world's biggest carnival celebration.",
                        ImageUrl = "https://images.unsplash.com/photo-1483729558449-99ef09a8c325?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 96,
                        DepartureYear = 2027
                    },
                    new Trip {
                        Title = "Thailand Beach Paradise",
                        Destination = "Phuket",
                        Country = "Thailand",
                        StartDate = new DateTime(2026, 9, 1),
                        EndDate = new DateTime(2026, 9, 10),
                        AvailableRooms = 14,
                        Price = 1199.00m,
                        OldPrice = 1399.00m,
                        IsDiscountActive = true,
                        PackageType = "Family",
                        ShortDescription = "Tropical beaches, cultural tours, and amazing cuisine.",
                        ImageUrl = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 118,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Morocco Desert Safari",
                        Destination = "Marrakech",
                        Country = "Morocco",
                        StartDate = new DateTime(2026, 10, 15),
                        EndDate = new DateTime(2026, 10, 22),
                        AvailableRooms = 7,
                        Price = 1099.00m,
                        IsDiscountActive = false,
                        PackageType = "Adventure",
                        AgeLimit = 14,
                        ShortDescription = "Sahara desert adventure, camel rides, and Berber culture.",
                        ImageUrl = "https://images.unsplash.com/photo-1516026672322-bc52d61a55d5?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 84,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Bali Tropical Retreat",
                        Destination = "Bali",
                        Country = "Indonesia",
                        StartDate = new DateTime(2026, 11, 10),
                        EndDate = new DateTime(2026, 11, 20),
                        AvailableRooms = 11,
                        Price = 1399.00m,
                        OldPrice = 1599.00m,
                        IsDiscountActive = true,
                        PackageType = "Honeymoon",
                        AgeLimit = 18,
                        ShortDescription = "Lush landscapes, ancient temples, and pristine beaches.",
                        ImageUrl = "https://images.unsplash.com/photo-1518546305927-5a555bb7020d?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 112,
                        DepartureYear = 2026
                    },
                    new Trip {
                        Title = "Vienna Classical Music Tour",
                        Destination = "Vienna",
                        Country = "Austria",
                        StartDate = new DateTime(2026, 9, 20),
                        EndDate = new DateTime(2026, 9, 25),
                        AvailableRooms = 8,
                        Price = 949.00m,
                        IsDiscountActive = false,
                        PackageType = "Family",
                        ShortDescription = "Classical concerts, imperial palaces, and coffee culture.",
                        ImageUrl = "https://images.unsplash.com/photo-1547036967-23d11aacaee0?w=800&h=600&fit=crop&q=80",
                        PopularityScore = 86,
                        DepartureYear = 2026
                    }
                };

            // Only add trips that don't already exist (check by title)
            foreach (var trip in allTrips)
            {
                if (!existingTripTitles.Contains(trip.Title))
                {
                    tripsToAdd.Add(trip);
                }
            }

            // Add missing trips to database
            if (tripsToAdd.Any())
            {
                context.Trips.AddRange(tripsToAdd);
                await context.SaveChangesAsync();
                Console.WriteLine($"Added {tripsToAdd.Count} new trips to the database. Total trips: {await context.Trips.CountAsync()}");
            }
            else
            {
                Console.WriteLine($"All trips already exist in database. Total trips: {await context.Trips.CountAsync()}");
            }

            // Update existing trips with new image URLs and ensure IsVisible is set
            int updatedCount = 0;
            var existingTrips = await context.Trips.ToListAsync();
            foreach (var existingTrip in existingTrips)
            {
                bool needsSave = false;
                var matchingTrip = allTrips.FirstOrDefault(t => t.Title == existingTrip.Title);
                if (matchingTrip != null && existingTrip.ImageUrl != matchingTrip.ImageUrl)
                {
                    existingTrip.ImageUrl = matchingTrip.ImageUrl;
                    needsSave = true;
                }
                // Ensure IsVisible is true for all existing trips (default visibility)
                if (!existingTrip.IsVisible)
                {
                    existingTrip.IsVisible = true;
                    needsSave = true;
                }
                if (needsSave)
                {
                    updatedCount++;
                }
            }
            
            if (updatedCount > 0)
            {
                await context.SaveChangesAsync();
                Console.WriteLine($"Updated {updatedCount} existing trips with new image URLs and visibility settings.");
            }

            // 4) Initialize admin settings if not exists
            try
            {
                if (context.AdminSettings.Any())
                {
                    // Settings already exist, skip
                }
                else
                {
                    var settings = new AdminSettings
                    {
                        DaysBeforeTripLatestBooking = 7,
                        DaysBeforeTripCancellationDeadline = 5,
                        DaysBeforeTripReminder = 5,
                        MaxDiscountDurationDays = 7,
                        WaitingListNotificationExpirationDays = 3,
                        LastUpdated = DateTime.Now
                    };
                    context.AdminSettings.Add(settings);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not initialize AdminSettings: {ex.Message}");
                // Continue - AdminSettings will be initialized when migration is applied
            }
        }
    }
}
