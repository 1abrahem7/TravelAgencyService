using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminSettingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminSettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /AdminSettings
        public async Task<IActionResult> Index()
        {
            var settings = await _context.AdminSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // Create default settings if none exist
                settings = new AdminSettings
                {
                    DaysBeforeTripLatestBooking = 7,
                    DaysBeforeTripCancellationDeadline = 5,
                    DaysBeforeTripReminder = 5,
                    MaxDiscountDurationDays = 7,
                    LastUpdated = DateTime.Now
                };
                _context.AdminSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return View(settings);
        }

        // POST: /AdminSettings/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(AdminSettings model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            var settings = await _context.AdminSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AdminSettings();
                _context.AdminSettings.Add(settings);
            }

            settings.DaysBeforeTripLatestBooking = model.DaysBeforeTripLatestBooking;
            settings.DaysBeforeTripCancellationDeadline = model.DaysBeforeTripCancellationDeadline;
            settings.DaysBeforeTripReminder = model.DaysBeforeTripReminder;
            settings.MaxDiscountDurationDays = model.MaxDiscountDurationDays;
            settings.LastUpdated = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["Message"] = "Settings updated successfully.";
            return RedirectToAction("Index");
        }
    }
}