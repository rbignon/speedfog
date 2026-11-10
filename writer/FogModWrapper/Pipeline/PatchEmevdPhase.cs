using SoulsFormats;

namespace FogModWrapper.Pipeline;

/// <summary>
/// Single-pass EMEVD patches. Each per-map file is loaded once, all
/// applicable patches run, then written once if modified. common.emevd.dcx
/// receives the same warp patches in-memory and is written at the end of
/// CommonEmevdPhase.
/// </summary>
internal static class PatchEmevdPhase
{
    public static void Run(Context ctx)
    {
        var erdtree = ErdtreeWarpPatcher.BuildWarpData(ctx.Ann);
        var sealingTreeTargets = SealingTreeWarpPatcher.BuildTargets(ctx.Ann);
        var regionToTrapFlag = BossTriggerInjector.BuildRegionToTrapFlag(ctx.InjectionResult, ctx.Graph.Areas);
        Console.WriteLine($"Boss trigger: {regionToTrapFlag.Count} boss arena warp region(s) mapped");

        bool doZoneTracking = ctx.GraphData.FinishEvent > 0;
        HashSet<int>? expectedFlags = null;

        if (doZoneTracking)
        {
            // Use graph.json's FinishBossDefeatFlag (from fog.txt) with priority,
            // falling back to FogMod's Graph extraction. This fixes leyndell_erdtree
            // where the boss zone (erdtree) is reachable via norandom fogs but not
            // directly in the cluster, so FogMod's Graph doesn't have the DefeatFlag.
            ctx.BossDefeatFlag = ctx.GraphData.FinishBossDefeatFlag > 0
                ? ctx.GraphData.FinishBossDefeatFlag
                : ctx.InjectionResult.BossDefeatFlag;

            if (ctx.GraphData.FinishBossDefeatFlag > 0 && ctx.InjectionResult.BossDefeatFlag > 0
                && ctx.GraphData.FinishBossDefeatFlag != ctx.InjectionResult.BossDefeatFlag)
            {
                Console.WriteLine($"Note: Using graph.json defeat flag {ctx.GraphData.FinishBossDefeatFlag} " +
                    $"(FogMod Graph had {ctx.InjectionResult.BossDefeatFlag})");
            }

            expectedFlags = ctx.GraphData.Connections
                .Where(c => c.FlagId > 0)
                .Select(c => c.FlagId)
                .Distinct()
                .ToHashSet();

            Console.WriteLine($"Zone tracking: region lookup with {ctx.InjectionResult.RegionToFlags.Count} regions, " +
                $"{ctx.InjectionResult.RegionToFlags.Values.Sum(f => f.Count)} flag entries");
        }

        ctx.CommonEmevd = EMEVD.Read(ctx.CommonEmevdPath);

        // Synthetic map-splits gates get no showsfx (fog-wall mist) from
        // FogMod: no vanilla event means no FogEdits entry. Inject the inits
        // ourselves, using the showsfx common event FogMod wrote from
        // fogevents.txt (see MapSplitsInjector.InjectShowSfx).
        int showSfxEventId = 0;
        if (ctx.MapSplits.Fogs.Count > 0)
        {
            showSfxEventId = ctx.EventConfig.NewEvents?
                .FirstOrDefault(e => e.Name == "showsfx")?.ID ?? 0;
            if (showSfxEventId == 0)
                Console.WriteLine(
                    "Warning: showsfx event not found in fogevents.txt; map-splits gates will be invisible");
        }

        var injectedFlags = new HashSet<int>();
        int totalZoneTrackingInjected = 0;
        int totalBossTriggerInjected = 0;
        int totalErdtreePatched = 0;
        int totalSealingTreePatched = 0;
        int totalShowSfxInjected = 0;
        bool chapelEmevdSeen = false;

        foreach (var file in Directory.GetFiles(ctx.EventDir, "*.emevd.dcx"))
        {
            // Skip common.emevd.dcx: handled in-memory below
            if (Path.GetFullPath(file) == Path.GetFullPath(ctx.CommonEmevdPath))
                continue;

            var emevd = EMEVD.Read(file);
            bool modified = false;

            if (doZoneTracking)
            {
                int n = ZoneTrackingInjector.PatchEmevdFile(
                    emevd, ctx.Events, ctx.InjectionResult.RegionToFlags, injectedFlags);
                if (n > 0)
                {
                    totalZoneTrackingInjected += n;
                    modified = true;
                }
            }

            if (regionToTrapFlag.Count > 0)
            {
                int n = BossTriggerInjector.PatchEmevdFile(emevd, ctx.Events, regionToTrapFlag);
                if (n > 0)
                {
                    totalBossTriggerInjected += n;
                    modified = true;
                }
            }

            if (erdtree != null)
            {
                int n = ErdtreeWarpPatcher.PatchEmevd(
                    emevd, erdtree.PrimaryRegion, erdtree.AltRegion, erdtree.AltMapBytes, erdtree.AltMapPacked);
                if (n > 0)
                {
                    Console.WriteLine($"  {Path.GetFileName(file)}: patched {n} erdtree warp(s)");
                    totalErdtreePatched += n;
                    modified = true;
                }
            }

            if (sealingTreeTargets.Count > 0)
            {
                int n = SealingTreeWarpPatcher.PatchEmevd(emevd, sealingTreeTargets);
                if (n > 0)
                {
                    Console.WriteLine($"  {Path.GetFileName(file)}: patched {n} sealing tree warp(s)");
                    totalSealingTreePatched += n;
                    modified = true;
                }
            }

            // Suppress "Somewhere, a heavy door has opened" popup in common_func
            if (Path.GetFileName(file).Equals("common_func.emevd.dcx", StringComparison.OrdinalIgnoreCase))
            {
                if (HeavyDoorMessagePatcher.Patch(emevd) > 0)
                    modified = true;
            }

            // Remove the new-game intro cutscene from the Chapel of Anticipation
            // "Game start" event (10010020).
            if (Path.GetFileName(file).Equals("m10_01_00_00.emevd.dcx", StringComparison.OrdinalIgnoreCase))
            {
                chapelEmevdSeen = true;
                if (IntroCutscenePatcher.Patch(emevd) > 0)
                    modified = true;
            }

            if (showSfxEventId > 0)
            {
                var mapName = Path.GetFileName(file).Replace(".emevd.dcx", "");
                int n = MapSplitsInjector.InjectShowSfx(emevd, mapName, ctx.MapSplits, showSfxEventId);
                if (n > 0)
                {
                    totalShowSfxInjected += n;
                    modified = true;
                }
            }

            if (modified)
                emevd.Write(file);
        }

        if (!chapelEmevdSeen)
            Console.WriteLine("Warning: m10_01_00_00.emevd.dcx not written by FogMod, intro cutscene not removed");

        int expectedShowSfx = MapSplitsInjector.CountShowSfxGates(ctx.MapSplits);
        if (showSfxEventId > 0 && totalShowSfxInjected != expectedShowSfx)
        {
            Console.WriteLine(
                $"Warning: injected showsfx for {totalShowSfxInjected}/{expectedShowSfx} " +
                "map-splits gates (missing map EMEVD or Event 0?)");
        }

        // Apply the same warp patches to common.emevd (in memory).
        if (doZoneTracking)
        {
            totalZoneTrackingInjected += ZoneTrackingInjector.PatchEmevdFile(
                ctx.CommonEmevd, ctx.Events, ctx.InjectionResult.RegionToFlags, injectedFlags);
        }
        if (regionToTrapFlag.Count > 0)
        {
            totalBossTriggerInjected += BossTriggerInjector.PatchEmevdFile(
                ctx.CommonEmevd, ctx.Events, regionToTrapFlag);
        }
        if (erdtree != null)
        {
            int n = ErdtreeWarpPatcher.PatchEmevd(
                ctx.CommonEmevd, erdtree.PrimaryRegion, erdtree.AltRegion, erdtree.AltMapBytes, erdtree.AltMapPacked);
            if (n > 0)
            {
                Console.WriteLine($"  common.emevd.dcx: patched {n} erdtree warp(s)");
                totalErdtreePatched += n;
            }
        }
        if (sealingTreeTargets.Count > 0)
        {
            int n = SealingTreeWarpPatcher.PatchEmevd(ctx.CommonEmevd, sealingTreeTargets);
            if (n > 0)
            {
                Console.WriteLine($"  common.emevd.dcx: patched {n} sealing tree warp(s)");
                totalSealingTreePatched += n;
            }
        }

        // Log scan results
        if (regionToTrapFlag.Count > 0)
        {
            Console.WriteLine($"Boss trigger: injected {totalBossTriggerInjected} SetEventFlag(TrapFlag) " +
                $"before warp instructions ({regionToTrapFlag.Count} boss arena regions)");
        }
        if (doZoneTracking)
        {
            ZoneTrackingInjector.ValidateInjectedFlags(injectedFlags, expectedFlags!, totalZoneTrackingInjected);
        }
        if (erdtree != null)
        {
            if (totalErdtreePatched > 0)
                Console.WriteLine($"Erdtree warp fix: patched {totalErdtreePatched} warp(s) " +
                    $"(region {erdtree.PrimaryRegion} -> {erdtree.AltRegion})");
            else
                Console.WriteLine($"Erdtree warp fix: no matching warps found " +
                    $"(region {erdtree.PrimaryRegion} may not be connected)");
        }
        if (sealingTreeTargets.Count > 0)
        {
            if (totalSealingTreePatched > 0)
                Console.WriteLine($"Sealing Tree warp fix: patched {totalSealingTreePatched} warp(s) across {sealingTreeTargets.Count} entrance(s)");
            else
                Console.WriteLine("Sealing Tree warp fix: no matching warps found (entrances may not be connected)");
        }
    }
}
