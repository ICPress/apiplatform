public class AuthCacheData{
    public string AuthToken { get; set; } = "";
    public DateTime Expiration { get; set; } = DateTime.Now;
    public DateTime Issued { get; set; } = DateTime.Now;
}