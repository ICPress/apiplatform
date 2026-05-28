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
public class CommentController : ControllerBase
{
    private readonly ILogger<CommentController> _logger;
    private readonly ServerSettings _serverSettings;

    public CommentController(ILogger<CommentController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [HttpGet("story/{slugTitle}")]
    public async Task<List<ArticleCommentPublished>> GetCommentsForStory(string slugTitle, int count = 10, int offset = 0)
    {
        var comments = new List<ArticleCommentPublished>();
        if (count <= 0 || offset < 0 || count >= 20) return comments;

        using (MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings)))
        {
            await connection.OpenAsync();
            // 1. Fetch Comments
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = @"WITH fw_cte AS (SELECT comment_uuid, ROW_NUMBER() OVER (ORDER BY comment_id DESC) as row_num FROM user_story_comment WHERE slug_title = @slug_title and reply_to_comment_uuid is null and hidden=0 )
                SELECT BIN_TO_UUID(comment_uuid) as comment_uuid, BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid, username, comment, hidden, deleted, lang_code, DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at 
                FROM user_story_comment WHERE slug_title = @slug_title AND comment_uuid IN (SELECT comment_uuid from fw_cte where row_num > @offset) LIMIT @count";

            mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommand.Parameters.AddWithValue("@offset", offset);
            mySqlCommand.Parameters.AddWithValue("@count", count);
            mySqlCommand.Connection = connection;

            await using (var reader = await mySqlCommand.ExecuteReaderAsync())
            {
                int uuidOrdinal = reader.GetOrdinal("comment_uuid");
                int replyUuidOrdinal = reader.GetOrdinal("reply_to_comment_uuid");
                int userOrdinal = reader.GetOrdinal("username");
                int commentOrdinal = reader.GetOrdinal("comment");
                int hiddenOrdinal = reader.GetOrdinal("hidden");
                int deletedOrdinal = reader.GetOrdinal("deleted");
                int langOrdinal = reader.GetOrdinal("lang_code");
                int tsOrdinal = reader.GetOrdinal("published_at");

                while (await reader.ReadAsync())
                {
                    string commentUuid = reader.GetString(uuidOrdinal);
                    string? replyToUuid = await reader.IsDBNullAsync(replyUuidOrdinal) ? null : reader.GetString(replyUuidOrdinal);
                    string commentUsername = reader.GetString(userOrdinal);
                    bool deleted = reader.GetBoolean(deletedOrdinal);
                    string commentText = deleted ? "" : reader.GetString(commentOrdinal);

                    var commentEntry = new ArticleCommentPublished(
                        commentUsername, slugTitle, commentUuid, replyToUuid, null, commentText,
                        reader.GetBoolean(hiddenOrdinal), deleted, reader.GetString(langOrdinal),
                        reader.GetString(tsOrdinal), false);

                    comments.Add(commentEntry);
                }
            }

            if (comments.Count > 0)
            {
                // 2. Fetch Reply Counts
                // Optimized to use parameters instead of string building UUIDs
                var replyCmd = new MySql.Data.MySqlClient.MySqlCommand();
                replyCmd.Connection = connection;
                replyCmd.CommandText = "SELECT COUNT(*) as CNT, BIN_TO_UUID(reply_to_comment_uuid) as reply_to_uuid FROM user_story_comment WHERE slug_title = @slug_title AND reply_to_comment_uuid IN (" +
                                       string.Join(",", comments.Select(_ => "UUID_TO_BIN(?)")) + ") GROUP BY reply_to_comment_uuid";

                replyCmd.Parameters.AddWithValue("@slug_title", slugTitle);
                foreach (var c in comments) replyCmd.Parameters.Add(new MySqlParameter("", c.CommentUUID));

                await using (var reader = await replyCmd.ExecuteReaderAsync())
                {
                    int cntOrdinal = reader.GetOrdinal("CNT");
                    int replyUuidOrdinal = reader.GetOrdinal("reply_to_uuid");
                    while (await reader.ReadAsync())
                    {
                        var readId = reader.GetString(replyUuidOrdinal);
                        var match = comments.FirstOrDefault(x => x.CommentUUID == readId);
                        if (match != null) match.NumReplies = Convert.ToUInt32(reader.GetInt64(cntOrdinal));
                    }
                }

                // 3. Fetch Likes/Hearts
                var likeCmd = new MySql.Data.MySqlClient.MySqlCommand();
                likeCmd.Connection = connection;
                likeCmd.CommandText = "SELECT COUNT(*) as CNT, BIN_TO_UUID(comment_uuid) as comment_uuid FROM user_story_comment_like WHERE comment_uuid IN (" +
                                      string.Join(",", comments.Select(_ => "UUID_TO_BIN(?)")) + ") GROUP BY comment_uuid";

                foreach (var c in comments) likeCmd.Parameters.Add(new MySqlParameter("", c.CommentUUID));

                await using (var reader = await likeCmd.ExecuteReaderAsync())
                {
                    int cntOrdinal = reader.GetOrdinal("CNT");
                    int uuidOrdinal = reader.GetOrdinal("comment_uuid");
                    while (await reader.ReadAsync())
                    {
                        var readId = reader.GetString(uuidOrdinal);
                        var match = comments.FirstOrDefault(x => x.CommentUUID == readId);
                        if (match != null) match.Hearts = Convert.ToUInt32(reader.GetInt64(cntOrdinal));
                    }
                }

                // 4. Preload Badges Asynchronously
                await GeneralUtil.PreloadBadgeDataAsync(connection, comments);
            }

        }
        return comments;
    }

    [Authorize]
    [HttpDelete("like/{slugTitle}/{commentUUID}")]
    public async Task<StatusCodeResult> DeleteLikeComment(string commentUUID)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username != null && role != null)
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();

            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "DELETE FROM user_story_comment_like WHERE comment_uuid = UUID_TO_BIN(@comment_uuid) AND username = @username";
            mySqlCommand.Parameters.AddWithValue("@comment_uuid", commentUUID);
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Connection = connection;
            await mySqlCommand.ExecuteNonQueryAsync();
            return StatusCode(200);

        }
        else
        {
            _logger.LogError("Username {0} unauthorized or role missing to like commentUUID: {1}", username, commentUUID);
            return StatusCode(400);
        }
    }

    [Authorize]
    [HttpPost("like/{slugTitle}/{commentUUID}")]
    public async Task<StatusCodeResult> LikeComment(string slugTitle, string commentUUID)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {
            _logger.LogError("Username {0} unauthorized or role missing to like commentUUID: {1}", username, commentUUID);
            return StatusCode(400);
        }
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand.CommandText = "INSERT IGNORE INTO user_story_comment_like (comment_uuid, username) VALUES (UUID_TO_BIN(@comment_uuid), @username)";
        mySqlCommand.Parameters.AddWithValue("@comment_uuid", commentUUID);
        mySqlCommand.Parameters.AddWithValue("@username", username);
        mySqlCommand.Connection = connection;
        await mySqlCommand.ExecuteNonQueryAsync();
        var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand5.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)";
        mySqlCommand5.Parameters.AddWithValue("@username", username);
        mySqlCommand5.Parameters.AddWithValue("@additional_data", slugTitle + ":" + commentUUID);
        mySqlCommand5.Parameters.AddWithValue("@type", (int)EventTriggerType.LIKE_COMMENT);
        mySqlCommand5.Connection = connection;
        await mySqlCommand5.ExecuteNonQueryAsync();
        return StatusCode(200);

    }


    [Authorize]
    [HttpDelete("{slugTitle}/{commentUUID}")]
    public async Task<StatusCodeResult> HideDeleteComment(string slugTitle, string commentUUID)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {
            _logger.LogError("Username {0} unauthorized or role missing to delete/hide commentUUID: {1}", username, commentUUID);
            return StatusCode(400);
        }
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand.CommandText = "UPDATE user_story_comment AS A INNER JOIN user_stories AS B on A.slug_title = B.slug_title SET A.hidden = CASE WHEN A.username = @username THEN A.hidden ELSE 1-A.hidden END, A.deleted = CASE WHEN A.username = @username THEN 1 ELSE A.deleted END" +
        " WHERE A.slug_title = @slug_title AND A.comment_uuid = UUID_TO_BIN(@comment_uuid) and (A.username = @username OR B.username = @username)";
        mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
        mySqlCommand.Parameters.AddWithValue("@comment_uuid", commentUUID);
        mySqlCommand.Parameters.AddWithValue("@username", username);
        mySqlCommand.Connection = connection;
        await mySqlCommand.ExecuteNonQueryAsync();
        return StatusCode(200);


    }


    [HttpGet("replies/{slugTitle}/{commentUUID}")]
    public async Task<List<ArticleCommentPublished>> GetCommentReplies(string commentUUID, string slugTitle, int count = 10, int offset = 0)
    {
        var comments = new List<ArticleCommentPublished>();
        if (count <= 0 || offset < 0 || count >= 20) return comments;

        using (MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings)))
        {
            await connection.OpenAsync();

            // 1. Fetch Replies
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = @"WITH fw_cte AS (SELECT comment_uuid, ROW_NUMBER() OVER (ORDER BY comment_id ASC) as row_num FROM user_story_comment WHERE slug_title = @slug_title and reply_to_comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) and hidden=0 )
                SELECT BIN_TO_UUID(comment_uuid) as comment_uuid, BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid, reply_to_username, username, comment, hidden, deleted, lang_code, DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at 
                FROM user_story_comment WHERE slug_title = @slug_title AND comment_uuid IN (SELECT comment_uuid from fw_cte where row_num > @offset) LIMIT @count";

            mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommand.Parameters.AddWithValue("@offset", offset);
            mySqlCommand.Parameters.AddWithValue("@count", count);
            mySqlCommand.Parameters.AddWithValue("@reply_to_comment_uuid", commentUUID);
            mySqlCommand.Connection = connection;

            await using (var reader = await mySqlCommand.ExecuteReaderAsync())
            {
                int uuidOrdinal = reader.GetOrdinal("comment_uuid");
                int replyUuidOrdinal = reader.GetOrdinal("reply_to_comment_uuid");
                int replyUserOrdinal = reader.GetOrdinal("reply_to_username");
                int userOrdinal = reader.GetOrdinal("username");
                int commentOrdinal = reader.GetOrdinal("comment");
                int hiddenOrdinal = reader.GetOrdinal("hidden");
                int deletedOrdinal = reader.GetOrdinal("deleted");
                int langOrdinal = reader.GetOrdinal("lang_code");
                int tsOrdinal = reader.GetOrdinal("published_at");

                while (await reader.ReadAsync())
                {
                    bool deleted = reader.GetBoolean(deletedOrdinal);
                    string commentText = deleted ? "" : reader.GetString(commentOrdinal);
                    string? replyToUsername = await reader.IsDBNullAsync(replyUserOrdinal) ? null : reader.GetString(replyUserOrdinal);

                    var commentEntry = new ArticleCommentPublished(
                        reader.GetString(userOrdinal),
                        slugTitle,
                        reader.GetString(uuidOrdinal),
                        reader.GetString(replyUuidOrdinal),
                        replyToUsername,
                        commentText,
                        reader.GetBoolean(hiddenOrdinal),
                        deleted,
                        reader.GetString(langOrdinal),
                        reader.GetString(tsOrdinal),
                        false);

                    comments.Add(commentEntry);
                }
            }

            if (comments.Count > 0)
            {
                // 2. Fetch Likes
                var likeCmd = new MySql.Data.MySqlClient.MySqlCommand();
                likeCmd.Connection = connection;
                likeCmd.CommandText = "SELECT COUNT(*) as CNT, BIN_TO_UUID(comment_uuid) as comment_uuid FROM user_story_comment_like WHERE comment_uuid IN (" +
                                      string.Join(",", comments.Select(_ => "UUID_TO_BIN(?)")) + ") GROUP BY comment_uuid";

                foreach (var c in comments) likeCmd.Parameters.Add(new MySqlParameter("", c.CommentUUID));

                await using (var reader = await likeCmd.ExecuteReaderAsync())
                {
                    int cntOrdinal = reader.GetOrdinal("CNT");
                    int uuidOrdinal = reader.GetOrdinal("comment_uuid");

                    while (await reader.ReadAsync())
                    {
                        var readId = reader.GetString(uuidOrdinal);
                        var match = comments.FirstOrDefault(x => x.CommentUUID == readId);
                        if (match != null) match.Hearts = Convert.ToUInt32(reader.GetInt64(cntOrdinal));
                    }
                }

                // 3. Preload Badges
                await GeneralUtil.PreloadBadgeDataAsync(connection, comments);
            }

        }
        return comments;
    }

    [Authorize]
    [HttpGet("user/{username}/replies/{slugTitle}/{commentUUID}")]
    public async Task<List<ArticleCommentPublished>> GetCommentRepliesForUser(string username, string commentUUID, string slugTitle, int count = 10, int offset = 0)
    {
        var comments = new List<ArticleCommentPublished>();
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);

        if (role == null) return comments;
        if (count <= 0 || offset < 0 || count >= 20) return comments;

        using (MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings)))
        {
            await connection.OpenAsync();

            // 1. Fetch Replies with User-specific Like status
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = @"WITH fw_cte AS (
                    SELECT A.comment_uuid, ROW_NUMBER() OVER (ORDER BY A.comment_id ASC) as row_num 
                    FROM user_story_comment AS A 
                    INNER JOIN user_stories AS B ON A.slug_title = B.slug_title 
                    WHERE A.slug_title = @slug_title AND A.reply_to_comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) 
                    AND (A.hidden=0 OR B.username = @username)
                )
                SELECT BIN_TO_UUID(A.comment_uuid) as comment_uuid, BIN_TO_UUID(A.reply_to_comment_uuid) as reply_to_comment_uuid, 
                A.reply_to_username, A.username, A.comment, A.hidden, A.deleted, A.lang_code, 
                DATE_FORMAT(A.published_at, '%Y-%m-%dT%TZ') as published_at, 1-ISNULL(LK.comment_uuid) as liked 
                FROM user_story_comment A
                LEFT JOIN user_story_comment_like AS LK ON LK.comment_uuid = A.comment_uuid AND LK.username = @username  
                WHERE A.slug_title = @slug_title AND A.comment_uuid IN (SELECT comment_uuid FROM fw_cte WHERE row_num > @offset) LIMIT @count";

            mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommand.Parameters.AddWithValue("@offset", offset);
            mySqlCommand.Parameters.AddWithValue("@count", count);
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@reply_to_comment_uuid", commentUUID);
            mySqlCommand.Connection = connection;

            await using (var reader = await mySqlCommand.ExecuteReaderAsync())
            {
                int uuidOrd = reader.GetOrdinal("comment_uuid");
                int replyUuidOrd = reader.GetOrdinal("reply_to_comment_uuid");
                int replyUserOrd = reader.GetOrdinal("reply_to_username");
                int userOrd = reader.GetOrdinal("username");
                int commentOrd = reader.GetOrdinal("comment");
                int hiddenOrd = reader.GetOrdinal("hidden");
                int deletedOrd = reader.GetOrdinal("deleted");
                int langOrd = reader.GetOrdinal("lang_code");
                int tsOrd = reader.GetOrdinal("published_at");
                int likedOrd = reader.GetOrdinal("liked");

                while (await reader.ReadAsync())
                {
                    bool deleted = reader.GetBoolean(deletedOrd);
                    string commentText = deleted ? "" : reader.GetString(commentOrd);
                    string? replyToUsername = await reader.IsDBNullAsync(replyUserOrd) ? null : reader.GetString(replyUserOrd);

                    var commentEntry = new ArticleCommentPublished(
                        reader.GetString(userOrd),
                        slugTitle,
                        reader.GetString(uuidOrd),
                        reader.GetString(replyUuidOrd),
                        replyToUsername,
                        commentText,
                        reader.GetBoolean(hiddenOrd),
                        deleted,
                        reader.GetString(langOrd),
                        reader.GetString(tsOrd),
                        reader.GetBoolean(likedOrd)
                    );
                    comments.Add(commentEntry);
                }
            }

            if (comments.Count > 0)
            {
                // 2. Fetch Total Hearts Count for each comment
                var heartCmd = new MySql.Data.MySqlClient.MySqlCommand();
                heartCmd.Connection = connection;
                heartCmd.CommandText = "SELECT COUNT(*) as CNT, BIN_TO_UUID(comment_uuid) as comment_uuid FROM user_story_comment_like WHERE comment_uuid IN (" +
                                       string.Join(",", comments.Select(_ => "UUID_TO_BIN(?)")) + ") GROUP BY comment_uuid";

                foreach (var c in comments) heartCmd.Parameters.Add(new MySqlParameter("", c.CommentUUID));

                await using (var reader = await heartCmd.ExecuteReaderAsync())
                {
                    int cntOrd = reader.GetOrdinal("CNT");
                    int uuidOrd = reader.GetOrdinal("comment_uuid");

                    while (await reader.ReadAsync())
                    {
                        var readId = reader.GetString(uuidOrd);
                        var match = comments.FirstOrDefault(x => x.CommentUUID == readId);
                        if (match != null) match.Hearts = Convert.ToUInt32(reader.GetInt64(cntOrd));
                    }
                }

                // 3. Preload Profile Badges
                await GeneralUtil.PreloadBadgeDataAsync(connection, comments);
            }

        }
        return comments;
    }

    [Authorize]
    [HttpGet("user/{username}/{slugTitle}")]
    public async Task<List<ArticleCommentPublished>> GetCommentsForStoryUser(string username, string slugTitle, int count = 10, int offset = 0)
    {
        var comments = new List<ArticleCommentPublished>();
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null) return comments;
        if (count <= 0 || offset < 0 || count >= byte.MaxValue) return comments;

        using (MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings)))
        {
            await connection.OpenAsync();
            // 1. Fetch Top-level Comments
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = @"WITH fw_cte AS (SELECT A.comment_uuid, ROW_NUMBER() OVER (ORDER BY A.comment_id DESC) as row_num FROM user_story_comment AS A INNER JOIN user_stories AS B on A.slug_title = B.slug_title WHERE A.slug_title = @slug_title and A.reply_to_comment_uuid is null AND (A.hidden = 0 OR B.username = @username))
                SELECT BIN_TO_UUID(A.comment_uuid) as comment_uuid, BIN_TO_UUID(A.reply_to_comment_uuid) as reply_to_comment_uuid, A.username, A.comment, A.hidden, A.deleted, A.lang_code, DATE_FORMAT(A.published_at, '%Y-%m-%dT%TZ') as published_at, 1-ISNULL(LK.comment_uuid) as liked FROM user_story_comment AS A
                LEFT JOIN user_story_comment_like AS LK ON LK.comment_uuid = A.comment_uuid AND LK.username = @username WHERE A.slug_title = @slug_title AND A.comment_uuid IN (SELECT comment_uuid from fw_cte where row_num > @offset) LIMIT @count";

            mySqlCommand.Parameters.AddWithValue("@slug_title", slugTitle);
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@offset", offset);
            mySqlCommand.Parameters.AddWithValue("@count", count);
            mySqlCommand.Connection = connection;

            await using (var reader = await mySqlCommand.ExecuteReaderAsync())
            {
                int uuidOrd = reader.GetOrdinal("comment_uuid");
                int replyUuidOrd = reader.GetOrdinal("reply_to_comment_uuid");
                int userOrd = reader.GetOrdinal("username");
                int commentOrd = reader.GetOrdinal("comment");
                int hiddenOrd = reader.GetOrdinal("hidden");
                int deletedOrd = reader.GetOrdinal("deleted");
                int langOrd = reader.GetOrdinal("lang_code");
                int tsOrd = reader.GetOrdinal("published_at");
                int likedOrd = reader.GetOrdinal("liked");

                while (await reader.ReadAsync())
                {
                    bool deleted = reader.GetBoolean(deletedOrd);
                    var commentEntry = new ArticleCommentPublished(
                        reader.GetString(userOrd),
                        slugTitle,
                        reader.GetString(uuidOrd),
                        await reader.IsDBNullAsync(replyUuidOrd) ? null : reader.GetString(replyUuidOrd),
                        null,
                        deleted ? "" : reader.GetString(commentOrd),
                        reader.GetBoolean(hiddenOrd),
                        deleted,
                        reader.GetString(langOrd),
                        reader.GetString(tsOrd),
                        reader.GetBoolean(likedOrd));
                    comments.Add(commentEntry);
                }
            }

            if (comments.Count > 0)
            {
                // 2. Fetch Reply Counts
                var commentsUUIDSelectStrBuilder = new StringBuilder("SELECT COUNT(*) as CNT,BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid FROM user_story_comment WHERE slug_title = @slug_title and reply_to_comment_uuid IN (");
                for (var i = 0; i < comments.Count; i++)
                {
                    commentsUUIDSelectStrBuilder.Append("UUID_TO_BIN('");
                    commentsUUIDSelectStrBuilder.Append(comments[i].CommentUUID);
                    commentsUUIDSelectStrBuilder.Append("')");
                    if (i + 1 < comments.Count) commentsUUIDSelectStrBuilder.Append(",");
                }
                commentsUUIDSelectStrBuilder.Append(") group by reply_to_comment_uuid");
                var replyCmd = new MySql.Data.MySqlClient.MySqlCommand();
                replyCmd.CommandText = commentsUUIDSelectStrBuilder.ToString();
                replyCmd.Parameters.AddWithValue("@slug_title", slugTitle);
                replyCmd.Connection = connection;

                await using (var reader = await replyCmd.ExecuteReaderAsync())
                {
                    int cntOrd = reader.GetOrdinal("CNT");
                    int idOrd = reader.GetOrdinal("reply_to_comment_uuid");
                    while (await reader.ReadAsync())
                    {
                        var match = comments.FirstOrDefault(x => x.CommentUUID == reader.GetString(idOrd));
                        if (match != null) match.NumReplies = Convert.ToUInt32(reader.GetInt64(cntOrd));
                    }
                }

                // 3. Fetch Hearts Count
                var heartCmd = new MySql.Data.MySqlClient.MySqlCommand();
                heartCmd.CommandText = "SELECT COUNT(*) as CNT, BIN_TO_UUID(comment_uuid) as comment_uuid FROM user_story_comment_like WHERE comment_uuid IN (" + string.Join(",", comments.Select(_ => "UUID_TO_BIN(?)")) + ") GROUP BY comment_uuid";
                foreach (var c in comments) heartCmd.Parameters.Add(new MySqlParameter("", c.CommentUUID));
                heartCmd.Connection = connection;

                await using (var reader = await heartCmd.ExecuteReaderAsync())
                {
                    int cntOrd = reader.GetOrdinal("CNT");
                    int uuidOrd = reader.GetOrdinal("comment_uuid");
                    while (await reader.ReadAsync())
                    {
                        var match = comments.FirstOrDefault(x => x.CommentUUID == reader.GetString(uuidOrd));
                        if (match != null) match.Hearts = Convert.ToUInt32(reader.GetInt64(cntOrd));
                    }
                }
            }

            // 4. Handle Sub-replies for user's own comments (if offset 0)
            if (offset == 0)
            {
                var userComments = comments.Where(x => x.AuthorName.Equals(username)).ToList();
                foreach (var parentComment in userComments)
                {
                    var subReplyCmd = new MySql.Data.MySqlClient.MySqlCommand();
                    subReplyCmd.CommandText = @"SELECT BIN_TO_UUID(A.comment_uuid) as comment_uuid, BIN_TO_UUID(A.reply_to_comment_uuid) as reply_to_comment_uuid, A.reply_to_username, A.username, A.comment, A.hidden, A.deleted, A.lang_code, DATE_FORMAT(A.published_at, '%Y-%m-%dT%TZ') as published_at, 1-ISNULL(LK.comment_uuid) as liked 
                        FROM user_story_comment AS A INNER JOIN user_stories AS B ON A.slug_title = B.slug_title 
                        LEFT JOIN user_story_comment_like AS LK ON LK.comment_uuid = A.comment_uuid AND LK.username = @username  
                        WHERE A.reply_to_comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) AND (A.hidden = 0 OR B.username=@username)";

                    subReplyCmd.Parameters.AddWithValue("@reply_to_comment_uuid", parentComment.CommentUUID);
                    subReplyCmd.Parameters.AddWithValue("@username", username);
                    subReplyCmd.Connection = connection;

                    await using (var reader = await subReplyCmd.ExecuteReaderAsync())
                    {
                        int uuidOrd = reader.GetOrdinal("comment_uuid");
                        int replyUuidOrd = reader.GetOrdinal("reply_to_comment_uuid");
                        int replyUserOrd = reader.GetOrdinal("reply_to_username");
                        int userOrd = reader.GetOrdinal("username");
                        int commentOrd = reader.GetOrdinal("comment");
                        int hiddenOrd = reader.GetOrdinal("hidden");
                        int deletedOrd = reader.GetOrdinal("deleted");
                        int langOrd = reader.GetOrdinal("lang_code");
                        int tsOrd = reader.GetOrdinal("published_at");
                        int likedOrd = reader.GetOrdinal("liked");

                        while (await reader.ReadAsync())
                        {
                            bool deleted = reader.GetBoolean(deletedOrd);
                            parentComment.Replies.Add(new ArticleCommentPublished(
                                reader.GetString(userOrd), slugTitle, reader.GetString(uuidOrd), reader.GetString(replyUuidOrd),
                                await reader.IsDBNullAsync(replyUserOrd) ? null : reader.GetString(replyUserOrd),
                                deleted ? "" : reader.GetString(commentOrd),
                                reader.GetBoolean(hiddenOrd), deleted, reader.GetString(langOrd), reader.GetString(tsOrd),
                                reader.GetBoolean(likedOrd)));
                        }
                    }
                }
            }

            if (comments.Count > 0)
            {
                await GeneralUtil.PreloadBadgeDataAsync(connection, comments);
            }

        }
        return comments;
    }


    [Authorize]
    [HttpPost()]
    public async Task<StatusCodeResult> PublishComment([FromBody] ArticleComment commentModel)
    {
        var role = (commentModel.AuthorName == null) ? null : ConfigUtil.VerifyUserNameFromClaimAndGetRole(commentModel.AuthorName, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            _logger.LogError("Could not verify author for commentUUID {0} posted by {1}", commentModel.CommentUUID, commentModel.AuthorName);
            return StatusCode(401); // Changed to 401 Unauthorized for better API semantics
        }

        // Detect language
        LanguageDetector detector = new LanguageDetector();
        detector.AddAllLanguages();
        var langCode = detector.Detect(commentModel.Comment) ?? "";

        if (string.IsNullOrEmpty(langCode))
        {
            _logger.LogError("No detected language for commentUUID {0} posted by {1}", commentModel.CommentUUID, commentModel.AuthorName);
        }

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        // 1. Check if comment already exists
        var checkCmd = new MySql.Data.MySqlClient.MySqlCommand(
            "SELECT COUNT(*) FROM user_story_comment WHERE comment_uuid = UUID_TO_BIN(@comment_uuid) AND slug_title = @slug_title",
            connection);
        checkCmd.Parameters.AddWithValue("@comment_uuid", commentModel.CommentUUID);
        checkCmd.Parameters.AddWithValue("@slug_title", commentModel.SlugTitle);

        var existingCount = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
        if (existingCount > 0)
        {
            return StatusCode(200); // Comment already submitted
        }

        // 2. Insert the new comment
        // Note: Logic maintains your nested COALESCE for reply_to_comment_uuid hierarchy
        var insertCmd = new MySql.Data.MySqlClient.MySqlCommand(@"
            INSERT INTO user_story_comment (comment_uuid, reply_to_comment_uuid, slug_title, username, comment, lang_code, reply_to_username, original_reply_to_comment_uuid) 
            VALUES (
                UUID_TO_BIN(@comment_uuid), 
                COALESCE((SELECT B.reply_to_comment_uuid FROM user_story_comment B WHERE comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) LIMIT 1), UUID_TO_BIN(@reply_to_comment_uuid)), 
                @slug_title, 
                @username, 
                @comment, 
                @lang_code, 
                (SELECT B.username FROM user_story_comment B WHERE comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) LIMIT 1), 
                UUID_TO_BIN(@reply_to_comment_uuid)
            )", connection);

        insertCmd.Parameters.AddWithValue("@comment_uuid", commentModel.CommentUUID);
        insertCmd.Parameters.AddWithValue("@reply_to_comment_uuid", commentModel.ReplyToCommentUUID);
        insertCmd.Parameters.AddWithValue("@slug_title", commentModel.SlugTitle);
        insertCmd.Parameters.AddWithValue("@username", commentModel.AuthorName);
        insertCmd.Parameters.AddWithValue("@comment", commentModel.Comment);
        insertCmd.Parameters.AddWithValue("@lang_code", langCode);

        if (await insertCmd.ExecuteNonQueryAsync() > 0)
        {
            // 3. Queue the event notification
            var eventCmd = new MySql.Data.MySqlClient.MySqlCommand(
                "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)",
                connection);

            string additionalData = $"{commentModel.SlugTitle}:{commentModel.CommentUUID}" +
                                    (commentModel.ReplyToCommentUUID != null ? $":{commentModel.ReplyToCommentUUID}" : "");

            int eventType = (commentModel.ReplyToCommentUUID != null)
                ? (int)EventTriggerType.REPLY_COMMENT
                : (int)EventTriggerType.WRITE_COMMENT;

            eventCmd.Parameters.AddWithValue("@username", commentModel.AuthorName);
            eventCmd.Parameters.AddWithValue("@additional_data", additionalData);
            eventCmd.Parameters.AddWithValue("@type", eventType);

            await eventCmd.ExecuteNonQueryAsync();
            return StatusCode(200);
        }

        return StatusCode(400);

    }

}