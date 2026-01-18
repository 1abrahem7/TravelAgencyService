using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    public class TripsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public TripsController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: /Trips
        public async Task<IActionResult> Index(string search, string category, string sort, bool discountOnly = false, 
            decimal? minPrice = null, decimal? maxPrice = null, DateTime? startDate = null, DateTime? endDate = null, int? departureYear = null)
        {
            var tripsQuery = _context.Trips.AsQueryable();

            // Filter out invisible trips for non-admin users
            if (!User.IsInRole("Admin"))
            {
                tripsQuery = tripsQuery.Where(t => t.IsVisible);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                tripsQuery = tripsQuery.Where(t =>
                    t.Title.ToLower().Contains(search) ||
                    t.Destination.ToLower().Contains(search) ||
                    t.Country.ToLower().Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                tripsQuery = tripsQuery.Where(t => t.PackageType == category);
            }

            if (discountOnly)
            {
                tripsQuery = tripsQuery.Where(t => t.IsDiscountActive);
            }

            // Price range filtering
            if (minPrice.HasValue)
            {
                tripsQuery = tripsQuery.Where(t => t.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                tripsQuery = tripsQuery.Where(t => t.Price <= maxPrice.Value);
            }

            // Travel date filtering (filter by trip start date)
            if (startDate.HasValue)
            {
                tripsQuery = tripsQuery.Where(t => t.StartDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                tripsQuery = tripsQuery.Where(t => t.StartDate <= endDate.Value);
            }

            // Departure year filtering
            if (departureYear.HasValue)
            {
                tripsQuery = tripsQuery.Where(t => t.DepartureYear == departureYear.Value);
            }

            switch (sort)
            {
                case "priceAsc":
                    tripsQuery = tripsQuery.OrderBy(t => t.Price);
                    break;
                case "priceDesc":
                    tripsQuery = tripsQuery.OrderByDescending(t => t.Price);
                    break;
                case "popular":
                    tripsQuery = tripsQuery.OrderByDescending(t => t.PopularityScore);
                    break;
                case "date":
                    tripsQuery = tripsQuery.OrderBy(t => t.StartDate);
                    break;
                default:
                    tripsQuery = tripsQuery.OrderBy(t => t.Id);
                    break;
            }

            var trips = await tripsQuery.ToListAsync();

            // Check and expire discounts (max 1 week)
            var now = DateTime.Now;
            int maxDiscountDays = 7; // default
            try
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                maxDiscountDays = settings?.MaxDiscountDurationDays ?? 7;
            }
            catch
            {
                // AdminSettings table may not exist yet - use default
            }
            
            foreach (var trip in trips.Where(t => t.IsDiscountActive))
            {
                // If discount has expiry date and it's passed, expire it
                if (trip.DiscountExpiryDate.HasValue && trip.DiscountExpiryDate.Value < now)
                {
                    trip.IsDiscountActive = false;
                    trip.OldPrice = null;
                    trip.DiscountExpiryDate = null;
                }
                // If no expiry date but discount is older than max days, expire it
                else if (!trip.DiscountExpiryDate.HasValue)
                {
                    // We can't know when discount started, so skip this check
                    // In real app, you'd track when discount was activated
                }
            }

            // Save expired discounts
            await _context.SaveChangesAsync();

            // ⭐ Load ratings for these trips
            var tripIds = trips.Select(t => t.Id).ToList();

            var ratings = await _context.Reviews
                .Where(r => tripIds.Contains(r.TripId))
                .GroupBy(r => r.TripId)
                .Select(g => new
                {
                    TripId = g.Key,
                    Avg = g.Average(r => r.Rating),
                    Count = g.Count()
                })
                .ToListAsync();

            foreach (var trip in trips)
            {
                var r = ratings.FirstOrDefault(x => x.TripId == trip.Id);
                if (r != null)
                {
                    trip.AverageRating = r.Avg;
                    trip.ReviewCount = r.Count;
                }
            }

            return View(trips);
        }

        // GET: /Trips/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var trip = await _context.Trips
                .FirstOrDefaultAsync(t => t.Id == id);

            if (trip == null) return NotFound();

            // Non-admin users cannot access invisible trips
            if (!trip.IsVisible && !User.IsInRole("Admin"))
            {
                return NotFound();
            }

            // Check and expire discount if needed
            if (trip.IsDiscountActive)
            {
                var now = DateTime.Now;
                if (trip.DiscountExpiryDate.HasValue && trip.DiscountExpiryDate.Value < now)
                {
                    trip.IsDiscountActive = false;
                    trip.OldPrice = null;
                    trip.DiscountExpiryDate = null;
                    await _context.SaveChangesAsync();
                }
            }

            var reviews = await _context.Reviews
                .Include(r => r.User)
                .Where(r => r.TripId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.Reviews = reviews;

            if (reviews.Any())
            {
                trip.AverageRating = reviews.Average(r => r.Rating);
                trip.ReviewCount = reviews.Count;
            }

            return View(trip);
        }

        // ========= ADMIN ONLY BELOW =========

        // GET: /Trips/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Trips/Create
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Trip trip, IFormFile? imageFile)
        {
            if (!ModelState.IsValid)
            {
                return View(trip);
            }

            // Set discount expiry date if discount is active
            if (trip.IsDiscountActive && !trip.DiscountExpiryDate.HasValue)
            {
                int maxDiscountDays = 7; // default
                try
                {
                    var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                    maxDiscountDays = settings?.MaxDiscountDurationDays ?? 7;
                }
                catch
                {
                    // AdminSettings table may not exist - use default
                }
                trip.DiscountExpiryDate = DateTime.Now.AddDays(maxDiscountDays);
            }
            else if (!trip.IsDiscountActive)
            {
                trip.DiscountExpiryDate = null;
                trip.OldPrice = null;
            }

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploads = Path.Combine(_env.WebRootPath, "images");
                Directory.CreateDirectory(uploads);
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
                var filePath = Path.Combine(uploads, fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await imageFile.CopyToAsync(stream);
                trip.ImageUrl = $"/images/{fileName}";
            }

            _context.Trips.Add(trip);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Trip created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Trips/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var trip = await _context.Trips.FindAsync(id);
            if (trip == null) return NotFound();

            return View(trip);
        }

        // POST: /Trips/Edit/5
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Trip trip, IFormFile? imageFile)
        {
            if (id != trip.Id) return NotFound();

            if (!ModelState.IsValid)
            {
                return View(trip);
            }

            // Set discount expiry date if discount is active
            if (trip.IsDiscountActive && !trip.DiscountExpiryDate.HasValue)
            {
                int maxDiscountDays = 7; // default
                try
                {
                    var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                    maxDiscountDays = settings?.MaxDiscountDurationDays ?? 7;
                }
                catch
                {
                    // AdminSettings table may not exist - use default
                }
                trip.DiscountExpiryDate = DateTime.Now.AddDays(maxDiscountDays);
            }
            else if (!trip.IsDiscountActive)
            {
                trip.DiscountExpiryDate = null;
                trip.OldPrice = null;
            }

            // get existing for old image path
            var existing = await _context.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (existing == null) return NotFound();

            try
            {
                if (imageFile != null && imageFile.Length > 0)
                {
                    // save new file
                    var uploads = Path.Combine(_env.WebRootPath, "images");
                    Directory.CreateDirectory(uploads);
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
                    var filePath = Path.Combine(uploads, fileName);
                    using var stream = new FileStream(filePath, FileMode.Create);
                    await imageFile.CopyToAsync(stream);
                    trip.ImageUrl = $"/images/{fileName}";

                    // delete old file if present
                    if (!string.IsNullOrEmpty(existing.ImageUrl))
                    {
                        var oldPath = Path.Combine(_env.WebRootPath, existing.ImageUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                        if (System.IO.File.Exists(oldPath))
                        {
                            System.IO.File.Delete(oldPath);
                        }
                    }
                }
                else
                {
                    // keep existing image
                    trip.ImageUrl = existing.ImageUrl;
                }

                _context.Entry(trip).Property(x => x.RowVersion).OriginalValue = trip.RowVersion;
                _context.Update(trip);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Trip updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TripExists(trip.Id))
                {
                    return NotFound();
                }
                else
                {
                    ModelState.AddModelError(string.Empty,
                        "The record was modified by another user. Please reload and try again.");
                    return View(trip);
                }
            }
        }

        // GET: /Trips/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == id);
            if (trip == null) return NotFound();

            return View(trip);
        }

        // POST: /Trips/Delete/5
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var trip = await _context.Trips.FindAsync(id);
            if (trip == null) return NotFound();

            // delete image file if exists
            if (!string.IsNullOrEmpty(trip.ImageUrl))
            {
                var oldPath = Path.Combine(_env.WebRootPath, trip.ImageUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(oldPath))
                {
                    System.IO.File.Delete(oldPath);
                }
            }

            _context.Trips.Remove(trip);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Trip deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        private bool TripExists(int id)
        {
            return _context.Trips.Any(e => e.Id == id);
        }
    }
}
