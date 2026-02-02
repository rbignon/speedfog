"""Tests for generate_clusters.py"""

from __future__ import annotations

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
        assert fog.aside.area == "stormveil"
        assert fog.bside.area == "stormveil_godrick"
        assert fog.tags == ["major"]
        assert fog.is_unique is False

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
                entry_fogs=[FogData("f1", 1, FogSide("a", ""), FogSide("x", ""), [])],
                exit_fogs=[FogData("f1", 1, FogSide("a", ""), FogSide("x", ""), [])],
            ),
            "b": ZoneFogs(
                entry_fogs=[FogData("f2", 2, FogSide("b", ""), FogSide("y", ""), [])],
                exit_fogs=[FogData("f2", 2, FogSide("b", ""), FogSide("y", ""), [])],
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
                entry_fogs=[
                    FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""), [])
                ],
                exit_fogs=[
                    FogData("f_top", 1, FogSide("top", ""), FogSide("x", ""), [])
                ],
            ),
            "bottom": ZoneFogs(
                entry_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""), [])
                ],
                exit_fogs=[
                    FogData("f_bot", 2, FogSide("bottom", ""), FogSide("y", ""), [])
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
