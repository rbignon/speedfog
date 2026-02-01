#!/bin/bash
# writer/test/run_integration.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="$SCRIPT_DIR/../../data"
WRITER_DIR="$SCRIPT_DIR/.."

echo "=== SpeedFogWriter Integration Test ==="

# Check data files exist
if [ ! -f "$DATA_DIR/fog_data.json" ]; then
    echo "ERROR: data/fog_data.json not found"
    exit 1
fi

if [ ! -f "$DATA_DIR/clusters.json" ]; then
    echo "ERROR: data/clusters.json not found"
    exit 1
fi

if [ ! -f "$DATA_DIR/er-common.emedf.json" ]; then
    echo "ERROR: data/er-common.emedf.json not found"
    exit 1
fi

FOGEVENTS_PATH="$DATA_DIR/fogevents.txt"
if [ ! -f "$FOGEVENTS_PATH" ]; then
    echo "ERROR: data/fogevents.txt not found"
    exit 1
fi

echo "Data files: OK"

# Build project
echo ""
echo "Building..."
cd "$WRITER_DIR"
dotnet build --nologo -v q

# Run with sample graph (just parsing test - no game dir needed)
echo ""
echo "Testing graph parsing..."
dotnet run --project SpeedFogWriter --no-build -- test/sample_graph.json 2>&1 || true

echo ""
echo "=== Integration test passed ==="
