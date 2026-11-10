using FogModWrapper.Common;
using FogModWrapper.Enemies;
using FogModWrapper.Graces;
using FogModWrapper.Plugins.Themes;
using FogModWrapper.Racing;
using FogModWrapper.Start;
using FogModWrapper.World;

namespace FogModWrapper.Pipeline;

/// <summary>
/// Remaining mod-directory injectors (FMG, MSB, per-map EMEVD). Vanilla
/// stake removal is handled pre-Write via ann.RetryPoints in
/// WriteFogModPhase (FogMod reads MSBs from BHD archives and removes tagged
/// stakes during Write()).
/// </summary>
internal static class ModDirPhase
{
    public static void Run(Context ctx)
    {
        // Ship the snapshot MSB/EMEVD of maps the mod would otherwise leave to
        // the player's install (data/game_tweaks.toml [[pin_vanilla_maps]]);
        // first, so every injector of this phase sees those files. The
        // snapshot is where FogMod reads it: eldendata/Vanilla under the cwd.
        var vanillaDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "eldendata", "Vanilla"));
        if (Directory.Exists(vanillaDir))
            VanillaMapPinner.Pin(ctx.ModDir, vanillaDir, ctx.Tweaks.PinVanillaMaps, ctx.Config.MergeDir);
        else if (ctx.Tweaks.PinVanillaMaps.Count > 0)
            Console.WriteLine($"Warning: {vanillaDir} not found, {ctx.Tweaks.PinVanillaMaps.Count} pinned vanilla map(s) not shipped");

        // "RUN COMPLETE" banner FMG entries (all languages)
        if (ctx.GraphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectFmgEntries(ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.RunCompleteMessage);
        }

        // Class-selection equipment text: CharacterWriter wrote the
        // pre-loadout weapon names into GR_LineHelp; swap in the forced ones
        // (runs after CopyLocalizedFmgs so every language is present).
        if (ctx.LoadoutSwaps.Count > 0)
        {
            ClassLoadoutTextPatcher.Patch(ctx.ModDir, ctx.Config.MergeDir, ctx.Config.GameDir, ctx.LoadoutSwaps);
        }

        // Healthbar names of the relocated enemies (graph.json boss_names):
        // repoint the arena's DisplayBossHealthBar at the placed enemy's name
        // where the randomizer left the arena's own. Before the text themes so
        // their NpcName edits layer on the same mod copy, and so they can
        // reskin the ids it allocates by boss_name.
        IReadOnlyDictionary<string, int> bossNameIds = new Dictionary<string, int>();
        if (ctx.GraphData.BossNames.Count > 0)
        {
            bossNameIds = BossNameInjector.Inject(ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.BossNames, Console.WriteLine);
        }

        // Text theme plugins: cosmetic boss/UI text reskins, one catalogue per
        // theme (SpeedFogPlugin.TextTheme). Applied in plugin order; a later
        // theme overwrites colliding entries of an earlier one.
        var textThemes = ctx.Plugins.Select(p => p.TextTheme).OfType<string>().ToList();
        if (textThemes.Count > 1)
            Console.WriteLine(
                $"Warning: multiple text themes enabled ({string.Join(", ", textThemes)}); later themes overwrite colliding entries");
        foreach (var theme in textThemes)
        {
            TextTheme.Apply(theme, ctx.ModDir, ctx.Config.GameDir, ctx.Config.DataDir, bossNameIds);
        }

        // Death markers at fog gates
        var gateSides = GateGeometry.BuildGateSideLookup(ctx.Ann);
        DeathMarkerInjector.Inject(
            ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.Connections, ctx.Events,
            ctx.GraphData.EventMap, ctx.GraphData.DeathFlags, gateSides);

        // Plugin hook (Halloween ambient pass: gate decorations, greeters,
        // ambushes, jumpscare actors): MSB edits after the death markers and
        // before the Untouchable repoint and VanillaWarpRemover (the decor
        // ground estimate reads vanilla assets that the remover deletes).
        foreach (var plugin in ctx.Plugins)
            plugin.OnModDirMsb(ctx, gateSides);

        // Aging Untouchable minor boss: repoint enemy-randomizer-placed
        // untouchables to the boss NpcParam clone (see RegulationPhase).
        // Skipped when the boss was placed but the regulation phase
        // warn-returned: the boss NpcParam row was never written, so
        // repointing parts to it would point at a nonexistent row.
        if (UntouchableBossInjector.IsBossPlaced(ctx.GraphData.EnemyAssignments) && !ctx.UntouchableBossParamsApplied)
        {
            Console.WriteLine("Untouchable boss: regulation phase did not write boss rows, skipping MSB repoint");
        }
        else
        {
            // Also patches, in each arena map's EMEVD, the wall event the
            // enemy randomizer copied for the boss: the vanilla full wall
            // becomes the partial cut row written by RegulationPhase.
            UntouchableBossInjector.Inject(
                ctx.ModDir, ctx.GraphData.EnemyAssignments, ctx.Config.MergeDir, ctx.Tweaks.FallbackArenaMaps,
                ctx.UntouchableBossMovesetApplied);
        }

        // Rebirth option at Sites of Grace
        if (ctx.GraphData.StartingLarvalTears > 0)
        {
            RebirthInjector.Inject(ctx.ModDir, ctx.Config.GameDir);
        }

        // Remove the DLC "Shadow Realm Blessing" entry from the grace menu
        // (Scadutree leveling is irrelevant under SpeedFog's tier scaling).
        ShadowRealmBlessingRemover.Inject(ctx.ModDir, ctx.Config.GameDir);

        // Set startup flags (open gates, etc.) from data/game_tweaks.toml.
        // See docs/startup-flag-injection.md for the methodology used to find these flags.
        StartupFlagInjector.Inject(ctx.ModDir, ctx.Tweaks.StartupFlags);

        // Neutralize vanilla events listed in data/game_tweaks.toml [[disable_events]]
        // (Tarnished Pack NPC invasion at Leyndell, see docs/game-patch-migration.md).
        EventDisabler.Inject(ctx.ModDir, ctx.Tweaks.DisableEvents);

        // Plugin hook (Halloween darkness fast-loop repoint): per-map EMEVD
        // edits after EventDisabler, before VanillaWarpRemover.
        foreach (var plugin in ctx.Plugins)
            plugin.OnModDirLate(ctx);

        // Remove vanilla assets that conflict with fog gates: graph.json entities
        // plus the [[remove_entities]] entries of data/game_tweaks.toml.
        var removeEntities = ctx.GraphData.RemoveEntities
            .Concat(ctx.Tweaks.RemoveEntities)
            .ToList();
        if (removeEntities.Count > 0)
        {
            VanillaWarpRemover.Remove(ctx.ModDir, removeEntities);
        }

        // Re-enable Torrent inside boss arenas where vanilla blocks it via
        // collision DisableTorrent flags. See docs/torrent-arena-patcher.md.
        TorrentArenaPatcher.Patch(ctx.ModDir, ctx.Config.GameDir, ctx.Tweaks.TorrentArenas);

        // Remove spiritspring jumps that bypass map-splits chokepoints
        // (Fort of Reprimand back ravine). See docs/map-splits.md.
        SpiritspringRemover.Patch(
            ctx.ModDir, ctx.Config.GameDir, ctx.GraphData.AreaTiers,
            ctx.Tweaks.SpiritspringRemovals);
    }
}
