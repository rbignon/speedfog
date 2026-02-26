using SoulsFormats;

// Elden Ring EMEVD analysis tool for SpeedFog debugging.
// Uses SoulsFormats to read compiled .emevd.dcx files.
//
// Modes:
//   dump <file-or-dir> [--event ID] [--map-filter mAA_BB_CC]
//       Dump events from EMEVD files. Without --event, shows warp events only.
//       With --event ID, dumps all instructions of that specific event.
//       With --event all, dumps every event in every file.
//
//   search <file-or-dir> --flag ID
//       Find ALL references to a flag (set, check, goto, end, batch, brute-force).
//
//   init <file-or-dir> --event ID
//       Find InitializeEvent/InitializeCommonEvent calls targeting a specific event.

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dump_emevd_warps dump <dir-or-file> [--event ID|all] [--map-filter mAA_BB]");
    Console.Error.WriteLine("  dump_emevd_warps search <dir-or-file> --flag ID");
    Console.Error.WriteLine("  dump_emevd_warps init <dir-or-file> --event ID");
    return 1;
}

string mode = args[0];
string target = args[1];

// Parse options
string? mapFilter = null;
long? eventFilter = null;
bool eventAll = false;
int? searchFlag = null;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--map-filter" && i + 1 < args.Length)
        mapFilter = args[++i];
    else if (args[i] == "--event" && i + 1 < args.Length)
    {
        string val = args[++i];
        if (val == "all") eventAll = true;
        else eventFilter = long.Parse(val);
    }
    else if (args[i] == "--flag" && i + 1 < args.Length)
        searchFlag = int.Parse(args[++i]);
}

var files = GetFiles(target, mapFilter);
if (files.Count == 0)
{
    Console.Error.WriteLine($"No EMEVD files found: {target}");
    return 1;
}

switch (mode)
{
    case "dump":
        return DoDump(files, eventFilter, eventAll);
    case "search":
        if (!searchFlag.HasValue) { Console.Error.WriteLine("--flag required"); return 1; }
        return DoSearch(files, searchFlag.Value);
    case "init":
        if (!eventFilter.HasValue) { Console.Error.WriteLine("--event required"); return 1; }
        return DoInit(files, eventFilter.Value);
    default:
        Console.Error.WriteLine($"Unknown mode: {mode}");
        return 1;
}

// ─── Modes ─────────────────────────────────────────────────────────────

static int DoDump(List<string> files, long? eventFilter, bool eventAll)
{
    foreach (var file in files)
    {
        var emevd = EMEVD.Read(file);
        var fn = Path.GetFileName(file);

        foreach (var evt in emevd.Events)
        {
            if (eventFilter.HasValue && evt.ID != eventFilter.Value) continue;

            if (eventAll || eventFilter.HasValue)
            {
                // Full dump of every instruction
                Console.WriteLine($"\n=== {fn} Event {evt.ID} ({evt.RestBehavior}) ===");
                PrintParams(evt);
                PrintAllInstructions(evt);
            }
            else
            {
                // Warp-only mode: only show events containing warp instructions
                bool hasWarp = false;
                foreach (var instr in evt.Instructions)
                {
                    if (IsWarpInstruction(instr)) { hasWarp = true; break; }
                }
                if (!hasWarp) continue;

                Console.WriteLine($"\n=== {fn} Event {evt.ID} ({evt.RestBehavior}) ===");
                PrintParams(evt);
                PrintAllInstructions(evt);
            }
        }
    }
    return 0;
}

static int DoSearch(List<string> files, int flagId)
{
    Console.WriteLine($"Searching ALL references to flag {flagId} in {files.Count} files...\n");
    int total = 0;

    foreach (var file in files)
    {
        var emevd = EMEVD.Read(file);
        var fn = Path.GetFileName(file);

        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                var a = instr.ArgData;
                bool found = false;
                string detail = "";

                // --- Flag setters ---
                // SetEventFlag (2003:66): FlagType(b) FlagID(u32) State(b)
                if (instr.Bank == 2003 && instr.ID == 66 && a.Length >= 9)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        byte state = a[8];
                        detail = $"SetEventFlag({fid}, {OnOff(state)})";
                        found = true;
                    }
                }
                // SetNetworkConnectedEventFlag (2003:69): FlagType(b) FlagID(u32) State(b)
                if (instr.Bank == 2003 && instr.ID == 69 && a.Length >= 9)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        byte state = a[8];
                        detail = $"SetNetworkEventFlag({fid}, {OnOff(state)})";
                        found = true;
                    }
                }
                // BatchSetEventFlags (2003:22): Start(u32) End(u32) State(b)
                if (instr.Bank == 2003 && instr.ID == 22 && a.Length >= 9)
                {
                    uint start = BitConverter.ToUInt32(a, 0);
                    uint end = BitConverter.ToUInt32(a, 4);
                    if ((uint)flagId >= start && (uint)flagId <= end)
                    {
                        byte state = a[8];
                        detail = $"BatchSetEventFlags({start}-{end}, {OnOff(state)})";
                        found = true;
                    }
                }
                // BatchSetNetworkEventFlags (2003:63): Start(u32) End(u32) State(b)
                if (instr.Bank == 2003 && instr.ID == 63 && a.Length >= 9)
                {
                    uint start = BitConverter.ToUInt32(a, 0);
                    uint end = BitConverter.ToUInt32(a, 4);
                    if ((uint)flagId >= start && (uint)flagId <= end)
                    {
                        byte state = a[8];
                        detail = $"BatchSetNetworkEventFlags({start}-{end}, {OnOff(state)})";
                        found = true;
                    }
                }

                // --- Flag checkers ---
                // IfEventFlag (3:0): Cond(i32@0) State(b@4) FlagType(b@5) pad(2) FlagID(u32@8) = 12 bytes
                if (instr.Bank == 3 && instr.ID == 0 && a.Length >= 12)
                {
                    uint fid = BitConverter.ToUInt32(a, 8);
                    if (fid == (uint)flagId)
                    {
                        detail = $"IfEventFlag(cond={Cond(a[0])}, state={OnOff(a[4])}, flag={fid})";
                        found = true;
                    }
                }
                // IfBatchEventFlags (3:1): Cond(i32@0) State(b@4) Type(b@5) pad(2) Start(u32@8) End(u32@12)
                if (instr.Bank == 3 && instr.ID == 1 && a.Length >= 16)
                {
                    uint start = BitConverter.ToUInt32(a, 8);
                    uint end = BitConverter.ToUInt32(a, 12);
                    if ((uint)flagId >= start && (uint)flagId <= end)
                    {
                        detail = $"IfBatchEventFlags(cond={Cond(a[0])}, state={OnOff(a[4])}, range={start}-{end})";
                        found = true;
                    }
                }
                // WaitForEventFlag (1003:0): State(b@0) FlagType(b@1) pad(2) FlagID(u32@4)
                if (instr.Bank == 1003 && instr.ID == 0 && a.Length >= 8)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        detail = $"WaitForEventFlag(state={OnOff(a[0])}, flag={fid})";
                        found = true;
                    }
                }
                // SkipIfEventFlag (1003:1): Skip(b@0) State(b@1) FlagType(b@2) pad(1) FlagID(u32@4)
                if (instr.Bank == 1003 && instr.ID == 1 && a.Length >= 8)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        detail = $"SkipIfEventFlag(skip={a[0]}, state={OnOff(a[1])}, flag={fid})";
                        found = true;
                    }
                }
                // EndIfEventFlag (1003:2): End(b@0) State(b@1) FlagType(b@2) pad(1) FlagID(u32@4)
                if (instr.Bank == 1003 && instr.ID == 2 && a.Length >= 8)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        detail = $"EndIfEventFlag(end={EndType(a[0])}, state={OnOff(a[1])}, flag={fid})";
                        found = true;
                    }
                }
                // GotoIfEventFlag (1003:101): Label(b@0) State(b@1) FlagType(b@2) pad(1) FlagID(u32@4)
                if (instr.Bank == 1003 && instr.ID == 101 && a.Length >= 8)
                {
                    uint fid = BitConverter.ToUInt32(a, 4);
                    if (fid == (uint)flagId)
                    {
                        detail = $"GotoIfEventFlag(label={a[0]}, state={OnOff(a[1])}, flag={fid})";
                        found = true;
                    }
                }

                // --- Brute-force: scan 4-byte aligned positions ---
                // Skip instructions already handled by specialized decoders above
                if (!found && !IsDecodedFlagInstruction(instr))
                {
                    for (int off = 0; off + 3 < a.Length; off += 4)
                    {
                        int val = BitConverter.ToInt32(a, off);
                        if (val == flagId)
                        {
                            detail = $"BRUTE[{instr.Bank}:{instr.ID}] match at offset {off}, args={BitConverter.ToString(a)}";
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                {
                    bool param = HasParam(evt, i);
                    string pn = param ? " [PARAM]" : "";
                    Console.WriteLine($"  {fn} Event {evt.ID} [{i:D3}]: {detail}{pn}");
                    total++;
                }
            }
        }
    }
    Console.WriteLine($"\nTotal: {total} references");
    return 0;
}

static int DoInit(List<string> files, long eventId)
{
    Console.WriteLine($"Searching InitializeEvent/InitializeCommonEvent for event {eventId}...\n");

    foreach (var file in files)
    {
        var emevd = EMEVD.Read(file);
        var fn = Path.GetFileName(file);

        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];

                // InitializeEvent (2000:0): Slot(i32) EventID(u32) Params...
                if (instr.Bank == 2000 && instr.ID == 0 && instr.ArgData.Length >= 8)
                {
                    uint evtId = BitConverter.ToUInt32(instr.ArgData, 4);
                    if (evtId == (uint)eventId)
                    {
                        Console.WriteLine($"{fn} Event {evt.ID} [{i:D3}]: InitializeEvent(slot={BitConverter.ToInt32(instr.ArgData, 0)}, event={evtId})");
                        Console.WriteLine($"  args: {BitConverter.ToString(instr.ArgData)}");
                        DumpInitParams(instr.ArgData, 8);
                    }
                }
                // InitializeCommonEvent (2000:6): Unknown(i32) EventID(u32) Params...
                if (instr.Bank == 2000 && instr.ID == 6 && instr.ArgData.Length >= 8)
                {
                    uint evtId = BitConverter.ToUInt32(instr.ArgData, 4);
                    if (evtId == (uint)eventId)
                    {
                        Console.WriteLine($"{fn} Event {evt.ID} [{i:D3}]: InitializeCommonEvent(unk={BitConverter.ToInt32(instr.ArgData, 0)}, event={evtId})");
                        Console.WriteLine($"  args: {BitConverter.ToString(instr.ArgData)}");
                        DumpInitParams(instr.ArgData, 8);
                    }
                }
            }
        }
    }
    return 0;
}

// ─── Instruction decoding ──────────────────────────────────────────────

static void PrintAllInstructions(EMEVD.Event evt)
{
    for (int i = 0; i < evt.Instructions.Count; i++)
    {
        var instr = evt.Instructions[i];
        string decoded = Decode(instr);

        // Parameter annotations
        var paramNotes = new List<string>();
        foreach (var p in evt.Parameters)
        {
            if (p.InstructionIndex == i)
                paramNotes.Add($"P[src={p.SourceStartByte}→tgt={p.TargetStartByte},len={p.ByteCount}]");
        }
        string ps = paramNotes.Count > 0 ? "  " + string.Join(" ", paramNotes) : "";

        Console.WriteLine($"  [{i:D3}] {instr.Bank:D4}:{instr.ID:D3} {decoded}  ({Hex(instr.ArgData)}){ps}");
    }
}

static string Decode(EMEVD.Instruction ins)
{
    var a = ins.ArgData;
    int b = ins.Bank, id = ins.ID;

    // ── Labels (1014:0-20) ──
    if (b == 1014 && id >= 0 && id <= 20) return $"Label {id}";

    // ── Condition system (0:xxx) ──
    // IfConditionGroup: ResultCond(i32@0) State(b@4) TargetCond(i32@8) — but compiled as 4 bytes total?
    // In practice, compiled events pack this as 4 bytes: result(sbyte) state(byte) target(sbyte) pad
    if (b == 0 && id == 0 && a.Length >= 3)
        return $"IfConditionGroup(result={Cond(a[0])}, state={OnOff(a[1])}, target={Cond(a[2])})";
    if (b == 0 && id == 1 && a.Length >= 12)
        return $"IfParamComparison(result={Cond(a[0])}, cmp={I32(a,4)}, left={I32(a,4)}, right={I32(a,8)})";

    // ── Wait (1001:xxx) ──
    if (b == 1001 && id == 0 && a.Length >= 4)
        return $"WaitSeconds({F32(a, 0)})";
    if (b == 1001 && id == 1 && a.Length >= 4)
        return $"WaitFrames({I32(a, 0)})";
    if (b == 1001 && id == 6 && a.Length >= 4)
        return $"WaitRealFrames({I32(a, 0)})";

    // ── Flow control (1000:xxx) ──
    if (b == 1000 && id == 0 && a.Length >= 2) return $"WaitForCondGroup(state={OnOff(a[0])}, cond={Cond(a[1])})";
    if (b == 1000 && id == 2 && a.Length >= 3) return $"EndIfCondGroup(end={EndType(a[0])}, state={OnOff(a[1])}, cond={Cond(a[2])})";
    if (b == 1000 && id == 3 && a.Length >= 1) return $"Skip({a[0]})";
    if (b == 1000 && id == 4 && a.Length >= 1) return $"End({EndType(a[0])})";
    if (b == 1000 && id == 7 && a.Length >= 3) return $"SkipIfCondGroup_Compiled(skip={a[0]}, state={OnOff(a[1])}, cond={Cond(a[2])})";
    if (b == 1000 && id == 8 && a.Length >= 3) return $"EndIfCondGroup_Compiled(end={EndType(a[0])}, state={OnOff(a[1])}, cond={Cond(a[2])})";
    if (b == 1000 && id == 101 && a.Length >= 3) return $"GotoIfCondGroup(label={a[0]}, state={OnOff(a[1])}, cond={Cond(a[2])})";
    if (b == 1000 && id == 103 && a.Length >= 1) return $"Goto(label={a[0]})";
    // GotoIfComparison: Label(b@0) pad(3) Cmp(i32@4) Left(i32@8) Right(i32@12)
    if (b == 1000 && id == 105 && a.Length >= 16)
        return $"GotoIfComparison(label={a[0]}, cmp={I32(a,4)}, left={I32(a,8)}, right={I32(a,12)})";
    if (b == 1000 && id == 6 && a.Length >= 16)
        return $"EndIfComparison(end={EndType(a[0])}, cmp={I32(a,4)}, left={I32(a,8)}, right={I32(a,12)})";
    if (b == 1000 && id == 5 && a.Length >= 16)
        return $"SkipIfComparison(skip={a[0]}, cmp={I32(a,4)}, left={I32(a,8)}, right={I32(a,12)})";

    // ── Flag flow (1003:xxx) ──
    // Layout: byte fields, then u32 FlagID aligned to offset 4
    if (b == 1003 && id == 0 && a.Length >= 8)
        return $"WaitForEventFlag(state={OnOff(a[0])}, flag={U32(a, 4)})";
    if (b == 1003 && id == 1 && a.Length >= 8)
        return $"SkipIfEventFlag(skip={a[0]}, state={OnOff(a[1])}, flag={U32(a, 4)})";
    if (b == 1003 && id == 2 && a.Length >= 8)
        return $"EndIfEventFlag(end={EndType(a[0])}, state={OnOff(a[1])}, flag={U32(a, 4)})";
    if (b == 1003 && id == 4 && a.Length >= 12)
        return $"EndIfBatchEventFlags(end={EndType(a[0])}, state={OnOff(a[1])}, range={U32(a,4)}-{U32(a,8)})";
    if (b == 1003 && id == 12 && a.Length >= 2)
        return $"SkipIfPlayerInWorldType(skip={a[0]}, worldType={a[1]})";
    if (b == 1003 && id == 14 && a.Length >= 2)
        return $"EndIfPlayerInWorldType(end={EndType(a[0])}, worldType={a[1]})";
    if (b == 1003 && id == 101 && a.Length >= 8)
        return $"GotoIfEventFlag(label={a[0]}, state={OnOff(a[1])}, flag={U32(a, 4)})";
    if (b == 1003 && id == 103 && a.Length >= 12)
        return $"GotoIfBatchEventFlags(label={a[0]}, state={OnOff(a[1])}, range={U32(a,4)}-{U32(a,8)})";

    // ── Event flag conditions (3:xxx) ──
    // Layout: CondGroup is i32 at [0], then byte fields, then u32 aligned
    // 3:0 IfEventFlag: cond(i32@0) state(b@4) type(b@5) pad(2) flagID(u32@8) — 12 bytes
    if (b == 3 && id == 0 && a.Length >= 12)
        return $"IfEventFlag(cond={Cond(a[0])}, state={OnOff(a[4])}, flag={U32(a, 8)})";
    // Fallback for 8-byte variant (seen in some compiled events with different packing)
    if (b == 3 && id == 0 && a.Length >= 8 && a.Length < 12)
        return $"IfEventFlag(cond={Cond(a[0])}, state={OnOff(a[1])}, flag={U32(a, 4)})";
    if (b == 3 && id == 1 && a.Length >= 16)
        return $"IfBatchEventFlags(cond={Cond(a[0])}, state={OnOff(a[4])}, range={U32(a,8)}-{U32(a,12)})";
    if (b == 3 && id == 2 && a.Length >= 16)
        return $"IfInOutArea(cond={Cond(a[0])}, state={OnOff(a[4])}, entity={U32(a,8)}, area={U32(a,12)})";
    if (b == 3 && id == 4 && a.Length >= 12)
        return $"IfHasItem(cond={Cond(a[0])}, type={a[4]}, item={I32(a,8)})";
    if (b == 3 && id == 24 && a.Length >= 12)
        return $"IfActionButton(cond={I32(a,0)}, button={I32(a,4)}, entity={U32(a,8)})";
    if (b == 3 && id == 26 && a.Length >= 8)
        return $"IfPlayerInWorldType(cond={I32(a,0)}, worldType={a[4]})";
    if (b == 3 && id == 30 && a.Length >= 8)
        return $"IfMapLoaded(cond={I32(a,0)}, map=m{a[4]}_{a[5]:D2}_{a[6]:D2}_{a[7]:D2})";

    // ── Character conditions (4:xxx) ──
    if (b == 4 && id == 0 && a.Length >= 9)
        return $"IfCharDeadAlive(cond={I32(a,0)}, entity={U32(a,4)}, dead={a[8]})";
    if (b == 4 && id == 5 && a.Length >= 10)
        return $"IfCharHasSpEffect(cond={I32(a,0)}, entity={U32(a,4)}, speffect={I32(a,8)}, has={a[12]})";

    // ── Event init (2000:xxx) ──
    if (b == 2000 && id == 0 && a.Length >= 8)
        return $"InitializeEvent(slot={I32(a,0)}, event={U32(a,4)})";
    if (b == 2000 && id == 2 && a.Length >= 1)
        return $"SetNetworkSync({(a[0]==1?"ON":"OFF")})";
    if (b == 2000 && id == 5)
        return "SaveRequest";
    if (b == 2000 && id == 6 && a.Length >= 8)
        return $"InitializeCommonEvent(unk={I32(a,0)}, event={U32(a,4)})";

    // ── Cutscene/warp (2002:xxx) ──
    if (b == 2002 && (id == 11 || id == 12) && a.Length >= 16)
    {
        int cutscene = I32(a, 0);
        int region = I32(a, 8);
        int mapPacked = I32(a, 12);
        byte ma = (byte)(mapPacked / 1000000);
        byte mb = (byte)((mapPacked % 1000000) / 10000);
        byte mc = (byte)((mapPacked % 10000) / 100);
        byte md = (byte)(mapPacked % 100);
        return $"CutsceneWarp(cutscene={cutscene}, region={region}, map=m{ma}_{mb:D2}_{mc:D2}_{md:D2})";
    }

    // ── Actions (2003:xxx) ──
    if (b == 2003 && id == 4 && a.Length >= 4) return $"AwardItemLot({I32(a,0)})";
    if (b == 2003 && id == 6 && a.Length >= 5) return $"ChangeMapHitEnable({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2003 && id == 11 && a.Length >= 10) return $"DisplayBossHP(on={I32(a,0)}, entity={U32(a,4)}, nameId={I32(a,8)})";
    if (b == 2003 && id == 12 && a.Length >= 5) return $"HandleBossDefeat(entity={U32(a,0)}, banner={a[4]})";
    if (b == 2003 && id == 14 && a.Length >= 8)
        return $"WarpPlayer(m{a[0]}_{a[1]:D2}_{a[2]:D2}_{a[3]:D2}, region={U32(a,4)})";
    if (b == 2003 && id == 22 && a.Length >= 9)
        return $"BatchSetEventFlags({U32(a,0)}-{U32(a,4)}, {OnOff(a[8])})";
    if (b == 2003 && id == 23 && a.Length >= 4) return $"SetRespawnPoint({U32(a,0)})";
    if (b == 2003 && id == 43 && a.Length >= 5) return $"DirectlyGiveItem(type={a[0]}, item={I32(a,4)})";
    if (b == 2003 && id == 66 && a.Length >= 9)
        return $"SetEventFlag({U32(a,4)}, {OnOff(a[8])})";
    if (b == 2003 && id == 69 && a.Length >= 9)
        return $"SetNetworkEventFlag({U32(a,4)}, {OnOff(a[8])})";
    if (b == 2003 && id == 80 && a.Length >= 1)
        return $"ShowLoadingText({(a[0]==0?"OFF":"ON")})";

    // ── Character actions (2004:xxx) ──
    if (b == 2004 && id == 1 && a.Length >= 5) return $"SetCharAI({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2004 && id == 4 && a.Length >= 5) return $"ForceCharDeath({U32(a,0)}, runes={OnOff(a[4])})";
    if (b == 2004 && id == 5 && a.Length >= 5) return $"SetCharEnable({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2004 && id == 7 && a.Length >= 4) return $"CreateBulletOwner({U32(a,0)})";
    if (b == 2004 && id == 8 && a.Length >= 8) return $"SetSpEffect({U32(a,0)}, {I32(a,4)})";
    if (b == 2004 && id == 14 && a.Length >= 12) return $"RotateChar({U32(a,0)}, target={U32(a,4)})";
    if (b == 2004 && id == 15 && a.Length >= 5) return $"SetCharInvincibility({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2004 && id == 21 && a.Length >= 8) return $"ClearSpEffect({U32(a,0)}, {I32(a,4)})";
    if (b == 2004 && id == 34 && a.Length >= 6) return $"SetNetworkUpdateRate({U32(a,0)}, fixed={a[4]})";
    if (b == 2004 && id == 39 && a.Length >= 5) return $"SetCharCollision({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2004 && id == 77 && a.Length >= 8) return $"FadeToBlack(ratio={F32(a,0)}, time={F32(a,4)})";

    // ── Asset actions (2005:xxx) ──
    if (b == 2005 && id == 3 && a.Length >= 5) return $"SetAssetEnable({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2005 && id == 4 && a.Length >= 5) return $"SetAssetTreasure({U32(a,0)}, {OnOff(a[4])})";
    if (b == 2005 && id == 7 && a.Length >= 8) return $"ReproduceAssetAnim({U32(a,0)}, anim={I32(a,4)})";

    // ── Display (2007:xxx) ──
    if (b == 2007 && id == 1 && a.Length >= 8) return $"DisplayDialog(msg={I32(a,0)}, entity={U32(a,8)})";
    if (b == 2007 && id == 2 && a.Length >= 1) return $"DisplayBanner(type={a[0]})";
    if (b == 2007 && id == 10 && a.Length >= 20)
        return $"DisplayDialogSetFlags(msg={I32(a,0)}, entity={U32(a,8)}, left={U32(a,12)}, right={U32(a,16)})";

    // ── Sound (2010:xxx) ──
    if (b == 2010 && id == 2 && a.Length >= 12) return $"PlaySE({U32(a,0)}, type={I32(a,4)}, id={I32(a,8)})";
    if (b == 2010 && id == 10 && a.Length >= 8) return $"SetBossBGM({I32(a,0)}, state={I32(a,4)})";

    return "";
}

static bool IsWarpInstruction(EMEVD.Instruction ins)
{
    // WarpPlayer (2003:14)
    if (ins.Bank == 2003 && ins.ID == 14) return true;
    // CutsceneWarp (2002:11/12)
    if (ins.Bank == 2002 && (ins.ID == 11 || ins.ID == 12)) return true;
    return false;
}

// ─── Helpers ───────────────────────────────────────────────────────────

static List<string> GetFiles(string target, string? mapFilter)
{
    var files = new List<string>();
    if (Directory.Exists(target))
        files.AddRange(Directory.GetFiles(target, "*.emevd.dcx"));
    else if (File.Exists(target))
        files.Add(target);

    if (mapFilter != null)
        files = files.Where(f => Path.GetFileName(f)
            .StartsWith(mapFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    return files.OrderBy(f => f).ToList();
}

static void PrintParams(EMEVD.Event evt)
{
    if (evt.Parameters.Count == 0) return;
    Console.WriteLine($"  Parameters ({evt.Parameters.Count}):");
    foreach (var p in evt.Parameters)
        Console.WriteLine($"    InstrIdx={p.InstructionIndex}, src={p.SourceStartByte}→tgt={p.TargetStartByte}, len={p.ByteCount}");
}

static void DumpInitParams(byte[] data, int offset)
{
    int slot = BitConverter.ToInt32(data, 0);
    uint evtId = BitConverter.ToUInt32(data, 4);
    Console.Write($"  slot={slot}, eventId={evtId}");
    for (int off = offset; off + 3 < data.Length; off += 4)
    {
        int val = BitConverter.ToInt32(data, off);
        Console.Write($", p[{off - offset}]={val}");
    }
    Console.WriteLine();
}

static bool HasParam(EMEVD.Event evt, int instrIdx)
{
    foreach (var p in evt.Parameters)
        if (p.InstructionIndex == instrIdx) return true;
    return false;
}

static string Hex(byte[] data) => data.Length > 0 ? BitConverter.ToString(data) : "";
static string OnOff(byte v) => v == 1 ? "ON" : v == 0 ? "OFF" : $"?{v}";
static string EndType(byte v) => v == 0 ? "End" : v == 1 ? "Restart" : $"?{v}";
static string Cond(byte v) {
    sbyte s = (sbyte)v;
    if (s == 0) return "MAIN";
    if (s > 0 && s <= 7) return $"AND_{s:D2}";
    if (s >= -7 && s < 0) return $"OR_{(-s):D2}";
    return $"cond_{s}";
}
static int I32(byte[] a, int off) => BitConverter.ToInt32(a, off);
static uint U32(byte[] a, int off) => BitConverter.ToUInt32(a, off);
static float F32(byte[] a, int off) => BitConverter.ToSingle(a, off);

/// <summary>
/// Returns true if this instruction's flag references are already handled by
/// the specialized decoders in DoSearch. Used to suppress brute-force false positives.
/// </summary>
static bool IsDecodedFlagInstruction(EMEVD.Instruction ins)
{
    int b = ins.Bank, id = ins.ID;

    // SetEventFlag, SetNetworkEventFlag
    if (b == 2003 && (id == 66 || id == 69)) return true;
    // BatchSetEventFlags, BatchSetNetworkEventFlags
    if (b == 2003 && (id == 22 || id == 63)) return true;
    // IfEventFlag, IfBatchEventFlags
    if (b == 3 && (id == 0 || id == 1)) return true;
    // WaitForEventFlag, SkipIfEventFlag, EndIfEventFlag, GotoIfEventFlag, GotoIfBatchEventFlags
    if (b == 1003 && (id == 0 || id == 1 || id == 2 || id == 101 || id == 103)) return true;

    return false;
}
