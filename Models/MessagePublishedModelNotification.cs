using System.Text.Json.Serialization;

public class MessagePublishedNotificationModel : MessagePublishedModel
{
    public MessagePublishedNotificationModel(
        uint messageId, ushort messageType, string content, string authorName, 
        string messageUUID, string targetUsername, bool deleted, string timestamp, 
        bool contactApproved, int additionalMessagess, bool read)
        : base(messageId, messageType, content, authorName, messageUUID, targetUsername, deleted, read, timestamp)
    {
        this.ContactApproved = contactApproved;
        this.AdditionalMessages = additionalMessagess;
    }

    [JsonPropertyName("contactapproved")]
    public bool ContactApproved { get; set; } = false;

    [JsonPropertyName("additionalmessages")]
    public int AdditionalMessages { get; set; } = 0;
}
