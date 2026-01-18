using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    /// <summary>
    /// Admin settings for booking time frames and cancellation rules
    /// </summary>
    public class AdminSettings
    {
        public int Id { get; set; }

        [Display(Name = "Days Before Trip - Latest Booking Date")]
        [Range(0, 365)]
        public int DaysBeforeTripLatestBooking { get; set; } = 7; // Can't book less than 7 days before trip

        [Display(Name = "Days Before Trip - Cancellation Deadline")]
        [Range(0, 365)]
        public int DaysBeforeTripCancellationDeadline { get; set; } = 5; // Can't cancel less than 5 days before trip

        [Display(Name = "Days Before Trip - Reminder Sent")]
        [Range(0, 365)]
        public int DaysBeforeTripReminder { get; set; } = 5; // Send reminder 5 days before trip

        [Display(Name = "Maximum Discount Duration (Days)")]
        [Range(1, 7)]
        public int MaxDiscountDurationDays { get; set; } = 7; // Discounts expire after max 7 days

        [Display(Name = "Waiting List Notification Expiration (Days)")]
        [Range(1, 14)]
        public int WaitingListNotificationExpirationDays { get; set; } = 3; // Notified users have 3 days to book

        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}