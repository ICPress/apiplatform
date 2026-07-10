using System.Text.Json.Serialization;
using System.Collections.Generic;

public class PublicSourceModel
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("urlTitle")]
    public string? UrlTitle { get; set; } = null;

    // Resolved server-side by looking up the source host in known_public_sources.
    // Left null when the host is not a recognised source. Any value sent by the
    // client is ignored/overwritten on publish/update.
    [JsonPropertyName("sourceId")]
    public int? SourceId { get; set; } = null;

    // Populated server-side (read-only) from known_public_sources.source_name when
    // an article is fetched. Never accepted from the client and never written back
    // to the database — always reset to null on publish/update.
    [JsonPropertyName("sourceName")]
    public string? SourceName { get; set; } = null;

    [JsonPropertyName("references")]
    public List<PublicSourceReferenceModel>? References { get; set; } = null;
}
