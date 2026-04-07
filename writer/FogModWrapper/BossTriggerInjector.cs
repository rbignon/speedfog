using FogMod;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects startboss-equivalent events that trigger boss activation when the player
/// lands in a fog gate's warp region. This prevents bypassing the exit fog gate
/// before the boss fight starts (BossTrapName/TrapFlag exploit).
///
/// The vanilla startboss trigger regions are positioned inside the arena, but when
/// SpeedFog randomizes connections, the player may enter from a direction that
/// misses these regions. By adding a trigger at the warp landing point, the boss
/// activates immediately on arrival.
///
/// Chain: warp region entry -> BossTrigger flag set -> vanilla boss event activates
/// -> TrapFlag set -> exit fogwarp locked behind DefeatFlag.
/// </summary>
public static class BossTriggerInjector
{
    private const int EVENT_BASE = 755865000;

    /// <summary>
    /// A boss arena entrance identified from graph connections.
    /// </summary>
    public readonly record struct BossEntrance(
        int WarpRegion, int DefeatFlag, int BossTrigger, string Description);

    /// <summary>
    /// Collect boss arena entrances from deferred edges and area data.
    /// Must be called after GameDataWriterE.Write() (Side.Warp is populated).
    /// </summary>
    public static List<BossEntrance> CollectBossEntrances(
        InjectionResult injectionResult,
        Dictionary<string, AnnotationData.Area> areas)
    {
        var entrances = new List<BossEntrance>();
        var seen = new HashSet<(int region, int trigger)>();

        foreach (var (flagId, edge, desc) in injectionResult.DeferredEdges)
        {
            var side = edge.Side;
            if (side?.Warp == null)
                continue;

            if (!areas.TryGetValue(side.Area, out var area))
                continue;
            if (area.DefeatFlag <= 0 || area.BossTrigger <= 0)
                continue;

            AddEntrance(entrances, seen, side.Warp.Region, area, desc);

            // Handle AlternateFlag warps (e.g., flag 300/330) with different warp regions
            if (side.AlternateSide?.Warp != null)
            {
                AddEntrance(entrances, seen, side.AlternateSide.Warp.Region, area, $"{desc} (alt)");
            }
        }

        return entrances;
    }

    private static void AddEntrance(
        List<BossEntrance> entrances,
        HashSet<(int, int)> seen,
        int warpRegion,
        AnnotationData.Area area,
        string desc)
    {
        if (warpRegion <= 0)
            return;

        // Deduplicate: same warp region + same boss trigger = redundant event
        var key = (warpRegion, area.BossTrigger);
        if (!seen.Add(key))
            return;

        entrances.Add(new BossEntrance(warpRegion, area.DefeatFlag, area.BossTrigger, desc));
    }

    /// <summary>
    /// Inject startboss-equivalent events into common.emevd for each boss arena entrance.
    /// Each event monitors a fog gate warp region and sets BossTrigger when the player lands.
    /// </summary>
    /// <returns>Number of events injected.</returns>
    public static int Inject(EMEVD commonEmevd, Events events, List<BossEntrance> entrances)
    {
        if (entrances.Count == 0)
            return 0;

        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping boss trigger injection");
            return 0;
        }

        for (int i = 0; i < entrances.Count; i++)
        {
            var entry = entrances[i];
            int eventId = EVENT_BASE + i;

            var evt = new EMEVD.Event(eventId);

            // End permanently if boss already defeated
            evt.Instructions.Add(events.ParseAdd(
                $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {entry.DefeatFlag})"));

            // Wait for player in warp region (10000 = player entity)
            evt.Instructions.Add(events.ParseAdd(
                $"IfInoutsideArea(MAIN, InsideOutsideState.Inside, 10000, {entry.WarpRegion}, 1)"));

            // 1-frame delay (let standing-up animation start)
            evt.Instructions.Add(events.ParseAdd("WaitFixedTimeFrames(1)"));

            // Set BossTrigger -> vanilla boss event activates -> TrapFlag set
            evt.Instructions.Add(events.ParseAdd(
                $"SetEventFlag(TargetEventFlagType.EventFlag, {entry.BossTrigger}, ON)"));

            // Restart: re-check on next arena entry (e.g., after player death)
            evt.Instructions.Add(events.ParseAdd("EndUnconditionally(EventEndType.Restart)"));

            commonEmevd.Events.Add(evt);

            // Register in event 0 (InitializeEvent: bank 2000, id 0)
            var initArgs = new byte[8];
            BitConverter.GetBytes(0).CopyTo(initArgs, 0);           // slot = 0
            BitConverter.GetBytes(eventId).CopyTo(initArgs, 4);     // eventId
            initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));
        }

        Console.WriteLine($"Boss trigger: injected {entrances.Count} warp-region trigger(s)");
        return entrances.Count;
    }
}
