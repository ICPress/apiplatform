using System.Text.Json.Serialization;

public class AccountInfoFull : AccountInfo
{
    public AccountInfoFull(
        string? refreshToken,
        string? profileIcon,
        string? profileBackgroundImage,
        string? profileText,
        string email,
        string user_uuid,
        string username,
        string followingLatestCheck,
        string? walletAddress,
        bool useTempAuthSwiftStorage,
        string cdnLarge,
        string cdnSmall,
        string cdnMessage,
        string cdnLargeRequestPath,
        string cdnSmallRequestPath,
        string cdnMessageRequestPath,
        string imageStaticPath,
        bool requireArticleSources,
        bool requireArticleReview)
    {
        Email = email;
        ProfileBackgroundImage = profileBackgroundImage;
        ProfileIcon = profileIcon;
        RefreshToken = refreshToken;
        UserUuid = user_uuid;
        Username = username;
        ProfileText = profileText;
        FollowingLatestCheck = followingLatestCheck;
        WalletAddress = walletAddress;
        UseTempAuthSwiftStorage = useTempAuthSwiftStorage;
        CdnLargePublishPath = cdnLarge;
        CdnSmallPublishPath = cdnSmall;
        CdnMessagePublishPath = cdnMessage;
        CdnLargeRequestPath = cdnLargeRequestPath;
        CdnSmallRequestPath = cdnSmallRequestPath;
        CdnMessageRequestPath = cdnMessageRequestPath;
        ImageStaticPath = imageStaticPath;
        RequireArticleReview = requireArticleReview;
        RequireArticleSources = requireArticleSources;
    }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("profileIcon")]
    public string? ProfileIcon { get; set; }

    [JsonPropertyName("profileBackgroundImage")]
    public string? ProfileBackgroundImage { get; set; }

    [JsonPropertyName("profileText")]
    public string? ProfileText { get; set; }

    [JsonPropertyName("followingLatestCheck")]
    public string? FollowingLatestCheck { get; set; }

    [JsonPropertyName("accountBalance")]
    public int? AccountBalance { get; set; }

    [JsonPropertyName("unreadNotifications")]
    public long? UnreadNotifications { get; set; }

    [JsonPropertyName("unreadFollowedStories")]
    public long? UnreadFollowedStories { get; set; }

    [JsonPropertyName("walletAddress")]
    public string? WalletAddress { get; set; }

    [JsonPropertyName("useTempAuthSwiftStorage")]
    public bool UseTempAuthSwiftStorage { get; set; }

    [JsonPropertyName("cdnLargePublishPath")]
    public string CdnLargePublishPath { get; set; }

    [JsonPropertyName("cdnSmallPublishPath")]
    public string CdnSmallPublishPath { get; set; }

    [JsonPropertyName("cdnMessagePublishPath")]
    public string CdnMessagePublishPath { get; set; }

    [JsonPropertyName("cdnLargeRequestPath")]
    public string CdnLargeRequestPath { get; set; }

    [JsonPropertyName("cdnSmallRequestPath")]
    public string CdnSmallRequestPath { get; set; }

    [JsonPropertyName("cdnMessageRequestPath")]
    public string CdnMessageRequestPath { get; set; }

    [JsonPropertyName("imageStaticPath")]
    public string ImageStaticPath { get; set; } = "";

    [JsonPropertyName("requireArticleSources")]
    public bool RequireArticleSources { get; set; } = false;

    [JsonPropertyName("requireArticleReview")]
    public bool RequireArticleReview { get; set; } = false;
}