#!/usr/bin/env python3
"""
Bootstrap the SpeedFog project from Nexusmods mod archives.

Extracts dependencies, generates derived data, builds C# wrappers,
and runs GamePatcher and WitchyBND for overlay generation.

This script extracts:
- FogRando:
  - DLLs from FogMod.exe → writer/lib/
  - eldendata/ → writer/FogModWrapper/eldendata/
  - Data files (fog.txt, etc.) → data/
- Item Randomizer:
  - DLLs from EldenRingRandomizer.exe → writer/lib/
  - diste/ → writer/ItemRandomizerWrapper/diste/
  - Runtime DLLs → data/packaging/lib/
- ModEngine 2:
  - launcher + runtime → data/packaging/modengine2/
- WitchyBND (Windows build, run via Wine on Linux):
  - extracted to tools/witchybnd/ (downloaded lazily when scripts need repacking)
  - repacks data/overlay-src/script/*-luabnd-dcx/ into data/overlay/script/

Prerequisites:
- sfextract (dotnet tool): dotnet tool install -g sfextract
- Elden Ring game directory (for oo2core_6_win64.dll, needed by dotnet publish)

Usage:
    # Extract both (recommended)
    python tools/bootstrap.py --game-dir /path/to/Game \
        --fogrando /path/to/FogRando.zip --itemrando /path/to/ItemRandomizer.zip

    # Refresh only shared setup assets such as ModEngine 2 and Oodle
    python tools/bootstrap.py --game-dir /path/to/Game
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path

# Project root (parent of tools/)
PROJECT_ROOT = Path(__file__).parent.parent

# Destination paths
WRITER_LIB = PROJECT_ROOT / "writer" / "lib"
ELDENDATA_DEST = PROJECT_ROOT / "writer" / "FogModWrapper" / "eldendata"
DISTE_DEST = PROJECT_ROOT / "writer" / "ItemRandomizerWrapper" / "diste"
DATA_DEST = PROJECT_ROOT / "data"
PACKAGING_DEST = DATA_DEST / "packaging"
PACKAGING_LIB_DEST = PACKAGING_DEST / "lib"
MODENGINE_DEST = PACKAGING_DEST / "modengine2"
WITCHYBND_DEST = PROJECT_ROOT / "tools" / "witchybnd"
OVERLAY_SRC_DEST = DATA_DEST / "overlay-src"
OVERLAY_DEST = DATA_DEST / "overlay"

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
    "SoulsFormats.dll",  # Used by FogModWrapper + ItemRandomizerWrapper (FogMod/SoulsIds depend on old API)
    "SoulsIds.dll",
    "BouncyCastle.Cryptography.dll",
    "Newtonsoft.Json.dll",
    "YamlDotNet.dll",
    "ZstdNet.dll",
    "DrSwizzler.dll",
    # GamePatcher uses SoulsFormatsNEXT (git submodule) instead of these DLLs.
]

# Data files to extract from Item Randomizer's diste/Base/ to data/
ITEMRANDO_DATA_FILES = [
    "enemy.txt",
]

# DLLs we need from Item Randomizer
ITEMRANDO_REQUIRED_DLLS = [
    "RandomizerCommon.dll",
    "Pidgin.dll",  # Parser library used by RandomizerCommon
    # Shared with FogRando (already extracted):
    # SoulsFormats.dll, SoulsIds.dll, YamlDotNet.dll, etc.
]

# Extra DLLs to copy from Item Randomizer zip (not from sfextract)
ITEMRANDO_EXTRA_DLLS = [
    "RandomizerCrashFix.dll",
    "RandomizerHelper.dll",
]

MODENGINE_RELEASE_API = (
    "https://api.github.com/repos/soulsmods/ModEngine2/releases/latest"
)

WITCHYBND_RELEASE_API = "https://api.github.com/repos/ividyon/WitchyBND/releases/latest"
WITCHYBND_ASSET_SUFFIX = "-win-x64.zip"


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

    # Check for data files
    for filename in ITEMRANDO_DATA_FILES:
        if not (DATA_DEST / filename).exists():
            return False

    # Check for runtime DLLs in packaging assets
    for dll in ITEMRANDO_EXTRA_DLLS:
        if not (PACKAGING_LIB_DEST / dll).exists():
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

    # Copy runtime DLLs from randomizer/dll/ to data/packaging/lib/.
    PACKAGING_LIB_DEST.mkdir(parents=True, exist_ok=True)
    dll_dir = randomizer_dir / "dll"
    extra_count = 0
    for dll_name in ITEMRANDO_EXTRA_DLLS:
        src = dll_dir / dll_name
        if src.exists():
            shutil.copy2(src, PACKAGING_LIB_DEST / dll_name)
            extra_count += 1
        else:
            print_error(f"Missing extra DLL: {dll_name}")
    print_ok(f"data/packaging/lib/ ({extra_count} DLLs)")

    # Copy data files from diste/Base/ to data/
    base_dir = randomizer_dir / "diste" / "Base"
    DATA_DEST.mkdir(parents=True, exist_ok=True)
    data_count = 0
    for filename in ITEMRANDO_DATA_FILES:
        src = base_dir / filename
        if src.exists():
            shutil.copy2(src, DATA_DEST / filename)
            data_count += 1
        else:
            print_error(f"Missing: {filename}")
    print_ok(f"data/ (+{data_count} files from Item Randomizer)")

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
        print_error("dotnet not found - install .NET 10.0 SDK")
        return False


def ensure_submodule() -> bool:
    """Initialize the SoulsFormatsNEXT git submodule if needed."""
    submodule_dir = PROJECT_ROOT / "SoulsFormats"
    # Check if already populated (has at least a .git or source files)
    if (submodule_dir / "SoulsFormats").exists():
        print_ok("SoulsFormatsNEXT submodule already initialized")
        return True

    print_info("Initializing SoulsFormatsNEXT submodule...")
    try:
        subprocess.run(
            ["git", "submodule", "update", "--init", "SoulsFormats"],
            cwd=PROJECT_ROOT,
            capture_output=True,
            text=True,
            check=True,
        )
        print_ok("SoulsFormatsNEXT submodule initialized")
        return True
    except subprocess.CalledProcessError as e:
        print_error(f"git submodule update --init failed: {e.stderr}")
        return False
    except FileNotFoundError:
        print_error("git not found")
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

        # Initialize SoulsFormatsNEXT submodule (needed by GamePatcher)
        if not ensure_submodule():
            return False

        # Compile GamePatcher (overlay generator, uses SoulsFormatsNEXT submodule)
        if not compile_wrapper("GamePatcher"):
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


def is_modengine_installed() -> bool:
    """Check if ModEngine 2 is present in data/packaging/modengine2/."""
    return (MODENGINE_DEST / "modengine2_launcher.exe").exists() and (
        MODENGINE_DEST / "modengine2" / "bin" / "modengine2.dll"
    ).exists()


# Runtime essentials to keep when copying the extracted ModEngine 2 archive.
# Drops other-game launchers/configs, README, C++ headers, debug menu assets,
# .lib linker files and cmake configs (~17 MB saved).
MODENGINE_RUNTIME_FILES = frozenset({"modengine2_launcher.exe"})
MODENGINE_RUNTIME_SUBDIRS = frozenset({"bin", "crashpad", "tools"})


def install_modengine_runtime(staging_root: Path, dest: Path) -> None:
    """Copy only runtime essentials from the extracted archive into dest."""
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True, exist_ok=True)

    runtime_src = staging_root / "modengine2"
    if not runtime_src.is_dir():
        raise FileNotFoundError(
            f"ModEngine 2 archive missing modengine2/ subdirectory under {staging_root}"
        )

    for entry in staging_root.iterdir():
        if entry.is_file():
            if entry.name in MODENGINE_RUNTIME_FILES:
                shutil.copy2(entry, dest / entry.name)
            else:
                print_info(f"Skipping non-runtime file: {entry.name}")
            continue
        if entry.name != "modengine2":
            print_info(f"Skipping non-runtime directory: {entry.name}/")

    runtime_dst = dest / "modengine2"
    runtime_dst.mkdir(exist_ok=True)
    for subdir in runtime_src.iterdir():
        if not subdir.is_dir():
            continue
        if subdir.name in MODENGINE_RUNTIME_SUBDIRS:
            shutil.copytree(subdir, runtime_dst / subdir.name)
        else:
            print_info(f"Skipping non-runtime directory: modengine2/{subdir.name}/")


def ensure_modengine(force: bool = False) -> bool:
    """Download and extract ModEngine 2 into data/packaging/modengine2/."""
    print("\n" + "=" * 50)
    print("Setting up ModEngine 2")
    print("=" * 50)

    if is_modengine_installed() and not force:
        print_ok(
            "ModEngine 2 already installed in data/packaging/modengine2/"
            " (use --force to reinstall)"
        )
        return True

    try:
        print_info("Fetching latest ModEngine 2 release metadata...")
        req = urllib.request.Request(
            MODENGINE_RELEASE_API,
            headers={"User-Agent": "SpeedFog/1.0"},
        )
        with urllib.request.urlopen(req, timeout=60) as response:
            release = json.loads(response.read().decode("utf-8"))
    except OSError as e:
        print_error(f"Failed to query ModEngine 2 release: {e}")
        return False

    candidates = [
        a
        for a in release.get("assets", [])
        if a.get("name", "").startswith("ModEngine-")
        and a.get("name", "").endswith("-win64.zip")
    ]
    if not candidates:
        print_error(
            "Could not find a ModEngine-*-win64.zip asset in latest ModEngine 2 release"
        )
        return False
    if len(candidates) > 1:
        names = ", ".join(c.get("name", "") for c in candidates)
        print_info(f"Multiple win64 zip assets matched ({names}); using the first")
    asset = candidates[0]

    archive_path = Path(tempfile.gettempdir()) / asset["name"]
    try:
        print_info(f"Downloading {asset['name']}...")
        req = urllib.request.Request(
            asset["browser_download_url"],
            headers={"User-Agent": "SpeedFog/1.0"},
        )
        with urllib.request.urlopen(req, timeout=600) as response:
            with archive_path.open("wb") as out:
                shutil.copyfileobj(response, out)

        print_info("Extracting ModEngine 2...")
        extract_dir = Path(tempfile.mkdtemp(prefix="modengine_extract_"))
        try:
            with zipfile.ZipFile(archive_path, "r") as zf:
                zf.extractall(extract_dir)

            # ModEngine zip contains a single top-level directory; flatten it.
            launcher_src = extract_dir / "modengine2_launcher.exe"
            if not launcher_src.exists():
                candidates = [
                    path
                    for path in extract_dir.rglob("modengine2_launcher.exe")
                    if path.is_file()
                ]
                if not candidates:
                    print_error(
                        "ModEngine 2 archive does not contain modengine2_launcher.exe"
                    )
                    return False
                launcher_src = candidates[0]

            install_modengine_runtime(launcher_src.parent, MODENGINE_DEST)
        finally:
            shutil.rmtree(extract_dir, ignore_errors=True)

        version = release.get("tag_name", "unknown")
        (MODENGINE_DEST / "version.txt").write_text(f"{version}\n", encoding="utf-8")

    except (OSError, zipfile.BadZipFile, KeyError) as e:
        print_error(f"Failed to install ModEngine 2: {e}")
        return False
    finally:
        archive_path.unlink(missing_ok=True)

    if not is_modengine_installed():
        print_error(
            "ModEngine 2 extraction completed but required binaries are missing"
        )
        return False

    print_ok(
        f"ModEngine 2 {release.get('tag_name', 'unknown')}"
        " installed in data/packaging/modengine2/"
    )
    return True


def copy_oodle_dll(game_dir: Path, force: bool = False) -> bool:
    """Copy oo2core_6_win64.dll from the game directory to writer/lib/.

    This DLL is required by FogMod/SoulsFormats for Oodle decompression
    of game files. It ships with Elden Ring, not with FogRando.
    """
    dll_name = "oo2core_6_win64.dll"
    src = game_dir / dll_name
    dest = WRITER_LIB / dll_name

    if dest.exists() and not force:
        print_ok(f"{dll_name} already in writer/lib/")
        return True

    if not game_dir.is_dir():
        print_error(f"Game directory does not exist: {game_dir}")
        return False

    if not src.exists():
        print_error(f"{dll_name} not found in {game_dir}")
        return False

    WRITER_LIB.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)
    print_ok(f"Copied {dll_name} from game directory")
    return True


def run_modpatcher(game_dir: Path) -> bool:
    """Run GamePatcher to generate overlay files (e.g., patched animations).

    Outputs to data/overlay/ so the main pipeline copies them into each mod build.
    """
    print("\n" + "=" * 50)
    print("Overlay generation")
    print("=" * 50)

    patcher_dir = PROJECT_ROOT / "writer" / "GamePatcher"
    patcher_exe = patcher_dir / "publish" / "win-x64" / "GamePatcher.exe"

    if not patcher_exe.exists():
        print_error("GamePatcher not published, skipping overlay generation")
        return True

    OVERLAY_DEST.mkdir(parents=True, exist_ok=True)

    # Detect platform
    if sys.platform == "win32":
        cmd = [str(patcher_exe)]
    else:
        if not shutil.which("wine"):
            print_error("Wine not found, skipping GamePatcher")
            return True
        cmd = ["wine", str(patcher_exe)]

    cmd.extend([str(game_dir.resolve()), str(OVERLAY_DEST.resolve())])

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=patcher_dir,
        )
        for line in result.stdout.strip().splitlines():
            print(f"      {line}")
        if result.returncode != 0:
            print_error(f"GamePatcher failed: {result.stderr}")
            return False
    except FileNotFoundError:
        print_error("Failed to run GamePatcher")
        return False

    print_ok("Overlay files generated in data/overlay/")
    return True


def is_witchybnd_installed() -> bool:
    """Check if WitchyBND has been extracted under tools/witchybnd/."""
    return (WITCHYBND_DEST / "WitchyBND.exe").exists() and (
        WITCHYBND_DEST / "oo2core_6_win64.dll"
    ).exists()


def select_witchybnd_asset(release: dict) -> dict | None:
    """Pick the Windows x64 asset from a WitchyBND GitHub release payload."""
    candidates = [
        a
        for a in release.get("assets", [])
        if a.get("name", "").endswith(WITCHYBND_ASSET_SUFFIX)
    ]
    if not candidates:
        return None
    if len(candidates) > 1:
        names = ", ".join(c.get("name", "") for c in candidates)
        print_info(f"Multiple win-x64 zip assets matched ({names}); using the first")
    return candidates[0]


def ensure_witchybnd(force: bool = False) -> bool:
    """Download and extract WitchyBND (Windows build) into tools/witchybnd/.

    Also copies oo2core_6_win64.dll next to WitchyBND.exe so Wine can resolve
    Oodle from the standard DLL search path.
    """
    print("\n" + "=" * 50)
    print("Setting up WitchyBND")
    print("=" * 50)

    oodle_src = WRITER_LIB / "oo2core_6_win64.dll"
    if not oodle_src.exists():
        print_error(
            f"{oodle_src.name} missing in writer/lib/;"
            " --game-dir must point to an Elden Ring install that provides it"
        )
        return False

    if is_witchybnd_installed() and not force:
        print_ok(
            "WitchyBND already installed in tools/witchybnd/"
            " (use --force to reinstall)"
        )
        return True

    try:
        print_info("Fetching latest WitchyBND release metadata...")
        req = urllib.request.Request(
            WITCHYBND_RELEASE_API,
            headers={"User-Agent": "SpeedFog/1.0"},
        )
        with urllib.request.urlopen(req, timeout=60) as response:
            release = json.loads(response.read().decode("utf-8"))
    except OSError as e:
        print_error(f"Failed to query WitchyBND release: {e}")
        return False

    asset = select_witchybnd_asset(release)
    if asset is None:
        print_error(
            f"Could not find a *{WITCHYBND_ASSET_SUFFIX} asset"
            " in latest WitchyBND release"
        )
        return False

    archive_path = Path(tempfile.gettempdir()) / asset["name"]
    try:
        print_info(f"Downloading {asset['name']}...")
        req = urllib.request.Request(
            asset["browser_download_url"],
            headers={"User-Agent": "SpeedFog/1.0"},
        )
        with urllib.request.urlopen(req, timeout=600) as response:
            with archive_path.open("wb") as out:
                shutil.copyfileobj(response, out)

        print_info("Extracting WitchyBND...")
        if WITCHYBND_DEST.exists():
            shutil.rmtree(WITCHYBND_DEST)
        WITCHYBND_DEST.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive_path, "r") as zf:
            zf.extractall(WITCHYBND_DEST)

        shutil.copy2(oodle_src, WITCHYBND_DEST / oodle_src.name)
        version = release.get("tag_name", "unknown")
        (WITCHYBND_DEST / "version.txt").write_text(f"{version}\n", encoding="utf-8")
    except (OSError, zipfile.BadZipFile, KeyError) as e:
        print_error(f"Failed to install WitchyBND: {e}")
        return False
    finally:
        archive_path.unlink(missing_ok=True)

    if not is_witchybnd_installed():
        print_error("WitchyBND extraction completed but required files are missing")
        return False

    print_ok(
        f"WitchyBND {release.get('tag_name', 'unknown')}"
        " installed in tools/witchybnd/"
    )
    return True


def _overlay_script_sources() -> list[Path]:
    """Return repackable source directories under data/overlay-src/script/.

    A repackable source is a directory ending in `-luabnd-dcx` that contains
    a WitchyBND manifest (`_witchy-bnd4.xml`).
    """
    script_src = OVERLAY_SRC_DEST / "script"
    if not script_src.is_dir():
        return []
    sources = []
    for entry in sorted(script_src.iterdir()):
        if not entry.is_dir() or not entry.name.endswith("-luabnd-dcx"):
            continue
        if not (entry / "_witchy-bnd4.xml").is_file():
            continue
        sources.append(entry)
    return sources


def build_overlay_scripts(force: bool = False) -> bool:
    """Repack `data/overlay-src/script/*-luabnd-dcx/` into `data/overlay/script/`.

    Each source directory is packed via WitchyBND (run through Wine on Linux).
    The resulting `*.luabnd.dcx` file is moved into `data/overlay/script/`.
    Silent no-op if there are no sources.
    """
    sources = _overlay_script_sources()
    if not sources:
        return True

    print("\n" + "=" * 50)
    print("Building overlay scripts (WitchyBND)")
    print("=" * 50)

    if not ensure_witchybnd(force):
        return False

    witchy_exe = WITCHYBND_DEST / "WitchyBND.exe"
    if sys.platform == "win32":
        cmd_prefix: list[str] = [str(witchy_exe)]
    else:
        if not shutil.which("wine"):
            print_error(
                "Wine not found but overlay scripts need repacking;"
                " install wine or remove sources under data/overlay-src/script/"
            )
            return False
        cmd_prefix = ["wine", str(witchy_exe)]

    script_dst = OVERLAY_DEST / "script"
    script_dst.mkdir(parents=True, exist_ok=True)

    for src in sources:
        produced_name = src.name.replace("-luabnd-dcx", ".luabnd.dcx")
        produced = src.parent / produced_name
        produced.unlink(missing_ok=True)

        cmd = cmd_prefix + ["--passive", str(src.resolve())]
        try:
            result = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                cwd=WITCHYBND_DEST,
            )
        except FileNotFoundError:
            print_error("Failed to run WitchyBND")
            return False

        if result.returncode != 0 or not produced.exists():
            stderr = result.stderr.strip() or result.stdout.strip()
            print_error(f"WitchyBND failed for {src.name}: {stderr}")
            return False

        produced.replace(script_dst / produced_name)
        print_ok(f"Built {produced_name}")

    return True


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Install SpeedFog local dependencies and packaging assets."
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
        "--game-dir",
        type=Path,
        required=True,
        help="Path to Elden Ring Game directory (for Oodle DLL and GamePatcher overlay)",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing files",
    )
    parser.add_argument(
        "--skip-overlay",
        action="store_true",
        help="Skip overlay generation (GamePatcher and WitchyBND script repack)",
    )
    args = parser.parse_args()

    # Check prerequisites
    print_step(1, 4, "Checking prerequisites...")
    sfextract = None
    if args.fogrando or args.itemrando:
        sfextract = find_sfextract()
        if not sfextract:
            print_error("sfextract not found")
            print()
            print("Install it with: dotnet tool install -g sfextract")
            return 1
        print_ok("sfextract found")
    else:
        print_info("No mod archive provided; skipping sfextract check")

    # Copy oo2core_6_win64.dll from game directory (needed by dotnet publish)
    print_step(2, 4, "Copying Oodle DLL from game directory...")
    if not copy_oodle_dll(args.game_dir, args.force):
        return 1

    print_step(3, 4, "Setting up mod dependencies...")

    success = True

    # Set up FogRando
    if args.fogrando:
        assert sfextract is not None
        if not setup_fogrando(sfextract, args.fogrando, args.force):
            success = False

    # Set up Item Randomizer
    if args.itemrando:
        assert sfextract is not None
        if not setup_itemrando(sfextract, args.itemrando, args.force):
            success = False

    print_step(4, 4, "Setting up packaging assets...")
    if success and not ensure_modengine(args.force):
        success = False

    # Run GamePatcher and WitchyBND to generate overlay files
    if success and not args.skip_overlay:
        if not run_modpatcher(args.game_dir):
            success = False
        if success and not build_overlay_scripts(args.force):
            success = False

    if success:
        print()
        print("=" * 50)
        print("Setup complete!")
        print("=" * 50)
        print()
        print("You can now generate runs with:")
        print("  uv run speedfog config.toml --logs")
        return 0
    else:
        print()
        print_error("Setup failed")
        return 1


if __name__ == "__main__":
    sys.exit(main())
