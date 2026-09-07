# Static Mod Sources

This directory holds source files that `tools/bootstrap.py` repacks into
`data/mods/speedfog/` with WitchyBND (see `build_static_mod_scripts()` in
`tools/bootstrap.py`).

Layout: one WitchyBND-unpacked directory per archive under `speedfog/`, e.g.
`speedfog/script/<name>-luabnd-dcx/` containing the unpacked files plus the
`_witchy-bnd4.xml` manifest. Bootstrap repacks each such directory into the
corresponding `.dcx` under `data/mods/speedfog/`.

A seed ships the built `.dcx`, so a source edited after the last bootstrap
would run stale in game with nothing to show for it: `uv run speedfog`
refuses to build a seed while a built script is missing or older than any
file of its source directory (`speedfog.packaging.stale_static_mod_scripts`),
and names the bootstrap step to rerun. The repack alone, without the rest
of the bootstrap:

```bash
uv run python -c "import sys; sys.path.insert(0, 'tools'); import bootstrap; sys.exit(0 if bootstrap.build_static_mod_scripts() else 1)"
```

The check compares modification times, so a `git checkout` or `stash pop`
that rewrites a source file trips it even when the content is unchanged;
one repack clears it.

Current content:

- `speedfog/script/755890_battle-luabnd-dcx/`: the Aging Untouchable boss
  battle AI (plain-text Lua, loaded by the game because the boss's
  NpcThinkParam clone sets `battleGoalID = 755890`; ambient untouchables keep
  the vanilla bytecode `528000_battle`). Regenerate the baseline with
  WitchyBND (`--passive Game/script/528000_battle.luabnd.dcx`) and
  DSLuaDecompiler, then re-apply the SpeedFog edits (the pristine
  decompile is not in git; `docs/untouchable-boss.md` "AI script"
  describes the design): the header comment, the two `GOAL_`
  assignments, the knobs and helpers above `Goal.Initialize`, the
  rewritten brackets, `SetCoolTime` weights and swing zeroing of
  `Goal.Activate`, Act01's `successDist` local (the decompile declares
  `f3_local11` and passes an undefined global), Act02, Act03, Act04,
  Act05/Act06, Act11, the far teleport's block in the interrupt and the
  reactions at the end of `Goal.Interrupt`.

The Rykard AI script that once lived here was reverted in commit c3f921d
("Revert 'overlay: import Rykard AI script'"); the plain-text Lua + manifest
layout is the same.
