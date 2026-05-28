using System.Text.Json.Serialization;

public interface IAuthorEntity
{
    [JsonPropertyName("authorName")]
    string AuthorName { get; set; }
}
