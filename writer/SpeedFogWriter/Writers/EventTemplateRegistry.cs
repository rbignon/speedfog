// writer/SpeedFogWriter/Writers/EventTemplateRegistry.cs
using SpeedFogWriter.Models;
using SpeedFogWriter.Parsers;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Unified registry for event templates loaded from FogRando's fogevents.txt.
/// Converts FogRando's NewEvent format to our EventTemplate format.
/// </summary>
public class EventTemplateRegistry
{
    private readonly Dictionary<string, EventTemplate> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, EventTemplate> _byId = new();

    /// <summary>
    /// Get a template by name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Template not found</exception>
    public EventTemplate GetByName(string name)
    {
        if (!_byName.TryGetValue(name, out var template))
            throw new KeyNotFoundException($"Event template '{name}' not found in registry");
        return template;
    }

    /// <summary>
    /// Get a template by event ID.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Template not found</exception>
    public EventTemplate GetById(int id)
    {
        if (!_byId.TryGetValue(id, out var template))
            throw new KeyNotFoundException($"Event template with ID {id} not found in registry");
        return template;
    }

    /// <summary>
    /// Try to get a template by name.
    /// </summary>
    public bool TryGetByName(string name, out EventTemplate? template) =>
        _byName.TryGetValue(name, out template);

    /// <summary>
    /// Try to get a template by event ID.
    /// </summary>
    public bool TryGetById(int id, out EventTemplate? template) =>
        _byId.TryGetValue(id, out template);

    /// <summary>
    /// Check if a template with the given name exists.
    /// </summary>
    public bool HasTemplate(string name) => _byName.ContainsKey(name);

    /// <summary>
    /// Get all templates in the registry.
    /// </summary>
    public IEnumerable<EventTemplate> GetAllTemplates() => _byId.Values;

    /// <summary>
    /// Register a template in the registry.
    /// If a template with the same name exists, it will be overwritten.
    /// </summary>
    private void Register(EventTemplate template)
    {
        if (template.Name != null)
            _byName[template.Name] = template;
        _byId[template.Id] = template;
    }

    /// <summary>
    /// Load templates from FogRando's fogevents.txt.
    /// </summary>
    /// <param name="fogEventsPath">Path to fogevents.txt</param>
    /// <returns>Populated registry</returns>
    public static EventTemplateRegistry Load(string fogEventsPath)
    {
        var registry = new EventTemplateRegistry();

        // Load FogRando NewEvents
        var fogConfig = FogEventsParser.Load(fogEventsPath);
        foreach (var evt in fogConfig.NewEvents.Where(e => e.Commands != null && e.Commands.Count > 0))
        {
            var template = new EventTemplate
            {
                Id = evt.ID,
                Name = evt.Name,
                Restart = evt.HasTag("restart"),
                Commands = StripComments(evt.Commands!)
            };
            registry.Register(template);
        }

        return registry;
    }

    /// <summary>
    /// Strip inline comments (// ...) from commands and filter empty lines.
    /// FogRando uses "// comment" for inline comments in fogevents.txt.
    /// </summary>
    private static List<string> StripComments(List<string> commands)
    {
        return commands
            .Select(cmd =>
            {
                // Remove inline comments
                var idx = cmd.IndexOf("//", StringComparison.Ordinal);
                return idx >= 0 ? cmd.Substring(0, idx).Trim() : cmd.Trim();
            })
            .Where(cmd => !string.IsNullOrWhiteSpace(cmd))
            .ToList();
    }
}
