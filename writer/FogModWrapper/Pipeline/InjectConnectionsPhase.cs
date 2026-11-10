namespace FogModWrapper.Pipeline;

/// <summary>Inject our connections and area tiers into FogMod's Graph.</summary>
internal static class InjectConnectionsPhase
{
    public static void Run(Context ctx)
    {
        ctx.InjectionResult = ConnectionInjector.InjectAndExtract(
            ctx.Graph, ctx.GraphData.Connections, ctx.GraphData.FinishEvent, ctx.GraphData.FinalNodeFlag);

        ConnectionInjector.ApplyAreaTiers(ctx.Graph, ctx.GraphData.AreaTiers);
    }
}
