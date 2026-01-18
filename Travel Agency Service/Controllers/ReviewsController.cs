using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    [Authorize]
    public class ReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // POST: /Reviews/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int tripId, int rating, string comment)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            if (rating < 1 || rating > 5)
            {
                TempData["Message"] = "Rating must be between 1 and 5.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            // Check if user already reviewed this trip (one review per user per trip)
            bool already = await _context.Reviews
                .AnyAsync(r => r.TripId == tripId && r.UserId == user.Id);

            if (already)
            {
                TempData["Message"] = "You already reviewed this trip.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            var review = new Review
            {
                TripId = tripId,
                UserId = user.Id,
                Rating = rating,
                Comment = comment ?? "",
                CreatedAt = DateTime.Now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Thank you for your review!";
            return RedirectToAction("Details", "Trips", new { id = tripId });
        }

        // POST: /Reviews/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var review = await _context.Reviews
                .Include(r => r.Trip)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (review == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Allow delete if owner or Admin
            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (review.UserId != user.Id && !isAdmin)
            {
                return Forbid();
            }

            int tripId = review.TripId;

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Review deleted.";
            return RedirectToAction("Details", "Trips", new { id = tripId });
        }
    }
}
