using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects the phantom skins catalog into regulation.bin params. Each skin
/// becomes three rows (PhantomParam + SpEffectVfxParam + SpEffectParam) sharing
/// the same id. The visual is an "aura only" effect: edge color is set, all
/// other color components are zeroed to keep the player's body and equipment
/// untinted.
///
/// Pattern adapted from RandomizerCommon's EnemyRandomizer.cs:1740-1771,
/// which uses the same template ids and field set in production.
/// </summary>
public static class PhantomCatalogInjector
{
    private const int PhantomTemplateId = 260;
    private const int SpEffectVfxTemplateId = 51508;
    private const int SpEffectTemplateId = 13177;

    // PhantomParam fields that must be zeroed on aura-only skins so template
    // defaults do not tint the player model.
    private static readonly string[] AuraZeroByteFields =
    {
        "frontColorR", "frontColorG", "frontColorB",
        "diffMulColorR", "diffMulColorG", "diffMulColorB",
        "specMulColorR", "specMulColorG", "specMulColorB",
        "lightColorR", "lightColorG", "lightColorB",
    };

    private static readonly string[] AuraZeroFloatFields =
    {
        "frontColorA", "diffMulColorA", "specMulColorA", "lightColorA",
    };

    public static void ApplyTo(RegulationEditor reg, IReadOnlyList<PhantomSkin> skins)
    {
        if (skins.Count == 0)
        {
            Console.WriteLine("Phantom skins: empty catalog, skipping injection");
            return;
        }

        var phantomParam = reg.GetParam("PhantomParam");
        var vfxParam = reg.GetParam("SpEffectVfxParam", "SpEffectVfx");
        var spParam = reg.GetParam("SpEffectParam", "SpEffect");

        if (phantomParam == null || vfxParam == null || spParam == null)
        {
            Console.WriteLine("Phantom skins: missing one or more required params, skipping");
            return;
        }

        Apply(phantomParam, vfxParam, spParam, skins);
    }

    /// <summary>
    /// Lower-level entry point used by tests. Operates on already-loaded PARAMs.
    /// </summary>
    public static void Apply(
        PARAM phantomParam,
        PARAM vfxParam,
        PARAM spParam,
        IReadOnlyList<PhantomSkin> skins)
    {
        if (skins.Count == 0)
            return;

        foreach (var skin in skins)
        {
            AddPhantomRow(phantomParam, skin);
            AddVfxRow(vfxParam, skin);
            AddSpEffectRow(spParam, skin);
        }

        Console.WriteLine($"Phantom skins: injected {skins.Count} skin(s) into PhantomParam/SpEffectVfxParam/SpEffectParam");
    }

    private static void AddPhantomRow(PARAM phantomParam, PhantomSkin skin)
    {
        var row = GameEditor.AddRow(phantomParam, skin.Id, PhantomTemplateId);

        row["edgeColorR"].Value = skin.EdgeColorR;
        row["edgeColorG"].Value = skin.EdgeColorG;
        row["edgeColorB"].Value = skin.EdgeColorB;
        row["edgeColorA"].Value = skin.Alpha;
        row["edgePower"].Value = skin.EdgePower;
        row["glowScale"].Value = skin.GlowScale;
        row["alpha"].Value = skin.Alpha;

        // Aura-only: prevent the template defaults from tinting the player model.
        foreach (var field in AuraZeroByteFields)
            row[field].Value = (byte)0;
        foreach (var field in AuraZeroFloatFields)
            row[field].Value = 0f;
    }

    private static void AddVfxRow(PARAM vfxParam, PhantomSkin skin)
    {
        var row = GameEditor.AddRow(vfxParam, skin.Id, SpEffectVfxTemplateId);
        row["phantomParamOverwriteId"].Value = skin.Id;
    }

    private static void AddSpEffectRow(PARAM spParam, PhantomSkin skin)
    {
        var row = GameEditor.AddRow(spParam, skin.Id, SpEffectTemplateId);
        row["vfxId"].Value = skin.Id;
    }
}
