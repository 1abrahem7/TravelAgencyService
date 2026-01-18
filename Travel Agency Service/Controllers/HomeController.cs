using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;

        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Dynamic trip count for home page
            var tripCount = await _context.Trips.CountAsync();

            // Latest 3 service reviews for "What users think about our service"
            // Handle case where table might not exist yet
            List<ServiceReview> latestServiceReviews = new List<ServiceReview>();
            try
            {
                latestServiceReviews = await _context.ServiceReviews
                    .Include(r => r.User)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(3)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ServiceReviews table may not exist yet. Skipping service reviews.");
                latestServiceReviews = new List<ServiceReview>();
            }

            ViewBag.TripCount = tripCount;
            ViewBag.ServiceReviews = latestServiceReviews;

            return View();
        }
        [Route("/privacy")]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
