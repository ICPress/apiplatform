using System.Text.Json.Serialization;

public record struct Version
{
    [JsonConstructor]
    public Version(bool igniteRunning)
    {
        this.IgniteRunning = igniteRunning;
    }

    [JsonPropertyName("versionCode")]
    public int VersionCode { get; init; } = 1;

    [JsonPropertyName("igniteRunning")]
    public bool IgniteRunning { get; init; }
}
