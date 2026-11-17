"""Race items catalog loader.

The catalog (data/plugins/race_items.toml) is read here to build the
graph.json ``race_items`` value, consumed by speedfog-racing. The C#
RaceItems plugin reads the same TOML for the regulation rows, EMEVD events
and texts.
"""

from __future__ import annotations

import math
import tomllib
from pathlib import Path
from typing import Any

# Reserved ranges, see docs/event-flags.md.
# Goods ids (and their marker SpEffects) sit below the item effect SpEffects,
# so a marker never takes an effect's id.
GOODS_ID_RANGE = range(1450800, 1450820)
EFFECT_ID_RANGE = range(1450820, 1450850)  # Item effect SpEffects
# Flags of goods n (n = goods_id - 1450800): base + n. Same values as
# SpeedFogIds.RaceItemUseFlagBase / RaceItemRemoveFlagBase.
USE_FLAG_BASE = 1050290100
REMOVE_FLAG_BASE = 1050290200
ICON_ID_RANGE = range(60385, 60415)
NPC_PARAM_RANGE = range(1450850, 1450900)  # NpcParam and NpcThinkParam
# One Hunter NpcParam row per tier, tiers 1 to 34.
HUNTER_TIERS = 34
# Removal flag of talisman n of race_items_talismans.toml: base + n. Same value
# as SpeedFogIds.RaceItemTalismanRemoveFlagBase.
TALISMAN_FLAG_BASE = 1050290300
TALISMAN_FLAG_COUNT = 200
TALISMANS_FILE = "race_items_talismans.toml"

# The keys each table accepts; any other one is rejected, so a typo cannot
# silently drop an effect or a duration. Mirrored by the C# loader
# (RaceItemsCatalog), and both loaders replay tests/fixtures/race_items/.
_TOP_KEYS = {"icons", "goods", "item", "junk_weapon", "hunter"}
_GOODS_KEYS = {"key", "icon", "name", "info", "caption", "goods_id", "marker"}
_ITEM_KEYS = {"key", "goods", "icon", "name", "duration_s", "effect"}
_HUNTER_KEYS = {
    "chr_id",
    "source_npc_param",
    "source_think",
    "npc_param_base",
    "think_param",
}
_NAMED_ID_KEYS = {"id", "name"}


def _load_toml(path: Path) -> dict[str, Any]:
    # utf-8-sig: a BOM left by a Windows editor is not a syntax error (the C#
    # loader's File.ReadAllText drops it too).
    return tomllib.loads(path.read_text(encoding="utf-8-sig"))


def _is_number(value: Any) -> bool:
    """A finite TOML number (not a boolean, inf or nan)."""
    return (
        isinstance(value, int | float)
        and not isinstance(value, bool)
        and math.isfinite(value)
    )


def _check_keys(table: dict[str, Any], allowed: set[str], where: str) -> None:
    for key in table:
        if key not in allowed:
            raise ValueError(f"race_items: {where}: unknown key '{key}'")


def _tables(data: dict[str, Any], name: str) -> list[dict[str, Any]]:
    """The tables of the ``[[name]]`` array, none when absent."""
    entries = data.get(name, [])
    if not isinstance(entries, list) or not all(isinstance(e, dict) for e in entries):
        raise ValueError(f"race_items: [[{name}]] must be an array of tables")
    return entries


def _int(entry: dict[str, Any], field: str) -> int:
    if field not in entry:
        raise ValueError(f"race_items: entry missing required field '{field}'")
    value = entry[field]
    if not isinstance(value, int) or isinstance(value, bool):
        raise ValueError(
            f"race_items: field '{field}' must be integer, got {type(value).__name__}"
        )
    # Every id is a 32-bit cell or flag in game, and the C# loader reads an int.
    if not -(2**31) <= value < 2**31:
        raise ValueError(
            f"race_items: field '{field}' must be integer, got out-of-range {value}"
        )
    return value


def _str(entry: dict[str, Any], field: str) -> str:
    if field not in entry:
        raise ValueError(f"race_items: entry missing required field '{field}'")
    value = entry[field]
    if not isinstance(value, str):
        raise ValueError(f"race_items: field '{field}' must be a string")
    return value


def _check_range(label: str, key: str, value: int, allowed: range) -> None:
    if value not in allowed:
        raise ValueError(
            f"race_items: {key}: {label} {value!r} outside "
            f"{allowed.start}-{allowed.stop - 1}"
        )


def _positive_id(label: str, value: int) -> None:
    if value <= 0:
        raise ValueError(f"race_items: {label} id {value!r} must be positive")


def _speffect(entry: dict[str, Any], field: str, key: str, *, own_id: bool) -> int:
    """Validate a ``{ [id,] template, fields }`` inline table; return its id.

    The marker has no id of its own (``own_id`` false: 0 is returned, its id
    is the goods id). The row itself is written by the C# plugin.
    """
    table = entry[field]
    if not isinstance(table, dict):
        raise ValueError(f"race_items: {key}: '{field}' must be a table")
    allowed = {"template", "fields"} | ({"id"} if own_id else set())
    _check_keys(table, allowed, f"{key}: {field}")
    effect_id = _int(table, "id") if own_id else 0
    _int(table, "template")
    fields = table.get("fields", {})
    if not isinstance(fields, dict):
        raise ValueError(f"race_items: {key}: '{field}.fields' must be a table")
    for name, value in fields.items():
        if not _is_number(value):
            raise ValueError(
                f"race_items: {key}: '{field}.fields.{name}' must be numeric, "
                f"got {type(value).__name__}"
            )
    return effect_id


def _named_ids(
    label: str,
    entries: list[dict[str, Any]],
    *,
    ascending: bool,
    limit: int | None = None,
) -> list[dict[str, Any]]:
    """Validate ``[[label]]`` entries of ``{id, name}`` and return them."""
    if limit is not None and len(entries) > limit:
        raise ValueError(
            f"race_items: {len(entries)} {label}s, more than the {limit} reserved"
        )
    out: list[dict[str, Any]] = []
    seen: set[int] = set()
    for entry in entries:
        _check_keys(entry, _NAMED_ID_KEYS, label)
        entry_id, name = _int(entry, "id"), _str(entry, "name")
        _positive_id(label, entry_id)
        if not name:
            raise ValueError(f"race_items: {label} {entry_id}: empty name")
        if ascending and out and entry_id <= out[-1]["id"]:
            raise ValueError(
                f"race_items: {label} {entry_id} after {out[-1]['id']}: "
                "ids must be strictly ascending"
            )
        if entry_id in seen:
            raise ValueError(f"race_items: duplicate {label} id {entry_id}")
        seen.add(entry_id)
        out.append({"id": entry_id, "name": name})
    return out


def _load_hunter(hunter: Any) -> dict[str, Any] | None:
    if hunter is None:
        return None
    if not isinstance(hunter, dict):
        raise ValueError("race_items: [hunter] must be a table")
    _check_keys(hunter, _HUNTER_KEYS, "hunter")
    chr_id = _int(hunter, "chr_id")
    # The source rows are read by the C# writer only: required here too, so a
    # catalog without them fails before the DAG is generated.
    _int(hunter, "source_npc_param")
    _int(hunter, "source_think")
    base = _int(hunter, "npc_param_base")
    think = _int(hunter, "think_param")
    _positive_id("hunter chr", chr_id)
    _check_range("npc_param_base", "hunter", base, NPC_PARAM_RANGE)
    _check_range("last tier row", "hunter", base + HUNTER_TIERS - 1, NPC_PARAM_RANGE)
    _check_range("think_param", "hunter", think, NPC_PARAM_RANGE)
    return {
        "chr_id": chr_id,
        "think_param": think,
        "npc_params": list(range(base, base + HUNTER_TIERS)),
    }


def load_race_items_catalog(path: Path) -> dict[str, Any]:
    """Load the race items catalog and return the graph.json ``race_items`` value.

    The value is ``{"goods": {key: goods}, "items": {key: item}, "talismans":
    [...], "junk_weapons": [...], "hunter": {...} | None}``: the goods rows
    the game holds (``goods_id``, ``use_flag``, ``remove_flag``, ``icon``,
    ``icon_id``), the items that point at one of them (``name``, ``icon``,
    ``icon_id``, ``goods``, ``effect_speffect``, ``duration_ms``), the
    talisman table (``id``, ``name``, ``flag``) read from the sibling
    ``race_items_talismans.toml`` (empty when absent), the junk weapons
    (``id``, ``name``) and the Hunter's ``chr_id``, ``think_param`` and
    ``npc_params`` (one row per tier).

    A goods' flags are not in the catalog: ``remove_flag`` is
    ``REMOVE_FLAG_BASE + n`` and ``use_flag`` ``USE_FLAG_BASE + n`` (``None``
    for a goods without a ``marker``, which cannot be used), n being
    ``goods_id - 1450800``.

    Raises:
        ValueError: on a malformed catalog (unknown key, missing or mistyped
            field, duplicate key, goods id or effect id, id outside its
            reserved range, unknown icon or goods, effect on a goods that
            cannot be used, malformed marker or effect table, non-positive
            duration, talismans not strictly ascending, an empty name, a
            non-positive or duplicate id, more talismans than reserved flags,
            a Hunter row outside its range).
    """
    data = _load_toml(path)
    _check_keys(data, _TOP_KEYS, "top level")

    icons = data.get("icons", {})
    if not isinstance(icons, dict):
        raise ValueError("race_items: [icons] must be a table")

    seen_icon_ids: dict[int, str] = {}
    for icon_name in icons:
        icon_id = _int(icons, icon_name)
        _check_range("icon id", icon_name, icon_id, ICON_ID_RANGE)
        if icon_id in seen_icon_ids:
            raise ValueError(
                f"race_items: icons '{icon_name}' and '{seen_icon_ids[icon_id]}' "
                f"share id {icon_id}"
            )
        seen_icon_ids[icon_id] = icon_name

    goods: dict[str, dict[str, Any]] = {}
    goods_ids: set[int] = set()
    for entry in _tables(data, "goods"):
        key = _str(entry, "key")
        _check_keys(entry, _GOODS_KEYS, key)
        icon = _str(entry, "icon")
        if icon not in icons:
            raise ValueError(f"race_items: {key}: unknown icon '{icon}'")
        goods_id = _int(entry, "goods_id")
        _check_range("goods_id", key, goods_id, GOODS_ID_RANGE)
        usable = "marker" in entry
        if usable:
            _speffect(entry, "marker", key, own_id=False)
        # name, info and caption are the goods' texts, written by the C#
        # plugin: required here too, so both loaders reject the same catalogs.
        for text in ("name", "info", "caption"):
            _str(entry, text)
        if key in goods:
            raise ValueError(f"race_items: duplicate goods key '{key}'")
        if goods_id in goods_ids:
            raise ValueError(f"race_items: {key}: duplicate goods_id {goods_id}")

        n = goods_id - GOODS_ID_RANGE.start
        goods_ids.add(goods_id)
        goods[key] = {
            "goods_id": goods_id,
            "use_flag": USE_FLAG_BASE + n if usable else None,
            "remove_flag": REMOVE_FLAG_BASE + n,
            "icon": icon,
            "icon_id": icons[icon],
        }

    items: dict[str, dict[str, Any]] = {}
    effect_ids: set[int] = set()
    for entry in _tables(data, "item"):
        key = _str(entry, "key")
        _check_keys(entry, _ITEM_KEYS, key)
        goods_key = _str(entry, "goods")
        if goods_key not in goods:
            raise ValueError(f"race_items: {key}: unknown goods '{goods_key}'")
        icon = _str(entry, "icon")
        if icon not in icons:
            raise ValueError(f"race_items: {key}: unknown icon '{icon}'")

        effect_id = None
        if "effect" in entry:
            if goods[goods_key]["use_flag"] is None:
                raise ValueError(
                    f"race_items: {key}: effect on goods '{goods_key}', "
                    "which cannot be used"
                )
            effect_id = _speffect(entry, "effect", key, own_id=True)
            _check_range("effect id", key, effect_id, EFFECT_ID_RANGE)

        name = _str(entry, "name")
        duration_s = entry.get("duration_s")
        # At least 1 ms once rounded: the export is in milliseconds.
        if duration_s is not None and (
            not _is_number(duration_s) or round(duration_s * 1000) < 1
        ):
            raise ValueError(
                f"race_items: {key}: duration_s must be a finite number of "
                "at least 1 ms"
            )

        if key in items:
            raise ValueError(f"race_items: duplicate item key '{key}'")
        if effect_id is not None:
            if effect_id in effect_ids:
                raise ValueError(f"race_items: {key}: duplicate effect id {effect_id}")
            effect_ids.add(effect_id)
        items[key] = {
            "name": name,
            "icon": icon,
            "icon_id": icons[icon],
            "goods": goods_key,
            "effect_speffect": effect_id,
            "duration_ms": None if duration_s is None else round(duration_s * 1000),
        }

    talismans_path = path.parent / TALISMANS_FILE
    talisman_data: dict[str, Any] = {}
    if talismans_path.exists():
        talisman_data = _load_toml(talismans_path)
        _check_keys(talisman_data, {"talisman"}, "talismans")
    talismans = [
        {**talisman, "flag": TALISMAN_FLAG_BASE + index}
        for index, talisman in enumerate(
            _named_ids(
                "talisman",
                _tables(talisman_data, "talisman"),
                ascending=True,
                limit=TALISMAN_FLAG_COUNT,
            )
        )
    ]
    junk_weapons = _named_ids(
        "junk weapon", _tables(data, "junk_weapon"), ascending=False
    )
    return {
        "goods": goods,
        "items": items,
        "talismans": talismans,
        "junk_weapons": junk_weapons,
        "hunter": _load_hunter(data.get("hunter")),
    }
