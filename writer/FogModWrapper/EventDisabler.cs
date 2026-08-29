using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Neutralizes vanilla EMEVD events listed under [[disable_events]] in
/// data/game_tweaks.toml: the event body is replaced by a single
/// END Unconditionally and its parameter table cleared, so the Event 0
/// initializer (left untouched, which keeps Event 0's own parameter table
/// valid) starts an event that ends immediately.
///
/// Introduced for the Elden Ring 1.17 Tarnished Pack NPC invasion at Redmane
/// (m60_51_36_00 event 1051360740, gated by DLC flag 6953): unwanted in a
/// SpeedFog run, and not disableable through a flag (its conditions are the
/// Radahn festival flags 9410/9413, which the run needs as they are). Its
/// prologue is also what disables the invader NPC, so the MSB part is removed
/// alongside through [[remove_entities]] (VanillaWarpRemover). An event
/// absent from the file (a map still on 1.16 data) is reported and skipped,
/// so the list stays correct whichever version the snapshot holds.
/// </summary>
public static class EventDisabler
{
    public static void Inject(string modDir, IEnumerable<DisableEvent> entries)
    {
        foreach (var group in entries.GroupBy(e => e.Map))
        {
            var mapId = group.Key;
            var emevdPath = Path.Combine(modDir, "event", $"{mapId}.emevd.dcx");
            if (!File.Exists(emevdPath))
            {
                Console.WriteLine($"Disable events: {mapId}.emevd.dcx not in the mod output, skipping {group.Count()} entry(ies)");
                continue;
            }

            var emevd = EMEVD.Read(emevdPath);
            int disabled = 0;
            foreach (var entry in group)
            {
                var evt = emevd.Events.FirstOrDefault(e => e.ID == entry.EventId);
                if (evt == null)
                {
                    Console.WriteLine($"Disable events: event {entry.EventId} not present in {mapId} (pre-patch file?), skipping");
                    continue;
                }
                evt.Instructions.Clear();
                evt.Instructions.Add(MakeEndUnconditionally());
                evt.Parameters.Clear();
                disabled++;
            }

            if (disabled == 0)
                continue;
            emevd.Write(emevdPath);
            Console.WriteLine($"Disable events: neutralized {disabled} event(s) in {mapId} ({string.Join(", ", group.Select(e => e.EventId))})");
        }
    }

    /// <summary>1000[4] END Unconditionally with execution end type 0 (End, not Restart).</summary>
    private static EMEVD.Instruction MakeEndUnconditionally() =>
        new(1000, 4, new byte[4]);
}
