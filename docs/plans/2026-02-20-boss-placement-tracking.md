# Boss Placement Tracking Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** When boss randomization is enabled, capture which boss the randomizer placed in each arena and expose it per-node in graph.json for spoiler/visualization.

**Architecture:** C# captures stdout from `Randomizer.Randomize()`, parses `Replacing` lines to extract entity IDs and boss names, writes `boss_placements.json`. Python reads this file after the randomizer runs, matches entity IDs to cluster defeat_flags, and patches graph.json nodes with `randomized_boss` fields.

**Tech Stack:** C# (.NET 8, System.Text.RegularExpressions, System.Text.Json), Python 3.10+ (json, pathlib)

---

### Task 1: C# — BossPlacementParser class

Extract boss placement parsing into a testable static class.

**Files:**
- Create: `writer/ItemRandomizerWrapper/BossPlacementParser.cs`
- Test: `writer/ItemRandomizerWrapper.Tests/BossPlacementParserTests.cs`

**Step 1: Write the failing test**

In `writer/ItemRandomizerWrapper.Tests/BossPlacementParserTests.cs`:

```csharp
using System.Text.Json;
using ItemRandomizerWrapper;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

public class BossPlacementParserTests
{
    [Fact]
    public void Parse_BossLine_ExtractsPlacement()
    {
        var lines = new[]
        {
            "-- Boss placements",
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Single(result);
        Assert.True(result.ContainsKey("14000850"));
        Assert.Equal("Rennala Queen of the Full Moon", result["14000850"].Name);
        Assert.Equal(14000800, result["14000850"].EntityId);
    }

    [Fact]
    public void Parse_WithScaling_ExtractsPlacement()
    {
        var lines = new[]
        {
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria (scaling 5->3)",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Single(result);
        Assert.Equal("Rennala Queen of the Full Moon", result["14000850"].Name);
    }

    [Fact]
    public void Parse_MultipleBosses_ExtractsAll()
    {
        var lines = new[]
        {
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria",
            "Replacing Rennala Queen of the Full Moon (#14000800) in Raya Lucaria: Godrick the Grafted (#14000850) from Stormveil Castle",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Parse_NonReplacingLines_Ignored()
    {
        var lines = new[]
        {
            "-- Boss placements",
            "Some other output",
            "(not randomized)",
            "",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_CategoryPrefix_IncludedInName()
    {
        // Some bosses get a category prefix like "Black Phantom" from the randomizer
        var lines = new[]
        {
            "Replacing Crucible Knight (#10000850) in Evergaol: Night Black Phantom Crucible Knight (#10000860) from Night Arena",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Equal("Night Black Phantom Crucible Knight", result["10000850"].Name);
    }

    [Fact]
    public void Serialize_ProducesExpectedJson()
    {
        var placements = new Dictionary<string, BossPlacement>
        {
            ["14000850"] = new BossPlacement { Name = "Rennala", EntityId = 14000800 },
        };

        var json = BossPlacementParser.Serialize(placements);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, BossPlacement>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Rennala", deserialized["14000850"].Name);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `cd writer && dotnet test ItemRandomizerWrapper.Tests --filter "BossPlacementParser" -v n`
Expected: Build error — `BossPlacementParser` doesn't exist yet.

**Step 3: Write the implementation**

In `writer/ItemRandomizerWrapper/BossPlacementParser.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ItemRandomizerWrapper;

public class BossPlacement
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("entity_id")]
    public int EntityId { get; set; }
}

public static class BossPlacementParser
{
    // Matches: "Replacing {name} (#{target_id}) in {loc}: {source_name} (#{source_id}) from {loc}..."
    private static readonly Regex ReplacingPattern = new(
        @"^Replacing .+ \(#(\d+)\) in .+: (.+) \(#(\d+)\) from ",
        RegexOptions.Compiled);

    public static Dictionary<string, BossPlacement> Parse(IEnumerable<string> lines)
    {
        var placements = new Dictionary<string, BossPlacement>();

        foreach (var line in lines)
        {
            var match = ReplacingPattern.Match(line);
            if (!match.Success) continue;

            var targetId = match.Groups[1].Value;
            var sourceName = match.Groups[2].Value;
            var sourceId = int.Parse(match.Groups[3].Value);

            placements[targetId] = new BossPlacement
            {
                Name = sourceName,
                EntityId = sourceId,
            };
        }

        return placements;
    }

    public static string Serialize(Dictionary<string, BossPlacement> placements)
    {
        return JsonSerializer.Serialize(placements, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `cd writer && dotnet test ItemRandomizerWrapper.Tests --filter "BossPlacementParser" -v n`
Expected: All 6 tests PASS.

**Step 5: Commit**

```bash
git add writer/ItemRandomizerWrapper/BossPlacementParser.cs writer/ItemRandomizerWrapper.Tests/BossPlacementParserTests.cs
git commit -m "feat: add BossPlacementParser to extract boss placements from randomizer output"
```

---

### Task 2: C# — Wire parser into Program.cs

Capture stdout during `Randomize()`, parse it, write `boss_placements.json`.

**Files:**
- Modify: `writer/ItemRandomizerWrapper/Program.cs:173-206` (RunRandomizer method)

**Step 1: Modify RunRandomizer to capture and parse stdout**

In `Program.cs`, replace the `Randomize()` call block (lines 173-206) with:

```csharp
        // Capture Console.Out during randomization to parse boss placements
        var originalOut = Console.Out;
        var capturedOutput = new StringWriter();
        var teeWriter = new TeeTextWriter(originalOut, capturedOutput);
        Console.SetOut(teeWriter);

        try
        {
            var randomizer = new Randomizer();
            randomizer.Randomize(
                opt,
                GameSpec.FromGame.ER,
                notify: status => Console.Error.WriteLine($"  {status}"),
                outPath: config.OutputDir,
                preset: preset,
                itemPreset: itemPreset,
                messages: null,
                gameExe: Path.Combine(config.GameDir, "eldenring.exe")
            );
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Parse boss placements from captured output
        var capturedLines = capturedOutput.ToString().Split('\n', StringSplitOptions.TrimEntries);
        var placements = BossPlacementParser.Parse(capturedLines);

        if (placements.Count > 0)
        {
            var placementsPath = Path.Combine(config.OutputDir, "boss_placements.json");
            File.WriteAllText(placementsPath, BossPlacementParser.Serialize(placements));
            Console.WriteLine($"Boss placements: {placements.Count} bosses randomized");
            Console.WriteLine($"Written: {placementsPath}");
        }
```

Note: `notify` callback changed to `Console.Error.WriteLine` since Console.Out is now captured. The notify messages are status updates ("Randomizing enemies", "Randomizing items") that should still be visible but shouldn't pollute the captured output.

**Step 2: Create TeeTextWriter helper**

The `TeeTextWriter` writes to both the original console AND the capture buffer, so the user still sees output in real-time. Create `writer/ItemRandomizerWrapper/TeeTextWriter.cs`:

```csharp
using System.Text;

namespace ItemRandomizerWrapper;

/// <summary>
/// TextWriter that writes to two outputs simultaneously.
/// Used to capture Console.Out while still displaying to the user.
/// </summary>
public class TeeTextWriter : TextWriter
{
    private readonly TextWriter _primary;
    private readonly TextWriter _secondary;

    public TeeTextWriter(TextWriter primary, TextWriter secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public override Encoding Encoding => _primary.Encoding;

    public override void Write(char value)
    {
        _primary.Write(value);
        _secondary.Write(value);
    }

    public override void Write(string? value)
    {
        _primary.Write(value);
        _secondary.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _primary.WriteLine(value);
        _secondary.WriteLine(value);
    }

    public override void Flush()
    {
        _primary.Flush();
        _secondary.Flush();
    }
}
```

**Step 3: Build to verify compilation**

Run: `cd writer/ItemRandomizerWrapper && dotnet build`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add writer/ItemRandomizerWrapper/Program.cs writer/ItemRandomizerWrapper/TeeTextWriter.cs
git commit -m "feat: capture boss placements and write boss_placements.json"
```

---

### Task 3: Python — Boss placement loading and graph patching

**Files:**
- Modify: `speedfog/output.py` (add `load_boss_placements`, `patch_graph_boss_placements`)
- Modify: `tests/test_output.py` (add tests)

**Context:** `export_json()` runs BEFORE the item randomizer in `main.py`. Rather than reorganizing the flow, we post-patch graph.json after the randomizer runs. This keeps the existing flow intact.

**Step 1: Write the failing tests**

Append to `tests/test_output.py`:

```python
from speedfog.output import load_boss_placements, patch_graph_boss_placements


class TestBossPlacementLoading:
    def test_load_boss_placements_returns_dict(self, tmp_path):
        path = tmp_path / "boss_placements.json"
        path.write_text('{"14000850": {"name": "Rennala", "entity_id": 14000800}}')

        result = load_boss_placements(path)

        assert result == {"14000850": {"name": "Rennala", "entity_id": 14000800}}

    def test_load_boss_placements_missing_file_returns_empty(self, tmp_path):
        result = load_boss_placements(tmp_path / "nonexistent.json")

        assert result == {}


class TestPatchGraphBossPlacements:
    def test_patch_matches_defeat_flag(self, tmp_path):
        """Node with defeat_flag matching a placement key gets randomized_boss."""
        graph = {
            "nodes": {
                "stormveil_godrick": {
                    "type": "major_boss",
                    "display_name": "Stormveil Castle",
                }
            }
        }
        graph_path = tmp_path / "graph.json"
        graph_path.write_text(json.dumps(graph))

        placements = {"14000850": {"name": "Rennala", "entity_id": 14000800}}

        # Build a minimal DAG with matching defeat_flag
        dag = Dag(seed=1)
        cluster = make_cluster(
            "stormveil_godrick", cluster_type="major_boss", exit_fogs=[]
        )
        cluster.defeat_flag = 14000850
        dag.add_node(
            DagNode(
                id="n1", cluster=cluster, layer=0, tier=1,
                entry_fogs=[], exit_fogs=[],
            )
        )

        patch_graph_boss_placements(graph_path, dag, placements)

        patched = json.loads(graph_path.read_text())
        assert patched["nodes"]["stormveil_godrick"]["randomized_boss"] == "Rennala"

    def test_patch_200m_offset_match(self, tmp_path):
        """Radahn/Fire Giant: defeat_flag = entity_id + 200_000_000."""
        graph = {
            "nodes": {
                "radahn_arena": {
                    "type": "major_boss",
                    "display_name": "Radahn Arena",
                }
            }
        }
        graph_path = tmp_path / "graph.json"
        graph_path.write_text(json.dumps(graph))

        # Entity ID = 1_052_380_800, defeat_flag = 1_252_380_800
        placements = {"1052380800": {"name": "Fire Giant", "entity_id": 99999}}

        dag = Dag(seed=1)
        cluster = make_cluster("radahn_arena", cluster_type="major_boss", exit_fogs=[])
        cluster.defeat_flag = 1_252_380_800  # entity_id + 200M
        dag.add_node(
            DagNode(
                id="n1", cluster=cluster, layer=0, tier=1,
                entry_fogs=[], exit_fogs=[],
            )
        )

        patch_graph_boss_placements(graph_path, dag, placements)

        patched = json.loads(graph_path.read_text())
        assert patched["nodes"]["radahn_arena"]["randomized_boss"] == "Fire Giant"

    def test_patch_no_match_leaves_node_unchanged(self, tmp_path):
        """Node without matching defeat_flag is not modified."""
        graph = {
            "nodes": {
                "some_dungeon": {
                    "type": "mini_dungeon",
                    "display_name": "Some Dungeon",
                }
            }
        }
        graph_path = tmp_path / "graph.json"
        graph_path.write_text(json.dumps(graph))

        placements = {"99999999": {"name": "SomeBoss", "entity_id": 11111}}

        dag = Dag(seed=1)
        cluster = make_cluster("some_dungeon", exit_fogs=[])
        cluster.defeat_flag = 0  # No defeat flag
        dag.add_node(
            DagNode(
                id="n1", cluster=cluster, layer=0, tier=1,
                entry_fogs=[], exit_fogs=[],
            )
        )

        patch_graph_boss_placements(graph_path, dag, placements)

        patched = json.loads(graph_path.read_text())
        assert "randomized_boss" not in patched["nodes"]["some_dungeon"]

    def test_patch_empty_placements_no_change(self, tmp_path):
        """Empty placements dict leaves graph unchanged."""
        graph = {"nodes": {"a": {"type": "major_boss"}}}
        graph_path = tmp_path / "graph.json"
        graph_path.write_text(json.dumps(graph))

        dag = Dag(seed=1)
        cluster = make_cluster("a", cluster_type="major_boss", exit_fogs=[])
        cluster.defeat_flag = 14000850
        dag.add_node(
            DagNode(
                id="n1", cluster=cluster, layer=0, tier=1,
                entry_fogs=[], exit_fogs=[],
            )
        )

        patch_graph_boss_placements(graph_path, dag, {})

        patched = json.loads(graph_path.read_text())
        assert "randomized_boss" not in patched["nodes"]["a"]
```

**Step 2: Run tests to verify they fail**

Run: `pytest tests/test_output.py -k "BossPlacement" -v`
Expected: ImportError — `load_boss_placements` and `patch_graph_boss_placements` don't exist.

**Step 3: Write the implementation**

Add to `speedfog/output.py`, after the existing imports:

```python
def load_boss_placements(path: Path) -> dict[str, dict[str, Any]]:
    """Load boss_placements.json written by ItemRandomizerWrapper.

    Args:
        path: Path to boss_placements.json

    Returns:
        Dictionary of target_entity_id (str) -> {"name": str, "entity_id": int}
        Empty dict if file doesn't exist.
    """
    if not path.exists():
        return {}
    with open(path, encoding="utf-8") as f:
        data: dict[str, Any] = json.load(f)
    return data


def patch_graph_boss_placements(
    graph_path: Path,
    dag: Dag,
    placements: dict[str, dict[str, Any]],
) -> None:
    """Patch graph.json nodes with randomized boss names.

    Matches each node's cluster.defeat_flag against the entity IDs in placements.
    For most bosses, defeat_flag == entity_id. For Radahn and Fire Giant,
    defeat_flag == entity_id + 200_000_000.

    Args:
        graph_path: Path to existing graph.json to patch
        dag: The DAG with cluster defeat_flags
        placements: Boss placements from load_boss_placements()
    """
    if not placements:
        return

    with open(graph_path, encoding="utf-8") as f:
        graph: dict[str, Any] = json.load(f)

    nodes = graph.get("nodes", {})

    for node in dag.nodes.values():
        defeat_flag = node.cluster.defeat_flag
        if defeat_flag == 0:
            continue

        boss_name = _match_boss_placement(defeat_flag, placements)
        if boss_name and node.cluster.id in nodes:
            nodes[node.cluster.id]["randomized_boss"] = boss_name

    with open(graph_path, "w", encoding="utf-8") as f:
        json.dump(graph, f, indent=2)


def _match_boss_placement(
    defeat_flag: int, placements: dict[str, dict[str, Any]]
) -> str | None:
    """Match a defeat_flag to a boss placement entry.

    Args:
        defeat_flag: Cluster's DefeatFlag from fog.txt
        placements: Boss placements keyed by entity ID string

    Returns:
        Boss name if matched, None otherwise.
    """
    key = str(defeat_flag)
    if key in placements:
        return str(placements[key]["name"])

    # Radahn/Fire Giant: defeat_flag = entity_id + 200_000_000
    if 1_200_000_000 <= defeat_flag < 2_000_000_000:
        key = str(defeat_flag - 200_000_000)
        if key in placements:
            return str(placements[key]["name"])

    return None
```

**Step 4: Update the import in test file**

Add `import json` and `load_boss_placements, patch_graph_boss_placements` to the existing imports from `speedfog.output` in `tests/test_output.py`.

**Step 5: Run tests to verify they pass**

Run: `pytest tests/test_output.py -k "BossPlacement" -v`
Expected: All 6 tests PASS.

**Step 6: Commit**

```bash
git add speedfog/output.py tests/test_output.py
git commit -m "feat: add boss placement loading and graph patching"
```

---

### Task 4: Python — Wire into main.py and spoiler log

**Files:**
- Modify: `speedfog/main.py:290-303` (after item randomizer call)
- Modify: `speedfog/output.py` (spoiler log function)

**Step 1: Add boss placement patching to main.py**

After the item randomizer success block (line 297: `item_rando_output = item_rando_dir`), before the `else` on line 298, add:

```python
            # Patch graph.json with boss placements if available
            boss_placements_path = item_rando_dir / "boss_placements.json"
            boss_placements = load_boss_placements(boss_placements_path)
            if boss_placements:
                patch_graph_boss_placements(json_path, dag, boss_placements)
                print(f"Boss placements: {len(boss_placements)} bosses randomized")

                # Append boss placements to spoiler log
                if args.spoiler:
                    append_boss_placements_to_spoiler(spoiler_path, boss_placements)
```

Update the imports at the top of `main.py`:

```python
from speedfog.output import (
    export_json,
    export_spoiler_log,
    load_boss_placements,
    load_fog_data,
    load_vanilla_tiers,
    patch_graph_boss_placements,
    append_boss_placements_to_spoiler,
)
```

**Step 2: Add `append_boss_placements_to_spoiler` to output.py**

Add after `export_spoiler_log()`:

```python
def append_boss_placements_to_spoiler(
    spoiler_path: Path,
    placements: dict[str, dict[str, Any]],
) -> None:
    """Append boss placement section to an existing spoiler log.

    Args:
        spoiler_path: Path to existing spoiler.txt
        placements: Boss placements from load_boss_placements()
    """
    if not placements or not spoiler_path.exists():
        return

    lines: list[str] = []
    lines.append("")
    lines.append("=" * 60)
    lines.append("BOSS PLACEMENTS (randomized)")
    lines.append("=" * 60)

    for target_id, info in sorted(placements.items()):
        lines.append(f"  Arena #{target_id} -> {info['name']} (#{info['entity_id']})")

    with open(spoiler_path, "a", encoding="utf-8") as f:
        f.write("\n".join(lines))
        f.write("\n")
```

**Step 3: Build and run full test suite**

Run: `pytest -v`
Expected: All tests PASS.

**Step 4: Commit**

```bash
git add speedfog/main.py speedfog/output.py
git commit -m "feat: integrate boss placement tracking into main pipeline and spoiler log"
```

---

### Task 5: Verification — End-to-end check

**Step 1: Run C# tests**

Run: `cd writer && dotnet test -v n`
Expected: All tests pass (including new BossPlacementParserTests).

**Step 2: Run Python tests**

Run: `pytest -v`
Expected: All tests pass.

**Step 3: Verify with `--no-build` (graph.json without boss placements)**

Run: `uv run speedfog config.toml --spoiler --no-build`
Expected: graph.json written without `randomized_boss` fields. No errors.

**Step 4: Commit any fixes if needed**
