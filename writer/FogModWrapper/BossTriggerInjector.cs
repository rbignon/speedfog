using FogMod;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects SetEventFlag(TrapFlag, ON) before WarpPlayer instructions that target
/// boss arena warp regions. This locks the exit fog gate (which checks TrapFlag via
/// BossTrapName) before the player even arrives in the arena.
///
/// Uses the same warp-patching pattern as ZoneTrackingInjector: scan all EMEVD files
/// for WarpPlayer instructions, match destination regions against a lookup, and insert
/// SetEventFlag before matching warps.
///
/// The fogwarp exit template checks TrapFlag (X20_4, from BossTrapName -> area.TrapFlag).
/// When TrapFlag is ON, the exit is locked behind DefeatFlag. By setting TrapFlag before
/// the entrance warp, the exit is already locked when the player arrives.
/// </summary>
public static class BossTriggerInjector
{
    /// <summary>
    /// Build a mapping of warp regions to TrapFlag values for boss arena entrances.
    /// The fogwarp exit template checks TrapFlag (from BossTrapName -> area.TrapFlag).
    /// Must be called after GameDataWriterE.Write() (Side.Warp is populated).
    /// </summary>
    public static Dictionary<int, int> BuildRegionToTrapFlag(
        InjectionResult injectionResult,
        Dictionary<string, AnnotationData.Area> areas)
    {
        var mapping = new Dictionary<int, int>();

        foreach (var (flagId, edge, desc) in injectionResult.DeferredEdges)
        {
            var side = edge.Side;
            if (side?.Warp == null)
                continue;

            if (!areas.TryGetValue(side.Area, out var area))
                continue;
            if (area.TrapFlag <= 0)
                continue;

            if (side.Warp.Region > 0)
                mapping.TryAdd(side.Warp.Region, area.TrapFlag);

            // Handle AlternateFlag warps (e.g., flag 300/330) with different warp regions
            if (side.AlternateSide?.Warp != null && side.AlternateSide.Warp.Region > 0)
                mapping.TryAdd(side.AlternateSide.Warp.Region, area.TrapFlag);
        }

        return mapping;
    }

    /// <summary>
    /// Patch a single EMEVD: insert SetEventFlag(TrapFlag, ON) before WarpPlayer
    /// instructions whose destination region matches a boss arena entrance.
    /// Returns the number of SetEventFlag instructions inserted.
    ///
    /// Same pattern as ZoneTrackingInjector.PatchEmevdFile: scan events for warp
    /// instructions, match regions, insert flags before warps in reverse order.
    /// </summary>
    public static int PatchEmevdFile(
        EMEVD emevd, Events events,
        Dictionary<int, int> regionToTrapFlag)
    {
        int totalInjected = 0;

        foreach (var evt in emevd.Events)
        {
            // First pass: find warp positions with matching boss arena regions.
            var warpPositions = new List<(int index, int trapFlag)>();

            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
                if (warpInfo == null)
                    continue;

                int region = warpInfo.Value.Region;
                if (region == 0)
                    continue;

                if (regionToTrapFlag.TryGetValue(region, out var trapFlag))
                {
                    warpPositions.Add((i, trapFlag));
                }
            }

            if (warpPositions.Count == 0)
                continue;

            // Second pass: insert SetEventFlag before each warp, from last to first
            // to avoid index shifting affecting earlier positions.
            for (int j = warpPositions.Count - 1; j >= 0; j--)
            {
                var (warpIdx, trapFlag) = warpPositions[j];

                var setFlagInstr = events.ParseAdd(
                    $"SetEventFlag(TargetEventFlagType.EventFlag, {trapFlag}, ON)");
                evt.Instructions.Insert(warpIdx, setFlagInstr);

                // Shift Parameter entries for instructions at or after insertion point
                foreach (var param in evt.Parameters)
                {
                    if (param.InstructionIndex >= warpIdx)
                        param.InstructionIndex++;
                }
            }

            totalInjected += warpPositions.Count;
        }

        return totalInjected;
    }
}
