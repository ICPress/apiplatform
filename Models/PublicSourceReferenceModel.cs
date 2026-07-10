using System.Text.Json.Serialization;
using System.Collections.Generic;

public class PublicSourceReferenceModel
{
    [JsonPropertyName("referenceSentence")]
    public string ReferenceSentence { get; set; } = string.Empty;

    [JsonPropertyName("referenceIndex")]
    public List<int> ReferenceIndex { get; set; } = new List<int>();
}
