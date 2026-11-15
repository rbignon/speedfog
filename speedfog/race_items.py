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
# Goods ids (and their marker SpEffects) sit below the item effect SpEffects,
# so a marker never takes an effect's id.
GOODS_ID_RANGE = range(1450800, 1450820)
EFFECT_ID_RANGE = range(1450820, 1450850)  # Item effect SpEffects
USE_FLAG_RANGE = range(1050290100, 1050290200)
REMOVE_FLAG_RANGE = range(1050290200, 1050290300)
ICON_ID_RANGE = range(60385, 60415)


def _check_range(label: str, key: str, value: Any, allowed: range) -> None:
    if not isinstance(value, int) or isinstance(value, bool) or value not in allowed:
        raise ValueError(
            f"race_items: {key}: {label} {value!r} outside "
            f"{allowed.start}-{allowed.stop - 1}"
        )


def _required(entry: dict[str, Any], *fields: str) -> list[Any]:
    try:
        return [entry[field] for field in fields]
    except KeyError as exc:
        raise ValueError(f"race_items: entry missing required field {exc}") from None


def load_race_items_catalog(path: Path) -> dict[str, dict[str, dict[str, Any]]]:
    """Load the race items catalog and return the graph.json ``race_items`` value.

    The value is ``{"goods": {key: goods}, "items": {key: item}}``: the goods
    rows the game holds (``goods_id``, ``use_flag``, ``remove_flag``, ``icon``,
    ``icon_id``) and the items that point at one of them (``name``, ``icon``,
    ``icon_id``, ``goods``, ``effect_speffect``, ``duration_ms``).

    Raises:
        ValueError: on a malformed catalog (missing field, duplicate key,
            goods id or effect id, id or flag outside its reserved range,
            use_flag without marker or the reverse, unknown icon or goods,
            effect on a goods that cannot be used).
    """
    with path.open("rb") as fp:
        data = tomllib.load(fp)

    icons = data.get("icons", {})
    goods_entries = data.get("goods", [])
    item_entries = data.get("item", [])
    if (
        not isinstance(icons, dict)
        or not isinstance(goods_entries, list)
        or not isinstance(item_entries, list)
    ):
        raise ValueError(
            "race_items: [icons] must be a table, [[goods]] and [[item]] arrays"
        )

    seen_icon_ids: dict[int, str] = {}
    for icon_name, icon_id in icons.items():
        _check_range("icon id", icon_name, icon_id, ICON_ID_RANGE)
        if icon_id in seen_icon_ids:
            raise ValueError(
                f"race_items: icons '{icon_name}' and '{seen_icon_ids[icon_id]}' "
                f"share id {icon_id}"
            )
        seen_icon_ids[icon_id] = icon_name

    goods: dict[str, dict[str, Any]] = {}
    goods_ids: set[int] = set()
    for entry in goods_entries:
        # name, info and caption are the goods' texts, written by the C#
        # plugin: required here too, so both loaders reject the same catalogs.
        key, icon, goods_id, remove_flag, *_texts = _required(
            entry, "key", "icon", "goods_id", "remove_flag", "name", "info", "caption"
        )
        if key in goods:
            raise ValueError(f"race_items: duplicate goods key '{key}'")
        if goods_id in goods_ids:
            raise ValueError(f"race_items: {key}: duplicate goods_id {goods_id}")
        if icon not in icons:
            raise ValueError(f"race_items: {key}: unknown icon '{icon}'")
        _check_range("goods_id", key, goods_id, GOODS_ID_RANGE)
        _check_range("remove_flag", key, remove_flag, REMOVE_FLAG_RANGE)

        use_flag = entry.get("use_flag")
        if (use_flag is None) != ("marker" not in entry):
            raise ValueError(f"race_items: {key}: use_flag and marker go together")
        if use_flag is not None:
            _check_range("use_flag", key, use_flag, USE_FLAG_RANGE)

        goods_ids.add(goods_id)
        goods[key] = {
            "goods_id": goods_id,
            "use_flag": use_flag,
            "remove_flag": remove_flag,
            "icon": icon,
            "icon_id": icons[icon],
        }

    items: dict[str, dict[str, Any]] = {}
    effect_ids: set[int] = set()
    for entry in item_entries:
        key, goods_key, icon, name = _required(entry, "key", "goods", "icon", "name")
        if key in items:
            raise ValueError(f"race_items: duplicate item key '{key}'")
        if goods_key not in goods:
            raise ValueError(f"race_items: {key}: unknown goods '{goods_key}'")
        if icon not in icons:
            raise ValueError(f"race_items: {key}: unknown icon '{icon}'")

        effect = entry.get("effect")
        effect_id = effect["id"] if effect is not None else None
        if effect_id is not None:
            if goods[goods_key]["use_flag"] is None:
                raise ValueError(
                    f"race_items: {key}: effect on goods '{goods_key}', "
                    "which cannot be used"
                )
            _check_range("effect id", key, effect_id, EFFECT_ID_RANGE)
            if effect_id in effect_ids:
                raise ValueError(f"race_items: {key}: duplicate effect id {effect_id}")
            effect_ids.add(effect_id)

        duration_s = entry.get("duration_s")
        items[key] = {
            "name": name,
            "icon": icon,
            "icon_id": icons[icon],
            "goods": goods_key,
            "effect_speffect": effect_id,
            "duration_ms": None if duration_s is None else int(duration_s * 1000),
        }
    return {"goods": goods, "items": items}
