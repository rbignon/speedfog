"""Transition shim. Will be deleted in a follow-up commit.

Tests still import from this module; re-export from the canonical location
in speedfog.generator to keep them green during the cutover.
"""

from speedfog.generator import (  # noqa: F401
    GenerationError,
    compute_target_width,
    connect_nodes,
    count_node_net_exits,
    generate_dag,
    pick_layer_clusters,
    route_exits,
)
