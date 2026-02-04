"""FogMod wrapper for SpeedFog."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path


def run_fogmodwrapper(
    seed_dir: Path,
    game_dir: Path,
    platform: str | None,
    verbose: bool,
    merge_dir: Path | None = None,
) -> bool:
    """Run FogModWrapper to generate the mod.

    Args:
        seed_dir: Directory containing graph.json (output also goes here)
        game_dir: Path to Elden Ring Game directory
        platform: "windows", "linux", or None for auto-detect
        verbose: Print command and output
        merge_dir: Optional directory with Item Randomizer output to merge

    Returns:
        True on success, False on failure.
    """
    project_root = Path(__file__).parent.parent
    wrapper_dir = project_root / "writer" / "FogModWrapper"
    wrapper_exe = wrapper_dir / "publish" / "win-x64" / "FogModWrapper.exe"
    data_dir = project_root / "data"

    if not wrapper_exe.exists():
        print(f"Error: FogModWrapper not found at {wrapper_exe}", file=sys.stderr)
        print(
            "Run: python tools/setup_dependencies.py --fogrando <path> --itemrando <path>",
            file=sys.stderr,
        )
        return False

    # Detect platform (only Windows is native, everything else needs Wine)
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    # Check Wine availability on non-Windows
    if platform == "linux" and shutil.which("wine") is None:
        print(
            "Error: Wine not found. Install wine to build mods on Linux.",
            file=sys.stderr,
        )
        return False

    # Build command with absolute paths (since we change cwd)
    seed_dir = seed_dir.resolve()
    game_dir = game_dir.resolve()
    data_dir = data_dir.resolve()

    if platform == "linux":
        cmd = ["wine", str(wrapper_exe.resolve())]
    else:
        cmd = [str(wrapper_exe.resolve())]

    cmd.extend(
        [
            str(seed_dir),
            "--game-dir",
            str(game_dir),
            "--data-dir",
            str(data_dir),
            "-o",
            str(seed_dir),
        ]
    )

    if merge_dir is not None:
        cmd.extend(["--merge-dir", str(merge_dir.resolve())])

    if verbose:
        print(f"Running: {' '.join(cmd)}")
        print(f"Working directory: {wrapper_dir}")

    # Run from wrapper_dir so FogModWrapper finds eldendata/
    # Stream output in real-time
    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        cwd=wrapper_dir,
        bufsize=1,  # Line buffered
    )

    # Print output as it arrives
    assert process.stdout is not None
    for line in process.stdout:
        print(line, end="")

    process.wait()
    return process.returncode == 0
