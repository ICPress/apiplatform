using System.Text.Json.Serialization;
using System.Runtime.Serialization;

[DataContract]
public class SpanInfoModel
{
    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("style")]
    public TextStyleModel Style { get; set; }

    [JsonPropertyName("additionalinfoflag")]
    public string? AdditionalInfoFlag { get; set; } = null;
}
