using Microsoft.AspNetCore.Mvc;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Cache.Query;
using Apache.Ignite.Core.Client.Cache;
using Apache.Ignite.Core.Cache.Configuration;
using JWT.Builder;
using JWT.Algorithms;
using System.Text.Json;
using Slugify;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using MySql.Data.MySqlClient;
using LanguageDetection;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace apiplatform.Controllers;


[ApiController]
[Route("[controller]")]
public class TagController : ControllerBase
{
    private readonly ILogger<TagController> _logger;

    private readonly ServerSettings _serverSettings;

    public TagController(ILogger<TagController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [HttpGet("{searchvalue}")]
    public List<string> FindArticleTag(string searchvalue)
    {
        var foundTags = new List<string>(5);
        if (searchvalue.Length <= 3) return foundTags;
        string currentTag;

        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connectionStory.Open();
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT tag FROM story_tags_rank WHERE tag LIKE @search ORDER BY tag_rank DESC LIMIT 5;";
            mySqlCommand.Parameters.AddWithValue("@search", searchvalue.Replace("%", "") + "%");
            mySqlCommand.Connection = connectionStory;
            using (var reader = mySqlCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    currentTag = reader.GetString("tag");
                    foundTags.Add(currentTag);
                }
                reader.Close();
            }
            return foundTags;
        }
        finally
        {
            connectionStory.Close();
        }
    }

    [HttpGet("trending")]
    public async Task<List<TrendingTagModel>> GetTrendingTags(uint n = 10)
    {
        if (n == 0 || n > 20) return new List<TrendingTagModel>();

        const string query = @"
        SELECT 
            r.tag,
            r.recent_usage,
            COALESCE(p.previous_usage, 0) AS previous_usage
        FROM
        (
            SELECT tag, COUNT(*) AS recent_usage
            FROM story_tags
            WHERE created_at >= NOW() - INTERVAL 7 DAY
              AND tag_type IN ('tag', 'topic')
            GROUP BY tag
        ) r
        LEFT JOIN
        (
            SELECT tag, COUNT(*) AS previous_usage
            FROM story_tags
            WHERE created_at >= NOW() - INTERVAL 14 DAY
              AND created_at < NOW() - INTERVAL 7 DAY
              AND tag_type IN ('tag', 'topic')
            GROUP BY tag
        ) p
        ON r.tag = p.tag
        ORDER BY (r.recent_usage - COALESCE(p.previous_usage, 0)) DESC
        LIMIT @limit;";

        return await ExecuteTrendingQuery(query, n);
    }

    [HttpGet("trending/categories")]
    public async Task<List<TrendingTagModel>> GetTrendingCategories(uint n = 10)
    {
        if (n == 0 || n > 20) return new List<TrendingTagModel>();

        const string query = @"
        SELECT 
            r.tag,
            r.recent_usage,
            COALESCE(p.previous_usage, 0) AS previous_usage
        FROM
        (
            SELECT tag, COUNT(*) AS recent_usage
            FROM story_tags
            WHERE created_at >= NOW() - INTERVAL 7 DAY
              AND tag_type = 'meta'
            GROUP BY tag
        ) r
        LEFT JOIN
        (
            SELECT tag, COUNT(*) AS previous_usage
            FROM story_tags
            WHERE created_at >= NOW() - INTERVAL 14 DAY
              AND created_at < NOW() - INTERVAL 7 DAY
              AND tag_type = 'meta'
            GROUP BY tag
        ) p
        ON r.tag = p.tag
        ORDER BY (r.recent_usage - COALESCE(p.previous_usage, 0)) DESC
        LIMIT @limit;";

        return await ExecuteTrendingQuery(query, n);
    }

    private async Task<List<TrendingTagModel>> ExecuteTrendingQuery(string query, uint limit)
    {
        var result = new List<TrendingTagModel>();

        using var connection = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(
                ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connection.OpenAsync();

        using var cmd = new MySqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();

        int tagOrdinal = reader.GetOrdinal("tag");
        int recentOrdinal = reader.GetOrdinal("recent_usage");
        int previousOrdinal = reader.GetOrdinal("previous_usage");

        while (await reader.ReadAsync())
        {
            var tag = reader.GetString(tagOrdinal);
            var recent = reader.GetInt32(recentOrdinal);
            var previous = reader.GetInt32(previousOrdinal);

            double change = previous == 0
                ? (recent > 0 ? 100.0 : 0.0)
                : ((double)(recent - previous) / previous) * 100.0;

            result.Add(new TrendingTagModel
            {
                Tag = tag,
                Usage = recent,
                PreviousUsage = previous,
                PercentageChange = Math.Round(change, 2)
            });
        }

        return result;
    }


}