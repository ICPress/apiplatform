using System.Text.Json.Serialization;


public class StoryMap {
    [JsonPropertyName("centerLat")] public double CenterLat { get; set; }
    [JsonPropertyName("centerLng")] public double CenterLng { get; set; }
    [JsonPropertyName("zoom")] public double Zoom { get; set; } = 8;
    [JsonPropertyName("geoJson")] public string GeoJson { get; set; } = "{\"type\":\"FeatureCollection\",\"features\":[]}";
}