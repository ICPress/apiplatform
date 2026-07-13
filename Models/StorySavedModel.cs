using System.Text.Json.Serialization;
using System.Collections.Generic;

public class StorySavedModel  : IAuthorEntity
{
    [JsonPropertyName("stylingInfo")]
    public StylingInfoModel StylingInfo { get; set; } = new StylingInfoModel();

    [JsonPropertyName("storyTitle")]
    public string? StoryTitle { get; set; }

    [JsonPropertyName("emptyTitle")]
    public string EmptyTitle { get; set; } = "";

    [JsonPropertyName("contentText")]
    public string ContentText { get; set; } ="";

    [JsonPropertyName("location")]
    public string Location { get; set; } ="";

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("slugTitle")]
    public string? SlugTitle { get; set; }

    [JsonPropertyName("langCode")]
    public string? LangCode { get; set; }

    [JsonPropertyName("authorName")]
    public string AuthorName { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("isReviewed")]
    public bool IsReviewed { get; set; } = true;

    [JsonPropertyName("privateSources")]
    public List<ArticlePrivateSource> PrivateSources { get; set; } = new List<ArticlePrivateSource>();

     [JsonPropertyName("publicSources")]
    public List<string> PublicSources { get; set; } = new List<string>(); 

    [JsonPropertyName("sources")]
    public List<SourceModel> Sources { get; set; } = new List<SourceModel>();

    [JsonPropertyName("storyMap")]
    public StoryMap? StoryMap { get; set; } = null;

    [JsonPropertyName("category")]
    public string? Category { get; set; } = null;

    [JsonPropertyName("updated")]
    public DateTime? Updated { get; set; }

}
