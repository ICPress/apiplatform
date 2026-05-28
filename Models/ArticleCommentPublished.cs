using System.Text.Json.Serialization;

public class ArticleCommentPublished : ArticleComment, IAuthorEntityPublished
{
    public ArticleCommentPublished() { }

    public ArticleCommentPublished(
        string authorName, string slugTitle, string commentUUID,
        string? reply_to_comment_uuid, string? reply_to_username,
        string comment,
        bool hidden,
        bool deleted,
        string langCode, string timestamp, bool liked)
    {
        this.AuthorName = authorName;
        this.Comment = comment;
        this.CommentUUID = commentUUID;
        this.ReplyToCommentUUID = reply_to_comment_uuid;
        this.SlugTitle = slugTitle;
        this.Hidden = hidden;
        this.Deleted = deleted;
        this.LangCode = langCode;
        this.Timestamp = timestamp;
        this.Liked = liked;
        this.ReplyToUsername = reply_to_username;
    }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; } = false;

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = false;

    [JsonPropertyName("langcode")]
    public string LangCode { get; set; } = "";

    [JsonPropertyName("hearts")]
    public uint Hearts { get; set; } = 0u;

    [JsonPropertyName("numReplies")]
    public uint NumReplies { get; set; } = 0u;

    [JsonPropertyName("replies")]
    public List<ArticleCommentPublished> Replies { get; set; } = new List<ArticleCommentPublished>();

    [JsonPropertyName("authorBadge")]
    public string? AuthorBadge { get; set; } = null;

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; } = null;

    [JsonPropertyName("liked")]
    public bool Liked { get; set; } = false;

    [JsonPropertyName("reply_to_username")]
    public string? ReplyToUsername { get; set; } = null;
}
