using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;
using Travel_Agency_Service.Services;

namespace Travel_Agency_Service.Controllers
{
    [Authorize]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<PaymentsController> _logger;
        private readonly IPayPalService _payPalService;

        public PaymentsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, 
            IEmailSender emailSender, ILogger<PaymentsController> logger, IPayPalService payPalService)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
            _payPalService = payPalService;
        }

        // GET: /Payments/Pay?bookingId=5
        public async Task<IActionResult> Pay(int bookingId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == user.Id);

            if (booking == null) return NotFound();

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking has been cancelled.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            ViewBag.Booking = booking;
            var model = new PaymentViewModel
            {
                BookingId = bookingId
            };

            return View(model);
        }

        // POST: /Payments/Pay
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(PaymentViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.UserId == user.Id);

            if (booking == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Booking = booking;
                return View(model);
            }

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking has been cancelled.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            // 🔁 Fake payment processing: we just mark it as paid
            booking.Paid = true;
            booking.PaymentDate = DateTime.Now;
            booking.PaymentReference = $"FAKE-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            await _context.SaveChangesAsync();

            TempData["Message"] = $"Payment successful. Reference: {booking.PaymentReference}";
            return RedirectToAction("MyBookings", "Bookings");
        }

        // POST: /Payments/PayWithPayPal
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayWithPayPal(int bookingId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == user.Id);

            if (booking == null) return NotFound();

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking has been cancelled.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            if (booking.Trip == null)
            {
                TempData["Message"] = "Trip information not found.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            // Build return URLs
            var scheme = Request.Scheme;
            var host = Request.Host;
            var returnUrl = $"{scheme}://{host}/Payments/PayPalSuccess?bookingId={bookingId}";
            var cancelUrl = $"{scheme}://{host}/Payments/PayPalCancel?bookingId={bookingId}";

            // Create PayPal order
            var orderResult = await _payPalService.CreateOrderAsync(
                booking.TotalPrice,
                "USD",
                $"Booking for {booking.Trip.Title}",
                returnUrl,
                cancelUrl
            );

            if (!orderResult.Success || string.IsNullOrEmpty(orderResult.ApprovalUrl))
            {
                // Fallback to simulation if PayPal is not configured
                _logger.LogWarning("PayPal order creation failed or PayPal not configured. Using simulation mode.");
                return await ProcessPayPalSimulation(booking, user);
            }

            // Store order ID in session for verification on callback
            HttpContext.Session.SetString($"PayPalOrder_{bookingId}", orderResult.OrderId ?? "");

            // Redirect to PayPal for user approval
            return Redirect(orderResult.ApprovalUrl);
        }

        // GET: /Payments/PayPalSuccess - Callback from PayPal after user approval
        [HttpGet]
        public async Task<IActionResult> PayPalSuccess(int bookingId, string token)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == user.Id);

            if (booking == null)
            {
                TempData["Message"] = "Booking not found.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            // Get order ID from session or from token parameter
            var orderId = HttpContext.Session.GetString($"PayPalOrder_{bookingId}") ?? token;
            
            if (string.IsNullOrEmpty(orderId))
            {
                TempData["Message"] = "PayPal order ID not found. Payment may not have completed.";
                return RedirectToAction("MyBookings", "Bookings");
            }

            // Capture the PayPal order
            var captureResult = await _payPalService.CaptureOrderAsync(orderId);

            if (!captureResult.Success)
            {
                TempData["Message"] = $"PayPal payment failed: {captureResult.ErrorMessage}";
                return RedirectToAction("MyBookings", "Bookings");
            }

            // Payment successful - update booking
            booking.Paid = true;
            booking.PaymentDate = DateTime.Now;
            booking.PaymentReference = $"PAYPAL-{captureResult.PaymentId ?? orderId}";

            await _context.SaveChangesAsync();

            // Clear session
            HttpContext.Session.Remove($"PayPalOrder_{bookingId}");

            // Send confirmation email
            if (user.Email != null && booking.Trip != null)
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Payment Confirmation - PayPal",
                        $@"
                            <h2>Payment Confirmed via PayPal!</h2>
                            <p>Hello {user.FullName ?? user.Email},</p>
                            <p>Your payment for <b>{booking.Trip.Title}</b> has been successfully processed through PayPal.</p>
                            <div style='background-color: #f0f4ff; padding: 20px; border-radius: 10px; margin: 20px 0;'>
                                <h3>Payment Details:</h3>
                                <ul>
                                    <li><strong>Payment Method:</strong> PayPal</li>
                                    <li><strong>Payment Reference:</strong> {booking.PaymentReference}</li>
                                    <li><strong>Amount:</strong> ${booking.TotalPrice:F2}</li>
                                    <li><strong>Booking Date:</strong> {booking.BookingDate:MMMM dd, yyyy}</li>
                                    <li><strong>Payment Date:</strong> {booking.PaymentDate:MMMM dd, yyyy}</li>
                                </ul>
                                <h3>Trip Details:</h3>
                                <ul>
                                    <li><strong>Trip:</strong> {booking.Trip.Title}</li>
                                    <li><strong>Destination:</strong> {booking.Trip.Destination}, {booking.Trip.Country}</li>
                                    <li><strong>Travel Dates:</strong> {booking.Trip.StartDate:MMMM dd, yyyy} to {booking.Trip.EndDate:MMMM dd, yyyy}</li>
                                    <li><strong>Number of Travelers:</strong> {booking.NumberOfPeople}</li>
                                </ul>
                            </div>
                            <p>You can download your itinerary from your bookings page.</p>
                            <p>Thank you for choosing our service!</p>
                        "
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to send PayPal payment confirmation email");
                }
            }

            TempData["Message"] = $"Payment successful via PayPal! Reference: {booking.PaymentReference}";
            return RedirectToAction("MyBookings", "Bookings");
        }

        // GET: /Payments/PayPalCancel - User cancelled PayPal payment
        [HttpGet]
        public IActionResult PayPalCancel(int bookingId)
        {
            HttpContext.Session.Remove($"PayPalOrder_{bookingId}");
            TempData["Message"] = "PayPal payment was cancelled. You can try again from your bookings page.";
            return RedirectToAction("MyBookings", "Bookings");
        }

        // Fallback simulation method when PayPal is not configured
        private async Task<IActionResult> ProcessPayPalSimulation(Booking booking, ApplicationUser user)
        {
            booking.Paid = true;
            booking.PaymentDate = DateTime.Now;
            booking.PaymentReference = $"PAYPAL-SIM-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

            await _context.SaveChangesAsync();

            if (user.Email != null && booking.Trip != null)
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Payment Confirmation - PayPal (Simulation)",
                        $@"
                            <h2>Payment Confirmed via PayPal (Simulation Mode)</h2>
                            <p>Hello {user.FullName ?? user.Email},</p>
                            <p><strong>Note:</strong> PayPal is running in simulation mode. For production, configure PayPal credentials in appsettings.json</p>
                            <p>Your payment for <b>{booking.Trip.Title}</b> has been processed.</p>
                            <div style='background-color: #f0f4ff; padding: 20px; border-radius: 10px; margin: 20px 0;'>
                                <h3>Payment Details:</h3>
                                <ul>
                                    <li><strong>Payment Method:</strong> PayPal (Simulation)</li>
                                    <li><strong>Payment Reference:</strong> {booking.PaymentReference}</li>
                                    <li><strong>Amount:</strong> ${booking.TotalPrice:F2}</li>
                                </ul>
                            </div>
                        "
                    );
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to send PayPal simulation email");
                }
            }

            TempData["Message"] = $"Payment processed (simulation mode). Reference: {booking.PaymentReference}";
            return RedirectToAction("MyBookings", "Bookings");
        }
    }
}
