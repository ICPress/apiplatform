using System.Text.Json.Serialization;

public record ArticlePrivateSource(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address = "",
    [property: JsonPropertyName("phone")] string Phone = "",
    [property: JsonPropertyName("notes")] string Notes = ""
);
