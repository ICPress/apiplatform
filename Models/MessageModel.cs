using System.Text.Json.Serialization;

public class MessageModel : IAuthorEntity
{
    public MessageModel(uint messageId, ushort messageType, string content, string messageUUID, string targetUsername, string authorName)
    {
        this.MessageId = messageId;
        this.MessageType = messageType;
        this.Content = content;
        this.MessageUUID = messageUUID;
        this.TargetUsername = targetUsername;
        this.AuthorName = authorName;
    }

    [JsonPropertyName("messageId")] public uint MessageId { get; set; }
    [JsonPropertyName("messageType")] public ushort MessageType { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; }
    [JsonPropertyName("messageUUID")] public string MessageUUID { get; set; }
    [JsonPropertyName("targetUsername")] public string? TargetUsername { get; set; }
    [JsonPropertyName("authorName")] public string AuthorName { get; set; } = "";
}