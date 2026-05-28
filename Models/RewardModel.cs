using System.Text.Json.Serialization;

public class RewardModel
{
    public RewardModel(uint rewardId, string rewardName, uint rewardPrice, 
    ushort rewardType, ushort rewardRarity, string? imagePath, string availableUntil, string? rewardMetadata, string description)
    {
        this.RewardId = rewardId;
        this.RewardName = rewardName;
        this.RewardPrice = rewardPrice;
        this.RewardRarity = rewardRarity;
        this.RewardType = rewardType;
        this.RewardMetadata = rewardMetadata;
        this.ImagePath = imagePath;
        this.AvailableUntil = availableUntil;
        this.Description = description;
    }

    [JsonPropertyName("rewardId")]
    public uint RewardId { get; set; }

    [JsonPropertyName("rewardName")]
    public string RewardName { get; set; }

    [JsonPropertyName("rewardPrice")]
    public uint RewardPrice { get; set; }

    [JsonPropertyName("rewardType")]
    public ushort RewardType { get; set; }

    [JsonPropertyName("rewardRarity")]
    public ushort RewardRarity { get; set; }

    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; } = null;

    [JsonPropertyName("availableUntil")]
    public string AvailableUntil { get; set; }

    [JsonPropertyName("rewardMetadata")]
    public string? RewardMetadata { get; set; } = null;

    [JsonPropertyName("description")]
    public string Description { get; set; }
}
