using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel_Agency_Service.Data;
using Travel_Agency_Service.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Travel_Agency_Service.Services
{
    /// <summary>
    /// Background service to send automatic trip reminders to users
    /// Checks daily for upcoming trips and sends reminders based on admin settings
    /// </summary>
    public class TripReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TripReminderService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24); // Check once per day

        public TripReminderService(IServiceProvider serviceProvider, ILogger<TripReminderService> logger, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Trip Reminder Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendTripRemindersAsync();

                    // Wait for the check interval, but handle cancellation gracefully
                    try
                    {
                        await Task.Delay(_checkInterval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown - exit gracefully
                        _logger.LogInformation("Trip Reminder Service is shutting down.");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown - exit gracefully
                    _logger.LogInformation("Trip Reminder Service is shutting down.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Trip Reminder Service");
                    // Wait before retrying to avoid tight loop, but handle cancellation
                    try
                    {
                        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown - exit gracefully
                        _logger.LogInformation("Trip Reminder Service is shutting down.");
                        break;
                    }
                }
            }

            _logger.LogInformation("Trip Reminder Service stopped.");
        }

        private async Task SendTripRemindersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();

            try
            {
                // First, check and expire old waiting list notifications (default 3 days)
                await ExpireOldWaitingListNotificationsAsync(context, emailSender);

                // Get admin settings for reminder days - handle if table doesn't exist yet
                AdminSettings? settings = null;
                try
                {
                    settings = await context.AdminSettings.FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AdminSettings table may not exist yet. Using default reminder days.");
                }

                int reminderDays = settings?.DaysBeforeTripReminder ?? 5;

                var targetDate = DateTime.Today.AddDays(reminderDays);

                // Find all paid, non-cancelled bookings where:
                // 1. Trip start date is exactly X days from now (reminder date)
                // 2. Booking is paid
                // 3. Booking is not cancelled
                // 4. Reminder hasn't been sent yet (we'll track this in Booking model or send once)

                var bookingsNeedingReminder = await context.Bookings
                    .Include(b => b.Trip)
                    .Include(b => b.User)
                    .Where(b =>
                        b.Trip != null &&
                        b.Trip.StartDate.Date == targetDate.Date &&
                        b.Paid &&
                        !b.Cancelled &&
                        b.Trip.StartDate > DateTime.Now) // Only future trips
                    .ToListAsync();

                if (!bookingsNeedingReminder.Any())
                {
                    _logger.LogInformation($"No reminders to send for trips starting on {targetDate:yyyy-MM-dd}");
                    return;
                }

                int sentCount = 0;
                foreach (var booking in bookingsNeedingReminder)
                {
                    try
                    {
                        if (booking.User != null && !string.IsNullOrWhiteSpace(booking.User.Email) && booking.Trip != null)
                        {
                            var daysUntilTrip = (booking.Trip.StartDate.Date - DateTime.Today).Days;

                            var emailBody = $@"
                                <h2>Trip Reminder: {booking.Trip.Title}</h2>
                                <p>Hello {booking.User.FullName ?? booking.User.Email},</p>
                                <p>This is a friendly reminder that your trip is coming up soon!</p>
                                <div style='background-color: #f0f4ff; padding: 20px; border-radius: 10px; margin: 20px 0;'>
                                    <h3>Trip Details:</h3>
                                    <ul>
                                        <li><strong>Trip:</strong> {booking.Trip.Title}</li>
                                        <li><strong>Destination:</strong> {booking.Trip.Destination}, {booking.Trip.Country}</li>
                                        <li><strong>Departure Date:</strong> {booking.Trip.StartDate:MMMM dd, yyyy}</li>
                                        <li><strong>Return Date:</strong> {booking.Trip.EndDate:MMMM dd, yyyy}</li>
                                        <li><strong>Number of Travelers:</strong> {booking.NumberOfPeople}</li>
                                        <li><strong>Days Remaining:</strong> {daysUntilTrip} day(s)</li>
                                    </ul>
                                </div>
                                <p>Please make sure you have all necessary documents ready and check your booking details in your account.</p>
                                <p>We look forward to seeing you soon!</p>
                                <p>Best regards,<br/>Travel Agency Service Team</p>";

                            await emailSender.SendEmailAsync(
                                booking.User.Email,
                                $"Reminder: Your trip to {booking.Trip.Destination} is in {daysUntilTrip} day(s)!",
                                emailBody
                            );

                            sentCount++;
                            _logger.LogInformation($"Reminder sent to {booking.User.Email} for trip {booking.Trip.Title} (ID: {booking.Id})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to send reminder for booking ID {booking.Id}");
                    }
                }

                _logger.LogInformation($"Trip reminder service: Sent {sentCount} reminder(s) for trips starting on {targetDate:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendTripRemindersAsync");
                throw;
            }
        }

        /// <summary>
        /// Removes users from waiting list who were notified but didn't book within the expiration period
        /// Default expiration: 3 days (configurable via AdminSettings)
        /// </summary>
        private async Task ExpireOldWaitingListNotificationsAsync(ApplicationDbContext context, Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender)
        {
            try
            {
                // Get expiration days from admin settings (default 3 days)
                int expirationDays = 3;
                try
                {
                    var settings = await context.AdminSettings.FirstOrDefaultAsync();
                    expirationDays = settings?.WaitingListNotificationExpirationDays ?? 3;
                }
                catch
                {
                    // Use default if settings don't exist
                }

                var expirationDate = DateTime.Now.AddDays(-expirationDays);

                // Find all notified users who haven't booked within expiration period
                var expiredNotifications = await context.WaitingList
                    .Include(w => w.User)
                    .Include(w => w.Trip)
                    .Where(w =>
                        w.Notified &&
                        w.NotifiedAt.HasValue &&
                        w.NotifiedAt.Value < expirationDate)
                    .ToListAsync();

                if (expiredNotifications.Any())
                {
                    _logger.LogInformation($"Found {expiredNotifications.Count} expired waiting list notifications. Removing...");

                    foreach (var expired in expiredNotifications)
                    {
                        // Notify next user if there's still a room available
                        var trip = expired.Trip;
                        if (trip != null && trip.AvailableRooms > 0)
                        {
                            var next = await context.WaitingList
                                .Include(w => w.User)
                                .Where(w => w.TripId == trip.Id && !w.Notified && w.Id != expired.Id)
                                .OrderBy(w => w.JoinedAt)
                                .FirstOrDefaultAsync();

                            if (next != null && next.User != null && !string.IsNullOrWhiteSpace(next.User.Email))
                            {
                                try
                                {
                                    // Get base URL from configuration or use default
                                    var baseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://localhost:5001";
                                    var bookingUrl = $"{baseUrl}/Trips/Details/{trip.Id}";

                                    var emailBody = $@"
                                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                                            <h2 style='color: #1e88e5;'>🎉 Room Available - Your Turn!</h2>
                                            <p>Hi {next.User.FullName ?? "there"},</p>
                                            <p>A room has become available for <b>{trip.Title}</b>. The previous person didn't book in time, so it's now your turn!</p>
                                            <div style='background-color: #f0f8ff; padding: 20px; border-radius: 10px; margin: 20px 0;'>
                                                <h3 style='color: #1e88e5;'>Trip: {trip.Title}</h3>
                                                <p><strong>Destination:</strong> {trip.Destination}, {trip.Country}</p>
                                                <p><strong>Departure:</strong> {trip.StartDate:MMMM dd, yyyy}</p>
                                                <p><strong>Price:</strong> {trip.Price:C} per person</p>
                                            </div>
                                            <div style='text-align: center; margin: 30px 0;'>
                                                <a href='{bookingUrl}' style='background-color: #1e88e5; color: white; padding: 15px 30px; text-decoration: none; border-radius: 8px; font-weight: bold; display: inline-block;'>
                                                    📋 Book Now!
                                                </a>
                                            </div>
                                            <p style='color: #d32f2f; font-weight: bold;'>⚠️ Please book soon to secure your spot!</p>
                                        </div>";

                                    await emailSender.SendEmailAsync(
                                        next.User.Email,
                                        $"🎉 Room Available: {trip.Title} - Book Now!",
                                        emailBody
                                    );

                                    next.Notified = true;
                                    next.NotifiedAt = DateTime.Now;
                                    _logger.LogInformation($"Notified next user {next.User.Email} for trip {trip.Title}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Failed to notify next user for trip {trip.Id}");
                                    // Continue with removal even if notification fails
                                }
                            }
                        }

                        // Remove expired notification
                        context.WaitingList.Remove(expired);
                        var userEmail = expired.User?.Email ?? expired.UserId;
                        var tripTitle = trip?.Title ?? $"Trip ID {expired.TripId}";
                        _logger.LogInformation($"Removed expired waiting list entry for user {userEmail} (trip {tripTitle})");
                    }

                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Successfully processed {expiredNotifications.Count} expired waiting list notifications.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error expiring old waiting list notifications");
                // Don't throw - this is a background maintenance task
            }
        }
    }
}
