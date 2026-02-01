// writer/SpeedFogWriter/Models/FogLocations.cs
using YamlDotNet.Serialization;

namespace SpeedFogWriter.Models;

public class FogLocations
{
    public List<object> Items { get; set; } = new();
    public List<EnemyArea> EnemyAreas { get; set; } = new();
    public List<object> Enemies { get; set; } = new();

    public static FogLocations Load(string path)
    {
        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<FogLocations>(reader);
    }
}

public class EnemyArea
{
    public string Name { get; set; } = "";
    public string? Groups { get; set; }
    public string? Cols { get; set; }
    public string? MainMap { get; set; }
    public int ScalingTier { get; set; }

    public List<int> GetGroups() =>
        Groups?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse).ToList() ?? new();

    public List<string> GetCols() =>
        Cols?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new();

    public List<string> GetMainMaps() =>
        MainMap?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new();
}
