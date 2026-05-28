using System.Text.Json.Serialization;

public class RewardClaimedModel : RewardModel
{
    public RewardClaimedModel(RewardModel reward, bool walletTransferable, string? tranferedDate, uint claimId, uint? transferRequestId)
      : base(reward.RewardId, reward.RewardName, reward.RewardPrice, reward.RewardType, reward.RewardRarity,
      reward.ImagePath, reward.AvailableUntil, reward.RewardMetadata, reward.Description)
    {
        this.WalletTransferable = walletTransferable;
        this.TransferedDate = tranferedDate;
        this.ClaimId = claimId;
        this.TransferRequestId = transferRequestId;
    }

    [JsonPropertyName("claimId")]
    public uint ClaimId { get; set; }

    [JsonPropertyName("walletTransferable")]
    public bool WalletTransferable { get; set; } = false;

    [JsonPropertyName("transferedDate")]
    public string? TransferedDate { get; set; } = null;

    [JsonPropertyName("transferRequestId")]
    public uint? TransferRequestId { get; set; } = null;
}
