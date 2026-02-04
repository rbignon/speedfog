#!/usr/bin/env python3
"""
Extract FogRando and Item Randomizer dependencies from Nexusmods downloads.

This script extracts:
- FogRando:
  - DLLs from FogMod.exe → writer/lib/
  - eldendata/ → writer/FogModWrapper/eldendata/
  - Data files (fog.txt, etc.) → data/
- Item Randomizer:
  - DLLs from EldenRingRandomizer.exe → writer/lib/
  - diste/ → writer/ItemRandomizerWrapper/diste/
  - RandomizerCrashFix.dll → writer/assets/

Prerequisites:
- sfextract (dotnet tool): dotnet tool install -g sfextract

Usage:
    # Extract both (recommended)
    python tools/setup_dependencies.py --fogrando /path/to/FogRando.zip --itemrando /path/to/ItemRandomizer.zip

    # Extract only FogRando
    python tools/setup_dependencies.py --fogrando /path/to/FogRando.zip

    # Extract only Item Randomizer
    python tools/setup_dependencies.py --itemrando /path/to/ItemRandomizer.zip

    # Legacy single-argument mode (FogRando only)
    python tools/setup_dependencies.py /path/to/FogRando.zip
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
WRITER_ASSETS = PROJECT_ROOT / "writer" / "assets"
ELDENDATA_DEST = PROJECT_ROOT / "writer" / "FogModWrapper" / "eldendata"
DISTE_DEST = PROJECT_ROOT / "writer" / "ItemRandomizerWrapper" / "diste"
DATA_DEST = PROJECT_ROOT / "data"

# Files to extract from eldendata/Base/ to data/
FOGRANDO_DATA_FILES = [
    "fog.txt",
    "fogevents.txt",
    "foglocations2.txt",
    "er-common.emedf.json",
]

# DLLs we need from FogMod (subset of what sfextract produces)
FOGRANDO_REQUIRED_DLLS = [
    "FogMod.dll",
    "SoulsFormats.dll",
    "SoulsIds.dll",
    "BouncyCastle.Cryptography.dll",
    "Newtonsoft.Json.dll",
    "YamlDotNet.dll",
    "ZstdNet.dll",
    "DrSwizzler.dll",
]

# DLLs we need from Item Randomizer
ITEMRANDO_REQUIRED_DLLS = [
    "RandomizerCommon.dll",
    # Shared with FogRando (already extracted):
    # SoulsFormats.dll, SoulsIds.dll, YamlDotNet.dll, etc.
]

# Extra DLLs to copy from Item Randomizer zip (not from sfextract)
ITEMRANDO_EXTRA_DLLS = [
    "RandomizerCrashFix.dll",
    "RandomizerHelper.dll",
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


def is_fogrando_installed() -> bool:
    """Check if FogRando dependencies are already installed."""
    # Check for DLLs
    if not WRITER_LIB.exists():
        return False
    for dll in FOGRANDO_REQUIRED_DLLS:
        if not (WRITER_LIB / dll).exists():
            return False

    # Check for eldendata
    if not ELDENDATA_DEST.exists():
        return False

    # Check for data files
    for filename in FOGRANDO_DATA_FILES:
        if not (DATA_DEST / filename).exists():
            return False

    return True


def is_itemrando_installed() -> bool:
    """Check if Item Randomizer dependencies are already installed."""
    # Check for RandomizerCommon.dll
    if not (WRITER_LIB / "RandomizerCommon.dll").exists():
        return False

    # Check for diste
    if not DISTE_DEST.exists():
        return False

    # Check for extra DLLs in assets
    for dll in ITEMRANDO_EXTRA_DLLS:
        if not (WRITER_ASSETS / dll).exists():
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


def extract_dlls(
    sfextract: Path, exe_path: Path, output_dir: Path, exe_name: str = "FogMod.exe"
) -> int:
    """Extract DLLs from an exe using sfextract. Returns count of DLLs."""
    print_info(f"Extracting {exe_name}...")

    try:
        subprocess.run(
            [str(sfextract), str(exe_path), "-o", str(output_dir)],
            capture_output=True,
            text=True,
            check=True,
        )
        # Count extracted DLLs
        dll_count = len(list(output_dir.glob("*.dll")))
        print_ok(f"Extracted {dll_count} DLLs from {exe_name}")
        return dll_count
    except subprocess.CalledProcessError as e:
        print_error(f"sfextract failed: {e.stderr}")
        return 0


def copy_fogrando_files(temp_dir: Path, extracted_dir: Path) -> bool:
    """Copy FogRando extracted files to their destinations."""
    print_info("Copying FogRando files...")

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
    print_ok(f"writer/lib/ ({dll_count} DLLs)")

    # Copy eldendata/
    src_eldendata = fog_dir / "eldendata"
    if not src_eldendata.exists():
        print_error("eldendata/ not found in ZIP")
        return False
    if ELDENDATA_DEST.exists():
        shutil.rmtree(ELDENDATA_DEST)
    shutil.copytree(src_eldendata, ELDENDATA_DEST)
    print_ok("writer/FogModWrapper/eldendata/")

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
    print_ok(f"data/ ({file_count} files)")

    return True


def copy_itemrando_files(temp_dir: Path, extracted_dir: Path) -> bool:
    """Copy Item Randomizer extracted files to their destinations."""
    print_info("Copying Item Randomizer files...")

    randomizer_dir = temp_dir / "randomizer"

    # Copy DLLs to writer/lib/ (only RandomizerCommon.dll, others are shared)
    WRITER_LIB.mkdir(parents=True, exist_ok=True)
    dll_count = 0
    for dll_name in ITEMRANDO_REQUIRED_DLLS:
        src = extracted_dir / dll_name
        if src.exists():
            shutil.copy2(src, WRITER_LIB / dll_name)
            dll_count += 1
        else:
            print_error(f"Missing DLL: {dll_name}")
            return False
    print_ok(f"writer/lib/ (+{dll_count} DLLs)")

    # Copy extra DLLs from randomizer/dll/ to writer/assets/
    WRITER_ASSETS.mkdir(parents=True, exist_ok=True)
    dll_dir = randomizer_dir / "dll"
    extra_count = 0
    for dll_name in ITEMRANDO_EXTRA_DLLS:
        src = dll_dir / dll_name
        if src.exists():
            shutil.copy2(src, WRITER_ASSETS / dll_name)
            extra_count += 1
        else:
            print_error(f"Missing extra DLL: {dll_name}")
    print_ok(f"writer/assets/ ({extra_count} DLLs)")

    # Copy diste/
    src_diste = randomizer_dir / "diste"
    if not src_diste.exists():
        print_error("diste/ not found in ZIP")
        return False
    if DISTE_DEST.exists():
        shutil.rmtree(DISTE_DEST)
    shutil.copytree(src_diste, DISTE_DEST)
    print_ok("writer/ItemRandomizerWrapper/diste/")

    return True


def regenerate_derived_data() -> bool:
    """Regenerate clusters.json and fog_data.json."""
    print_info("Regenerating derived data...")

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
        print_ok(f"clusters.json ({cluster_count} clusters)")
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
        print_ok(f"fog_data.json ({fog_count} fog gates)")
    except subprocess.CalledProcessError as e:
        print_error(f"extract_fog_data.py failed: {e.stderr}")
        return False

    return True


def compile_wrapper(wrapper_name: str) -> bool:
    """Compile a wrapper as self-contained Windows executable."""
    print_info(f"Compiling {wrapper_name}...")

    wrapper_dir = PROJECT_ROOT / "writer" / wrapper_name
    publish_dir = wrapper_dir / "publish" / "win-x64"

    if not wrapper_dir.exists():
        print_error(f"{wrapper_name} directory not found")
        return False

    csproj = wrapper_dir / f"{wrapper_name}.csproj"
    if not csproj.exists():
        print_info(f"{wrapper_name}.csproj not found, skipping compilation")
        return True

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


def setup_fogrando(sfextract: Path, zip_path: Path, force: bool) -> bool:
    """Set up FogRando dependencies."""
    print("\n" + "=" * 50)
    print("Setting up FogRando")
    print("=" * 50)

    if not force and is_fogrando_installed():
        print_ok("FogRando already installed (use --force to reinstall)")
        return True

    # Validate ZIP
    if not zip_path.exists():
        print_error(f"ZIP file not found: {zip_path}")
        return False
    if not zipfile.is_zipfile(zip_path):
        print_error(f"Not a valid ZIP file: {zip_path}")
        return False

    temp_dir = Path(tempfile.mkdtemp(prefix="fogrando_"))
    try:
        # Extract ZIP
        if not extract_zip(zip_path, temp_dir):
            return False

        # Find FogMod.exe
        exe_path = temp_dir / "fog" / "FogMod.exe"
        if not exe_path.exists():
            print_error("FogMod.exe not found in ZIP (expected at fog/FogMod.exe)")
            return False

        # Extract DLLs
        extracted_dir = temp_dir / "extracted_fog"
        extracted_dir.mkdir()
        dll_count = extract_dlls(sfextract, exe_path, extracted_dir, "FogMod.exe")
        if dll_count == 0:
            return False

        # Copy files
        if not copy_fogrando_files(temp_dir, extracted_dir):
            return False

        # Regenerate derived data
        if not regenerate_derived_data():
            return False

        # Compile FogModWrapper
        if not compile_wrapper("FogModWrapper"):
            return False

    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)

    print_ok("FogRando setup complete")
    return True


def setup_itemrando(sfextract: Path, zip_path: Path, force: bool) -> bool:
    """Set up Item Randomizer dependencies."""
    print("\n" + "=" * 50)
    print("Setting up Item Randomizer")
    print("=" * 50)

    if not force and is_itemrando_installed():
        print_ok("Item Randomizer already installed (use --force to reinstall)")
        return True

    # Validate ZIP
    if not zip_path.exists():
        print_error(f"ZIP file not found: {zip_path}")
        return False
    if not zipfile.is_zipfile(zip_path):
        print_error(f"Not a valid ZIP file: {zip_path}")
        return False

    temp_dir = Path(tempfile.mkdtemp(prefix="itemrando_"))
    try:
        # Extract ZIP
        if not extract_zip(zip_path, temp_dir):
            return False

        # Find EldenRingRandomizer.exe
        exe_path = temp_dir / "randomizer" / "EldenRingRandomizer.exe"
        if not exe_path.exists():
            print_error(
                "EldenRingRandomizer.exe not found in ZIP "
                "(expected at randomizer/EldenRingRandomizer.exe)"
            )
            return False

        # Extract DLLs
        extracted_dir = temp_dir / "extracted_rando"
        extracted_dir.mkdir()
        dll_count = extract_dlls(
            sfextract, exe_path, extracted_dir, "EldenRingRandomizer.exe"
        )
        if dll_count == 0:
            return False

        # Copy files
        if not copy_itemrando_files(temp_dir, extracted_dir):
            return False

        # Compile ItemRandomizerWrapper (if it exists)
        if not compile_wrapper("ItemRandomizerWrapper"):
            return False

    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)

    print_ok("Item Randomizer setup complete")
    return True


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Extract FogRando and Item Randomizer dependencies from Nexusmods downloads."
    )
    parser.add_argument(
        "zip_path",
        type=Path,
        nargs="?",
        help="(Legacy) Path to FogRando ZIP file",
    )
    parser.add_argument(
        "--fogrando",
        type=Path,
        help="Path to FogRando ZIP file (Fog Gate Randomizer)",
    )
    parser.add_argument(
        "--itemrando",
        type=Path,
        help="Path to Item Randomizer ZIP file (Elden Ring Randomizer)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing files",
    )
    args = parser.parse_args()

    # Handle legacy single-argument mode
    if args.zip_path and not args.fogrando:
        args.fogrando = args.zip_path

    # Check that at least one ZIP is provided
    if not args.fogrando and not args.itemrando:
        parser.error("At least one of --fogrando or --itemrando must be provided")

    # Check prerequisites
    print_step(1, 1, "Checking prerequisites...")
    sfextract = find_sfextract()
    if not sfextract:
        print_error("sfextract not found")
        print()
        print("Install it with: dotnet tool install -g sfextract")
        return 1
    print_ok("sfextract found")

    success = True

    # Set up FogRando
    if args.fogrando:
        if not setup_fogrando(sfextract, args.fogrando, args.force):
            success = False

    # Set up Item Randomizer
    if args.itemrando:
        if not setup_itemrando(sfextract, args.itemrando, args.force):
            success = False

    if success:
        print()
        print("=" * 50)
        print("Setup complete!")
        print("=" * 50)
        print()
        print("You can now generate runs with:")
        print("  uv run speedfog config.toml --spoiler")
        return 0
    else:
        print()
        print_error("Setup failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
