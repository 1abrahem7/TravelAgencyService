using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminWaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AdminWaitingListController(
            ApplicationDbContext context, 
            IEmailSender emailSender, 
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _emailSender = emailSender;
            _httpContextAccessor = httpContextAccessor;
        }

        // GET: /AdminWaitingList - List all trips with waiting lists
        public async Task<IActionResult> Index()
        {
            try
            {
                // Get all trips that have waiting list entries
                var tripsWithWaitingList = await _context.Trips
                    .Where(t => _context.WaitingList.Any(w => w.TripId == t.Id))
                    .OrderByDescending(t => t.StartDate)
                    .ToListAsync();

                // Get waiting list counts for each trip
                var tripIds = tripsWithWaitingList.Select(t => t.Id).ToList();
                if (tripIds.Any())
                {
                    var waitingListCounts = await _context.WaitingList
                        .Where(w => tripIds.Contains(w.TripId))
                        .GroupBy(w => w.TripId)
                        .Select(g => new
                        {
                            TripId = g.Key,
                            TotalWaiting = g.Count(),
                            Notified = g.Count(w => w.Notified),
                            NotNotified = g.Count(w => !w.Notified)
                        })
                        .ToListAsync();

                    var countsDictionary = new Dictionary<int, (int TotalWaiting, int Notified, int NotNotified)>();
                    foreach (var trip in tripsWithWaitingList)
                    {
                        var counts = waitingListCounts.FirstOrDefault(c => c.TripId == trip.Id);
                        if (counts != null)
                        {
                            countsDictionary[trip.Id] = (counts.TotalWaiting, counts.Notified, counts.NotNotified);
                        }
                        else
                        {
                            countsDictionary[trip.Id] = (0, 0, 0);
                        }
                    }
                    ViewBag.WaitingListCounts = countsDictionary;
                }

                return View(tripsWithWaitingList ?? new List<Trip>());
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error loading waiting lists: {ex.Message}";
                return View(new List<Trip>());
            }
        }

        // GET: /AdminWaitingList/TripQueue/5 - View queue for a specific trip
        public async Task<IActionResult> TripQueue(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == tripId);
            if (trip == null) return NotFound();

            var queue = await _context.WaitingList
                .Include(w => w.User)
                .Where(w => w.TripId == tripId)
                .OrderBy(w => w.JoinedAt)
                .ToListAsync();

            ViewBag.Trip = trip;
            return View(queue);
        }

        // Remove a user from queue
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id, int tripId)
        {
            var item = await _context.WaitingList.FirstOrDefaultAsync(w => w.Id == id);
            if (item != null)
            {
                _context.WaitingList.Remove(item);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Removed from waiting list.";
            }
            return RedirectToAction(nameof(TripQueue), new { tripId });
        }

        // Notify next user manually (optional button)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NotifyNext(int tripId)
        {
            var trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == tripId);
            if (trip == null) return NotFound();

            if (trip.AvailableRooms <= 0)
            {
                TempData["Message"] = "No rooms available to notify waiting users.";
                return RedirectToAction(nameof(TripQueue), new { tripId });
            }

            var next = await _context.WaitingList
                .Include(w => w.User)
                .Where(w => w.TripId == tripId && !w.Notified)
                .OrderBy(w => w.JoinedAt)
                .FirstOrDefaultAsync();

            if (next == null)
            {
                TempData["Message"] = "No waiting users to notify.";
                return RedirectToAction(nameof(TripQueue), new { tripId });
            }

            if (!string.IsNullOrWhiteSpace(next.User?.Email))
            {
                // Generate booking link
                var scheme = _httpContextAccessor.HttpContext?.Request.Scheme ?? "https";
                var host = _httpContextAccessor.HttpContext?.Request.Host.Value ?? "localhost";
                var bookingUrl = $"{scheme}://{host}/Trips/Details/{tripId}";
                var myWaitingListUrl = $"{scheme}://{host}/WaitingList/MyWaitingList";

                var emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <h2 style='color: #1e88e5;'>🎉 Great News! A Room is Available!</h2>
                        <p>Hi {next.User.FullName ?? "there"},</p>
                        <p>We have exciting news for you!</p>
                        <div style='background-color: #f0f8ff; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #1e88e5;'>
                            <h3 style='color: #1e88e5; margin-top: 0;'>📋 Trip Details:</h3>
                            <ul style='line-height: 1.8;'>
                                <li><strong>Trip:</strong> {trip.Title}</li>
                                <li><strong>Destination:</strong> {trip.Destination}, {trip.Country}</li>
                                <li><strong>Departure Date:</strong> {trip.StartDate:MMMM dd, yyyy}</li>
                                <li><strong>Return Date:</strong> {trip.EndDate:MMMM dd, yyyy}</li>
                                <li><strong>Price:</strong> {trip.Price:C} per person</li>
                                <li><strong>Available Rooms:</strong> {trip.AvailableRooms}</li>
                            </ul>
                        </div>
                        <p style='font-size: 16px; color: #d32f2f; font-weight: bold;'>⚠️ Important: This is your turn to book! Please book soon as rooms are limited.</p>
                        <div style='text-align: center; margin: 30px 0;'>
                            <a href='{bookingUrl}' 
                               style='background-color: #1e88e5; color: white; padding: 15px 30px; text-decoration: none; 
                                      border-radius: 8px; font-weight: bold; font-size: 16px; display: inline-block;'>
                                📋 Book This Trip Now
                            </a>
                        </div>
                        <p style='color: #666; font-size: 14px;'>
                            You can also view your waiting list status 
                            <a href='{myWaitingListUrl}' style='color: #1e88e5;'>here</a>.
                        </p>
                        <p style='margin-top: 30px; color: #888; font-size: 12px;'>
                            <strong>Note:</strong> If you don't book within a reasonable time, the next person in the waiting list may get the opportunity to book.
                        </p>
                        <p style='margin-top: 20px;'>
                            Best regards,<br/>
                            <strong>Travel Agency Service Team</strong>
                        </p>
                    </div>";

                await _emailSender.SendEmailAsync(
                    next.User.Email,
                    $"🎉 Room Available: {trip.Title} - Book Now!",
                    emailBody
                );
                next.Notified = true;
                next.NotifiedAt = DateTime.Now; // Track when notified
                await _context.SaveChangesAsync();
            }

            TempData["Message"] = "Next user has been notified.";
            return RedirectToAction(nameof(TripQueue), new { tripId });
        }

        // Clear entire waiting list for a trip (optional)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear(int tripId)
        {
            var items = await _context.WaitingList.Where(w => w.TripId == tripId).ToListAsync();
            _context.WaitingList.RemoveRange(items);
            await _context.SaveChangesAsync();
            TempData["Message"] = "Waiting list cleared.";
            return RedirectToAction(nameof(TripQueue), new { tripId });
        }
    }
}
