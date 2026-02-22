using FogMod;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Fixes Maliketh's post-defeat warp when connecting to Ashen Leyndell.
///
/// Event 900 (Maliketh defeat in common.emevd.dcx) sets flag 300 (Erdtree burning),
/// which causes the game to load m11_05_00_00 (Ashen Capital) instead of m11_00_00_00
/// (pre-ash Leyndell). FogMod replaces the warp destination with the entrance's primary
/// map (m11_00_00_00), but the warp region doesn't exist in the Ashen map, so the warp
/// fails silently.
///
/// FogMod's AlternateSide mechanism computes warp points for BOTH map versions.
/// This injector patches PlayCutsceneToPlayerAndWarp instructions to use the alternate
/// (Ashen) warp destination instead of the primary (pre-ash) one.
/// </summary>
public static class AshenLeyndellWarpInjector
{
    /// <summary>
    /// Patch PlayCutsceneToPlayerAndWarp instructions in common.emevd.dcx that target
    /// pre-ash Leyndell (m11_00_00_00) with a FogMod region to use the Ashen Capital
    /// (m11_05_00_00) alternate warp instead.
    /// </summary>
    /// <param name="modDir">Path to mod output directory (contains event/ subdirectory)</param>
    /// <param name="primaryWarp">FogMod's warp point for pre-ash m11_00_00_00</param>
    /// <param name="altWarp">FogMod's warp point for Ashen m11_05_00_00</param>
    public static void Inject(
        string modDir,
        Graph.WarpPoint primaryWarp,
        Graph.WarpPoint altWarp)
    {
        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine("Warning: common.emevd.dcx not found, skipping Ashen Leyndell warp fix");
            return;
        }

        int primaryRegion = primaryWarp.Region;
        int primaryMapPacked = PackMapId(primaryWarp.Map);
        int altRegion = altWarp.Region;
        int altMapPacked = PackMapId(altWarp.Map);

        var emevd = EMEVD.Read(emevdPath);
        int patched = 0;

        foreach (var evt in emevd.Events)
        {
            foreach (var instr in evt.Instructions)
            {
                // PlayCutsceneToPlayerAndWarp (2002:11) / PlayCutsceneToPlayerAndWarpWithWeatherAndTime (2002:12)
                // ArgData layout: [cutsceneId(4), playback(4), region(4), mapId(4), ...]
                if (instr.Bank != 2002 || (instr.ID != 11 && instr.ID != 12))
                    continue;
                if (instr.ArgData.Length < 16)
                    continue;

                int region = BitConverter.ToInt32(instr.ArgData, 8);
                int mapInt = BitConverter.ToInt32(instr.ArgData, 12);

                if (region == primaryRegion && mapInt == primaryMapPacked)
                {
                    BitConverter.GetBytes(altRegion).CopyTo(instr.ArgData, 8);
                    BitConverter.GetBytes(altMapPacked).CopyTo(instr.ArgData, 12);
                    patched++;
                }
            }
        }

        if (patched > 0)
        {
            emevd.Write(emevdPath);
            Console.WriteLine($"Patched {patched} warp(s) from {primaryWarp.Map} to {altWarp.Map} (Ashen Leyndell fix)");
        }
        else
        {
            Console.WriteLine($"Warning: No PlayCutsceneToPlayerAndWarp matched for Ashen Leyndell fix " +
                $"(expected region {primaryRegion}, map {primaryMapPacked})");
        }
    }

    /// <summary>
    /// Pack a map name string into a decimal int.
    /// "m11_05_00_00" → 11050000
    /// Format: area*1000000 + block*10000 + sub*100 + sub2
    /// </summary>
    private static int PackMapId(string map)
    {
        var parts = map.TrimStart('m').Split('_');
        return int.Parse(parts[0]) * 1000000
             + int.Parse(parts[1]) * 10000
             + int.Parse(parts[2]) * 100
             + int.Parse(parts[3]);
    }
}
