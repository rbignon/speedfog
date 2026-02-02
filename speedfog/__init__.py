"""SpeedFog core - DAG generator for Elden Ring zone randomization."""

__version__ = "0.1.0"

from speedfog.balance import PathStats, analyze_balance, report_balance
from speedfog.clusters import ClusterData, ClusterPool, load_clusters
from speedfog.config import (
    BudgetConfig,
    Config,
    PathsConfig,
    RequirementsConfig,
    StructureConfig,
    load_config,
)
from speedfog.dag import Dag, DagEdge, DagNode
from speedfog.generator import (
    GenerationError,
    GenerationResult,
    generate_dag,
    generate_with_retry,
)
from speedfog.output import dag_to_dict, export_json, export_spoiler_log
from speedfog.planner import compute_tier, plan_layer_types
from speedfog.validator import ValidationResult, validate_dag

__all__ = [
    # Config
    "BudgetConfig",
    "Config",
    "PathsConfig",
    "RequirementsConfig",
    "StructureConfig",
    "load_config",
    # Clusters
    "ClusterData",
    "ClusterPool",
    "load_clusters",
    # DAG
    "Dag",
    "DagEdge",
    "DagNode",
    # Planner
    "compute_tier",
    "plan_layer_types",
    # Generator
    "GenerationError",
    "GenerationResult",
    "generate_dag",
    "generate_with_retry",
    # Balance
    "PathStats",
    "analyze_balance",
    "report_balance",
    # Validator
    "ValidationResult",
    "validate_dag",
    # Output
    "dag_to_dict",
    "export_json",
    "export_spoiler_log",
]
