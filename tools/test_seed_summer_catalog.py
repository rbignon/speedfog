import json

from seed_summer_catalog import build_summer_skeleton, major_entity_ids, parse_npc_names


def test_build_skeleton_filters_to_major_with_npcname():
    boss_tags = {
        "10000850": {"name": "Margit The Fell Omen"},
        "10000800": {"name": "Godrick the Grafted"},
        "99999999": {"name": "No NpcName Boss"},
    }
    npc_by_entity = {10000850: 902130000, 10000800: 904750000}
    major = {10000850, 10000800, 99999999, 12345678}  # last not in tags

    out = build_summer_skeleton(boss_tags, npc_by_entity, major)

    assert {"npc_name_id": 904750000, "name": "Godrick the Grafted"} in out
    assert {"npc_name_id": 902130000, "name": "Margit The Fell Omen"} in out
    assert len(out) == 2  # boss without NpcName and id absent from tags dropped


def test_build_skeleton_dedupes_npcname():
    boss_tags = {"1": {"name": "A"}, "2": {"name": "B"}}
    npc_by_entity = {1: 500, 2: 500}  # same NpcName
    out = build_summer_skeleton(boss_tags, npc_by_entity, {1, 2})
    assert len(out) == 1
    # lower entity id (1) wins the dedup; its name is "A"
    assert out[0] == {"npc_name_id": 500, "name": "A"}


def test_parse_npc_names(tmp_path):
    enemy = tmp_path / "enemy.txt"
    enemy.write_text(
        "- ID: 10000850\n"
        "  PartName: Margit\n"
        "  NpcName: 902130000\n"
        "- ID: 10000800\n"
        "  NpcName: 904750000\n"
    )
    assert parse_npc_names(enemy) == {10000850: 902130000, 10000800: 904750000}


def test_parse_npc_names_no_stale_state(tmp_path):
    """NpcName should not bleed into the next entry if the block has two NpcName lines."""
    enemy = tmp_path / "enemy.txt"
    enemy.write_text(
        "- ID: 1\n"
        "  NpcName: 100\n"
        "  NpcName: 200\n"  # second line: current already cleared, should be ignored
        "- ID: 2\n"
        "  NpcName: 300\n"
    )
    result = parse_npc_names(enemy)
    assert result[1] == 100  # first NpcName wins; second is ignored
    assert result[2] == 300


def test_major_entity_ids(tmp_path):
    clusters = {
        "clusters": [
            {
                "type": "major_boss",
                "zones": [{"entity_id": 10000850}, {"entity_id": 10000800}],
            },
            {
                "type": "legacy_dungeon",
                "zones": [{"entity_id": 99999999}],
            },
        ]
    }
    path = tmp_path / "clusters.json"
    path.write_text(json.dumps(clusters))
    ids = major_entity_ids(path)
    assert ids == {10000850, 10000800}
    assert 99999999 not in ids
