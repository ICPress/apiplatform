using System.Text.Json.Serialization;
using System.Collections.Generic;

public class UserFollowingModel
{
    public UserFollowingModel(string latestFetchTimestamp, IEnumerable<UserFollowingInfo> userFollowings)
    {
        this.LatestFetchTimestamp = latestFetchTimestamp;
        this.UserFollowings = userFollowings;
    }

    [JsonPropertyName("userFollowings")]
    public IEnumerable<UserFollowingInfo> UserFollowings { get; set; }

    [JsonPropertyName("latestFetchTimestamp")]
    public string LatestFetchTimestamp { get; set; }
}
