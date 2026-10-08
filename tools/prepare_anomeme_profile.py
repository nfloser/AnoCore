"""Prepare an offline ANOMEME profile without writing into an existing server config."""

import argparse
import json
import os
import shutil
import tempfile
from datetime import datetime, timezone
from pathlib import Path


TEMPLATES = Path(__file__).resolve().parents[1] / "examples/server-profiles/anomeme"
NAMES = ("ranks", "gameplay-stats", "gameplay-xp", "progression", "achievements", "challenges", "seasons")


def read(path):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise ValueError(f"{path.name}: JSON object required")
    return data


def canonical(data, keys):
    """Match the server's case-insensitive property names without duplicate aliases."""
    names = {key.lower(): key for key in keys}
    result = {}
    for key, value in data.items():
        target = names.get(key.lower(), key)
        if target in result:
            raise ValueError(f"Duplicate configuration property: {target}")
        result[target] = value
    return result


def combine(existing, proposed, limit, name):
    if not isinstance(existing, list):
        raise ValueError(f"{name}: existing definitions must be a list")
    ids = set()
    result = []
    existing_ids = [item.get("Id") for item in existing if isinstance(item, dict)]
    if len(existing_ids) != len(set(existing_ids)):
        raise ValueError(f"{name}: existing IDs are duplicated")
    for item in existing + proposed:
        if not isinstance(item, dict) or not isinstance(item.get("Id"), str):
            raise ValueError(f"{name}: definitions need an Id")
        if item["Id"] in ids:
            continue
        ids.add(item["Id"])
        result.append(item)
    if len(result) > limit:
        raise ValueError(f"{name}: merged definitions exceed {limit}; remove examples explicitly")
    return result


def prepare(output, existing=None):
    output = Path(output)
    if output.exists():
        raise ValueError("Output must be a new directory; existing files are never overwritten")
    profiles = {name: read(TEMPLATES / f"{name}.json") for name in NAMES}
    originals = {}
    if existing is not None:
        existing = Path(existing)
        if not existing.is_dir():
            raise ValueError("Existing configuration directory does not exist")
        for name in NAMES:
            path = existing / f"{name}.json"
            if path.exists():
                originals[name] = canonical(read(path), (*profiles[name], "EarnFromUtc", "Levels", "Boosts"))
    xp = profiles["gameplay-xp"]
    if "gameplay-xp" in originals:
        old = originals["gameplay-xp"]
        if not isinstance(old.get("EarnFromUtc"), str):
            raise ValueError("Existing gameplay-xp.EarnFromUtc is required; do not reset earning history implicitly")
        weights = xp["GameplayXp"] | canonical(old.get("GameplayXp", {}), xp["GameplayXp"])
        xp.update(old)
        xp["GameplayXp"] = weights
    else:
        xp["EarnFromUtc"] = datetime.now(timezone.utc).isoformat(timespec="microseconds")
    if "progression" in originals:
        profiles["progression"] = originals["progression"]
    elif "achievements" in originals:
        legacy = originals["achievements"]
        for key in ("Levels", "Boosts"):
            if key in legacy:
                profiles["progression"][key] = legacy[key]
    for name, fields in (("achievements", {"Achievements": 32}),
                         ("challenges", {"Recurring": 32, "Predefined": 96})):
        if name not in originals:
            continue
        proposed = profiles[name]
        merged = proposed | originals[name]
        for key, limit in fields.items():
            merged[key] = combine(originals[name].get(key, []), proposed[key], limit, f"{name}.{key}")
        profiles[name] = merged
    if "seasons" in originals:
        profiles["seasons"] = originals["seasons"]
    # Parse all input/merges before creating output. Runtime owns full typed validation. Only a staging directory is mutated.
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".anomeme-profile-", dir=output.parent))
    try:
        for name, data in profiles.items():
            (staging / f"{name}.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (staging / "README.txt").write_text(
            "Offline-Vorschlag, noch nicht installiert. Rangregeln entsprechen dem Server-Backup.\n"
            "Vor dem Kopieren vorhandene Dateien sichern und Änderungen prüfen.\n"
            "EarnFromUtc und bestehende Progression/Seasons/Definitionen wurden übernommen, soweit angegeben.\n"
            "Ein Wechsel zu EventLedger übernimmt keine alten Spieler-Punktestände automatisch.\n"
            "Siehe docs/anomeme-server-profile.md für Anwendung und Abnahme.\n", encoding="utf-8")
        os.rename(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--existing-config", type=Path)
    args = parser.parse_args()
    try:
        result = prepare(args.output, args.existing_config)
    except (ValueError, OSError, TypeError) as error:
        parser.exit(1, f"Profil konnte nicht vorbereitet werden: {error}\n")
    print(f"Profil vorbereitet: {result}")


if __name__ == "__main__":
    main()
