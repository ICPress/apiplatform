using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;

public static class ProfileImageUpload
{
    public const int FullLimit = 1024;
    public const int PreviewLimit = 576;
    public sealed record Receipt(string Owner, string Kind, string Name, int Width, int Height, int? MinWidth, int? MinHeight, long Expires);
    public sealed record Prepared(string UploadUrl, string? PreviewUploadUrl, string AuthToken, string Ticket);
    private static byte[] Key(ServerSettings settings) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.JWTSecret), Encoding.UTF8.GetBytes("ICPress.ProfileImageUpload.v2"));
    public static string RegistrationOwner(string proof) => "registration:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
    public static bool ValidSize(string kind, int width, int height) => (kind is "avatar" or "cover") && width is > 0 and <= FullLimit && height is > 0 and <= FullLimit && (kind != "avatar" || width == height);
    public static (int Width, int Height) PreviewSize(int width, int height)
    {
        var scale = Math.Min(1d, Math.Min((double)PreviewLimit / width, (double)PreviewLimit / height));
        return (Math.Max(1,(int)Math.Round(width * scale,MidpointRounding.AwayFromZero)),Math.Max(1,(int)Math.Round(height * scale,MidpointRounding.AwayFromZero)));
    }
    public static async Task<bool> ActiveRegistration(MySqlConnection db, string proof)
    {
        using var cmd = new MySqlCommand("SELECT COUNT(*) FROM sent_links_users WHERE token_hash=@hash AND username IS NULL AND verified_at IS NULL AND expires_at>UTC_TIMESTAMP()", db);
        cmd.Parameters.AddWithValue("@hash", EmailTokenStore.Hash(proof));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }
    private static string? ObjectUrl(string root, string name)
    {
        if(!Uri.TryCreate(root.TrimEnd('/')+"/",UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))return null;
        return new Uri(uri,name).AbsoluteUri;
    }
    public static async Task<Prepared?> Prepare(ServerSettings settings, ILogger logger, string owner, string kind, int width, int height)
    {
        if(!ValidSize(kind,width,height))return null;
        var name="profile_"+Guid.NewGuid().ToString("N")+".jpg";
        var largeUrl=ObjectUrl(settings.SwiftBucketLargePath,name);
        bool needsPreview=width>PreviewLimit||height>PreviewLimit;
        var smallUrl=needsPreview?ObjectUrl(settings.SwiftBucketSmallPath,name):null;
        if(largeUrl==null||(needsPreview&&smallUrl==null))return null;
        using var client=new HttpClient();
        var token=await GeneralUtil.StartGetNewTokenTask(client,"",settings,logger);
        if(string.IsNullOrWhiteSpace(token))return null;
        var small=PreviewSize(width,height);
        var receipt=new Receipt(owner,kind,name,width,height,needsPreview?small.Width:null,needsPreview?small.Height:null,DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
        var payload=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(receipt));
        var signature=Convert.ToBase64String(HMACSHA256.HashData(Key(settings),Encoding.UTF8.GetBytes(payload)));
        return new Prepared(largeUrl,smallUrl,token,payload+"."+signature);
    }
    private static Receipt? Validate(ServerSettings settings,string? ticket,string owner,string kind)
    {
        if(string.IsNullOrWhiteSpace(ticket)||ticket.Length>2048)return null;
        try
        {
            var parts=ticket.Split('.');
            if(parts.Length!=2||!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]),HMACSHA256.HashData(Key(settings),Encoding.UTF8.GetBytes(parts[0]))))return null;
            var receipt=JsonSerializer.Deserialize<Receipt>(Convert.FromBase64String(parts[0]));
            return receipt!=null&&receipt.Owner==owner&&receipt.Kind==kind&&receipt.Expires>DateTimeOffset.UtcNow.ToUnixTimeSeconds()&&ValidSize(kind,receipt.Width,receipt.Height)?receipt:null;
        }
        catch(Exception ex) when(ex is FormatException or JsonException){return null;}
    }
    private static async Task<bool> CheckObject(HttpClient client,string? url,string token,int width,int height)
    {
        if(url==null)return false;
        using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Add("X-Auth-Token",token);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength is >5*1024*1024||response.Content.Headers.ContentType?.MediaType!="image/jpeg")return false;
        using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+count>5*1024*1024)return false;memory.Write(buffer,0,count);}
        var info=ProfileImages.Inspect(memory.ToArray());
        return info!=null&&info.Value.Extension==".jpg"&&info.Value.Width==width&&info.Value.Height==height;
    }
    public static async Task<string?> Verify(ServerSettings settings,ILogger logger,string? ticket,string owner,string kind)
    {
        var receipt=Validate(settings,ticket,owner,kind);if(receipt==null)return null;
        using var authClient=new HttpClient();var token=await GeneralUtil.StartGetNewTokenTask(authClient,"",settings,logger);if(string.IsNullOrWhiteSpace(token))return null;
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});
        if(!await CheckObject(client,ObjectUrl(settings.SwiftBucketLargePath,receipt.Name),token,receipt.Width,receipt.Height))return null;
        if(receipt.MinWidth.HasValue&&receipt.MinHeight.HasValue&&!await CheckObject(client,ObjectUrl(settings.SwiftBucketSmallPath,receipt.Name),token,receipt.MinWidth.Value,receipt.MinHeight.Value))return null;
        return JsonSerializer.Serialize(new ImageInfoMetadata{Name=receipt.Name,Width=receipt.Width,Height=receipt.Height,MinWidth=receipt.MinWidth,MinHeight=receipt.MinHeight});
    }
}
