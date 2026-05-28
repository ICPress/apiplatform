using System.Text.Json.Serialization;
using System.Collections.Generic;

public class StylingInfoModel
{
    [JsonPropertyName("titleBackgroundColor")]
    public int TitleBackgroundColor { get; set; }

    [JsonPropertyName("titleHighlightColor")]
    public int TitleHighlightColor { get; set; }

    [JsonPropertyName("titleBackgroundImage")]
    public string? TitleBackgroundImage { get; set; } = null;

    [JsonPropertyName("spans")]
    public List<SpanInfoModel>? Spans { get; set; }
}
