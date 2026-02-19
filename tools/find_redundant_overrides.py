#!/usr/bin/env python3
"""Identify redundant overrides in data/i18n/fr.toml.

An override is redundant if a pattern + names lookup produces
the same translation (case-insensitive comparison).

This simulates the server's translation pipeline from
speedfog-racing/server/speedfog_racing/services/i18n.py.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import tomllib

# ---------------------------------------------------------------------------
# French contraction rules (mirror of server)
# ---------------------------------------------------------------------------
_CONTRACTIONS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"\bde le\b"), "du"),
    (re.compile(r"\bde les\b"), "des"),
    (re.compile(r"\bDe le\b"), "Du"),
    (re.compile(r"\bDe les\b"), "Des"),
    (re.compile(r"\bà le\b"), "au"),
    (re.compile(r"\bà les\b"), "aux"),
    (re.compile(r"\bÀ le\b"), "Au"),
    (re.compile(r"\bÀ les\b"), "Aux"),
]

_PLACEHOLDER_NAMES = {"boss", "zone", "zone1", "zone2", "location", "direction", "name"}


def _apply_contractions(text: str) -> str:
    for pat, repl in _CONTRACTIONS:
        text = pat.sub(repl, text)
    return text


# ---------------------------------------------------------------------------
# Pattern → regex compilation (mirror of server)
# ---------------------------------------------------------------------------
_regex_cache: dict[str, re.Pattern[str]] = {}


def _build_pattern_regex(en_template: str) -> re.Pattern[str]:
    if en_template in _regex_cache:
        return _regex_cache[en_template]

    # Replace placeholders with temporary markers
    temp = en_template
    placeholders_found: list[str] = []
    for ph in _PLACEHOLDER_NAMES:
        token = "{" + ph + "}"
        if token in temp:
            temp = temp.replace(token, f"__PH_{ph}__")
            placeholders_found.append(ph)

    # Escape regex special chars
    escaped = re.escape(temp)

    # Replace markers with capture groups; handle possessive forms
    for ph in placeholders_found:
        marker = re.escape(f"__PH_{ph}__")
        # Check if followed by possessive 's or '
        poss = r"(?:'s|')?"
        escaped = re.sub(marker + re.escape("'s"), f"(?P<{ph}>.+?){poss}", escaped)
        escaped = re.sub(marker + re.escape("'"), f"(?P<{ph}>.+?){poss}", escaped)
        escaped = escaped.replace(re.escape(f"__PH_{ph}__"), f"(?P<{ph}>.+?)")

    regex = re.compile(f"^{escaped}$", re.IGNORECASE)
    _regex_cache[en_template] = regex
    return regex


# ---------------------------------------------------------------------------
# Name lookup (mirror of server)
# ---------------------------------------------------------------------------
def _lookup_name(
    name: str,
    bosses: dict[str, str],
    regions: dict[str, str],
    locations: dict[str, str],
) -> str | None:
    for d in (bosses, regions, locations):
        if name in d:
            return d[name]
    return None


def _translate_name(
    name: str,
    bosses: dict[str, str],
    regions: dict[str, str],
    locations: dict[str, str],
) -> str:
    result = _lookup_name(name, bosses, regions, locations)
    if result is not None:
        return result
    return name  # fallback to English


# ---------------------------------------------------------------------------
# Try matching an override against patterns
# ---------------------------------------------------------------------------
def _try_pattern_match(
    en_text: str,
    patterns: dict[str, str],
    bosses: dict[str, str],
    regions: dict[str, str],
    locations: dict[str, str],
) -> str | None:
    """Return the French translation via pattern matching, or None."""
    for en_template, fr_template in patterns.items():
        regex = _build_pattern_regex(en_template)
        m = regex.match(en_text)
        if m:
            # Extract and translate captured names
            fr_result = fr_template
            for ph in _PLACEHOLDER_NAMES:
                try:
                    captured = m.group(ph)
                except IndexError:
                    continue
                if captured is not None:
                    translated = _translate_name(captured, bosses, regions, locations)
                    fr_result = fr_result.replace("{" + ph + "}", translated)
            fr_result = _apply_contractions(fr_result)
            return fr_result
    return None


def main() -> int:
    toml_path = Path(__file__).resolve().parent.parent / "data" / "i18n" / "fr.toml"
    with open(toml_path, "rb") as f:
        data = tomllib.load(f)

    bosses = data.get("names", {}).get("bosses", {})
    regions = data.get("names", {}).get("regions", {})
    locations = data.get("names", {}).get("locations", {})

    patterns_text = data.get("patterns", {}).get("text", {})
    patterns_side_text = data.get("patterns", {}).get("side_text", {})
    overrides_text = data.get("overrides", {}).get("text", {})
    overrides_side_text = data.get("overrides", {}).get("side_text", {})

    redundant: list[
        tuple[str, str, str, str]
    ] = []  # (section, en_key, override_val, pattern_val)
    non_redundant: list[tuple[str, str, str]] = []

    for section_name, overrides, pattern_sets in [
        ("overrides.text", overrides_text, [patterns_text, patterns_side_text]),
        (
            "overrides.side_text",
            overrides_side_text,
            [patterns_side_text, patterns_text],
        ),
    ]:
        for en_key, override_val in overrides.items():
            matched = None
            for patterns in pattern_sets:
                matched = _try_pattern_match(
                    en_key, patterns, bosses, regions, locations
                )
                if matched is not None:
                    break

            if matched is not None:
                # Case-insensitive comparison
                if matched.lower() == override_val.lower():
                    redundant.append((section_name, en_key, override_val, matched))
                else:
                    non_redundant.append((section_name, en_key, override_val))
                    print(
                        f'  DIFFERS: [{section_name}] "{en_key}"\n'
                        f"    override : {override_val}\n"
                        f"    pattern  : {matched}"
                    )
            else:
                non_redundant.append((section_name, en_key, override_val))

    print(f"\n{'='*70}")
    print(f"REDUNDANT overrides: {len(redundant)}")
    print(f"Non-redundant overrides: {len(non_redundant)}")
    print(f"{'='*70}\n")

    if redundant:
        print("REDUNDANT entries to remove:")
        for section, en_key, override_val, pattern_val in redundant:
            case_note = ""
            if override_val != pattern_val:
                case_note = f"  (case differs: override={override_val!r} pattern={pattern_val!r})"
            print(f'  [{section}] "{en_key}"{case_note}')
        print()

    return 0 if not redundant else 1


if __name__ == "__main__":
    sys.exit(main())
