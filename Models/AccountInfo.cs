using System.Text.Json.Serialization;

public class AccountInfo
{
    [JsonPropertyName("user_uuid")]
    public string? UserUuid { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}