using Microsoft.AspNetCore.Identity.UI.Services;
using System;
using System.Threading.Tasks;

namespace Travel_Agency_Service.Services
{
    /// <summary>
    /// Simple email sender implementation for development/testing
    /// In production, configure SMTP settings in appsettings.json
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // For development: just log the email
            // In production, implement actual SMTP sending
            Console.WriteLine($"=== EMAIL SENT ===");
            Console.WriteLine($"To: {email}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine($"Message: {htmlMessage}");
            Console.WriteLine($"==================");
            
            return Task.CompletedTask;
        }
    }
}