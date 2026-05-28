using System.Text.Json.Serialization;

public class ImageInfoMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 0;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 0;

    [JsonPropertyName("minWidth")]
    public int? MinWidth { get; set; } = null;

    [JsonPropertyName("minHeight")]
    public int? MinHeight { get; set; } = null;
}
