using SoulsFormats;
using SoulsIds;

/// <summary>
/// Dump ESD (talk script) state machines from a talkesdbnd.dcx archive.
///
/// Modes:
///   dump-esd &lt;talkesdbnd.dcx&gt; &lt;esd-substr&gt;            full dump of all machines/states
///   dump-esd &lt;talkesdbnd.dcx&gt; &lt;esd-substr&gt; --int N    only states referencing int N
///
/// Command args and condition evaluators are disassembled via SoulsIds.AST so
/// menu message IDs and item checks are human-readable.
/// </summary>
static class DumpEsd
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: game_inspect dump-esd <talkesdbnd.dcx> <esd-substr> [--int N]");
            return 1;
        }

        string bndPath = args[1];
        string esdSubstr = args[2];
        int? grepInt = null;
        for (int i = 3; i < args.Length - 1; i++)
            if (args[i] == "--int") grepInt = int.Parse(args[i + 1]);

        var bnd = BND4.Read(bndPath);
        var binderFile = bnd.Files.Find(f => f.Name.Contains(esdSubstr));
        if (binderFile == null)
        {
            Console.Error.WriteLine($"No ESD matching '{esdSubstr}' in {bndPath}");
            Console.Error.WriteLine("Available: " + string.Join(", ", bnd.Files.Select(f => Path.GetFileName(f.Name))));
            return 1;
        }

        Console.WriteLine($"=== {Path.GetFileName(binderFile.Name)} ===");
        var esd = ESD.Read(binderFile.Bytes);

        foreach (var (machineId, states) in esd.StateGroups)
        {
            var matchingStates = new List<KeyValuePair<long, ESD.State>>();
            foreach (var kv in states)
            {
                if (grepInt == null || StateRefsInt(kv.Value, grepInt.Value))
                    matchingStates.Add(kv);
            }
            if (matchingStates.Count == 0) continue;

            Console.WriteLine($"\n-- Machine {machineId} ({states.Count} states) --");
            foreach (var (stateId, state) in matchingStates)
                DumpState(stateId, state);
        }
        return 0;
    }

    static void DumpState(long stateId, ESD.State state)
    {
        Console.WriteLine($"  State {stateId}:");
        DumpCommands("entry", state.EntryCommands);
        DumpCommands("exit", state.ExitCommands);
        DumpCommands("while", state.WhileCommands);
        foreach (var cond in state.Conditions)
            DumpCondition(cond, "    ");
    }

    static void DumpCommands(string label, List<ESD.CommandCall> commands)
    {
        foreach (var c in commands)
        {
            var argStrs = c.Arguments.Select(FormatArg);
            Console.WriteLine($"    [{label}] cmd({c.CommandBank},{c.CommandID})  {string.Join(" | ", argStrs)}");
        }
    }

    static void DumpCondition(ESD.Condition cond, string indent)
    {
        string target = cond.TargetState?.ToString() ?? "(sub)";
        string eval = cond.Evaluator != null ? FormatArg(cond.Evaluator) : "(none)";
        Console.WriteLine($"{indent}cond -> {target}  if {eval}");
        foreach (var pc in cond.PassCommands)
        {
            var argStrs = pc.Arguments.Select(FormatArg);
            Console.WriteLine($"{indent}  [pass] cmd({pc.CommandBank},{pc.CommandID})  {string.Join(" | ", argStrs)}");
        }
        foreach (var sub in cond.Subconditions)
            DumpCondition(sub, indent + "  ");
    }

    static string FormatArg(byte[] bytes)
    {
        try
        {
            var expr = AST.DisassembleExpression(bytes);
            return expr?.ToString() ?? BitConverter.ToString(bytes);
        }
        catch
        {
            return "0x" + BitConverter.ToString(bytes).Replace("-", "");
        }
    }

    static bool StateRefsInt(ESD.State state, int target)
    {
        foreach (var list in new[] { state.EntryCommands, state.ExitCommands, state.WhileCommands })
            foreach (var c in list)
                if (c.Arguments.Any(a => ArgHasInt(a, target)))
                    return true;
        foreach (var cond in state.Conditions)
            if (CondRefsInt(cond, target))
                return true;
        return false;
    }

    static bool CondRefsInt(ESD.Condition cond, int target)
    {
        if (cond.Evaluator != null && ArgHasInt(cond.Evaluator, target)) return true;
        foreach (var pc in cond.PassCommands)
            if (pc.Arguments.Any(a => ArgHasInt(a, target))) return true;
        foreach (var sub in cond.Subconditions)
            if (CondRefsInt(sub, target)) return true;
        return false;
    }

    static bool ArgHasInt(byte[] bytes, int target)
    {
        // Cheap scan: the literal int appears little-endian in the bytecode.
        var needle = BitConverter.GetBytes(target);
        for (int i = 0; i + 4 <= bytes.Length; i++)
            if (bytes[i] == needle[0] && bytes[i + 1] == needle[1] &&
                bytes[i + 2] == needle[2] && bytes[i + 3] == needle[3])
                return true;
        return false;
    }
}
