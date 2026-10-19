using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;

public static class EmailTokenStore
{
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static async Task InsertAsync(MySqlConnection connection, MySqlTransaction transaction,
        string username, string token, string? ipAddress, DateTime? expiresAt = null, bool emailDelivery = true)
    {
        using var command = new MySqlCommand(@"INSERT INTO sent_links_users
            (username, email, ip_address, token_hash, expires_at)
            VALUES (@username, (SELECT CASE WHEN @emailDelivery THEN email ELSE NULL END FROM users WHERE username = @username), INET6_ATON(@ip), @hash, @expires)", connection, transaction);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@emailDelivery", emailDelivery);
        command.Parameters.AddWithValue("@ip", ipAddress ?? "::");
        command.Parameters.AddWithValue("@hash", Hash(token));
        command.Parameters.AddWithValue("@expires", expiresAt ?? DateTime.UtcNow.AddMinutes(30));
        await command.ExecuteNonQueryAsync();
    }
}
