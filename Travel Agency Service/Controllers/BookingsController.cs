using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Travel_Agency_Service.Controllers
{
    [Authorize]
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<BookingsController> _logger;

        public BookingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
            IEmailSender emailSender, ILogger<BookingsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        // GET: /Bookings/All (Admin only)
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> All()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Trip)
                .Include(b => b.User)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            return View(bookings);
        }

        // GET: /Bookings/MyBookings
        public async Task<IActionResult> MyBookings(string filter = "all") // "all", "upcoming", "past"
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var now = DateTime.Now;
            var bookingsQuery = _context.Bookings
                .Include(b => b.Trip)
                .Where(b => b.UserId == user.Id);

            // Apply filter
            switch (filter?.ToLower())
            {
                case "upcoming":
                    bookingsQuery = bookingsQuery.Where(b => b.Trip != null && b.Trip.StartDate > now && !b.Cancelled);
                    break;
                case "past":
                    bookingsQuery = bookingsQuery.Where(b => b.Trip != null && b.Trip.StartDate <= now);
                    break;
                case "all":
                default:
                    // Show all
                    break;
            }

            var bookings = await bookingsQuery
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            ViewBag.Filter = filter ?? "all";
            ViewBag.Now = now;

            return View(bookings);
        }

        // GET: /Bookings/Create?tripId=5
        public async Task<IActionResult> Create(int tripId)
        {
            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            ViewBag.Trip = trip;
            return View();
        }

        // POST: /Bookings/BuyNow - Direct payment flow (Buy Now button)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BuyNow(int tripId, int numberOfPeople = 1)
        {
            // Basic validation
            if (numberOfPeople <= 0)
            {
                TempData["Message"] = "Number of people must be at least 1.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Load trip to check booking time frames
            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            // Check booking time frame - get admin settings
            int daysBeforeTrip = 7; // default
            try
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                daysBeforeTrip = settings?.DaysBeforeTripLatestBooking ?? 7;
            }
            catch
            {
                // AdminSettings table may not exist - use default
            }

            if (trip.StartDate <= DateTime.Today.AddDays(daysBeforeTrip))
            {
                TempData["Message"] = $"You cannot book this trip. The latest booking date is {daysBeforeTrip} days before departure.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            // Rule: max 3 upcoming trips
            var upcomingCount = await CountUpcomingBookingsAsync(user.Id);
            if (upcomingCount >= 3)
            {
                TempData["Message"] = "You already have 3 upcoming trips booked. Cancel one before booking another.";
                return RedirectToAction("MyBookings");
            }

            // Prevent duplicate booking
            var alreadyBooked = await _context.Bookings
                .AnyAsync(b => b.TripId == tripId && b.UserId == user.Id && !b.Cancelled);

            if (alreadyBooked)
            {
                TempData["Message"] = "You have already booked this trip before.";
                return RedirectToAction(nameof(MyBookings));
            }

            // Create booking and immediately redirect to payment (same transaction logic as Create)
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            try
            {
                trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == tripId);
                if (trip == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                // Waiting list turn check
                var firstInQueueUserId = await _context.WaitingList
                    .Where(w => w.TripId == tripId)
                    .OrderBy(w => w.JoinedAt)
                    .Select(w => w.UserId)
                    .FirstOrDefaultAsync();

                if (firstInQueueUserId != null && firstInQueueUserId != user.Id)
                {
                    await tx.RollbackAsync();
                    TempData["Message"] = "This trip has a waiting list. You can book only when it is your turn.";
                    return RedirectToAction("Details", "Trips", new { id = tripId });
                }

                // Check rooms
                if (trip.AvailableRooms < numberOfPeople)
                {
                    await tx.RollbackAsync();
                    TempData["Message"] = "Not enough available rooms right now. You can join the waiting list from the trip page.";
                    return RedirectToAction("Details", "Trips", new { id = tripId });
                }

                // Create booking
                var booking = new Booking
                {
                    TripId = tripId,
                    UserId = user.Id,
                    NumberOfPeople = numberOfPeople,
                    BookingDate = DateTime.Now,
                    TotalPrice = trip.Price * numberOfPeople,
                    Paid = false,
                    Cancelled = false,
                    PaymentReference = "PENDING"
                };

                // Reserve rooms
                trip.AvailableRooms -= numberOfPeople;

                _context.Bookings.Add(booking);
                _context.Trips.Update(trip);

                // Remove from waiting list if applicable
                var myWaiting = await _context.WaitingList
                    .FirstOrDefaultAsync(w => w.TripId == tripId && w.UserId == user.Id);

                if (myWaiting != null)
                {
                    _context.WaitingList.Remove(myWaiting);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                // ✅ BUY NOW: Immediately redirect to payment page
                TempData["Message"] = "Booking created. Please complete payment now.";
                return RedirectToAction(nameof(Pay), new { id = booking.Id });
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                TempData["Message"] = "Someone else booked the last room before you. Please try again or join the waiting list.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                TempData["Message"] = "An unexpected error happened while creating the booking. Please try again.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }

        // POST: /Bookings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int tripId, int numberOfPeople)
        {
            // Basic validation
            if (numberOfPeople <= 0)
            {
                TempData["Message"] = "Number of people must be at least 1.";
                return RedirectToAction("Create", new { tripId });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Load trip to check booking time frames
            var trip = await _context.Trips.FindAsync(tripId);
            if (trip == null) return NotFound();

            // Check booking time frame - get admin settings
            int daysBeforeTrip = 7; // default
            try
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                daysBeforeTrip = settings?.DaysBeforeTripLatestBooking ?? 7;
            }
            catch
            {
                // AdminSettings table may not exist - use default
            }

            if (trip.StartDate <= DateTime.Today.AddDays(daysBeforeTrip))
            {
                TempData["Message"] = $"You cannot book this trip. The latest booking date is {daysBeforeTrip} days before departure.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }

            // Rule: max 3 upcoming trips
            var upcomingCount = await CountUpcomingBookingsAsync(user.Id);
            if (upcomingCount >= 3)
            {
                TempData["Message"] = "You already have 3 upcoming trips booked. Cancel one before booking another.";
                return RedirectToAction("MyBookings");
            }

            // Prevent duplicate booking: same user, same trip, not cancelled
            var alreadyBooked = await _context.Bookings
                .AnyAsync(b => b.TripId == tripId && b.UserId == user.Id && !b.Cancelled);

            if (alreadyBooked)
            {
                TempData["Message"] = "You have already booked this trip before.";
                return RedirectToAction(nameof(MyBookings));
            }

            // ============================
            // CONCURRENCY FIX (LAST ROOM)
            // ============================
            // We do the "check rooms + decrease rooms + create booking" inside ONE transaction
            // with SERIALIZABLE isolation to avoid two users taking the last room at the same time.
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            try
            {
                // Reload trip INSIDE transaction (already loaded above, but reload for consistency)
                trip = await _context.Trips.FirstOrDefaultAsync(t => t.Id == tripId);
                if (trip == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                // Waiting list turn check (inside transaction so it matches the real state)
                var firstInQueueUserId = await _context.WaitingList
                    .Where(w => w.TripId == tripId)
                    .OrderBy(w => w.JoinedAt)
                    .Select(w => w.UserId)
                    .FirstOrDefaultAsync();

                if (firstInQueueUserId != null && firstInQueueUserId != user.Id)
                {
                    await tx.RollbackAsync();
                    TempData["Message"] = "This trip has a waiting list. You can book only when it is your turn.";
                    return RedirectToAction("Details", "Trips", new { id = tripId });
                }

                // Re-check rooms INSIDE transaction (this is the important part)
                if (trip.AvailableRooms < numberOfPeople)
                {
                    await tx.RollbackAsync();
                    TempData["Message"] = "Not enough available rooms right now. You can join the waiting list from the trip page.";
                    return RedirectToAction("Details", "Trips", new { id = tripId });
                }

                // Create booking
                var booking = new Booking
                {
                    TripId = tripId,
                    UserId = user.Id,
                    NumberOfPeople = numberOfPeople,
                    BookingDate = DateTime.Now,
                    TotalPrice = trip.Price * numberOfPeople,
                    Paid = false,
                    Cancelled = false,
                    PaymentReference = "PENDING" // keep DB non-null
                };

                // Reserve rooms
                trip.AvailableRooms -= numberOfPeople;

                _context.Bookings.Add(booking);
                _context.Trips.Update(trip);

                // If user is first in waiting list, remove them ONLY AFTER booking is created successfully
                var myWaiting = await _context.WaitingList
                    .FirstOrDefaultAsync(w => w.TripId == tripId && w.UserId == user.Id);

                if (myWaiting != null)
                {
                    _context.WaitingList.Remove(myWaiting);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Message"] = "Booking created successfully.";
                return RedirectToAction(nameof(MyBookings));
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                TempData["Message"] = "Someone else booked the last room before you. Please try again or join the waiting list.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                TempData["Message"] = "An unexpected error happened while creating the booking. Please try again.";
                return RedirectToAction("Details", "Trips", new { id = tripId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Challenge();

            // Only booking owner or Admin can cancel
            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (booking.UserId != user.Id && !isAdmin)
                return Forbid();

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking is already cancelled.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            // Optional rule: don't allow cancelling after trip start
            if (booking.Trip == null)
            {
                TempData["Message"] = "Trip information not found.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            // Check cancellation deadline from admin settings
            int daysBeforeTrip = 5; // default
            try
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                daysBeforeTrip = settings?.DaysBeforeTripCancellationDeadline ?? 5;
            }
            catch
            {
                // AdminSettings table may not exist - use default
            }

            if (booking.Trip.StartDate <= DateTime.Today.AddDays(daysBeforeTrip))
            {
                TempData["Message"] = $"You cannot cancel this trip. The cancellation deadline is {daysBeforeTrip} days before departure.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            if (booking.Trip.StartDate <= DateTime.Today)
            {
                TempData["Message"] = "You cannot cancel a trip that has already started.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            booking.Cancelled = true;
            booking.CancelledDate = DateTime.Now;

            // Return rooms back to trip
            if (booking.Trip != null)
            {
                booking.Trip.AvailableRooms += booking.NumberOfPeople;
            }

            await _context.SaveChangesAsync();

            // Notify first user in waiting list if rooms became available
            if (booking.Trip != null && booking.Trip.AvailableRooms > 0)
            {
                var next = await _context.WaitingList
                    .Include(w => w.User)
                    .Where(w => w.TripId == booking.Trip.Id && !w.Notified)
                    .OrderBy(w => w.JoinedAt)
                    .FirstOrDefaultAsync();

                if (next != null && next.User != null && !string.IsNullOrWhiteSpace(next.User.Email))
                {
                    try
                    {
                        // Generate booking link
                        var scheme = Request.Scheme;
                        var host = Request.Host.Value;
                        var bookingUrl = $"{scheme}://{host}/Trips/Details/{booking.Trip.Id}";
                        var myWaitingListUrl = $"{scheme}://{host}/WaitingList/MyWaitingList";

                        var emailBody = $@"
                            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                                <h2 style='color: #1e88e5;'>🎉 Great News! A Room is Available!</h2>
                                <p>Hi {next.User.FullName ?? "there"},</p>
                                <p>We have exciting news for you! A room has become available for the trip you were waiting for.</p>
                                <div style='background-color: #f0f8ff; padding: 20px; border-radius: 10px; margin: 20px 0; border-left: 4px solid #1e88e5;'>
                                    <h3 style='color: #1e88e5; margin-top: 0;'>📋 Trip Details:</h3>
                                    <ul style='line-height: 1.8;'>
                                        <li><strong>Trip:</strong> {booking.Trip.Title}</li>
                                        <li><strong>Destination:</strong> {booking.Trip.Destination}, {booking.Trip.Country}</li>
                                        <li><strong>Departure Date:</strong> {booking.Trip.StartDate:MMMM dd, yyyy}</li>
                                        <li><strong>Return Date:</strong> {booking.Trip.EndDate:MMMM dd, yyyy}</li>
                                        <li><strong>Price:</strong> {booking.Trip.Price:C} per person</li>
                                        <li><strong>Available Rooms:</strong> {booking.Trip.AvailableRooms}</li>
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
                            $"🎉 Room Available: {booking.Trip.Title} - Book Now!",
                            emailBody
                        );

                        next.Notified = true;
                        next.NotifiedAt = DateTime.Now;
                        await _context.SaveChangesAsync();

                        _logger?.LogInformation($"Notified waiting list user {next.User.Email} for trip {booking.Trip.Title} after cancellation");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, $"Failed to send waiting list notification email for trip {booking.Trip.Id}");
                        // Don't fail the cancellation if email fails
                    }
                }
            }

            TempData["Message"] = "Booking cancelled successfully.";
            return RedirectToAction(isAdmin ? "All" : "MyBookings");
        }

        // GET: /Bookings/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            // Only owner can edit, and only if not paid and not cancelled
            if (booking.UserId != user.Id) return Forbid();

            if (booking.Paid)
            {
                TempData["Message"] = "You cannot edit a booking after payment has been made.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (booking.Cancelled)
            {
                TempData["Message"] = "You cannot edit a cancelled booking.";
                return RedirectToAction(nameof(MyBookings));
            }

            ViewBag.Trip = booking.Trip;
            return View(booking);
        }

        // POST: /Bookings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, int numberOfPeople)
        {
            if (numberOfPeople <= 0 || numberOfPeople > 20)
            {
                TempData["Message"] = "Number of people must be between 1 and 20.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (booking.UserId != user.Id) return Forbid();

            if (booking.Paid)
            {
                TempData["Message"] = "You cannot edit a booking after payment has been made.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (booking.Cancelled)
            {
                TempData["Message"] = "You cannot edit a cancelled booking.";
                return RedirectToAction(nameof(MyBookings));
            }

            var trip = booking.Trip;
            if (trip == null) return NotFound();

            // Calculate room difference
            int roomDifference = numberOfPeople - booking.NumberOfPeople;

            // Check if enough rooms available (if increasing number of people)
            if (roomDifference > 0 && trip.AvailableRooms < roomDifference)
            {
                TempData["Message"] = $"Not enough available rooms. Only {trip.AvailableRooms} room(s) available.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            // Update booking
            booking.NumberOfPeople = numberOfPeople;
            booking.TotalPrice = trip.Price * numberOfPeople;

            // Update available rooms
            trip.AvailableRooms -= roomDifference;

            await _context.SaveChangesAsync();

            TempData["Message"] = "Booking updated successfully.";
            return RedirectToAction(nameof(MyBookings));
        }

        [HttpGet]
        public async Task<IActionResult> Pay(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Only owner or admin can pay
            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (booking.UserId != user.Id && !isAdmin)
                return Forbid();

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking is cancelled and cannot be paid.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            return View(booking);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(
            int id,
            string cardNumber,
            string expiry,     // MM/YY
            string cvv,
            string cardHolder,
            string paymentReference)
        {
            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (booking.UserId != user.Id && !isAdmin)
                return Forbid();

            if (booking.Cancelled)
            {
                TempData["Message"] = "This booking is cancelled and cannot be paid.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            if (booking.Paid)
            {
                TempData["Message"] = "This booking is already paid.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            // Safety check: max 3 upcoming trips
            // Note: This count includes THIS booking already (since it exists), so allow up to 3 total.
            var now = DateTime.Now;
            var upcomingCount = await _context.Bookings
                .Include(b => b.Trip)
                .Where(b =>
                    b.UserId == booking.UserId &&
                    !b.Cancelled &&
                    b.Trip != null &&
                    b.Trip.StartDate > now)
                .CountAsync();

            if (upcomingCount > 3)
            {
                TempData["Message"] = "You cannot have more than 3 upcoming trips. Cancel one booking before paying.";
                return RedirectToAction(isAdmin ? "All" : "MyBookings");
            }

            // Validate credit card input (DO NOT STORE in DB)
            var cardNumberDigits = new string((cardNumber ?? "").Where(char.IsDigit).ToArray());
            if (cardNumberDigits.Length < 13 || cardNumberDigits.Length > 19)
            {
                TempData["Message"] = "Invalid card number.";
                return View(booking);
            }

            if (string.IsNullOrWhiteSpace(expiry) || expiry.Length != 5 || expiry[2] != '/')
            {
                TempData["Message"] = "Invalid expiry date. Use MM/YY.";
                return View(booking);
            }

            if (!int.TryParse(expiry.Substring(0, 2), out int mm) || mm < 1 || mm > 12)
            {
                TempData["Message"] = "Invalid expiry month.";
                return View(booking);
            }

            if (!int.TryParse(expiry.Substring(3, 2), out int yy))
            {
                TempData["Message"] = "Invalid expiry year.";
                return View(booking);
            }

            var cvvDigits = new string((cvv ?? "").Where(char.IsDigit).ToArray());
            if (cvvDigits.Length < 3 || cvvDigits.Length > 4)
            {
                TempData["Message"] = "Invalid CVV.";
                return View(booking);
            }

            if (string.IsNullOrWhiteSpace(cardHolder) || cardHolder.Trim().Length < 3)
            {
                TempData["Message"] = "Card holder name is required.";
                return View(booking);
            }

            // Mark paid (store ONLY safe info)
            booking.Paid = true;
            booking.PaymentDate = DateTime.Now;
            booking.PaymentReference = string.IsNullOrWhiteSpace(paymentReference)
                ? $"PAY-{Guid.NewGuid().ToString("N").Substring(0, 8)}"
                : paymentReference;

            await _context.SaveChangesAsync();

            // Send email notification after payment
            if (user.Email != null)
            {
                try
                {
                    var trip = booking.Trip;
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Payment Confirmation - Travel Booking",
                        $"<h2>Thank you for your payment!</h2>" +
                        $"<p>Your booking for <b>{trip?.Title}</b> has been confirmed.</p>" +
                        $"<p><b>Booking Details:</b></p>" +
                        $"<ul>" +
                        $"<li>Trip: {trip?.Title}</li>" +
                        $"<li>Destination: {trip?.Destination}, {trip?.Country}</li>" +
                        $"<li>Travel Dates: {trip?.StartDate:MM/dd/yyyy} to {trip?.EndDate:MM/dd/yyyy}</li>" +
                        $"<li>Number of People: {booking.NumberOfPeople}</li>" +
                        $"<li>Total Amount: ${booking.TotalPrice:F2}</li>" +
                        $"<li>Payment Reference: {booking.PaymentReference}</li>" +
                        $"</ul>" +
                        $"<p>You can view and download your itinerary from your bookings page.</p>"
                    );
                }
                catch (Exception ex)
                {
                    // Log error but don't fail the payment
                    _logger?.LogError(ex, "Failed to send payment confirmation email");
                }
            }

            TempData["Message"] = "Payment completed successfully. A confirmation email has been sent.";
            return RedirectToAction(isAdmin ? "All" : "MyBookings");
        }

        // GET: /Bookings/CheckoutFromCart
        public async Task<IActionResult> CheckoutFromCart()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var cartJson = TempData["CartCheckout"]?.ToString();
            if (string.IsNullOrEmpty(cartJson))
            {
                TempData["Message"] = "No items in cart to checkout.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Parse cart items
            var cartItems = System.Text.Json.JsonSerializer.Deserialize<List<Models.ShoppingCartItem>>(cartJson);
            if (cartItems == null || !cartItems.Any())
            {
                TempData["Message"] = "No items in cart to checkout.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Check booking time frame
            int daysBeforeTrip = 7;
            try
            {
                var settings = await _context.AdminSettings.FirstOrDefaultAsync();
                daysBeforeTrip = settings?.DaysBeforeTripLatestBooking ?? 7;
            }
            catch { }

            var now = DateTime.Now;
            var upcomingCount = await CountUpcomingBookingsAsync(user.Id);
            int remainingSlots = 3 - upcomingCount;

            var bookingsCreated = 0;
            var errors = new List<string>();

            foreach (var item in cartItems)
            {
                // Check remaining slots
                if (bookingsCreated >= remainingSlots)
                {
                    errors.Add($"Cannot book more than 3 upcoming trips. Added {bookingsCreated} trip(s) from cart.");
                    break;
                }

                var trip = await _context.Trips.FindAsync(item.TripId);
                if (trip == null)
                {
                    errors.Add($"Trip {item.TripId} not found.");
                    continue;
                }

                // Check booking time frame
                if (trip.StartDate <= DateTime.Today.AddDays(daysBeforeTrip))
                {
                    errors.Add($"{trip.Title}: Cannot book less than {daysBeforeTrip} days before departure.");
                    continue;
                }

                // Check rooms
                if (trip.AvailableRooms < item.NumberOfPeople)
                {
                    errors.Add($"{trip.Title}: Not enough available rooms ({trip.AvailableRooms} available, {item.NumberOfPeople} requested).");
                    continue;
                }

                // Check duplicate booking
                var alreadyBooked = await _context.Bookings
                    .AnyAsync(b => b.TripId == item.TripId && b.UserId == user.Id && !b.Cancelled);

                if (alreadyBooked)
                {
                    errors.Add($"{trip.Title}: You have already booked this trip.");
                    continue;
                }

                // Create booking
                try
                {
                    var booking = new Booking
                    {
                        TripId = item.TripId,
                        UserId = user.Id,
                        NumberOfPeople = item.NumberOfPeople,
                        BookingDate = DateTime.Now,
                        TotalPrice = trip.Price * item.NumberOfPeople,
                        Paid = false,
                        Cancelled = false,
                        PaymentReference = "PENDING"
                    };

                    trip.AvailableRooms -= item.NumberOfPeople;

                    _context.Bookings.Add(booking);
                    _context.Trips.Update(trip);

                    // Remove from waiting list if applicable
                    var myWaiting = await _context.WaitingList
                        .FirstOrDefaultAsync(w => w.TripId == item.TripId && w.UserId == user.Id);

                    if (myWaiting != null)
                    {
                        _context.WaitingList.Remove(myWaiting);
                    }

                    await _context.SaveChangesAsync();
                    bookingsCreated++;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Error creating booking for trip {item.TripId}");
                    errors.Add($"{trip.Title}: Error creating booking. Please try again.");
                }
            }

            // Clear cart session after checkout
            if (bookingsCreated > 0)
            {
                // Clear cart from session
                HttpContext.Session.Remove("ShoppingCart");

                TempData["Message"] = $"Successfully created {bookingsCreated} booking(s) from cart.";
                if (errors.Any())
                {
                    TempData["Message"] += " " + string.Join(" ", errors);
                }
                return RedirectToAction(nameof(MyBookings));
            }
            else
            {
                TempData["Message"] = "No bookings were created. " + string.Join(" ", errors);
                return RedirectToAction("Index", "ShoppingCart");
            }
        }

        // GET: /Bookings/DownloadItinerary/5
        public async Task<IActionResult> DownloadItinerary(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Trip)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            // Only owner or admin can download
            bool isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (booking.UserId != user.Id && !isAdmin)
                return Forbid();

            if (!booking.Paid)
            {
                TempData["Message"] = "You can only download itinerary after payment.";
                return RedirectToAction("MyBookings");
            }

            // Generate PDF itinerary using QuestPDF
            try
            {
                var pdfBytes = GenerateItineraryPdf(booking);
                return File(pdfBytes, "application/pdf", $"Itinerary_{booking.PaymentReference}.pdf");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error generating PDF itinerary");
                // Fallback to text format if PDF generation fails
                var itinerary = $"TRAVEL ITINERARY\n" +
                               $"================\n\n" +
                               $"Booking Reference: {booking.PaymentReference}\n" +
                               $"Booking Date: {booking.BookingDate:yyyy-MM-dd}\n\n" +
                               $"TRIP DETAILS\n" +
                               $"------------\n" +
                               $"Trip: {booking.Trip?.Title}\n" +
                               $"Destination: {booking.Trip?.Destination}, {booking.Trip?.Country}\n" +
                               $"Travel Dates: {booking.Trip?.StartDate:yyyy-MM-dd} to {booking.Trip?.EndDate:yyyy-MM-dd}\n" +
                               $"Package Type: {booking.Trip?.PackageType}\n\n" +
                               $"BOOKING DETAILS\n" +
                               $"---------------\n" +
                               $"Traveler: {booking.User?.FullName ?? user.Email}\n" +
                               $"Number of People: {booking.NumberOfPeople}\n" +
                               $"Total Price: ${booking.TotalPrice:F2}\n" +
                               $"Payment Date: {booking.PaymentDate:yyyy-MM-dd}\n\n" +
                               $"Thank you for choosing our travel service!\n";

                var bytes = System.Text.Encoding.UTF8.GetBytes(itinerary);
                return File(bytes, "text/plain", $"Itinerary_{booking.PaymentReference}.txt");
            }
        }

        private byte[] GenerateItineraryPdf(Booking booking)
        {
            // Set QuestPDF license (Community edition - free)
            QuestPDF.Settings.License = LicenseType.Community;

            using var stream = new MemoryStream();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .AlignCenter()
                        .Text("TRAVEL ITINERARY")
                        .FontSize(20)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(column =>
                        {
                            column.Spacing(10);

                            // Booking Reference
                            column.Item()
                                .Background(Colors.Grey.Lighten3)
                                .Padding(10)
                                .Row(row =>
                                {
                                    row.ConstantItem(100).Text("Reference:").Bold();
                                    row.RelativeItem().Text(booking.PaymentReference);
                                });

                            column.Item().PaddingTop(10);

                            // Trip Details Section
                            column.Item()
                                .Text("TRIP DETAILS")
                                .FontSize(14)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);

                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(80);
                                    columns.RelativeColumn();
                                });

                                if (booking.Trip != null)
                                {
                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Trip:");
                                    table.Cell().Element(ApplyCellStyle).Text(booking.Trip.Title);

                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Destination:");
                                    table.Cell().Element(ApplyCellStyle).Text($"{booking.Trip.Destination}, {booking.Trip.Country}");

                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Departure:");
                                    table.Cell().Element(ApplyCellStyle).Text(booking.Trip.StartDate.ToString("MMMM dd, yyyy"));

                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Return:");
                                    table.Cell().Element(ApplyCellStyle).Text(booking.Trip.EndDate.ToString("MMMM dd, yyyy"));

                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Duration:");
                                    var duration = (booking.Trip.EndDate - booking.Trip.StartDate).Days;
                                    table.Cell().Element(ApplyCellStyle).Text($"{duration} day(s)");

                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Package:");
                                    table.Cell().Element(ApplyCellStyle).Text(booking.Trip.PackageType);
                                }
                            });

                            column.Item().PaddingTop(10);

                            // Booking Details Section
                            column.Item()
                                .Text("BOOKING DETAILS")
                                .FontSize(14)
                                .Bold()
                                .FontColor(Colors.Blue.Darken2);

                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(120);
                                    columns.RelativeColumn();
                                });

                                table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Traveler:");
                                table.Cell().Element(ApplyCellStyle).Text(booking.User?.FullName ?? booking.User?.Email ?? "N/A");

                                table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Booking Date:");
                                table.Cell().Element(ApplyCellStyle).Text(booking.BookingDate.ToString("MMMM dd, yyyy"));

                                table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Number of People:");
                                table.Cell().Element(ApplyCellStyle).Text(booking.NumberOfPeople.ToString());

                                table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Total Price:");
                                table.Cell().Element(ApplyCellStyle).Text($"${booking.TotalPrice:F2}");

                                if (booking.PaymentDate.HasValue)
                                {
                                    table.Cell().Element(ApplyCellStyle).DefaultTextStyle(x => x.Bold()).Text("Payment Date:");
                                    table.Cell().Element(ApplyCellStyle).Text(booking.PaymentDate.Value.ToString("MMMM dd, yyyy"));
                                }
                            });

                            column.Item().PaddingTop(20);

                            // Footer message
                            column.Item()
                                .AlignCenter()
                                .Text("Thank you for choosing our travel service!")
                                .Italic()
                                .FontColor(Colors.Grey.Medium);
                        });

                    page.Footer()
                        .AlignCenter()
                        .DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium))
                        .Text(x =>
                        {
                            x.Span("Generated on: ");
                            x.Span(DateTime.Now.ToString("MMMM dd, yyyy HH:mm"));
                        });
                });
            })
            .GeneratePdf(stream);

            return stream.ToArray();
        }

        private static IContainer ApplyCellStyle(IContainer container)
        {
            return container
                .BorderBottom(1)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(5)
                .DefaultTextStyle(x => x.FontSize(10));
        }

        private async Task<int> CountUpcomingBookingsAsync(string userId)
        {
            var now = DateTime.Now;

            return await _context.Bookings
                .Include(b => b.Trip)
                .Where(b =>
                    b.UserId == userId &&
                    b.Trip != null &&
                    b.Trip.StartDate > now &&
                    b.Cancelled == false)
                .CountAsync();
        }
    }
}
