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
public class MessageController : ControllerBase
{
    private readonly ILogger<TokenController> _logger;
    private readonly ServerSettings _serverSettings;

    public MessageController(ILogger<TokenController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [Authorize]
    [HttpGet("{fromUsername}")]
    public async Task<List<MessagePublishedModel>>? GetMessages(string fromUsername, uint? startIndex, int count = 5)
    {
        var response = new List<MessagePublishedModel>();
        if (fromUsername == null)
        {
            _logger.LogError("Attemted to fetch messages without target username, returning empty result!");
            return response;
        }
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username != null && role != null)
        {
            using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            try
            {
                await connectionStory.OpenAsync();
                var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand.CommandText = "SELECT mess.message_id, BIN_TO_UUID(message_uuid) as message_uuid, mess.target_username,mess.username,mess.content, mess.type, mess.deleted,mess.is_read, DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at FROM user_message mess  WHERE ((mess.username = @username and mess.target_username = @fromUsername) OR (mess.username = @fromUsername and mess.target_username = @username)) and mess.message_id < coalesce(@startIndex,4294967295)  order by mess.message_id desc LIMIT @count";
                mySqlCommand.Parameters.AddWithValue("@username", username);
                mySqlCommand.Parameters.AddWithValue("@fromUsername", fromUsername);
                mySqlCommand.Parameters.AddWithValue("@startIndex", startIndex);
                mySqlCommand.Parameters.AddWithValue("@count", count);
                mySqlCommand.Connection = connectionStory;
                using (var reader = mySqlCommand.ExecuteReader())
                {
                    while (await reader.ReadAsync())
                    {
                        uint messageId = reader.GetUInt32("message_id");
                        string messageUUID = reader.GetString("message_uuid");
                        string targetUsername = reader.GetString("target_username");
                        string messageUsername = reader.GetString("username");
                        string content = reader.GetString("content");
                        bool deleted = reader.GetBoolean("deleted");
                        bool read = reader.GetBoolean("is_read");
                        string publishedAt = reader.GetString("published_at");
                        //string? profileIcon = reader.IsDBNull(reader.GetOrdinal("profile_icon")) ? null : reader.GetString("profile_icon");
                        ushort type = reader.GetUInt16("type");
                        var message = new MessagePublishedModel(messageId, type, deleted ? "" : content, messageUsername, messageUUID, targetUsername, deleted, read, publishedAt);
                        response.Add(message);
                    }
                    reader.Close();
                }
                var unreadMessages = response.Where(x => !x.Read);
                if (unreadMessages.Any())
                {
                    var mySqlCommandUpdate = new MySql.Data.MySqlClient.MySqlCommand();
                    mySqlCommandUpdate.CommandText = "UPDATE user_message SET is_read = 1 WHERE message_id IN (" + string.Join(",", unreadMessages.Select(_ => "?")) + ")";
                    mySqlCommandUpdate.Parameters.AddRange(unreadMessages.Select(x => new MySqlParameter("", x.MessageId)).ToArray());
                    mySqlCommandUpdate.Connection = connectionStory;
                    await mySqlCommandUpdate.ExecuteNonQueryAsync();
                }
                return response;
            }
            finally
            {
                await connectionStory.CloseAsync();
            }
        }
        else
        {
            _logger.LogError("Attemted to fetch messages with wrong username:" + username + ", returning empty result!");
            throw new UnauthorizedAccessException("Unauthorized!");
        }
    }

    [Authorize]
    [HttpPost()]
    public async Task<StatusCodeResult> SendMessage([FromBody] MessageModel messageModel)
    {
        var role = (messageModel.AuthorName == null) ? null : ConfigUtil.VerifyUserNameFromClaimAndGetRole(messageModel.AuthorName, HttpContext.User.Identity as ClaimsIdentity);
        if (role != null)
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();
            try
            {
                var mySqlCommand3 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand3.CommandText = "INSERT INTO user_message (message_uuid, target_username, username, content, type)" +
                " VALUES (UUID_TO_BIN(@message_uuid), @target_username,@username, @content, @type); SELECT LAST_INSERT_ID()";
                mySqlCommand3.Parameters.AddWithValue("@message_uuid", messageModel.MessageUUID);
                mySqlCommand3.Parameters.AddWithValue("@target_username", messageModel.TargetUsername);
                mySqlCommand3.Parameters.AddWithValue("@username", messageModel.AuthorName);
                mySqlCommand3.Parameters.AddWithValue("@content", messageModel.Content);
                mySqlCommand3.Parameters.AddWithValue("@type", messageModel.MessageType);
                mySqlCommand3.Connection = connection;
                var messageId = await mySqlCommand3.ExecuteScalarAsync();
                if (messageId != null)
                {
                    var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
                    mySqlCommand5.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type) SELECT @username, @additional_data, @type WHERE NOT EXISTS (select 1 FROM user_contact_approved WHERE username = @targetUsername AND target_username = @username AND blocked = 1) AND NOT EXISTS (select 1 FROM user_notification WHERE username = @targetUsername AND notification_read = 0 AND type = 8 AND additional_data LIKE @usernameLike)";
                    mySqlCommand5.Parameters.AddWithValue("@username", messageModel.AuthorName);
                    mySqlCommand5.Parameters.AddWithValue("@targetUsername", messageModel.TargetUsername);
                    mySqlCommand5.Parameters.AddWithValue("@usernameLike", messageModel.AuthorName + "%");
                    mySqlCommand5.Parameters.AddWithValue("@additional_data", messageModel.TargetUsername + ":" + messageId.ToString());
                    mySqlCommand5.Parameters.AddWithValue("@type", (int)EventTriggerType.NEW_MESSAGE_NOTIFIATION);
                    mySqlCommand5.Connection = connection;
                    await mySqlCommand5.ExecuteNonQueryAsync();
                    return StatusCode(200);
                }
                else return StatusCode(400);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        else
        {
            _logger.LogError("Could not verify author for message {0} posted by {1}", messageModel.MessageUUID, messageModel.AuthorName);
            return StatusCode(400);
        }
    }

    [Authorize]
    [HttpDelete("{messageUUID}")]
    public async Task<StatusCodeResult> DeleteMessage(string messageUUID)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {

            _logger.LogError("Attemted to fetch messages with wrong username:" + username);
            throw new UnauthorizedAccessException("Unauthorized!");

        }
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            await connectionStory.OpenAsync();
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "UPDATE user_message SET deleted = 1  WHERE username = @username AND message_uuid = UUID_TO_BIN(@message_uuid)  ";
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@message_uuid", messageUUID);
            mySqlCommand.Connection = connectionStory;
            await mySqlCommand.ExecuteNonQueryAsync();
            return StatusCode(200);
        }
        finally
        {
            await connectionStory.CloseAsync();
        }
    }
}