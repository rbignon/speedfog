using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// [plugin.halloween] darkness: keeps the vanilla evergaol gparam override
/// (m00_00_0000_commonevent) active everywhere, via a looping event in
/// common.emevd (see docs/plugins/halloween-darkness.md).
/// </summary>
public static class HalloweenDarknessInjector
{
    private static readonly int EVENT_ID = SpeedFogIds.HalloweenDarknessEvents.Base;

    /// <summary>Gparam override sub ID of the vanilla evergaol ambience, the
    /// one common_func event 90005882 activates on entering an evergaol.</summary>
    private const int EVERGAOL_GPARAM_SUB_ID = 0;

    /// <summary>Vanilla events deactivate overrides (evergaol exit, Academy,
    /// Specimen Storehouse, Enir-Ilim) and persistence across map loads is
    /// unverified, so the event re-applies the override. Short enough that a
    /// bright window after a fog gate warp goes unnoticed.</summary>
    private const int REAPPLY_INTERVAL_SECONDS = 2;

    /// <summary>Create the looping darkness event in common.emevd and
    /// register it in event 0.</summary>
    public static void InjectEmevdEvent(EMEVD commonEmevd, Events events)
    {
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping darkness event");
            return;
        }

        // Apply, wait, restart: same periodic-reapply idiom as WeatherInjector
        // (EMEVD Goto only jumps forward, so loops use EventEndType.Restart).
        var evt = new EMEVD.Event(EVENT_ID);
        evt.Instructions.Add(events.ParseAdd($"ActivateGparamOverride({EVERGAOL_GPARAM_SUB_ID}, 0)"));
        evt.Instructions.Add(events.ParseAdd($"WaitFixedTimeSeconds({REAPPLY_INTERVAL_SECONDS})"));
        evt.Instructions.Add(events.ParseAdd("EndUnconditionally(EventEndType.Restart)"));
        commonEmevd.Events.Add(evt);
        initEvent.Instructions.Add(EmevdHelper.InitializeEvent(EVENT_ID));

        Console.WriteLine(
            $"Halloween darkness: event {EVENT_ID} (evergaol gparam override, " +
            $"reapply every {REAPPLY_INTERVAL_SECONDS}s)");
    }
}
