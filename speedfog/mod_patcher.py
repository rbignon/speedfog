"""ModPatcher runner for SpeedFog post-processing."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path


def run_modpatcher(
    seed_dir: Path,
    game_dir: Path,
    platform: str | None,
    verbose: bool,
) -> bool:
    """Run ModPatcher for post-processing patches (grace animations, etc.).

    Args:
        seed_dir: Directory containing the mod output (mods/fogmod/)
        game_dir: Path to Elden Ring Game directory
        platform: "windows", "linux", or None for auto-detect
        verbose: Print command and output

    Returns:
        True on success, False on failure.
    """
    project_root = Path(__file__).parent.parent
    patcher_dir = project_root / "writer" / "ModPatcher"
    patcher_exe = patcher_dir / "publish" / "win-x64" / "ModPatcher.exe"

    if not patcher_exe.exists():
        if verbose:
            print("ModPatcher not published, skipping post-processing")
        return True

    # Detect platform
    if platform is None or platform == "auto":
        platform = "windows" if sys.platform == "win32" else "linux"

    if platform == "linux" and shutil.which("wine") is None:
        print(
            "Error: Wine not found. Install wine to run ModPatcher on Linux.",
            file=sys.stderr,
        )
        return False

    game_dir = game_dir.resolve()
    mod_dir = (seed_dir / "mods" / "fogmod").resolve()

    if platform == "linux":
        cmd = ["wine", str(patcher_exe.resolve())]
    else:
        cmd = [str(patcher_exe.resolve())]

    cmd.extend([str(game_dir), str(mod_dir)])

    if verbose:
        print(f"Running: {' '.join(cmd)}")

    process = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        cwd=patcher_dir,
        bufsize=1,
    )

    assert process.stdout is not None
    for line in process.stdout:
        print(line, end="")

    process.wait()
    return process.returncode == 0
