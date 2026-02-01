// writer/SpeedFogWriter/Helpers/EntityIdAllocator.cs
namespace SpeedFogWriter.Helpers;

public class EntityIdAllocator
{
    // ID allocation ranges (per speedfog-events.yaml allocation plan)
    // 79000001-79000099: Template event definitions (in common_func)
    // 79000100-79099999: Per-fog-gate event instances
    // 79100000-79199999: SpawnPoint region IDs
    // 79200000-79299999: Event flags
    // 79900000-79999999: Reserved for special flags
    //
    // Entity IDs reuse FogRando's range (755890000+) since they won't be used together
    private uint _nextEntityId = 755890000;
    private uint _nextFlagId = 79200000;
    private uint _nextRegionId = 79100000;
    private uint _nextEventId = 79000100;

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
