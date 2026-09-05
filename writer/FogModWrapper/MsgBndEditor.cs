using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Shared msgbnd editing for the FMG injectors that author text in English
/// and French only (TextTheme, BossNameInjector). Edits layer on the mod
/// copy of a bnd when one exists (FogMod's engus output, the Item
/// Randomizer's localized copies, an earlier injector) and start from the
/// vanilla bnd otherwise; the mod copy is written only when the edit
/// reports changes.
/// </summary>
public static class MsgBndEditor
{
    /// <summary>Languages the injectors author for: engus and frafr. Other
    /// languages keep their vanilla text (touching all ~15 tripled the
    /// per-seed cost for no benefit).</summary>
    public static readonly HashSet<string> TargetLanguages = new() { "engus", "frafr" };

    /// <summary>frafr gets fr when present; every other language (incl. engus) gets en.</summary>
    public static string LocalizedText(string langName, string en, string? fr)
        => langName == "frafr" && !string.IsNullOrEmpty(fr) ? fr! : en;

    /// <summary>Reads the source bnd (mod copy if present, else vanilla from
    /// <paramref name="langDir"/>), runs <paramref name="edit"/>, and writes
    /// the mod copy only when it reports changes. Returns the edit count.</summary>
    public static int EditBnd(string modDir, string langDir, string lang, string bndName,
        Func<BND4, int> edit)
    {
        var vanillaPath = Path.Combine(langDir, bndName);
        if (!File.Exists(vanillaPath))
            return 0;

        var modPath = Path.Combine(modDir, "msg", lang, bndName);
        var sourcePath = File.Exists(modPath) ? modPath : vanillaPath;

        var bnd = BND4.Read(sourcePath);
        int n = edit(bnd);
        if (n == 0)
            return 0;

        Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
        bnd.Write(modPath);
        return n;
    }
}
