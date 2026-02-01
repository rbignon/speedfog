// writer/SpeedFogWriter/Writers/EventBuilder.cs
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Builds EMEVD events from templates loaded from FogRando's fogevents.txt.
/// Uses SoulsIds Events class for instruction parsing.
/// </summary>
public class EventBuilder
{
    private readonly EventTemplateRegistry _registry;
    private readonly Events _events;

    public EventBuilder(EventTemplateRegistry registry, Events events)
    {
        _registry = registry;
        _events = events;
    }

    /// <summary>
    /// Get a template's event ID.
    /// </summary>
    public int GetTemplateId(string templateName)
    {
        return _registry.GetByName(templateName).Id;
    }

    /// <summary>
    /// Check if a template exists in the registry.
    /// </summary>
    public bool HasTemplate(string templateName)
    {
        return _registry.HasTemplate(templateName);
    }

    /// <summary>
    /// Create an EMEVD event from a template.
    /// The event uses parameter notation (X0_4, etc.) for runtime substitution.
    /// </summary>
    /// <param name="templateName">Template name (e.g., "scale", "fogwarp")</param>
    /// <param name="eventId">Event ID to use (or null to use template's ID)</param>
    /// <returns>EMEVD event with parameterized instructions</returns>
    public EMEVD.Event BuildTemplateEvent(string templateName, long? eventId = null)
    {
        var template = _registry.GetByName(templateName);
        var id = eventId ?? template.Id;

        var restartType = template.Restart
            ? EMEVD.Event.RestBehaviorType.Restart
            : EMEVD.Event.RestBehaviorType.Default;

        var evt = new EMEVD.Event(id, restartType);

        foreach (var commandStr in template.Commands)
        {
            // Skip comment-only lines (# style from YAML)
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
    /// Create an EMEVD event from a template by ID.
    /// </summary>
    public EMEVD.Event BuildTemplateEventById(int templateId)
    {
        var template = _registry.GetById(templateId);
        if (template.Name == null)
            throw new InvalidOperationException($"Template {templateId} has no name");
        return BuildTemplateEvent(template.Name);
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
        var template = _registry.GetByName(templateName);

        // Build argument list for InitializeCommonEvent (2000[6]):
        // Format: (slot, eventId, param1, param2, ...)
        // Reference: FogRando GameDataWriterE.cs line 3225
        var fullArgs = new List<object> { slot, template.Id };
        fullArgs.AddRange(args);

        // Instruction 2000[06] = InitializeCommonEvent (for events in common_func.emevd)
        return new EMEVD.Instruction(2000, 6, fullArgs);
    }

    /// <summary>
    /// Create an InitializeEvent instruction with explicit event ID.
    /// </summary>
    public EMEVD.Instruction BuildInitializeEventById(int eventId, int slot, params object[] args)
    {
        // Build argument list for InitializeCommonEvent (2000[6]):
        // Format: (slot, eventId, param1, param2, ...)
        var fullArgs = new List<object> { slot, eventId };
        fullArgs.AddRange(args);
        // Instruction 2000[06] = InitializeCommonEvent (for events in common_func.emevd)
        return new EMEVD.Instruction(2000, 6, fullArgs);
    }

    /// <summary>
    /// Get all template events that should be registered.
    /// Filters based on prefix:
    /// - "common_*" templates go to common.emevd
    /// - Other templates go to common_func.emevd
    /// </summary>
    /// <param name="forCommon">If true, return common_* templates; if false, return non-common templates</param>
    public IEnumerable<EMEVD.Event> GetTemplateEvents(bool forCommon)
    {
        foreach (var template in _registry.GetAllTemplates())
        {
            if (template.Name == null)
                continue;

            bool isCommon = template.Name.StartsWith("common_", StringComparison.OrdinalIgnoreCase);
            if (isCommon != forCommon)
                continue;

            yield return BuildTemplateEvent(template.Name);
        }
    }

    /// <summary>
    /// Get all non-common template events for common_func.emevd.
    /// </summary>
    public IEnumerable<EMEVD.Event> GetAllTemplateEvents() => GetTemplateEvents(forCommon: false);

    /// <summary>
    /// Get all common_* template events for common.emevd.
    /// </summary>
    public IEnumerable<EMEVD.Event> GetCommonTemplateEvents() => GetTemplateEvents(forCommon: true);
}
