using FogModWrapper.Common;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// [plugin.halloween] darkness: keeps the vanilla evergaol gparam override
/// (m00_00_0000_commonevent) active everywhere, via a looping event in
/// common.emevd, and repoints vanilla fast-loop overrides to it so they stop
/// racing that loop (see docs/plugins/halloween-darkness.md).
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

    /// <summary>Vanilla events that re-activate their own override in a fast
    /// loop: against the 2 second darkness loop they make the image flash
    /// (seen in game at Rennala's Grand Library, sub ID 500 every 0.1 s until
    /// flag 14000801), so their activations are repointed to the evergaol
    /// sub ID.</summary>
    private static readonly (string Map, long EventId)[] FastLoopOverrideEvents =
    {
        ("m14_00_00_00", 14002689),
    };

    /// <summary>Repoint the fast-loop vanilla overrides in the mod output's
    /// map EMEVDs. A map absent from the output is reported and skipped.</summary>
    public static void RepointFastLoopOverrides(string modDir)
    {
        foreach (var (map, eventId) in FastLoopOverrideEvents)
        {
            var emevdPath = Path.Combine(modDir, "event", $"{map}.emevd.dcx");
            if (!File.Exists(emevdPath))
            {
                Console.WriteLine($"Halloween darkness: {map}.emevd.dcx not in the mod output, event {eventId} left vanilla");
                continue;
            }
            var emevd = EMEVD.Read(emevdPath);
            int repointed = RepointOverrides(emevd, eventId);
            if (repointed == 0)
            {
                Console.WriteLine($"Halloween darkness: no gparam override in {map} event {eventId}, skipping");
                continue;
            }
            emevd.Write(emevdPath);
            Console.WriteLine($"Halloween darkness: repointed {repointed} override(s) of {map} event {eventId} to the evergaol");
        }
    }

    /// <summary>Set the sub ID of every Activate Gparam Override (2012[11]) of
    /// one event to the evergaol's, keeping its transition time. Returns the
    /// number of instructions changed (0 when the event is absent).</summary>
    public static int RepointOverrides(EMEVD emevd, long eventId)
    {
        var evt = emevd.Events.Find(e => e.ID == eventId);
        if (evt == null)
            return 0;
        int count = 0;
        foreach (var ins in evt.Instructions.Where(i => i.Bank == 2012 && i.ID == 11))
        {
            BitConverter.GetBytes(EVERGAOL_GPARAM_SUB_ID).CopyTo(ins.ArgData, 0);
            count++;
        }
        return count;
    }

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
