"""Tests for the shared subprocess streaming helper."""

from __future__ import annotations

import re
import sys

from speedfog.proc import stream_command

# A prefixed line looks like "[+   0.0s] hello"
PREFIX_RE = r"\[\+\s*\d+\.\d+s\] "


def _py(code: str) -> list[str]:
    return [sys.executable, "-c", code]


def test_streams_lines_with_elapsed_prefix(capsys):
    rc = stream_command(_py("print('hello'); print('world')"))
    assert rc == 0
    out = capsys.readouterr().out.splitlines()
    assert re.fullmatch(PREFIX_RE + "hello", out[0])
    assert re.fullmatch(PREFIX_RE + "world", out[1])


def test_returns_exit_code():
    rc = stream_command(_py("import sys; sys.exit(3)"))
    assert rc == 3


def test_merges_stderr_into_stdout(capsys):
    rc = stream_command(_py("import sys; print('oops', file=sys.stderr)"))
    assert rc == 0
    out = capsys.readouterr().out
    assert re.search(PREFIX_RE + "oops", out)


def test_replaces_undecodable_bytes(capsys):
    # Wine output may contain non-UTF-8 bytes (e.g. Windows-1252); the
    # helper must replace them instead of crashing.
    rc = stream_command(_py("import sys; sys.stdout.buffer.write(b'caf\\xe9\\n')"))
    assert rc == 0
    out = capsys.readouterr().out
    assert re.search(PREFIX_RE + "caf�", out)


def test_honors_cwd(tmp_path, capsys):
    rc = stream_command(
        _py("import os; print(os.path.basename(os.getcwd()))"),
        cwd=tmp_path,
    )
    assert rc == 0
    assert tmp_path.name in capsys.readouterr().out
