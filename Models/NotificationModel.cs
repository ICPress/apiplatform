using System.Text.Json.Serialization;

public class NotificationModel
{
    public NotificationModel(uint notificationId, ushort notificationType,
     string additionalData, uint transactionDescriptionType, string timestamp, bool notificationRead)
    {
        this.NotificationId = notificationId;
        this.NotificationType = notificationType;
        this.AdditionalData = additionalData;
        this.TransactionDescriptionType = transactionDescriptionType;
        this.Timestamp = timestamp;
        this.NotificationRead = notificationRead;
    }

    [JsonPropertyName("notificationId")]
    public uint NotificationId { get; set; }

    [JsonPropertyName("notificationType")]
    public ushort NotificationType { get; set; }

    [JsonPropertyName("additionalData")]
    public string AdditionalData { get; set; }

    [JsonPropertyName("transactionDescriptionType")]
    public uint TransactionDescriptionType { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; }

    [JsonPropertyName("triggerAuthor")]
    public string? TriggerAuthor { get; set; } = null;

    [JsonPropertyName("profileIcon")]
    public string? ProfileIcon { get; set; } = null;

    [JsonPropertyName("storyTitle")]
    public string? StoryTitle { get; set; } = null;

    [JsonPropertyName("notificationRead")]
    public bool NotificationRead { get; set; } = false;
}
