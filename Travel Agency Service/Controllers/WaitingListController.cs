using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    [Authorize]
    public class WaitingListController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public WaitingListController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // POST: /WaitingList/Join
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int tripId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            // if there are rooms, no need waiting list
            if (trip.AvailableRooms > 0)
            {
                TempData["Message"] = "There are still available rooms – you can book directly.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            // check if already in waiting list
            bool already = await _context.WaitingList
                .AnyAsync(w => w.TripId == tripId && w.UserId == user.Id);

            if (already)
            {
                TempData["Message"] = "You are already on the waiting list for this trip.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            var item = new WaitingListItem
            {
                TripId = tripId,
                UserId = user.Id,
                JoinedAt = System.DateTime.Now,
                Notified = false
            };

            _context.WaitingList.Add(item);
            await _context.SaveChangesAsync();

            TempData["Message"] = "You have been added to the waiting list.";
            return RedirectToAction("MyWaitingList");
        }

        // GET: /WaitingList/MyWaitingList
        public async Task<IActionResult> MyWaitingList()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var myItems = await _context.WaitingList
                .Include(w => w.Trip)
                .Where(w => w.UserId == user.Id)
                .ToListAsync();

            var tripIds = myItems.Select(x => x.TripId).Distinct().ToList();

            var allForTrips = await _context.WaitingList
                .Include(w => w.Trip)
                .Where(w => tripIds.Contains(w.TripId))
                .OrderBy(w => w.JoinedAt)
                .ToListAsync();

            var result = myItems.Select(item =>
            {
                var queue = allForTrips.Where(x => x.TripId == item.TripId).ToList();
                var position = queue.FindIndex(x => x.UserId == user.Id) + 1;

                return new WaitingListStatusVM
                {
                    TripId = item.TripId,
                    TripTitle = item.Trip?.Title ?? string.Empty,
                    Destination = item.Trip?.Destination ?? string.Empty,
                    Country = item.Trip?.Country ?? string.Empty,
                    JoinedAt = item.JoinedAt,
                    Notified = item.Notified,
                    NotifiedAt = item.NotifiedAt,
                    Position = position <= 0 ? queue.Count : position,
                    TotalWaiting = queue.Count
                };
            }).OrderByDescending(x => x.JoinedAt).ToList();

            return View(result);
        }

    }
}
