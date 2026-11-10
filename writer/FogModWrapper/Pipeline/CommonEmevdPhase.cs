namespace FogModWrapper.Pipeline;

/// <summary>
/// All common.emevd injectors (single Read in PatchEmevdPhase, single Write
/// at the end of this phase).
/// </summary>
internal static class CommonEmevdPhase
{
    public static void Run(Context ctx)
    {
        // Starting items
        if (ctx.GraphData.StartingGoods.Count > 0 || ctx.GraphData.CarePackage.Count > 0 || ctx.GraphData.TorrentSkins?.Unlock == true)
        {
            StartingItemInjector.Inject(ctx.CommonEmevd, ctx.GraphData.StartingGoods, ctx.GraphData.CarePackage, ctx.Events, ctx.GraphData.TorrentSkins);
        }

        // Starting resources (consumables via EMEVD)
        StartingResourcesInjector.Inject(
            ctx.CommonEmevd,
            ctx.Events,
            ctx.GraphData.StartingGoldenSeeds,
            ctx.GraphData.StartingSacredTears,
            ctx.GraphData.StartingLarvalTears,
            ctx.GraphData.StartingStoneswordKeys
        );

        // Roundtable unlock
        RoundtableUnlockInjector.Inject(ctx.CommonEmevd);

        // Boss death monitor for zone tracking (BossDefeatFlag resolved in PatchEmevdPhase).
        if (ctx.GraphData.FinishEvent > 0 && ctx.BossDefeatFlag > 0)
        {
            ZoneTrackingInjector.InjectBossDeathEvent(
                ctx.CommonEmevd, ctx.Events, ctx.InjectionResult.FinishEvent, ctx.BossDefeatFlag);
        }

        // "RUN COMPLETE" banner event
        if (ctx.GraphData.FinishEvent > 0)
        {
            RunCompleteInjector.InjectEmevdEvent(ctx.CommonEmevd, ctx.Events, ctx.GraphData.FinishEvent);
        }

        // Plugin hook (weather event, Halloween darkness event): after the
        // core injectors, before AlternateFlagPatcher and the single write.
        foreach (var plugin in ctx.Plugins)
            plugin.OnCommonEmevd(ctx);

        // Neutralize vanilla events that set AlternateFlag values (flags 300, 330).
        AlternateFlagPatcher.Patch(ctx.CommonEmevd);

        // Write common.emevd.dcx once (was previously read/written 6+ times).
        ctx.CommonEmevd.Write(ctx.CommonEmevdPath);
    }
}
