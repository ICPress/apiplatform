using System.Text.Json.Serialization;
using System.Collections.Generic;

public class ReferenceModel
{
    [JsonPropertyName("sentence")]
    public string Sentence { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public List<int> Index { get; set; } = new List<int>();
}
