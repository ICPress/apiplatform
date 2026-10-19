using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
public static class EmailChangeProof
{
    public const string Purpose="WEB_EMAIL_CHANGE";
    public static async Task<IActionResult> RequestAsync(MySqlConnection db,ServerSettings settings,string username,string email,string? ip)
    {
        using var tx=await db.BeginTransactionAsync();
        using var find=new MySqlCommand("SELECT email,COALESCE(BIN_TO_UUID(device_uuid),DATE_FORMAT(created_at,'%Y-%m-%dT%TZ')) FROM users WHERE username=@user FOR UPDATE",db,tx);
        find.Parameters.AddWithValue("@user",username);string previous,id;
        using(var r=await find.ExecuteReaderAsync()){if(!await r.ReadAsync())return new UnauthorizedResult();previous=r.GetString(0);id=r.GetString(1);}
        if(string.Equals(previous,email,StringComparison.OrdinalIgnoreCase))return new BadRequestObjectResult("Enter a different email address.");
        using var taken=new MySqlCommand("SELECT COUNT(*) FROM users WHERE email=@email",db,tx);taken.Parameters.AddWithValue("@email",email);
        if(Convert.ToInt64(await taken.ExecuteScalarAsync())!=0)return new ConflictObjectResult("That email address cannot be used.");
        using var recent=new MySqlCommand("SELECT COUNT(*) FROM sent_links_users WHERE username=@user AND created_at>UTC_TIMESTAMP()-INTERVAL 1 MINUTE",db,tx);
        recent.Parameters.AddWithValue("@user",username);if(Convert.ToInt64(await recent.ExecuteScalarAsync())!=0)return new StatusCodeResult(429);
        var proof=WebAuthProof.Create(settings,Purpose,new WebAuthProof.Identity(email,null,null,username,previous,id));
        using var store=new MySqlCommand(@"INSERT INTO sent_links_users(username,email,ip_address,token_hash,expires_at) VALUES(@user,@email,INET6_ATON(@ip),@hash,UTC_TIMESTAMP()+INTERVAL 30 MINUTE)",db,tx);
        store.Parameters.AddWithValue("@user",username);store.Parameters.AddWithValue("@email",email);store.Parameters.AddWithValue("@ip",ip??"::");store.Parameters.AddWithValue("@hash",EmailTokenStore.Hash(proof));await store.ExecuteNonQueryAsync();
        using var queue=new MySqlCommand("INSERT INTO mail_queue(email,type,additional_data) VALUES(@email,@type,@token)",db,tx);
        queue.Parameters.AddWithValue("@email",email);queue.Parameters.AddWithValue("@type",(int)EmailType.RECOVER_SIGN_ON);queue.Parameters.AddWithValue("@token",proof);
        await queue.ExecuteNonQueryAsync();await tx.CommitAsync();return new AcceptedResult();
    }
    public static async Task<IActionResult> ConfirmAsync(ServerSettings settings,string token,WebAuthProof.Identity identity)
    {
        if(identity.Username==null||identity.PreviousEmail==null||identity.AccountId==null)return new UnauthorizedResult();
        using var db=new MySqlConnection(settings.MysqlConnectionStoryPop);await db.OpenAsync();using var tx=await db.BeginTransactionAsync();
        using var account=new MySqlCommand(@"SELECT username FROM users WHERE username=@user AND email=@previous AND COALESCE(BIN_TO_UUID(device_uuid),DATE_FORMAT(created_at,'%Y-%m-%dT%TZ'))=@id FOR UPDATE",db,tx);
        account.Parameters.AddWithValue("@user",identity.Username);account.Parameters.AddWithValue("@previous",identity.PreviousEmail);account.Parameters.AddWithValue("@id",identity.AccountId);
        if(await account.ExecuteScalarAsync()==null)return new UnauthorizedResult();
        using var consume=new MySqlCommand(@"UPDATE sent_links_users SET verified_at=UTC_TIMESTAMP() WHERE token_hash=@hash AND username=@user AND email=@email AND verified_at IS NULL AND expires_at>UTC_TIMESTAMP()",db,tx);
        consume.Parameters.AddWithValue("@hash",EmailTokenStore.Hash(token));consume.Parameters.AddWithValue("@user",identity.Username);consume.Parameters.AddWithValue("@email",identity.Email);
        if(await consume.ExecuteNonQueryAsync()!=1)return new UnauthorizedResult();
        try
        {
            using var update=new MySqlCommand("UPDATE users SET email=@email,verified_at=UTC_TIMESTAMP() WHERE username=@user AND email=@previous",db,tx);
            update.Parameters.AddWithValue("@email",identity.Email);update.Parameters.AddWithValue("@user",identity.Username);update.Parameters.AddWithValue("@previous",identity.PreviousEmail);
            if(await update.ExecuteNonQueryAsync()!=1)return new UnauthorizedResult();
            using var revoke=new MySqlCommand("UPDATE sent_links_users SET verified_at=UTC_TIMESTAMP() WHERE verified_at IS NULL AND (username=@user OR email=@previous)",db,tx);
            revoke.Parameters.AddWithValue("@user",identity.Username);revoke.Parameters.AddWithValue("@previous",identity.PreviousEmail);await revoke.ExecuteNonQueryAsync();
            await tx.CommitAsync();return new OkObjectResult(new {token=ConfigUtil.GetNewToken(settings,identity.Username),emailChanged=true});
        }catch(MySqlException ex)when(ex.Number==1062){return new ConflictObjectResult(new {code="email_unavailable"});}
    }
}
