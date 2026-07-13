using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

public class SourceModel
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("urlTitle")]
    public string? UrlTitle { get; set; } = null;


    [JsonPropertyName("sourceId")]
    public int? SourceId { get; set; } = null;

    [JsonPropertyName("sourceName")]
    public string? SourceName { get; set; } = null;

    [JsonPropertyName("references")]
    public List<ReferenceModel>? References { get; set; } = null;

    [JsonPropertyName("published")]
    public DateTime? Published { get; set; } = null;
}
