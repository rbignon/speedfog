using System.Numerics;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Halloween jumpscares ([plugin.halloween] jumpscares): in the layers
/// JumpscarePlanner draws, a hidden Aging Untouchable warps onto the player
/// a random delay after they enter an armed cluster, then fades away
/// (scare) or fires one lantern burst first (strike). See
/// docs/plugins/halloween-jumpscares.md.
///
/// Three phases:
/// 1. Regulation (ApplyJumpscareRow): the actor's NpcParam row, the greeter
///    recipe under its own id.
/// 2. MSB (ApplyToMsb, driven per map by HalloweenAmbientPass): one c5280
///    per actor, cloned from a vanilla enemy of the map.
/// 3. EMEVD (WriteEvents): one event per placed actor in its map.
/// </summary>
public static class HalloweenJumpscareInjector
{
    internal const string ACTOR_MODEL = "c5280";
    // Vanilla untouchable think row: the part needs a valid one, but the
    // event disables the AI for the whole jumpscare.
    internal const int ACTOR_THINK_PARAM = 52800000;

    /// <summary>Write the actor NpcParam row. Returns false when NpcParam is
    /// unavailable: the MSB phase must then place no actor, or the parts
    /// would point at a missing row.</summary>
    public static bool ApplyJumpscareRow(RegulationEditor reg)
    {
        var npc = reg.GetParam("NpcParam");
        if (npc == null)
        {
            Console.WriteLine("Halloween jumpscares: NpcParam unavailable, jumpscare row not written");
            return false;
        }
        ApplyJumpscare(npc);
        return true;
    }

    /// <summary>PARAM-level entry point used by tests.</summary>
    public static void ApplyJumpscare(PARAM npc)
    {
        int slot = AmbientSpawnInjector.CloneFlatTierUntouchable(npc, SpeedFogIds.HalloweenJumpscareNpcRow);
        Console.WriteLine(
            $"Halloween jumpscares: NpcParam {SpeedFogIds.HalloweenJumpscareNpcRow} " +
            $"(flat tier {AmbientSpawnInjector.GREETER_TIER} untouchable, scaling in slot {slot}, runes 0, no drop)");
    }

    /// <summary>
    /// Place one c5280 per actor in an already-loaded MSB. Each actor clones
    /// the vanilla enemy nearest to its anchor gate (the map's first clone
    /// source without one, see AmbientSpawnInjector.CloneSources) and stands
    /// on that enemy's spot, a walkable position by construction; the event
    /// keeps it disabled until it warps it onto the player. Returns the
    /// actors actually placed: a map with no vanilla enemy places none.
    /// </summary>
    internal static List<JumpscareActor> ApplyToMsb(MSBE msb, List<JumpscareActor> actors, Action<string> log)
    {
        var placed = new List<JumpscareActor>();
        var cloneSources = AmbientSpawnInjector.CloneSources(msb.Parts.Enemies);
        if (cloneSources.Count == 0)
        {
            log($"  Warning: No vanilla enemy to clone from, skipping {actors.Count} jumpscare actor(s)");
            return placed;
        }
        MsbHelper.EnsureEnemyModel(msb, ACTOR_MODEL);

        foreach (var actor in actors)
        {
            var source = cloneSources[0];
            var gate = actor.AnchorGatePartName == null
                ? null
                : msb.Parts.Assets.Find(a => a.Name == actor.AnchorGatePartName);
            if (gate != null)
                source = cloneSources.MinBy(e => Vector3.DistanceSquared(e.Position, gate.Position))!;

            var part = (MSBE.Part.Enemy)source.DeepCopy();
            MsbHelper.CopyVisibilityGroups(part);
            part.ModelName = ACTOR_MODEL;
            part.Name = MsbHelper.GeneratePartName(msb.Parts.Enemies.Select(e => e.Name), ACTOR_MODEL);
            MsbHelper.SetNameIdent(part);
            part.EntityID = actor.EntityId;
            Array.Clear(part.EntityGroupIDs);
            part.NPCParamID = SpeedFogIds.HalloweenJumpscareNpcRow;
            part.ThinkParamID = ACTOR_THINK_PARAM;
            part.TalkID = 0;
            part.CharaInitID = -1;
            part.WalkRouteName = null;
            // Position and CollisionPartName stay the source's own.
            msb.Parts.Enemies.Add(part);
            placed.Add(actor);
        }
        return placed;
    }
}
