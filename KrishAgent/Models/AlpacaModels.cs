using System.Text.Json.Serialization;

namespace KrishAgent.Models
{
    public class AlpacaBar
    {
        [JsonPropertyName("t")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("o")]
        public decimal Open { get; set; }

        [JsonPropertyName("h")]
        public decimal High { get; set; }

        [JsonPropertyName("l")]
        public decimal Low { get; set; }

        [JsonPropertyName("c")]
        public decimal Close { get; set; }

        [JsonPropertyName("v")]
        public long Volume { get; set; }
    }

    public class AlpacaBarsResponse
    {
        [JsonPropertyName("bars")]
        public List<AlpacaBar> Bars { get; set; } = [];
    }
}
