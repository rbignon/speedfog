// writer/SpeedFogWriter/Writers/EventBuilder.cs
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Builds EMEVD events from YAML templates.
/// Uses SoulsIds Events class for instruction parsing.
/// </summary>
public class EventBuilder
{
    private readonly SpeedFogEventConfig _config;
    private readonly Events _events;

    public EventBuilder(SpeedFogEventConfig config, Events events)
    {
        _config = config;
        _events = events;
    }

    /// <summary>
    /// Get a template's event ID.
    /// </summary>
    public int GetTemplateId(string templateName)
    {
        return _config.GetTemplate(templateName).Id;
    }

    /// <summary>
    /// Create an EMEVD event from a template.
    /// The event uses parameter notation (X0_4, etc.) for runtime substitution.
    /// </summary>
    /// <param name="templateName">Template name (e.g., "scale", "fogwarp_simple")</param>
    /// <param name="eventId">Event ID to use (or null to use template's ID)</param>
    /// <returns>EMEVD event with parameterized instructions</returns>
    public EMEVD.Event BuildTemplateEvent(string templateName, long? eventId = null)
    {
        var template = _config.GetTemplate(templateName);
        var id = eventId ?? template.Id;

        var restartType = template.Restart
            ? EMEVD.Event.RestBehaviorType.Restart
            : EMEVD.Event.RestBehaviorType.Default;

        var evt = new EMEVD.Event(id, restartType);

        foreach (var commandStr in template.Commands)
        {
            // Skip comment-only lines
            if (commandStr.TrimStart().StartsWith("#"))
                continue;

            // Parse using SoulsIds Events (handles X0_4 notation)
            var (instruction, parameters) = _events.ParseAddArg(commandStr, evt.Instructions.Count);
            evt.Instructions.Add(instruction);
            evt.Parameters.AddRange(parameters);
        }

        return evt;
    }

    /// <summary>
    /// Create an InitializeEvent instruction for a template event.
    /// </summary>
    /// <param name="templateName">Template name</param>
    /// <param name="slot">Event slot (usually 0)</param>
    /// <param name="args">Arguments to pass to the event (in template param order)</param>
    /// <returns>EMEVD instruction for InitializeEvent</returns>
    public EMEVD.Instruction BuildInitializeEvent(string templateName, int slot, params object[] args)
    {
        var template = _config.GetTemplate(templateName);

        // Build argument list: slot, eventId, then template args
        var fullArgs = new List<object> { slot, template.Id };
        fullArgs.AddRange(args);

        // Instruction 2000[00] = InitializeEvent
        return new EMEVD.Instruction(2000, 0, fullArgs);
    }

    /// <summary>
    /// Create an InitializeEvent instruction with explicit event ID.
    /// </summary>
    public EMEVD.Instruction BuildInitializeEventById(int eventId, int slot, params object[] args)
    {
        var fullArgs = new List<object> { slot, eventId };
        fullArgs.AddRange(args);
        return new EMEVD.Instruction(2000, 0, fullArgs);
    }

    /// <summary>
    /// Get all template events that should be registered in common_func.
    /// </summary>
    public IEnumerable<EMEVD.Event> GetAllTemplateEvents()
    {
        foreach (var (name, template) in _config.Templates)
        {
            // Skip special templates that aren't meant to be events
            if (name == "common_init")
                continue;

            yield return BuildTemplateEvent(name);
        }
    }

    /// <summary>
    /// Get default value from config.
    /// </summary>
    public int GetDefaultInt(string name, int fallback = 0)
    {
        return _config.GetDefault(name, fallback);
    }
}
