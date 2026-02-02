#!/usr/bin/env python3
"""
Extract FogRando dependencies from the Nexusmods download.

This script extracts:
- DLLs from FogMod.exe → writer/lib/
- eldendata/ → writer/FogModWrapper/eldendata/
- Data files (fog.txt, etc.) → data/

Prerequisites:
- sfextract (dotnet tool): dotnet tool install -g sfextract

Usage:
    python tools/setup_fogrando.py /path/to/FogRando.zip
    python tools/setup_fogrando.py /path/to/FogRando.zip --force
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

# Project root (parent of tools/)
PROJECT_ROOT = Path(__file__).parent.parent

# Destination paths
WRITER_LIB = PROJECT_ROOT / "writer" / "lib"
ELDENDATA_DEST = PROJECT_ROOT / "writer" / "FogModWrapper" / "eldendata"
DATA_DEST = PROJECT_ROOT / "data"

# Files to extract from eldendata/Base/ to data/
FOGRANDO_DATA_FILES = [
    "fog.txt",
    "fogevents.txt",
    "foglocations2.txt",
    "er-common.emedf.json",
]

# DLLs we need (subset of what sfextract produces)
REQUIRED_DLLS = [
    "FogMod.dll",
    "SoulsFormats.dll",
    "SoulsIds.dll",
    "BouncyCastle.Cryptography.dll",
    "Newtonsoft.Json.dll",
    "YamlDotNet.dll",
    "ZstdNet.dll",
    "DrSwizzler.dll",
]


def print_step(step: int, total: int, message: str) -> None:
    """Print a step header."""
    print(f"\n[{step}/{total}] {message}")


def print_ok(message: str) -> None:
    """Print a success message."""
    print(f"      \u2713 {message}")


def print_info(message: str) -> None:
    """Print an info message."""
    print(f"      \u2192 {message}")


def print_error(message: str) -> None:
    """Print an error message."""
    print(f"      \u2717 {message}")


def find_sfextract() -> Path | None:
    """Find the sfextract tool."""
    # Try common locations
    candidates = [
        Path.home() / ".dotnet" / "tools" / "sfextract",
        Path("/usr/local/bin/sfextract"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate

    # Try PATH
    result = shutil.which("sfextract")
    if result:
        return Path(result)

    return None


def check_prerequisites(zip_path: Path) -> Path | None:
    """Check that all prerequisites are met. Returns sfextract path or None."""
    print_step(1, 6, "Checking prerequisites...")

    # Check sfextract
    sfextract = find_sfextract()
    if not sfextract:
        print_error("sfextract not found")
        print()
        print("Install it with: dotnet tool install -g sfextract")
        return None
    print_ok("sfextract found")

    # Check ZIP file
    if not zip_path.exists():
        print_error(f"ZIP file not found: {zip_path}")
        return None
    if not zipfile.is_zipfile(zip_path):
        print_error(f"Not a valid ZIP file: {zip_path}")
        return None
    print_ok("ZIP file valid")

    return sfextract


def is_already_installed() -> bool:
    """Check if FogRando dependencies are already installed."""
    # Check for DLLs
    if not WRITER_LIB.exists():
        return False
    dll_count = len(list(WRITER_LIB.glob("*.dll")))
    if dll_count < len(REQUIRED_DLLS):
        return False

    # Check for eldendata
    if not ELDENDATA_DEST.exists():
        return False

    # Check for data files
    for filename in FOGRANDO_DATA_FILES:
        if not (DATA_DEST / filename).exists():
            return False

    return True


def extract_zip(zip_path: Path, temp_dir: Path) -> bool:
    """Extract the FogRando ZIP to temp directory."""
    try:
        with zipfile.ZipFile(zip_path, "r") as zf:
            zf.extractall(temp_dir)
        return True
    except zipfile.BadZipFile:
        print_error("Failed to extract ZIP file")
        return False


def extract_dlls(sfextract: Path, exe_path: Path, output_dir: Path) -> int:
    """Extract DLLs from FogMod.exe using sfextract. Returns count of DLLs."""
    print_step(2, 6, "Extracting FogMod.exe...")

    try:
        subprocess.run(
            [str(sfextract), str(exe_path), "-o", str(output_dir)],
            capture_output=True,
            text=True,
            check=True,
        )
        # Count extracted DLLs
        dll_count = len(list(output_dir.glob("*.dll")))
        print_ok(f"Extracted {dll_count} DLLs")
        return dll_count
    except subprocess.CalledProcessError as e:
        print_error(f"sfextract failed: {e.stderr}")
        return 0


def copy_files(temp_dir: Path) -> bool:
    """Copy extracted files to their destinations."""
    print_step(3, 6, "Copying files...")

    extracted_dir = temp_dir / "extracted"
    fog_dir = temp_dir / "fog"

    # Copy DLLs to writer/lib/
    WRITER_LIB.mkdir(parents=True, exist_ok=True)
    dll_count = 0
    for dll in extracted_dir.glob("*.dll"):
        shutil.copy2(dll, WRITER_LIB / dll.name)
        dll_count += 1
    # Also copy from x64 subfolder if present
    x64_dir = extracted_dir / "x64"
    if x64_dir.exists():
        for dll in x64_dir.glob("*.dll"):
            shutil.copy2(dll, WRITER_LIB / dll.name)
            dll_count += 1
    # Copy libzstd.dll from fog/ root
    libzstd = fog_dir / "libzstd.dll"
    if libzstd.exists():
        shutil.copy2(libzstd, WRITER_LIB / "libzstd.dll")
        dll_count += 1
    print_info(f"writer/lib/ ({dll_count} DLLs)")

    # Copy eldendata/
    src_eldendata = fog_dir / "eldendata"
    if not src_eldendata.exists():
        print_error("eldendata/ not found in ZIP")
        return False
    if ELDENDATA_DEST.exists():
        shutil.rmtree(ELDENDATA_DEST)
    shutil.copytree(src_eldendata, ELDENDATA_DEST)
    print_info("writer/FogModWrapper/eldendata/")

    # Copy data files to data/
    DATA_DEST.mkdir(parents=True, exist_ok=True)
    base_dir = src_eldendata / "Base"
    file_count = 0
    for filename in FOGRANDO_DATA_FILES:
        src = base_dir / filename
        if src.exists():
            shutil.copy2(src, DATA_DEST / filename)
            file_count += 1
        else:
            print_error(f"Missing: {filename}")
    print_info(f"data/ ({file_count} files)")

    return True


def regenerate_derived_data() -> bool:
    """Regenerate clusters.json and fog_data.json."""
    print_step(4, 6, "Regenerating derived data...")

    tools_dir = PROJECT_ROOT / "tools"

    # Generate clusters.json
    try:
        subprocess.run(
            [
                sys.executable,
                str(tools_dir / "generate_clusters.py"),
                str(DATA_DEST / "fog.txt"),
                str(DATA_DEST / "clusters.json"),
                "--metadata",
                str(DATA_DEST / "zone_metadata.toml"),
            ],
            capture_output=True,
            text=True,
            check=True,
        )
        with open(DATA_DEST / "clusters.json") as f:
            data = json.load(f)
        cluster_count = data.get("cluster_count", "?")
        print_info(f"clusters.json ({cluster_count} clusters)")
    except subprocess.CalledProcessError as e:
        print_error(f"generate_clusters.py failed: {e.stderr}")
        return False

    # Generate fog_data.json
    try:
        subprocess.run(
            [
                sys.executable,
                str(tools_dir / "extract_fog_data.py"),
                str(DATA_DEST / "fog.txt"),
                str(DATA_DEST / "fog_data.json"),
            ],
            capture_output=True,
            text=True,
            check=True,
        )
        # Parse fog count from file
        with open(DATA_DEST / "fog_data.json") as f:
            data = json.load(f)
        fog_count = len(data.get("fogs", {}))
        print_info(f"fog_data.json ({fog_count} fog gates)")
    except subprocess.CalledProcessError as e:
        print_error(f"extract_fog_data.py failed: {e.stderr}")
        return False

    return True


def compile_fogmodwrapper() -> bool:
    """Compile FogModWrapper as self-contained Windows executable."""
    print_step(5, 6, "Compiling FogModWrapper...")

    wrapper_dir = PROJECT_ROOT / "writer" / "FogModWrapper"
    publish_dir = wrapper_dir / "publish" / "win-x64"

    try:
        subprocess.run(
            [
                "dotnet",
                "publish",
                "-c",
                "Release",
                "-r",
                "win-x64",
                "--self-contained",
                "-o",
                str(publish_dir),
            ],
            cwd=wrapper_dir,
            capture_output=True,
            text=True,
            check=True,
        )
        print_ok(f"Published to {publish_dir.relative_to(PROJECT_ROOT)}")
        return True
    except subprocess.CalledProcessError as e:
        print_error(f"dotnet publish failed: {e.stderr}")
        return False
    except FileNotFoundError:
        print_error("dotnet not found - install .NET 8.0 SDK")
        return False


def cleanup(temp_dir: Path) -> None:
    """Clean up temporary directory."""
    print_step(6, 6, "Cleanup...")
    shutil.rmtree(temp_dir, ignore_errors=True)
    print_ok("Done")


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Extract FogRando dependencies from Nexusmods download."
    )
    parser.add_argument(
        "zip_path",
        type=Path,
        help="Path to FogRando ZIP file downloaded from Nexusmods",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing files",
    )
    args = parser.parse_args()

    # Check if already installed
    if not args.force and is_already_installed():
        print("FogRando dependencies already installed.")
        print("Use --force to reinstall.")
        return 0

    # Check prerequisites
    sfextract = check_prerequisites(args.zip_path)
    if not sfextract:
        return 1

    # Create temp directory and extract
    temp_dir = Path(tempfile.mkdtemp(prefix="fogrando_"))
    try:
        # Extract ZIP
        if not extract_zip(args.zip_path, temp_dir):
            return 1

        # Find FogMod.exe
        exe_path = temp_dir / "fog" / "FogMod.exe"
        if not exe_path.exists():
            print_error("FogMod.exe not found in ZIP (expected at fog/FogMod.exe)")
            return 1

        # Extract DLLs
        extracted_dir = temp_dir / "extracted"
        extracted_dir.mkdir()
        dll_count = extract_dlls(sfextract, exe_path, extracted_dir)
        if dll_count == 0:
            return 1

        # Copy files
        if not copy_files(temp_dir):
            return 1

        # Regenerate derived data
        if not regenerate_derived_data():
            return 1

        # Compile FogModWrapper
        if not compile_fogmodwrapper():
            return 1

        # Cleanup
        cleanup(temp_dir)

    except Exception as e:
        print_error(f"Unexpected error: {e}")
        shutil.rmtree(temp_dir, ignore_errors=True)
        return 1

    print()
    print("Setup complete! You can now generate runs with:")
    print("  uv run speedfog config.toml --spoiler")

    return 0


if __name__ == "__main__":
    sys.exit(main())
