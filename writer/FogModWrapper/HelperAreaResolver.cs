using FogMod;
using SoulsFormats;
using static FogMod.AnnotationData;

namespace FogModWrapper;

/// <summary>
/// Fixes enemy scaling for helper enemies created by the enemy randomizer
/// (RandomizerCommon) before FogMod's writer runs.
///
/// FogMod resolves each enemy's scaling area by part name (foglocations2.txt
/// Enemies), then entity group, then collision, then the map's default area
/// (MainMap fallback, GameDataWriterE.cs:2081-2098). Randomizer-created
/// helpers (e.g. the Godskin Duo respawning backups) defeat all three
/// specific lookups: their part names are new, the transplant clears the
/// vanilla boss group and replaces it with a randomizer-allocated one, and
/// some boss arenas (volcano_rykard) declare Groups only, no Cols. They fall
/// through to the map default area, whose DAG tier can be wildly different
/// from the arena's (tier 4 vs 15 on a real seed).
///
/// The boss slot parts themselves keep their vanilla part names, so they
/// still resolve by name. This pass propagates that resolution: any part
/// that is unresolvable by name or known groups but shares a non-vanilla
/// entity group with a name-resolved boss slot gets an EnemyLoc entry
/// pointing at the boss arena. FogMod's name lookup (highest priority) then
/// treats it like any vanilla boss part: arena tier, unique boss scaling.
/// See docs/item-randomizer.md, section "Helper enemy scaling".
/// </summary>
public static class HelperAreaResolver
{
    // CollisionName is intentionally unused by the decision logic (see
    // ComputeAdditions); it is carried so the contract mirrors the MSB data
    // and tests can document the deliberate collision override.
    public sealed record EnemyPart(string Name, IReadOnlyList<uint> Groups, string? CollisionName);

    /// <summary>
    /// Scans the merge directory's MSBs (the maps the item/enemy randomizer
    /// actually modified) and appends EnemyLoc entries to
    /// <paramref name="ann"/>.Locations.Enemies for randomizer helper parts.
    /// Must run before GameDataWriterE.Write and after area tiers are applied
    /// to <paramref name="graph"/>.
    /// </summary>
    /// <returns>The number of entries added.</returns>
    public static int Resolve(AnnotationData ann, Graph graph, string mergeDir, Action<string> log)
    {
        var msbDir = Path.Combine(mergeDir, "map", "mapstudio");
        if (!Directory.Exists(msbDir) || ann.Locations == null)
            return 0;

        bool isEligibleBossArea(string area) =>
            graph.Areas.TryGetValue(area, out var a)
            && a.DefeatFlag > 0
            && graph.AreaTiers != null
            && graph.AreaTiers.ContainsKey(area);

        var eligibleMaps = EligibleMaps(ann.Locations, isEligibleBossArea);
        int total = 0, maps = 0;
        foreach (var msbPath in Directory.EnumerateFiles(msbDir, "*.msb.dcx").Order())
        {
            var map = Path.GetFileName(msbPath)[..^".msb.dcx".Length];
            if (!eligibleMaps.Contains(map))
                continue;
            MSBE msb;
            try
            {
                msb = MSBE.Read(msbPath);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Failed to parse merge-dir MSB {msbPath}", e);
            }
            var parts = msb.Parts.Enemies
                .Select(e => new EnemyPart(e.Name, e.EntityGroupIDs, e.CollisionPartName))
                .ToList();
            maps++;

            var added = ComputeAdditions(map, parts, ann.Locations, isEligibleBossArea);
            foreach (var loc in added)
            {
                // Appending also feeds FogMod's per-area boss-representative
                // selection, but vanilla slots still win there (it prefers
                // the part whose EntityID matches the area's DefeatFlag).
                ann.Locations.Enemies.Add(loc);
                log($"  Helper area: {map} {loc.ID} -> {loc.ActualArea}");
            }
            total += added.Count;
        }

        log($"HelperAreaResolver: added {total} enemy location entries " +
            $"({maps} maps scanned)");
        return total;
    }

    /// <summary>
    /// Pure core: computes the EnemyLoc entries to add for one map.
    /// A part qualifies when it is not resolvable by FogMod's name or group
    /// lookups and shares a non-vanilla entity group with a part that
    /// resolves by name to an eligible boss area. Collisions are deliberately
    /// ignored: the boss-group signal outranks a collision (arena floors can
    /// belong to another area's Cols), and name entries win over collisions
    /// in FogMod's resolution order anyway.
    /// </summary>
    public static List<EnemyLoc> ComputeAdditions(
        string map,
        IReadOnlyList<EnemyPart> parts,
        FogLocations locations,
        Func<string, bool> isEligibleBossArea)
    {
        // Groups already declared on some area resolve via FogMod's group
        // lookup; parts carrying them need no help. Also collect area names:
        // FogMod indexes EnemyAreas by name and would throw on an EnemyLoc
        // pointing at an area with no EnemyLocArea entry.
        var knownGroups = new HashSet<uint>();
        var enemyAreaNames = new HashSet<string>();
        foreach (var area in locations.EnemyAreas)
        {
            enemyAreaNames.Add(area.Name);
            foreach (var group in SplitIds(area.Groups))
                knownGroups.Add(group);
        }

        var locByName = new Dictionary<string, EnemyLoc>();
        foreach (var loc in locations.Enemies)
        {
            if (loc.Map == map)
                locByName.TryAdd(loc.ID, loc);
        }

        // Non-vanilla group -> boss area of the name-resolved part carrying
        // it. A group seen on slots of two different areas is ambiguous and
        // dropped (null).
        var bossGroups = new Dictionary<uint, string?>();
        foreach (var part in parts)
        {
            if (!locByName.TryGetValue(part.Name, out var loc))
                continue;
            var area = loc.ActualArea;
            if (!isEligibleBossArea(area) || !enemyAreaNames.Contains(area))
                continue;
            foreach (var group in part.Groups)
            {
                if (group == 0 || knownGroups.Contains(group))
                    continue;
                if (bossGroups.TryGetValue(group, out var existing))
                {
                    if (existing != area)
                        bossGroups[group] = null;
                }
                else
                {
                    bossGroups[group] = area;
                }
            }
        }

        // FogMod builds its name dictionary with ToDictionary, which throws
        // on duplicate (Map, ID) pairs; never emit the same part name twice.
        var emitted = new HashSet<string>();
        var added = new List<EnemyLoc>();
        foreach (var part in parts)
        {
            if (locByName.ContainsKey(part.Name) || emitted.Contains(part.Name))
                continue;
            if (part.Groups.Any(g => g != 0 && knownGroups.Contains(g)))
                continue;

            var area = part.Groups
                .Where(g => g != 0)
                .Select(g => bossGroups.GetValueOrDefault(g))
                .FirstOrDefault(a => a != null);
            if (area == null)
                continue;

            emitted.Add(part.Name);
            added.Add(new EnemyLoc { Map = map, ID = part.Name, Area = area });
        }
        return added;
    }

    /// <summary>
    /// Maps that can yield additions: those with at least one name-resolvable
    /// enemy in an eligible boss area. Helpers are always created in the same
    /// MSB as the boss slot they belong to, so scanning other maps (hundreds
    /// of open-world tiles in a typical merge dir) is wasted work.
    /// </summary>
    public static HashSet<string> EligibleMaps(
        FogLocations locations, Func<string, bool> isEligibleBossArea)
    {
        return locations.Enemies
            .Where(l => isEligibleBossArea(l.ActualArea))
            .Select(l => l.Map)
            .ToHashSet();
    }

    private static IEnumerable<uint> SplitIds(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            yield break;
        foreach (var token in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(token, out var id))
                yield return id;
        }
    }
}
