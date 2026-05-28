using System.Text.Json.Serialization;

public class ArticleCommentLikeReplyNotification
{
    public ArticleCommentLikeReplyNotification(ArticleCommentPublished comment, ArticleCommentPublished? commentReply, ArticleCommentPublished? notificationReply)
    {
        this.Comment = comment;
        this.ReplyToComment = commentReply;
        this.NotificationReply = notificationReply;
    }

    [JsonPropertyName("comment")]
    public ArticleCommentPublished Comment { get; set; }

    [JsonPropertyName("replytocomment")]
    public ArticleCommentPublished? ReplyToComment { get; set; }

    [JsonPropertyName("notificationreply")]
    public ArticleCommentPublished? NotificationReply { get; set; }
}
