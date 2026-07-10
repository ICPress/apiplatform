using System.Text.Json.Serialization;

public class StoryPublishedModel : StorySavedModel, IAuthorEntityPublished
{
    public StoryPublishedModel() { }

    public StoryPublishedModel(StorySavedModel savedModel, bool isAdmin)
    {
        this.StylingInfo = savedModel.StylingInfo;
        this.StoryTitle = savedModel.StoryTitle;
        this.EmptyTitle = savedModel.EmptyTitle;
        this.ContentText = savedModel.ContentText;
        this.Location = savedModel.Location;
        this.Tags = savedModel.Tags;
        this.SlugTitle = savedModel.SlugTitle;
        this.LangCode = savedModel.LangCode;
        this.AuthorName = savedModel.AuthorName;
        this.Timestamp = savedModel.Timestamp;
        this.References = savedModel.References;
        if (isAdmin) this.PrivateSources = savedModel.PrivateSources;
        this.StoryMap = savedModel.StoryMap;
        this.Category = savedModel.Category;
    }

    [JsonPropertyName("hearts")]
    public int Hearts { get; set; } = 0;

    [JsonPropertyName("comments")]
    public int Comments { get; set; } = 0;

    [JsonPropertyName("authorBadge")]
    public string? AuthorBadge { get; set; } = null;

    [JsonPropertyName("rejectionReason")]
    public string? RejectionReason { get; set; } = null;
}
