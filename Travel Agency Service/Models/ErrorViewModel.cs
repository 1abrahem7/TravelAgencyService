namespace Travel_Agency_Service.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
