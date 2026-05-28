using System.Text.Json.Serialization;
using System.Collections.Generic;

public class ApplicationCredential
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";
}

public class Auth
{
    [JsonPropertyName("identity")]
    public Identity Identity { get; set; } = new Identity();
}

public class Identity
{
    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; } = new List<string>();

    [JsonPropertyName("application_credential")]
    public ApplicationCredential ApplicationCredential { get; set; } = new ApplicationCredential();
}

public class OpenStackAuth
{
    [JsonPropertyName("auth")]
    public Auth Auth { get; set; } = new Auth();
}
