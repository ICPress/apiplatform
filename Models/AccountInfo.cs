using System.Text.Json.Serialization;

public class AccountInfo
{
    [System.ComponentModel.DataAnnotations.StringLength(200)]
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("user_uuid")]
    public string? UserUuid { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}