using System.Text.Json.Serialization;

public class GorseItemModel {
    public string ItemId { get; set; } = "";
    public bool IsHidden { get; set; } = false;
    public string[] Categories { get; set; } = new string[]{};
    public string Timestamp { get; set; } = "";
    public GorseLabels Labels { get; set; } = new GorseLabels();
    public string Comment { get; set; } = "";
}

public class GorseLabels {
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = new float[]{};
    [JsonPropertyName("topics")]
    public string[] Topics { get; set; } = new string[]{};
}
