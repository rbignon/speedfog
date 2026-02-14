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
    build_world_graph,
    classify_fogs,
    compute_cluster_fogs,
    filter_and_enrich_clusters,
    find_defeat_flag,
    generate_cluster_id,
    generate_clusters,
    get_evergaol_zones,
    get_zone_type,
    is_condition_guaranteed,
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
        """Minorwarp tag on BSide means ASide=exit only, BSide=entry only."""
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

        # BSide is entry only
        assert fog in zone_fogs["zone_b"].entry_fogs
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs
        )

    def test_minorwarp_on_aside(self):
        """Minorwarp tag on ASide means ASide=exit only, BSide=entry only."""
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

        # BSide is entry only
        assert fog in zone_fogs["zone_b"].entry_fogs
        assert (
            "zone_b" not in zone_fogs
            or fog not in zone_fogs.get("zone_b", ZoneFogs()).exit_fogs
        )

    def test_minorwarp_paired_provides_bidirectional(self):
        """Paired minorwarp chests provide bidirectional zone connectivity."""
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

        # dupejail has: entry via fog2, exit via fog1
        assert fog2 in zone_fogs["dupejail"].entry_fogs
        assert fog1 in zone_fogs["dupejail"].exit_fogs

        # dupehallway has: entry via fog1, exit via fog2
        assert fog1 in zone_fogs["dupehallway"].entry_fogs
        assert fog2 in zone_fogs["dupehallway"].exit_fogs

    def test_minorwarp_at_fog_level(self):
        """Fog-level minorwarp tag follows same one-way logic as side-level."""
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

        # BSide is entry only
        assert fog in zone_fogs["zone_b"].entry_fogs
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
