using JWT.Algorithms;
using JWT.Builder;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;

// Purpose-scoped proofs are never application sessions. Only their digests are stored.
public static class WebAuthProof
{
    public const string EmailPurpose = "WEB_EMAIL";
    public const string ProfilePurpose = "WEB_PROFILE";
    public sealed record Identity(string Email, string? GoogleSubject, string? FullName, string? Username = null, string? PreviousEmail = null, string? AccountId = null);

    // A distinct signing key keeps proofs out of the API's normal bearer authentication.
    private static string ProofSecret(ServerSettings settings) => Convert.ToBase64String(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.JWTSecret), Encoding.UTF8.GetBytes("ICPress.WebAuthProof.v1")));

    public static string Create(ServerSettings settings, string purpose, Identity identity)
    {
        var claims = new Dictionary<string, object> {
            [ConfigUtil.JWT_USERNAME_KEY] = ConfigUtil.TEMP_TOKEN_PREFIX + Guid.NewGuid().ToString("N"),
            [ConfigUtil.JWT_ROLE_KEY] = purpose,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["email"] = identity.Email
        };
        if (identity.GoogleSubject != null) claims["google_subject"] = identity.GoogleSubject;
        if (identity.FullName != null) claims["full_name"] = identity.FullName;
        if (identity.Username != null) claims["account_username"] = identity.Username;
        if (identity.PreviousEmail != null) claims["previous_email"] = identity.PreviousEmail;
        if (identity.AccountId != null) claims["account_id"] = identity.AccountId;
        return JwtBuilder.Create().WithAlgorithm(new HMACSHA256Algorithm()).WithSecret(ProofSecret(settings))
            .ExpirationTime(DateTime.UtcNow.AddMinutes(30)).AddClaims(claims).Encode();
    }

    public static Identity? Validate(ServerSettings settings, string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        try
        {
            var claims = JwtBuilder.Create().WithAlgorithm(new HMACSHA256Algorithm()).WithSecret(ProofSecret(settings))
                .MustVerifySignature().Decode<IDictionary<string, object>>(token);
            string? Claim(string key) => claims.TryGetValue(key, out var value) ? value?.ToString() : null;
            if (Claim(ConfigUtil.JWT_ROLE_KEY) != purpose ||
                !long.TryParse(Claim("exp"), out var expiry) || expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var email = Claim("email");
            if (email == null || email.Length > 255 || !System.Net.Mail.MailAddress.TryCreate(email, out _)) return null;
            return new Identity(email, Claim("google_subject"), Claim("full_name"), Claim("account_username"), Claim("previous_email"), Claim("account_id"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public static async Task StoreAsync(MySqlConnection connection, MySqlTransaction transaction,
        string token, string? email, string? ip)
    {
        using var command = new MySqlCommand(@"INSERT INTO sent_links_users
            (username, email, ip_address, token_hash, expires_at)
            VALUES (NULL, @email, INET6_ATON(@ip), @hash, UTC_TIMESTAMP() + INTERVAL 30 MINUTE)", connection, transaction);
        command.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
        command.Parameters.AddWithValue("@ip", ip ?? "::");
        command.Parameters.AddWithValue("@hash", EmailTokenStore.Hash(token));
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<bool> ConsumeAsync(MySqlConnection connection, MySqlTransaction transaction, string token)
    {
        using var command = new MySqlCommand(@"UPDATE sent_links_users SET verified_at = UTC_TIMESTAMP()
            WHERE token_hash = @hash AND username IS NULL AND verified_at IS NULL AND expires_at > UTC_TIMESTAMP()", connection, transaction);
        command.Parameters.AddWithValue("@hash", EmailTokenStore.Hash(token));
        return await command.ExecuteNonQueryAsync() == 1;
    }
}
