"""Configuration parsing for SpeedFog."""

from __future__ import annotations

import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# Use tomllib (Python 3.11+) with fallback to tomli
if sys.version_info >= (3, 11):
    import tomllib
else:
    try:
        import tomli as tomllib
    except ImportError as e:
        raise ImportError(
            "tomli is required for Python < 3.11. Install with: pip install tomli"
        ) from e


@dataclass
class BudgetConfig:
    """Path budget configuration."""

    total_weight: int = 30
    tolerance: int = 5

    @property
    def min_weight(self) -> int:
        """Minimum acceptable path weight."""
        return self.total_weight - self.tolerance

    @property
    def max_weight(self) -> int:
        """Maximum acceptable path weight."""
        return self.total_weight + self.tolerance


@dataclass
class RequirementsConfig:
    """Zone requirements configuration."""

    legacy_dungeons: int = 1
    bosses: int = 5
    mini_dungeons: int = 5


@dataclass
class StructureConfig:
    """DAG structure configuration."""

    max_parallel_paths: int = 3
    min_layers: int = 6
    max_layers: int = 10


@dataclass
class PathsConfig:
    """File paths configuration."""

    game_dir: str = ""
    output_dir: str = "./output"
    clusters_file: str = "./data/clusters.json"
    randomizer_dir: str | None = None


@dataclass
class Config:
    """Main configuration container."""

    seed: int = 0
    budget: BudgetConfig = field(default_factory=BudgetConfig)
    requirements: RequirementsConfig = field(default_factory=RequirementsConfig)
    structure: StructureConfig = field(default_factory=StructureConfig)
    paths: PathsConfig = field(default_factory=PathsConfig)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> Config:
        """Create Config from a dictionary (e.g., parsed TOML)."""
        run_section = data.get("run", {})
        budget_section = data.get("budget", {})
        requirements_section = data.get("requirements", {})
        structure_section = data.get("structure", {})
        paths_section = data.get("paths", {})

        return cls(
            seed=run_section.get("seed", 0),
            budget=BudgetConfig(
                total_weight=budget_section.get("total_weight", 30),
                tolerance=budget_section.get("tolerance", 5),
            ),
            requirements=RequirementsConfig(
                legacy_dungeons=requirements_section.get("legacy_dungeons", 1),
                bosses=requirements_section.get("bosses", 5),
                mini_dungeons=requirements_section.get("mini_dungeons", 5),
            ),
            structure=StructureConfig(
                max_parallel_paths=structure_section.get("max_parallel_paths", 3),
                min_layers=structure_section.get("min_layers", 6),
                max_layers=structure_section.get("max_layers", 10),
            ),
            paths=PathsConfig(
                game_dir=paths_section.get("game_dir", ""),
                output_dir=paths_section.get("output_dir", "./output"),
                clusters_file=paths_section.get("clusters_file", "./data/clusters.json"),
                randomizer_dir=paths_section.get("randomizer_dir"),
            ),
        )

    @classmethod
    def from_toml(cls, path: str | Path) -> Config:
        """Load configuration from a TOML file."""
        path = Path(path)
        with path.open("rb") as f:
            data = tomllib.load(f)
        return cls.from_dict(data)


def load_config(path: str | Path) -> Config:
    """Load configuration from a TOML file.

    This is a convenience function that wraps Config.from_toml().

    Args:
        path: Path to the TOML configuration file.

    Returns:
        Parsed Config object.
    """
    return Config.from_toml(path)
