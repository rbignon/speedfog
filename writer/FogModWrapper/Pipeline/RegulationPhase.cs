namespace FogModWrapper.Pipeline;

/// <summary>
/// All regulation.bin modifications batched behind a single decrypt/encrypt
/// cycle.
/// </summary>
internal static class RegulationPhase
{
    public static void Run(Context ctx)
    {
        var reg = RegulationEditor.Open(ctx.ModDir);
        if (reg == null)
        {
            Console.WriteLine("Warning: regulation.bin unavailable, skipping all regulation injections");
            return;
        }

        ShopInjector.ApplyTo(reg, ctx.GraphData.SentryTorchShop);
        if (!ctx.GraphData.TarnishedPack)
            ShopInjector.RemovePackGatedRows(reg);
        else
            Console.WriteLine("Tarnished Pack seed: keeping pack-gated shop rows");
        ctx.LoadoutSwaps = ClassLoadoutInjector.ApplyTo(reg, ctx.GraphData.ClassLoadout);
        WeaponUpgradeInjector.ApplyTo(reg, ctx.GraphData.WeaponUpgrade);
        StartingRuneInjector.ApplyTo(reg, ctx.GraphData.StartingRunes);

        // Replace world copies of start-granted goods already at their
        // possession cap (care package crystal tears, whetblades, Talisman
        // Pouches...) with Rune Arcs, so the key slots the item preset routes
        // them to stay real rewards (see docs/unique-item-neutralization.md).
        // Shares ShopLineupParam with ShopInjector: disjoint by data, not by
        // construction. The rows ShopInjector adds sell smithing stones
        // (never granted at start) and the Sentry's Torch (a weapon row), so
        // they can never match a saturated good; revisit if ShopInjector
        // ever sells a granted good. Reads EquipParamGoods (maxNum) only,
        // disjoint from HalloweenIconInjector's iconId writes.
        UniqueItemNeutralizer.ApplyTo(reg, ctx.GraphData);

        if (ctx.GraphData.ChapelGrace)
            ChapelGraceInjector.Inject(ctx.ModDir, ctx.Config.GameDir, ctx.Events, reg);

        PhantomCatalogInjector.ApplyTo(reg, ctx.PhantomSkins);

        // Aging Untouchable minor boss (allowlist-only): boss NpcParam
        // clone + partial damage-cut SpEffect. Independent of the
        // halloween plugin; gated on the enemy allowlist actually
        // placing the boss.
        if (UntouchableBossInjector.IsBossPlaced(ctx.GraphData.EnemyAssignments))
        {
            var bossParams = UntouchableBossInjector.ApplyParams(reg, ctx.Config.DataDir);
            ctx.UntouchableBossParamsApplied = bossParams.Core;
            ctx.UntouchableBossMovesetApplied = bossParams.Moveset;
        }
        // Every seed: the static anibnd applies this marker on every
        // untouchable's burst (3001), boss placed or not.
        UntouchableBossInjector.ApplyNearWarpMarker(reg);

        if (ctx.GraphData.IsPluginEnabled("halloween"))
        {
            // The greeter and ambusher rows are only consumed
            // when their spawns are actually placed (AmbientSpawnInjector
            // gates greeters and ambush packs on the same settings.Greeters
            // and settings.Ambushes flags); parsed once via
            // ctx.HalloweenSettings and reused by the spawn injection in
            // ModDirPhase, which places a flavor only if its rows were
            // written here.
            if (ctx.HalloweenSettings.Greeters)
                ctx.GreeterRowApplied = AmbientSpawnInjector.ApplyHostileGreeterRow(reg, ctx.HalloweenSettings.GreeterSpecies);
            if (ctx.HalloweenSettings.Ambushes)
                ctx.AmbusherRowsApplied = AmbientSpawnInjector.ApplyDecorativeAmbusherRows(reg, ctx.HalloweenSettings.AmbusherSpecies);
            if (ctx.HalloweenSettings.Jumpscares > 0)
                ctx.JumpscareRowApplied = HalloweenJumpscareInjector.ApplyJumpscareRow(reg);
            HalloweenIconInjector.ApplyTo(reg);
        }

        // Undo FogMod's makestable remap on the constant-flag play regions
        // (row 0 = all default ground); see docs/quitout-respawn.md.
        PlayRegionPatcher.ApplyTo(reg, ctx.Config.GameDir);

        // Re-apply the blessing neutralization that FogMod skips because of
        // the ExcludeMode override in WriteFogModPhase. Touches SpEffectParam
        // rows 200001xx/200002xx, disjoint from PhantomCatalogInjector's
        // additions.
        ScaduBlessingNeutralizer.ApplyTo(reg);

        reg.Save();
    }
}
