using System.Text.Json.Serialization;

public class TransactionModel
{
    public TransactionModel(uint transactionId, string timestamp, ushort descriptionType, int amount, ushort transactionType, string additionalData)
    {
        this.TransactionId = transactionId;
        this.Timestamp = timestamp;
        this.DescriptionType = descriptionType;
        this.Amount = amount;
        this.TransactionType = transactionType;
        this.AdditionalData = additionalData;
    }

    [JsonPropertyName("transactionId")]
    public uint TransactionId { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; }

    [JsonPropertyName("descriptionType")]
    public ushort DescriptionType { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("transactionType")]
    public ushort TransactionType { get; set; }

    [JsonPropertyName("additionalData")]
    public string AdditionalData { get; set; }
}
