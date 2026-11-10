using System.Numerics;
using FogModWrapper.Common;
using FogModWrapper.Warps;
using SoulsFormats;
using SoulsIds;
using static System.FormattableString;

namespace FogModWrapper.Plugins.Halloween;

/// <summary>
/// Halloween jumpscares ([plugin.halloween] jumpscares): in the layers
/// JumpscarePlanner draws, a hidden Aging Untouchable warps onto the player
/// a random delay after they enter an armed cluster, fires its lantern
/// burst on them, fights with its AI for a few seconds (strike only), then
/// vanishes in a puff. See docs/plugins/halloween-jumpscares.md.
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
    // Vanilla untouchable think row: the part needs a valid one, and the
    // event switches this AI on after the burst in strike mode only.
    internal const int ACTOR_THINK_PARAM = 52800000;
    private const int PLAYER_ENTITY = 10000;
    // Vanilla event 2049422200 warps its helper onto the player this way.
    internal const int WARP_DUMMY_POLY = 12;

    // The c5280 animation and the fixed waits that sequence the event
    // (whether ForceAnimationPlayback's "wait for completion" argument
    // blocks the event is not established, so the waits do not rely on it).
    // Tuned in game, see docs/plugins/halloween-jumpscares.md.
    //
    // The lantern burst vanilla plays right after its far teleport: the
    // explosion, hit 0.03-0.27 s at radius 4. Played straight after the
    // warp: in game, an arrival animation (5010) there showed the actor
    // dropping in, left behind by the running player.
    internal const int BURST_ANIM = 3001;
    internal const float BURST_SECONDS = 1.5f;
    // Strike: how long the actor fights with its AI after the burst.
    internal const float FIGHT_SECONDS = 10f;

    // The exit: the puff vanilla common event 90005636 spawns on an NPC
    // before disabling it 0.2 s later, masking the disable. The body itself
    // cannot fade from EMEVD: Set Character Fade On Enable only fades in,
    // the opacity-fading 7600 has no W_Event in the shared c9997 behavior
    // (forced from EMEVD, it showed nothing in game), and c5280's materials
    // have no fade parameter for a SpEffect to drive. On c5280, dummy 905
    // sits at head height and dummy 960 is spread over the whole body.
    internal const int PUFF_HEAD_DUMMY = 905;
    internal const int PUFF_HEAD_SFX = 643041;
    internal const int PUFF_BODY_DUMMY = 960;
    internal const int PUFF_BODY_SFX = 643040;
    internal const float PUFF_SECONDS = 0.2f;

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
            log($"  Warning: No vanilla enemy to clone from, skipping {actors.Count} jumpscare actor(s) in " +
                string.Join(", ", actors.Select(a => a.MapId).Distinct()));
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

    /// <summary>
    /// The actor's event, literal values. The disable comes first, before
    /// any early exit (MSB parts load enabled); the layer flag is set before
    /// the warp, so a death or quit-out mid-jumpscare never replays it.
    /// </summary>
    internal static EMEVD.Event BuildEvent(
        Events events, JumpscareActor actor, HalloweenPluginSettings.Settings settings)
    {
        uint e = actor.EntityId;
        var map = ErdtreeWarpPatcher.ParseMapBytes(actor.MapId);
        static string EndIfFlag(int flag) =>
            $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {flag})";
        static string Animation(uint entity, int anim) =>
            $"ForceAnimationPlayback({entity}, {anim}, false, false, false, ComparisonType.Equal, 1)";
        static string Wait(float seconds) => Invariant($"WaitFixedTimeSeconds({seconds})");
        static string Puff(uint entity, int dummy, int sfx) =>
            $"SpawnOneshotSFX(TargetEntityType.Character, {entity}, {dummy}, {sfx})";

        var lines = new List<string>
        {
            $"ChangeCharacterEnableState({e}, Disabled)",
            $"SetCharacterAIState({e}, Disabled)",
            EndIfFlag(actor.LayerFlag),  // this layer already played
            EndIfFlag(actor.LeftFlag),   // player already left the cluster
            // Mirror flags, never the zone-tracking flags: the racing mod
            // clears those right after capture, before this map loads.
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {actor.EnteredFlag})",
            Invariant($"WaitRandomTimeSeconds({settings.JumpscareDelayMin}, {settings.JumpscareDelayMax})"),
            // Another actor of the layer (second map of the cluster) may
            // have played during the delay.
            EndIfFlag(actor.LayerFlag),
            EndIfFlag(actor.LeftFlag),   // left during the delay: lost
        };
        // Multi-map cluster or open-world part of it: wait for this map.
        lines.Add($"EndIfPlayerInoutMap(EventEndType.Restart, false, {map[0]}, {map[1]}, {map[2]}, {map[3]})");
        lines.Add($"SetEventFlag(TargetEventFlagType.EventFlag, {actor.LayerFlag}, ON)");
        lines.Add($"ChangeCharacterEnableState({e}, Enabled)");
        lines.Add($"WarpCharacterAndCopyFloor({e}, TargetEntityType.Character, {PLAYER_ENTITY}, {WARP_DUMMY_POLY}, {PLAYER_ENTITY})");
        lines.Add(Animation(e, BURST_ANIM));
        lines.Add(Wait(BURST_SECONDS));
        if (settings.JumpscareMode == HalloweenPluginSettings.JumpscareMode.Strike)
        {
            lines.Add($"SetCharacterAIState({e}, Enabled)");
            lines.Add(Wait(FIGHT_SECONDS));
        }
        lines.Add(Puff(e, PUFF_HEAD_DUMMY, PUFF_HEAD_SFX));
        lines.Add(Puff(e, PUFF_BODY_DUMMY, PUFF_BODY_SFX));
        lines.Add(Wait(PUFF_SECONDS));
        lines.Add($"ChangeCharacterEnableState({e}, Disabled)");

        var evt = new EMEVD.Event(actor.EventId);
        // events is shared across the parallel map workers; serialize
        // instruction building like GateDecorInjector does.
        lock (events)
        {
            foreach (var line in lines)
                evt.Instructions.Add(events.ParseAdd(line));
        }
        return evt;
    }

    /// <summary>
    /// EMEVD phase: one event per placed actor in the map's EMEVD (mod copy,
    /// else copied from the game), registered in event 0. Called by
    /// HalloweenAmbientPass after the shared MSB write, with the actors
    /// ApplyToMsb actually placed.
    /// </summary>
    internal static void WriteEvents(
        string modDir, string gameDir, Events events, string mapId,
        List<JumpscareActor> actors, HalloweenPluginSettings.Settings settings, Action<string> log)
    {
        if (actors.Count == 0)
            return;

        var emevdFileName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdFileName);
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
            if (!File.Exists(gameEmevdPath))
            {
                log($"  Warning: {emevdFileName} not found, skipping jumpscare events for {mapId}");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            log($"  Warning: Event 0 not found in {emevdFileName}, skipping jumpscare events");
            return;
        }

        foreach (var actor in actors)
        {
            emevd.Events.Add(BuildEvent(events, actor, settings));
            initEvent.Instructions.Add(EmevdHelper.InitializeEvent(actor.EventId));
        }
        emevd.Write(emevdPath);
    }
}
