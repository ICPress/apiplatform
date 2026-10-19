using Microsoft.AspNetCore.Mvc;
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
public class AdminController : ControllerBase
{
    private readonly ILogger<AdminController> _logger;


    private readonly ServerSettings _serverSettings;

    public AdminController(ILogger<AdminController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [Authorize]
    [HttpDelete("user/{userToTerminate}")]
    public async Task<StatusCodeResult> TerminateUser(string userToTerminate)
    {
        (string? username, string? role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role != null && username != null && role.Equals(ConfigUtil.JWT_ADMIN_ROLE) )
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            try
            {
                await connection.OpenAsync();
                var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandInsert.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@usernameToTerminate, '', @type)";
                mySqlCommandInsert.Connection = connection;
                mySqlCommandInsert.Parameters.AddWithValue("@usernameToTerminate", userToTerminate);
                mySqlCommandInsert.Parameters.AddWithValue("@type", (int)EventTriggerType.TERMINATE_ACCOUNT);
                await mySqlCommandInsert.ExecuteNonQueryAsync();
                return StatusCode(201);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        return await Task.FromResult(StatusCode(400));
    }



    [Authorize]
    [HttpDelete("article/{usernameAffected}/{articleToDelete}")]
    public async Task<StatusCodeResult> DeleteArticle(string usernameAffected,string articleToDelete)
    {
        (string? username, string? role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role != null && username != null && role.Equals(ConfigUtil.JWT_ADMIN_ROLE))
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            try
            {
                await connection.OpenAsync();
                var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandInsert.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@usernameToTerminate, @additional_data, @type)";
                mySqlCommandInsert.Connection = connection;
                mySqlCommandInsert.Parameters.AddWithValue("@usernameToTerminate", usernameAffected);
                mySqlCommandInsert.Parameters.AddWithValue("@additional_data", articleToDelete);
                mySqlCommandInsert.Parameters.AddWithValue("@type", (int)EventTriggerType.REMOVE_ARTICLE);
                await mySqlCommandInsert.ExecuteNonQueryAsync();
                return StatusCode(201);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        return await Task.FromResult(StatusCode(400));
    }

    [Authorize]
    [HttpPost("article/{usernameAffected}/accept/{slugTitle}")]
    public async Task<StatusCodeResult> ReviewAccepted(string slugTitle, string usernameAffected)
    {
        var (_, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role != ConfigUtil.JWT_ADMIN_ROLE)
        {
            _logger.LogError("Unauthorized attempt to accept review for {0}", slugTitle);
            return StatusCode(401);
        }

        using var httpClient = new HttpClient();
        await using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(
            ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        var storyModel = await ArticleUtil.GetAsync(slugTitle, connectionStory, _logger);

        if (storyModel == null)
        {
            _logger.LogError("ReviewAccepted: article {0} not found in database", slugTitle);
            return StatusCode(404);
        }

        // Verify it is actually pending review
        var checkCmd = new MySql.Data.MySqlClient.MySqlCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM article_pending_review WHERE slug_title = @slug_title";
        checkCmd.Connection = connectionStory;
        checkCmd.Parameters.AddWithValue("@slug_title", slugTitle);

        if ((long)(await checkCmd.ExecuteScalarAsync()) == 0)
        {
            _logger.LogError("ReviewAccepted: article {0} not found in pending review table", slugTitle);
            return StatusCode(404);
        }

        try
        {
            using var transaction = await connectionStory.BeginTransactionAsync();
            using var deleteCmd = new MySqlCommand("DELETE FROM article_pending_review WHERE slug_title = @slug_title", connectionStory, transaction);
            deleteCmd.Parameters.AddWithValue("@slug_title", slugTitle);
            if (await deleteCmd.ExecuteNonQueryAsync() == 0) return StatusCode(404);
            storyModel.IsReviewed = true;
            using var snapshot = new MySqlCommand(@"INSERT INTO user_story_log (slug_title, story_title, empty_title, story_compressed)
                VALUES (@slug, @title, @emptyTitle, COMPRESS(@story))", connectionStory, transaction);
            snapshot.Parameters.AddWithValue("@slug", slugTitle);
            snapshot.Parameters.AddWithValue("@title", storyModel.StoryTitle ?? "");
            snapshot.Parameters.AddWithValue("@emptyTitle", storyModel.EmptyTitle ?? "");
            snapshot.Parameters.AddWithValue("@story", JsonSerializer.Serialize(storyModel));
            await snapshot.ExecuteNonQueryAsync();
            using var publish = new MySqlCommand("INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES (@username, @slug, @type)", connectionStory, transaction);
            publish.Parameters.AddWithValue("@username", storyModel.AuthorName);
            publish.Parameters.AddWithValue("@slug", slugTitle);
            publish.Parameters.AddWithValue("@type", (int)EventTriggerType.PUBLISHED_ARTICLE);
            await publish.ExecuteNonQueryAsync();
            await transaction.CommitAsync();

            _logger.LogDebug("ReviewAccepted: article {0} published to Gorse", slugTitle);
            return StatusCode(200);
        }
        catch (Exception ex)
        {
            _logger.LogError("ReviewAccepted failed for {0}: {1}", slugTitle, ex.Message);
            return StatusCode(500);
        }
    }


    [Authorize]
    [HttpPost("article/{usernameAffected}/reject/{slugTitle}")]
    public async Task<StatusCodeResult> ReviewRejected(string slugTitle, string usernameAffected, [FromBody] string rejectionReason)
    {
        var (_, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role != ConfigUtil.JWT_ADMIN_ROLE)
        {
            _logger.LogError("Unauthorized attempt to reject review for {0}", slugTitle);
            return StatusCode(401);
        }

        if (string.IsNullOrWhiteSpace(rejectionReason) || rejectionReason.Length > 190)
        {
            _logger.LogError("Invalid rejection reason for {0} — must be 1–190 characters", slugTitle);
            return StatusCode(400);
        }

        await using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(
            ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        var updateCmd = new MySql.Data.MySqlClient.MySqlCommand();
        updateCmd.CommandText = "UPDATE article_pending_review SET rejection_reason = @reason WHERE slug_title = @slug_title";
        updateCmd.Connection = connectionStory;
        updateCmd.Parameters.AddWithValue("@reason", rejectionReason);
        updateCmd.Parameters.AddWithValue("@slug_title", slugTitle);

        var rows = await updateCmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            _logger.LogError("ReviewRejected: article {0} not found in pending review", slugTitle);
            return StatusCode(404);
        }

        var notificationQueryCommand = GeneralUtil.CreateNotificationMySQLCommand(connectionStory, null,
                 usernameAffected, $"{slugTitle}:{rejectionReason}", TransactionDescriptionType.NONE, DateTime.UtcNow, NotificationType.ARTICLE_REJECTED);
        await notificationQueryCommand.ExecuteNonQueryAsync();

        _logger.LogDebug("ReviewRejected: article {0} rejected with reason: {1}", slugTitle, rejectionReason);
        return StatusCode(200);
    }
}