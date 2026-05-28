using System.Text.Json.Serialization;

public class ArticleComment : IAuthorEntity
{
    [JsonPropertyName("slugTitle")]
    public string SlugTitle { get; set; } = "";

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = "";

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";

    [JsonPropertyName("commentUUID")]
    public string CommentUUID { get; set; } = "";

    [JsonPropertyName("replyToCommentUUID")]
    public string? ReplyToCommentUUID { get; set; } = null;
}
