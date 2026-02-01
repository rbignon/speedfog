// writer/SpeedFogWriter/Models/EventTemplate.cs
namespace SpeedFogWriter.Models;

/// <summary>
/// A single event template loaded from FogRando's fogevents.txt.
/// </summary>
public class EventTemplate
{
    /// <summary>
    /// Event ID for this template (e.g., 9005770 for scale).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Template name (e.g., "scale", "showsfx", "fogwarp").
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether the event should restart after completion.
    /// </summary>
    public bool Restart { get; set; }

    /// <summary>
    /// List of EMEVD commands with parameter placeholders (X0_4, etc.).
    /// </summary>
    public List<string> Commands { get; set; } = new();
}
