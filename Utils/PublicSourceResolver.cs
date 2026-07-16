using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

/// <summary>
/// Resolves StorySavedModel.PublicSources against the known_public_sources lookup table
/// and normalizes reference indices so they always sit at a sentence boundary within
/// the article's ContentText. Call ResolvePublicSourcesAsync right before persisting
/// a story on both publish and update.
/// </summary>
public static class PublicSourceResolver
{
    private static readonly char[] SentenceBoundaryChars = { '.', ';' };

    public static async Task ResolvePublicSourcesAsync(
        MySqlConnection connection,
        StorySavedModel storyModel,
        MySqlTransaction? transaction = null)
    {
        if (storyModel?.Sources == null || storyModel.Sources.Count == 0)
            return;

        RealignReferenceIndexes(storyModel);

        foreach (var source in storyModel.Sources)
        {
            if (source == null)
                continue;

            // Reset any client-supplied value; SourceId is always server-resolved.
            source.SourceId = await LookupSourceIdByUrlAsync(connection, transaction, source.Url);

            // SourceName is read-only: never accept it from the client, never persist
            // a stale copy. It is only ever populated when an article is fetched.
            source.SourceName = null;
        }
    }

    public static void RealignReferenceIndexes(StorySavedModel storyModel)
    {
        if (storyModel?.Sources == null || storyModel.Sources.Count == 0)
            return;

        var contentText = storyModel.ContentText ?? string.Empty;

        foreach (var source in storyModel.Sources)
        {
            if (source?.References == null)
                continue;

            foreach (var reference in source.References)
            {
                if (reference?.Index == null)
                    continue;

                for (int i = 0; i < reference.Index.Count; i++)
                {
                    reference.Index[i] = CorrectIndexToSentenceBoundary(
                        reference.Index[i], contentText);
                }
            }
        }
    }

    /// <summary>
    /// Maps each story's Sources[].Url into its (legacy) PublicSources string list, so
    /// older clients that only read PublicSources still see the URLs. Call this on every
    /// article fetch, alongside PopulateSourceNamesAsync.
    /// </summary>
    public static void MapSourceUrlsToPublicSources(IEnumerable<StorySavedModel> stories)
    {
        if (stories == null)
            return;

        foreach (var story in stories)
        {
            if (story == null)
                continue;

            story.PublicSources = story.Sources == null
                ? new List<string>()
                : story.Sources
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Url))
                    .Select(s => s.Url)
                    .ToList();
        }
    }

    /// <summary>
    /// Read-only lookup: populates PublicSourceModel.SourceName for every source that has a
    /// SourceId, across all supplied stories, in a single batched query. This never inserts,
    /// updates, or deletes rows in known_public_sources — call it whenever articles are
    /// fetched for display (never on publish/update).
    /// </summary>
    public static async Task PopulateSourceNamesAsync(
        MySqlConnection connection,
        IEnumerable<StorySavedModel> stories,
        MySqlTransaction? transaction = null)
    {
        if (stories == null)
            return;

        var storyList = stories.Where(s => s?.Sources != null).ToList();

        var sourceIds = storyList
            .SelectMany(s => s.Sources)
            .Where(p => p?.SourceId != null)
            .Select(p => p!.SourceId!.Value)
            .Distinct()
            .ToList();

        if (sourceIds.Count == 0)
            return;

        var idToName = new Dictionary<int, string>();
        var paramNames = sourceIds.Select((_, i) => "@id" + i).ToList();

        var cmd = new MySqlCommand();
        cmd.Connection = connection;
        if (transaction != null)
            cmd.Transaction = transaction;
        cmd.CommandText = $"SELECT source_id, source_name FROM known_public_sources WHERE source_id IN ({string.Join(",", paramNames)})";
        for (int i = 0; i < sourceIds.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], sourceIds[i]);

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            int idOrdinal = reader.GetOrdinal("source_id");
            int nameOrdinal = reader.GetOrdinal("source_name");
            while (await reader.ReadAsync())
                idToName[reader.GetInt32(idOrdinal)] = reader.GetString(nameOrdinal);
        }

        foreach (var story in storyList)
        {
            foreach (var source in story.Sources)
            {
                if (source?.SourceId == null)
                    continue;

                source.SourceName = idToName.TryGetValue(source.SourceId.Value, out var name)
                    ? name
                    : null;
            }
        }
    }

    /// <summary>
    /// For every Source with a non-null SourceName, extracts the host from its Url and, if
    /// that host isn't already in known_public_sources, inserts it with the given SourceName.
    /// Uses INSERT IGNORE against the unique host_name key, so this is a race-safe equivalent
    /// of "check if it exists, insert only if it doesn't" and it never overwrites/updates an
    /// existing row — an already-known host's source_name is left exactly as-is.
    ///
    /// NOTE: assumes SourceModel exposes `Url` (string) and `SourceName` (string?). Adjust the
    /// two property accesses below if the actual class differs.
    /// </summary>
    public static async Task UpsertKnownSourcesAsync(
        MySqlConnection connection,
        IEnumerable<SourceModel>? sources,
        MySqlTransaction? transaction = null)
    {
        if (sources == null)
            return;

        foreach (var source in sources)
        {
            if (source?.SourceName == null)
                continue;

            var host = ExtractHost(source.Url);
            if (string.IsNullOrEmpty(host))
                continue;

            var cmd = new MySqlCommand();
            cmd.Connection = connection;
            if (transaction != null)
                cmd.Transaction = transaction;
            cmd.CommandText = "INSERT IGNORE INTO known_public_sources (host_name, source_name) VALUES (@host_name, @source_name)";
            cmd.Parameters.AddWithValue("@host_name", host);
            cmd.Parameters.AddWithValue("@source_name", source.SourceName);
            await cmd.ExecuteNonQueryAsync();
        }
    }
    private static async Task<int?> LookupSourceIdByUrlAsync(
        MySqlConnection connection, MySqlTransaction? transaction, string? url)
    {
        var host = ExtractHost(url);
        if (string.IsNullOrEmpty(host))
            return null;

        var cmd = new MySqlCommand();
        cmd.Connection = connection;
        if (transaction != null)
            cmd.Transaction = transaction;
        cmd.CommandText = "SELECT source_id FROM known_public_sources WHERE host_name = @host_name LIMIT 1";
        cmd.Parameters.AddWithValue("@host_name", host);

        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
            return null;

        return Convert.ToInt32(result);
    }

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host.Substring(4);

        return host.ToLowerInvariant();
    }

    /// <summary>
    /// Moves index to just after the nearest '.' or ';' found by scanning both left and
    /// right from index within contentText. Ties go to the left. If no sentence boundary
    /// exists on either side, the index is only clamped to the content bounds.
    /// The result is always within [0, contentText.Length].
    /// </summary>
    internal static int CorrectIndexToSentenceBoundary(int index, string contentText)
    {
        int len = contentText?.Length ?? 0;
        if (len == 0)
            return 0;

        int clamped = Math.Max(0, Math.Min(index, len));

        int leftBoundary = -1;
        for (int p = Math.Min(clamped, len - 1); p >= 0; p--)
        {
            if (SentenceBoundaryChars.Contains(contentText![p]))
            {
                leftBoundary = p;
                break;
            }
        }

        int rightBoundary = -1;
        for (int p = clamped; p < len; p++)
        {
            if (SentenceBoundaryChars.Contains(contentText![p]))
            {
                rightBoundary = p;
                break;
            }
        }

        int corrected;
        if (leftBoundary == -1 && rightBoundary == -1)
        {
            // No sentence boundary anywhere in the content — just keep the clamped index.
            corrected = clamped;
        }
        else if (leftBoundary == -1)
        {
            corrected = rightBoundary + 1;
        }
        else if (rightBoundary == -1)
        {
            corrected = leftBoundary + 1;
        }
        else
        {
            int leftDistance = clamped - leftBoundary;
            int rightDistance = rightBoundary - clamped;
            corrected = leftDistance <= rightDistance ? leftBoundary + 1 : rightBoundary + 1;
        }

        return Math.Max(0, Math.Min(corrected, len));
    }
}
