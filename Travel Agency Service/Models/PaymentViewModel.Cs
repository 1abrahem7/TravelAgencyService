using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    public class PaymentViewModel
    {
        [Required]
        public int BookingId { get; set; }

        [Required]
        [Display(Name = "Card holder name")]
        public string CardHolderName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Card number")]
        [RegularExpression(@"^\d{16}$", ErrorMessage = "Card number must be 16 digits.")]
        public string CardNumber { get; set; } = string.Empty;

        [Required]
        [Range(1, 12)]
        [Display(Name = "Expiry month")]
        public int ExpiryMonth { get; set; }

        [Required]
        [Range(2024, 2040)]
        [Display(Name = "Expiry year")]
        public int ExpiryYear { get; set; }

        [Required]
        [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits.")]
        public string CVV { get; set; } = string.Empty;
    }
}
