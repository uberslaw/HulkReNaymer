from __future__ import annotations

import json
from pathlib import Path

from hulkrenaymer.apply import data_dir


def favorites_path() -> Path:
    return data_dir() / "favorites.json"


def load_favorites() -> list[dict]:
    path = favorites_path()
    if not path.exists():
        return []
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return []
    if isinstance(data, list):
        return data
    return []


def save_favorite(name: str, rules: dict) -> list[dict]:
    favorites = [item for item in load_favorites() if item.get("name") != name]
    favorites.append({"name": name, "rules": rules})
    favorites_path().write_text(json.dumps(favorites, indent=2), encoding="utf-8")
    return favorites


def delete_favorite(name: str) -> list[dict]:
    favorites = [item for item in load_favorites() if item.get("name") != name]
    favorites_path().write_text(json.dumps(favorites, indent=2), encoding="utf-8")
    return favorites


def parse_mapping(text: str) -> dict[str, str]:
    mapping: dict[str, str] = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "|" in line:
            old, new = line.split("|", 1)
        elif "," in line:
            old, new = line.split(",", 1)
        elif "\t" in line:
            old, new = line.split("\t", 1)
        else:
            continue
        old, new = old.strip(), new.strip()
        if old and new:
            mapping[old] = new
    return mapping
