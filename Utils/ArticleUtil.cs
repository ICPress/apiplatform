using Apache.Ignite.Core;
using MySql.Data.MySqlClient;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apache.Ignite.Core.Cache.Query;
using Apache.Ignite.Core.Client.Cache;
using Apache.Ignite.Core.Cache.Configuration;
using Apache.Ignite.Core.Client;
using Apache.Ignite.Core.Cache.Expiry;

public static class ArticleUtil
{


    public static async Task QueuePublishEventAsync(MySqlConnection connectionStory, StorySavedModel storyModel, ILogger logger)
    {
        try
        {
            var cmd = new MySql.Data.MySqlClient.MySqlCommand();
            cmd.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)";
            cmd.Parameters.AddWithValue("@username", storyModel.AuthorName);
            cmd.Parameters.AddWithValue("@additional_data", storyModel.SlugTitle);
            cmd.Parameters.AddWithValue("@type", (int)EventTriggerType.PUBLISHED_ARTICLE);
            cmd.Connection = connectionStory;
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            logger.LogError("Could not queue publish event for {0}: {1}", storyModel.SlugTitle, ex.Message);
        }
    }


    public static async Task PreloadArticlesMetadataAsync(MySqlConnection connection, MySqlConnection connectionStory, List<StoryPublishedModel> storyArticles)
    {
        if (storyArticles == null || storyArticles.Count == 0) return;

        // Hearts/feedback counts
        var heartCmd = new MySql.Data.MySqlClient.MySqlCommand();
        heartCmd.Connection = connection;
        heartCmd.CommandText = $"SELECT COUNT(*) as CNT, item_id FROM feedback WHERE item_id IN ({string.Join(",", storyArticles.Select(_ => "?"))}) AND feedback_type = 'heart' GROUP BY item_id";
        foreach (var article in storyArticles)
            heartCmd.Parameters.Add(new MySqlParameter("", article.SlugTitle));

        await using (var reader = await heartCmd.ExecuteReaderAsync())
        {
            int cntOrdinal = reader.GetOrdinal("CNT");
            int itemIdOrdinal = reader.GetOrdinal("item_id");
            while (await reader.ReadAsync())
            {
                var readItemId = reader.GetString(itemIdOrdinal);
                var readItemCount = reader.GetInt32(cntOrdinal);
                var match = storyArticles.FirstOrDefault(x => x.SlugTitle == readItemId);
                if (match != null) match.Hearts = readItemCount;
            }
        }

        // Comment counts
        var commentCmd = new MySql.Data.MySqlClient.MySqlCommand();
        commentCmd.Connection = connectionStory;
        commentCmd.CommandText = $"SELECT slug_title, COUNT(*) as CNT FROM user_story_comment WHERE slug_title IN ({string.Join(",", storyArticles.Select(_ => "?"))}) GROUP BY slug_title";
        foreach (var article in storyArticles)
            commentCmd.Parameters.Add(new MySqlParameter("", article.SlugTitle));

        await using (var reader = await commentCmd.ExecuteReaderAsync())
        {
            int slugOrdinal = reader.GetOrdinal("slug_title");
            int cntOrdinal = reader.GetOrdinal("CNT");
            while (await reader.ReadAsync())
            {
                var readItemId = reader.GetString(slugOrdinal);
                var count = reader.GetInt32(cntOrdinal);
                var match = storyArticles.FirstOrDefault(x => x.SlugTitle == readItemId);
                if (match != null) match.Comments = count;
            }
        }

        await GeneralUtil.PreloadBadgeDataAsync(connectionStory, storyArticles);
    }
    // ---------------------------------------------------------------------------
    // Cache helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the storyarticle cache configured with a 14-day create/update expiry.
    /// Access TTL is intentionally left null so reads do not reset the clock.
    /// </summary>
    public static ICacheClient<string, StorySavedModel> GetArticleCacheWithTtl(IIgniteClient client)
    {
        return client.GetOrCreateCache<string, StorySavedModel>("storyarticle")
        .WithExpiryPolicy(
            new ExpiryPolicy(
                create: TimeSpan.FromDays(14),
                update: TimeSpan.FromDays(14),
                access: null)
        );
    }

    /// <summary>
    /// Attempts a cache get; on miss, fetches the latest published snapshot from
    /// user_story_log, deserialises it.  Returns null when the story cannot be found anywhere.
    /// </summary>
    public static async Task<StorySavedModel?> TryGetWithFallbackAsync(
        ICacheClient<string, StorySavedModel> cache,
        string slugTitle,
        MySqlConnection connection, ILogger logger)
    {
        var result = await cache.TryGetAsync(slugTitle);
        if (result.Success)
            return result.Value;

        logger.LogDebug("Cache miss for '{0}' — querying user_story_log", slugTitle);

        var cmd = new MySql.Data.MySqlClient.MySqlCommand(
            "SELECT CAST(UNCOMPRESS(story_compressed) AS CHAR) FROM user_story_log " +
            "WHERE slug_title = @slug " + 
            "ORDER BY log_id DESC LIMIT 1",
            connection);
        cmd.Parameters.AddWithValue("@slug", slugTitle);

        object? raw;
        try { raw = await cmd.ExecuteScalarAsync(); }
        catch (Exception ex)
        {
            logger.LogError("DB error fetching log row for '{0}': {1}", slugTitle, ex.Message);
            return null;
        }

        if (raw == null || raw == DBNull.Value)
        {
            logger.LogWarning("No publish log row found for '{0}'", slugTitle);
            return null;
        }

        StorySavedModel? story;
        try
        {
            var json = raw is byte[] bytes
                ? Encoding.UTF8.GetString(bytes)
                : raw.ToString()!;
            story = JsonSerializer.Deserialize<StorySavedModel>(json);
        }
        catch (Exception ex)
        {
            logger.LogError("Deserialise failed for '{0}': {1}", slugTitle, ex.Message);
            return null;
        }

        if (story == null) return null;

        return story;
    }

    public static void SanitizeStylingInfo(StylingInfoModel stylingInfo, int contentTextLength){
        
        var contentLength = contentTextLength - 1;

        // Logic for adjusting spans
        if (stylingInfo.Spans?.Any(x => x.Start< 0 || x.Start> contentLength) == true)
        {
            var startsUnder = stylingInfo.Spans.Where(x => x.Start< 0);
            foreach (var span in startsUnder) span.Start= 0;

            var startsOver = stylingInfo.Spans.Where(x => x.Start> contentLength);
            foreach (var span in startsOver) span.Start= contentLength;
        }

        if (stylingInfo.Spans?.Any(x => x.End < 0 || x.End > contentLength) == true)
        {
            var endsUnder = stylingInfo.Spans.Where(x => x.End < 0);
            foreach (var span in endsUnder) span.End = 0;

            var endsOver = stylingInfo.Spans.Where(x => x.End > contentLength);
            foreach (var span in endsOver) span.End = contentLength;
        }

        if (stylingInfo.Spans?.Any(x => x.Start> x.End) == true)
        {
            var invalidSpans = stylingInfo.Spans.Where(x => x.Start>= x.End);
            foreach (var span in invalidSpans)
            {
                span.Start= (span.End - 1 < 0) ? 0 : span.End;
            }
        }
    }
}