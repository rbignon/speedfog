# Static Mod Sources

This directory holds source files that `tools/bootstrap.py` repacks into
`data/mods/speedfog/` with WitchyBND (see `build_static_mod_scripts()` in
`tools/bootstrap.py`).

Layout: one WitchyBND-unpacked directory per archive under `speedfog/`, e.g.
`speedfog/script/<name>-luabnd-dcx/` containing the unpacked files plus the
`_witchy-bnd4.xml` manifest. Bootstrap repacks each such directory into the
corresponding `.dcx` under `data/mods/speedfog/`.

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
