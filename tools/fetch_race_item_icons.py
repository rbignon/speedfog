#!/usr/bin/env python3
"""Download the race item icons (data/plugins/race_items_icon_<icon>.png).

The art is not in the repository: each icon of race_items.toml's [icons] is
downloaded from its URL below and checked against its SHA-256, so a build
only ever ships the exact files listed here. tools/bootstrap.py runs this
before StaticModBuilder; run it alone (python tools/fetch_race_item_icons.py)
to refresh the icons without a full bootstrap. An icon already on disk with
the expected hash is not downloaded again. The images come in any shape:
ItemIconPatcher centers each one on a transparent square. The files are
gitignored.

Changing an icon: put the new URL and the new file's SHA-256 (sha256sum) in
ICON_SOURCES, then rerun bootstrap. speedfog-racing's
tools/fetch_race_item_icons.py lists the same images for the racing mod's
overlay: keep both lists in step.
"""

from __future__ import annotations

import hashlib
import http.client
import sys
import urllib.request
from pathlib import Path

PLUGINS_DIR = Path(__file__).resolve().parent.parent / "data" / "plugins"
USER_AGENT = "race-item-icon-fetch/1.0"
TIMEOUT_S = 60

# Icon name ([icons] of race_items.toml): source URL and SHA-256 of the file.
# Wiki originals, never /thumb/ URLs: the wiki regenerates its thumbnails (new
# bytes for the same image), which would break the pinned hash.
ICON_SOURCES: dict[str, tuple[str, str]] = {
    "shield": (
        "https://mario.wiki.gallery/images/9/9d/MKW_Super_Horn_Roulette.png",
        "904d6b0684635da8723f83285ca1f8b879c13c049c2cedfb8096fb5106e6bda2",
    ),
    "rune_pouch": (
        "https://mario.wiki.gallery/images/1/10/MKW_Coin_Roulette.png",
        "71648e34eca3325f064559aef0808c613ac0273c352efba59ff5f8aca2e0d059",
    ),
    "banana": (
        "https://mario.wiki.gallery/images/7/75/MKW_Banana_Roulette.png",
        "51de9a7fbaa0011981ba90501603ca02fb33c5d889b0099812cb62df8901a253",
    ),
    "red_shell": (
        "https://mario.wiki.gallery/images/b/b8/MKW_Red_Shell_Roulette.png",
        "a7d26007a0c266c637f09548036b9300e0290248d018149dfec16a5ea96a2b6e",
    ),
    "mushroom": (
        "https://mario.wiki.gallery/images/0/03/MKW_Mushroom_Roulette.png",
        "bf94e2195a3220e03438cd0d334b8c0f4d9e6fbf4b206fe2fb68549d9655c920",
    ),
    "hammer": (
        "https://mario.wiki.gallery/images/6/6c/MKW_Hammer_Roulette.png",
        "be454026e154a3a2980903b3e6aa01b6d9c504aaf64cc375781008e5e6d9d6b1",
    ),
    "ink": (
        "https://mario.wiki.gallery/images/4/49/BlooperMK8.png",
        "a247c4fe4b6e186dfe72add877fcd2ec7ab4e5282830aa654fa02687b1c65f8d",
    ),
    "blue_shell": (
        "https://mario.wiki.gallery/images/7/7c/MKW_Spiny_Shell_Roulette.png",
        "efe9af48a89a15707beef449f3f0bef07921654893502dab53fafaefd4276bb7",
    ),
    "lightning": (
        "https://mario.wiki.gallery/images/1/17/MKW_Lightning_Roulette.png",
        "bd4d5d86cf3ef1a92ee60265dc8c2348783c060c60dd5161e2044a319d880a11",
    ),
    "flask_stopper": (
        "https://mario.wiki.gallery/images/5/58/MKW_Boo_Roulette.png",
        "aa482e40fc87d8de2e0fc87b50ae619663e11ad07fdb5e8c0ad1bf42610874f7",
    ),
    "toxic_blossom": (
        "https://mario.wiki.gallery/images/8/8d/PiranhaPlantPotMK8.png",
        "8a772a5543e33f70e5d6b7f29871fbc916422564a2108ac921290c2154a0b239",
    ),
}


def icon_path(dest_dir: Path, icon: str) -> Path:
    return dest_dir / f"race_items_icon_{icon}.png"


def fetch_icons(
    dest_dir: Path = PLUGINS_DIR,
    sources: dict[str, tuple[str, str]] = ICON_SOURCES,
) -> tuple[int, list[str]]:
    """Download every icon missing from dest_dir or not matching its hash.

    Returns the number of icons downloaded and one problem line per icon
    that could not be (download error or unexpected hash). A failed icon
    leaves its file as it was; the others are still downloaded.
    """
    downloaded = 0
    problems: list[str] = []
    for icon, (url, sha256) in sources.items():
        dest = icon_path(dest_dir, icon)
        if dest.is_file() and hashlib.sha256(dest.read_bytes()).hexdigest() == sha256:
            continue
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=TIMEOUT_S) as response:
                data = response.read()
        except (OSError, ValueError, http.client.HTTPException) as exc:
            problems.append(f"{icon}: cannot download {url} ({exc})")
            continue
        actual = hashlib.sha256(data).hexdigest()
        if actual != sha256:
            problems.append(
                f"{icon}: {url} has SHA-256 {actual}, expected {sha256}"
                " (if the new image is right, update ICON_SOURCES)"
            )
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        partial = dest.with_name(dest.name + ".part")
        partial.write_bytes(data)
        partial.replace(dest)
        downloaded += 1
    return downloaded, problems


def main() -> int:
    downloaded, problems = fetch_icons()
    for line in problems:
        print(line, file=sys.stderr)
    print(
        f"{downloaded} downloaded, {len(ICON_SOURCES) - downloaded - len(problems)}"
        f" already up to date, {len(problems)} failed"
    )
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
