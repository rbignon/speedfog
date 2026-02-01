// writer/SpeedFogWriter/Models/ClusterFile.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class ClusterFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("zone_maps")]
    public Dictionary<string, string> ZoneMaps { get; set; } = new();

    [JsonPropertyName("clusters")]
    public List<ClusterEntry> Clusters { get; set; } = new();

    public static ClusterFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ClusterFile>(json)
            ?? throw new InvalidOperationException("Failed to parse clusters.json");
    }

    public string? GetMap(string zoneId)
    {
        return ZoneMaps.TryGetValue(zoneId, out var map) ? map : null;
    }

    public string? GetMapForCluster(List<string> zones)
    {
        foreach (var zone in zones)
        {
            if (ZoneMaps.TryGetValue(zone, out var map))
                return map;
        }
        return null;
    }
}

public class ClusterEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("zones")]
    public List<string> Zones { get; set; } = new();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("fog_ids")]
    public List<string> FogIds { get; set; } = new();
}
