#!/usr/bin/env python3
"""Update i18n name translations from Elden Ring FMG data.

Runs the FmgNameExtractor C# tool to get official EN→FR name mappings,
then updates the [names.*] sections in data/i18n/fr.toml.

Usage:
    python tools/update_i18n_names.py [--dry-run] [--lang frafr]
    python tools/update_i18n_names.py --dry-run --skip-extract  # reuse cached FMG data

Prerequisites:
    - Build the extractor: cd writer/FmgNameExtractor && dotnet build
    - FMG data in writer/FogModWrapper/eldendata/Vanilla/msg/
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

# Project root (parent of tools/)
ROOT = Path(__file__).resolve().parent.parent

TOML_PATH = ROOT / "data" / "i18n" / "fr.toml"
EXTRACTOR_EXE = (
    ROOT
    / "writer"
    / "FmgNameExtractor"
    / "publish"
    / "win-x64"
    / "FmgNameExtractor.exe"
)
MSG_DIR = ROOT / "writer" / "FogModWrapper" / "eldendata" / "Vanilla" / "msg"
FMG_JSON_PATH = ROOT / "data" / "i18n" / "fmg_names.json"

# French definite articles
_FR_ARTICLE_RE = re.compile(r"^(le |la |l'|les )", re.IGNORECASE)


def strip_article(text: str) -> str:
    """Strip leading French definite article from a name."""
    return _FR_ARTICLE_RE.sub("", text)


def run_extractor(lang: str) -> dict[str, dict[str, str]]:
    """Run the C# FmgNameExtractor via Wine and return its JSON output."""
    if not EXTRACTOR_EXE.exists():
        print(f"Extractor not found: {EXTRACTOR_EXE}", file=sys.stderr)
        print(
            "Build it: dotnet publish writer/FmgNameExtractor -c Release "
            "-r win-x64 --self-contained -o writer/FmgNameExtractor/publish/win-x64",
            file=sys.stderr,
        )
        sys.exit(1)

    output_path = str(FMG_JSON_PATH)
    result = subprocess.run(
        ["wine", str(EXTRACTOR_EXE), str(MSG_DIR), output_path, "--target", lang],
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        print(f"Extractor failed (exit {result.returncode}):", file=sys.stderr)
        # Filter Wine debug noise from stderr
        for line in result.stderr.splitlines():
            # Filter Wine debug noise (hex thread IDs like "0009:fixme:")
            if not line.startswith(("wine:", "it looks like")) and not re.match(
                r"^[0-9a-f]{4}:", line
            ):
                print(f"  {line}", file=sys.stderr)
        sys.exit(1)

    with open(output_path, encoding="utf-8") as f:
        fmg_result: dict[str, dict[str, str]] = json.load(f)
        return fmg_result


def find_best_match(
    name: str,
    all_entries: dict[str, str],
) -> tuple[str | None, bool]:
    """Find the best FMG translation for a given English name.

    Returns (translation, is_singular_match).
    Tries in order:
    1. Exact match
    2. "The {name}" variant (FMG has "The Shaded Castle" not "Shaded Castle")
    3. Singular form for plurals ("Fell Twins" → "Fell Twin")
    No substring matching — too error-prone with short names and titled variants.
    """
    # 1. Exact match
    if name in all_entries:
        return all_entries[name], False

    # 2. Try "The {name}" (e.g., "Shaded Castle" → "The Shaded Castle")
    the_name = f"The {name}"
    if the_name in all_entries:
        return all_entries[the_name], False

    # 3. Try singular forms for plurals
    for singular in _singular_variants(name):
        if singular in all_entries:
            return all_entries[singular], True
        the_singular = f"The {singular}"
        if the_singular in all_entries:
            return all_entries[the_singular], True

    return None, False


def _singular_variants(name: str) -> list[str]:
    """Generate plausible singular forms from a plural English name."""
    variants = []
    if name.endswith("ies"):
        variants.append(name[:-3] + "y")
    if name.endswith("es") and not name.endswith("ies"):
        variants.append(name[:-2])
        variants.append(name[:-1])
    elif name.endswith("s") and not name.endswith("ss"):
        variants.append(name[:-1])
    # "Beastmen" → "Beastman"
    if name.endswith("men"):
        variants.append(name[:-2] + "an")
    return variants


def _safe_prepend_article(article: str, current_fr: str, fmg_fr: str) -> str:
    """Prepend article only if FMG translation starts with the same base word.

    This prevents gender mismatches ("le" + feminine noun) and structural
    mismatches ("le" + proper name that was restructured).
    """
    current_core = strip_article(current_fr)
    if not current_core or not fmg_fr:
        return fmg_fr

    first_current = current_core.split()[0].lower()
    first_fmg = fmg_fr.split()[0].lower()

    if first_current == first_fmg:
        return article + fmg_fr
    # Different first word → FMG restructured the name, use as-is
    return fmg_fr


def load_toml_names(toml_text: str) -> dict[str, dict[str, str]]:
    """Parse [names.*] sections from TOML text.

    Returns {"bosses": {"English Name": "French Name", ...}, ...}
    """
    sections: dict[str, dict[str, str]] = {}
    current_section = None

    for line in toml_text.splitlines():
        # Match [names.X] section headers
        m = re.match(r"^\[names\.(\w+)\]", line)
        if m:
            current_section = m.group(1)
            sections[current_section] = {}
            continue

        # Match any other section header -> leave names
        if re.match(r"^\[", line):
            current_section = None
            continue

        if current_section is None:
            continue

        # Match "key" = "value" lines
        m = re.match(r'^"(.+?)"\s*=\s*"(.+?)"', line)
        if m:
            sections[current_section][m.group(1)] = m.group(2)

    return sections


def update_toml_name(toml_text: str, en_name: str, old_fr: str, new_fr: str) -> str:
    """Replace a single name translation in the TOML text.

    Note: Uses regex rather than tomllib to preserve comments and formatting.
    Does not handle escaped quotes in values (none exist currently).
    """
    escaped_en = re.escape(en_name)
    escaped_old = re.escape(old_fr)
    pattern = f'^("{escaped_en}"\\s*=\\s*)"{escaped_old}"'
    replacement = f'\\1"{new_fr}"'
    result = re.sub(pattern, replacement, toml_text, count=1, flags=re.MULTILINE)
    if result == toml_text:
        print(f"  WARNING: failed to apply replacement for {en_name}", file=sys.stderr)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description="Update i18n names from FMG data")
    parser.add_argument(
        "--dry-run", action="store_true", help="Show changes without writing"
    )
    parser.add_argument(
        "--lang", default="frafr", help="Target language directory (default: frafr)"
    )
    parser.add_argument(
        "--skip-extract",
        action="store_true",
        help="Reuse existing fmg_names.json instead of re-running the extractor",
    )
    args = parser.parse_args()

    if not TOML_PATH.exists():
        print(f"TOML file not found: {TOML_PATH}", file=sys.stderr)
        sys.exit(1)

    # Extract or reuse FMG name mappings
    if args.skip_extract:
        if not FMG_JSON_PATH.exists():
            print(f"No cached FMG data: {FMG_JSON_PATH}", file=sys.stderr)
            print("Run without --skip-extract first.", file=sys.stderr)
            sys.exit(1)
        print(f"Reusing cached FMG data from {FMG_JSON_PATH}")
        with open(FMG_JSON_PATH, encoding="utf-8") as f:
            fmg_data = json.load(f)
    else:
        if not MSG_DIR.exists():
            print(f"FMG data not found: {MSG_DIR}", file=sys.stderr)
            print(
                "Run tools/setup_dependencies.py first to extract eldendata.",
                file=sys.stderr,
            )
            sys.exit(1)
        print(f"Extracting FMG names ({args.lang})...")
        fmg_data = run_extractor(args.lang)

    # Flatten FMG data for lookup
    all_fmg: dict[str, str] = {}
    for entries in fmg_data.values():
        all_fmg.update(entries)

    # Load current TOML
    toml_text = TOML_PATH.read_text(encoding="utf-8")
    names = load_toml_names(toml_text)

    # Match and update
    updated = 0
    skipped = 0
    not_found = 0
    singular_skipped = 0

    for section_name, entries in names.items():
        for en_name, current_fr in entries.items():
            fmg_fr, is_singular = find_best_match(en_name, all_fmg)

            if fmg_fr is None:
                not_found += 1
                if args.dry_run:
                    print(f"  ? {en_name} -> (no FMG match)")
                continue

            # Skip singular matches for plural entries — French pluralization
            # is too complex to automate reliably. Keep manual translation.
            if is_singular:
                singular_skipped += 1
                if args.dry_run:
                    print(
                        f"  ~ {en_name} -> singular FMG: {fmg_fr} (skipped, needs plural)"
                    )
                continue

            if fmg_fr == current_fr:
                skipped += 1
                continue

            # Article handling
            current_article_m = _FR_ARTICLE_RE.match(current_fr)
            fmg_article_m = _FR_ARTICLE_RE.match(fmg_fr)

            if current_article_m and not fmg_article_m:
                # TOML has article, FMG doesn't → safe prepend (checks first word)
                new_fr = _safe_prepend_article(
                    current_article_m.group(1), current_fr, fmg_fr
                )
            else:
                # FMG has its own article or neither has one → use FMG as-is
                new_fr = fmg_fr

            if new_fr == current_fr:
                skipped += 1
                continue

            print(f"  {section_name}: {en_name}")
            print(f"    - {current_fr}")
            print(f"    + {new_fr}")

            # Update in TOML text (name entries only, not overrides)
            toml_text = update_toml_name(toml_text, en_name, current_fr, new_fr)

            updated += 1

    print(
        f"\nSummary: {updated} updated, {skipped} unchanged, "
        f"{singular_skipped} plural (skipped), {not_found} not found in FMG"
    )

    if updated > 0 and not args.dry_run:
        TOML_PATH.write_text(toml_text, encoding="utf-8")
        print(f"Written to {TOML_PATH}")
    elif args.dry_run and updated > 0:
        print("(dry run - no changes written)")


if __name__ == "__main__":
    main()
