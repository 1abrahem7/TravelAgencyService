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
    public class ServiceReviewsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ServiceReviewsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: /ServiceReviews/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /ServiceReviews/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServiceReview model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.UserId = user.Id;
            model.CreatedAt = DateTime.Now;

            _context.ServiceReviews.Add(model);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Thank you for your feedback about our service!";
            return RedirectToAction("Index", "Home");
        }
    }
}