// writer/SpeedFogWriter/Helpers/EntityIdAllocator.cs
namespace SpeedFogWriter.Helpers;

public class EntityIdAllocator
{
    // Reuse FogRando's ID ranges (SpeedFog and FogRando won't be used together)
    // Reference: GameDataWriterE.cs L124-135
    private uint _nextEntityId = 755890000;
    private uint _nextFlagId = 1040290000;
    private uint _nextRegionId = 1040290070;
    private uint _nextEventId = 79000100;  // Per-fog-gate events

    public uint AllocateEntityId() => _nextEntityId++;
    public uint AllocateRegionId() => _nextRegionId++;
    public uint AllocateFlagId() => _nextFlagId++;
    public uint AllocateEventId() => _nextEventId++;

    public enum IdType { Entity, Region, Flag, Event }

    public (uint Start, uint End) ReserveBlock(int count, IdType type)
    {
        return type switch
        {
            IdType.Entity => ReserveFrom(ref _nextEntityId, count),
            IdType.Region => ReserveFrom(ref _nextRegionId, count),
            IdType.Flag => ReserveFrom(ref _nextFlagId, count),
            IdType.Event => ReserveFrom(ref _nextEventId, count),
            _ => throw new ArgumentException($"Unknown ID type: {type}")
        };
    }

    private static (uint Start, uint End) ReserveFrom(ref uint current, int count)
    {
        var start = current;
        current += (uint)count;
        return (start, current - 1);
    }
}
