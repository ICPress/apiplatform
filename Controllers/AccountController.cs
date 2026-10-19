using Microsoft.AspNetCore.Mvc;
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
public class AccountController : ControllerBase
{
    private readonly ILogger<ArticleController> _logger;

    private readonly ServerSettings _serverSettings;


    public AccountController(ILogger<ArticleController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [Authorize]
    [HttpGet("Test")]
    public string Test(string userName)
    {
        var identityRole = ConfigUtil.VerifyUserNameFromClaimAndGetRole(userName, HttpContext.User.Identity as ClaimsIdentity);
        if (identityRole != null)
        {
            return identityRole;
        }
        else return "CLAIM VALUE MISSING!";
    }

    [HttpPost("getSignInLink/{email}")]
    public async Task<IActionResult> GetSigninLink(string email)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || email.Length > 255) return BadRequest();
        using var connection = new MySqlConnection(_serverSettings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        // Serialize link issuance for an account across API instances.
        using var userCommand = new MySqlCommand("SELECT username FROM users WHERE email = @email FOR UPDATE", connection, transaction);
        userCommand.Parameters.AddWithValue("@email", email.Trim());
        var username = (string?)await userCommand.ExecuteScalarAsync();
        if (username == null) return StatusCode(201); // Avoid email enumeration.
        using var recent = new MySqlCommand("SELECT COUNT(*) FROM sent_links_users WHERE username = @username AND email = @email AND verified_at IS NULL AND expires_at > UTC_TIMESTAMP() AND created_at > UTC_TIMESTAMP() - INTERVAL 30 MINUTE", connection, transaction);
        recent.Parameters.AddWithValue("@username", username);
        recent.Parameters.AddWithValue("@email", email.Trim());
        if (Convert.ToInt64(await recent.ExecuteScalarAsync()) > 0) return StatusCode(201);
        var token = ConfigUtil.GetNewToken(_serverSettings, ConfigUtil.TEMP_TOKEN_PREFIX + username,
            DateTime.UtcNow.AddMinutes(ConfigUtil.SIGN_IN_TOKEN_EXPIRATION_MINUTES), ConfigUtil.JWT_DEFAULT_ROLE);
        await EmailTokenStore.InsertAsync(connection, transaction, username, token, HttpContext.Connection.RemoteIpAddress?.ToString());
        using var mail = new MySqlCommand("INSERT INTO mail_queue (email, type, additional_data) VALUES (@email, @type, @token)", connection, transaction);
        mail.Parameters.AddWithValue("@email", email.Trim());
        mail.Parameters.AddWithValue("@type", (int)EmailType.RECOVER_SIGN_ON);
        mail.Parameters.AddWithValue("@token", token);
        await mail.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return StatusCode(201);
    }

    [HttpPost("verify/{authToken}")]
    public async Task<IActionResult> VerifyEmail(string authToken)
    {
        IDictionary<string, object> claims;
        try
        {
            claims = JwtBuilder.Create().WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(_serverSettings.JWTSecret).MustVerifySignature()
                .Decode<IDictionary<string, object>>(authToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Unauthorized("The sign-in link is invalid or expired.");
        }
        var subject = (claims.TryGetValue(ConfigUtil.JWT_USERNAME_KEY, out var subjectClaim) ? subjectClaim?.ToString() : null);
        var role = (claims.TryGetValue(ConfigUtil.JWT_ROLE_KEY, out var roleClaim) ? roleClaim?.ToString() : null);
        if (role != ConfigUtil.JWT_DEFAULT_ROLE || subject == null ||
            !subject.StartsWith(ConfigUtil.TEMP_TOKEN_PREFIX, StringComparison.Ordinal)) return Unauthorized();
        var username = subject[ConfigUtil.TEMP_TOKEN_PREFIX.Length..];
        using var connection = new MySqlConnection(_serverSettings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        using var consume = new MySqlCommand(@"UPDATE sent_links_users SET verified_at = UTC_TIMESTAMP()
            WHERE username = @username AND token_hash = @hash AND verified_at IS NULL AND expires_at > UTC_TIMESTAMP()", connection, transaction);
        consume.Parameters.AddWithValue("@username", username);
        consume.Parameters.AddWithValue("@hash", EmailTokenStore.Hash(authToken));
        if (await consume.ExecuteNonQueryAsync() != 1) return Unauthorized("The sign-in link is invalid, expired or already used.");
        using var verify = new MySqlCommand(@"UPDATE users SET verified_at = COALESCE(verified_at, UTC_TIMESTAMP()) WHERE username = @username", connection, transaction);
        verify.Parameters.AddWithValue("@username", username);
        await verify.ExecuteNonQueryAsync();
        using var exists = new MySqlCommand("SELECT COUNT(*) FROM users WHERE username = @username", connection, transaction);
        exists.Parameters.AddWithValue("@username", username);
        if (Convert.ToInt64(await exists.ExecuteScalarAsync()) != 1) return Unauthorized();
        await transaction.CommitAsync();
        // Preserve the original Android response: a raw JWT string.
        return Content(ConfigUtil.GetNewToken(_serverSettings, username), "text/plain");
    }

    [HttpGet("exists/{username}")]
    public StatusCodeResult UsernameExists(string username)
    {
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "SELECT COUNT(*) from users WHERE username = @username";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Parameters.AddWithValue("@username", username);
            if ((long)mySqlCommandCheck.ExecuteScalar() > 0)
            {
                return StatusCode(409);
            }
        }
        finally
        {
            connection.Close();
        }
        return StatusCode(200);
    }


    [HttpGet("existsEmail/{email}")]
    public StatusCodeResult EmailExists(string email)
    {
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "SELECT COUNT(*) from users WHERE email = @email";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Parameters.AddWithValue("@email", email);
            if ((long)mySqlCommandCheck.ExecuteScalar() > 0)
            {
                return StatusCode(409);
            }
        }
        finally
        {
            connection.Close();
        }
        return StatusCode(200); ;
    }

    [Authorize]
    [HttpPost("signIn")]
    public async Task<IActionResult> SignInUpdateToken()
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {0} and role is {1}", username, role);
            return BadRequest();
        }
        string? newTokenDataStr = null;
        using (var sr = new System.IO.StreamReader(Request.Body))
        {
            newTokenDataStr = await sr.ReadToEndAsync();
        }
        NewTokenData? newTokenData = null;
        if (newTokenDataStr != null)
        {
            newTokenData = JsonConvert.DeserializeObject<NewTokenData>(newTokenDataStr);
        }
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT BIN_TO_UUID(device_uuid) as device_uuid, email, full_name, username, profile_icon, profile_background_image, profile_text, DATE_FORMAT(following_latest_check_at, '%Y-%m-%dT%TZ') as following_latest_check_at,  wallet_address from users WHERE username = @username";
            mySqlCommand.Connection = connection;
            mySqlCommand.Parameters.AddWithValue("@username", username);
            using var reader = mySqlCommand.ExecuteReader();
            AccountInfoFull? accountInfo = null;
            while (reader.Read())
            {
                string? profileIcon = reader.IsDBNull(reader.GetOrdinal("profile_icon")) ? null : reader.GetString("profile_icon");
                string? profileBackgroundImage = reader.IsDBNull(reader.GetOrdinal("profile_background_image")) ? null : reader.GetString("profile_background_image");
                string email = reader.GetString("email");
                string device_uuid = reader.IsDBNull(reader.GetOrdinal("device_uuid")) ? "" : reader.GetString("device_uuid");
                string refreshToken = ConfigUtil.GetNewToken(_serverSettings, username, DateTime.Now.AddDays(30));
                string? profile_text = reader.IsDBNull(reader.GetOrdinal("profile_text")) ? null : reader.GetString("profile_text");
                string followingLatestCheck = reader.GetString("following_latest_check_at");
                string? walletAddress = reader.IsDBNull(reader.GetOrdinal("wallet_address")) ? null : reader.GetString("wallet_address");
                accountInfo = new AccountInfoFull(refreshToken, profileIcon, profileBackgroundImage, profile_text, email, device_uuid,
                 username, followingLatestCheck, walletAddress,
                 _serverSettings.SwiftTempAuthUser != "", _serverSettings.CDNPublishLargePath, _serverSettings.CDNPublishSmallPath, 
                 _serverSettings.CDNPublishUserMessagePath, _serverSettings.CDNRequestLargePath,_serverSettings.CDNRequestSmallPath,
                  _serverSettings.CDNRequestUserMessagePath, _serverSettings.ImageStaticPath, _serverSettings.RequireArticleSources, _serverSettings.RequireArticleReview);
                accountInfo.FullName = reader.IsDBNull(reader.GetOrdinal("full_name")) ? null : reader.GetString("full_name");
            }
            reader.Close();
            if (accountInfo == null)
            {
                _logger.LogError("Account info is empty for username:" + username);
                return Unauthorized("unauthorized user:" + username);
            }
            if (newTokenData?.Token != null && newTokenData?.DeviceUUID != null)
            {
                InsertFCMToken(connection, username, newTokenData.DeviceUUID, newTokenData.Token);
            }
            var mySqlCommand2 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand2.CommandText = "SELECT COALESCE(SUM(amount),0) as balance from user_transactions WHERE available_from < CURRENT_TIMESTAMP and username = @username ";
            mySqlCommand2.Connection = connection;
            mySqlCommand2.Parameters.AddWithValue("@username", username);
            decimal accountBalance = (decimal)mySqlCommand2.ExecuteScalar();
            accountInfo.AccountBalance = (int)accountBalance;
            var mySqlCommand3 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand3.CommandText = "SELECT COUNT(*) from user_notification WHERE available_from < CURRENT_TIMESTAMP and username = @username and notification_read = 0";
            mySqlCommand3.Connection = connection;
            mySqlCommand3.Parameters.AddWithValue("@username", username);
            long unreadNotificationCount = (long)mySqlCommand3.ExecuteScalar();
            accountInfo.UnreadNotifications = unreadNotificationCount;
            var mySqlCommand4 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand4.CommandText = "SELECT COUNT(*) FROM user_stories WHERE username IN (SELECT following FROM user_following WHERE username = @username) and published_at > @from_date";
            mySqlCommand4.Connection = connection;
            mySqlCommand4.Parameters.AddWithValue("@username", username);
            mySqlCommand4.Parameters.AddWithValue("@from_date", accountInfo.FollowingLatestCheck);
            long unreadFollowedStoriesCount = (long)mySqlCommand4.ExecuteScalar();
            accountInfo.UnreadFollowedStories = unreadFollowedStoriesCount;
            return Ok(accountInfo);
        }
        finally
        {
            connection.Close();
        }


    }

    [Authorize]
    [HttpDelete]
    public async Task<StatusCodeResult> DeleteAccount()
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {0} and role is {1}", username, role);
            return StatusCode(400);
        }
        using var httpClient = new HttpClient();
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();

            var mySqlCommandDeleteLinks = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandDeleteLinks.CommandText = "DELETE FROM sent_links_users WHERE username = @username OR email IN (SELECT email FROM users WHERE username = @username)";
            mySqlCommandDeleteLinks.Connection = connection;
            mySqlCommandDeleteLinks.Parameters.AddWithValue("@username", username);
            await mySqlCommandDeleteLinks.ExecuteNonQueryAsync();

            var mySqlCommandDeleteFCM = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandDeleteFCM.CommandText = "DELETE FROM user_fcm_token WHERE username = @username";
            mySqlCommandDeleteFCM.Connection = connection;
            mySqlCommandDeleteFCM.Parameters.AddWithValue("@username", username);
            await mySqlCommandDeleteFCM.ExecuteNonQueryAsync();

            var mySqlCommandFollowing = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandFollowing.CommandText = "DELETE FROM user_following WHERE following = @username";
            mySqlCommandFollowing.Connection = connection;
            mySqlCommandFollowing.Parameters.AddWithValue("@username", username);
            await mySqlCommandFollowing.ExecuteNonQueryAsync();


            var mySqlCommandFollows = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandFollows.CommandText = "DELETE FROM user_following WHERE username = @username";
            mySqlCommandFollows.Connection = connection;
            mySqlCommandFollows.Parameters.AddWithValue("@username", username);
            await mySqlCommandFollows.ExecuteNonQueryAsync();

            var mySqlCommandInvite = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInvite.CommandText = "DELETE FROM user_invite WHERE username_source = @username";
            mySqlCommandInvite.Connection = connection;
            mySqlCommandInvite.Parameters.AddWithValue("@username", username);
            await mySqlCommandInvite.ExecuteNonQueryAsync();


            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT slug_title FROM user_stories WHERE username = @username";
            mySqlCommand.Connection = connection;
            mySqlCommand.Parameters.AddWithValue("@username", username);
            List<string> slugTitleToDelete = new List<string>();
            using (var reader = mySqlCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    slugTitleToDelete.Add(reader.GetString("slug_title"));
                }
                reader.Close();
            }
            foreach (var toDelete in slugTitleToDelete)
            {
                var mySqlCommandStoryLogDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandStoryLogDelete.CommandText = "DELETE FROM user_story_log WHERE slug_title = @slug_title";
                mySqlCommandStoryLogDelete.Connection = connection;
                mySqlCommandStoryLogDelete.Parameters.AddWithValue("@slug_title", toDelete);
                await mySqlCommandStoryLogDelete.ExecuteNonQueryAsync();
                var deleteRespone = await httpClient.DeleteAsync(_serverSettings.GorseAPIEndpoint + "item/" + toDelete);
                if (!deleteRespone.IsSuccessStatusCode)
                {
                    _logger.LogError("Error occured when deleting post '{0}' in GORSE for user {1}, statusCode: {2}, response:" + deleteRespone.Content.ReadAsStringAsync().Result, toDelete, username, deleteRespone.StatusCode);
                    return StatusCode(500);
                }

            }
            var mySqlCommandStoryDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandStoryDelete.CommandText = "DELETE FROM user_stories WHERE username = @username";
            mySqlCommandStoryDelete.Connection = connection;
            mySqlCommandStoryDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandStoryDelete.ExecuteNonQueryAsync();

            var mySqlCommandTransactionsDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandTransactionsDelete.CommandText = "DELETE FROM user_transactions WHERE username = @username";
            mySqlCommandTransactionsDelete.Connection = connection;
            mySqlCommandTransactionsDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandTransactionsDelete.ExecuteNonQueryAsync();

            var mySqlCommandTransferRequestDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandTransferRequestDelete.CommandText = "DELETE FROM claim_transfer_request WHERE transfer_request_id IN (SELECT transfer_request_id FROM user_claimed_rewards WHERE username =@username and transfer_request_id is not null and transfered_at is NULL )";
            mySqlCommandTransferRequestDelete.Connection = connection;
            mySqlCommandTransferRequestDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandTransferRequestDelete.ExecuteNonQueryAsync();

            var mySqlCommandRewardsDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandRewardsDelete.CommandText = "DELETE FROM user_claimed_rewards WHERE username = @username";
            mySqlCommandRewardsDelete.Connection = connection;
            mySqlCommandRewardsDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandRewardsDelete.ExecuteNonQueryAsync();

            var mySqlCommandNotificationsDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandNotificationsDelete.CommandText = "DELETE from user_notification WHERE username = @username ";
            mySqlCommandNotificationsDelete.Connection = connection;
            mySqlCommandNotificationsDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandNotificationsDelete.ExecuteNonQueryAsync();

            var mySqlCommandWalletDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandWalletDelete.CommandText = "DELETE from user_wallet_history WHERE username = @username ";
            mySqlCommandWalletDelete.Connection = connection;
            mySqlCommandWalletDelete.Parameters.AddWithValue("@username", username);
            await mySqlCommandWalletDelete.ExecuteNonQueryAsync();

            var mySqlCommandDeleteMessages = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandDeleteMessages.CommandText = "DELETE FROM user_message WHERE username = @username OR target_username = @username";
            mySqlCommandDeleteMessages.Connection = connection;
            mySqlCommandDeleteMessages.Parameters.AddWithValue("@username", username);
            await mySqlCommandDeleteMessages.ExecuteNonQueryAsync();

            foreach (var cleanup in new[] {
                "DELETE FROM user_contact_approved WHERE username=@username OR target_username=@username",
                "DELETE FROM mail_queue WHERE email IN (SELECT email FROM users WHERE username=@username)" })
            {
                using var clean = new MySqlCommand(cleanup, connection);
                clean.Parameters.AddWithValue("@username", username);
                await clean.ExecuteNonQueryAsync();
            }

            var mySqlCommandRewardsDeleteUser = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandRewardsDeleteUser.CommandText = "DELETE FROM users WHERE username = @username";
            mySqlCommandRewardsDeleteUser.Connection = connection;
            mySqlCommandRewardsDeleteUser.Parameters.AddWithValue("@username", username);
            await mySqlCommandRewardsDeleteUser.ExecuteNonQueryAsync();

            return StatusCode(200);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occured when deleting user {0}, exception: " + ex.Message, username);
            return StatusCode(500);
        }
        finally
        {
            connection.Close();
        }

    }

    [HttpPost]
    public async Task<IActionResult> CreateAccount([FromBody] AccountInfo newUser)
    {
        if (newUser?.Username?.StartsWith(ConfigUtil.TEMP_TOKEN_PREFIX, StringComparison.Ordinal) == true || newUser?.Username?.Length > 20 || !Guid.TryParse(newUser?.UserUuid, out _) || !System.Net.Mail.MailAddress.TryCreate(newUser?.Email, out _) || string.IsNullOrEmpty(newUser?.Email) || string.IsNullOrEmpty(newUser?.UserUuid) || string.IsNullOrEmpty(newUser?.Username))
        {
            return StatusCode(409);
        }

        var connectionString = ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings);
        using var connection = new MySqlConnection(connectionString);

        MySqlTransaction? myTrans = null;

        try
        {
            await connection.OpenAsync();

            // 1. Check if username exists
            using var checkCmd = new MySqlCommand("SELECT COUNT(*) from users WHERE username = @username", connection);
            checkCmd.Parameters.AddWithValue("@username", newUser.Username);

            var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
            if (count > 0) return StatusCode(409);

            var lowerUsername = newUser.Username.ToLowerInvariant();
            myTrans = await connection.BeginTransactionAsync();

            // 2. Insert User
            using var insertCmd = new MySqlCommand(
                @"INSERT INTO users (device_uuid, email, username, ip_address, full_name) 
              VALUES (UUID_TO_BIN(@device_uuid), @email, @username, INET6_ATON(@ip_address), @full_name)",
                connection, myTrans);

            insertCmd.Parameters.AddWithValue("@full_name", string.IsNullOrWhiteSpace(newUser.FullName) ? DBNull.Value : newUser.FullName.Trim());
            insertCmd.Parameters.AddWithValue("@device_uuid", newUser.UserUuid);
            insertCmd.Parameters.AddWithValue("@username", lowerUsername);
            insertCmd.Parameters.AddWithValue("@email", newUser.Email);
            insertCmd.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "::");
            await insertCmd.ExecuteNonQueryAsync();

            // 3. Check Invites
            using var inviteCmd = new MySqlCommand("SELECT username_source FROM user_invite WHERE email = @email LIMIT 1", connection, myTrans);
            inviteCmd.Parameters.AddWithValue("@email", newUser.Email);

            var inviteUsername = (string?)await inviteCmd.ExecuteScalarAsync();

            if (inviteUsername != null)
            {
                using var acceptedInvitesCheck = new MySqlCommand(
                    @"SELECT COUNT(*) FROM user_transactions 
                  WHERE username = @username_source AND additional_data = 'Sent friendly invite'",
                    connection, myTrans);

                acceptedInvitesCheck.Parameters.AddWithValue("@username_source", inviteUsername);
                var invitesCount = Convert.ToInt64(await acceptedInvitesCheck.ExecuteScalarAsync());

                if (invitesCount <= 1)
                {
                    // Note: Ensure CreateReward is also updated to be Async if it does DB work
                    CreateReward(connection, lowerUsername, "Accepted friendly invite", TransactionDescriptionType.SPECIAL_REWARD, 500, myTrans, false);
                    CreateReward(connection, inviteUsername, "Sent friendly invite", TransactionDescriptionType.SPECIAL_REWARD, 500, myTrans, false);
                }
            }

            // Store only a SHA-256 digest; enqueue the original token for the existing mail worker.
            var tempToken = ConfigUtil.GetNewToken(_serverSettings, ConfigUtil.TEMP_TOKEN_PREFIX + lowerUsername,
                DateTime.UtcNow.AddMinutes(ConfigUtil.SIGN_IN_TOKEN_EXPIRATION_MINUTES), ConfigUtil.JWT_DEFAULT_ROLE);
            await EmailTokenStore.InsertAsync(connection, myTrans, lowerUsername, tempToken, HttpContext.Connection.RemoteIpAddress?.ToString());

            // 5. Queue Email
            using var emailCmd = new MySqlCommand(
                "INSERT INTO mail_queue (email, type, additional_data) VALUES (@email, @type, @additional_data)",
                connection, myTrans);

            emailCmd.Parameters.AddWithValue("@email", newUser.Email);
            emailCmd.Parameters.AddWithValue("@type", (int)EmailType.REGISTER);
            emailCmd.Parameters.AddWithValue("@additional_data", tempToken);

            await emailCmd.ExecuteNonQueryAsync();

            await myTrans.CommitAsync();
            return StatusCode(201);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Username {0} account could not be created", newUser.Username);
            if (myTrans != null) await myTrans.RollbackAsync();
            return StatusCode(409);
        }
        finally
        {
            if (connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }


    [HttpGet("VerifyToken")]
    public string? VerifyToken(string token, string userName)
    {
        try
        {
            string secret = _serverSettings.JWTSecret;
            var json = JwtBuilder.Create()
                         .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
                         .WithSecret(secret)
                         .MustVerifySignature()
                         .Decode<IDictionary<string, object>>(token);
            // Console.WriteLine(json);
            if (json.ContainsKey(ConfigUtil.JWT_USERNAME_KEY) && json.ContainsKey(ConfigUtil.JWT_ROLE_KEY) && json[ConfigUtil.JWT_USERNAME_KEY].ToString() == userName.ToLower())
            {
                return ConfigUtil.GetNewToken(_serverSettings, userName);
            }
            else return null;
        }
        catch (TokenExpiredException)
        {
            Console.WriteLine("Token has expired");
        }
        catch (SignatureVerificationException)
        {
            Console.WriteLine("Token has invalid signature");
        }
        return null;
    }




    [Authorize]
    [HttpPost("Wallet")]
    public StatusCodeResult Wallet(string address, string deviceId)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {0} and role is {1}", username, role);
            return StatusCode(400);
        }
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        MySqlTransaction? myTrans = null;
        try
        {
            connection.Open();
            myTrans = connection.BeginTransaction();
            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.Transaction = myTrans;
            mySqlCommandInsert.CommandText = "INSERT INTO user_wallet_history (username,device_uuid,ip_address, wallet_address,is_removal) VALUES ( @username, UUID_TO_BIN(@deviceUUID),INET6_ATON(@ip_address), @wallet,false)";
            mySqlCommandInsert.Connection = connection;
            mySqlCommandInsert.Transaction = myTrans;
            mySqlCommandInsert.Parameters.AddWithValue("@wallet", address);
            mySqlCommandInsert.Parameters.AddWithValue("@username", username);
            mySqlCommandInsert.Parameters.AddWithValue("@deviceUUID", deviceId);
            mySqlCommandInsert.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "::");
            if ((long)mySqlCommandInsert.ExecuteNonQuery() <= 0)
            {
                _logger.LogError("User {0} wallet-history could not be updated, could not set wallet: {1}, something went wrong.. ", username, address);
                return StatusCode(400);
            }
            var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandCheck.CommandText = "UPDATE users SET wallet_address = @wallet WHERE username = @username AND wallet_address IS NULL";
            mySqlCommandCheck.Connection = connection;
            mySqlCommandCheck.Transaction = myTrans;
            mySqlCommandCheck.Parameters.AddWithValue("@wallet", address);
            mySqlCommandCheck.Parameters.AddWithValue("@username", username);
            if ((long)mySqlCommandCheck.ExecuteNonQuery() > 0)
            {
                myTrans.Commit();
                return StatusCode(201);
            }
            else _logger.LogError("User {0} wallet could not be updated, could not set wallet: {1}, something went wrong.. ", username, address);
            return StatusCode(400);
        }
        catch (Exception ex)
        {
            myTrans?.Rollback();
            _logger.LogError("Exception occured when updating user {0} wallet, could not set wallet: {1}, exception: " + ex.Message, username, address);
            return StatusCode(400);
        }
        finally
        {
            connection.Close();
        }
    }


    [Authorize]
    [HttpDelete("Wallet")]
    public StatusCodeResult DeleteWallet(string address, string deviceId)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {0} and role is {1}", username, role);
            return StatusCode(400);
        }

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        MySqlTransaction? myTrans = null;
        try
        {
            connection.Open();
            myTrans = connection.BeginTransaction();
            var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandInsert.Transaction = myTrans;
            mySqlCommandInsert.CommandText = "INSERT INTO user_wallet_history (username,device_uuid,ip_address, wallet_address,is_removal) VALUES ( @username, UUID_TO_BIN(@deviceUUID),INET6_ATON(@ip_address), @wallet,1)";
            mySqlCommandInsert.Connection = connection;
            mySqlCommandInsert.Parameters.AddWithValue("@wallet", address);
            mySqlCommandInsert.Parameters.AddWithValue("@username", username);
            mySqlCommandInsert.Parameters.AddWithValue("@deviceUUID", deviceId);
            mySqlCommandInsert.Parameters.AddWithValue("@ip_address", Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "::");
            if ((long)mySqlCommandInsert.ExecuteNonQuery() > 0)
            {
                var mySqlCommandCheck = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandCheck.CommandText = "UPDATE users SET wallet_address = NULL WHERE username = @username";
                mySqlCommandCheck.Connection = connection;
                mySqlCommandCheck.Transaction = myTrans;
                mySqlCommandCheck.Parameters.AddWithValue("@wallet", address);
                mySqlCommandCheck.Parameters.AddWithValue("@username", username);
                if ((long)mySqlCommandCheck.ExecuteNonQuery() > 0)
                {
                    myTrans.Commit();
                    return StatusCode(201);
                }
                else _logger.LogError("User {0} wallet could not be updated, could not set wallet: {1}, something went wrong.. ", username, address);
            }
            else _logger.LogError("User {0} wallet-history could not be updated, could not set wallet: {1}, something went wrong.. ", username, address);
            return StatusCode(400);
        }
        catch (Exception ex)
        {
            myTrans?.Rollback();
            _logger.LogError("Exception occured when updating user {0} wallet, could not set wallet: {1}, exception: " + ex.Message, username, address);
            return StatusCode(400);
        }
        finally
        {
            connection.Close();
        }
    }

    [Authorize]
    [HttpPost("FCMToken")]
    public StatusCodeResult NewFCMToken(string token, string deviceUUID)
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(HttpContext.User.Identity as ClaimsIdentity);
        if (role == null || username == null)
        {
            _logger.LogError("Missing username or role! Decrypted username is {0} and role is {1}", username, role);
            return StatusCode(400);
        }

        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        try
        {
            connection.Open();
            InsertFCMToken(connection, username, deviceUUID, token);
            return StatusCode(200);
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception occured when updating user {0} fcm token, could not set token: {1}, exception: " + ex.Message, username, token);
            return StatusCode(400);
        }
        finally
        {
            connection.Close();
        }

    }

    void InsertFCMToken(MySqlConnection connection, string username, string deviceUUID, string token)
    {
        var mySqlCommandInsert = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommandInsert.CommandText = "INSERT IGNORE INTO user_fcm_token (username,device_uuid,token) VALUES ( @username, UUID_TO_BIN(@deviceUUID), @token)";
        mySqlCommandInsert.Connection = connection;
        mySqlCommandInsert.Parameters.AddWithValue("@username", username);
        mySqlCommandInsert.Parameters.AddWithValue("@deviceUUID", deviceUUID);
        mySqlCommandInsert.Parameters.AddWithValue("@token", token);
        mySqlCommandInsert.ExecuteNonQuery();
    }

    void CreateReward(MySqlConnection connection, string targetUsername, string metaData, TransactionDescriptionType descriptionType, int rewardSP, MySqlTransaction? transaction = null, bool commitTransaction = true)
    {
        MySqlTransaction myTrans = (transaction == null) ? connection.BeginTransaction() : transaction;
        try
        {
            var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
            var availableFrom = DateTime.UtcNow; //.AddDays(1);
            mySqlCommand5.CommandText = "INSERT INTO user_transactions (username,amount, transaction_type,additional_data,description_type,available_from) VALUES(@username, @amount, @transaction_type,@additional_data,@description_type, @available_from)";
            mySqlCommand5.Parameters.AddWithValue("@username", targetUsername);
            mySqlCommand5.Parameters.AddWithValue("@amount", rewardSP);
            mySqlCommand5.Parameters.AddWithValue("@transaction_type", 1); // TransactionType.STORY_POINTS_REWARD in Eventplatfrom
            mySqlCommand5.Parameters.AddWithValue("@additional_data", metaData);
            mySqlCommand5.Parameters.AddWithValue("@description_type", (int)descriptionType);
            mySqlCommand5.Parameters.AddWithValue("@available_from", availableFrom);
            mySqlCommand5.Connection = connection;
            mySqlCommand5.Transaction = myTrans;
            if ((long)mySqlCommand5.ExecuteNonQuery() > 0)
            {
                var mySqlCommand6 = GeneralUtil.CreateNotificationMySQLCommand(connection, myTrans,
                 targetUsername, metaData, descriptionType, availableFrom, NotificationType.REWARD);
                if ((long)mySqlCommand6.ExecuteNonQuery() > 0)
                {
                    if (commitTransaction) myTrans.Commit();
                }
                else myTrans.Rollback();
            }
            else myTrans.Rollback();
        }
        catch (Exception)
        {
            myTrans.Rollback();
        }
    }

}