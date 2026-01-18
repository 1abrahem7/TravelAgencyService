using System;
using System.ComponentModel.DataAnnotations;

namespace Travel_Agency_Service.Models
{
    /// <summary>
    /// Shopping cart item stored in session
    /// </summary>
    public class ShoppingCartItem
    {
        public int TripId { get; set; }
        public string TripTitle { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int NumberOfPeople { get; set; } = 1;
        public decimal TotalPrice => Price * NumberOfPeople;
        public string ImageUrl { get; set; } = string.Empty;
    }
}