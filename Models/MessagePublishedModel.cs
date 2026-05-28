using System.Text.Json.Serialization;

public class MessagePublishedModel : MessageModel
{
    public MessagePublishedModel(uint messageId, ushort messageType,
     string content, string authorName, string messageUUID, string targetUsername, bool deleted, bool read, string timestamp)
     : base(messageId, messageType, content, messageUUID, targetUsername, authorName)
    {
        this.MessageId = messageId;
        this.AuthorName = authorName;
        this.MessageType = messageType;
        this.Content = content;
        this.MessageUUID = messageUUID;
        this.TargetUsername = targetUsername;
        this.Deleted = deleted;
        this.Read = read;
        this.Timestamp = timestamp;
    }

    [JsonPropertyName("read")]
    public bool Read { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; }
}
