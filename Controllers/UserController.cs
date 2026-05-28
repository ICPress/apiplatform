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
public class UserController : ControllerBase
{
    private readonly ILogger<UserController> _logger;


    private readonly ServerSettings _serverSettings;

    public UserController(ILogger<UserController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [Authorize]
    [HttpDelete("{followingUsername}/follow")]
    public async Task<StatusCodeResult> UnfollowUser(string followingUsername)
    {
        (string? username, string? role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null || followingUsername == null)
        {
            return StatusCode(400);
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        try
        {
            // 1. Delete Follow Relationship
            using var cmdDelete = new MySqlCommand(
                "DELETE FROM user_following WHERE username = @username AND following = @following",
                connection);
            cmdDelete.Parameters.AddWithValue("@username", username);
            cmdDelete.Parameters.AddWithValue("@following", followingUsername);

            await cmdDelete.ExecuteNonQueryAsync();

            // 2. Queue Event (Fire and forget style within the try/catch)
            try
            {
                using var cmdEvent = new MySqlCommand(
                    "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)",
                    connection);
                cmdEvent.Parameters.AddWithValue("@username", username);
                cmdEvent.Parameters.AddWithValue("@additional_data", followingUsername);
                cmdEvent.Parameters.AddWithValue("@type", (int)EventTriggerType.UNFOLLOW_USER);

                await cmdEvent.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue unfollow event for {Username}", username);
            }

            return StatusCode(201);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unfollowing user {Following} for {Username}", followingUsername, username);
            return StatusCode(500);
        }
    }



    [Authorize]
    [HttpPost("{followingUsername}/follow")]
    public async Task<StatusCodeResult> FollowUser(string followingUsername)
    {
        (string? username, string? role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null || followingUsername == null)
        {
            return StatusCode(400);
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        try
        {
            // 1. Insert Follow Relationship
            using var cmdInsert = new MySqlCommand(
                "INSERT IGNORE INTO user_following (username, following) VALUES (@username, @following)",
                connection);
            cmdInsert.Parameters.AddWithValue("@username", username);
            cmdInsert.Parameters.AddWithValue("@following", followingUsername);

            // If a new row was actually inserted (not ignored), queue the event
            if (await cmdInsert.ExecuteNonQueryAsync() > 0)
            {
                try
                {
                    // 2. Queue Event
                    using var cmdEvent = new MySqlCommand(
                        "INSERT INTO events_queued (trigger_source_username, additional_data, type) VALUES(@username, @additional_data, @type)",
                        connection);
                    cmdEvent.Parameters.AddWithValue("@username", username);
                    cmdEvent.Parameters.AddWithValue("@additional_data", followingUsername);
                    cmdEvent.Parameters.AddWithValue("@type", (int)EventTriggerType.FOLLOW_USER);

                    await cmdEvent.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to queue follow event for {Username}", username);
                }
            }

            return StatusCode(201);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error following user {Following} for {Username}", followingUsername, username);
            return StatusCode(500);
        }
    }


    [Authorize]
    [HttpPost("profile")]
    public async Task<StatusCodeResult> UpdateProfileInfo([FromBody] UpdateProfileInfo profileInfo)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {Username} and role is {Role}", username, role);
            return StatusCode(409);
        }

        string? profileBadgeJson = null;
        if (profileInfo.ProfileBadgeImageInfo != null && profileInfo.ProfileBadgeImageInfo.Height != 0 &&
            profileInfo.ProfileBadgeImageInfo.Width != 0 && !string.IsNullOrEmpty(profileInfo.ProfileBadgeImageInfo.Name))
        {
            profileBadgeJson = JsonSerializer.Serialize(profileInfo.ProfileBadgeImageInfo);
        }

        string? profileBackgroundJson = null;
        if (profileInfo.ProfileBackgroundImageInfo != null && profileInfo.ProfileBackgroundImageInfo.Height != 0 &&
            profileInfo.ProfileBackgroundImageInfo.Width != 0 && !string.IsNullOrEmpty(profileInfo.ProfileBackgroundImageInfo.Name))
        {
            profileBackgroundJson = JsonSerializer.Serialize(profileInfo.ProfileBackgroundImageInfo);
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        try
        {
            using var mySqlCommand = new MySqlCommand(
                "UPDATE users SET profile_icon = @badgeJsonData, profile_background_image = @backgroundJsonData, profile_text = @profileDescription WHERE username = @username",
                connection);

            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@badgeJsonData", (object?)profileBadgeJson ?? DBNull.Value);
            mySqlCommand.Parameters.AddWithValue("@backgroundJsonData", (object?)profileBackgroundJson ?? DBNull.Value);
            mySqlCommand.Parameters.AddWithValue("@profileDescription", (object?)profileInfo.ProfileDescription ?? DBNull.Value);

            if (await mySqlCommand.ExecuteNonQueryAsync() > 0)
            {
                return StatusCode(200);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for user {Username}", username);
            return StatusCode(500);
        }

        _logger.LogError("Update failed or dimensions for profile images were not valid for user {Username}", username);
        return StatusCode(409);
    }

    [Authorize]
    [HttpPost("updateBadge")]
    public async Task<StatusCodeResult> UpdateBadge([FromBody] ImageInfoMetadata imageInfoMetadata)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {Username} and role is {Role}", username, role);
            return StatusCode(401);
        }

        if (imageInfoMetadata == null || imageInfoMetadata.Height == 0 || imageInfoMetadata.Width == 0 || string.IsNullOrEmpty(imageInfoMetadata.Name))
        {
            _logger.LogError("The dimensions for profileBadge are not valid!");
            return StatusCode(409);
        }

        var jsonData = JsonSerializer.Serialize(imageInfoMetadata);

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        try
        {
            using var mySqlCommand = new MySqlCommand("UPDATE users SET profile_icon = @jsonData WHERE username = @username", connection);
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@jsonData", jsonData);

            if (await mySqlCommand.ExecuteNonQueryAsync() > 0)
            {
                return StatusCode(200);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating badge for user {Username}", username);
            return StatusCode(500);
        }

        return StatusCode(400);
    }


    [Authorize]
    [HttpGet("full/{targetUsername}")]
    public async Task<ActionResult<ProfileInfo?>> ProfileFull(string targetUsername)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username == null || role == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {Username} and role is {Role}", username, role);
            return null;
        }

        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        using var mySqlCommand = new MySqlCommand(
            "SELECT profile_icon, profile_background_image, profile_text, " +
            "COALESCE((SELECT blocked FROM user_contact_approved WHERE username = @username AND target_username = @targetUsername AND blocked = 1), 0) as contact_blocked, " +
            "DATE_FORMAT(created_at, '%Y-%m-%dT%TZ') as created_at FROM users WHERE username = @targetUsername",
            connection);

        mySqlCommand.Parameters.AddWithValue("@username", username);
        mySqlCommand.Parameters.AddWithValue("@targetUsername", targetUsername);

        using var reader = await mySqlCommand.ExecuteReaderAsync();

        int iconOrd = reader.GetOrdinal("profile_icon");
        int bgOrd = reader.GetOrdinal("profile_background_image");
        int textOrd = reader.GetOrdinal("profile_text");
        int createdOrd = reader.GetOrdinal("created_at");
        int blockedOrd = reader.GetOrdinal("contact_blocked");

        if (await reader.ReadAsync())
        {
            var accountInfo = new ProfileInfo(targetUsername);
            accountInfo.ProfileIcon = await reader.IsDBNullAsync(iconOrd) ? null : reader.GetString(iconOrd);
            accountInfo.ProfileBackgroundImage = await reader.IsDBNullAsync(bgOrd) ? null : reader.GetString(bgOrd);
            accountInfo.ProfileText = await reader.IsDBNullAsync(textOrd) ? null : reader.GetString(textOrd);
            accountInfo.MemberSince = await reader.IsDBNullAsync(createdOrd) ? null : reader.GetString(createdOrd);

            // Note: These helper methods (Followers/Numberofposts) should also be made async for best performance
            accountInfo.FollowerSpan = await Followers(targetUsername);
            accountInfo.ArticlesPublished = await Numberofposts(targetUsername);
            accountInfo.ContactBlocked = reader.GetBoolean(blockedOrd);

            return accountInfo;
        }

        _logger.LogError("Account info is empty for username: {TargetUsername}", targetUsername);
        return null;
    }

    [HttpGet("{username}")]
    public async Task<ActionResult<ProfileInfo?>> Profile(string username)
    {
        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        using var mySqlCommand = new MySqlCommand(
            "SELECT profile_icon, profile_background_image, profile_text, DATE_FORMAT(created_at, '%Y-%m-%dT%TZ') as created_at FROM users WHERE username = @username",
            connection);

        mySqlCommand.Parameters.AddWithValue("@username", username);

        using var reader = await mySqlCommand.ExecuteReaderAsync();

        int iconOrd = reader.GetOrdinal("profile_icon");
        int bgOrd = reader.GetOrdinal("profile_background_image");
        int textOrd = reader.GetOrdinal("profile_text");
        int createdOrd = reader.GetOrdinal("created_at");

        if (await reader.ReadAsync())
        {
            var accountInfo = new ProfileInfo(username);
            accountInfo.ProfileIcon = await reader.IsDBNullAsync(iconOrd) ? null : reader.GetString(iconOrd);
            accountInfo.ProfileBackgroundImage = await reader.IsDBNullAsync(bgOrd) ? null : reader.GetString(bgOrd);
            accountInfo.ProfileText = await reader.IsDBNullAsync(textOrd) ? null : reader.GetString(textOrd);
            accountInfo.MemberSince = await reader.IsDBNullAsync(createdOrd) ? null : reader.GetString(createdOrd);

            accountInfo.FollowerSpan = await Followers(username);
            accountInfo.ArticlesPublished =await Numberofposts(username);

            return accountInfo;
        }

        _logger.LogError("Account info is empty for username: {Username}", username);
        return null;
    }

    [HttpGet("followers/{username}")]
    public async Task<string?> Followers(string username)
    {
        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        using var mySqlCommandCheck = new MySqlCommand("SELECT COUNT(*) AS CNT FROM user_following WHERE following = @username", connection);
        mySqlCommandCheck.Parameters.AddWithValue("@username", username);

        var result = await mySqlCommandCheck.ExecuteScalarAsync();
        long following = Convert.ToInt64(result);

        if (following <= 0) return null;
        if (following <= 10) return "1-10";
        if (following <= 50) return "10-50";
        if (following <= 100) return "50-100";
        if (following <= 150) return "100";
        if (following <= 250) return "200";
        if (following <= 350) return "300";
        if (following <= 750) return "500";
        if (following <= 1500) return "1K";
        if (following <= 2500) return "2K";
        if (following <= 3500) return "3K";
        if (following <= 4500) return "4K";
        if (following <= 5500) return "5K";
        if (following <= 6500) return "6K";
        return ">6K";
    }

    [HttpGet("{username}/posts")]
    public async Task<long> Numberofposts(string username)
    {
        using var connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connection.OpenAsync();

        using var mySqlCommandCheck = new MySqlCommand("SELECT COUNT(*) AS CNT from user_stories WHERE username = @username", connection);
        mySqlCommandCheck.Parameters.AddWithValue("@username", username);

        var result = await mySqlCommandCheck.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    [Authorize]
    [HttpGet("search/{searchvalue}")]
    public async Task<ActionResult<List<UsernameSearchResult>>> FindUsername(string searchvalue)
    {
        var foundUsers = new List<UsernameSearchResult>(5);
        if (string.IsNullOrEmpty(searchvalue) || searchvalue.Length <= 3) return foundUsers;

        using var connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();

        using var mySqlCommand = new MySqlCommand("SELECT username, profile_icon FROM users WHERE username LIKE @search LIMIT 5;", connectionStory);
        mySqlCommand.Parameters.AddWithValue("@search", searchvalue.Replace("%", "") + "%");

        using var reader = await mySqlCommand.ExecuteReaderAsync();

        int userOrd = reader.GetOrdinal("username");
        int iconOrd = reader.GetOrdinal("profile_icon");

        while (await reader.ReadAsync())
        {
            string currentUsername = reader.GetString(userOrd);
            string? currentProfileIcon = await reader.IsDBNullAsync(iconOrd) ? null : reader.GetString(iconOrd);
            foundUsers.Add(new UsernameSearchResult(currentUsername, currentProfileIcon));
        }

        return foundUsers;
    }


}