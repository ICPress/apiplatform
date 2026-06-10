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
public class ArticleController : ControllerBase
{
    private readonly ILogger<ArticleController> _logger;
    private readonly ServerSettings _serverSettings;

    public ArticleController(ILogger<ArticleController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }
    private readonly string SearchArticleByTagQuery = @"
                SELECT st.slug_title
                FROM story_tags st
                JOIN user_stories us ON us.slug_title = st.slug_title
                LEFT JOIN article_pending_review rev ON rev.slug_title = st.slug_title
                WHERE st.tag = @tag
                AND rev.slug_title IS NULL
                GROUP BY st.slug_title, us.story_id
                HAVING MAX(st.tag_type = 'tag') > 0
                    OR (MAX(st.tag_type = 'meta') > 0 AND MAX(st.tag_type = 'tag') = 0)
                ORDER BY us.story_id DESC
                LIMIT @count OFFSET @offset";

    [HttpGet("guidelines")]
    public ArticleGuidelines GetArticleGuidelines()
    {
        return new ArticleGuidelines
        {
            Version = "1.1",
            LastUpdated = "2026-05-21",
            Categories = new List<GuidelineCategory>
            {
                new GuidelineCategory
                {
                    Id = "category_classification",
                    Name = "Category Classification",
                    Description = "Every article must be classifiable into exactly one of the six recognised ICPress editorial categories.",
                    Weight = 1.5f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule
                        {
                            Id = "category_1",
                            Text = "The article must fall within exactly one of the following primary categories: Markets, Technology, Economy, Startup, Crypto, or Security. ",
                            IsRequired = true,
                            MinScore = 0.9f
                        },
                        new GuidelineRule
                        {
                            Id = "category_2",
                            Text = "Markets: Covers equity movements, stock reports, technical analysis, and insider buying or selling activity.",
                            IsRequired = true,
                            MinScore = 0.8f
                        },
                        new GuidelineRule
                        {
                            Id = "category_3",
                            Text = "Technology: Covers AI breakthroughs, quantum computing progress, and hardware innovation.",
                            IsRequired = true,
                            MinScore = 0.8f
                        },
                        new GuidelineRule
                        {
                            Id = "category_4",
                            Text = "Economy: Covers inflation, interest rates, GDP, and geopolitical events with material impact on trade or wealth.",
                            IsRequired = true,
                            MinScore = 0.8f
                        },
                        new GuidelineRule
                        {
                            Id = "category_5",
                            Text = "Startup: Covers venture capital funding rounds, valuations, mergers and acquisitions, and notable open-source tools or repositories.",
                            IsRequired = true,
                            MinScore = 0.8f
                        },
                        new GuidelineRule
                        {
                            Id = "category_6",
                            Text = "Crypto: Covers cryptocurrency price action, blockchain protocol upgrades, forks, and Web3 developments.",
                            IsRequired = true,
                            MinScore = 0.8f
                        },
                        new GuidelineRule
                        {
                            Id = "category_7",
                            Text = "Security: Covers cybersecurity incidents, privacy developments, deepfake defence, and online safety.",
                            IsRequired = true,
                            MinScore = 0.8f
                        }
                    }
                },
                new GuidelineCategory
                {
                    Id = "accuracy",
                    Name = "Accuracy and Fact-Checking",
                    Description = "All claims must be verifiable and factually accurate.",
                    Weight = 1.5f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "accuracy_1", Text = "All claims must be verifiable using credible sources.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "accuracy_2", Text = "Avoid spreading rumors, unconfirmed information, or misinformation.", IsRequired = true, MinScore = 0.9f },
                        new GuidelineRule { Id = "accuracy_3", Text = "For investigative pieces, clearly distinguish between verified facts and conjecture.", IsRequired = true, MinScore = 0.7f },
                        new GuidelineRule { Id = "accuracy_4", Text = "Provide citations or links to sources wherever possible.", IsRequired = false, MinScore = 0.6f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "clarity",
                    Name = "Clarity and Structure",
                    Description = "Articles should be well structured and easy to read.",
                    Weight = 1.0f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "clarity_1", Text = "Articles should have a clear title, introduction, body, and conclusion.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "clarity_2", Text = "Use concise language and avoid unnecessary jargon.", IsRequired = true, MinScore = 0.7f },
                        new GuidelineRule { Id = "clarity_3", Text = "Organize content logically; each paragraph should focus on a single idea.", IsRequired = true, MinScore = 0.7f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "sourcing",
                    Name = "Sourcing and Attribution",
                    Description = "Use reliable sources and give proper credit.",
                    Weight = 1.3f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "sourcing_1", Text = "Use reliable and reputable sources such as recognized media, official reports, and experts.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "sourcing_2", Text = "Give proper credit for quotes, data, or images.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "sourcing_3", Text = "Avoid excessive self-promotion or linking to unrelated websites.", IsRequired = true, MinScore = 0.7f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "relevance",
                    Name = "Relevance and Newsworthiness",
                    Description = "Content must be timely and relevant.",
                    Weight = 1.0f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "relevance_1", Text = "Submit content that is timely and relevant to the topic or category.", IsRequired = true, MinScore = 0.7f },
                        new GuidelineRule { Id = "relevance_2", Text = "Avoid off-topic submissions or content that is primarily personal opinion unless labeled as op-ed.", IsRequired = true, MinScore = 0.7f },
                        new GuidelineRule { Id = "relevance_3", Text = "Financial or technical articles should include clear context or explanation for general readers.", IsRequired = false, MinScore = 0.6f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "bias",
                    Name = "Bias and Fairness",
                    Description = "Reporting must be neutral and fair.",
                    Weight = 1.2f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "bias_1", Text = "News articles must aim for neutral reporting; clearly label opinion pieces or commentary.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "bias_2", Text = "Avoid hate speech, discrimination, or targeting individuals unfairly.", IsRequired = true, MinScore = 0.9f },
                        new GuidelineRule { Id = "bias_3", Text = "Present multiple perspectives where applicable.", IsRequired = false, MinScore = 0.6f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "originality",
                    Name = "Originality",
                    Description = "Content must be original and properly attributed.",
                    Weight = 1.2f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "originality_1", Text = "Articles should be original content; plagiarism is strictly prohibited.", IsRequired = true, MinScore = 0.9f },
                        new GuidelineRule { Id = "originality_2", Text = "When summarizing or referencing other work, quote or paraphrase responsibly and cite the source.", IsRequired = true, MinScore = 0.7f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "grammar",
                    Name = "Grammar, Style, and Professionalism",
                    Description = "Articles must meet professional writing standards.",
                    Weight = 1.0f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "grammar_1", Text = "Use correct spelling, punctuation, grammar and English language.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "grammar_2", Text = "Maintain a professional tone; avoid excessive slang or informal language in news reporting.", IsRequired = true, MinScore = 0.7f },
                        new GuidelineRule { Id = "grammar_3", Text = "Formatting should be clean and readable with headings, bullet points, and paragraphs.", IsRequired = false, MinScore = 0.6f }
                    }
                },
                new GuidelineCategory
                {
                    Id = "ethics",
                    Name = "Ethics and Integrity",
                    Description = "Uphold the highest standards of journalistic ethics.",
                    Weight = 1.5f,
                    SubRules = new List<GuidelineRule>
                    {
                        new GuidelineRule { Id = "ethics_1", Text = "Do not fabricate quotes, statistics, or sources.", IsRequired = true, MinScore = 1.0f },
                        new GuidelineRule { Id = "ethics_2", Text = "Disclose conflicts of interest when relevant such as financial ties or affiliations.", IsRequired = true, MinScore = 0.8f },
                        new GuidelineRule { Id = "ethics_3", Text = "Avoid sensationalism or clickbait titles; headlines should reflect article content accurately.", IsRequired = true, MinScore = 0.8f }
                    }
                }
            }
        };
    }

    [HttpGet("category/{category}")]
    public async Task<List<StoryPublishedModel>> GetArticlesByCategoryLatest(
    string category,
    int count = 10,
    int offset = 0)
    {
        var storyArticles = new List<StoryPublishedModel>();

        if (string.IsNullOrWhiteSpace(category) || category.Length <= 2)
            return storyArticles;

        if (count <= 0 || offset < 0)
            return storyArticles;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using var httpClient = new HttpClient();
        using var connection = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using var connectionStory = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connection.OpenAsync();
        await connectionStory.OpenAsync();

        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");

        // 🔹 FIXED: category in path, not query
        var url = $"{_serverSettings.GorseAPIEndpoint}latest/{category}?n={count}&offset={offset}";

        var recommendations = await httpClient.GetFromJsonAsync<List<GorseItemRecommendation>>(url);

        if (recommendations == null || recommendations.Count == 0)
            return storyArticles;

        foreach (var item in recommendations)
        {
            if (item?.Id == null) continue;

            var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, item.Id, connectionStory, _logger);
            if (res == null)
            {
                _logger.LogError("Missing article {0} from cache and log", item.Id);
                continue;
            }
            storyArticles.Add(new StoryPublishedModel(res, false));
        }

        await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, storyArticles);

        return storyArticles;
    }

    [HttpGet("title/{slugTitle}")]
    public async Task<StoryPublishedModel?> GetArticle(string slugTitle)
    {
        var storyArticle = new List<StoryPublishedModel>();
        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connection.OpenAsync();

        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");

        var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, slugTitle, connectionStory, _logger);

        if (res != null)
        {
            storyArticle.Add(new StoryPublishedModel(res, false));
            await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, storyArticle);
        }

        return storyArticle.FirstOrDefault();
    }


    [Authorize]
    [HttpGet("recommended/{username}")]
    public async Task<List<StoryPublishedModel>> GetRecommendedArticles(string username, int count = 10, int offset = 0)
    {
        return await GetRecommededArticlesByTag(username, null, count, offset);
    }


    [Authorize]
    [HttpGet("recommended/{hashtag}/{username}")]
    public async Task<List<StoryPublishedModel>> GetRecommededArticlesByTag(string username, string? hashtag = null, int count = 10, int offset = 0)
    {
        var storyArticles = new List<StoryPublishedModel>();
        if (count <= 0 || offset < 0 || count >= 20) return storyArticles;

        // Get role for pending review hydration — null is fine for non-admin users
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using var httpClient = new HttpClient();
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connection.OpenAsync();

        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");

        // 1. Hydrate pending review articles when review mode is enabled
        if (_serverSettings.RequireArticleReview)
        {
            var pendingCmd = new MySql.Data.MySqlClient.MySqlCommand();
            pendingCmd.Connection = connectionStory;

            if (role == ConfigUtil.JWT_ADMIN_ROLE)
            {
                // Admins see all articles awaiting first review (no rejection reason set yet)
                pendingCmd.CommandText = "SELECT slug_title, rejection_reason FROM article_pending_review WHERE rejection_reason = ''";
            }
            else
            {
                // Regular users see only their own pending or rejected articles
                pendingCmd.CommandText = "SELECT slug_title,rejection_reason FROM article_pending_review WHERE username = @username";
                pendingCmd.Parameters.AddWithValue("@username", username);
            }

            var pendingSlugs = new Dictionary<string, string>();
            await using (var reader = await pendingCmd.ExecuteReaderAsync())
            {
                int slugOrdinal = reader.GetOrdinal("slug_title");
                int reason = reader.GetOrdinal("rejection_reason");
                while (await reader.ReadAsync())
                {
                    pendingSlugs.Add(reader.GetString(slugOrdinal), reader.GetString(reason));
                }
            }

            var newPendingArticles = new List<StoryPublishedModel>();
            foreach (var pendingSlug in pendingSlugs)
            {
                StorySavedModel? pendingStory = await ArticleUtil.TryGetWithFallbackAsync(generalCache, pendingSlug.Key, connectionStory, _logger);

                if (pendingStory == null)
                {
                    _logger.LogError("Pending review article {0} not found in cache or log", pendingSlug.Key);
                    continue;
                }

                var pendingArticle = new StoryPublishedModel(pendingStory, role == ConfigUtil.JWT_ADMIN_ROLE || pendingStory.AuthorName == username);
                pendingArticle.IsReviewed = false;
                pendingArticle.RejectionReason = pendingSlug.Value;
                newPendingArticles.Add(pendingArticle);
                storyArticles.Add(pendingArticle);
            }

            // Preload metadata for newly added pending articles
            if (newPendingArticles.Count > 0)
                await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, newPendingArticles);
        }


        // 2. Get Recommendations from Gorse
        var userArticleIds = (hashtag == null && offset == 0)
            ? await httpClient.GetFromJsonAsync<List<string>>(_serverSettings.GorseAPIEndpoint + "recommend/" + username.ToLower() + "?write-back-type=read&n=" + count + "&offset=" + offset)
            : new List<string>();

        if (userArticleIds == null || userArticleIds.Count < count)
        {
            if (userArticleIds == null)
            {
                _logger.LogDebug("userArticleIds is null, fetching new articles for user!");
                userArticleIds = new List<string>();
            }
            else
            {
                _logger.LogDebug("Fetching additional articles, found: {0}", userArticleIds.Count);
            }

            if (hashtag != null)
            {
                var cmd = new MySqlCommand(SearchArticleByTagQuery, connectionStory);

                cmd.Parameters.AddWithValue("@tag", hashtag.ToLower());
                cmd.Parameters.AddWithValue("@count", count);
                cmd.Parameters.AddWithValue("@offset", offset);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    userArticleIds.Add(reader.GetString(0));
            }
            else
            {
                var topIdCmd = new MySql.Data.MySqlClient.MySqlCommand(
                    "SELECT us.story_id FROM user_stories us ORDER BY story_id DESC LIMIT 1", connectionStory);
                var topId = Convert.ToUInt32(await topIdCmd.ExecuteScalarAsync() ?? 0U);
                var selectFrom = topId - (uint)offset;

                var listCmd = new MySql.Data.MySqlClient.MySqlCommand(
                    "SELECT us.slug_title FROM user_stories us WHERE us.story_id <= @select_from ORDER BY us.story_id DESC LIMIT @count", connectionStory);
                listCmd.Parameters.AddWithValue("@select_from", selectFrom);
                listCmd.Parameters.AddWithValue("@count", count);

                await using (var reader = await listCmd.ExecuteReaderAsync())
                {
                    int slugOrdinal = reader.GetOrdinal("slug_title");
                    while (await reader.ReadAsync())
                    {
                        var newArticleId = reader.GetString(slugOrdinal);
                        if (hashtag == null && offset == 0 && userArticleIds.Contains(newArticleId)) continue;
                        // Skip if already hydrated from pending
                        if (storyArticles.Any(x => x.SlugTitle == newArticleId)) continue;
                        userArticleIds.Add(newArticleId);
                    }
                }
            }
        }

        _logger.LogDebug("Fetching articles, found: {0}", userArticleIds?.Count);
        if (userArticleIds == null || userArticleIds.Count == 0) return storyArticles;

        // 3. Hydrate from Cache
        foreach (var recommendation in userArticleIds)
        {
            var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, recommendation, connectionStory, _logger);
            if (res == null)
            {
                _logger.LogError("Did not find article with id: {0}", recommendation);
                try { await httpClient.DeleteAsync(_serverSettings.GorseAPIEndpoint + "item/" + recommendation); }
                catch (Exception ex) { _logger.LogError("Could not delete article {0}: {1}", recommendation, ex.Message); }
                continue;
            }
            storyArticles.Add(new StoryPublishedModel(res, role == ConfigUtil.JWT_ADMIN_ROLE || res.AuthorName == username));
        }

        // 4. Preload Metadata
        await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, storyArticles);

        return storyArticles;
    }


    [HttpGet("latest")]
    public async Task<List<StoryPublishedModel>> GetArticlesLatest(int count = 10, int offset = 0)
    {
        return await GetArticlesByTagLatest(null, count, offset);
    }


    [HttpGet("tag/{hashtag}")]
    public async Task<List<StoryPublishedModel>> GetArticlesByTagLatest(string? hashtag = null, int count = 10, int offset = 0)
    {
        var storyArticles = new List<StoryPublishedModel>();
        if (count <= 0 || offset < 0) return storyArticles;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using MySqlConnection connectionGorse = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connectionGorse.OpenAsync();

        var articleIds = new List<string>();

        if (hashtag != null)
        {
            var cmd = new MySqlCommand(SearchArticleByTagQuery, connectionStory);

            cmd.Parameters.AddWithValue("@tag", hashtag.ToLower());
            cmd.Parameters.AddWithValue("@count", count);
            cmd.Parameters.AddWithValue("@offset", offset);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                articleIds.Add(reader.GetString(0));
        }
        else
        {
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand(
                "SELECT us.story_id FROM user_stories us ORDER BY story_id DESC LIMIT 1", connectionStory);
            var topId = (uint)(await mySqlCommand.ExecuteScalarAsync() ?? 0U);
            var selectFrom = topId - (uint)offset;

            var listCmd = new MySql.Data.MySqlClient.MySqlCommand(
                "SELECT us.slug_title FROM user_stories us LEFT JOIN article_pending_review rev on rev.slug_title = us.slug_title WHERE us.story_id <= @select_from and rev.slug_title IS NULL ORDER BY us.story_id DESC LIMIT @count", connectionStory);
            listCmd.Parameters.AddWithValue("@select_from", selectFrom);
            listCmd.Parameters.AddWithValue("@count", count);

            await using (var reader = await listCmd.ExecuteReaderAsync())
            {
                int slugOrdinal = reader.GetOrdinal("slug_title");
                while (await reader.ReadAsync())
                    articleIds.Add(reader.GetString(slugOrdinal));
            }
        }

        if (!articleIds.Any()) return storyArticles;

        var cache = ArticleUtil.GetArticleCacheWithTtl(client);

        foreach (var id in articleIds)
        {
            var res = await ArticleUtil.TryGetWithFallbackAsync(cache, id, connectionStory, _logger);
            if (res != null)
            {
                storyArticles.Add(new StoryPublishedModel(res, false));
            }
        }

        await ArticleUtil.PreloadArticlesMetadataAsync(connectionGorse, connectionStory, storyArticles);

        return storyArticles;
    }


    [HttpGet("similar/{slugTitle}")]
    public async Task<List<StoryPublishedModel>> GetSimilarArticles(string slugTitle, int count = 10, int offset = 0)
    {
        var storyArticles = new List<StoryPublishedModel>();
        if (string.IsNullOrWhiteSpace(slugTitle)) return storyArticles;
        if (count <= 0 || offset < 0) return storyArticles;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using var httpClient = new HttpClient();
        using MySqlConnection connectionGorse = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(
            ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connectionGorse.OpenAsync();

        var url = $"{_serverSettings.GorseAPIEndpoint}item/{slugTitle}/neighbors?n={count}&offset={offset}";
        List<GorseItemRecommendation>? recommendations;
        try
        {
            recommendations = await httpClient.GetFromJsonAsync<List<GorseItemRecommendation>>(url);
        }
        catch (Exception ex)
        {
            _logger.LogError("GetSimilarArticles: Gorse request failed for '{0}': {1}", slugTitle, ex.Message);
            return storyArticles;
        }

        if (recommendations == null || recommendations.Count == 0)
            return storyArticles;

        var articleIds = recommendations
            .Where(r => r?.Id != null)
            .Select(r => r.Id!)
            .ToList();

        var cache = ArticleUtil.GetArticleCacheWithTtl(client);
        foreach (var id in articleIds)
        {
            var res = await ArticleUtil.TryGetWithFallbackAsync(cache, id, connectionStory, _logger);
            if (res != null)
            {
                storyArticles.Add(new StoryPublishedModel(res, false));
            }
        }

        await ArticleUtil.PreloadArticlesMetadataAsync(connectionGorse, connectionStory, storyArticles);

        return storyArticles;
    }



    [Authorize]
    [HttpGet("followed/{username}")]
    public async Task<UserFollowingModel> GetArticlesFollowed(string username, string followingLatestDate, int count = 5, int offset = 0)
    {
        var response = new List<UserFollowingInfo>();

        if (username == null)
        {
            _logger.LogError("Attempted to fetch followed articles without username!");
            return new UserFollowingModel(followingLatestDate, response);
        }
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            _logger.LogError("User was not authorized!");
            return new UserFollowingModel(followingLatestDate, response);
        }
        if (count <= 0 || offset < 0 || count >= byte.MaxValue)
        {
            _logger.LogError("Limits exceeded for fetching followed user posts!");
            return new UserFollowingModel(followingLatestDate, response);
        }

        var (responseTimestamp, responseFollowed) = await GetUserFollowingInfoAsync(username, followingLatestDate, count, offset);
        return new UserFollowingModel(responseTimestamp, responseFollowed);
    }


    private async Task<(string, IEnumerable<UserFollowingInfo>)> GetUserFollowingInfoAsync(
        string username, string followingLatestDate, int count, int offset)
    {
        var response = new List<UserFollowingInfo>();
        var fetchDate = followingLatestDate;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connection.OpenAsync();

        // 1. Fetch Following and Story Titles
        var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand.CommandText = "SELECT us.slug_title, uf.following, DATE_FORMAT(CURRENT_TIMESTAMP, '%Y-%m-%d %T') as timestamp FROM user_following uf LEFT JOIN user_stories us ON us.username = uf.following AND us.published_at > @from_date WHERE uf.username = @username ORDER BY uf.started_follow_at DESC LIMIT 200";
        mySqlCommand.Parameters.AddWithValue("@username", username);
        mySqlCommand.Parameters.AddWithValue("@from_date", followingLatestDate);
        mySqlCommand.Connection = connectionStory;

        await using (var reader = await mySqlCommand.ExecuteReaderAsync())
        {
            int slugOrdinal = reader.GetOrdinal("slug_title");
            int followingOrdinal = reader.GetOrdinal("following");
            int tsOrdinal = reader.GetOrdinal("timestamp");

            UserFollowingInfo? currentFollowingInfoUser = null;
            while (await reader.ReadAsync())
            {
                var currentRowUsername = reader.GetString(followingOrdinal);
                if (currentFollowingInfoUser?.Username != currentRowUsername)
                {
                    currentFollowingInfoUser = new UserFollowingInfo(currentRowUsername);
                    response.Add(currentFollowingInfoUser);
                }
                if (!await reader.IsDBNullAsync(slugOrdinal))
                    currentFollowingInfoUser.StoryTitles.Add(reader.GetString(slugOrdinal));
                fetchDate = reader.GetString(tsOrdinal);
            }
        }

        // 2. Fetch Profile Icons
        if (response.Count > 0)
        {
            var distinctUsernames = response.Select(x => x.Username).Distinct().ToList();
            var mySqlCommandBadge = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandBadge.CommandText = $"SELECT username, profile_icon FROM users WHERE username IN ({string.Join(",", distinctUsernames.Select(_ => "?"))})";
            mySqlCommandBadge.Connection = connectionStory;
            foreach (var name in distinctUsernames)
                mySqlCommandBadge.Parameters.Add(new MySqlParameter("", name));

            await using (var reader = await mySqlCommandBadge.ExecuteReaderAsync())
            {
                int userOrdinal = reader.GetOrdinal("username");
                int iconOrdinal = reader.GetOrdinal("profile_icon");
                while (await reader.ReadAsync())
                {
                    var badgeUsername = reader.GetString(userOrdinal);
                    var authorBadgeMetadata = await reader.IsDBNullAsync(iconOrdinal) ? null : reader.GetString(iconOrdinal);
                    if (authorBadgeMetadata == null) continue;
                    foreach (var authorArticle in response.Where(x => x.Username == badgeUsername))
                        authorArticle.ProfileIcon = authorBadgeMetadata;
                }
            }
        }

        // 3. Hydrate Stories from Cache
        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");
        foreach (UserFollowingInfo userFollowed in response)
        {
            foreach (string storyTitle in userFollowed.StoryTitles)
            {
                var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, storyTitle, connectionStory, _logger);
                if (res != null)
                {
                    userFollowed.NewStories.Add(new StoryPublishedModel(res, false));
                }
            }
            await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, userFollowed.NewStories);
        }

        // 4. Update latest check timestamp
        var mySqlCommandCheck2 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommandCheck2.CommandText = "UPDATE users SET following_latest_check_at = CURRENT_TIMESTAMP WHERE username = @username";
        mySqlCommandCheck2.Connection = connectionStory;
        mySqlCommandCheck2.Parameters.AddWithValue("@username", username);
        await mySqlCommandCheck2.ExecuteNonQueryAsync();

        return (fetchDate, response.OrderByDescending(x => x.NewStories.Count));
    }


    [Authorize]
    [HttpGet("liked/{username}")]
    public async Task<List<StoryPublishedModel>> GetLikedArticles(string username, int count = 10, int offset = 0)
    {
        var storyArticles = new List<StoryPublishedModel>();
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);

        if (role == null) return storyArticles;
        if (count <= 0 || offset < 0 || count >= byte.MaxValue) return storyArticles;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        using var httpClient = new HttpClient();
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connection.OpenAsync();

        var userArticleIdsEnum = await httpClient.GetFromJsonAsync<IEnumerable<GorseFeedbackModel>>(
            _serverSettings.GorseAPIEndpoint + "user/" + username + "/feedback/heart");

        if (userArticleIdsEnum == null || !userArticleIdsEnum.Any()) return storyArticles;

        var userArticleIds = userArticleIdsEnum.OrderByDescending(x => x.Timestamp).ToList();

        if (offset >= userArticleIds.Count) return storyArticles;
        int limit = Math.Max(Math.Min(count, userArticleIds.Count - offset), 0);
        userArticleIds = userArticleIds.GetRange(offset, limit);

        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");
        foreach (var feedback in userArticleIds)
        {
            var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, feedback.ItemId, connectionStory, _logger);
            if (res == null)
            {
                continue;
            }
            storyArticles.Add(new StoryPublishedModel(res, role == ConfigUtil.JWT_ADMIN_ROLE || res.AuthorName == username));
        }

        await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, storyArticles);
        return storyArticles;
    }


    [HttpGet("author/{username}")]
    public async Task<List<StoryPublishedModel>> GetPublishedBy(string username, int count = 10, int offset = 0)
    {
        var (loggedInUsername, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        var storyArticles = new List<StoryPublishedModel>();
        var userArticleIds = new List<string>();

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));

        await connectionStory.OpenAsync();
        await connection.OpenAsync();

        // Hydrate pending review articles when review mode is enabled
        if (_serverSettings.RequireArticleReview && loggedInUsername == username)
        {
            var pendingCmd = new MySql.Data.MySqlClient.MySqlCommand();
            pendingCmd.Connection = connectionStory;

            if (role == ConfigUtil.JWT_ADMIN_ROLE)
            {
                // Admins see all articles awaiting first review (no rejection reason set yet)
                pendingCmd.CommandText = "SELECT slug_title, rejection_reason FROM article_pending_review WHERE rejection_reason = ''";
            }
            else
            {
                // Regular users see only their own pending or rejected articles
                pendingCmd.CommandText = "SELECT slug_title,rejection_reason FROM article_pending_review WHERE username = @username";
                pendingCmd.Parameters.AddWithValue("@username", username);
            }

            var pendingSlugs = new Dictionary<string, string>();
            await using (var reader = await pendingCmd.ExecuteReaderAsync())
            {
                int slugOrdinal = reader.GetOrdinal("slug_title");
                int reason = reader.GetOrdinal("rejection_reason");
                while (await reader.ReadAsync())
                {
                    pendingSlugs.Add(reader.GetString(slugOrdinal), reader.GetString(reason));
                }
            }

            var newPendingArticles = new List<StoryPublishedModel>();
            foreach (var pendingSlug in pendingSlugs)
            {
                StorySavedModel? pendingStory = await ArticleUtil.TryGetWithFallbackAsync(generalCache, pendingSlug.Key, connectionStory, _logger);

                if (pendingStory == null)
                {
                    _logger.LogError("Pending review article {0} not found in cache or log", pendingSlug.Key);
                    continue;
                }

                var pendingArticle = new StoryPublishedModel(pendingStory, role == ConfigUtil.JWT_ADMIN_ROLE || pendingStory.AuthorName == loggedInUsername);
                pendingArticle.IsReviewed = false;
                pendingArticle.RejectionReason = pendingSlug.Value;
                newPendingArticles.Add(pendingArticle);
                storyArticles.Add(pendingArticle);
            }
        }

        var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand.CommandText = "SELECT slug_title FROM user_stories WHERE username = @username ORDER BY published_at DESC";
        mySqlCommand.Connection = connectionStory;
        mySqlCommand.Parameters.AddWithValue("@username", username);

        await using (var reader = await mySqlCommand.ExecuteReaderAsync())
        {
            int slugOrdinal = reader.GetOrdinal("slug_title");
            while (await reader.ReadAsync())
            {
                var articleId = reader.GetString(slugOrdinal);
                // Skip if already hydrated from pending
                if (storyArticles.Any(x => x.SlugTitle == articleId)) continue;
                userArticleIds.Add(articleId);
            }

        }

        if (userArticleIds.Count == 0) return storyArticles;
        if (offset >= userArticleIds.Count) return storyArticles;

        int limit = Math.Max(Math.Min(count, userArticleIds.Count - offset), 0);
        userArticleIds = userArticleIds.GetRange(offset, limit);
        foreach (string storyTitle in userArticleIds)
        {
            if (!string.IsNullOrEmpty(storyTitle))
            {
                var res = await ArticleUtil.TryGetWithFallbackAsync(generalCache, storyTitle, connectionStory, _logger);
                if (res != null)
                    storyArticles.Add(new StoryPublishedModel(res, role == ConfigUtil.JWT_ADMIN_ROLE || res.AuthorName == loggedInUsername));
            }
        }

        await ArticleUtil.PreloadArticlesMetadataAsync(connection, connectionStory, storyArticles);
        return storyArticles;
    }


    [Authorize]
    [HttpPost()]
    public async Task<string> PublishArticle([FromBody] StorySavedModel storyModel)
    {
        var role = (storyModel.AuthorName == null) ? null :
            ConfigUtil.VerifyUserNameFromClaimAndGetRole(storyModel.AuthorName, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            _logger.LogError("Attempt to publish article without proper authorization for user: {0}", storyModel.AuthorName);
            throw new ArgumentNullException("storyModel", "Not authorized: " + storyModel.AuthorName);
        }
        if (storyModel?.StoryTitle?.Length > 60)
            throw new ArgumentException("storyModel", "Title out of bounds!");
        if (storyModel?.EmptyTitle?.Length > 60)
            throw new ArgumentException("storyModel", "Empty title out of bounds!");
        if (storyModel?.ContentText?.Length > 10000)
            throw new ArgumentException("storyModel", "Content out of bounds!");

        LanguageDetector detector = new LanguageDetector();
        detector.AddAllLanguages();
        var detectedLanguage = detector.Detect(storyModel.ContentText);
        if (detectedLanguage != null)
            storyModel.LangCode = detectedLanguage;
        else
        {
            _logger.LogError("No detected language for story posted by {0}", storyModel.AuthorName);
            storyModel.LangCode = null; //overwrite keyboard input langcode sent by the app
        }

        using var httpClient = new HttpClient();
        var mainTitle = (storyModel.StoryTitle == null || storyModel.StoryTitle.Length == 0)
            ? storyModel.EmptyTitle : storyModel.StoryTitle;

        MySqlTransaction? myTrans = null;

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        await using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(
            ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        var mySqlCommandCheckUserExists = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommandCheckUserExists.CommandText = "SELECT COUNT(*) AS CNT FROM users WHERE username = @username";
        mySqlCommandCheckUserExists.Connection = connectionStory;
        mySqlCommandCheckUserExists.Parameters.AddWithValue("@username", storyModel.AuthorName);

        if ((long?)(await mySqlCommandCheckUserExists.ExecuteScalarAsync()) != 1)
            throw new ArgumentNullException("storyModel", "Not authorized: " + storyModel.AuthorName);

        _logger.LogDebug("Publishing story for user {0}, title {1}, descriptive title {2}",
            storyModel.AuthorName, storyModel.StoryTitle, storyModel.EmptyTitle);

        var config = new SlugHelperConfiguration();
        config.AllowedChars.Remove('.');
        config.AllowedChars.Remove('_');
        SlugHelper helper = new SlugHelper(config);

        var aCache = ArticleUtil.GetArticleCacheWithTtl(client);

        if (storyModel == null || storyModel.AuthorName == null)
            throw new ArgumentNullException("storyModel", "Article cannot be empty");
        if (mainTitle == null || mainTitle.Length == 0)
            throw new ArgumentNullException("storyModel.StoryTitle", "Article title cannot be empty");

        storyModel.SlugTitle = helper.GenerateSlug(mainTitle);
        storyModel.Timestamp = DateTime.UtcNow.ToString("o");
        storyModel.Tags = storyModel.Tags?.Select(x => x.ToLower())?.ToList();
        storyModel.IsReviewed = !_serverSettings.RequireArticleReview || role == ConfigUtil.JWT_ADMIN_ROLE;
        ArticleUtil.SanitizeStylingInfo(storyModel.StylingInfo, storyModel.ContentText?.Length ?? 1);

        try
        {
            if (!await aCache.WithKeepBinary<string, StorySavedModel>().PutIfAbsentAsync(storyModel.SlugTitle, storyModel))
            {
                var mySqlCommandCheckExistingPost = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandCheckExistingPost.CommandText = "SELECT slug_title FROM user_stories WHERE username = @username ORDER BY published_at DESC LIMIT 1";
                mySqlCommandCheckExistingPost.Connection = connectionStory;
                mySqlCommandCheckExistingPost.Parameters.AddWithValue("@username", storyModel.AuthorName);

                string? latestSlugTitle = (string?)await mySqlCommandCheckExistingPost.ExecuteScalarAsync();

                if (latestSlugTitle != null && latestSlugTitle.Equals(storyModel.SlugTitle))
                {
                    _logger.LogError("Story already published with slug_title: {0}", storyModel.SlugTitle);
                    return latestSlugTitle;
                }

                const int maxSlugLength = 60;
                var dateSuffix = " " + DateTime.UtcNow.ToString("g", DateTimeFormatInfo.InvariantInfo); // 17 chars
                var truncatedTitle = storyModel.SlugTitle.Length + dateSuffix.Length > maxSlugLength
                    ? storyModel.SlugTitle[..(maxSlugLength - dateSuffix.Length)]
                    : storyModel.SlugTitle;

                storyModel.SlugTitle = helper.GenerateSlug(truncatedTitle + dateSuffix);
                _logger.LogDebug("Slug collision — new URL title: {0}", storyModel.SlugTitle);

                if (!await aCache.WithKeepBinary<string, StorySavedModel>().PutIfAbsentAsync(storyModel.SlugTitle, storyModel))
                    throw new ArgumentNullException("storyModel.StoryTitle", "Article title is taken");
            }

            _logger.LogDebug("Story URL-title resolved to {0}", storyModel.SlugTitle);

            myTrans = await connectionStory.BeginTransactionAsync();

            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.Transaction = myTrans;
            mySqlCommandInsert.CommandText = "INSERT INTO user_stories (slug_title, username) VALUES (@slug_title, @username)";
            mySqlCommandInsert.Connection = connectionStory;
            mySqlCommandInsert.Parameters.AddWithValue("@slug_title", storyModel.SlugTitle);
            mySqlCommandInsert.Parameters.AddWithValue("@username", storyModel.AuthorName);

            if (await mySqlCommandInsert.ExecuteNonQueryAsync() <= 0)
                throw new Exception("Could not insert story record!");

            // Snapshot the published story so it can be recovered after cache expiry
            var mySqlCommandLog = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandLog.Transaction = myTrans;
            mySqlCommandLog.CommandText =
                "INSERT INTO user_story_log (slug_title, story_title, empty_title, story_compressed) " +
                "VALUES (@slug_title, @story_title, @empty_title, COMPRESS(@story))";
            mySqlCommandLog.Connection = connectionStory;
            mySqlCommandLog.Parameters.AddWithValue("@slug_title", storyModel.SlugTitle);
            mySqlCommandLog.Parameters.AddWithValue("@story_title", storyModel.StoryTitle ?? string.Empty);
            mySqlCommandLog.Parameters.AddWithValue("@empty_title", storyModel.EmptyTitle ?? string.Empty);
            mySqlCommandLog.Parameters.AddWithValue("@story", JsonSerializer.Serialize(storyModel));

            if (await mySqlCommandLog.ExecuteNonQueryAsync() <= 0)
                throw new Exception("Could not write story publish log!");

            if (_serverSettings.RequireArticleReview && role != ConfigUtil.JWT_ADMIN_ROLE)
            {
                // Queue for moderation instead of publishing to Gorse immediately
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var mySqlCommandReview = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandReview.Transaction = myTrans;
                mySqlCommandReview.CommandText = "INSERT INTO article_pending_review (slug_title, username, ip_address, rejection_reason) VALUES (@slug_title, @username, @ip_address, '')";
                mySqlCommandReview.Connection = connectionStory;
                mySqlCommandReview.Parameters.AddWithValue("@slug_title", storyModel.SlugTitle);
                mySqlCommandReview.Parameters.AddWithValue("@username", storyModel.AuthorName);
                mySqlCommandReview.Parameters.AddWithValue("@ip_address", ipAddress);

                if (await mySqlCommandReview.ExecuteNonQueryAsync() <= 0)
                    throw new Exception("Could not queue story for review!");

                await myTrans.CommitAsync();
                _logger.LogDebug("Story {0} queued for review", storyModel.SlugTitle);
            }
            else
            {
                await myTrans.CommitAsync();
                await ArticleUtil.QueuePublishEventAsync(connectionStory, storyModel, _logger);
            }

            return storyModel.SlugTitle;
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError("Exception publishing {0}: {1} — rolling back", storyModel.SlugTitle, ex.Message);

                if (!_serverSettings.RequireArticleReview || role == ConfigUtil.JWT_ADMIN_ROLE)
                    await httpClient.DeleteAsync(_serverSettings.GorseAPIEndpoint + "item/" + storyModel.SlugTitle);

                if (myTrans != null)
                    await myTrans.RollbackAsync();

                await aCache.WithKeepBinary<string, StorySavedModel>().RemoveAsync(storyModel.SlugTitle);
            }
            catch (MySqlException ex2)
            {
                _logger.LogError("Rollback failed: {0}", ex2.Message);
            }
            throw new Exception("Could not publish story due to internal error!");
        }
    }


    [Authorize]
    [HttpPut("{slugTitle}")]
    public async Task<StatusCodeResult> UpdateArticle(string slugTitle, [FromBody] StorySavedModel storyModel)
    {
        if (slugTitle == null)
        {
            _logger.LogError("SlugTitle was null for article {0} of user {1}", storyModel?.StoryTitle, storyModel?.AuthorName);
            return StatusCode(404);
        }

        var role = (storyModel.AuthorName == null) ? null :
            ConfigUtil.VerifyUserNameFromClaimAndGetRole(storyModel.AuthorName, HttpContext.User.Identity as ClaimsIdentity);

        if (role == null)
        {
            _logger.LogError("Unauthorized update attempt for user: {0}", storyModel?.AuthorName);
            return StatusCode(401);
        }

        if (storyModel?.StoryTitle?.Length > 60)
            throw new ArgumentException("storyModel", "Title out of bounds!");
        if (storyModel?.EmptyTitle?.Length > 60)
            throw new ArgumentException("storyModel", "Empty title out of bounds!");
        if (storyModel?.ContentText?.Length > 10000)
            throw new ArgumentException("storyModel", "Content out of bounds!");

        var mainTitle = (storyModel?.StoryTitle == null || storyModel.StoryTitle.Length == 0)
            ? storyModel.EmptyTitle : storyModel.StoryTitle;
        ArticleUtil.SanitizeStylingInfo(storyModel.StylingInfo, storyModel.ContentText?.Length ?? 1);

        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
        await using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(
            ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        _logger.LogDebug("Update request for user {0}, story {1}", storyModel.AuthorName, storyModel.StoryTitle);

        var aCache = ArticleUtil.GetArticleCacheWithTtl(client);

        if (storyModel == null || storyModel.AuthorName == null)
            throw new ArgumentNullException("storyModel", "Article cannot be empty");
        if (mainTitle == null || mainTitle.Length == 0)
            throw new ArgumentNullException("storyModel.StoryTitle", "Article title cannot be empty");

        try
        {
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT COUNT(*) FROM user_stories WHERE slug_title = @slug_title AND username = @username";
            mySqlCommand.Connection = connectionStory;
            mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommand.Parameters.AddWithValue("@username", storyModel.AuthorName);

            if ((long)(await mySqlCommand.ExecuteScalarAsync()) == 0L)
                return StatusCode(404);

            // Cache-miss fallback: hydrate from user_story_log before comparing authors
            StorySavedModel? oldStory = await ArticleUtil.TryGetWithFallbackAsync(aCache, slugTitle, connectionStory, _logger);
            if (oldStory == null || oldStory.AuthorName?.Equals(storyModel.AuthorName) != true)
            {
                _logger.LogError("Story {0} not found or author mismatch", slugTitle);
                return StatusCode(500);
            }

            storyModel.Category = oldStory.Category; //keep category classification
            storyModel.LangCode = oldStory.LangCode; //keep language classification
            storyModel.SlugTitle = slugTitle;
            storyModel.Timestamp = oldStory.Timestamp;
            storyModel.Tags = oldStory.Tags;

            // Log old version before overwriting — include titles for searchability
            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.CommandText =
                "INSERT INTO user_story_log (slug_title, story_title, empty_title, story_compressed) " +
                "VALUES (@slug_title, @story_title, @empty_title, COMPRESS(@story))";
            mySqlCommandInsert.Connection = connectionStory;
            mySqlCommandInsert.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommandInsert.Parameters.AddWithValue("@story_title", storyModel.StoryTitle ?? string.Empty);
            mySqlCommandInsert.Parameters.AddWithValue("@empty_title", storyModel.EmptyTitle ?? string.Empty);
            mySqlCommandInsert.Parameters.AddWithValue("@story", JsonSerializer.Serialize(storyModel));

            if (await mySqlCommandInsert.ExecuteNonQueryAsync() <= 0)
                return StatusCode(500);


            _logger.LogDebug("Updating story {0} in Ignite", slugTitle);

            if (!await aCache.WithKeepBinary<string, StorySavedModel>().ReplaceAsync(slugTitle, storyModel))
            {
                _logger.LogInformation("Story {0} missing in Ignite during update", slugTitle);
            }

            // When review is required, clear the rejection reason so the article
            // goes back to the pending queue for re-review after the author's edit
            if (_serverSettings.RequireArticleReview)
            {
                var clearRejectionCmd = new MySql.Data.MySqlClient.MySqlCommand();
                clearRejectionCmd.CommandText = "UPDATE article_pending_review SET rejection_reason = '' WHERE slug_title = @slug_title";
                clearRejectionCmd.Connection = connectionStory;
                clearRejectionCmd.Parameters.AddWithValue("@slug_title", slugTitle);
                var affected = await clearRejectionCmd.ExecuteNonQueryAsync();
                // affected == 0 means article was already accepted and removed from pending — that is fine
                _logger.LogDebug("Cleared rejection reason for {0}, rows affected: {1}", slugTitle, affected);
            }

            _logger.LogDebug("Successfully updated article {0}", slugTitle);
            return StatusCode(200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating article {0}", slugTitle);
            return StatusCode(500);
        }
    }
}