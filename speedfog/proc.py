"""Shared subprocess streaming helper.

Used by the FogModWrapper and ItemRandomizerWrapper runners to stream
subprocess output in real time, prefixing each line with the elapsed
time since process start. The prefixes turn the wrappers' existing
phase-boundary log lines (including FogMod's and RandomizerCommon's
``notify`` callbacks) into a phase-level timing profile.
"""

from __future__ import annotations

import subprocess
import time
from pathlib import Path


def format_elapsed_prefix(seconds: float) -> str:
    """Format an elapsed-time line prefix, e.g. ``[+  12.3s] ``."""
    return f"[+{seconds:6.1f}s] "


def stream_command(cmd: list[str], cwd: Path | None = None) -> int:
    """Run *cmd*, streaming its output with elapsed-time prefixes.

    stderr is merged into stdout and each line is printed as it arrives,
    prefixed with the time elapsed since the process started. Undecodable
    bytes (e.g. Windows-1252 characters in Wine output) are replaced
    rather than raising.

    Returns the process exit code.
    """
    start = time.perf_counter()
    # text=True implies universal newlines: lone \r (console progress
    # updates) starts a new prefixed line and \r\n is normalized. This is
    # intentional; it keeps captured logs free of stray carriage returns.
    with subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        cwd=cwd,
        bufsize=1,  # Line buffered
    ) as process:
        assert process.stdout is not None
        for line in process.stdout:
            elapsed = time.perf_counter() - start
            print(f"{format_elapsed_prefix(elapsed)}{line}", end="")
    return process.returncode
