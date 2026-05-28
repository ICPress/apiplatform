using System;
using System.Collections.Generic;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class ArticleGuidelines
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("lastUpdated")]
    public string LastUpdated { get; set; } = string.Empty;

    [JsonPropertyName("categories")]
    public List<GuidelineCategory> Categories { get; set; } = new();
}

public class GuidelineCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public float Weight { get; set; } = 1.0f;

    [JsonPropertyName("subRules")]
    public List<GuidelineRule> SubRules { get; set; } = new();
}

public class GuidelineRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; } = true;

    [JsonPropertyName("minScore")]
    public float MinScore { get; set; } = 0.7f;
}