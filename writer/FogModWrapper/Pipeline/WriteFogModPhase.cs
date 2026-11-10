using FogMod;
using SoulsIds;

namespace FogModWrapper.Pipeline;

/// <summary>
/// Call FogMod's writer to emit mod files, then copy localized FMGs from the
/// merge directory (FogMod only writes msg/engus/).
/// </summary>
internal static class WriteFogModPhase
{
    public static void Run(Context ctx)
    {
        Console.WriteLine($"Writing mod files to: {ctx.ModDir}");

        // Create MergedMods to merge Item Randomizer output files.
        // MergedMods.Resolve() looks in these directories for files to merge.
        List<string>? modDirs = null;
        if (!string.IsNullOrEmpty(ctx.Config.MergeDir))
        {
            Console.WriteLine($"Merging with: {ctx.Config.MergeDir}");
            modDirs = new List<string> { ctx.Config.MergeDir };
        }
        var mergedMods = new MergedMods(modDirs, null);

        // Tag vanilla stakes for removal by FogMod.
        // FogMod reads MSBs from BHD archives (not loose files), so we can't
        // post-process them. Instead, inject RetryPoints with "remove" tag so
        // FogMod removes them during Write() and writes the modified MSB.
        // LoadLiteConfig doesn't load RetryPoints, so ann.RetryPoints is null.
        ctx.Ann.RetryPoints = StakeRemover.GetRetryPointsToRemove(ctx.Tweaks.StakeRemovals);

        // Re-point vanilla parts misfiled outside their boss arena (Mini
        // Midra); when the surrounding area is not in the DAG they otherwise
        // keep vanilla stats (AllowUnlinked skips the rescale). Independent of
        // the enemy randomizer; must run before Resolve below so it sees
        // consistent name resolutions.
        HelperAreaResolver.ApplyVanillaOverrides(ctx.Ann.Locations, Console.WriteLine);

        // Resolve scaling areas for enemy-randomizer helper parts (e.g. the
        // Godskin Duo respawning backups). Without this they fall through to
        // the map's default area and get scaled to an unrelated DAG tier.
        // Must run after ApplyAreaTiers (uses Graph.AreaTiers) and before
        // Write (mutates ann.Locations.Enemies).
        if (!string.IsNullOrEmpty(ctx.Config.MergeDir))
        {
            HelperAreaResolver.Resolve(
                ctx.Ann, ctx.Graph, ctx.Config.MergeDir, ctx.GraphData.HelperModels, Console.WriteLine);
        }

        // FogMod's writer clamps every scaling target tier 21-28 down to 21
        // (29+ down to 22) when ExcludeMode == None, i.e. when base game and
        // DLC are both in the pool. SpeedFog wants tiers 22+ to mean what the
        // graph says, so pretend to be a DLC-only run (the value dlconly sets).
        // ExcludeMode is only read in four places during Write() and the other
        // three are unaffected: the != DLC data-load check stays true, and the
        // tierreq/scadushop branches are dead (options never set by SpeedFog).
        // The Scadutree blessing neutralization that the same condition guarded
        // is re-applied in RegulationPhase (ScaduBlessingNeutralizer). See
        // docs/enemy-scaling.md.
        ctx.Graph.ExcludeMode = AnnotationData.AreaMode.Base;

        var writer = new GameDataWriterE();
        writer.Write(ctx.Opt, ctx.Ann, ctx.Graph, mergedMods, ctx.ModDir, ctx.Events, ctx.EventConfig, Console.WriteLine);

        // Side.Warp is now populated by Write() (reads from MSB data).
        ctx.InjectionResult.BuildRegionToFlags(ctx.GraphData.EventMap);

        // Plugin hook: last point where flags can join RegionToFlags before
        // PatchEmevdPhase injects them (Halloween jumpscare mirror flags).
        foreach (var plugin in ctx.Plugins)
            plugin.AfterFogModWrite(ctx);

        // Copy non-English FMG files from Item Randomizer output.
        // FogMod only loads and writes msg/engus/ FMGs. When Item Randomizer
        // generates localized content (e.g., class descriptions in French),
        // those files are in the merge-dir but FogMod never writes them out.
        // This must run BEFORE other injectors (e.g., RunCompleteInjector) so
        // they can layer their changes on top of the copied files.
        if (!string.IsNullOrEmpty(ctx.Config.MergeDir))
        {
            CopyLocalizedFmgs(ctx.Config.MergeDir, ctx.ModDir);
        }
    }

    static void CopyLocalizedFmgs(string mergeDir, string modDir)
    {
        var mergeMsgDir = Path.Combine(mergeDir, "msg");
        if (!Directory.Exists(mergeMsgDir))
            return;

        foreach (var langDir in Directory.GetDirectories(mergeMsgDir))
        {
            var langName = Path.GetFileName(langDir);
            if (langName == "engus")
                continue; // FogMod already merges English FMGs

            var destDir = Path.Combine(modDir, "msg", langName);
            Directory.CreateDirectory(destDir);

            var files = Directory.GetFiles(langDir);
            foreach (var srcFile in files)
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(srcFile));
                File.Copy(srcFile, destFile, overwrite: true);
            }
            Console.WriteLine($"Copied {files.Length} localized FMGs: msg/{langName}/");
        }
    }
}
