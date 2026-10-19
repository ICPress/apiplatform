using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace apiplatform.Controllers;

[ApiController]
[Route("Account")]
public class WebAccountController(ServerSettings settings, ILogger<WebAccountController> logger) : ControllerBase
{
    public sealed record GoogleLogin([Required, MaxLength(16384)] string Credential);

    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleLogin request)
    {
        if (string.IsNullOrWhiteSpace(settings.GoogleClientId))
            return StatusCode(503, "Google sign-in is not configured.");
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { settings.GoogleClientId } });
        }
        catch (InvalidJwtException) { return Unauthorized("Invalid Google credential."); }
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
            return Unauthorized("A verified Google email is required.");
        var email = payload.Email.Trim().ToLowerInvariant();
        var fullName = string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name.Trim();
        if (fullName?.Length > 200) fullName = fullName[..200];
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            string? username = null, existingSubject = null;
            // The immutable Google subject is the primary identity, not an editable email address.
            using (var find = new MySqlCommand("SELECT username, google_subject FROM users WHERE google_subject = @subject FOR UPDATE", connection, transaction))
            {
                find.Parameters.AddWithValue("@subject", payload.Subject);
                using var reader = await find.ExecuteReaderAsync();
                if (await reader.ReadAsync()) { username = reader.GetString(0); existingSubject = reader.GetString(1); }
            }
            if (username == null)
            {
                using var findEmail = new MySqlCommand("SELECT username, google_subject FROM users WHERE email = @email FOR UPDATE", connection, transaction);
                findEmail.Parameters.AddWithValue("@email", email);
                using var reader = await findEmail.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    username = reader.GetString(0);
                    existingSubject = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
                // Google is authoritative for Gmail and verified Workspace email only.
                if (username != null && (existingSubject != null ||
                    !(email.EndsWith("@gmail.com", StringComparison.Ordinal) || !string.IsNullOrEmpty(payload.HostedDomain))))
                    return Conflict("Use your email sign-in link for this existing account.");
            }
            if (username == null)
            {
                var result = await BeginProfile(connection, transaction, new WebAuthProof.Identity(email, payload.Subject, fullName));
                await transaction.CommitAsync();
                return result;
            }
            else
            {
                using var update = new MySqlCommand(@"UPDATE users SET google_subject = @subject,
                    full_name = COALESCE(full_name, @name), verified_at = COALESCE(verified_at, UTC_TIMESTAMP()) WHERE username = @username", connection, transaction);
                update.Parameters.AddWithValue("@subject", payload.Subject);
                update.Parameters.AddWithValue("@name", (object?)fullName ?? DBNull.Value);
                update.Parameters.AddWithValue("@username", username);
                await update.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            return Ok(new { token = ConfigUtil.GetNewToken(settings, username) });
        }
        catch (MySqlException ex) when (ex.Number is 1062 or 1213)
        {
            await transaction.RollbackAsync();
            return Conflict("Sign-in changed concurrently. Please try again.");
        }
    }

    public sealed record EmailLogin([Required, EmailAddress, MaxLength(255)] string Email);
    public sealed record VerifyLogin([Required, MaxLength(4096)] string Token);
    public sealed record CompleteProfile([Required, MaxLength(4096)] string RegistrationToken,
        [Required, RegularExpression("^[a-zA-Z0-9_]{3,20}$")] string Username,
        [Required, MaxLength(200)] string FullName, [MaxLength(2048)] string? AvatarTicket = null);

    [HttpPost("web/email")]
    public async Task<IActionResult> Email([FromBody] EmailLogin request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) return BadRequest();
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        // A MySQL advisory lock also serializes requests for emails without a users row.
        var lockName = "web-email:" + Convert.ToHexString(EmailTokenStore.Hash(email))[..50];
        using var acquire = new MySqlCommand("SELECT GET_LOCK(@key, 0)", connection);
        acquire.Parameters.AddWithValue("@key", lockName);
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync()) != 1) return Accepted();
        try
        {
            using var transaction = await connection.BeginTransactionAsync();
            using var recent = new MySqlCommand(@"SELECT COUNT(*) FROM sent_links_users
                WHERE email = @email AND verified_at IS NULL AND expires_at > UTC_TIMESTAMP() AND created_at > UTC_TIMESTAMP() - INTERVAL 30 MINUTE", connection, transaction);
            recent.Parameters.AddWithValue("@email", email);
            if (Convert.ToInt64(await recent.ExecuteScalarAsync()) > 0) return Accepted();
            var token = WebAuthProof.Create(settings, WebAuthProof.EmailPurpose, new WebAuthProof.Identity(email, null, null));
            await WebAuthProof.StoreAsync(connection, transaction, token, email, HttpContext.Connection.RemoteIpAddress?.ToString());
            using var mail = new MySqlCommand("INSERT INTO mail_queue (email, type, additional_data) VALUES (@email, @type, @token)", connection, transaction);
            mail.Parameters.AddWithValue("@email", email);
            mail.Parameters.AddWithValue("@type", (int)EmailType.RECOVER_SIGN_ON);
            mail.Parameters.AddWithValue("@token", token);
            await mail.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return Accepted();
        }
        finally
        {
            using var release = new MySqlCommand("SELECT RELEASE_LOCK(@key)", connection);
            release.Parameters.AddWithValue("@key", lockName);
            await release.ExecuteScalarAsync();
        }
    }

    [HttpPost("web/verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyLogin request)
    {
        var emailChange = WebAuthProof.Validate(settings, request.Token, EmailChangeProof.Purpose);
        if (emailChange != null) return await EmailChangeProof.ConfirmAsync(settings, request.Token, emailChange);
        var identity = WebAuthProof.Validate(settings, request.Token, WebAuthProof.EmailPurpose);
        if (identity == null) return Unauthorized();
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        if (!await WebAuthProof.ConsumeAsync(connection, transaction, request.Token)) return Unauthorized();
        using var find = new MySqlCommand("SELECT username FROM users WHERE email = @email FOR UPDATE", connection, transaction);
        find.Parameters.AddWithValue("@email", identity.Email);
        var username = (string?)await find.ExecuteScalarAsync();
        IActionResult result;
        if (username == null) result = await BeginProfile(connection, transaction, identity);
        else
        {
            using var verify = new MySqlCommand("UPDATE users SET verified_at = COALESCE(verified_at, UTC_TIMESTAMP()) WHERE username = @username", connection, transaction);
            verify.Parameters.AddWithValue("@username", username);
            await verify.ExecuteNonQueryAsync();
            result = Ok(new { token = ConfigUtil.GetNewToken(settings, username) });
        }
        await transaction.CommitAsync();
        return result;
    }

    public sealed record RegistrationImage([Required,MaxLength(4096)] string RegistrationToken,int Width,int Height);
    [HttpPost("web/profile-image/prepare")]
    public async Task<IActionResult> PrepareProfileImage(RegistrationImage request)
    {
        Response.Headers.CacheControl="no-store";
        if(WebAuthProof.Validate(settings,request.RegistrationToken,WebAuthProof.ProfilePurpose)==null)return Unauthorized();
        if(!ProfileImageUpload.ValidSize("avatar",request.Width,request.Height))return BadRequest();
        using var db=new MySqlConnection(settings.MysqlConnectionStoryPop);await db.OpenAsync();
        if(!await ProfileImageUpload.ActiveRegistration(db,request.RegistrationToken))return Unauthorized();
        var result=await ProfileImageUpload.Prepare(settings,logger,ProfileImageUpload.RegistrationOwner(request.RegistrationToken),"avatar",request.Width,request.Height);
        return result==null?StatusCode(503):Ok(result);
    }
    [HttpPost("web/complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteProfile request)
    {
        var identity = WebAuthProof.Validate(settings, request.RegistrationToken, WebAuthProof.ProfilePurpose);
        if (identity == null) return Unauthorized();
        var username = request.Username.ToLowerInvariant();
        var fullName = request.FullName.Trim();
        if (fullName.Length == 0) return BadRequest(new { code = "name_required" });
        if (string.Equals(username, settings.AdminUsername, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { code = "username_unavailable" });
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        string? avatar=null;
        if(!string.IsNullOrEmpty(request.AvatarTicket))
        {
            if(!await ProfileImageUpload.ActiveRegistration(connection,request.RegistrationToken))return Unauthorized();
            avatar=await ProfileImageUpload.Verify(settings,logger,request.AvatarTicket,ProfileImageUpload.RegistrationOwner(request.RegistrationToken),"avatar");
            if(avatar==null)return BadRequest(new {code="avatar_invalid"});
        }
        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // Consumption and account creation commit together; a rejected username can be retried.
            if (!await WebAuthProof.ConsumeAsync(connection, transaction, request.RegistrationToken)) return Unauthorized();
            using var exists = new MySqlCommand("SELECT COUNT(*) FROM users WHERE email = @email OR google_subject = @subject", connection, transaction);
            exists.Parameters.AddWithValue("@email", identity.Email);
            exists.Parameters.AddWithValue("@subject", (object?)identity.GoogleSubject ?? DBNull.Value);
            if (Convert.ToInt64(await exists.ExecuteScalarAsync()) != 0) return Conflict(new { code = "account_exists" });
            using var create = new MySqlCommand(@"INSERT INTO users
                (username, email, full_name, google_subject, ip_address, verified_at, device_uuid, profile_icon)
                VALUES (@username, @email, @name, @subject, INET6_ATON(@ip), UTC_TIMESTAMP(), UUID_TO_BIN(UUID()), @avatar)", connection, transaction);
            create.Parameters.AddWithValue("@username", username);
            create.Parameters.AddWithValue("@email", identity.Email);
            create.Parameters.AddWithValue("@name", fullName);
            create.Parameters.AddWithValue("@avatar", (object?)avatar ?? DBNull.Value);
            create.Parameters.AddWithValue("@subject", (object?)identity.GoogleSubject ?? DBNull.Value);
            create.Parameters.AddWithValue("@ip", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "::");
            await create.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return Ok(new { token = ConfigUtil.GetNewToken(settings, username) });
        }
        catch (MySqlException ex) when (ex.Number is 1062 or 1213)
        {
            await transaction.RollbackAsync();
            return Conflict(new { code = ex.Number == 1062 ? "username_unavailable" : "retry" });
        }
    }

    private async Task<IActionResult> BeginProfile(MySqlConnection connection, MySqlTransaction transaction, WebAuthProof.Identity identity)
    {
        var token = WebAuthProof.Create(settings, WebAuthProof.ProfilePurpose, identity);
        await WebAuthProof.StoreAsync(connection, transaction, token, null, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { requiresProfile = true, registrationToken = token, email = identity.Email, fullName = identity.FullName });
    }

    [Authorize]
    [HttpPost("web/app-link")]
    public async Task<IActionResult> AppLink()
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(User.Identity as ClaimsIdentity);
        if (username == null || username.StartsWith(ConfigUtil.TEMP_TOKEN_PREFIX, StringComparison.Ordinal) ||
            role is not (ConfigUtil.JWT_DEFAULT_ROLE or ConfigUtil.JWT_ADMIN_ROLE)) return Unauthorized();
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        using var account = new MySqlCommand("SELECT username FROM users WHERE username = @username AND verified_at IS NOT NULL FOR UPDATE", connection, transaction);
        account.Parameters.AddWithValue("@username", username);
        if (await account.ExecuteScalarAsync() == null) return Unauthorized();
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        // Same temporary claim format as Android's existing /Account/verify/{authToken}.
        // This fresh proof is independent of the email token the browser already consumed.
        var token = ConfigUtil.GetNewToken(settings, ConfigUtil.TEMP_TOKEN_PREFIX + username, expiresAt, ConfigUtil.JWT_DEFAULT_ROLE);
        await EmailTokenStore.InsertAsync(connection, transaction, username, token, HttpContext.Connection.RemoteIpAddress?.ToString(), expiresAt, emailDelivery: false);
        await transaction.CommitAsync();
        return Ok(new { verificationToken = token, expiresAt });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var (username, role) = ConfigUtil.GetUsernameAndRoleFromClaims(User.Identity as ClaimsIdentity);
        if (username == null || username.StartsWith(ConfigUtil.TEMP_TOKEN_PREFIX, StringComparison.Ordinal) ||
            role is not (ConfigUtil.JWT_DEFAULT_ROLE or ConfigUtil.JWT_ADMIN_ROLE)) return Unauthorized();
        using var connection = new MySqlConnection(settings.MysqlConnectionStoryPop);
        await connection.OpenAsync();
        using var command = new MySqlCommand(@"SELECT u.username, u.full_name, u.email,
            (SELECT COUNT(*) FROM user_notification n WHERE n.username = u.username AND n.notification_read = 0 AND n.available_from <= UTC_TIMESTAMP()
                AND (n.type <> 8 OR NOT EXISTS (SELECT 1 FROM user_contact_approved c WHERE c.username=u.username AND c.target_username=SUBSTRING_INDEX(n.additional_data,':',1) AND c.blocked=1))) AS notifications,
            (SELECT COUNT(*) FROM user_message m WHERE m.target_username = u.username AND m.is_read = 0 AND m.deleted = 0
                AND NOT EXISTS (SELECT 1 FROM user_contact_approved c WHERE c.username = u.username AND c.target_username = m.username AND c.blocked = 1)) AS messages, u.profile_icon
            FROM users u WHERE u.username = @username AND u.verified_at IS NOT NULL", connection);
        command.Parameters.AddWithValue("@username", username);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Unauthorized();
        return Ok(new {
            username = reader.GetString(0), fullName = reader.IsDBNull(1) ? null : reader.GetString(1),
            email = reader.GetString(2), onBetaWaitingList = true,
            unreadNotifications = reader.GetInt64(3), unreadMessages = reader.GetInt64(4),
            avatarUrl = ProfileImages.Url(reader.IsDBNull(5) ? null : reader.GetString(5), settings)
        });
    }
}
