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
using Microsoft.AspNetCore.Cors;

namespace apiplatform.Controllers;


[ApiController]
[EnableCors("AllowStoryPopCORS")]
[Route("[controller]")]
public class WaitingListController : ControllerBase
{
    private readonly ILogger<WaitingListController> _logger;

    private readonly ServerSettings _serverSettings;


    public WaitingListController(ILogger<WaitingListController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }

    [HttpPost]
    public StatusCodeResult SignUp()
    {
        if (Request.Form["email"].Count == 0)
        {
            return StatusCode(401);
        }
        var isAndroidSignUp = Request.Form["option"].FirstOrDefault() != "1";
        var email = Request.Form["email"].First();
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "SELECT COUNT(*) from " + (isAndroidSignUp ? "user_invite_android" : "user_invite_ios") + " WHERE email = @email";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Parameters.AddWithValue("@email", email);
            if ((long)mySqlCommandCheck.ExecuteScalar() > 0)
            {
                return StatusCode(409);
            }
            var inviteCode = TokenController.GenerateNonce(12).ToLower();
            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.CommandText = "INSERT INTO " + (isAndroidSignUp ? "user_invite_android" : "user_invite_ios") + " ( email, ip_address, invite_code) VALUES (@email,INET6_ATON(@ip_address), @invite_code)";
            mySqlCommandInsert.Connection = connection;
            mySqlCommandInsert.Parameters.AddWithValue("@invite_code", inviteCode);
            mySqlCommandInsert.Parameters.AddWithValue("@email", email);
            mySqlCommandInsert.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString());
            if (mySqlCommandInsert.ExecuteNonQuery() > 0)
            {
                Console.WriteLine("Created tmpToken:" + inviteCode);
                return StatusCode(200);
            }
            else return StatusCode(409);
        }
        finally
        {
            connection.Close();
        }

    }

    [HttpPost("invite/{inviteToken}")]
    public StatusCodeResult InviteSignUp(string inviteToken)
    {
        if (Request.Form["email"].Count == 0)//|| Request.Form["frc-captcha-solution"].Count == 0
        {
            return StatusCode(401);
        }
        var email = Request.Form["email"].First();

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "SELECT COUNT(*) from user_invite WHERE email = @email";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Parameters.AddWithValue("@email", email);
            if ((long)mySqlCommandCheck.ExecuteScalar() > 0)
            {
                return StatusCode(409);
            }
            var myTrans = connection.BeginTransaction();
            var sourceUsernameBytes = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Decode(inviteToken);
            var sourceUsername = Encoding.UTF8.GetString(sourceUsernameBytes);
            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.CommandText = "INSERT INTO user_invite (email, ip_address, username_source) VALUES (@email,INET6_ATON(@ip_address), @sourceUsername)";
            mySqlCommandInsert.Connection = connection;
            mySqlCommandInsert.Transaction = myTrans;
            mySqlCommandInsert.Parameters.AddWithValue("@email", email);
            mySqlCommandInsert.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString());
            mySqlCommandInsert.Parameters.AddWithValue("@sourceUsername", sourceUsername);
            if (mySqlCommandInsert.ExecuteNonQuery() > 0)
            {
                var mySqlCommandInsertEmail = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandInsertEmail.CommandText = "INSERT INTO mail_queue (email, type, additional_data) VALUES (@email, @type, @additional_data)";
                mySqlCommandInsertEmail.Connection = connection;
                mySqlCommandInsertEmail.Transaction = myTrans;
                mySqlCommandInsertEmail.Parameters.AddWithValue("@email", email);
                mySqlCommandInsertEmail.Parameters.AddWithValue("@type", (int)EmailType.INVITE);
                mySqlCommandInsertEmail.Parameters.AddWithValue("@additional_data", sourceUsername);
                mySqlCommandInsertEmail.ExecuteNonQuery();
                myTrans.Commit();
                return StatusCode(200);
            }
            else return StatusCode(409);
        }
        finally
        {
            connection.Close();
        }
    }

    [HttpPost("cleardatarequest")]
    public StatusCodeResult ClearDataRequest()
    {
        if (Request.Form["email"].Count == 0)
        {
            return StatusCode(401);
        }
        var email = Request.Form["email"].First();

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "SELECT COUNT(*) from users WHERE email = @email";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Parameters.AddWithValue("@email", email);
            if ((long)mySqlCommandCheck.ExecuteScalar() == 0)
            {
                return StatusCode(200);
            }
            var mySqlCommandInsertEmail = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsertEmail.CommandText = "INSERT INTO mail_queue (email, type) VALUES (@email, @type)";
            mySqlCommandInsertEmail.Connection = connection;
            mySqlCommandInsertEmail.Parameters.AddWithValue("@email", email);
            mySqlCommandInsertEmail.Parameters.AddWithValue("@type", (int)EmailType.CLEAR_DATA_REQUEST);
            if (mySqlCommandInsertEmail.ExecuteNonQuery() > 0)
            {

                return StatusCode(200);
            }
            else return StatusCode(409);
        }
        finally
        {
            connection.Close();
        }
    }
}