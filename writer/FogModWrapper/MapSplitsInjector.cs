using FogMod;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects synthetic zones and fog gates from data/map_splits.toml into
/// FogMod's AnnotationData before Graph.Construct, so FogMod creates the
/// gates (MakeFrom), trigger regions and warps through its standard path.
/// Also splits EnemyAreas by collision so each half of a split map scales
/// with its own tier. Mirrors the Python-side injection in
/// tools/generate_clusters.py (docs/map-splits.md).
/// </summary>
public static class MapSplitsInjector
{
    public static void Apply(AnnotationData ann, MapSplits splits)
    {
        foreach (var zone in splits.Zones)
        {
            if (ann.Areas.Any(a => a.Name == zone.Name))
                throw new InvalidDataException(
                    $"map_splits: zone '{zone.Name}' already exists in fog.txt");
            ann.Areas.Add(new AnnotationData.Area
            {
                Name = zone.Name,
                Text = zone.DisplayName,
                Maps = zone.Map,
                // null, not "": Taggable's Tags setter splits on ' ', so ""
                // would produce TagList = [""] (a phantom empty tag). fog.txt
                // areas with no tags have Tags = null.
                Tags = zone.Tags.Count > 0 ? string.Join(' ', zone.Tags) : null,
            });
            Console.WriteLine($"MapSplits: added area '{zone.Name}' ({zone.Map})");
        }

        // Sides whose area is a boss area (DefeatFlag set) carry
        // BossDefeatName = "area", the universal fog.txt convention for every
        // fog side inside a boss room: GameDataWriterE resolves it to the
        // area's DefeatFlag and gates the side's fogwarp on it
        // (IfEventFlag wait), so the gate only opens outward once the boss is
        // dead. Without it the flag resolves to 0 and the warp is always
        // usable. BossTrapName/BossTriggerName are NOT mirrored: they drive
        // trap-flag locks and MSB trigger regions that only apply to areas
        // declaring those flags in fog.txt.
        // Boss sides also get the "main" tag (unless the area already has a
        // main-tagged side): FogMod resolves a boss area's spawn point
        // through its main entrance side (getMainSpawnPoint), the universal
        // convention on vanilla boss sides.
        bool AreaHasMainSide(string area) =>
            ann.Entrances.Concat(ann.Warps)
                .SelectMany(x => new[] { x.ASide, x.BSide })
                .Any(s => s != null && s.Area == area && s.HasTag("main"));

        AnnotationData.Side MakeSide(string area, string text)
        {
            bool isBoss = ann.Areas.FirstOrDefault(a => a.Name == area)?.DefeatFlag > 0;
            return new()
            {
                Area = area,
                Text = text,
                BossDefeatName = isBoss ? "area" : null,
                Tags = isBoss && !AreaHasMainSide(area) ? "main" : null,
            };
        }

        foreach (var fog in splits.Fogs)
        {
            // FogMod keys entrances by FullName = Area + "_" + Name (see
            // Graph.cs's EntranceIds construction), not by Name alone: the
            // same asset-derived Name (e.g. "AEG099_002_9000") is reused
            // across dozens of unrelated maps in fog.txt. Scope the
            // collision check to the target map to match that invariant.
            if (ann.Entrances.Any(x => x.Name == fog.Name && x.Area == fog.Map)
                || ann.Warps.Any(x => x.Name == fog.Name && x.Area == fog.Map))
                throw new InvalidDataException(
                    $"map_splits: fog '{fog.Name}' already exists in fog.txt for area '{fog.Map}'");
            ann.Entrances.Add(new AnnotationData.Entrance
            {
                Name = fog.Name,
                ID = fog.Id,
                Area = fog.Map,
                Text = fog.Text,
                MakeFrom = fog.MakeFrom,
                ASide = MakeSide(fog.ASideArea, fog.ASideText),
                BSide = MakeSide(fog.BSideArea, fog.BSideText),
            });
            Console.WriteLine(
                $"MapSplits: added synthetic gate '{fog.Name}' ({fog.ASideArea} -> {fog.BSideArea})");
        }

        SplitEnemyAreas(ann, splits);
    }

    /// <summary>
    /// SFX id of the standard white fog-wall mist. FogRando uses this value
    /// for the AEG099_001/AEG099_002 gates in its own hardcoded showsfx list
    /// (GameDataWriterE.cs ~L3211-3234) when no vanilla sfx id was captured.
    /// </summary>
    public const int WhiteFogSfx = 5;

    /// <summary>
    /// FogRando's grey boundary-wall models (GameDataWriterE mfogModels).
    /// A MakeFrom gate using one of these gets AssetSfxParamRelativeID = 0
    /// from FogMod, so the grey mist is rendered by the asset's own
    /// AssetEnvironmentGeometryParam row (992xx); it needs no showsfx event,
    /// and adding one would overlay the white mist on the grey wall.
    /// </summary>
    private static readonly HashSet<string> GreyFogModels =
        new() { "AEG099_230", "AEG099_231", "AEG099_232" };

    /// <summary>
    /// A gate needs a showsfx init unless it uses a grey model (self-visible,
    /// see <see cref="GreyFogModels"/>). The model is the first MakeFrom token,
    /// exactly what FogMod's addFakeGate assigns to the created asset.
    /// </summary>
    private static bool NeedsShowSfx(SplitFog fog) =>
        !GreyFogModels.Contains(fog.MakeFrom.Split(' ')[0]);

    /// <summary>
    /// Number of map-splits gates that need a showsfx init. Program.cs checks
    /// the injected total against this.
    /// </summary>
    public static int CountShowSfxGates(MapSplits splits) =>
        splits.Fogs.Count(NeedsShowSfx);

    /// <summary>
    /// Appends one showsfx init (ChangeAssetEnableState + CreateAssetfollowingSFX,
    /// i.e. the fog-wall mist) per map-splits gate to the owning map's
    /// constructor event. FogMod only emits showsfx for gates captured in
    /// EventEditor.FogEdits, which is built from vanilla events carrying
    /// Fog:/Sfx: template annotations in fogevents.txt (EventEditor.cs ~L180),
    /// plus a hardcoded three-gate list (GameDataWriterE.cs ~L3211-3234).
    /// Synthetic gates have no vanilla event, so without this step they are
    /// interactable but invisible. Mirrors FogRando's hardcoded pattern:
    /// InitializeCommonEvent(0, showsfx, gate entity, sfx).
    /// Gates using a grey model (<see cref="GreyFogModels"/>) are skipped:
    /// they display their own mist. Returns the number of inits added.
    /// </summary>
    public static int InjectShowSfx(EMEVD emevd, string mapName, MapSplits splits, int showSfxEventId)
    {
        var mapFogs = splits.Fogs.Where(f => f.Map == mapName).ToList();
        foreach (var fog in mapFogs.Where(f => !NeedsShowSfx(f)))
        {
            Console.WriteLine(
                $"MapSplits: showsfx skipped for '{fog.Name}' "
                + $"({fog.MakeFrom.Split(' ')[0]} renders its own grey mist)");
        }
        var fogs = mapFogs.Where(NeedsShowSfx).ToList();
        if (fogs.Count == 0)
            return 0;
        var evt0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (evt0 == null)
        {
            Console.WriteLine(
                $"Warning: Event 0 not found in {mapName}, cannot inject showsfx for map-splits gates");
            return 0;
        }
        foreach (var fog in fogs)
        {
            // Appended at the end of Event 0: no Parameter index shifting needed.
            evt0.Instructions.Add(
                EmevdHelper.InitializeCommonEvent(showSfxEventId, fog.Id, WhiteFogSfx));
            Console.WriteLine(
                $"MapSplits: showsfx({fog.Id}, sfx={WhiteFogSfx}) injected in {mapName} for '{fog.Name}'");
        }
        return fogs.Count;
    }

    /// <summary>
    /// Gives each supplement zone that declares cols and/or enemies its own
    /// EnemyArea (inheriting the source's ScalingTier) and reassigns per-enemy
    /// entries into it, so the zone scales with its own graph.json tier:
    /// - cols: moves the map-prefixed collision names out of the source
    ///   EnemyArea and reassigns EnemyLocs by collision (EnemyLocArea.Cols are
    ///   map-prefixed "m20_01_00_00_h001800"; EnemyLoc.Col is unprefixed).
    /// - enemies: reassigns EnemyLocs by entity name (e.g. "c5651_9000") on the
    ///   zone's map. This is the overworld-tile variant where EnemyLocs carry no
    ///   Col; the per-enemy Area field is FogRando's native override (same
    ///   mechanism as the vanilla "Area: abyssal" entries in foglocations2.txt).
    ///   Entries default to split_from as the expected source area; the
    ///   qualified form "area:cNNNN_NNNN" declares another source for enemies
    ///   FogRando attributed to an overlapping area (e.g. the Fort of
    ///   Reprimand gatehouse trio under scadualtus_high).
    /// Unknown enemy names and names whose EnemyLoc belongs to an unexpected
    /// area are configuration errors and throw.
    /// </summary>
    private static void SplitEnemyAreas(AnnotationData ann, MapSplits splits)
    {
        if (ann.Locations?.EnemyAreas == null)
        {
            if (splits.Zones.Any(z => z.Cols.Count > 0 || z.Enemies.Count > 0))
                Console.WriteLine("MapSplits: warning, cols/enemies configured but no foglocations loaded; EnemyArea split skipped");
            return;
        }
        foreach (var zone in splits.Zones.Where(z => z.Cols.Count > 0 || z.Enemies.Count > 0))
        {
            var src = ann.Locations.EnemyAreas.FirstOrDefault(a => a.Name == zone.SplitFrom)
                ?? throw new InvalidDataException(
                    $"map_splits: EnemyArea '{zone.SplitFrom}' not found for split '{zone.Name}'");

            var moved = new List<string>();
            if (zone.Cols.Count > 0)
            {
                var prefixed = zone.Cols.Select(c => $"{zone.Map}_{c}").ToHashSet();
                var srcCols = (src.Cols ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                moved = srcCols.Where(prefixed.Contains).ToList();
                if (moved.Count != prefixed.Count)
                    throw new InvalidDataException(
                        $"map_splits: cols missing from EnemyArea '{zone.SplitFrom}': "
                        + string.Join(", ", prefixed.Except(moved)));
                src.Cols = string.Join(' ', srcCols.Where(c => !prefixed.Contains(c)));
            }
            ann.Locations.EnemyAreas.Add(new AnnotationData.EnemyLocArea
            {
                Name = zone.Name,
                Cols = moved.Count > 0 ? string.Join(' ', moved) : null,
                ScalingTier = src.ScalingTier,
            });

            var colSet = zone.Cols.ToHashSet();
            // entity name -> expected source area ("area:cNNNN_NNNN" qualified
            // form, defaulting to split_from)
            var wanted = new Dictionary<string, string>();
            foreach (var entry in zone.Enemies)
            {
                var sep = entry.IndexOf(':');
                if (sep >= 0)
                    wanted[entry[(sep + 1)..]] = entry[..sep];
                else
                    wanted[entry] = zone.SplitFrom;
            }
            var found = new HashSet<string>();
            int reassigned = 0;
            foreach (var loc in ann.Locations.Enemies)
            {
                if (loc.Map != zone.Map)
                    continue;
                bool byCol = loc.Col != null && colSet.Contains(loc.Col);
                string? expectedById =
                    loc.ID != null && wanted.TryGetValue(loc.ID, out var declared)
                        ? declared : null;
                if (!byCol && expectedById == null)
                    continue;
                if (loc.ActualArea != (expectedById ?? zone.SplitFrom))
                {
                    if (expectedById != null)
                        throw new InvalidDataException(
                            $"map_splits: enemy '{loc.ID}' belongs to area '{loc.ActualArea}', "
                            + $"expected '{expectedById}' for split '{zone.Name}'");
                    continue;
                }
                loc.Area = zone.Name;
                reassigned++;
                if (expectedById != null)
                    found.Add(loc.ID!);
            }
            var missing = wanted.Keys.Except(found).OrderBy(x => x).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException(
                    $"map_splits: enemies not found in foglocations for map '{zone.Map}': "
                    + string.Join(", ", missing));

            Console.WriteLine(
                $"MapSplits: EnemyArea '{zone.SplitFrom}' -> '{zone.Name}': "
                + $"{moved.Count} cols, {reassigned} enemies reassigned");
        }
    }
}
