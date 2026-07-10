using System.Threading.Tasks;
using MySql.Data.MySqlClient;

/// <summary>
/// Tracks slug_titles that have been permanently removed, so GetArticle can tell a
/// story that used to exist (410 Gone) apart from one that never did (404 Not Found).
/// </summary>
public static class DeletedStoriesUtil
{
    public static async Task<bool> IsSlugDeletedAsync(
        MySqlConnection connection, string slugTitle, MySqlTransaction? transaction = null)
    {
        var cmd = new MySqlCommand();
        cmd.Connection = connection;
        if (transaction != null)
            cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COUNT(*) FROM deleted_stories WHERE slug_title = @slug_title";
        cmd.Parameters.AddWithValue("@slug_title", slugTitle);
        return (long?)await cmd.ExecuteScalarAsync() > 0;
    }

}
