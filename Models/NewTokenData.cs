using System.Text.Json.Serialization;

public class NewTokenData
{
    [JsonPropertyName("token")]
    public string? Token { get; set; } = null;

    [JsonPropertyName("deviceuuid")]
    public string? DeviceUUID { get; set; } = null;
}
