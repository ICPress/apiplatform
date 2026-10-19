using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
namespace apiplatform.Controllers;
[ApiController, Authorize, Route("Community")]
public class CommunityController(ServerSettings settings, ILogger<CommunityController> logger) : ControllerBase
{
    private string? CurrentUser
    {
        get { var (username,role)=ConfigUtil.GetUsernameAndRoleFromClaims(User.Identity as ClaimsIdentity); return role is ConfigUtil.JWT_DEFAULT_ROLE or ConfigUtil.JWT_ADMIN_ROLE ? username : null; }
    }
    private async Task<MySqlConnection> Open()
    {
        var db=new MySqlConnection(settings.MysqlConnectionStoryPop);
        try { await db.OpenAsync();return db; } catch { db.Dispose();throw; }
    }
    private static string? Value(System.Data.Common.DbDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    [AllowAnonymous,HttpGet("profile/{username}")]
    public async Task<IActionResult> Profile(string username)
    {
        using var db=await Open();
        using var cmd=new MySqlCommand(@"SELECT u.username,u.full_name,u.profile_text,u.profile_icon,u.profile_background_image,
            DATE_FORMAT(u.created_at,'%Y-%m-%dT%TZ'),
            (SELECT COUNT(*) FROM user_following f WHERE f.following=u.username),
            (SELECT COUNT(*) FROM user_following f WHERE f.username=u.username),
            (SELECT COUNT(*) FROM user_stories s WHERE s.username=u.username)
            FROM users u WHERE u.username=@username",db);
        cmd.Parameters.AddWithValue("@username",username);using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync())return NotFound();
        return Ok(new {username=r.GetString(0),fullName=Value(r,1),description=Value(r,2),avatarUrl=ProfileImages.Url(Value(r,3),settings),coverUrl=ProfileImages.Url(Value(r,4),settings),memberSince=r.GetString(5),followers=r.GetInt64(6),following=r.GetInt64(7),articlesPublished=r.GetInt64(8)});
    }
    public sealed record ProfileEdit([MaxLength(200)] string? FullName,[MaxLength(500)] string? Description);
    [HttpPost("profile")]
    public async Task<IActionResult> Edit(ProfileEdit edit)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();using var db=await Open();
        using var cmd=new MySqlCommand("UPDATE users SET full_name=@name,profile_text=@text WHERE username=@user",db);
        cmd.Parameters.AddWithValue("@name",string.IsNullOrWhiteSpace(edit.FullName)?DBNull.Value:edit.FullName.Trim());
        cmd.Parameters.AddWithValue("@text",(object?)edit.Description?.Trim()??DBNull.Value);cmd.Parameters.AddWithValue("@user",username);
        await cmd.ExecuteNonQueryAsync();return NoContent();
    }
    public sealed record ImageSize(int Width,int Height);
    [HttpPost("profile/image/{kind}/prepare")]
    public async Task<IActionResult> PrepareImage(string kind,ImageSize size)
    {
        Response.Headers.CacheControl="no-store";
        var username=CurrentUser;if(username==null)return Unauthorized();
        if(!ProfileImageUpload.ValidSize(kind,size.Width,size.Height))return BadRequest();
        using var db=await Open();using var exists=new MySqlCommand("SELECT COUNT(*) FROM users WHERE username=@user",db);
        exists.Parameters.AddWithValue("@user",username);if(Convert.ToInt64(await exists.ExecuteScalarAsync())!=1)return Unauthorized();
        var result=await ProfileImageUpload.Prepare(settings,logger,"user:"+username,kind,size.Width,size.Height);
        return result==null?StatusCode(503):Ok(result);
    }
    public sealed record ImageCommit([Required,MaxLength(2048)] string Ticket);
    [HttpPost("profile/image/{kind}/complete")]
    public async Task<IActionResult> CompleteImage(string kind,ImageCommit request)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();if(kind is not ("avatar" or "cover"))return BadRequest();
        var metadata=await ProfileImageUpload.Verify(settings,logger,request.Ticket,"user:"+username,kind);
        if(metadata==null)return BadRequest("An image upload is incomplete or expired. Please select the image again.");
        using var db=await Open();var column=kind=="avatar"?"profile_icon":"profile_background_image";
        using var update=new MySqlCommand($"UPDATE users SET {column}=@image WHERE username=@user",db);
        update.Parameters.AddWithValue("@image",metadata);update.Parameters.AddWithValue("@user",username);
        if(await update.ExecuteNonQueryAsync()!=1)return Unauthorized();return Ok(new {url=ProfileImages.Url(metadata,settings)});
    }
    [HttpDelete("profile/image/{kind}")]
    public async Task<IActionResult> RemoveImage(string kind)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();if(kind is not ("avatar" or "cover"))return BadRequest();
        using var db=await Open();var column=kind=="avatar"?"profile_icon":"profile_background_image";
        using var cmd=new MySqlCommand($"UPDATE users SET {column}=NULL WHERE username=@user",db);cmd.Parameters.AddWithValue("@user",username);await cmd.ExecuteNonQueryAsync();return NoContent();
    }
    public sealed record EmailEdit([Required,EmailAddress,MaxLength(255)] string Email);
    [HttpPost("email")]
    public async Task<IActionResult> ChangeEmail(EmailEdit edit)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();using var db=await Open();
        return await EmailChangeProof.RequestAsync(db,settings,username,edit.Email.Trim().ToLowerInvariant(),HttpContext.Connection.RemoteIpAddress?.ToString());
    }
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(uint? before=null)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();using var db=await Open();
        using var cmd=new MySqlCommand(@"SELECT n.notification_id,n.type,n.additional_data,n.notification_read,DATE_FORMAT(n.available_from,'%Y-%m-%dT%TZ'),u.username,u.full_name,u.profile_icon
            FROM user_notification n LEFT JOIN users u ON n.type IN (2,3,4,5,6,8) AND u.username=SUBSTRING_INDEX(n.additional_data,':',1)
            WHERE n.username=@user AND n.available_from<=UTC_TIMESTAMP() AND n.notification_id<@before
            AND (n.type<>8 OR NOT EXISTS(SELECT 1 FROM user_contact_approved c WHERE c.username=@user AND c.target_username=SUBSTRING_INDEX(n.additional_data,':',1) AND c.blocked=1))
            ORDER BY n.notification_id DESC LIMIT 30",db);
        cmd.Parameters.AddWithValue("@user",username);cmd.Parameters.AddWithValue("@before",before??uint.MaxValue);
        using var r=await cmd.ExecuteReaderAsync();var items=new List<object>();
        while(await r.ReadAsync())
        {
            var type=r.GetInt32(1);var data=r.GetString(2);var parts=data.Split(':');var actor=Value(r,5);var fullName=Value(r,6);var label=string.IsNullOrWhiteSpace(fullName)?actor??"Someone":fullName;
            var title=type switch {2=>label+" started following you",3=>label+" liked your article",4=>label+" liked your comment",5=>label+" replied to your comment",6=>label+" commented on your article",8=>label+" sent you a message",1=>"You received a reward",7=>"Gift box opened",9=>"Article review update",_=>"Notification"};
            var link=type==2&&actor!=null?"/u/"+Uri.EscapeDataString(actor):type==8&&actor!=null?"/messages/"+Uri.EscapeDataString(actor):type is 4 or 5 or 6 && parts.Length>1?"/s/"+Uri.EscapeDataString(parts[1]):null;
            items.Add(new {id=(uint)r.GetInt64(0),title,detail=type==3&&parts.Length>1?data[(data.IndexOf(':')+1)..]:type is 0 or 1 or 7 or 9?data:null,href=link,read=r.GetBoolean(3),timestamp=r.GetString(4),avatarUrl=ProfileImages.Url(Value(r,7),settings)});
        }
        return Ok(items);
    }
    public sealed record ReadItems([Required] uint[] Ids);
    [HttpPost("notifications/read")]
    public async Task<IActionResult> ReadNotifications(ReadItems request)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();if(request.Ids.Length is 0 or >30)return BadRequest();using var db=await Open();
        using var cmd=new MySqlCommand("UPDATE user_notification SET notification_read=1 WHERE username=@user AND available_from<=UTC_TIMESTAMP() AND notification_id IN ("+string.Join(",",request.Ids.Select((_,i)=>"@id"+i))+")",db);
        cmd.Parameters.AddWithValue("@user",username);for(int i=0;i<request.Ids.Length;i++)cmd.Parameters.AddWithValue("@id"+i,request.Ids[i]);await cmd.ExecuteNonQueryAsync();return NoContent();
    }
    [HttpGet("messages")]
    public async Task<IActionResult> Messages(uint? before=null)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();using var db=await Open();
        using var cmd=new MySqlCommand(@"WITH ranked AS (
            SELECT m.*,CASE WHEN m.username=@user THEN m.target_username ELSE m.username END AS peer,
            ROW_NUMBER() OVER(PARTITION BY CASE WHEN m.username=@user THEN m.target_username ELSE m.username END ORDER BY m.message_id DESC) AS row_rank
            FROM user_message m WHERE (m.username=@user OR m.target_username=@user)
            AND NOT EXISTS(SELECT 1 FROM user_contact_approved c WHERE c.username=@user AND c.target_username=CASE WHEN m.username=@user THEN m.target_username ELSE m.username END AND c.blocked=1))
            SELECT r.message_id,r.peer,u.full_name,u.profile_icon,r.content,r.type,r.deleted,DATE_FORMAT(r.published_at,'%Y-%m-%dT%TZ'),
            (SELECT COUNT(*) FROM user_message unread WHERE unread.username=r.peer AND unread.target_username=@user AND unread.is_read=0 AND unread.deleted=0)
            FROM ranked r LEFT JOIN users u ON u.username=r.peer WHERE r.row_rank=1 AND r.message_id<@before ORDER BY r.message_id DESC LIMIT 30",db);
        cmd.Parameters.AddWithValue("@user",username);cmd.Parameters.AddWithValue("@before",before??uint.MaxValue);using var r=await cmd.ExecuteReaderAsync();var items=new List<object>();
        while(await r.ReadAsync())items.Add(new {id=(uint)r.GetInt64(0),username=r.GetString(1),fullName=Value(r,2),avatarUrl=ProfileImages.Url(Value(r,3),settings),preview=r.GetBoolean(6)?"Message deleted":r.GetInt32(5)==0?r.GetString(4):"Image attachment",timestamp=r.GetString(7),unread=r.GetInt64(8)});
        return Ok(items);
    }
    [HttpGet("messages/{peer}")]
    public async Task<IActionResult> Conversation(string peer,uint? before=null)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();using var db=await Open();
        using var cmd=new MySqlCommand(@"SELECT message_id,username,content,type,deleted,is_read,DATE_FORMAT(published_at,'%Y-%m-%dT%TZ') FROM user_message m
            WHERE ((m.username=@user AND m.target_username=@peer) OR (m.username=@peer AND m.target_username=@user))
            AND NOT EXISTS(SELECT 1 FROM user_contact_approved c WHERE c.username=@user AND c.target_username=@peer AND c.blocked=1)
            AND message_id<@before ORDER BY message_id DESC LIMIT 30",db);
        cmd.Parameters.AddWithValue("@user",username);cmd.Parameters.AddWithValue("@peer",peer);cmd.Parameters.AddWithValue("@before",before??uint.MaxValue);using var r=await cmd.ExecuteReaderAsync();var items=new List<object>();
        while(await r.ReadAsync())
        {
            var deleted=r.GetBoolean(4);var content=deleted?"":r.GetString(2);var type=r.GetInt32(3);
            items.Add(new {id=(uint)r.GetInt64(0),author=r.GetString(1),content=type==0?content:"",images=type==1&&!deleted?ProfileImages.MessageUrls(content,settings):Array.Empty<string>(),deleted,read=r.GetBoolean(5),timestamp=r.GetString(6)});
        }
        return Ok(items);
    }
    [HttpPost("messages/{peer}/read")]
    public async Task<IActionResult> ReadMessages(string peer,ReadItems request)
    {
        var username=CurrentUser;if(username==null)return Unauthorized();if(request.Ids.Length is 0 or >30)return BadRequest();using var db=await Open();
        using var cmd=new MySqlCommand("UPDATE user_message SET is_read=1 WHERE target_username=@user AND username=@peer AND deleted=0 AND message_id IN ("+string.Join(",",request.Ids.Select((_,i)=>"@id"+i))+")",db);
        cmd.Parameters.AddWithValue("@user",username);cmd.Parameters.AddWithValue("@peer",peer);for(int i=0;i<request.Ids.Length;i++)cmd.Parameters.AddWithValue("@id"+i,request.Ids[i]);await cmd.ExecuteNonQueryAsync();return NoContent();
    }
}
