using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Warps the player to the Chapel of Anticipation grace on first game load.
/// This replaces the default tutorial spawn point (top of Chapel, facing the Grafted Scion)
/// with a direct spawn at the Site of Grace, so the player can immediately access fog gates
/// and begin their run.
///
/// Single-fire event in m10_01_00_00.emevd: uses a one-time flag so it only triggers once.
/// Must run after ChapelGraceInjector (which creates the player warp target in the MSB).
/// </summary>
public static class ChapelSpawnInjector
{
    private const string MAP_ID = "m10_01_00_00";
    private const int EVENT_ID = 755864000;

    // One-time flag: "chapel spawn already done" (category 1040299, pre-allocated by FogRando)
    private const int SPAWN_DONE_FLAG = 1040299002;

    /// <summary>
    /// Inject a warp-to-grace event into the chapel map EMEVD.
    /// </summary>
    /// <param name="modDir">Mod output directory (contains event/ subdirectory)</param>
    /// <param name="events">Events parser for building EMEVD instructions</param>
    /// <param name="playerEntityId">Player warp target entity from ChapelGraceInjector</param>
    public static void Inject(string modDir, Events events, uint playerEntityId)
    {
        if (playerEntityId == 0)
        {
            Console.WriteLine("Warning: Invalid player entity ID 0, skipping chapel spawn injection");
            return;
        }

        var emevdPath = Path.Combine(modDir, "event", $"{MAP_ID}.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine($"Warning: {MAP_ID}.emevd.dcx not found, skipping chapel spawn injection");
            return;
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine($"Warning: Event 0 not found in {MAP_ID}.emevd, skipping chapel spawn");
            return;
        }

        if (emevd.Events.Any(e => e.ID == EVENT_ID))
        {
            Console.WriteLine($"Chapel spawn event {EVENT_ID} already exists, skipping");
            return;
        }

        var evt = new EMEVD.Event(EVENT_ID);

        // 1. Skip if already spawned (one-time flag)
        evt.Instructions.Add(events.ParseAdd(
            $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {SPAWN_DONE_FLAG})"));

        // 2. Wait for map initialization (conservative margin for entity loading)
        evt.Instructions.Add(events.ParseAdd("WaitFixedTimeFrames(5)"));

        // 3. Warp player to grace position
        // WarpPlayer (bank 2003, id 14): mapBytes[4], playerEntity(uint4), unk(uint4)
        var warpArgs = new byte[12];
        warpArgs[0] = 10; // m10_xx_xx_xx
        warpArgs[1] = 1;  // m10_01_xx_xx
        warpArgs[2] = 0;  // m10_01_00_xx
        warpArgs[3] = 0;  // m10_01_00_00
        BitConverter.GetBytes(playerEntityId).CopyTo(warpArgs, 4);
        BitConverter.GetBytes(0).CopyTo(warpArgs, 8);
        evt.Instructions.Add(new EMEVD.Instruction(2003, 14, warpArgs));

        // 4. Mark as done so this doesn't trigger again
        evt.Instructions.Add(events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {SPAWN_DONE_FLAG}, ON)"));

        emevd.Events.Add(evt);

        // Register in Event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);           // slot = 0
        BitConverter.GetBytes(EVENT_ID).CopyTo(initArgs, 4);    // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);

        Console.WriteLine($"Chapel spawn: event {EVENT_ID} " +
                          $"(warp to player entity {playerEntityId}, flag {SPAWN_DONE_FLAG})");
    }
}
