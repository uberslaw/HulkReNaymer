from __future__ import annotations

import fnmatch
import os
import re
from pathlib import Path

from hulkrenaymer.metadata import describe_path
from hulkrenaymer.models import FileItem

SKIP_NAMES = {".git", ".venv", "venv", "node_modules", "__pycache__"}


def match_wildcard(name: str, pattern: str) -> bool:
    if not pattern or pattern in {"*", "*.*"}:
        return True
    parts = [part.strip() for part in pattern.replace(";", ",").split(",") if part.strip()]
    return any(fnmatch.fnmatch(name, part) for part in parts)


def scan_directory(
    path: str | Path,
    *,
    recurse: bool = False,
    include_files: bool = True,
    include_folders: bool = False,
    wildcard: str = "*",
    name_regex: str = "",
    min_length: int = 0,
    max_length: int = 0,
    max_files: int = 8000,
    include_hidden: bool = False,
    max_depth: int = 0,
) -> tuple[list[FileItem], str]:
    root = Path(path).expanduser().resolve()
    if not root.exists():
        return [], f"Path does not exist: {root}"
    if not root.is_dir():
        return [], f"Not a directory: {root}"

    compiled = re.compile(name_regex) if name_regex else None
    items: list[FileItem] = []
    warning = ""

    def consider(entry: Path, depth: int) -> None:
        nonlocal warning
        if len(items) >= max_files:
            return
        name = entry.name
        if not include_hidden and name.startswith("."):
            return
        if name in SKIP_NAMES:
            return
        is_dir = entry.is_dir()
        if is_dir and not include_folders:
            return
        if not is_dir and not include_files:
            return
        if not match_wildcard(name, wildcard):
            return
        if compiled and not compiled.search(name):
            return
        if min_length and len(name) < min_length:
            return
        if max_length and len(name) > max_length:
            return
        try:
            payload = describe_path(entry, root)
        except OSError:
            return
        payload["selected"] = True
        payload["depth"] = depth
        items.append(FileItem(**payload))

    if recurse:
        for dirpath, dirnames, filenames in os.walk(root):
            current = Path(dirpath)
            try:
                depth = len(current.relative_to(root).parts)
            except ValueError:
                depth = 0
            if max_depth and depth > max_depth:
                dirnames[:] = []
                continue
            dirnames[:] = [name for name in dirnames if include_hidden or not name.startswith(".")]
            dirnames.sort(key=str.casefold)
            if include_folders and current != root:
                consider(current, depth)
            if include_files:
                for filename in sorted(filenames, key=str.casefold):
                    consider(current / filename, depth)
                    if len(items) >= max_files:
                        break
            if len(items) >= max_files:
                warning = f"Listing truncated at {max_files} items"
                break
    else:
        try:
            entries = sorted(root.iterdir(), key=lambda item: (not item.is_dir(), item.name.casefold()))
        except OSError as exc:
            return [], str(exc)
        for entry in entries:
            consider(entry, 0)
            if len(items) >= max_files:
                warning = f"Listing truncated at {max_files} items"
                break

    return items, warning


def list_children(path: str | Path) -> list[dict]:
    root = Path(path).expanduser().resolve()
    if not root.is_dir():
        return []
    children = []
    try:
        entries = sorted(root.iterdir(), key=lambda item: item.name.casefold())
    except OSError:
        return []
    for entry in entries:
        if entry.name.startswith(".") or entry.name in SKIP_NAMES:
            continue
        if entry.is_dir():
            children.append({"name": entry.name, "path": str(entry), "is_dir": True})
    return children
