namespace FogModWrapper;

/// <summary>
/// Ships the snapshot (eldendata/Vanilla) MSB and EMEVD of maps listed under
/// [[pin_vanilla_maps]] in data/game_tweaks.toml when neither FogMod nor the
/// Item Randomizer wrote them. A map the mod does not override runs from the
/// player's own install, whatever version that is: the Elden Ring 1.17 NPC
/// invasion in the Radahn arena lives in m60_52_39_00, a map without fog
/// gates or randomized items, so the seed shipped nothing for it and pack
/// owners got the invasion from their 1.17 files. Pinning the snapshot copy
/// (1.16 today: no enemy, an empty EMEVD) overrides that. Files FogMod wrote
/// are never replaced, and files present in the merge dir are skipped: the
/// seed loads mods/itemrando after mods/fogmod, so a pinned copy would
/// shadow the Item Randomizer's edits for that map.
/// </summary>
public static class VanillaMapPinner
{
    /// <summary>Copy missing map files from vanillaDir into modDir; returns the number of files copied.</summary>
    public static int Pin(string modDir, string vanillaDir, IEnumerable<string> maps, string? mergeDir = null)
    {
        int copied = 0;
        foreach (var mapId in maps)
        {
            var msbName = $"{mapId}.msb.dcx";
            var emevdName = $"{mapId}.emevd.dcx";
            var candidates = new[]
            {
                (fileName: msbName,
                 existing: MsbHelper.FindMsbPath(modDir, msbName),
                 merged: mergeDir != null ? MsbHelper.FindMsbPath(mergeDir, msbName) : null,
                 target: MsbHelper.FindOrCreateMsbDir(modDir, msbName)),  // returns the file path
                (fileName: emevdName,
                 existing: ExistingOrNull(Path.Combine(modDir, "event", emevdName)),
                 merged: mergeDir != null ? ExistingOrNull(Path.Combine(mergeDir, "event", emevdName)) : null,
                 target: Path.Combine(modDir, "event", emevdName)),
            };
            foreach (var c in candidates)
            {
                var source = Path.Combine(vanillaDir, c.fileName);
                if (!File.Exists(source))
                {
                    Console.WriteLine($"Pin vanilla maps: {c.fileName} not in the snapshot, skipping");
                    continue;
                }
                if (c.existing != null)
                {
                    Console.WriteLine($"Pin vanilla maps: {c.fileName} already written by the mod, kept");
                    continue;
                }
                if (c.merged != null)
                {
                    Console.WriteLine($"Pin vanilla maps: {c.fileName} written by the Item Randomizer (merge dir), not pinned");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(c.target)!);
                File.Copy(source, c.target);
                copied++;
                Console.WriteLine($"Pin vanilla maps: shipped snapshot {c.fileName}");
            }
        }
        return copied;
    }

    private static string? ExistingOrNull(string path) => File.Exists(path) ? path : null;
}
