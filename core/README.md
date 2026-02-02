# SpeedFog Core

DAG generator for Elden Ring SpeedFog mod.

## Installation

```bash
uv venv
source .venv/bin/activate
uv pip install -e ".[dev]"
```

## Usage

```bash
speedfog config.toml -o graph.json --spoiler spoiler.txt
```

## Documentation

See [Architecture](../docs/architecture.md).
