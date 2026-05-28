using System.Text.Json.Serialization;

public class ProfileInfo
{
    public ProfileInfo(string username)
    {
        this.Username = username;
    }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("profileIcon")]
    public string? ProfileIcon { get; set; }

    [JsonPropertyName("profileBackgroundImage")]
    public string? ProfileBackgroundImage { get; set; }

    [JsonPropertyName("profileText")]
    public string? ProfileText { get; set; }

    [JsonPropertyName("followerSpan")]
    public string? FollowerSpan { get; set; }

    [JsonPropertyName("memberSince")]
    public string? MemberSince { get; set; }

    [JsonPropertyName("articlesPublished")]
    public long? ArticlesPublished { get; set; }

    [JsonPropertyName("contactBlocked")]
    public bool ContactBlocked { get; set; } = false;
}
