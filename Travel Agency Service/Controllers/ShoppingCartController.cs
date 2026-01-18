using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Controllers
{
    [Authorize]
    public class ShoppingCartController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const string CartSessionKey = "ShoppingCart";

        public ShoppingCartController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /ShoppingCart
        public async Task<IActionResult> Index()
        {
            var cart = GetCart();
            var tripIds = cart.Select(item => item.TripId).ToList();
            
            if (tripIds.Any())
            {
                var trips = await _context.Trips
                    .Where(t => tripIds.Contains(t.Id))
                    .ToListAsync();

                // Update cart items with current prices
                foreach (var item in cart)
                {
                    var trip = trips.FirstOrDefault(t => t.Id == item.TripId);
                    if (trip != null)
                    {
                        item.Price = trip.Price;
                        item.TripTitle = trip.Title;
                        item.ImageUrl = trip.ImageUrl ?? "";
                    }
                }
            }

            ViewBag.Total = cart.Sum(item => item.TotalPrice);
            return View(cart);
        }

        // POST: /ShoppingCart/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int tripId, int numberOfPeople = 1)
        {
            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            if (numberOfPeople <= 0 || numberOfPeople > 20)
            {
                TempData["Message"] = "Number of people must be between 1 and 20.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            var cart = GetCart();

            // Check if item already exists
            var existingItem = cart.FirstOrDefault(item => item.TripId == tripId);
            if (existingItem != null)
            {
                existingItem.NumberOfPeople += numberOfPeople;
            }
            else
            {
                cart.Add(new ShoppingCartItem
                {
                    TripId = tripId,
                    TripTitle = trip.Title,
                    Price = trip.Price,
                    NumberOfPeople = numberOfPeople,
                    ImageUrl = trip.ImageUrl ?? ""
                });
            }

            SaveCart(cart);
            TempData["Message"] = "Item added to cart.";
            return RedirectToAction("Index", "ShoppingCart");
        }

        // POST: /ShoppingCart/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Remove(int tripId)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(i => i.TripId == tripId);
            if (item != null)
            {
                cart.Remove(item);
                SaveCart(cart);
            }
            return RedirectToAction("Index");
        }

        // POST: /ShoppingCart/UpdateQuantity
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateQuantity(int tripId, int numberOfPeople)
        {
            if (numberOfPeople <= 0 || numberOfPeople > 20)
            {
                TempData["Message"] = "Number of people must be between 1 and 20.";
                return RedirectToAction("Index");
            }

            var cart = GetCart();
            var item = cart.FirstOrDefault(i => i.TripId == tripId);
            if (item != null)
            {
                item.NumberOfPeople = numberOfPeople;
                SaveCart(cart);
            }
            return RedirectToAction("Index");
        }

        // POST: /ShoppingCart/Checkout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout()
        {
            var cart = GetCart();
            if (!cart.Any())
            {
                TempData["Message"] = "Your cart is empty.";
                return RedirectToAction("Index");
            }

            // Save cart to TempData for checkout process
            // The cart will be cleared after successful booking creation in CheckoutFromCart
            TempData["CartCheckout"] = JsonSerializer.Serialize(cart);
            return RedirectToAction("CheckoutFromCart", "Bookings");
        }

        // POST: /ShoppingCart/Clear (called after successful checkout)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            HttpContext.Session.Remove(CartSessionKey);
            return RedirectToAction("Index", "Trips");
        }

        private List<ShoppingCartItem> GetCart()
        {
            var cartJson = HttpContext.Session.GetString(CartSessionKey);
            if (string.IsNullOrEmpty(cartJson))
                return new List<ShoppingCartItem>();

            try
            {
                return JsonSerializer.Deserialize<List<ShoppingCartItem>>(cartJson) ?? new List<ShoppingCartItem>();
            }
            catch
            {
                return new List<ShoppingCartItem>();
            }
        }

        private void SaveCart(List<ShoppingCartItem> cart)
        {
            var cartJson = JsonSerializer.Serialize(cart);
            HttpContext.Session.SetString(CartSessionKey, cartJson);
        }
    }
}