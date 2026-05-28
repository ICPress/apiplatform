using System.Text.Json.Serialization;

public class TransferRequestItemModel
{
    [JsonPropertyName("claimid")]
    public uint? ClaimId { get; set; } = null;

    [JsonPropertyName("rewardid")]
    public uint? RewardId { get; set; } = null;
}
