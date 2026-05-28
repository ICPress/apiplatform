using System.Text.Json.Serialization;

public class TokenResponse
{
    [JsonPropertyName("token")]
    public Token? Token { get; set; } = null;
}

public class Token
{
    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("issued_at")]
    public DateTime IssuedAt { get; set; }
}
