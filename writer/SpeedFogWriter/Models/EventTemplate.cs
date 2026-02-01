// writer/SpeedFogWriter/Models/EventTemplate.cs
using YamlDotNet.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// A single event template from speedfog-events.yaml.
/// </summary>
public class EventTemplate
{
    /// <summary>
    /// Event ID for this template (e.g., 79000001 for scale).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Whether the event should restart after completion.
    /// </summary>
    public bool Restart { get; set; }

    /// <summary>
    /// Parameter definitions mapping name to position notation (e.g., "entity_id": "X0_4").
    /// </summary>
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>
    /// List of EMEVD commands with parameter placeholders.
    /// </summary>
    public List<string> Commands { get; set; } = new();
}

/// <summary>
/// Root configuration loaded from speedfog-events.yaml.
/// </summary>
public class SpeedFogEventConfig
{
    /// <summary>
    /// Named event templates (scale, showsfx, fogwarp_simple, etc.).
    /// </summary>
    public Dictionary<string, EventTemplate> Templates { get; set; } = new();

    /// <summary>
    /// Default values for common parameters.
    /// </summary>
    public Dictionary<string, object> Defaults { get; set; } = new();

    /// <summary>
    /// Load configuration from YAML file.
    /// </summary>
    public static SpeedFogEventConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<SpeedFogEventConfig>(yaml)
            ?? throw new InvalidOperationException($"Failed to parse {path}");
    }

    /// <summary>
    /// Get a template by name.
    /// </summary>
    public EventTemplate GetTemplate(string name)
    {
        if (!Templates.TryGetValue(name, out var template))
            throw new KeyNotFoundException($"Event template '{name}' not found");
        return template;
    }

    /// <summary>
    /// Get default value for a parameter.
    /// </summary>
    public T GetDefault<T>(string name, T fallback)
    {
        if (Defaults.TryGetValue(name, out var value))
        {
            if (value is T typed)
                return typed;
            // Try conversion for numeric types
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return fallback;
            }
        }
        return fallback;
    }
}
