"""Tests for the summer catalogue seeding tool.

The join key between a major-boss cluster and its NpcName FMG id is the
cluster's `defeat_flag` (clusters.json) matched against `DefeatFlag` in the
randomizer enemy.txt, which also carries `NpcName`. clusters.json `zones` are
plain zone-name strings, not objects, and major-boss clusters carry
`boss_name` directly.
"""

from seed_summer_catalog import (
    build_summer_skeleton,
    major_boss_entries,
    parse_defeat_flag_npc_names,
)


def test_parse_defeat_flag_npc_names(tmp_path):
    enemy = tmp_path / "enemy.txt"
    enemy.write_text(
        "- ID: 10000850\n"
        "  DefeatFlag: 10000850\n"
        "  PartName: Margit\n"
        "  NpcName: 902130000\n"
        "- ID: 14000800\n"
        "  DefeatFlag: 14000800\n"
        "  NpcName: 902030001\n"
    )
    assert parse_defeat_flag_npc_names(enemy) == {
        10000850: 902130000,
        14000800: 902030001,
    }


def test_parse_skips_entities_without_both_fields(tmp_path):
    enemy = tmp_path / "enemy.txt"
    enemy.write_text(
        "- ID: 1\n"
        "  DefeatFlag: 100\n"  # no NpcName -> skipped
        "- ID: 2\n"
        "  NpcName: 200\n"  # no DefeatFlag -> skipped
        "- ID: 3\n"
        "  DefeatFlag: 300\n"
        "  NpcName: 303\n"
    )
    assert parse_defeat_flag_npc_names(enemy) == {300: 303}


def test_parse_defeat_flag_first_wins_on_duplicate(tmp_path):
    enemy = tmp_path / "enemy.txt"
    enemy.write_text(
        "- ID: 1\n"
        "  DefeatFlag: 500\n"
        "  NpcName: 111\n"
        "- ID: 2\n"
        "  DefeatFlag: 500\n"
        "  NpcName: 222\n"
    )
    assert parse_defeat_flag_npc_names(enemy) == {500: 111}


def test_major_boss_entries_filters_and_reads_boss_name(tmp_path):
    clusters = tmp_path / "clusters.json"
    clusters.write_text(
        '{"clusters": ['
        '{"type": "major_boss", "defeat_flag": 14000800, "boss_name": "Rennala",'
        ' "zones": ["academy_library", "academy_chest"]},'
        '{"type": "boss_arena", "defeat_flag": 99, "boss_name": "Minor",'
        ' "zones": ["x"]}'
        "]}"
    )
    assert major_boss_entries(clusters) == [
        {"defeat_flag": 14000800, "name": "Rennala"}
    ]


def test_major_boss_entries_falls_back_to_display_name(tmp_path):
    clusters = tmp_path / "clusters.json"
    clusters.write_text(
        '{"clusters": ['
        '{"type": "major_boss", "defeat_flag": 7, "display_name": "Some Arena",'
        ' "zones": ["z"]}'
        "]}"
    )
    assert major_boss_entries(clusters) == [{"defeat_flag": 7, "name": "Some Arena"}]


def test_build_skeleton_joins_on_defeat_flag():
    majors = [
        {"defeat_flag": 14000800, "name": "Rennala"},
        {"defeat_flag": 10000850, "name": "Margit"},
        {"defeat_flag": 999, "name": "No NpcName"},  # not in map -> dropped
    ]
    df_to_npc = {14000800: 902030001, 10000850: 902130000}

    out = build_summer_skeleton(majors, df_to_npc)

    assert {"npc_name_id": 902030001, "name": "Rennala"} in out
    assert {"npc_name_id": 902130000, "name": "Margit"} in out
    assert len(out) == 2


def test_build_skeleton_dedupes_by_npc_name_id():
    majors = [
        {"defeat_flag": 1, "name": "A"},
        {"defeat_flag": 2, "name": "B"},
    ]
    df_to_npc = {1: 500, 2: 500}  # same NpcName id
    out = build_summer_skeleton(majors, df_to_npc)
    assert out == [{"npc_name_id": 500, "name": "A"}]
