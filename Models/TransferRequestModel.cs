using System.Text.Json.Serialization;
using System.Collections.Generic;

public class TransferRequestModel
{
    [JsonPropertyName("deviceuuid")]
    public string? DeviceUUID { get; set; } = null;

    [JsonPropertyName("walletaddress")]
    public string? WalletAddress { get; set; } = null;

    [JsonPropertyName("items")]
    public List<TransferRequestItemModel> Items { get; set; } = new List<TransferRequestItemModel>();
}
