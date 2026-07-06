"""Tests for extract_fog_data.py (fog.txt -> fog_data.json extraction)."""

from __future__ import annotations

import json

import pytest
from extract_fog_data import (
    FogEntry,
    entries_to_json,
    extract_from_debug_info,
    extract_model_from_name,
    parse_fog_entry,
    parse_fog_txt,
    parse_makefrom,
    validate_against_clusters,
)

# --- extract_model_from_name ---


def test_extract_model_from_named_fog():
    assert extract_model_from_name("AEG099_002_9000") == "AEG099_002"
    assert extract_model_from_name("AEG099_230_9001") == "AEG099_230"


def test_extract_model_from_numeric_name_is_empty():
    assert extract_model_from_name("1034471610") == ""


def test_extract_model_from_bare_model_name_is_empty():
    # No instance suffix: not a valid asset name
    assert extract_model_from_name("AEG099_002") == ""


# --- extract_from_debug_info ---


def test_debug_info_full_asset_name():
    model, asset = extract_from_debug_info(
        "asset 10001800 (m10_00_00_00 (Stormveil Castle) AEG099_002_9000)"
    )
    assert model == "AEG099_002"
    assert asset == "AEG099_002_9000"


def test_debug_info_model_without_suffix():
    model, asset = extract_from_debug_info("asset 123 (somewhere AEG099_510)")
    assert model == "AEG099_510"
    assert asset == ""


def test_debug_info_list_uses_first_entry():
    model, asset = extract_from_debug_info(
        ["asset 1 (m60 AEG099_510_9000)", "asset 2 (m61 AEG099_511_9000)"]
    )
    assert asset == "AEG099_510_9000"


def test_debug_info_empty():
    assert extract_from_debug_info("") == ("", "")
    assert extract_from_debug_info([]) == ("", "")


# --- parse_makefrom ---


def test_parse_makefrom_minimal():
    model, position, rotation = parse_makefrom(
        "AEG099_170 AEG027_041_0500 -63.656 51.250 68.100 -90.000"
    )
    assert model == "AEG099_170"
    assert position == [-63.656, 51.25, 68.1]
    # Only rot_y given: rot_x and rot_z default to 0
    assert rotation == [0.0, -90.0, 0.0]


def test_parse_makefrom_full_rotation():
    model, position, rotation = parse_makefrom(
        "AEG099_170 AEG441_150_1000 -111.483 207.7 14.804 177.14 -10.306 -2.607"
    )
    assert rotation == [-10.306, 177.14, -2.607]


def test_parse_makefrom_too_few_parts_raises():
    with pytest.raises(ValueError, match="Invalid MakeFrom format"):
        parse_makefrom("AEG099_170 AEG027_041_0500 -63.656")


# --- parse_fog_entry ---


def _entrance_raw(**overrides) -> dict:
    raw = {
        "Name": "AEG099_002_9000",
        "ID": 10001800,
        "Area": "m10_00_00_00",
        "ASide": {"Area": "stormveil"},
        "BSide": {"Area": "stormveil_start"},
    }
    raw.update(overrides)
    return raw


def test_parse_named_entrance():
    entry = parse_fog_entry(_entrance_raw(), "Entrances")

    assert entry is not None
    assert entry.fog_type == "entrance"
    assert entry.fog_id == "AEG099_002_9000"
    assert entry.entity_id == 10001800
    assert entry.model == "AEG099_002"
    assert entry.asset_name == "AEG099_002_9000"
    assert entry.lookup_by == "name"
    assert entry.zones == ["stormveil", "stormveil_start"]
    assert entry.position is None


def test_parse_entry_without_name_returns_none():
    assert parse_fog_entry({"ID": 123}, "Entrances") is None


def test_parse_numeric_warp_uses_location_and_debug_info():
    raw = {
        "Name": "1034471610",
        "ID": 999,
        "Location": 1034471610,
        "Area": "m60_34_47_00",
        "ASide": {"Area": "liurnia"},
        "BSide": {"Area": "liurnia", "DestinationMap": "m60_35_47_00"},
        "DebugInfo": "asset 1034471610 (m60_34_47_00 (Liurnia) AEG099_510_9000)",
    }
    entry = parse_fog_entry(raw, "Warps")

    assert entry is not None
    assert entry.fog_type == "warp"
    # Location takes precedence over ID
    assert entry.entity_id == 1034471610
    assert entry.lookup_by == "entity_id"
    assert entry.model == "AEG099_510"
    assert entry.asset_name == "AEG099_510_9000"
    assert entry.destination_map == "m60_35_47_00"
    # Same zone on both sides is not duplicated
    assert entry.zones == ["liurnia"]


def test_parse_makefrom_entry():
    raw = _entrance_raw(
        Name="AEG099_170_9500",
        MakeFrom="AEG099_170 AEG027_041_0500 -63.656 51.250 68.100 -90.000",
    )
    entry = parse_fog_entry(raw, "Entrances")

    assert entry is not None
    assert entry.fog_type == "makefrom"
    assert entry.lookup_by is None
    assert entry.model == "AEG099_170"
    assert entry.asset_name == "AEG099_170_9500"
    assert entry.position == [-63.656, 51.25, 68.1]
    assert entry.rotation == [0.0, -90.0, 0.0]


def test_parse_adjust_heights():
    raw = _entrance_raw(
        AdjustHeight=1.5,
        ASide={"Area": "stormveil", "AdjustHeight": 0.5},
        BSide={"Area": "stormveil_start", "AdjustHeight": -0.25},
    )
    entry = parse_fog_entry(raw, "Entrances")

    assert entry is not None
    assert entry.entrance_adjust_height == 1.5
    assert entry.aside_adjust_height == 0.5
    assert entry.bside_adjust_height == -0.25


# --- entries_to_json: key deduplication ---


def _entry(fog_id: str, map_id: str, zone: str = "stormveil") -> FogEntry:
    return FogEntry(
        fog_id=fog_id,
        fog_type="entrance",
        aside_zone=zone,
        bside_zone="",
        map_id=map_id,
        entity_id=1000,
        model="AEG099_002",
        asset_name=fog_id,
        lookup_by="name",
    )


def test_entries_to_json_unique_name_gets_plain_and_prefixed_keys():
    data = entries_to_json([_entry("AEG099_002_9000", "m10_00_00_00")])

    fogs = data["fogs"]
    assert data["duplicate_names_handled"] == 0
    # Both lookups resolve to the same entry
    assert fogs["AEG099_002_9000"] == fogs["m10_00_00_00_AEG099_002_9000"]


def test_entries_to_json_duplicate_name_uses_map_prefixed_key():
    data = entries_to_json(
        [
            _entry("AEG099_002_9000", "m10_00_00_00", zone="stormveil"),
            _entry("AEG099_002_9000", "m14_00_00_00", zone="academy"),
        ]
    )

    fogs = data["fogs"]
    assert data["duplicate_names_handled"] == 1
    # Plain key belongs to the first occurrence
    assert fogs["AEG099_002_9000"]["zones"] == ["stormveil"]
    assert fogs["m10_00_00_00_AEG099_002_9000"]["zones"] == ["stormveil"]
    # Second occurrence only reachable via its map-prefixed key
    assert fogs["m14_00_00_00_AEG099_002_9000"]["zones"] == ["academy"]


def test_entries_to_json_sums_adjust_heights_per_side():
    entry = _entry("AEG099_002_9000", "m10_00_00_00")
    entry.entrance_adjust_height = 1.0
    entry.aside_adjust_height = 0.5
    entry.bside_adjust_height = -0.5

    data = entries_to_json([entry])

    assert data["fogs"]["AEG099_002_9000"]["adjust_heights"] == [1.5, 0.5]


# --- parse_fog_txt ---


def test_parse_fog_txt_reads_both_sections(tmp_path):
    fog_txt = tmp_path / "fog.txt"
    fog_txt.write_text(
        """
Entrances:
- Name: AEG099_002_9000
  ID: 10001800
  Area: m10_00_00_00
  ASide:
    Area: stormveil
  BSide:
    Area: stormveil_start
Warps:
- Name: 1034471610
  ID: 1034471610
  Area: m60_34_47_00
  ASide:
    Area: liurnia
  BSide:
    Area: liurnia
"""
    )

    entries = parse_fog_txt(fog_txt)

    assert [e.fog_type for e in entries] == ["entrance", "warp"]
    assert entries[0].fog_id == "AEG099_002_9000"
    assert entries[1].entity_id == 1034471610


# --- validate_against_clusters ---


def _write_clusters(tmp_path, entry_fogs, exit_fogs=()):
    clusters_path = tmp_path / "clusters.json"
    clusters_path.write_text(
        json.dumps(
            {
                "clusters": [
                    {
                        "id": "test_cluster",
                        "entry_fogs": list(entry_fogs),
                        "exit_fogs": list(exit_fogs),
                    }
                ]
            }
        )
    )
    return clusters_path


def test_validate_resolves_plain_key(tmp_path):
    fogs = entries_to_json([_entry("AEG099_002_9000", "m10_00_00_00")])["fogs"]
    clusters_path = _write_clusters(
        tmp_path, [{"fog_id": "AEG099_002_9000", "zone": "stormveil"}]
    )

    assert validate_against_clusters(fogs, clusters_path) == []


def test_validate_resolves_duplicate_via_zone_context(tmp_path):
    fogs = entries_to_json(
        [
            _entry("AEG099_002_9000", "m10_00_00_00", zone="stormveil"),
            _entry("AEG099_002_9000", "m14_00_00_00", zone="academy"),
        ]
    )["fogs"]
    # The academy copy is only reachable through its map-prefixed key
    clusters_path = _write_clusters(
        tmp_path, [{"fog_id": "AEG099_002_9000", "zone": "academy"}]
    )

    assert validate_against_clusters(fogs, clusters_path) == []


def test_validate_reports_missing_fog_with_zone(tmp_path):
    fogs = entries_to_json([_entry("AEG099_002_9000", "m10_00_00_00")])["fogs"]
    clusters_path = _write_clusters(
        tmp_path,
        [{"fog_id": "AEG099_999_9000", "zone": "caelid"}],
        exit_fogs=[{"fog_id": "AEG099_999_9000", "zone": "caelid"}],
    )

    missing = validate_against_clusters(fogs, clusters_path)

    # Reported once despite appearing as both entry and exit
    assert missing == ["AEG099_999_9000 (zone=caelid)"]


def test_validate_zone_mismatch_is_missing(tmp_path):
    fogs = entries_to_json([_entry("AEG099_002_9000", "m10_00_00_00")])["fogs"]
    clusters_path = _write_clusters(
        tmp_path, [{"fog_id": "AEG099_002_9000", "zone": "caelid"}]
    )

    missing = validate_against_clusters(fogs, clusters_path)

    assert missing == ["AEG099_002_9000 (zone=caelid)"]
