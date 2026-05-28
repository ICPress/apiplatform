using Microsoft.AspNetCore.Mvc;
using Apache.Ignite.Core.Cache.Expiry;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Client;
using JWT.Builder;
using JWT.Algorithms;
using JWT.Exceptions;
using System.Text.Json;
using Slugify;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using MySql.Data.MySqlClient;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Newtonsoft.Json;
using System.Data;

namespace apiplatform.Controllers;


[ApiController]
[Route("[controller]")]
public class ContactController : ControllerBase
{
    private readonly ILogger<ContactController> _logger;

    private readonly ServerSettings _serverSettings;


    public ContactController(ILogger<ContactController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [Authorize]
    [HttpPost("approve/{targetUsername}")]
    public async Task<StatusCodeResult> Approve(string targetUsername)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username != null && role != null)
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();
            try
            {
                var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand5.CommandText = "DELETE FROM user_contact_approved WHERE username = @username AND target_username = @targetUsername; INSERT INTO user_contact_approved (username,target_username,blocked) VALUES (@username, @targetUsername,0)";
                mySqlCommand5.Parameters.AddWithValue("@username", username);
                mySqlCommand5.Parameters.AddWithValue("@targetUsername", targetUsername);
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
        return StatusCode(500);
    }

    [Authorize]
    [HttpPost("block/{targetUsername}")]
    public async Task<StatusCodeResult> Block(string targetUsername)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (username != null && role != null)
        {
            using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
            await connection.OpenAsync();
            try
            {
                var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand5.CommandText = "DELETE FROM user_contact_approved WHERE username = @username AND target_username = @targetUsername; INSERT INTO user_contact_approved (username,target_username,approved) VALUES (@username, @targetUsername,0)";
                mySqlCommand5.Parameters.AddWithValue("@username", username);
                mySqlCommand5.Parameters.AddWithValue("@targetUsername", targetUsername);
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
        return StatusCode(500);
    }
}