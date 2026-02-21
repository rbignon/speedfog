"""Tests for generate_clusters.py"""

from __future__ import annotations

import pytest
from generate_clusters import (
    AreaData,
    Cluster,
    FogData,
    FogSide,
    WorldConnection,
    WorldGraph,
    ZoneFogs,
    _is_side_core,
    build_world_graph,
    classify_fogs,
    compute_allow_entry_as_exit,
    compute_allow_shared_entrance,
    compute_cluster_fogs,
    filter_and_enrich_clusters,
    find_defeat_flag,
    generate_cluster_id,
    generate_clusters,
    get_evergaol_zones,
    get_zone_type,
    is_condition_guaranteed,
    is_warp_edge_active,
    parse_area,
    parse_fog,
    parse_tags,
    should_exclude_area,
)

# =============================================================================
# Parser Tests
# =============================================================================


class TestParseTags:
    """Tests for parse_tags function."""

    def test_parse_string_tags(self):
        """Tags as space-separated string."""
        assert parse_tags("legacy major") == ["legacy", "major"]

    def test_parse_list_tags(self):
        """Tags as list."""
        assert parse_tags(["legacy", "major"]) == ["legacy", "major"]

    def test_parse_empty_tags(self):
        """Empty or None tags."""
        assert parse_tags(None) == []
        assert parse_tags("") == []
        assert parse_tags([]) == []


class TestParseArea:
    """Tests for parse_area function."""

    def test_parse_basic_area(self):
        """Parse a basic area entry."""
        area_data = {
            "Name": "stormveil_start",
            "Text": "Stormveil Castle before Gate",
            "Maps": "m10_00_00_00",
            "Tags": "legacy_dungeon",
        }
        area = parse_area(area_data)

        assert area.name == "stormveil_start"
        assert area.text == "Stormveil Castle before Gate"
        assert area.maps == ["m10_00_00_00"]
        assert area.tags == ["legacy_dungeon"]

    def test_parse_area_with_to_connections(self):
        """Parse area with To: connections."""
        area_data = {
            "Name": "stormveil_start",
            "Text": "Stormveil Castle before Gate",
            "Maps": "m10_00_00_00",
            "To": [
                {
                    "Area": "stormveil",
                    "Text": "with Rusty Key",
                    "Cond": "OR scalepass rustykey",
                    "Tags": "noscalecond",
                }
            ],
        }
        area = parse_area(area_data)

        assert len(area.to_connections) == 1
        conn = area.to_connections[0]
        assert conn.target_area == "stormveil"
        assert conn.cond == "OR scalepass rustykey"
        assert "noscalecond" in conn.tags

    def test_parse_area_with_drop(self):
        """Parse area with drop connection."""
        area_data = {
            "Name": "academy_courtyard",
            "Text": "Academy Courtyard",
            "Maps": "m14_00_00_00",
            "To": [
                {
                    "Area": "academy_redwolf",
                    "Text": "dropping down",
                    "Tags": "drop",
                }
            ],
        }
        area = parse_area(area_data)

        conn = area.to_connections[0]
        assert conn.is_drop is True


class TestParseFog:
    """Tests for parse_fog function."""

    def test_parse_basic_entrance(self):
        """Parse a basic entrance."""
        fog_data = {
            "Name": "AEG099_002_9000",
            "ID": 10001800,
            "Text": "Godrick front",
            "ASide": {
                "Area": "stormveil",
                "Text": "before Godrick's arena",
            },
            "BSide": {
                "Area": "stormveil_godrick",
                "Text": "at the front of Godrick's arena",
                "Tags": "main",
            },
            "Tags": "major",
        }
        fog = parse_fog(fog_data)

        assert fog.name == "AEG099_002_9000"
        assert fog.fog_id == 10001800
        assert fog.text == "Godrick front"
        assert fog.aside.area == "stormveil"
        assert fog.bside.area == "stormveil_godrick"
        assert fog.tags == ["major"]
        assert fog.is_unique is False

    def test_parse_fog_without_text(self):
        """Fog without Text field defaults to empty string."""
        fog_data = {
            "Name": "AEG099_001_9000",
            "ID": 99999,
            "ASide": {"Area": "a"},
            "BSide": {"Area": "b"},
        }
        fog = parse_fog(fog_data)
        assert fog.text == ""

    def test_parse_unique_fog(self):
        """Parse a unique (one-way) fog."""
        fog_data = {
            "Name": "11002697",
            "ID": 11002697,
            "ASide": {"Area": "peninsula", "Text": "Tower of Return"},
            "BSide": {"Area": "leyndell_divinebridge", "Text": "arriving"},
            "Tags": "unique legacy",
        }
        fog = parse_fog(fog_data)

        assert fog.is_unique is True
        assert fog.is_norandom is False

    def test_parse_norandom_fog(self):
        """Parse a non-randomizable fog."""
        fog_data = {
            "Name": "test",
            "ID": 12345,
            "ASide": {"Area": "a"},
            "BSide": {"Area": "b"},
            "Tags": "norandom",
        }
        fog = parse_fog(fog_data)

        assert fog.is_norandom is True

    def test_parse_baseonly_fog(self):
        """Baseonly fogs are excluded (special progression warps)."""
        fog_data = {
            "Name": "test",
            "ID": 12345,
            "ASide": {"Area": "a"},
            "BSide": {"Area": "b"},
            "Tags": "unique baseonly",
        }
        fog = parse_fog(fog_data)

        assert fog.is_norandom is True


class TestFogSideRequiresOwnZone:
    """Tests for FogSide.requires_own_zone method."""

    def test_no_condition_returns_false(self):
        """No Cond field means not a self-requirement."""
        side = FogSide(area="volcano_town", text="", tags=[], cond=None)
        assert side.requires_own_zone() is False

    def test_different_zone_condition_returns_false(self):
        """Cond with different zone is not a self-requirement."""
        side = FogSide(area="volcano_abductors", text="", tags=[], cond="volcano_town")
        assert side.requires_own_zone() is False

    def test_same_zone_condition_returns_true(self):
        """Cond with same zone as Area is a self-requirement (e.g., drops)."""
        # Real example: AEG099_002_9000 ASide has Area=volcano_town, Cond=volcano_town
        # This indicates a one-way drop - you must already be in volcano_town to use it
        side = FogSide(area="volcano_town", text="", tags=[], cond="volcano_town")
        assert side.requires_own_zone() is True

    def test_complex_condition_with_own_zone_returns_true(self):
        """Complex condition containing own zone is a self-requirement."""
        side = FogSide(
            area="test_zone",
            text="",
            tags=[],
            cond="OR test_zone other_zone",
        )
        assert side.requires_own_zone() is True

    def test_complex_condition_without_own_zone_returns_false(self):
        """Complex condition not containing own zone is not a self-requirement."""
        side = FogSide(
            area="test_zone",
            text="",
            tags=[],
            cond="OR zone_a zone_b",
        )
        assert side.requires_own_zone() is False

    def test_case_insensitive(self):
        """Zone name matching is case-insensitive."""
        side = FogSide(area="Volcano_Town", text="", tags=[], cond="volcano_town")
        assert side.requires_own_zone() is True


# =============================================================================
# World Graph Tests
# =============================================================================


class TestIsConditionGuaranteed:
    """Tests for is_condition_guaranteed function."""

    def test_no_condition(self):
        """No condition is always guaranteed."""
        assert is_condition_guaranteed(None, set()) is True

    def test_single_key_item(self):
        """Single key item condition is guaranteed."""
        key_items = {"rustykey", "academyglintstonekey"}
        assert is_condition_guaranteed("rustykey", key_items) is True

    def test_or_key_items(self):
        """OR condition with key items is guaranteed."""
        key_items = {"scalepass", "rustykey"}
        assert is_condition_guaranteed("OR scalepass rustykey", key_items) is True

    def test_complex_condition_with_items(self):
        """Complex nested condition with items is guaranteed."""
        key_items = {"scalepass", "logicpass", "imbued_base", "imbued_base_any"}
        cond = "OR ( AND scalepass imbued_base ) ( AND logicpass imbued_base_any )"
        assert is_condition_guaranteed(cond, key_items) is True

    def test_zone_condition_not_guaranteed(self):
        """Zone condition is NOT guaranteed."""
        key_items = {"rustykey"}
        # "academy_entrance" is a zone, not a key item
        assert is_condition_guaranteed("academy_entrance", key_items) is False


class TestWorldGraph:
    """Tests for WorldGraph class."""

    def test_add_bidirectional_edge(self):
        """Bidirectional edge adds both directions."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)

        assert ("b", True) in graph.edges["a"]
        assert ("a", True) in graph.edges["b"]

    def test_add_unidirectional_edge(self):
        """Unidirectional edge only adds one direction."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=False)

        assert ("b", False) in graph.edges["a"]
        assert "b" not in graph.edges or ("a", False) not in graph.edges["b"]

    def test_has_unidirectional_edge(self):
        """Check unidirectional edge detection."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=False)
        graph.add_edge("c", "d", bidirectional=True)

        assert graph.has_unidirectional_edge("a", "b") is True
        assert graph.has_unidirectional_edge("b", "a") is False
        assert graph.has_unidirectional_edge("c", "d") is False

    def test_get_reachable(self):
        """Test flood-fill reachability."""
        graph = WorldGraph()
        # a -> b (drop)
        # b <-> c (bidirectional)
        graph.add_edge("a", "b", bidirectional=False)
        graph.add_edge("b", "c", bidirectional=True)

        # From a: can reach b and c
        assert graph.get_reachable("a") == {"b", "c"}

        # From b: can reach c (and back from c)
        assert graph.get_reachable("b") == {"c"}

        # From c: can reach b
        assert graph.get_reachable("c") == {"b"}

    def test_get_reachable_within(self):
        """Flood-fill scoped to allowed zones only."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)
        graph.add_edge("b", "c", bidirectional=False)
        graph.add_edge("c", "d", bidirectional=False)

        # Scoped to {a, b, c}: d is excluded even though c->d exists
        result = graph.get_reachable_within({"a"}, frozenset({"a", "b", "c"}))
        assert result == {"a", "b", "c"}

        # Scoped to {a, b}: c is excluded
        result = graph.get_reachable_within({"a"}, frozenset({"a", "b"}))
        assert result == {"a", "b"}

        # Multiple starts
        result = graph.get_reachable_within({"a", "d"}, frozenset({"a", "b", "c", "d"}))
        assert result == {"a", "b", "c", "d"}


class TestBuildWorldGraph:
    """Tests for build_world_graph function."""

    def test_bidirectional_connection(self):
        """Two areas with mutual connections are bidirectional."""
        areas = {
            "a": AreaData(
                name="a",
                text="Area A",
                maps=[],
                tags=[],
                to_connections=[WorldConnection(target_area="b", text="", tags=[])],
            ),
            "b": AreaData(
                name="b",
                text="Area B",
                maps=[],
                tags=[],
                to_connections=[WorldConnection(target_area="a", text="", tags=[])],
            ),
        }
        graph = build_world_graph(areas, set())

        # Both directions should exist and be bidirectional
        assert ("b", True) in graph.edges["a"]
        assert ("a", True) in graph.edges["b"]

    def test_drop_connection(self):
        """Drop tag creates unidirectional connection."""
        areas = {
            "top": AreaData(
                name="top",
                text="Top",
                maps=[],
                tags=[],
                to_connections=[
                    WorldConnection(target_area="bottom", text="", tags=["drop"])
                ],
            ),
            "bottom": AreaData(name="bottom", text="Bottom", maps=[], tags=[]),
        }
        graph = build_world_graph(areas, set())

        assert graph.has_unidirectional_edge("top", "bottom") is True


# =============================================================================
# Fog Classification Tests
# =============================================================================


class TestClassifyFogs:
    """Tests for classify_fogs function."""

    def test_bidirectional_fog(self):
        """Normal fog is entry+exit on both sides."""
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=[],
        )
        zone_fogs = classify_fogs([fog], [])

        assert fog in zone_fogs["zone_a"].entry_fogs
        assert fog in zone_fogs["zone_a"].exit_fogs
        assert fog in zone_fogs["zone_b"].entry_fogs
        assert fog in zone_fogs["zone_b"].exit_fogs

    def test_unique_fog(self):
        """Unique fog: ASide=exit only, BSide has no fog gate (spawn point only)."""
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique"],
        )
        zone_fogs = classify_fogs([fog], [])

        # ASide is exit only - FogMod can redirect where the warp sends you
        assert fog not in zone_fogs["zone_a"].entry_fogs
        assert fog in zone_fogs["zone_a"].exit_fogs

        # BSide is NOT an entry_fog - there's no physical fog gate at destination
        # FogMod doesn't have a "To" edge for one-way warp destinations
        assert "zone_b" not in zone_fogs or fog not in zone_fogs["zone_b"].entry_fogs

    def test_norandom_fog_excluded(self):
        """Norandom fogs are excluded entirely."""
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["norandom"],
        )
        zone_fogs = classify_fogs([fog], [])

        assert (
            "zone_a" not in zone_fogs
            or fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        )
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).entry_fogs
        )

    def test_segmentonly_fog_excluded(self):
        """Segmentonly fogs are excluded (only valid in segmented modes).

        These are warps like Fortissax dream entry/exit that only work in
        FogRando's Segmented modes (Boss Rush, Endless). SpeedFog doesn't
        use segmented mode, so these must be excluded.
        """
        fog = FogData(
            name="12032858",
            fog_id=12032858,
            aside=FogSide(area="deeproot_dream", text="finishing Fortissax fight"),
            bside=FogSide(area="deeproot_boss", text="arriving at throne"),
            tags=["return", "segmentonly", "underground"],
        )
        zone_fogs = classify_fogs([], [fog])

        # Segmentonly fogs should be excluded from all zones
        assert (
            "deeproot_dream" not in zone_fogs
            or fog not in zone_fogs.get("deeproot_dream", ZoneFogs()).entry_fogs
        )
        assert (
            "deeproot_boss" not in zone_fogs
            or fog not in zone_fogs.get("deeproot_boss", ZoneFogs()).entry_fogs
        )

    def test_return_warp_exit_only(self):
        """Return warps are exit-only from ASide, no entry anywhere.

        Return warps (tagged 'return' but not 'returnpair') are post-boss
        return mechanisms, not fog gates for random connections. They should
        only create an exit from ASide, similar to 'unique' warps.
        """
        fog = FogData(
            name="34142852",
            fog_id=34142852,
            aside=FogSide(area="leyndell_tower_boss", text="finishing Fell Twins"),
            bside=FogSide(area="leyndell_tower", text="arriving at tower"),
            tags=["divine", "return"],
        )
        zone_fogs = classify_fogs([], [fog])

        # ASide should have exit only
        assert fog in zone_fogs["leyndell_tower_boss"].exit_fogs
        assert fog not in zone_fogs.get("leyndell_tower_boss", ZoneFogs()).entry_fogs

        # BSide should have nothing (no entry, no exit)
        assert (
            "leyndell_tower" not in zone_fogs
            or fog not in zone_fogs["leyndell_tower"].entry_fogs
        )
        assert (
            "leyndell_tower" not in zone_fogs
            or fog not in zone_fogs["leyndell_tower"].exit_fogs
        )

    def test_uniquegate_pair_coupled(self):
        """Uniquegate fogs connecting same zones are coupled as one bidirectional."""
        # Two uniquegate warps connecting the same zones (like academy gates)
        fog1 = FogData(
            name="gate_a_to_b",
            fog_id=1,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["uniquegate"],
        )
        fog2 = FogData(
            name="gate_b_to_a",
            fog_id=2,
            aside=FogSide(area="zone_b", text=""),
            bside=FogSide(area="zone_a", text=""),
            tags=["uniquegate"],
        )
        zone_fogs = classify_fogs([], [fog1, fog2])

        # Should be coupled as ONE bidirectional connection, not two
        # Only the first fog should be added as representative
        assert len(zone_fogs["zone_a"].entry_fogs) == 1
        assert len(zone_fogs["zone_a"].exit_fogs) == 1
        assert len(zone_fogs["zone_b"].entry_fogs) == 1
        assert len(zone_fogs["zone_b"].exit_fogs) == 1

        # The representative fog should be the first one
        assert zone_fogs["zone_a"].entry_fogs[0] == fog1

    def test_minorwarp_on_bside(self):
        """Minorwarp tag on BSide means ASide=exit only, BSide=nothing.

        BSide destinations are not added as entry_fogs because they can't
        be used as DAG connection entrances (FogMod handles arrival internally).
        """
        # Example: Auriza Side Tomb chest 30132216
        # ASide = dupejail (use the chest), BSide = dupehallway (arrive) with minorwarp
        fog = FogData(
            name="30132216",
            fog_id=30132216,
            aside=FogSide(area="zone_a", text="using the chest"),
            bside=FogSide(area="zone_b", text="arriving", tags=["minorwarp"]),
            tags=["dungeon", "catacomb"],  # No minorwarp at fog level
        )
        zone_fogs = classify_fogs([], [fog])

        # ASide is exit only
        assert (
            "zone_a" not in zone_fogs
            or fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        )
        assert fog in zone_fogs["zone_a"].exit_fogs

        # BSide has nothing (not usable as connection entrance)
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).entry_fogs
        )
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs
        )

    def test_minorwarp_on_aside(self):
        """Minorwarp tag on ASide means ASide=exit only, BSide=nothing."""
        # Example: Auriza Side Tomb chest 30132217
        # ASide = dupehallway (use the chest) with minorwarp, BSide = dupejail (arrive)
        fog = FogData(
            name="30132217",
            fog_id=30132217,
            aside=FogSide(area="zone_a", text="using the chest", tags=["minorwarp"]),
            bside=FogSide(area="zone_b", text="arriving"),
            tags=["dungeon", "catacomb"],  # No minorwarp at fog level
        )
        zone_fogs = classify_fogs([], [fog])

        # ASide is exit only
        assert (
            "zone_a" not in zone_fogs
            or fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        )
        assert fog in zone_fogs["zone_a"].exit_fogs

        # BSide has nothing
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).entry_fogs
        )
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs
        )

    def test_minorwarp_paired_provides_exits_only(self):
        """Paired minorwarp chests provide exits from both zones, no entries.

        Each zone gets an exit (the chest), but neither gets an entry_fog
        since minorwarp destinations can't be DAG connection targets.
        """
        # Two chests that PairWith each other (like Auriza Side Tomb 30132216/30132217)
        fog1 = FogData(
            name="30132216",
            fog_id=30132216,
            aside=FogSide(area="dupejail", text="using the chest"),
            bside=FogSide(area="dupehallway", text="arriving", tags=["minorwarp"]),
            tags=["dungeon", "catacomb"],
        )
        fog2 = FogData(
            name="30132217",
            fog_id=30132217,
            aside=FogSide(
                area="dupehallway", text="using the chest", tags=["minorwarp"]
            ),
            bside=FogSide(area="dupejail", text="arriving"),
            tags=["dungeon", "catacomb"],
        )
        zone_fogs = classify_fogs([], [fog1, fog2])

        # dupejail has: exit via fog1, no entry from minorwarps
        assert fog1 in zone_fogs["dupejail"].exit_fogs
        assert fog2 not in zone_fogs["dupejail"].entry_fogs

        # dupehallway has: exit via fog2, no entry from minorwarps
        assert fog2 in zone_fogs["dupehallway"].exit_fogs
        assert fog1 not in zone_fogs["dupehallway"].entry_fogs

    def test_minorwarp_at_fog_level(self):
        """Fog-level minorwarp tag follows same logic: ASide=exit, BSide=nothing."""
        # Example: Auriza Side Tomb chest 30132210 has minorwarp at fog level
        # ASide = hallway (use the chest), BSide = sidetomb (arrive)
        fog = FogData(
            name="30132210",
            fog_id=30132210,
            aside=FogSide(area="zone_a", text="using the chest"),
            bside=FogSide(area="zone_b", text="arriving"),
            tags=["dungeon", "minorwarp", "catacomb"],  # minorwarp at fog level
        )
        zone_fogs = classify_fogs([], [fog])

        # ASide is exit only
        assert fog in zone_fogs["zone_a"].exit_fogs
        assert (
            "zone_a" not in zone_fogs
            or fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        )

        # BSide has nothing
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).entry_fogs
        )
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs
        )

    def test_door_fog_excluded(self):
        """Door fogs (Morgott barriers) are excluded.

        Door fogs (tagged 'door') are pre-connected in FogMod's vanilla graph
        and cannot be redirected. They connect internal areas (e.g., sewer_mohg
        to sewer_preflame) and attempting to use them as exit fogs causes
        "Already matched" errors in FogMod.
        """
        # Real example: AEG099_002_9000 in sewers - Morgott barrier
        fog = FogData(
            name="AEG099_002_9000",
            fog_id=35001504,
            aside=FogSide(area="sewer_mohg", text="after Mohg's arena"),
            bside=FogSide(area="sewer_preflame", text="before Frenzied Flame"),
            tags=["door"],
        )
        zone_fogs = classify_fogs([fog], [])

        # Door fogs should be excluded from all zones
        assert (
            "sewer_mohg" not in zone_fogs
            or fog not in zone_fogs.get("sewer_mohg", ZoneFogs()).entry_fogs
        )
        assert (
            "sewer_mohg" not in zone_fogs
            or fog not in zone_fogs.get("sewer_mohg", ZoneFogs()).exit_fogs
        )
        assert (
            "sewer_preflame" not in zone_fogs
            or fog not in zone_fogs.get("sewer_preflame", ZoneFogs()).entry_fogs
        )
        assert (
            "sewer_preflame" not in zone_fogs
            or fog not in zone_fogs.get("sewer_preflame", ZoneFogs()).exit_fogs
        )

    def test_door_open_fog_excluded(self):
        """Door fogs with 'open' variant are also excluded.

        Some doors have 'door open' tags (e.g., Leyndell entrance barrier).
        These should also be excluded.
        """
        fog = FogData(
            name="AEG099_002_9000",
            fog_id=1045521500,
            aside=FogSide(area="outskirts_rampart", text="to Capital Rampart"),
            bside=FogSide(area="leyndell_start", text="to Leyndell"),
            tags=["door", "open"],
        )
        zone_fogs = classify_fogs([fog], [])

        # Door fogs should be excluded from all zones
        assert (
            "outskirts_rampart" not in zone_fogs
            or fog not in zone_fogs.get("outskirts_rampart", ZoneFogs()).entry_fogs
        )
        assert (
            "leyndell_start" not in zone_fogs
            or fog not in zone_fogs.get("leyndell_start", ZoneFogs()).entry_fogs
        )

    def test_split_fog_excluded(self):
        """Split fogs (ashen alternates) are excluded.

        Split fogs (e.g., ashen capital versions with SplitFrom) share the same
        logical connection as their canonical counterpart in FogMod's graph.
        They should not be used as separate entry/exit points.
        """
        # Canonical fog (normal version)
        fog_normal = FogData(
            name="AEG099_230_9000",
            fog_id=755894103,
            aside=FogSide(area="sidetomb", text="at entrance"),
            bside=FogSide(area="outskirts", text="at dungeon entrance"),
            tags=["dungeon", "catacomb"],
        )
        # Split fog (ashen version) - has split_from set
        fog_ashen = FogData(
            name="AEG099_230_9502",
            fog_id=755894107,
            aside=FogSide(area="sidetomb", text="at ashen entrance"),
            bside=FogSide(area="outskirts", text="at ashen dungeon entrance"),
            tags=["dungeon", "catacomb"],
            split_from="m60_45_52_00_AEG099_230_9000",  # Ashen split of canonical
        )
        zone_fogs = classify_fogs([fog_normal, fog_ashen], [])

        # Canonical fog should be included
        assert fog_normal in zone_fogs["sidetomb"].entry_fogs
        assert fog_normal in zone_fogs["sidetomb"].exit_fogs
        assert fog_normal in zone_fogs["outskirts"].entry_fogs
        assert fog_normal in zone_fogs["outskirts"].exit_fogs

        # Split fog should be excluded (not in any zone's fogs)
        assert fog_ashen not in zone_fogs.get("sidetomb", ZoneFogs()).entry_fogs
        assert fog_ashen not in zone_fogs.get("sidetomb", ZoneFogs()).exit_fogs
        assert fog_ashen not in zone_fogs.get("outskirts", ZoneFogs()).entry_fogs
        assert fog_ashen not in zone_fogs.get("outskirts", ZoneFogs()).exit_fogs

    def test_crawlonly_fog_included(self):
        """Crawlonly fogs are valid in SpeedFog (crawl=true).

        FogMod only marks crawlonly warps as unused when !crawl.
        Since SpeedFog uses crawl=true, they create valid graph edges.
        """
        fog = FogData(
            name="AEG099_230_9000",
            fog_id=12345,
            aside=FogSide(area="zone_a", text="at gate"),
            bside=FogSide(area="zone_b", text="at gate"),
            tags=["crawlonly"],
        )
        zone_fogs = classify_fogs([fog], [])
        # crawlonly fogs should be included as bidirectional
        assert fog in zone_fogs["zone_a"].exit_fogs
        assert fog in zone_fogs["zone_b"].exit_fogs

    @pytest.mark.parametrize(
        "tag", ["caveonly", "catacombonly", "forgeonly", "gaolonly"]
    )
    def test_dungeon_only_fog_excluded(self, tag):
        """Dungeon-entrance-only fogs are excluded.

        FogMod marks caveonly/catacombonly/forgeonly/gaolonly warps as unused
        in crawl mode with req_backportal=true (SpeedFog's standard config).
        They don't create graph edges. See Graph.cs:1069-1076.
        """
        fog = FogData(
            name="AEG099_232_9000",
            fog_id=42032840,
            aside=FogSide(area="zone_a", text="at entrance"),
            bside=FogSide(area="zone_b", text="at gate"),
            tags=[tag],
        )
        zone_fogs = classify_fogs([fog], [])
        assert fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        assert fog not in zone_fogs.get("zone_a", ZoneFogs()).exit_fogs
        assert fog not in zone_fogs.get("zone_b", ZoneFogs()).entry_fogs
        assert fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs

    def test_backportal_selfwarp(self):
        """Backportal warps become selfwarps, added to ASide only.

        With req_backportal=true, FogMod reassigns BSide.Area = ASide.Area
        (Graph.cs:1079-1095). They're added to both entry and exit fogs
        for the ASide zone only.
        """
        fog = FogData(
            name="42032840",
            fog_id=42032840,
            aside=FogSide(area="rauhbase_forge", text="return to entrance"),
            bside=FogSide(area="rauhbase_forge", text="arriving at entrance"),
            tags=["backportal", "dungeon", "alwaysback", "forge"],
        )
        zone_fogs = classify_fogs([], [fog])
        assert fog in zone_fogs["rauhbase_forge"].entry_fogs
        assert fog in zone_fogs["rauhbase_forge"].exit_fogs

    def test_zone_cond_excludes_side_from_exit(self):
        """Side with zone-based Cond is excluded from exit fogs.

        Real case: Redmane sending gate (1050360600) has ASide Cond
        'OR altus outskirts gelmir caelid_radahn'. The vanilla sending gate
        is controlled by the festival flag — if conditions aren't met, it's
        disabled in-game, causing a softlock.
        """
        fog = FogData(
            name="1050360600",
            fog_id=1050360600,
            aside=FogSide(
                area="caelid_redmane",
                text="using the sending gate in Redmane Castle",
                tags=["neveropen"],
                cond="OR altus outskirts gelmir caelid_radahn",
            ),
            bside=FogSide(
                area="caelid",
                text="arriving at Impassable Greatbridge",
                tags=["open"],
            ),
            tags=["uniqueminor", "minorwarp"],
        )
        zone_fogs = classify_fogs([fog], [])

        # ASide has zone Cond → not a valid exit (sending gate may be disabled)
        assert fog not in zone_fogs.get("caelid_redmane", ZoneFogs()).exit_fogs

    def test_zone_cond_excludes_side_from_entry(self):
        """Side with zone-based Cond is excluded from entry fogs too."""
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(
                area="zone_a",
                text="",
                cond="OR other_zone another_zone",
            ),
            bside=FogSide(area="zone_b", text=""),
            tags=[],
        )
        zone_fogs = classify_fogs([fog], [])

        # ASide has zone Cond → not a valid entry either
        assert fog not in zone_fogs.get("zone_a", ZoneFogs()).entry_fogs
        # BSide has no Cond → still valid
        assert fog in zone_fogs["zone_b"].entry_fogs
        assert fog in zone_fogs["zone_b"].exit_fogs

    def test_self_referencing_cond_still_valid_exit(self):
        """Self-referencing Cond (drop indicator) does NOT exclude from exits.

        Example: catacomb ASide with cond=own_zone means you must already
        be there to use it (a drop). It's not a valid entry, but IS a
        valid exit — you can drop down to leave the zone.
        """
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(
                area="my_catacomb",
                text="dropping down",
                cond="my_catacomb",
            ),
            bside=FogSide(area="overworld", text=""),
            tags=[],
        )
        zone_fogs = classify_fogs([fog], [])

        # ASide with self-referencing cond: NOT a valid entry, but IS a valid exit
        assert fog not in zone_fogs.get("my_catacomb", ZoneFogs()).entry_fogs
        assert fog in zone_fogs.get("my_catacomb", ZoneFogs()).exit_fogs

    def test_key_item_cond_not_excluded(self):
        """Side with key-item-only Cond is NOT excluded (items are given)."""
        fog = FogData(
            name="test",
            fog_id=1,
            aside=FogSide(
                area="zone_a",
                text="",
                cond="rustykey",
            ),
            bside=FogSide(area="zone_b", text=""),
            tags=[],
        )
        zone_fogs = classify_fogs([fog], [])

        # Key item cond is guaranteed → still valid
        assert fog in zone_fogs["zone_a"].entry_fogs
        assert fog in zone_fogs["zone_a"].exit_fogs

    def test_runes_leyndell_cond_not_excluded(self):
        """ConfigVar alias runes_leyndell is guaranteed (all runes given).

        Real case: Deeproot→Leyndell sending gate (11002500) has ASide
        Cond 'runes_leyndell'. This expands to OR2 of Great Runes, all
        of which SpeedFog gives at start.
        """
        fog = FogData(
            name="11002500",
            fog_id=11002500,
            aside=FogSide(
                area="deeproot_boss",
                text="using the sending gate after Fia's Champions",
                cond="runes_leyndell",
            ),
            bside=FogSide(
                area="leyndell",
                text="arriving at Leyndell from Deeproot",
                tags=["neveropen"],
            ),
            tags=["unique", "underground"],
        )
        zone_fogs = classify_fogs([fog], [])

        # runes_leyndell is guaranteed → ASide is a valid exit
        assert fog in zone_fogs["deeproot_boss"].exit_fogs


# =============================================================================
# Cluster Generation Tests
# =============================================================================


class TestGenerateClusters:
    """Tests for generate_clusters function."""

    def test_single_zone_cluster(self):
        """Zone with no connections forms singleton cluster."""
        graph = WorldGraph()
        zones = {"lonely"}

        clusters = generate_clusters(zones, graph)

        assert len(clusters) == 1
        assert clusters[0].zones == frozenset({"lonely"})

    def test_connected_zones_same_cluster(self):
        """Connected zones form one cluster."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)
        zones = {"a", "b"}

        clusters = generate_clusters(zones, graph)

        # Both starting from a or b should give same cluster
        assert len(clusters) == 1
        assert clusters[0].zones == frozenset({"a", "b"})

    def test_drop_creates_multiple_clusters(self):
        """Drop connection creates multiple entry points = multiple clusters."""
        graph = WorldGraph()
        graph.add_edge("top", "bottom", bidirectional=False)
        zones = {"top", "bottom"}

        clusters = generate_clusters(zones, graph)

        # Starting from top: reaches bottom -> cluster {top, bottom}
        # Starting from bottom: no reachable -> cluster {bottom}
        cluster_sets = {c.zones for c in clusters}
        assert frozenset({"top", "bottom"}) in cluster_sets
        assert frozenset({"bottom"}) in cluster_sets


class TestComputeClusterFogs:
    """Tests for compute_cluster_fogs function."""

    def test_entry_zones_all_bidirectional(self):
        """All zones are entry zones when all connections bidirectional."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)

        zone_fogs = {
            "a": ZoneFogs(
                entry_fogs=[FogData("f1", 1, FogSide("a", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f1", 1, FogSide("a", ""), FogSide("x", ""))],
            ),
            "b": ZoneFogs(
                entry_fogs=[FogData("f2", 2, FogSide("b", ""), FogSide("y", ""))],
                exit_fogs=[FogData("f2", 2, FogSide("b", ""), FogSide("y", ""))],
            ),
        }

        cluster = Cluster(zones=frozenset({"a", "b"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # Both zones are entry zones, so entry_fogs from both
        assert len(cluster.entry_fogs) == 2

    def test_entry_zones_with_drop(self):
        """Only top zone is entry zone when drop exists."""
        graph = WorldGraph()
        graph.add_edge("top", "bottom", bidirectional=False)

        zone_fogs = {
            "top": ZoneFogs(
                entry_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
            ),
            "bottom": ZoneFogs(
                entry_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""))
                ],
                exit_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""))
                ],
            ),
        }

        cluster = Cluster(zones=frozenset({"top", "bottom"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # Only top is entry zone
        entry_zones = {f["zone"] for f in cluster.entry_fogs}
        assert "top" in entry_zones
        assert "bottom" not in entry_zones

        # Both are in exit_fogs
        exit_zones = {f["zone"] for f in cluster.exit_fogs}
        assert "top" in exit_zones
        assert "bottom" in exit_zones

    def test_oneway_entry_zones_with_drop(self):
        """One-way drop marks entry zone as oneway_entry_zone."""
        graph = WorldGraph()
        graph.add_edge("top", "bottom", bidirectional=False)

        zone_fogs = {
            "top": ZoneFogs(
                entry_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
            ),
            "bottom": ZoneFogs(
                entry_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""))
                ],
                exit_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""))
                ],
            ),
        }

        cluster = Cluster(zones=frozenset({"top", "bottom"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert cluster.oneway_entry_zones == frozenset({"top"})

    def test_oneway_entry_zones_bidirectional(self):
        """No oneway entry zones when links are bidirectional."""
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)

        zone_fogs = {
            "a": ZoneFogs(
                entry_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
            ),
            "b": ZoneFogs(
                entry_fogs=[FogData("f_b", 2, FogSide("b", ""), FogSide("y", ""))],
                exit_fogs=[FogData("f_b", 2, FogSide("b", ""), FogSide("y", ""))],
            ),
        }

        cluster = Cluster(zones=frozenset({"a", "b"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert cluster.oneway_entry_zones == frozenset()

    def test_oneway_entry_zones_with_return_path(self):
        """If deep zone has bidirectional back, entry zone is NOT oneway."""
        graph = WorldGraph()
        graph.add_edge("top", "bottom", bidirectional=False)
        graph.add_edge("bottom", "top", bidirectional=True)

        zone_fogs = {
            "top": ZoneFogs(
                entry_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
            ),
            "bottom": ZoneFogs(
                entry_fogs=[FogData("f_b", 2, FogSide("bottom", ""), FogSide("y", ""))],
                exit_fogs=[FogData("f_b", 2, FogSide("bottom", ""), FogSide("y", ""))],
            ),
        }

        cluster = Cluster(zones=frozenset({"top", "bottom"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert cluster.oneway_entry_zones == frozenset()

    def test_same_fog_id_different_zones_creates_separate_entries(self):
        """Same fog_id in different zones creates separate exit entries.

        This tests the fix for multi-zone clusters where a fog gate
        connects two zones within the cluster. Each side of the gate
        should be listed as a separate exit.
        """
        graph = WorldGraph()
        graph.add_edge("boss_room", "post_boss", bidirectional=True)

        # The fog "shared_gate" exists in both zones (two sides of same gate)
        zone_fogs = {
            "boss_room": ZoneFogs(
                entry_fogs=[
                    FogData(
                        "shared_gate",
                        100,
                        FogSide("boss_room", ""),
                        FogSide("post_boss", ""),
                    )
                ],
                exit_fogs=[
                    FogData(
                        "shared_gate",
                        100,
                        FogSide("boss_room", ""),
                        FogSide("post_boss", ""),
                    ),
                    FogData("exit_a", 101, FogSide("boss_room", ""), FogSide("x", "")),
                ],
            ),
            "post_boss": ZoneFogs(
                entry_fogs=[
                    FogData(
                        "shared_gate",
                        100,
                        FogSide("post_boss", ""),
                        FogSide("boss_room", ""),
                    )
                ],
                exit_fogs=[
                    FogData(
                        "shared_gate",
                        100,
                        FogSide("post_boss", ""),
                        FogSide("boss_room", ""),
                    ),
                    FogData("exit_b", 102, FogSide("post_boss", ""), FogSide("y", "")),
                ],
            ),
        }

        cluster = Cluster(zones=frozenset({"boss_room", "post_boss"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # Should have 4 exits: shared_gate from boss_room, shared_gate from post_boss,
        # exit_a from boss_room, exit_b from post_boss
        assert len(cluster.exit_fogs) == 4

        # Verify we have shared_gate from both zones
        shared_exits = [f for f in cluster.exit_fogs if f["fog_id"] == "shared_gate"]
        assert len(shared_exits) == 2
        shared_zones = {f["zone"] for f in shared_exits}
        assert shared_zones == {"boss_room", "post_boss"}

    def test_fog_text_propagated_to_cluster_fogs(self):
        """Gate-level Text propagates into cluster entry/exit fog dicts."""
        graph = WorldGraph()

        fog = FogData(
            "gate1",
            1,
            FogSide("zone_a", "side A"),
            FogSide("zone_b", "side B"),
            text="Main Gate",
            tags=[],
        )
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[fog], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert cluster.entry_fogs[0]["text"] == "Main Gate"
        assert cluster.exit_fogs[0]["text"] == "Main Gate"
        # Side text from ASide (zone_a matches ASide area)
        assert cluster.entry_fogs[0]["side_text"] == "side A"
        assert cluster.exit_fogs[0]["side_text"] == "side A"

    def test_fog_without_text_omits_key(self):
        """Fog without text does not add text key to cluster fog dicts."""
        graph = WorldGraph()

        fog = FogData("gate1", 1, FogSide("zone_a", ""), FogSide("zone_b", ""), tags=[])
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[fog], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert "text" not in cluster.entry_fogs[0]
        assert "text" not in cluster.exit_fogs[0]
        assert "side_text" not in cluster.entry_fogs[0]
        assert "side_text" not in cluster.exit_fogs[0]

    def test_side_text_matches_zone(self):
        """Side text uses the text from the side matching the fog's zone."""
        graph = WorldGraph()

        fog = FogData(
            "gate1",
            1,
            FogSide("zone_a", "from zone A side"),
            FogSide("zone_b", "from zone B side"),
            text="Gate Name",
            tags=[],
        )
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[fog], exit_fogs=[fog]),
            "zone_b": ZoneFogs(entry_fogs=[fog], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a", "zone_b"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # Find entry/exit by zone
        entry_a = next(e for e in cluster.entry_fogs if e["zone"] == "zone_a")
        entry_b = next(e for e in cluster.entry_fogs if e["zone"] == "zone_b")
        exit_a = next(e for e in cluster.exit_fogs if e["zone"] == "zone_a")
        exit_b = next(e for e in cluster.exit_fogs if e["zone"] == "zone_b")

        assert entry_a["side_text"] == "from zone A side"
        assert entry_b["side_text"] == "from zone B side"
        assert exit_a["side_text"] == "from zone A side"
        assert exit_b["side_text"] == "from zone B side"

    def test_main_tag_propagated_from_bside(self):
        """Main tag on BSide propagates to entry dict when BSide is the entry zone."""
        graph = WorldGraph()

        fog = FogData(
            "boss_gate",
            1,
            FogSide("outside", ""),
            FogSide("boss_room", "", tags=["main"]),
            tags=[],
        )
        zone_fogs = {
            "boss_room": ZoneFogs(entry_fogs=[fog], exit_fogs=[]),
        }

        cluster = Cluster(zones=frozenset({"boss_room"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.entry_fogs) == 1
        assert cluster.entry_fogs[0].get("main") is True

    def test_main_tag_propagated_from_aside(self):
        """Main tag on ASide propagates when ASide is the entry zone."""
        graph = WorldGraph()

        fog = FogData(
            "boss_gate",
            1,
            FogSide("boss_room", "", tags=["Main"]),  # case-insensitive
            FogSide("outside", ""),
            tags=[],
        )
        zone_fogs = {
            "boss_room": ZoneFogs(entry_fogs=[fog], exit_fogs=[]),
        }

        cluster = Cluster(zones=frozenset({"boss_room"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.entry_fogs) == 1
        assert cluster.entry_fogs[0].get("main") is True

    def test_main_tag_absent_when_no_main(self):
        """Entry dict has no main key when neither side has main tag."""
        graph = WorldGraph()

        fog = FogData(
            "side_gate",
            2,
            FogSide("outside", ""),
            FogSide("boss_room", ""),
            tags=[],
        )
        zone_fogs = {
            "boss_room": ZoneFogs(entry_fogs=[fog], exit_fogs=[]),
        }

        cluster = Cluster(zones=frozenset({"boss_room"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.entry_fogs) == 1
        assert "main" not in cluster.entry_fogs[0]

    def test_main_tag_not_set_from_wrong_side(self):
        """Main tag on the opposite side (not entering this zone) is ignored."""
        graph = WorldGraph()

        # Main is on ASide (outside), but we're entering boss_room via BSide
        fog = FogData(
            "gate",
            3,
            FogSide("outside", "", tags=["main"]),
            FogSide("boss_room", ""),
            tags=[],
        )
        zone_fogs = {
            "boss_room": ZoneFogs(entry_fogs=[fog], exit_fogs=[]),
        }

        cluster = Cluster(zones=frozenset({"boss_room"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.entry_fogs) == 1
        assert "main" not in cluster.entry_fogs[0]

    def test_prune_unreachable_zones(self):
        """Zones unreachable from entry fog zones are pruned."""
        # Pattern: C --drop--> B <--bidir--> A (entry fog), C --> D (exit fog)
        # Cluster {A, B, C, D} from flood-fill starting at C
        # Entry fogs only at A -> reachable = {A, B} -> prune C, D
        graph = WorldGraph()
        graph.add_edge("c", "b", bidirectional=False)  # drop
        graph.add_edge("a", "b", bidirectional=True)
        graph.add_edge("c", "d", bidirectional=False)

        zone_fogs = {
            "a": ZoneFogs(
                entry_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
            ),
            "d": ZoneFogs(
                entry_fogs=[],
                exit_fogs=[FogData("f_d", 2, FogSide("d", ""), FogSide("y", ""))],
            ),
        }

        cluster = Cluster(zones=frozenset({"a", "b", "c", "d"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert cluster.zones == frozenset({"a", "b"})
        exit_zones = {f["zone"] for f in cluster.exit_fogs}
        assert "d" not in exit_zones
        assert "a" in exit_zones

    def test_no_pruning_when_all_reachable(self):
        """No zones pruned when all are reachable from entry fog zones."""
        graph = WorldGraph()
        graph.add_edge("top", "bottom", bidirectional=False)  # drop

        zone_fogs = {
            "top": ZoneFogs(
                entry_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""))],
            ),
            "bottom": ZoneFogs(
                entry_fogs=[],
                exit_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""))
                ],
            ),
        }

        cluster = Cluster(zones=frozenset({"top", "bottom"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # top is entry zone and has entry fog, bottom reachable via drop
        assert cluster.zones == frozenset({"top", "bottom"})
        exit_zones = {f["zone"] for f in cluster.exit_fogs}
        assert "bottom" in exit_zones

    def test_same_fog_id_different_zones_creates_separate_entries_for_entries(self):
        """Same fog_id in different zones creates separate entry entries."""
        graph = WorldGraph()
        graph.add_edge("zone_a", "zone_b", bidirectional=True)

        # Both zones have the same fog as entry
        zone_fogs = {
            "zone_a": ZoneFogs(
                entry_fogs=[
                    FogData(
                        "shared_entry",
                        200,
                        FogSide("zone_a", ""),
                        FogSide("zone_b", ""),
                    )
                ],
                exit_fogs=[],
            ),
            "zone_b": ZoneFogs(
                entry_fogs=[
                    FogData(
                        "shared_entry",
                        200,
                        FogSide("zone_b", ""),
                        FogSide("zone_a", ""),
                    )
                ],
                exit_fogs=[],
            ),
        }

        cluster = Cluster(zones=frozenset({"zone_a", "zone_b"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        # Should have 2 entries: shared_entry from zone_a, shared_entry from zone_b
        assert len(cluster.entry_fogs) == 2

        entry_zones = {f["zone"] for f in cluster.entry_fogs}
        assert entry_zones == {"zone_a", "zone_b"}

    def test_pruning_creates_deduplication_opportunity(self):
        """Two clusters with different zones become identical after pruning."""
        # Cluster 1: {a, b, c} where c is unreachable from a
        # Cluster 2: {a, b}
        # After pruning cluster 1 -> {a, b} = same as cluster 2
        graph = WorldGraph()
        graph.add_edge("a", "b", bidirectional=True)
        graph.add_edge("c", "b", bidirectional=False)  # drop, c unreachable from a

        zone_fogs = {
            "a": ZoneFogs(
                entry_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
                exit_fogs=[FogData("f_a", 1, FogSide("a", ""), FogSide("x", ""))],
            ),
            "b": ZoneFogs(
                entry_fogs=[],
                exit_fogs=[FogData("f_b", 2, FogSide("b", ""), FogSide("y", ""))],
            ),
        }

        cluster1 = Cluster(zones=frozenset({"a", "b", "c"}))
        cluster2 = Cluster(zones=frozenset({"a", "b"}))
        compute_cluster_fogs(cluster1, graph, zone_fogs)
        compute_cluster_fogs(cluster2, graph, zone_fogs)

        # After pruning, cluster1.zones == cluster2.zones
        assert cluster1.zones == cluster2.zones
        assert cluster1.zones == frozenset({"a", "b"})


# =============================================================================
# Filter Tests
# =============================================================================


class TestGetEvergaolZones:
    """Tests for get_evergaol_zones function."""

    def test_evergaol_zones_from_warps(self):
        """Zones connected by evergaol warps are collected."""
        # Entry warp into evergaol
        entry_warp = FogData(
            name="1033452805",
            fog_id=1033452805,
            aside=FogSide(area="liurnia", text="entering evergaol"),
            bside=FogSide(area="liurnia_evergaol_bols", text="arriving"),
            tags=["evergaol", "returnpair"],
        )
        # Return warp from evergaol
        return_warp = FogData(
            name="1033452806",
            fog_id=1033452806,
            aside=FogSide(area="liurnia_evergaol_bols", text="exiting"),
            bside=FogSide(area="liurnia", text="arriving outside"),
            tags=["evergaol", "return"],
        )

        evergaol_zones = get_evergaol_zones([], [entry_warp, return_warp])

        # Both the evergaol zone and the connected overworld zone are collected
        assert "liurnia_evergaol_bols" in evergaol_zones
        assert "liurnia" in evergaol_zones

    def test_non_evergaol_warps_ignored(self):
        """Warps without evergaol tag don't contribute zones."""
        normal_warp = FogData(
            name="normal_warp",
            fog_id=12345,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["dungeon"],
        )

        evergaol_zones = get_evergaol_zones([], [normal_warp])

        assert len(evergaol_zones) == 0

    def test_evergaol_entrances_also_checked(self):
        """Entrances with evergaol tag are also processed."""
        evergaol_entrance = FogData(
            name="evergaol_fog",
            fog_id=99999,
            aside=FogSide(area="overworld", text=""),
            bside=FogSide(area="evergaol_arena", text=""),
            tags=["evergaol"],
        )

        evergaol_zones = get_evergaol_zones([evergaol_entrance], [])

        assert "overworld" in evergaol_zones
        assert "evergaol_arena" in evergaol_zones


class TestShouldExcludeArea:
    """Tests for should_exclude_area function."""

    def test_exclude_dlc(self):
        """DLC areas excluded when exclude_dlc=True."""
        area = AreaData(name="dlc_area", text="", maps=[], tags=["dlc"])
        assert (
            should_exclude_area(area, exclude_dlc=True, exclude_overworld=False) is True
        )
        assert (
            should_exclude_area(area, exclude_dlc=False, exclude_overworld=False)
            is False
        )

    def test_exclude_overworld(self):
        """Overworld areas excluded when exclude_overworld=True."""
        area = AreaData(name="limgrave", text="", maps=[], tags=["overworld"])
        assert (
            should_exclude_area(area, exclude_dlc=False, exclude_overworld=True) is True
        )
        assert (
            should_exclude_area(area, exclude_dlc=False, exclude_overworld=False)
            is False
        )

    def test_exclude_unused(self):
        """Unused areas always excluded."""
        area = AreaData(name="unused_area", text="", maps=[], tags=["unused"])
        assert (
            should_exclude_area(area, exclude_dlc=False, exclude_overworld=False)
            is True
        )


class TestGetZoneType:
    """Tests for get_zone_type function."""

    def test_start_zone(self):
        """Zone with start tag."""
        area = AreaData(
            name="chapel_start", text="", maps=["m10_01_00_00"], tags=["start"]
        )
        assert get_zone_type(area) == "start"

    def test_legacy_dungeon(self):
        """Zone on legacy dungeon map."""
        area = AreaData(name="stormveil", text="", maps=["m10_00_00_00"], tags=[])
        assert get_zone_type(area) == "legacy_dungeon"

    def test_catacomb(self):
        """Zone on catacomb map returns mini_dungeon."""
        area = AreaData(name="test_catacomb", text="", maps=["m30_01_00_00"], tags=[])
        assert get_zone_type(area) == "mini_dungeon"

    def test_cave(self):
        """Zone on cave map returns mini_dungeon."""
        area = AreaData(name="test_cave", text="", maps=["m31_01_00_00"], tags=[])
        assert get_zone_type(area) == "mini_dungeon"

    def test_tunnel(self):
        """Zone on tunnel map returns mini_dungeon."""
        area = AreaData(name="test_tunnel", text="", maps=["m32_01_00_00"], tags=[])
        assert get_zone_type(area) == "mini_dungeon"

    def test_gaol(self):
        """Zone on gaol map returns mini_dungeon."""
        area = AreaData(name="test_gaol", text="", maps=["m39_01_00_00"], tags=[])
        assert get_zone_type(area) == "mini_dungeon"


class TestGenerateClusterId:
    """Tests for generate_cluster_id function."""

    def test_deterministic(self):
        """Same zones produce same ID."""
        zones1 = frozenset({"a", "b", "c"})
        zones2 = frozenset({"c", "a", "b"})  # Different order

        assert generate_cluster_id(zones1) == generate_cluster_id(zones2)

    def test_different_zones_different_id(self):
        """Different zones produce different IDs."""
        zones1 = frozenset({"a", "b"})
        zones2 = frozenset({"a", "c"})

        assert generate_cluster_id(zones1) != generate_cluster_id(zones2)

    def test_id_format(self):
        """ID has expected format: primary_zone_hash."""
        zones = frozenset({"zebra", "apple"})
        cluster_id = generate_cluster_id(zones)

        # Should start with first zone alphabetically
        assert cluster_id.startswith("apple_")
        # Should have 4-char hash suffix
        parts = cluster_id.split("_")
        assert len(parts[-1]) == 4


# =============================================================================
# Filter and Enrich Tests (metadata overrides)
# =============================================================================


def _make_cluster_with_fogs(zones: frozenset[str]) -> Cluster:
    """Helper: create a cluster with minimal entry/exit fogs."""
    primary = sorted(zones)[0]
    return Cluster(
        zones=zones,
        entry_fogs=[{"fog_id": f"entry_{primary}", "zone": primary}],
        exit_fogs=[{"fog_id": f"exit_{primary}", "zone": primary}],
    )


class TestFilterAndEnrichMetadataTypeOverride:
    """Tests for metadata type overrides in filter_and_enrich_clusters."""

    def test_metadata_type_override(self):
        """Metadata 'type' field overrides heuristic zone type."""
        areas = {
            "belurat": AreaData(
                name="belurat", text="Belurat", maps=["m20_00_00_00"], tags=["dlc"]
            ),
        }
        metadata = {
            "defaults": {"legacy_dungeon": 10, "other": 2},
            "zones": {"belurat": {"type": "legacy_dungeon", "weight": 12}},
        }
        cluster = _make_cluster_with_fogs(frozenset({"belurat"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].cluster_type == "legacy_dungeon"

    def test_heuristic_type_when_no_metadata(self):
        """Without metadata override, heuristic type is used."""
        areas = {
            "stormveil": AreaData(
                name="stormveil", text="Stormveil", maps=["m10_00_00_00"], tags=[]
            ),
        }
        metadata = {"defaults": {"legacy_dungeon": 10}, "zones": {}}
        cluster = _make_cluster_with_fogs(frozenset({"stormveil"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].cluster_type == "legacy_dungeon"


class TestFilterAndEnrichMetadataExclude:
    """Tests for metadata exclude flag in filter_and_enrich_clusters."""

    def test_exclude_flag_removes_cluster(self):
        """Cluster with excluded zone is filtered out."""
        areas = {
            "fissure_boss": AreaData(
                name="fissure_boss", text="", maps=["m20_01_00_00"], tags=["dlc"]
            ),
        }
        metadata = {
            "defaults": {"other": 2},
            "zones": {"fissure_boss": {"exclude": True}},
        }
        cluster = _make_cluster_with_fogs(frozenset({"fissure_boss"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 0

    def test_no_exclude_flag_keeps_cluster(self):
        """Cluster without exclude flag is kept."""
        areas = {
            "ensis": AreaData(
                name="ensis", text="Ensis", maps=["m20_02_00_00"], tags=["dlc"]
            ),
        }
        metadata = {
            "defaults": {"legacy_dungeon": 10},
            "zones": {"ensis": {"type": "legacy_dungeon"}},
        }
        cluster = _make_cluster_with_fogs(frozenset({"ensis"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1

    def test_exclude_multi_zone_cluster(self):
        """Cluster excluded when ANY zone has exclude flag."""
        areas = {
            "fissure_boss": AreaData(
                name="fissure_boss", text="", maps=["m20_01_00_00"], tags=["dlc"]
            ),
            "fissure_depths": AreaData(
                name="fissure_depths", text="", maps=["m20_01_00_00"], tags=["dlc"]
            ),
        }
        metadata = {
            "defaults": {"other": 2},
            "zones": {"fissure_boss": {"exclude": True}},
        }
        cluster = _make_cluster_with_fogs(frozenset({"fissure_boss", "fissure_depths"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 0


# =============================================================================
# DefeatFlag Tests
# =============================================================================


class TestParseAreaDefeatFlag:
    """Tests for DefeatFlag parsing in parse_area."""

    def test_parse_area_with_defeat_flag(self):
        """Area with DefeatFlag gets parsed correctly."""
        area_data = {
            "Name": "erdtree",
            "Text": "Erdtree",
            "Maps": "m11_10_00_00",
            "Tags": "final",
            "DefeatFlag": 19000800,
        }
        area = parse_area(area_data)
        assert area.defeat_flag == 19000800

    def test_parse_area_without_defeat_flag(self):
        """Area without DefeatFlag defaults to 0."""
        area_data = {
            "Name": "leyndell_erdtree",
            "Text": "Leyndell Erdtree",
            "Maps": "m11_00_00_00",
            "Tags": "trivial",
        }
        area = parse_area(area_data)
        assert area.defeat_flag == 0

    def test_parse_area_defeat_flag_string(self):
        """DefeatFlag as string (YAML parsing) is converted to int."""
        area_data = {
            "Name": "boss_zone",
            "Text": "Boss",
            "Maps": "m10_00_00_00",
            "DefeatFlag": "12345678",
        }
        area = parse_area(area_data)
        assert area.defeat_flag == 12345678


class TestFindDefeatFlag:
    """Tests for find_defeat_flag traversal function."""

    def test_direct_defeat_flag(self):
        """Zone with DefeatFlag returns it directly."""
        areas = {
            "boss_zone": AreaData(
                name="boss_zone", text="", maps=[], tags=[], defeat_flag=19000800
            ),
        }
        assert find_defeat_flag("boss_zone", areas, []) == 19000800

    def test_no_defeat_flag(self):
        """Zone with no reachable DefeatFlag returns 0."""
        areas = {
            "empty_zone": AreaData(name="empty_zone", text="", maps=[], tags=[]),
        }
        assert find_defeat_flag("empty_zone", areas, []) == 0

    def test_traversal_via_area_to(self):
        """DefeatFlag found via Area.To transition."""
        areas = {
            "zone_a": AreaData(
                name="zone_a",
                text="",
                maps=[],
                tags=[],
                to_connections=[WorldConnection(target_area="zone_b", text="")],
            ),
            "zone_b": AreaData(
                name="zone_b",
                text="",
                maps=[],
                tags=[],
                defeat_flag=99999,
            ),
        }
        assert find_defeat_flag("zone_a", areas, []) == 99999

    def test_traversal_via_norandom_fog(self):
        """DefeatFlag found via norandom fog gate."""
        areas = {
            "zone_a": AreaData(name="zone_a", text="", maps=[], tags=[]),
            "zone_b": AreaData(
                name="zone_b", text="", maps=[], tags=[], defeat_flag=88888
            ),
        }
        norandom_fog = FogData(
            name="norandom_gate",
            fog_id=1,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["norandom"],
        )
        assert find_defeat_flag("zone_a", areas, [norandom_fog]) == 88888

    def test_leyndell_erdtree_traversal(self):
        """Simulate leyndell_erdtree → leyndell2_erdtree → erdtree chain.

        This is the real-world case: leyndell_erdtree has no DefeatFlag,
        but erdtree (DefeatFlag=19000800) is reachable via Area.To +
        norandom fog gate.
        """
        areas = {
            "leyndell_erdtree": AreaData(
                name="leyndell_erdtree",
                text="Erdtree Sanctuary",
                maps=["m11_00_00_00"],
                tags=["trivial"],
                to_connections=[
                    WorldConnection(
                        target_area="leyndell2_erdtree",
                        text="to ashen capital",
                        cond="farumazula_maliketh",
                    )
                ],
            ),
            "leyndell2_erdtree": AreaData(
                name="leyndell2_erdtree",
                text="Erdtree Sanctuary (Ashen)",
                maps=["m11_00_00_00"],
                tags=[],
            ),
            "erdtree": AreaData(
                name="erdtree",
                text="Erdtree",
                maps=["m11_10_00_00"],
                tags=["final"],
                defeat_flag=19000800,
            ),
        }
        norandom_fog = FogData(
            name="erdtree_fog",
            fog_id=999,
            aside=FogSide(area="leyndell2_erdtree", text=""),
            bside=FogSide(area="erdtree", text=""),
            tags=["norandom"],
        )
        result = find_defeat_flag("leyndell_erdtree", areas, [norandom_fog])
        assert result == 19000800

    def test_depth_limit(self):
        """Traversal respects max_depth."""
        # Create a chain of 10 zones, defeat_flag at the end
        areas = {}
        for i in range(10):
            conns = (
                [WorldConnection(target_area=f"zone_{i + 1}", text="")] if i < 9 else []
            )
            areas[f"zone_{i}"] = AreaData(
                name=f"zone_{i}",
                text="",
                maps=[],
                tags=[],
                to_connections=conns,
                defeat_flag=77777 if i == 9 else 0,
            )

        # With depth 5, should not reach zone_9
        assert find_defeat_flag("zone_0", areas, [], max_depth=5) == 0
        # With depth 10, should reach it
        assert find_defeat_flag("zone_0", areas, [], max_depth=10) == 77777


class TestClusterDefeatFlag:
    """Tests for defeat_flag propagation to clusters."""

    def test_cluster_with_direct_defeat_flag(self):
        """Cluster zone with DefeatFlag gets it set."""
        areas = {
            "boss_zone": AreaData(
                name="boss_zone",
                text="Boss",
                maps=["m10_00_00_00"],
                tags=[],
                has_boss=True,
                defeat_flag=12345678,
            ),
        }
        metadata = {"defaults": {"boss_arena": 2}, "zones": {}}
        cluster = _make_cluster_with_fogs(frozenset({"boss_zone"}))

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            {"boss_zone"},
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].defeat_flag == 12345678

    def test_cluster_defeat_flag_via_traversal(self):
        """Cluster without direct DefeatFlag gets it via traversal."""
        areas = {
            "trivial_zone": AreaData(
                name="trivial_zone",
                text="Trivial",
                maps=["m11_00_00_00"],
                tags=["trivial"],
                to_connections=[WorldConnection(target_area="boss_zone", text="")],
            ),
            "boss_zone": AreaData(
                name="boss_zone",
                text="Boss",
                maps=["m11_10_00_00"],
                tags=["final"],
                defeat_flag=19000800,
            ),
        }
        metadata = {
            "defaults": {"other": 2, "final_boss": 4},
            "zones": {"trivial_zone": {"type": "final_boss"}},
        }
        cluster = _make_cluster_with_fogs(frozenset({"trivial_zone"}))

        # Need all_fogs for traversal (empty here since we traverse via Area.To)
        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
            all_fogs=[],
        )

        assert len(result) == 1
        assert result[0].defeat_flag == 19000800


# =============================================================================
# IsCore / is_warp_edge_active Tests
# =============================================================================


class TestIsSideCore:
    """Tests for _is_side_core helper."""

    def test_no_optional_tags_is_core(self):
        """Side with no optional tags is core by default."""
        fog = FogData(
            name="13002500",
            fog_id=13002500,
            aside=FogSide(area="flamepeak_firegiant", text=""),
            bside=FogSide(area="flamepeak_erdtree", text=""),
            tags=["unique", "major"],
        )
        assert _is_side_core(fog, fog.aside) is True
        assert _is_side_core(fog, fog.bside) is True

    def test_divine_tag_not_core(self):
        """Side with 'divine' tag is not core (no req_divine option)."""
        fog = FogData(
            name="34152692",
            fog_id=34152692,
            aside=FogSide(area="leyndell_tower", text=""),
            bside=FogSide(area="leyndell_divinebridge", text=""),
            tags=["unique", "divine"],
        )
        # Both sides inherit 'divine' from fog-level tags
        assert _is_side_core(fog, fog.aside) is False
        assert _is_side_core(fog, fog.bside) is False

    def test_open_tag_not_core_in_crawl(self):
        """Side with 'open' tag is not core in crawl mode."""
        fog = FogData(
            name="1037462650",
            fog_id=1037462650,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text="", tags=["open"]),
            tags=["unique"],
        )
        assert _is_side_core(fog, fog.aside) is True
        assert _is_side_core(fog, fog.bside) is False

    def test_neveropen_without_open_is_core(self):
        """'neveropen' tag makes side core when 'open' is absent."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique", "neveropen"],
        )
        assert _is_side_core(fog, fog.aside) is True
        assert _is_side_core(fog, fog.bside) is True

    def test_open_takes_precedence_over_neveropen(self):
        """'open' takes precedence over 'neveropen' (matches FogMod Graph.cs:1167-1176)."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique", "open", "neveropen"],
        )
        # FogMod checks open first (else-if neveropen), so open wins
        assert _is_side_core(fog, fog.aside) is False
        assert _is_side_core(fog, fog.bside) is False

    def test_cave_tag_core_with_req(self):
        """Side with 'cave' tag is core (req_cave is in SpeedFog options)."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="cave_zone", text="", tags=["cave"]),
            tags=["unique"],
        )
        assert _is_side_core(fog, fog.bside) is True

    def test_colosseum_tag_not_core(self):
        """Side with 'colosseum' tag is not core (no req_colosseum option)."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="colosseum", text="", tags=["colosseum"]),
            tags=["unique"],
        )
        assert _is_side_core(fog, fog.bside) is False

    def test_side_tags_combined_with_fog_tags(self):
        """Side tags and fog-level tags are combined for IsCore check."""
        # divine at fog level, cave at side level
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text="", tags=["cave"]),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique", "divine"],
        )
        # ASide has divine+cave → cave has req_cave → at least one → core
        assert _is_side_core(fog, fog.aside) is True
        # BSide has divine only → no req_divine → not core
        assert _is_side_core(fog, fog.bside) is False


class TestIsWarpEdgeActive:
    """Tests for is_warp_edge_active function."""

    def test_both_sides_core_is_active(self):
        """Warp with both sides core → edge active (usable exit)."""
        # 13002500: Melina's hand warp. Tags: unique major.
        # Neither unique nor major is an optional tag → both sides core.
        fog = FogData(
            name="13002500",
            fog_id=13002500,
            aside=FogSide(area="flamepeak_firegiant", text="Melina's hand"),
            bside=FogSide(area="flamepeak_erdtree", text="arriving"),
            tags=["unique", "major"],
        )
        assert is_warp_edge_active(fog) is True

    def test_divine_tower_inactive(self):
        """Divine tower warp → edge inactive (divine tag, no req_divine)."""
        fog = FogData(
            name="34152692",
            fog_id=34152692,
            aside=FogSide(area="leyndell_tower", text=""),
            bside=FogSide(area="leyndell_divinebridge", text=""),
            tags=["unique", "divine"],
        )
        assert is_warp_edge_active(fog) is False

    def test_bside_open_inactive(self):
        """Warp with BSide 'open' tag → edge inactive (crawl mode)."""
        fog = FogData(
            name="1037462650",
            fog_id=1037462650,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text="", tags=["open"]),
            tags=["unique"],
        )
        assert is_warp_edge_active(fog) is False

    def test_one_side_non_core_is_inactive(self):
        """Warp inactive if only one side is non-core."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="colosseum", text="", tags=["colosseum"]),
            tags=["unique"],
        )
        assert is_warp_edge_active(fog) is False

    def test_both_sides_have_enabled_optional_tags(self):
        """Warp active when both sides have optional tags with req_* enabled."""
        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text="", tags=["cave"]),
            bside=FogSide(area="zone_b", text="", tags=["tunnel"]),
            tags=["unique"],
        )
        assert is_warp_edge_active(fog) is True


class TestComputeClusterFogsUniqueClassification:
    """Tests for compute_cluster_fogs unique exit classification."""

    def test_usable_unique_exit_not_marked_unique(self):
        """Usable unique exits (both sides core) are NOT marked as 'unique'."""
        graph = WorldGraph()

        # Unique warp with no optional tags → both sides core → active
        fog = FogData(
            name="13002500",
            fog_id=13002500,
            aside=FogSide(area="zone_a", text="Melina's hand"),
            bside=FogSide(area="zone_b", text="arriving"),
            tags=["unique", "major"],
            location=13002500,
        )
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.exit_fogs) == 1
        exit_fog = cluster.exit_fogs[0]
        assert "unique" not in exit_fog  # NOT marked unique
        assert exit_fog["location"] == 13002500  # Location preserved

    def test_disabled_unique_exit_marked_unique(self):
        """Disabled unique exits (non-core side) ARE marked as 'unique'."""
        graph = WorldGraph()

        # Unique warp with divine tag → non-core → inactive
        fog = FogData(
            name="34152692",
            fog_id=34152692,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique", "divine"],
            location=34152692,
        )
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.exit_fogs) == 1
        exit_fog = cluster.exit_fogs[0]
        assert exit_fog.get("unique") is True  # Marked unique
        assert exit_fog["location"] == 34152692  # Location preserved

    def test_usable_unique_exit_without_location(self):
        """Usable unique exit without location has no location key."""
        graph = WorldGraph()

        fog = FogData(
            name="test",
            fog_id=999,
            aside=FogSide(area="zone_a", text=""),
            bside=FogSide(area="zone_b", text=""),
            tags=["unique", "major"],
            location=None,
        )
        zone_fogs = {
            "zone_a": ZoneFogs(entry_fogs=[], exit_fogs=[fog]),
        }

        cluster = Cluster(zones=frozenset({"zone_a"}))
        compute_cluster_fogs(cluster, graph, zone_fogs)

        assert len(cluster.exit_fogs) == 1
        assert "unique" not in cluster.exit_fogs[0]
        assert "location" not in cluster.exit_fogs[0]


# =============================================================================
# Fog Reuse Default Tests
# =============================================================================


class TestComputeAllowSharedEntrance:
    """Tests for compute_allow_shared_entrance function."""

    def test_true_when_two_plus_entries(self):
        """Clusters with 2+ entry fogs get allow_shared_entrance=True."""
        entry_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z2"},
        ]
        result = compute_allow_shared_entrance(entry_fogs, {}, frozenset({"z1", "z2"}))
        assert result is True

    def test_false_when_one_entry(self):
        """Clusters with 1 entry fog get allow_shared_entrance=False."""
        entry_fogs = [{"fog_id": "fog_a", "zone": "z1"}]
        result = compute_allow_shared_entrance(entry_fogs, {}, frozenset({"z1"}))
        assert result is False

    def test_false_when_no_entries(self):
        """Clusters with 0 entry fogs get allow_shared_entrance=False."""
        result = compute_allow_shared_entrance([], {}, frozenset({"z1"}))
        assert result is False

    def test_override_false(self):
        """zone_metadata.toml can override allow_shared_entrance to false."""
        entry_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z2"},
        ]
        zones_meta = {"z1": {"allow_shared_entrance": False}}
        result = compute_allow_shared_entrance(
            entry_fogs, zones_meta, frozenset({"z1", "z2"})
        )
        assert result is False

    def test_override_true(self):
        """zone_metadata.toml can force allow_shared_entrance to true on 1-entry cluster."""
        entry_fogs = [{"fog_id": "fog_a", "zone": "z1"}]
        zones_meta = {"z1": {"allow_shared_entrance": True}}
        result = compute_allow_shared_entrance(
            entry_fogs, zones_meta, frozenset({"z1"})
        )
        assert result is True


class TestFogReuseInFilterAndEnrich:
    """Tests for fog reuse flags flowing through filter_and_enrich_clusters."""

    def test_shared_entrance_set_on_multi_entry_cluster(self):
        """Cluster with 2+ entries gets allow_shared_entrance=True after enrichment."""
        areas = {
            "zone_a": AreaData(
                name="zone_a", text="Zone A", maps=["m10_00_00_00"], tags=[]
            ),
        }
        metadata = {"defaults": {"mini_dungeon": 5}, "zones": {}}
        cluster = Cluster(
            zones=frozenset({"zone_a"}),
            entry_fogs=[
                {"fog_id": "entry_a", "zone": "zone_a"},
                {"fog_id": "entry_b", "zone": "zone_a"},
            ],
            exit_fogs=[{"fog_id": "exit_a", "zone": "zone_a"}],
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].allow_shared_entrance is True

    def test_shared_entrance_false_on_single_entry_cluster(self):
        """Cluster with 1 entry gets allow_shared_entrance=False after enrichment."""
        areas = {
            "zone_a": AreaData(
                name="zone_a", text="Zone A", maps=["m10_00_00_00"], tags=[]
            ),
        }
        metadata = {"defaults": {"mini_dungeon": 5}, "zones": {}}
        cluster = Cluster(
            zones=frozenset({"zone_a"}),
            entry_fogs=[{"fog_id": "entry_a", "zone": "zone_a"}],
            exit_fogs=[{"fog_id": "exit_a", "zone": "zone_a"}],
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].allow_shared_entrance is False


class TestComputeAllowEntryAsExit:
    """Tests for compute_allow_entry_as_exit function."""

    def test_boss_arena_with_two_exits(self):
        """boss_arena with 2+ exit fogs gets allow_entry_as_exit=True."""
        exit_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z1"},
        ]
        result = compute_allow_entry_as_exit(
            "boss_arena", exit_fogs, {}, frozenset({"z1"})
        )
        assert result is True

    def test_boss_arena_with_one_exit(self):
        """boss_arena with 1 exit fog gets allow_entry_as_exit=False."""
        exit_fogs = [{"fog_id": "fog_a", "zone": "z1"}]
        result = compute_allow_entry_as_exit(
            "boss_arena", exit_fogs, {}, frozenset({"z1"})
        )
        assert result is False

    def test_mini_dungeon_with_two_exits(self):
        """mini_dungeon with 2+ exits gets allow_entry_as_exit=False."""
        exit_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z1"},
        ]
        result = compute_allow_entry_as_exit(
            "mini_dungeon", exit_fogs, {}, frozenset({"z1"})
        )
        assert result is False

    def test_override_false_on_boss_arena(self):
        """zone_metadata.toml can disable allow_entry_as_exit on a boss_arena."""
        exit_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z1"},
        ]
        zones_meta = {"z1": {"allow_entry_as_exit": False}}
        result = compute_allow_entry_as_exit(
            "boss_arena", exit_fogs, zones_meta, frozenset({"z1"})
        )
        assert result is False

    def test_override_true_on_mini_dungeon(self):
        """zone_metadata.toml can force allow_entry_as_exit on non-boss_arena."""
        exit_fogs = [
            {"fog_id": "fog_a", "zone": "z1"},
            {"fog_id": "fog_b", "zone": "z1"},
        ]
        zones_meta = {"z1": {"allow_entry_as_exit": True}}
        result = compute_allow_entry_as_exit(
            "mini_dungeon", exit_fogs, zones_meta, frozenset({"z1"})
        )
        assert result is True


class TestEntryAsExitInFilterAndEnrich:
    """Tests for entry-as-exit flag flowing through filter_and_enrich_clusters."""

    def test_boss_arena_type_override_with_two_exits(self):
        """boss_arena with type override and 2+ exits gets allow_entry_as_exit=True."""
        areas = {
            "zone_a": AreaData(
                name="zone_a", text="Zone A", maps=["m10_00_00_00"], tags=[]
            ),
        }
        metadata = {
            "defaults": {"boss_arena": 3},
            "zones": {"zone_a": {"type": "boss_arena"}},
        }
        cluster = Cluster(
            zones=frozenset({"zone_a"}),
            entry_fogs=[{"fog_id": "entry_a", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "exit_a", "zone": "zone_a"},
                {"fog_id": "entry_a", "zone": "zone_a"},
            ],
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].allow_entry_as_exit is True

    def test_boss_arena_one_exit_gets_false(self):
        """boss_arena with 1 exit gets allow_entry_as_exit=False."""
        areas = {
            "zone_a": AreaData(
                name="zone_a", text="Zone A", maps=["m10_00_00_00"], tags=[]
            ),
        }
        metadata = {
            "defaults": {"boss_arena": 3},
            "zones": {"zone_a": {"type": "boss_arena"}},
        }
        cluster = Cluster(
            zones=frozenset({"zone_a"}),
            entry_fogs=[{"fog_id": "entry_a", "zone": "zone_a"}],
            exit_fogs=[{"fog_id": "exit_a", "zone": "zone_a"}],
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert result[0].allow_entry_as_exit is False


class TestBossArenaOnewayExitPruning:
    """Boss arena clusters should exclude exits from oneway entry zones."""

    def test_boss_arena_prunes_exits_from_oneway_entry_zone(self):
        """Boss arena with one-way drop: exits in entry zone are removed."""
        areas = {
            "preboss": AreaData(
                name="preboss", text="Above Boss", maps=["m31_21_00_00"], tags=[]
            ),
            "boss": AreaData(
                name="boss",
                text="Boss Arena",
                maps=["m31_21_00_00"],
                tags=[],
                defeat_flag=31210800,
            ),
        }
        metadata = {
            "defaults": {"boss_arena": 3},
            "zones": {"boss": {"type": "boss_arena"}},
        }
        cluster = Cluster(
            zones=frozenset({"preboss", "boss"}),
            entry_fogs=[{"fog_id": "entry_fog", "zone": "preboss"}],
            exit_fogs=[
                {"fog_id": "entry_fog", "zone": "preboss"},
                {"fog_id": "other_exit", "zone": "preboss"},
                {"fog_id": "boss_exit", "zone": "boss"},
                {"fog_id": "boss_warp", "zone": "boss"},
            ],
            oneway_entry_zones=frozenset({"preboss"}),
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        exit_fogs = {(f["fog_id"], f["zone"]) for f in result[0].exit_fogs}
        # All preboss exits removed (both entry fog and other_exit)
        assert ("entry_fog", "preboss") not in exit_fogs
        assert ("other_exit", "preboss") not in exit_fogs
        # Boss zone exits preserved
        assert ("boss_exit", "boss") in exit_fogs
        assert ("boss_warp", "boss") in exit_fogs

    def test_non_boss_cluster_keeps_all_exits(self):
        """Legacy dungeon with one-way drop: all exits preserved."""
        areas = {
            "courtyard": AreaData(
                name="courtyard",
                text="Courtyard",
                maps=["m14_00_00_00"],
                tags=[],
            ),
            "arena": AreaData(
                name="arena", text="Arena", maps=["m14_00_00_00"], tags=[]
            ),
        }
        metadata = {
            "defaults": {"legacy_dungeon": 10},
            "zones": {"courtyard": {"type": "legacy_dungeon"}},
        }
        cluster = Cluster(
            zones=frozenset({"courtyard", "arena"}),
            entry_fogs=[{"fog_id": "entry_fog", "zone": "courtyard"}],
            exit_fogs=[
                {"fog_id": "entry_fog", "zone": "courtyard"},
                {"fog_id": "gate", "zone": "courtyard"},
                {"fog_id": "arena_exit", "zone": "arena"},
            ],
            oneway_entry_zones=frozenset({"courtyard"}),
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        # All exits preserved for non-boss cluster
        exit_fogs = {(f["fog_id"], f["zone"]) for f in result[0].exit_fogs}
        assert ("entry_fog", "courtyard") in exit_fogs
        assert ("gate", "courtyard") in exit_fogs
        assert ("arena_exit", "arena") in exit_fogs

    def test_boss_arena_with_oneway_to_postboss_keeps_entry_exits(self):
        """Boss in entry zone with one-way to postboss: entry exits preserved."""
        areas = {
            "boss": AreaData(
                name="boss",
                text="Boss Arena",
                maps=["m43_01_00_00"],
                tags=[],
                defeat_flag=43010800,
            ),
            "postboss": AreaData(
                name="postboss",
                text="After Boss",
                maps=["m43_01_00_00"],
                tags=[],
            ),
        }
        metadata = {
            "defaults": {"boss_arena": 3},
            "zones": {"boss": {"type": "boss_arena"}},
        }
        cluster = Cluster(
            zones=frozenset({"boss", "postboss"}),
            entry_fogs=[{"fog_id": "entry_fog", "zone": "boss"}],
            exit_fogs=[
                {"fog_id": "entry_fog", "zone": "boss"},
                {"fog_id": "boss_warp", "zone": "boss"},
                {"fog_id": "post_exit", "zone": "postboss"},
            ],
            oneway_entry_zones=frozenset({"boss"}),
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        exit_fogs = {(f["fog_id"], f["zone"]) for f in result[0].exit_fogs}
        # Boss is in entry zone - player is NOT forced to take one-way
        # All exits preserved
        assert ("entry_fog", "boss") in exit_fogs
        assert ("boss_warp", "boss") in exit_fogs
        assert ("post_exit", "postboss") in exit_fogs

    def test_boss_arena_without_oneway_keeps_all_exits(self):
        """Boss arena without one-way links: all exits preserved."""
        areas = {
            "zone_a": AreaData(
                name="zone_a",
                text="Zone A",
                maps=["m10_00_00_00"],
                tags=[],
                defeat_flag=10000800,
            ),
        }
        metadata = {
            "defaults": {"boss_arena": 3},
            "zones": {"zone_a": {"type": "boss_arena"}},
        }
        cluster = Cluster(
            zones=frozenset({"zone_a"}),
            entry_fogs=[{"fog_id": "entry", "zone": "zone_a"}],
            exit_fogs=[
                {"fog_id": "entry", "zone": "zone_a"},
                {"fog_id": "back", "zone": "zone_a"},
            ],
            # No oneway entry zones (single zone or bidirectional)
        )

        result = filter_and_enrich_clusters(
            [cluster],
            areas,
            metadata,
            set(),
            set(),
            exclude_dlc=False,
            exclude_overworld=False,
        )

        assert len(result) == 1
        assert len(result[0].exit_fogs) == 2
