"""DAG data structures for SpeedFog.

This module contains the core DAG (Directed Acyclic Graph) data structures
used to represent randomized speedrun routes.
"""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field

from speedfog.clusters import ClusterData


@dataclass
class Branch:
    """A branch in the DAG during generation.

    Tracks the current position and available exit fog for a parallel path.
    """

    id: str
    current_node_id: str
    available_exit: str


@dataclass
class DagNode:
    """A node in the DAG representing a cluster instance.

    Nodes are identified by their `id` field. Two nodes with the same id
    are considered equal regardless of other fields.
    """

    id: str
    cluster: ClusterData
    layer: int
    tier: int  # Difficulty scaling (1-28)
    entry_fogs: list[str] = field(
        default_factory=list
    )  # fog_ids used to enter (empty for start)
    exit_fogs: list[str] = field(default_factory=list)  # Available exits

    def __hash__(self) -> int:
        """Hash by id only."""
        return hash(self.id)

    def __eq__(self, other: object) -> bool:
        """Equality by id only."""
        if not isinstance(other, DagNode):
            return NotImplemented
        return self.id == other.id


@dataclass
class DagEdge:
    """A directed edge between two nodes.

    Edges are identified by the tuple (source_id, target_id, exit_fog, entry_fog).
    """

    source_id: str
    target_id: str
    exit_fog: str  # The fog gate used to exit source (in source node's exit_fogs)
    entry_fog: str  # The fog gate used to enter target (in target node's entry_fogs)

    # Legacy alias for backward compatibility
    @property
    def fog_id(self) -> str:
        """Alias for exit_fog for backward compatibility."""
        return self.exit_fog

    def __hash__(self) -> int:
        """Hash by (source_id, target_id, exit_fog, entry_fog) tuple."""
        return hash((self.source_id, self.target_id, self.exit_fog, self.entry_fog))

    def __eq__(self, other: object) -> bool:
        """Equality by (source_id, target_id, exit_fog, entry_fog) tuple."""
        if not isinstance(other, DagEdge):
            return NotImplemented
        return (
            self.source_id == other.source_id
            and self.target_id == other.target_id
            and self.exit_fog == other.exit_fog
            and self.entry_fog == other.entry_fog
        )


@dataclass
class Dag:
    """The complete DAG structure.

    A DAG represents a directed acyclic graph of clusters, where:
    - Nodes are cluster instances placed at specific layers
    - Edges connect nodes via fog gates
    - All paths lead from start_id to end_id
    """

    seed: int
    nodes: dict[str, DagNode] = field(default_factory=dict)
    edges: list[DagEdge] = field(default_factory=list)
    start_id: str = ""
    end_id: str = ""

    def add_node(self, node: DagNode) -> None:
        """Add a node to the DAG."""
        self.nodes[node.id] = node

    def add_edge(
        self, source_id: str, target_id: str, exit_fog: str, entry_fog: str
    ) -> None:
        """Add an edge to the DAG.

        Args:
            source_id: ID of the source node
            target_id: ID of the target node
            exit_fog: Fog ID used to exit from source
            entry_fog: Fog ID used to enter target
        """
        self.edges.append(DagEdge(source_id, target_id, exit_fog, entry_fog))

    def get_node(self, node_id: str) -> DagNode | None:
        """Get a node by id, or None if not found."""
        return self.nodes.get(node_id)

    def get_outgoing_edges(self, node_id: str) -> list[DagEdge]:
        """Get all edges originating from a node."""
        return [e for e in self.edges if e.source_id == node_id]

    def get_incoming_edges(self, node_id: str) -> list[DagEdge]:
        """Get all edges targeting a node."""
        return [e for e in self.edges if e.target_id == node_id]

    def enumerate_paths(self) -> list[list[str]]:
        """Enumerate all unique paths from start to end.

        A path is a sequence of node IDs. Multiple edges between the same
        pair of nodes (different fog gates) are treated as a single connection
        for path enumeration purposes.

        Returns:
            List of paths, where each path is a list of node IDs.
            Returns empty list if start_id or end_id is not set.
        """
        if not self.start_id or not self.end_id:
            return []

        paths: list[list[str]] = []

        def dfs(node_id: str, current_path: list[str]) -> None:
            current_path = current_path + [node_id]
            if node_id == self.end_id:
                paths.append(current_path)
                return
            # Get unique target nodes (ignore multiple edges to same target)
            targets = {edge.target_id for edge in self.get_outgoing_edges(node_id)}
            for target_id in sorted(targets):
                dfs(target_id, current_path)

        dfs(self.start_id, [])
        return paths

    def path_weight(self, path: list[str]) -> int:
        """Calculate total weight of a path.

        Args:
            path: List of node IDs in the path

        Returns:
            Sum of cluster weights for all nodes in the path.
            Returns 0 for empty path.
        """
        return sum(self.nodes[nid].cluster.weight for nid in path if nid in self.nodes)

    def total_nodes(self) -> int:
        """Return the total number of nodes in the DAG."""
        return len(self.nodes)

    def total_zones(self) -> int:
        """Return the count of unique zones across all nodes."""
        all_zones: set[str] = set()
        for node in self.nodes.values():
            all_zones.update(node.cluster.zones)
        return len(all_zones)

    def count_by_type(self, cluster_type: str) -> int:
        """Count nodes whose cluster matches the given type.

        Args:
            cluster_type: The cluster type to count (e.g., "legacy_dungeon")

        Returns:
            Number of nodes with matching cluster type.
        """
        return sum(
            1 for node in self.nodes.values() if node.cluster.type == cluster_type
        )

    def validate_structure(self) -> list[str]:
        """Validate the DAG structure for correctness.

        Checks:
        - start_id and end_id are set
        - start and end nodes exist
        - All edges reference existing nodes
        - No backward edges (source.layer >= target.layer)
        - All nodes are reachable from start
        - All nodes can reach end (no dead ends)

        Returns:
            List of error messages. Empty list means valid.
        """
        errors: list[str] = []

        # Check start_id and end_id are set
        if not self.start_id:
            errors.append("Missing start_id")
        if not self.end_id:
            errors.append("Missing end_id")

        # Check start and end nodes exist
        if self.start_id and self.start_id not in self.nodes:
            errors.append(f"Start node '{self.start_id}' not found in nodes")
        if self.end_id and self.end_id not in self.nodes:
            errors.append(f"End node '{self.end_id}' not found in nodes")

        # Check all edges reference existing nodes and are forward edges
        for edge in self.edges:
            if edge.source_id not in self.nodes:
                errors.append(f"Edge source '{edge.source_id}' not found in nodes")
            if edge.target_id not in self.nodes:
                errors.append(f"Edge target '{edge.target_id}' not found in nodes")

            # Check for backward edges (only if both nodes exist)
            if edge.source_id in self.nodes and edge.target_id in self.nodes:
                source_layer = self.nodes[edge.source_id].layer
                target_layer = self.nodes[edge.target_id].layer
                if source_layer >= target_layer:
                    errors.append(
                        f"Backward edge from '{edge.source_id}' (layer {source_layer}) "
                        f"to '{edge.target_id}' (layer {target_layer})"
                    )

        # Check reachability from start (only if start exists)
        if self.start_id and self.start_id in self.nodes:
            reachable = self._find_reachable_from_start()
            for node_id in self.nodes:
                if node_id not in reachable:
                    errors.append(f"Node '{node_id}' is unreachable from start")

        # Check all nodes can reach end (only if end exists)
        if self.end_id and self.end_id in self.nodes:
            can_reach_end = self._find_nodes_reaching_end()
            for node_id in self.nodes:
                if node_id not in can_reach_end:
                    errors.append(f"Node '{node_id}' is a dead end (cannot reach end)")

        return errors

    def _find_reachable_from_start(self) -> set[str]:
        """Find all nodes reachable from start via BFS."""
        if not self.start_id:
            return set()

        reachable: set[str] = set()
        queue: deque[str] = deque([self.start_id])

        while queue:
            node_id = queue.popleft()  # O(1) instead of list.pop(0) which is O(n)
            if node_id in reachable:
                continue
            reachable.add(node_id)
            for edge in self.get_outgoing_edges(node_id):
                if edge.target_id not in reachable:
                    queue.append(edge.target_id)

        return reachable

    def _find_nodes_reaching_end(self) -> set[str]:
        """Find all nodes that can reach end via reverse BFS."""
        if not self.end_id:
            return set()

        can_reach: set[str] = set()
        queue: deque[str] = deque([self.end_id])

        while queue:
            node_id = queue.popleft()  # O(1) instead of list.pop(0) which is O(n)
            if node_id in can_reach:
                continue
            can_reach.add(node_id)
            for edge in self.get_incoming_edges(node_id):
                if edge.source_id not in can_reach:
                    queue.append(edge.source_id)

        return can_reach
