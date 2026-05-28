using System.Text.Json.Serialization;

public class UsernameSearchResult
{
    public UsernameSearchResult(string userName, string? profileIcon)
    {
        this.Username = userName;
        this.ProfileIcon = profileIcon;
    }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("profileIcon")]
    public string? ProfileIcon { get; set; }
}
