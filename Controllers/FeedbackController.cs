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
public class FeedbackController : ControllerBase
{
    private readonly ILogger<FeedbackController> _logger;
    private readonly ServerSettings _serverSettings;

    public FeedbackController(ILogger<FeedbackController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [Authorize]
    [HttpDelete("{username}/{storyId}")]
    public async Task<StatusCodeResult> RemoveLikeArticle(string username, string storyId)
    {
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            return StatusCode(400);
        }

        using var httpClient = new HttpClient();
        var result = await httpClient.DeleteAsync(_serverSettings.GorseAPIEndpoint + "feedback/" + username + "/" + storyId);
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogError("Attemt to like article with URL-title {0} async to GORSE ended with statuscode {1} and respone {2} ", storyId, result.StatusCode, await result.Content.ReadAsStringAsync());
            return StatusCode((int)result.StatusCode);
        }
        else
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();
            try
            {
                var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand5.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)";
                mySqlCommand5.Parameters.AddWithValue("@username", username);
                mySqlCommand5.Parameters.AddWithValue("@additional_data", storyId);
                mySqlCommand5.Parameters.AddWithValue("@type", (int)EventTriggerType.LIKE_DELETE);
                mySqlCommand5.Connection = connection;
                if ((long)await mySqlCommand5.ExecuteNonQueryAsync() > 0)
                {
                    return StatusCode(200);
                }
                else return StatusCode(500);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

    }

    [Authorize]
    [HttpPost("{feedbackType}/{username}/{storyId}")]
    public async Task<StatusCodeResult> LikeArticle(string feedbackType, string username, string storyId)
    {
        if (!feedbackType.Equals("heart", StringComparison.OrdinalIgnoreCase) && !feedbackType.Equals("read", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Attemt to provide feedback of incorrect type {0} for username {1} and storyId {2}", feedbackType, username, storyId);
            return StatusCode(400);
        }
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            return StatusCode(400);
        }
        using var httpClient = new HttpClient();
        var feedbackModel = new GorseFeedbackModel();
        feedbackModel.FeedbackType = feedbackType; //"heart"
        feedbackModel.ItemId = storyId;
        feedbackModel.Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        feedbackModel.UserId = username.ToLower();
        var result = await httpClient.PutAsJsonAsync(_serverSettings.GorseAPIEndpoint + "feedback", new GorseFeedbackModel[] { feedbackModel });
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogError("Attemt to like article with URL-title {0} async to GORSE ended with statuscode {1} and respone {2} ", storyId, result.StatusCode, await result.Content.ReadAsStringAsync());
            return StatusCode(((int)result.StatusCode));
        }
        else
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();
            try
            {
                var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand5.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)";
                mySqlCommand5.Parameters.AddWithValue("@username", username);
                mySqlCommand5.Parameters.AddWithValue("@additional_data", storyId);
                mySqlCommand5.Parameters.AddWithValue("@type", (int)EventTriggerType.LIKE);
                mySqlCommand5.Connection = connection;
                if ((long)await mySqlCommand5.ExecuteNonQueryAsync() > 0)
                {
                    return StatusCode(200);
                }
                else return StatusCode(500);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }


    }


}