using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ItemRandomizerWrapper;

public class BossPlacement
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("entity_id")]
    public long EntityId { get; set; }
}

public static class BossPlacementParser
{
    // Matches: "Replacing {name} (#{target_id}) in {loc}: {source_name} (#{source_id}) from {loc}..."
    private static readonly Regex ReplacingPattern = new(
        @"^Replacing .+ \(#(\d+)\) in .+: (.+) \(#(\d+)\) from ",
        RegexOptions.Compiled);

    public static Dictionary<string, BossPlacement> Parse(IEnumerable<string> lines)
    {
        var placements = new Dictionary<string, BossPlacement>();

        foreach (var line in lines)
        {
            var match = ReplacingPattern.Match(line);
            if (!match.Success) continue;

            var targetId = match.Groups[1].Value;
            var sourceName = match.Groups[2].Value;
            var sourceId = long.Parse(match.Groups[3].Value);

            placements[targetId] = new BossPlacement
            {
                Name = sourceName,
                EntityId = sourceId,
            };
        }

        return placements;
    }

    public static string Serialize(Dictionary<string, BossPlacement> placements)
    {
        return JsonSerializer.Serialize(placements, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
