namespace KrishAgent.Models
{
    public class AlertRequest
    {
        public string Symbol { get; set; } = string.Empty;
        public string AlertType { get; set; } = string.Empty;
        public decimal Threshold { get; set; }
        public string? Condition { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? ExpiresAt { get; set; }
    }
}
