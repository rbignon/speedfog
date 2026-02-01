// writer/SpeedFogWriter/Writers/EnemyScalingApplicator.cs
using SoulsFormats;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

public class EnemyScalingApplicator
{
    // Scale event template ID from speedfog-events.yaml
    private const int ScaleEventId = 79000001;

    private readonly Dictionary<string, MSBE> _msbs;
    private readonly Dictionary<string, EMEVD> _emevds;
    private readonly SpeedFogGraph _graph;
    private readonly FogLocations _fogLocations;
    private readonly ScalingWriter _scalingWriter;

    private readonly Dictionary<string, int> _vanillaTiers = new();
    private readonly Dictionary<string, int> _targetTiers = new();
    private readonly Dictionary<string, string> _mapToZone = new();
    private readonly Dictionary<int, string> _groupToZone = new();
    private readonly Dictionary<string, string> _colToZone = new();

    private readonly HashSet<string> _excludedModels = new() { "c0000", "c4670" };
    private readonly HashSet<string> _modifiedMsbs = new();
    private uint _nextEntityId = 1028660000;

    public int EnemiesProcessed { get; private set; }
    public int EnemiesScaled { get; private set; }
    public int EnemiesSkipped { get; private set; }
    public IReadOnlySet<string> ModifiedMsbs => _modifiedMsbs;

    public EnemyScalingApplicator(
        Dictionary<string, MSBE> msbs,
        Dictionary<string, EMEVD> emevds,
        SpeedFogGraph graph,
        FogLocations fogLocations,
        ScalingWriter scalingWriter)
    {
        _msbs = msbs;
        _emevds = emevds;
        _graph = graph;
        _fogLocations = fogLocations;
        _scalingWriter = scalingWriter;

        BuildLookupTables();
    }

    private void BuildLookupTables()
    {
        foreach (var area in _fogLocations.EnemyAreas)
        {
            _vanillaTiers[area.Name] = area.ScalingTier;

            foreach (var map in area.GetMainMaps())
                _mapToZone[map] = area.Name;

            foreach (var group in area.GetGroups())
                _groupToZone[group] = area.Name;

            foreach (var col in area.GetCols())
                _colToZone[col] = area.Name;
        }

        foreach (var node in _graph.AllNodes())
        {
            foreach (var zone in node.Zones)
            {
                _targetTiers[zone] = node.Tier;
            }
        }
    }

    public void ApplyScaling()
    {
        foreach (var (mapName, msb) in _msbs)
        {
            foreach (var enemy in msb.Parts.Enemies)
            {
                ProcessEnemy(mapName, msb, enemy);
            }
        }

        Console.WriteLine($"  Enemies: {EnemiesProcessed} processed, {EnemiesScaled} scaled, {EnemiesSkipped} skipped");
    }

    private void ProcessEnemy(string mapName, MSBE msb, MSBE.Part.Enemy enemy)
    {
        EnemiesProcessed++;

        if (_excludedModels.Contains(enemy.ModelName))
        {
            EnemiesSkipped++;
            return;
        }

        var zone = DetermineEnemyZone(mapName, enemy);
        if (zone == null)
        {
            EnemiesSkipped++;
            return;
        }

        if (!_vanillaTiers.TryGetValue(zone, out var vanillaTier) ||
            !_targetTiers.TryGetValue(zone, out var targetTier))
        {
            EnemiesSkipped++;
            return;
        }

        if (vanillaTier == targetTier)
        {
            EnemiesSkipped++;
            return;
        }

        if (enemy.EntityID == 0)
        {
            enemy.EntityID = _nextEntityId++;
            _modifiedMsbs.Add(mapName);

            if (_nextEntityId % 10000U == 5000U)
            {
                _nextEntityId += 5000U;
            }
        }

        var spEffectId = _scalingWriter.GetTransitionEffect(vanillaTier, targetTier);
        if (spEffectId == -1)
        {
            EnemiesSkipped++;
            return;
        }

        CreateScaleEvent(mapName, (int)enemy.EntityID, spEffectId);
        EnemiesScaled++;
    }

    private string? DetermineEnemyZone(string mapName, MSBE.Part.Enemy enemy)
    {
        // Priority 1: Entity group ID
        foreach (var groupId in enemy.EntityGroupIDs)
        {
            if (groupId != 0 && _groupToZone.TryGetValue((int)groupId, out var zone))
                return zone;
        }

        // Priority 2: Collision part name
        if (!string.IsNullOrEmpty(enemy.CollisionPartName))
        {
            // Try with map prefix first (e.g., "m10_00_00_00_h0000B0")
            var colKey = $"{mapName}_{enemy.CollisionPartName}";
            if (_colToZone.TryGetValue(colKey, out var colZone))
                return colZone;

            // Try collision name alone
            if (_colToZone.TryGetValue(enemy.CollisionPartName, out colZone))
                return colZone;
        }

        // Priority 3: Map name
        if (_mapToZone.TryGetValue(mapName, out var mapZone))
            return mapZone;

        return null;
    }

    private void CreateScaleEvent(string mapName, int entityId, int spEffectId)
    {
        if (!_emevds.TryGetValue(mapName, out var emevd))
            return;

        var event0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (event0 == null) return;

        // InitializeEvent(slot=0, eventId=ScaleEventId, entityId, spEffectId)
        var initInstruction = new EMEVD.Instruction(2000, 0, new List<object>
        {
            0,              // slot
            ScaleEventId,   // scale event template ID
            entityId,
            spEffectId
        });

        event0.Instructions.Add(initInstruction);
    }
}
