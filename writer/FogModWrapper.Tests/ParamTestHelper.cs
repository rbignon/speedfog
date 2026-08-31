using SoulsFormats;

namespace FogModWrapper.Tests;

/// <summary>
/// Shared helper for injector tests that need a real in-memory PARAM (with a
/// template row for GameEditor.AddRow to clone from) without loading a full
/// regulation.bin. Defs are read from the eldendata/Defs directory published
/// alongside the test binaries, the same location RegulationEditor reads at
/// runtime.
///
/// Originally private to PhantomCatalogInjectorTests; promoted here when
/// UntouchableBossInjectorTests needed the same construction for a def whose
/// file basename differs from the PARAM name it backs (SpEffect.xml backs
/// SpEffectParam), to avoid a near-verbatim duplicate.
/// </summary>
internal static class ParamTestHelper
{
    public static string DefsDir() =>
        Path.Combine(AppContext.BaseDirectory, "eldendata", "Defs");

    /// <summary>
    /// Loads "&lt;defName&gt;.xml" from the Defs directory, creates an empty
    /// PARAM matching that layout, and adds one row (id
    /// <paramref name="templateId"/>) to serve as the clone source for
    /// GameEditor.AddRow. <paramref name="paramName"/> is accepted for
    /// call-site clarity only, mirroring RegulationEditor.GetParam(name,
    /// defName): it documents which real PARAM the def backs when that
    /// differs from the def file's own basename, but the returned PARAM's
    /// ParamType always comes from the def itself, not from this argument.
    /// </summary>
    public static PARAM BuildParamFromDef(string defName, int templateId, string? paramName = null)
    {
        var defPath = Path.Combine(DefsDir(), $"{defName}.xml");
        var def = PARAMDEF.XmlDeserialize(defPath);
        // Initialize Rows before calling ApplyParamdef so the foreach in
        // ApplyParamdef does not throw on a null collection. RowReader is
        // null for in-memory PARAMs, but that is fine because no rows exist
        // yet at ApplyParamdef time.
        var param = new PARAM { ParamType = def.ParamType, Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        param.Rows.Add(new PARAM.Row(templateId, "", def));
        return param;
    }

    /// <summary>
    /// Adds a second empty row (id <paramref name="rowId"/>) to a PARAM
    /// already built by <see cref="BuildParamFromDef"/>, reusing its applied
    /// paramdef. For tests needing more than one row of the same PARAM (e.g.
    /// two independent EquipParamGoods items), where GameEditor.AddRow's
    /// clone-from-template idiom does not apply because both rows already
    /// exist in the real regulation and are edited in place, not cloned.
    /// </summary>
    public static PARAM.Row AddRowFromTemplate(PARAM param, int rowId)
    {
        var row = new PARAM.Row(rowId, "", param.AppliedParamdef);
        param.Rows.Add(row);
        return row;
    }
}
