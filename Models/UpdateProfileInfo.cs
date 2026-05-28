using System.Text.Json.Serialization;

public class UpdateProfileInfo
{
    [JsonPropertyName("profileBadgeImageInfo")]
    public ImageInfoMetadata? ProfileBadgeImageInfo { get; set; }

    [JsonPropertyName("profileBackgroundImageInfo")]
    public ImageInfoMetadata? ProfileBackgroundImageInfo { get; set; }

    [JsonPropertyName("profileDescription")]
    public string? ProfileDescription { get; set; }
}
