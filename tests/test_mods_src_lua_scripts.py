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
