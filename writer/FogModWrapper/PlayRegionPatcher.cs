using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Restores the vanilla constant flags on PlayRegionParam rows that FogMod
/// needlessly remapped.
///
/// PlayRegionParam.pcPositionSaveLimitEventFlagId gates the engine's player
/// position save (flag ON = save enabled, OFF = disabled, 0 = always on).
/// FogMod remaps every nonzero value to a temp flag driven by its
/// common_makestable event. The boss-defeat-gated rows keep that stock
/// behavior (FogRando parity; the pulse is meant to anchor the arena
/// entrance after a fog gate warp but rarely captures, see
/// docs/quitout-respawn.md), while the remap is pointless for the
/// rows whose vanilla flag is the constant 6001 (always ON, set by vanilla
/// common.emevd Event 50 at every load): row 0, the default region for all
/// ground in the game, plus the DLC overworld rows. Remapping those onto a
/// temporary flag (2xxx offset, wiped on area reload) trades a permanently
/// ON gate for one re-asserted by EMEVD timing, for no benefit.
///
/// This patcher puts 6001 (and the single always-OFF 6000 row, which FogMod
/// inverts for 10 frames per load) back, so only boss-gated rows keep
/// FogMod's makestable behavior. See docs/quitout-respawn.md.
/// </summary>
public static class PlayRegionPatcher
{
    /// <summary>Vanilla constant flag, always ON (common.emevd Event 50).</summary>
    private const uint ALWAYS_ON_FLAG = 6001;

    /// <summary>Vanilla constant flag, always OFF (common.emevd Event 50).</summary>
    private const uint ALWAYS_OFF_FLAG = 6000;

    private const string FIELD = "pcPositionSaveLimitEventFlagId";

    /// <summary>
    /// Copy the vanilla pcPositionSaveLimitEventFlagId back onto every modded
    /// row whose vanilla value is one of the constant flags (6000/6001).
    /// Boss-defeat-gated rows are left untouched.
    /// </summary>
    /// <returns>Number of rows restored</returns>
    public static int Restore(PARAM modded, PARAM vanilla)
    {
        var constants = new Dictionary<int, uint>();
        foreach (var row in vanilla.Rows)
        {
            uint flag = (uint)row[FIELD].Value;
            if (flag == ALWAYS_ON_FLAG || flag == ALWAYS_OFF_FLAG)
                constants[row.ID] = flag;
        }

        int restored = 0;
        foreach (var row in modded.Rows)
        {
            if (constants.TryGetValue(row.ID, out uint flag))
            {
                row[FIELD].Value = flag;
                restored++;
            }
        }
        return restored;
    }

    /// <summary>
    /// Open the vanilla regulation from the game directory and restore the
    /// constant-flag rows in the modded regulation. No-op (with a warning)
    /// when either regulation or param is unavailable.
    /// </summary>
    public static void ApplyTo(RegulationEditor modded, string gameDir)
    {
        // Resolve the vanilla side first: GetParam caches the param on the
        // editor and Save() re-serializes every cached param, so the modded
        // PlayRegionParam must not be touched on the warn+skip paths.
        if (!File.Exists(Path.Combine(gameDir, "regulation.bin")))
        {
            Console.WriteLine("Warning: vanilla regulation.bin not found in game dir, skipping play region restore");
            return;
        }

        // Read-only: never call Save() on this editor, it points at the
        // player's game directory.
        var vanillaReg = RegulationEditor.Open(gameDir);
        var vanillaParam = vanillaReg?.GetParam("PlayRegionParam");
        if (vanillaParam == null)
        {
            Console.WriteLine("Warning: vanilla PlayRegionParam unavailable, skipping play region restore");
            return;
        }

        var moddedParam = modded.GetParam("PlayRegionParam");
        if (moddedParam == null)
        {
            Console.WriteLine("Warning: PlayRegionParam missing from modded regulation, skipping play region restore");
            return;
        }

        int restored = Restore(moddedParam, vanillaParam);
        Console.WriteLine($"Play region restore: reset {restored} constant-flag rows (6000/6001)");
    }
}
