using System.Text.Json.Serialization;

public class TrendingTagModel
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("usage")]
    public int Usage { get; set; }

    [JsonPropertyName("previousUsage")]
    public int PreviousUsage { get; set; }

    [JsonPropertyName("percentageChange")]
    public double PercentageChange { get; set; }
}
