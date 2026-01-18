using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Services
{
    /// <summary>
    /// Service interface for PayPal payment integration
    /// </summary>
    public interface IPayPalService
    {
        /// <summary>
        /// Creates a PayPal order and returns the approval URL for redirect
        /// </summary>
        Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description, string returnUrl, string cancelUrl);

        /// <summary>
        /// Captures a PayPal order after user approval
        /// </summary>
        Task<PayPalCaptureResult> CaptureOrderAsync(string orderId);

        /// <summary>
        /// Gets the access token from PayPal OAuth
        /// </summary>
        Task<string> GetAccessTokenAsync();
    }

    /// <summary>
    /// Result from PayPal order creation
    /// </summary>
    public class PayPalOrderResult
    {
        public bool Success { get; set; }
        public string? OrderId { get; set; }
        public string? ApprovalUrl { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result from PayPal order capture
    /// </summary>
    public class PayPalCaptureResult
    {
        public bool Success { get; set; }
        public string? PaymentId { get; set; }
        public string? PayerId { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
