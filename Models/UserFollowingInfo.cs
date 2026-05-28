using System.Text.Json.Serialization;
using System.Collections.Generic;

public class UserFollowingInfo
{
    public UserFollowingInfo(string username)
    {
        this.Username = username;
    }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("newstories")]
    public List<StoryPublishedModel> NewStories { get; set; } = new List<StoryPublishedModel>(10);

    [JsonPropertyName("profileicon")]
    public string? ProfileIcon { get; set; } = null;

    [JsonIgnore]
    public List<string> StoryTitles { get; set; } = new List<string>();
}
