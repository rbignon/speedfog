using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Boss healthbar names for promoted mobs (graph.json v4.8 boss_names, see
/// docs/boss-healthbar-names.md). The healthbar name comes from the nameId
/// argument of DisplayBossHealthBar (2003[11]); the enemy randomizer only
/// carries it along for sources with a vanilla NpcName (it copies their
/// healthbar events), so a regular mob placed in a boss arena keeps the
/// arena's vanilla name ("Rellana" over an Aging Untouchable). Per arena:
/// the display name is resolved to a NpcName id (an exact vanilla engus
/// match first, so "Crucible Knight" stays localized everywhere, else a
/// SpeedFog id whose entry is written to engus + frafr, English text in
/// both), then every 2003[11] of the arena entity in the arena map's EMEVD
/// is repointed at it. Instructions whose nameId is an event parameter are
/// left alone (none of the tagged arenas uses one).
///
/// New entries go to the base NpcName.fmg of item_dlc02.msgbnd.dcx, the
/// bundle the game resolves text from (it carries a full copy of the base
/// FMGs; FogMod and the Item Randomizer write only the _dlc02 bundles, and
/// the base item.msgbnd.dcx is not consulted when the DLC one is present).
/// </summary>
public static class BossNameInjector
{
    private static readonly string[] NpcNameBnds = { "item.msgbnd.dcx", "item_dlc02.msgbnd.dcx" };
    private const string NewEntriesBnd = "item_dlc02.msgbnd.dcx";
    private const string NpcNameFmg = "NpcName.fmg";

    private const int HealthbarBank = 2003;
    private const int HealthbarId = 11;
    private const int EntityOffset = 4;
    private const int NameIdOffset = 12;

    /// <summary>One arena's resolved healthbar name.</summary>
    public sealed record Resolution(uint ArenaId, string Name, string Map, int NameId, bool IsNew);

    public static void Inject(string modDir, string gameDir,
        IReadOnlyDictionary<string, BossNameEntry> bossNames, Action<string> log)
    {
        if (bossNames.Count == 0)
            return;

        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            log("Boss names: game msg directory not found, skipping");
            return;
        }

        var resolved = ResolveNameIds(bossNames, LoadVanillaNpcNames(gameMsgDir));

        var newEntries = resolved.Where(r => r.IsNew)
            .GroupBy(r => r.NameId)
            .Select(g => (id: g.Key, text: g.First().Name))
            .OrderBy(e => e.id)
            .ToList();
        int languages = WriteNpcNameEntries(modDir, gameMsgDir, newEntries);

        var eventDir = Path.Combine(modDir, "event");
        var pending = resolved.ToDictionary(r => r.ArenaId);
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var byMap in resolved.GroupBy(r => r.Map).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var emevdPath = Path.Combine(eventDir, $"{byMap.Key}.emevd.dcx");
            if (!File.Exists(emevdPath))
            {
                log($"  {byMap.Key}.emevd.dcx not in the mod dir, scanning the other EMEVDs for arena(s) "
                    + string.Join("/", byMap.Select(r => r.ArenaId)));
                continue;
            }
            scanned.Add(emevdPath);
            PatchArenas(emevdPath, byMap.Key, byMap.ToList(), pending, log, viaScan: false);
        }

        // Arenas the declared map does not cover: one pass over the other
        // EMEVDs of the mod dir (a large-tile boss whose events sit in a
        // neighbouring tile), patching every file that holds them.
        if (pending.Count > 0)
        {
            foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx").OrderBy(f => f, StringComparer.Ordinal))
            {
                if (scanned.Contains(emevdPath))
                    continue;
                var mapId = Path.GetFileName(emevdPath).Replace(".emevd.dcx", "", StringComparison.OrdinalIgnoreCase);
                PatchArenas(emevdPath, mapId, pending.Values.ToList(), pending, log, viaScan: true);
            }
            foreach (var r in pending.Values.OrderBy(r => r.ArenaId))
            {
                log($"  Warning: arena {r.ArenaId} (\"{r.Name}\") has no DisplayBossHealthBar in {r.Map}.emevd.dcx "
                    + "nor in any other EMEVD of the mod dir, healthbar name kept");
            }
        }

        log($"Boss names: {resolved.Count - pending.Count}/{resolved.Count} arena(s) patched, "
            + $"{newEntries.Count} new NpcName entr{(newEntries.Count == 1 ? "y" : "ies")} in {languages} language(s)");
    }

    /// <summary>Patches the given arenas in one EMEVD, writing it back when
    /// anything changed, and drops each patched arena from
    /// <paramref name="pending"/>. In scan mode an arena stays pending only
    /// while no file held it, so a boss whose healthbar shows from several
    /// EMEVDs gets every copy repointed.</summary>
    private static void PatchArenas(string emevdPath, string mapId, IReadOnlyList<Resolution> arenas,
        Dictionary<uint, Resolution> pending, Action<string> log, bool viaScan)
    {
        var emevd = EMEVD.Read(emevdPath);
        int total = 0;
        foreach (var r in arenas)
        {
            int n = PatchEmevd(emevd, new Dictionary<uint, int> { [r.ArenaId] = r.NameId });
            if (n == 0)
                continue;
            var how = viaScan ? $", found by scan, declared map {r.Map}" : "";
            log($"  {mapId}: arena {r.ArenaId} -> \"{r.Name}\" (NpcName {r.NameId}, {(r.IsNew ? "new" : "vanilla")}, {n} instruction(s){how})");
            pending.Remove(r.ArenaId);
            total += n;
        }
        if (total > 0)
            emevd.Write(emevdPath);
    }

    /// <summary>Resolves each arena's name to a NpcName id: the lowest vanilla
    /// entry with exactly that English text when one exists, else a
    /// SpeedFogIds.BossNameFmgIds id shared by every arena with the same
    /// name, allocated in ascending arena id order (deterministic across
    /// runs). Throws when the distinct new names exceed the range.</summary>
    public static IReadOnlyList<Resolution> ResolveNameIds(
        IReadOnlyDictionary<string, BossNameEntry> bossNames,
        IReadOnlyDictionary<string, int> vanillaNameIds)
    {
        var range = SpeedFogIds.BossNameFmgIds;
        var allocated = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<Resolution>();
        foreach (var (key, entry) in bossNames.Select(kv => (ParseArenaId(kv.Key), kv.Value)).OrderBy(kv => kv.Item1))
        {
            var name = entry.Name.Trim();
            if (vanillaNameIds.TryGetValue(name, out int vanillaId))
            {
                result.Add(new Resolution(key, name, entry.Map, vanillaId, IsNew: false));
                continue;
            }
            if (!allocated.TryGetValue(name, out int id))
            {
                if (allocated.Count >= range.Capacity)
                {
                    throw new InvalidOperationException(
                        $"Boss name NpcName budget exceeded: {allocated.Count + 1} distinct names " +
                        $"(max {range.Capacity}, next range starts at {range.End})");
                }
                id = range.Base + allocated.Count;
                allocated[name] = id;
            }
            result.Add(new Resolution(key, name, entry.Map, id, IsNew: true));
        }
        return result;
    }

    private static uint ParseArenaId(string key)
        => uint.TryParse(key, out uint id)
            ? id
            : throw new InvalidOperationException($"graph.json boss_names: arena key \"{key}\" is not an entity id");

    /// <summary>English NpcName text to lowest vanilla id, over every NpcName
    /// FMG of the engus item bnds (base game + DLC).</summary>
    public static Dictionary<string, int> LoadVanillaNpcNames(string gameMsgDir)
    {
        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        var engusDir = Path.Combine(gameMsgDir, "engus");
        foreach (var bndName in NpcNameBnds)
        {
            var path = Path.Combine(engusDir, bndName);
            if (!File.Exists(path))
                continue;
            var bnd = BND4.Read(path);
            foreach (var file in bnd.Files.Where(f => f.Name.Contains("NpcName")))
            {
                FMG fmg;
                try
                { fmg = FMG.Read(file.Bytes); }
                catch { continue; }

                foreach (var entry in fmg.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Text))
                        continue;
                    var text = entry.Text.Trim();
                    if (!byText.TryGetValue(text, out int existing) || entry.ID < existing)
                        byText[text] = entry.ID;
                }
            }
        }
        return byText;
    }

    /// <summary>Repoints the nameId of every DisplayBossHealthBar (2003[11])
    /// whose literal entity is a key of <paramref name="nameIdByArena"/>,
    /// enabling and disabling calls alike. Instructions whose nameId is bound
    /// to an event parameter are skipped (the literal bytes are dead).
    /// Returns the number of instructions rewritten.</summary>
    public static int PatchEmevd(EMEVD emevd, IReadOnlyDictionary<uint, int> nameIdByArena)
    {
        int n = 0;
        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                if (instr.Bank != HealthbarBank || instr.ID != HealthbarId || instr.ArgData.Length < NameIdOffset + 4)
                    continue;
                uint entity = BitConverter.ToUInt32(instr.ArgData, EntityOffset);
                if (!nameIdByArena.TryGetValue(entity, out int nameId))
                    continue;
                if (evt.Parameters.Any(p => p.InstructionIndex == i && p.TargetStartByte == NameIdOffset))
                    continue;
                BitConverter.GetBytes(nameId).CopyTo(instr.ArgData, NameIdOffset);
                n++;
            }
        }
        return n;
    }

    /// <summary>Adds (or overwrites) the entries in the base NpcName.fmg of
    /// item_dlc02.msgbnd.dcx for engus and frafr, English text in both.
    /// Returns the number of languages written.</summary>
    private static int WriteNpcNameEntries(string modDir, string gameMsgDir,
        IReadOnlyList<(int id, string text)> entries)
    {
        if (entries.Count == 0)
            return 0;

        int languages = 0;
        foreach (var lang in MsgBndEditor.TargetLanguages.OrderBy(l => l, StringComparer.Ordinal))
        {
            var langDir = Path.Combine(gameMsgDir, lang);
            if (!Directory.Exists(langDir))
                continue;
            int n = MsgBndEditor.EditBnd(modDir, langDir, lang, NewEntriesBnd, bnd =>
            {
                var file = bnd.Files.Find(f => f.Name.EndsWith(NpcNameFmg, StringComparison.OrdinalIgnoreCase));
                if (file == null)
                    return 0;
                var fmg = FMG.Read(file.Bytes);
                foreach (var (id, text) in entries)
                {
                    var existing = fmg.Entries.Find(e => e.ID == id);
                    if (existing != null)
                        existing.Text = text;
                    else
                        fmg.Entries.Add(new FMG.Entry(id, text));
                }
                file.Bytes = fmg.Write();
                return entries.Count;
            });
            if (n > 0)
                languages++;
        }
        return languages;
    }
}
