"""Race items catalog loader.

The catalog (data/plugins/race_items.toml) is read here to build the
graph.json ``race_items`` value, consumed by speedfog-racing. The C#
RaceItems plugin reads the same TOML for the regulation rows, EMEVD events
and texts.
"""

from __future__ import annotations

import tomllib
from pathlib import Path
from typing import Any

# Reserved ranges, see docs/event-flags.md.
ID_RANGE = range(1450800, 1450900)  # EquipParamGoods and SpEffectParam
USE_FLAG_RANGE = range(1050290100, 1050290200)
REMOVE_FLAG_RANGE = range(1050290200, 1050290300)


def _check_range(label: str, key: str, value: Any, allowed: range) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value not in allowed:
        raise ValueError(
            f"race_items: {key}: {label} {value!r} outside "
            f"{allowed.start}-{allowed.stop - 1}"
        )


def load_race_items_catalog(path: Path) -> dict[str, dict[str, Any]]:
    """Load the race items catalog and return the graph.json ``race_items`` value.

    Raises:
        ValueError: on a malformed catalog (duplicate key or goods id, id or
            flag outside its reserved range, use_flag without marker or the
            reverse, unknown icon).
    """
    with path.open("rb") as fp:
        data = tomllib.load(fp)

    icons = data.get("icons", {})
    items = data.get("item", [])
    if not isinstance(icons, dict) or not isinstance(items, list):
        raise ValueError("race_items: [icons] must be a table and [[item]] an array")

    result: dict[str, dict[str, Any]] = {}
    goods_ids: set[int] = set()
    for entry in items:
        try:
            key = entry["key"]
            icon = entry["icon"]
            goods_id = entry["goods_id"]
            remove_flag = entry["remove_flag"]
            name = entry["name"]
        except KeyError as exc:
            raise ValueError(
                f"race_items: entry missing required field {exc}"
            ) from None
        if key in result:
            raise ValueError(f"race_items: duplicate key '{key}'")
        if goods_id in goods_ids:
            raise ValueError(f"race_items: {key}: duplicate goods_id {goods_id}")
        if icon not in icons:
            raise ValueError(f"race_items: {key}: unknown icon '{icon}'")
        _check_range("goods_id", key, goods_id, ID_RANGE)
        _check_range("remove_flag", key, remove_flag, REMOVE_FLAG_RANGE)

        use_flag = entry.get("use_flag")
        if (use_flag is None) != ("marker" not in entry):
            raise ValueError(f"race_items: {key}: use_flag and marker go together")
        if use_flag is not None:
            _check_range("use_flag", key, use_flag, USE_FLAG_RANGE)

        effect = entry.get("effect")
        effect_id = effect["id"] if effect is not None else None
        if effect_id is not None:
            _check_range("effect id", key, effect_id, ID_RANGE)

        duration_s = entry.get("duration_s")
        goods_ids.add(goods_id)
        result[key] = {
            "name": name,
            "icon": icon,
            "goods_id": goods_id,
            "use_flag": use_flag,
            "remove_flag": remove_flag,
            "effect_speffect": effect_id,
            "duration_ms": None if duration_s is None else int(duration_s * 1000),
        }
    return result
