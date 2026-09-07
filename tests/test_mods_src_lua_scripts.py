"""Syntax check of the plain-text Lua AI scripts under data/mods-src.

The game compiles them with its Lua 5.0 compiler at load time and reports
nothing usable when one fails (the Untouchable boss would fall back to
aiCommon's errorAct and stand still). No Lua toolchain ships with the
project, so luaparser (a Lua 5.3 parser) stands in: a parse failure is a
real error, and the syntax Lua added after 5.0 is rejected here because the
engine does not know it.
"""

from pathlib import Path

import pytest
from luaparser import ast, astnodes

SCRIPT_ROOT = (
    Path(__file__).resolve().parents[1] / "data" / "mods-src" / "speedfog" / "script"
)
SCRIPTS = sorted(SCRIPT_ROOT.glob("*-luabnd-dcx/*.lua"))

# Syntax added after Lua 5.0 (length operator, modulo, floor division,
# bitwise operators, goto/labels), rejected by the engine's compiler.
POST_LUA_5_0_NODES = (
    astnodes.ULengthOP,
    astnodes.ModOp,
    astnodes.FloorDivOp,
    astnodes.BitOp,
    astnodes.UBNotOp,
    astnodes.Goto,
    astnodes.Label,
)


def test_glob_finds_the_untouchable_script() -> None:
    assert SCRIPT_ROOT / "755890_battle-luabnd-dcx" / "755890_battle.lua" in SCRIPTS


@pytest.mark.parametrize("script", SCRIPTS, ids=lambda p: p.name)
def test_script_parses_as_lua_5_0(script: Path) -> None:
    tree = ast.parse(script.read_text(encoding="utf-8"))
    offending = sorted(
        {
            type(node).__name__
            for node in ast.walk(tree)
            if isinstance(node, POST_LUA_5_0_NODES)
        }
    )
    assert not offending, f"{script.name} uses post-5.0 Lua syntax: {offending}"


# Lua 5.0 allows 32 upvalues per function (MAXUPVALUES). A file-scope local
# read by a function (the boss script's knobs and named ids) is one upvalue
# of that function, nested closures included; a function over the limit
# fails the compilation of the whole script and the boss stands still.
# luaparser cannot compile, so the references are counted here.
LUA_5_0_MAX_UPVALUES = 32


def _file_scope_locals(tree: astnodes.Chunk) -> set[str]:
    names: set[str] = set()
    for node in tree.body.body:
        if isinstance(node, astnodes.LocalAssign):
            names.update(t.id for t in node.targets if isinstance(t, astnodes.Name))
        elif isinstance(node, astnodes.LocalFunction):
            names.add(node.name.id)
    return names


def _function_label(node: astnodes.Node) -> str:
    if isinstance(node, astnodes.Assign):
        return str(ast.to_lua_source(node.targets[0]))
    if isinstance(node, astnodes.Method):
        return f"{ast.to_lua_source(node.source)}:{ast.to_lua_source(node.name)}"
    return str(ast.to_lua_source(node.name))


def _functions(tree: astnodes.Chunk) -> list[tuple[str, astnodes.Node]]:
    """Every function of the chunk with a readable label; anonymous functions
    assigned to a field (Goal.Activate = function ...) take the field's name,
    suffixed when the same field is assigned more than once."""
    found: list[tuple[str, astnodes.Node]] = []
    seen: dict[str, int] = {}
    for node in ast.walk(tree):
        if isinstance(
            node, astnodes.Function | astnodes.LocalFunction | astnodes.Method
        ):
            label, body = _function_label(node), node
        elif (
            isinstance(node, astnodes.Assign)
            and node.values
            and isinstance(node.values[0], astnodes.AnonymousFunction)
        ):
            label, body = _function_label(node), node.values[0]
        else:
            continue
        seen[label] = seen.get(label, 0) + 1
        found.append((label if seen[label] == 1 else f"{label}#{seen[label]}", body))
    return found


def _referenced_file_locals(func: astnodes.Node, file_locals: set[str]) -> set[str]:
    # A parameter or a local shadowing a file-scope name is counted too: an
    # over-estimate, never an under-estimate.
    return {
        node.id
        for node in ast.walk(func)
        if isinstance(node, astnodes.Name) and node.id in file_locals
    }


@pytest.mark.parametrize("script", SCRIPTS, ids=lambda p: p.name)
def test_functions_stay_under_the_lua_5_0_upvalue_limit(script: Path) -> None:
    tree = ast.parse(script.read_text(encoding="utf-8"))
    file_locals = _file_scope_locals(tree)
    counts = {
        label: sorted(_referenced_file_locals(func, file_locals))
        for label, func in _functions(tree)
    }
    if script.name == "755890_battle.lua":
        # The walker sees the knobs (a vacuous count would pass anything).
        assert "GRAB_COOLDOWN" in counts["Goal.Activate"]
        assert "REACT_HIT" in counts["Goal.Interrupt"]
    over = {
        label: len(names)
        for label, names in counts.items()
        if len(names) > LUA_5_0_MAX_UPVALUES
    }
    assert (
        not over
    ), f"{script.name}: functions over {LUA_5_0_MAX_UPVALUES} upvalues: {over}"
