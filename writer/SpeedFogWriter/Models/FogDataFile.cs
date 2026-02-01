// writer/SpeedFogWriter/Models/FogDataFile.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;

namespace SpeedFogWriter.Models;

public class FogDataFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("fogs")]
    public Dictionary<string, FogEntryData> Fogs { get; set; } = new();

    public static FogDataFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FogDataFile>(json)
            ?? throw new InvalidOperationException("Failed to parse fog_data.json");
    }

    public FogEntryData? GetFog(string fogId, string? zone = null)
    {
        if (Fogs.TryGetValue(fogId, out var fog))
        {
            if (zone == null || fog.Zones.Contains(zone))
                return fog;
        }

        foreach (var (key, data) in Fogs)
        {
            if (key.EndsWith($"_{fogId}") && (zone == null || data.Zones.Contains(zone)))
                return data;
        }

        return null;
    }
}

public class FogEntryData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("zones")]
    public List<string> Zones { get; set; } = new();

    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("entity_id")]
    public int EntityId { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("asset_name")]
    public string AssetName { get; set; } = "";

    [JsonPropertyName("lookup_by")]
    public string? LookupBy { get; set; }

    [JsonPropertyName("position")]
    public float[]? Position { get; set; }

    [JsonPropertyName("rotation")]
    public float[]? Rotation { get; set; }

    public bool HasPosition => Position != null && Position.Length == 3;
    public bool IsMakeFrom => Type == "makefrom";

    public Vector3 PositionVec =>
        Position != null ? new(Position[0], Position[1], Position[2]) : default;

    public Vector3 RotationVec =>
        Rotation != null ? new(Rotation[0], Rotation[1], Rotation[2]) : default;

    public byte[] MapBytes
    {
        get
        {
            var parts = Map.TrimStart('m').Split('_');
            if (parts.Length != 4)
                throw new FormatException($"Invalid map ID: {Map}");

            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3])
            };
        }
    }
}
