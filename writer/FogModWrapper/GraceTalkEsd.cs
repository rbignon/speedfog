using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Shared loader/writer for the Site of Grace talk script
/// (m00_00_00_00.talkesdbnd.dcx -> t000001000.esd) and its menu state machine.
///
/// The grace menu is edited by several post-processing injectors (RebirthInjector,
/// ShadowRealmBlessingRemover). They all load the same BND, anchor the same machine
/// via the "Memorize spell" talk data, edit it, and write back to the mod directory.
/// This centralizes that boilerplate. See docs/esd-editing.md.
/// </summary>
public sealed class GraceTalkEsd
{
    // Grace menu anchor: the "Memorize spell" talk data uniquely identifies the
    // single state machine that drives the Site of Grace menu (FogRando uses the
    // same anchor in GameDataWriterE.cs:3901).
    public const int MemorizeSpellMsg = 15000390;

    private const string BndFileName = "m00_00_00_00.talkesdbnd.dcx";

    // Talk script directory variants (vanilla=PascalCase, FogMod under Wine=lowercase).
    private static readonly string[] TalkDirVariants = { "talk", "Talk" };

    private readonly BND4 _bnd;
    private readonly BinderFile _binderFile;
    private readonly string _modDir;

    /// <summary>The parsed grace talk ESD.</summary>
    public ESD Esd { get; }

    /// <summary>The grace menu state machine (states keyed by id).</summary>
    public Dictionary<long, ESD.State> GraceMachine { get; }

    private GraceTalkEsd(BND4 bnd, BinderFile binderFile, ESD esd,
                         Dictionary<long, ESD.State> graceMachine, string modDir)
    {
        _bnd = bnd;
        _binderFile = binderFile;
        Esd = esd;
        GraceMachine = graceMachine;
        _modDir = modDir;
    }

    /// <summary>
    /// Load the grace talk ESD from the mod directory (preferred, so chained
    /// injectors see each other's edits) or the game directory. Returns null and
    /// logs a warning if the BND, the ESD, or the grace machine cannot be resolved.
    /// </summary>
    public static GraceTalkEsd? Load(string modDir, string gameDir)
    {
        var bndPath = FindBnd(modDir) ?? FindBnd(gameDir);
        if (bndPath == null)
        {
            Console.WriteLine($"Warning: {BndFileName} not found, skipping grace menu edit");
            return null;
        }

        var bnd = BND4.Read(bndPath);

        var binderFile = bnd.Files.Find(f => f.Name.Contains("t000001000"));
        if (binderFile == null)
        {
            Console.WriteLine("Warning: t000001000.esd not found in talk BND, skipping grace menu edit");
            return null;
        }

        var esd = ESD.Read(binderFile.Bytes);

        var machines = ESDEdits.FindMachinesWithTalkData(esd, MemorizeSpellMsg);
        if (machines.Count != 1)
        {
            Console.WriteLine($"Warning: Expected 1 grace machine with talk data {MemorizeSpellMsg}, " +
                              $"found {machines.Count}. Skipping grace menu edit.");
            return null;
        }

        return new GraceTalkEsd(bnd, binderFile, esd, esd.StateGroups[machines[0]], modDir);
    }

    /// <summary>
    /// Serialize the edited ESD back into the BND and write it to the mod directory
    /// (lowercase "talk", FogMod convention under Wine).
    /// </summary>
    public void Save()
    {
        _binderFile.Bytes = Esd.Write();

        var writePath = FindBnd(_modDir) ?? Path.Combine(_modDir, "script", "talk", BndFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        _bnd.Write(writePath);
    }

    private static string? FindBnd(string baseDir)
    {
        foreach (var dirName in TalkDirVariants)
        {
            var path = Path.Combine(baseDir, "script", dirName, BndFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
