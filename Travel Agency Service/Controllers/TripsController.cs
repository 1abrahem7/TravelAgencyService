// Controllers/TripsController.cs
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using Travel_Agency_Service.Models; // adjust namespace if different

namespace Travel_Agency_Service.Controllers
{
    public class TripsController : Controller
    {
        // GET: /Trips/
        public IActionResult Index()
        {
            var trips = GetSampleTrips();
            return View(trips);
        }

        // Temporary sample data - later you'll replace with DB
        private List<Trip> GetSampleTrips()
        {
            return new List<Trip>
            {
                new Trip {
                    Id = 1,
                    Title = "Rome Spring Escape",
                    Destination = "Rome, Italy",
                    StartDate = new DateTime(2026, 4, 10),
                    EndDate = new DateTime(2026, 4, 17),
                    Price = 899.99m,
                    ShortDescription = "Historic Rome: Colosseum, Vatican and more."
                },
                new Trip {
                    Id = 2,
                    Title = "Desert Nights Adventure",
                    Destination = "Negev Desert, Israel",
                    StartDate = new DateTime(2026, 6, 5),
                    EndDate = new DateTime(2026, 6, 8),
                    Price = 299.50m,
                    ShortDescription = "3 days camping and stargazing with local guides."
                },
                new Trip {
                    Id = 3,
                    Title = "Istanbul Culture Break",
                    Destination = "Istanbul, Turkey",
                    StartDate = new DateTime(2026, 9, 1),
                    EndDate = new DateTime(2026, 9, 6),
                    Price = 749.00m,
                    ShortDescription = "Markets, Bosphorus cruise and Ottoman history."
                }
            };
        }
    }
}
