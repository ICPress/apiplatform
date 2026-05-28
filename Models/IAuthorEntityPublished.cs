using System.Text.Json.Serialization;

public interface IAuthorEntityPublished 
{
    [JsonPropertyName("authorBadge")]
    string? AuthorBadge { get; set; }
}
