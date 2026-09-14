from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Any

import piexif
from PIL import Image, ExifTags


def _stat_times(path: Path) -> tuple[datetime | None, datetime | None, datetime | None]:
    try:
        info = path.stat()
    except OSError:
        return None, None, None
    modified = datetime.fromtimestamp(info.st_mtime)
    accessed = datetime.fromtimestamp(info.st_atime)
    created = datetime.fromtimestamp(getattr(info, "st_ctime", info.st_mtime))
    birth = getattr(info, "st_birthtime", None)
    if birth:
        created = datetime.fromtimestamp(birth)
    return created, modified, accessed


def read_exif(path: Path) -> dict[str, Any]:
    suffix = path.suffix.lower()
    if suffix not in {".jpg", ".jpeg", ".tif", ".tiff", ".webp"}:
        return {}
    try:
        with Image.open(path) as image:
            width, height = image.size
            data: dict[str, Any] = {"width": width, "height": height}
        try:
            exif_dict = piexif.load(str(path))
        except Exception:
            exif_dict = {}
        zeroth = exif_dict.get("0th") or {}
        exif = exif_dict.get("Exif") or {}
        raw_date = exif.get(piexif.ExifIFD.DateTimeOriginal) or zeroth.get(piexif.ImageIFD.DateTime)
        if raw_date:
            data["date"] = raw_date.decode("utf-8") if isinstance(raw_date, bytes) else str(raw_date)
        make = zeroth.get(piexif.ImageIFD.Make)
        model = zeroth.get(piexif.ImageIFD.Model)
        if make:
            data["make"] = make.decode("utf-8") if isinstance(make, bytes) else str(make)
        if model:
            data["model"] = model.decode("utf-8") if isinstance(model, bytes) else str(model)
        if not data.get("date"):
            with Image.open(path) as image:
                tagged = {ExifTags.TAGS.get(key, key): value for key, value in (image.getexif() or {}).items()}
            if "DateTimeOriginal" in tagged:
                data["date"] = tagged["DateTimeOriginal"]
            elif "DateTime" in tagged:
                data["date"] = tagged["DateTime"]
        return data
    except Exception:
        return {}


def read_id3(path: Path) -> dict[str, Any]:
    if path.suffix.lower() not in {".mp3", ".flac", ".ogg", ".m4a"}:
        return {}
    data: dict[str, Any] = {}
    try:
        from mutagen import File as MutagenFile
        from mutagen.id3 import ID3
    except Exception:
        return {}
    try:
        audio = MutagenFile(path, easy=True)
        if audio and audio.tags:

            def first(key: str) -> str:
                value = audio.tags.get(key)
                if not value:
                    return ""
                if isinstance(value, list):
                    return str(value[0])
                return str(value)

            data = {
                "artist": first("artist"),
                "album": first("album"),
                "title": first("title"),
                "track": first("tracknumber"),
                "genre": first("genre"),
                "year": first("date"),
            }
            if getattr(audio, "info", None) is not None:
                length = getattr(audio.info, "length", None)
                if length:
                    data["length"] = round(length, 2)
    except Exception:
        pass
    if not data.get("artist") and path.suffix.lower() == ".mp3":
        try:
            tags = ID3(path)

            def text(frame: str) -> str:
                value = tags.get(frame)
                if not value:
                    return ""
                return str(value.text[0]) if getattr(value, "text", None) else str(value)

            data = {
                "artist": text("TPE1"),
                "album": text("TALB"),
                "title": text("TIT2"),
                "track": text("TRCK"),
                "genre": text("TCON"),
                "year": text("TDRC") or text("TYER"),
            }
        except Exception:
            return data
    return {key: value for key, value in data.items() if value}


def read_props(path: Path) -> dict[str, Any]:
    props: dict[str, Any] = {}
    suffix = path.suffix.lower()
    if suffix in {".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff"}:
        try:
            with Image.open(path) as image:
                props["width"] = image.size[0]
                props["height"] = image.size[1]
                props["format"] = image.format
                props["mode"] = image.mode
        except Exception:
            pass
    return props


def describe_path(path: Path, root: Path | None = None) -> dict[str, Any]:
    created, modified, accessed = _stat_times(path)
    try:
        size = 0 if path.is_dir() else path.stat().st_size
    except OSError:
        size = 0
    depth = 0
    if root:
        try:
            depth = len(path.relative_to(root).parts) - 1
        except ValueError:
            depth = 0
    is_dir = path.is_dir()
    return {
        "path": str(path),
        "name": path.name,
        "stem": path.stem if not is_dir else path.name,
        "ext": path.suffix if not is_dir else "",
        "folder": path.parent.name,
        "parent": str(path.parent),
        "is_dir": is_dir,
        "size": size,
        "created": created,
        "modified": modified,
        "accessed": accessed,
        "depth": max(depth, 0),
        "exif": {} if is_dir else read_exif(path),
        "id3": {} if is_dir else read_id3(path),
        "props": {} if is_dir else read_props(path),
    }
