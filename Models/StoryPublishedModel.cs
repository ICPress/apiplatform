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
        this.PublicSources = savedModel.PublicSources;
        if (isAdmin) this.PrivateSources = savedModel.PrivateSources;
        this.StoryMap = savedModel.StoryMap;
        this.Category = savedModel.Category;
        this.Sources = savedModel.Sources;
        this.Updated = savedModel.Updated;
        

        var contentLength = (ContentText?.Length ?? 1) - 1;

        // Logic for adjusting spans
        if (StylingInfo?.Spans?.Any(x => x.Start< 0 || x.Start> contentLength) == true)
        {
            var startsUnder = StylingInfo.Spans.Where(x => x.Start< 0);
            foreach (var span in startsUnder) span.Start= 0;

            var startsOver = StylingInfo.Spans.Where(x => x.Start> contentLength);
            foreach (var span in startsOver) span.Start= contentLength;
        }

        if (StylingInfo?.Spans?.Any(x => x.End < 0 || x.End > contentLength) == true)
        {
            var endsUnder = StylingInfo.Spans.Where(x => x.End < 0);
            foreach (var span in endsUnder) span.End = 0;

            var endsOver = StylingInfo.Spans.Where(x => x.End > contentLength);
            foreach (var span in endsOver) span.End = contentLength;
        }

        if (StylingInfo?.Spans?.Any(x => x.Start> x.End) == true)
        {
            var invalidSpans = StylingInfo.Spans.Where(x => x.Start>= x.End);
            foreach (var span in invalidSpans)
            {
                span.Start= (span.End - 1 < 0) ? 0 : span.End;
            }
        }
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
