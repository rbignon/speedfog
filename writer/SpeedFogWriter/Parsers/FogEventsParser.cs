// writer/SpeedFogWriter/Parsers/FogEventsParser.cs
using SpeedFogWriter.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpeedFogWriter.Parsers;

/// <summary>
/// Parser for FogRando's fogevents.txt file.
/// This file is YAML format with sections: NewEvents, WarpArgs, Events.
/// We only care about NewEvents (reusable event templates).
/// </summary>
public static class FogEventsParser
{
    /// <summary>
    /// Load and parse fogevents.txt from the given path.
    /// Only the NewEvents section is parsed; WarpArgs and Events are ignored.
    /// </summary>
    public static FogEventConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()  // Ignore WarpArgs, Events sections
            .Build();

        return deserializer.Deserialize<FogEventConfig>(yaml)
            ?? throw new InvalidOperationException($"Failed to parse fogevents.txt: {path}");
    }
}
