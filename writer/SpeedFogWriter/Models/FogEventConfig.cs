// writer/SpeedFogWriter/Models/FogEventConfig.cs
namespace SpeedFogWriter.Models;

/// <summary>
/// Represents a single event from FogRando's fogevents.txt NewEvents section.
/// </summary>
public class NewEvent
{
    /// <summary>
    /// Event ID (e.g., 9005770 for scale, 755850280 for common_fingerstart).
    /// </summary>
    public int ID { get; set; }

    /// <summary>
    /// Template name (e.g., "scale", "showsfx", "fogwarp").
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Description of the event and its parameters.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// List of EMEVD commands with parameter placeholders (X0_4, etc.).
    /// May contain inline comments with "//".
    /// </summary>
    public List<string>? Commands { get; set; }

    /// <summary>
    /// Comma-separated tags (e.g., "restart").
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Check if this event has a specific tag.
    /// </summary>
    public bool HasTag(string tag) =>
        Tags?.Split(',').Any(t => t.Trim().Equals(tag, StringComparison.OrdinalIgnoreCase)) ?? false;
}

/// <summary>
/// Root configuration loaded from fogevents.txt.
/// Only the NewEvents section is parsed; WarpArgs and Events sections are ignored.
/// </summary>
public class FogEventConfig
{
    /// <summary>
    /// List of event templates from the NewEvents section.
    /// </summary>
    public List<NewEvent> NewEvents { get; set; } = new();
}
