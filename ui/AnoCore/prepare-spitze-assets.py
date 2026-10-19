#!/usr/bin/env python3
"""Prepare the five user-supplied CS skin pictures for the Alles Spitze HUD.

Place ggspray.png, safarimesh.webp, neonrevolution.webp, gold.webp and
bluegem.png in one folder. This script writes five optimized PNGs to the
repository's casino asset directory without changing the source originals.

Requires Pillow: py -m pip install pillow
"""
from __future__ import annotations

import argparse
from pathlib import Path

SOURCES = {
    "spitze_ggspray": ("ggspray.png",),
    "spitze_safarimesh": ("safarimesh.webp", "safarimesh.png"),
    "spitze_neonrevolution": ("neonrevolution.webp", "neonrevolution.png"),
    "spitze_gold": ("gold.webp", "gold.png"),
    "spitze_knife": ("bluegem.png", "knife.png", "messer.png"),
}
TEXTURE_SIZE = 320


def prepare(source: Path, destination: Path) -> list[Path]:
    try:
        from PIL import Image, ImageOps
    except ImportError as exc:
        raise SystemExit("Pillow fehlt: py -m pip install pillow") from exc

    if not source.is_dir():
        raise SystemExit(f"Bildordner nicht gefunden: {source}")

    resolved: dict[str, Path] = {}
    for name, candidates in SOURCES.items():
        original = next((source / candidate for candidate in candidates
                         if (source / candidate).is_file()), None)
        if original is None:
            raise SystemExit(f"Bild fuer {name} fehlt. Erwartet: {', '.join(candidates)} in {source}")
        resolved[name] = original

    destination.mkdir(parents=True, exist_ok=True)
    created: list[Path] = []
    for name, original in resolved.items():
        with Image.open(original) as image:
            image.load()
            if image.width < 1 or image.height < 1:
                raise SystemExit(f"Ungueltiges Bild: {original}")
            rgba = image.convert("RGBA")
            scaled = ImageOps.contain(rgba, (TEXTURE_SIZE, TEXTURE_SIZE),
                                      Image.Resampling.LANCZOS)
            canvas = Image.new("RGBA", (TEXTURE_SIZE, TEXTURE_SIZE), (0, 0, 0, 0))
            canvas.alpha_composite(scaled, ((TEXTURE_SIZE - scaled.width) // 2,
                                            (TEXTURE_SIZE - scaled.height) // 2))
            output = destination / (name + ".png")
            canvas.save(output, "PNG", optimize=True)
            created.append(output)
            print(f"{original.name} -> {output}")
    return created


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True,
                        help="Folder containing ggspray.png and the four other original skins")
    parser.add_argument("--destination", type=Path, default=Path(__file__).parent
                        / "styles/custom_game/anocore/casino",
                        help="Where to write the five spitze_*.png files")
    args = parser.parse_args()
    paths = prepare(args.source, args.destination)
    print(f"Bereit: {len(paths)} Texturen. Jetzt build.ps1 erneut ausfuehren.")


if __name__ == "__main__":
    main()
