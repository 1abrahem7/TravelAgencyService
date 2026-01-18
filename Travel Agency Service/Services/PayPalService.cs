using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travel_Agency_Service.Models;

namespace Travel_Agency_Service.Services
{
    /// <summary>
    /// PayPal REST API v2 integration service
    /// Uses PayPal Orders API for checkout
    /// </summary>
    public class PayPalService : IPayPalService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PayPalService> _logger;
        private readonly HttpClient _httpClient;
        private string? _cachedAccessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        // PayPal API endpoints
        private string BaseUrl => _configuration["PayPal:UseSandbox"] == "true"
            ? "https://api.sandbox.paypal.com"
            : "https://api.paypal.com";

        private string ClientId => _configuration["PayPal:ClientId"] ?? "";
        private string ClientSecret => _configuration["PayPal:ClientSecret"] ?? "";
        private bool IsEnabled => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

        public PayPalService(IConfiguration configuration, ILogger<PayPalService> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            // Return cached token if still valid
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedAccessToken;
            }

            if (!IsEnabled)
            {
                throw new InvalidOperationException("PayPal is not configured. Please set PayPal:ClientId and PayPal:ClientSecret in appsettings.json");
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"))
                );

                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<PayPalTokenResponse>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tokenResponse?.AccessToken == null)
                {
                    throw new Exception("Failed to get PayPal access token");
                }

                _cachedAccessToken = tokenResponse.AccessToken;
                // Set expiry to 5 minutes before actual expiry for safety
                _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300);

                return _cachedAccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting PayPal access token");
                throw;
            }
        }

        public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description, string returnUrl, string cancelUrl)
        {
            if (!IsEnabled)
            {
                _logger.LogWarning("PayPal is not configured. Falling back to simulation mode.");
                return new PayPalOrderResult
                {
                    Success = false,
                    ErrorMessage = "PayPal is not configured"
                };
            }

            try
            {
                var accessToken = await GetAccessTokenAsync();

                var orderRequest = new
                {
                    intent = "CAPTURE",
                    purchase_units = new[]
                    {
                        new
                        {
                            amount = new
                            {
                                currency_code = currency,
                                value = amount.ToString("F2")
                            },
                            description = description
                        }
                    },
                    application_context = new
                    {
                        return_url = returnUrl,
                        cancel_url = cancelUrl,
                        brand_name = "Travel Agency Service",
                        user_action = "PAY_NOW"
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(
                    JsonSerializer.Serialize(orderRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("PayPal order creation failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    return new PayPalOrderResult
                    {
                        Success = false,
                        ErrorMessage = $"PayPal API error: {response.StatusCode}"
                    };
                }

                var orderResponse = JsonSerializer.Deserialize<PayPalOrderResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (orderResponse?.Id == null || orderResponse.Links == null)
                {
                    return new PayPalOrderResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid PayPal order response"
                    };
                }

                var approvalLink = orderResponse.Links.FirstOrDefault(l => l.Rel == "approve");
                if (approvalLink == null)
                {
                    return new PayPalOrderResult
                    {
                        Success = false,
                        ErrorMessage = "PayPal approval URL not found"
                    };
                }

                return new PayPalOrderResult
                {
                    Success = true,
                    OrderId = orderResponse.Id,
                    ApprovalUrl = approvalLink.Href
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating PayPal order");
                return new PayPalOrderResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<PayPalCaptureResult> CaptureOrderAsync(string orderId)
        {
            if (!IsEnabled)
            {
                return new PayPalCaptureResult
                {
                    Success = false,
                    ErrorMessage = "PayPal is not configured"
                };
            }

            try
            {
                var accessToken = await GetAccessTokenAsync();

                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders/{orderId}/capture");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("PayPal order capture failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    return new PayPalCaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"PayPal API error: {response.StatusCode}"
                    };
                }

                var captureResponse = JsonSerializer.Deserialize<PayPalCaptureResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (captureResponse?.Status != "COMPLETED")
                {
                    return new PayPalCaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"Payment not completed. Status: {captureResponse?.Status}"
                    };
                }

                var purchaseUnit = captureResponse.PurchaseUnits?.FirstOrDefault();
                var capture = purchaseUnit?.Payments?.Captures?.FirstOrDefault();

                return new PayPalCaptureResult
                {
                    Success = true,
                    PaymentId = capture?.Id,
                    PayerId = captureResponse.Payer?.PayerId,
                    Amount = decimal.TryParse(capture?.Amount?.Value, out var amount) ? amount : null,
                    Currency = capture?.Amount?.CurrencyCode
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing PayPal order");
                return new PayPalCaptureResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        // Helper classes for JSON deserialization
        private class PayPalTokenResponse
        {
            public string? AccessToken { get; set; }
            public int ExpiresIn { get; set; }
        }

        private class PayPalOrderResponse
        {
            public string? Id { get; set; }
            public List<PayPalLink>? Links { get; set; }
        }

        private class PayPalLink
        {
            public string? Href { get; set; }
            public string? Rel { get; set; }
        }

        private class PayPalCaptureResponse
        {
            public string? Status { get; set; }
            public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
            public PayPalPayer? Payer { get; set; }
        }

        private class PayPalPurchaseUnit
        {
            public PayPalPayments? Payments { get; set; }
        }

        private class PayPalPayments
        {
            public List<PayPalCapture>? Captures { get; set; }
        }

        private class PayPalCapture
        {
            public string? Id { get; set; }
            public PayPalAmount? Amount { get; set; }
        }

        private class PayPalAmount
        {
            public string? Value { get; set; }
            public string? CurrencyCode { get; set; }
        }

        private class PayPalPayer
        {
            public string? PayerId { get; set; }
        }
    }
}
