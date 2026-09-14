from __future__ import annotations

import os
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from hulkrenaymer.apply import append_log, commit_preview, data_dir, load_undo, undo_last
from hulkrenaymer.engine import build_preview
from hulkrenaymer.favorites import delete_favorite, load_favorites, parse_mapping, save_favorite
from hulkrenaymer.models import FileItem, Rules
from hulkrenaymer.scan import list_children, scan_directory
from hulkrenaymer.seed import seed_demo

WEB_DIR = Path(__file__).parent / "web"

app = FastAPI(title="HulkReNaymer", version="1.0.0")
app.mount("/static", StaticFiles(directory=WEB_DIR), name="static")


class ScanRequest(BaseModel):
    path: str
    recurse: bool = False
    include_files: bool = True
    include_folders: bool = False
    include_hidden: bool = False
    wildcard: str = "*"
    name_regex: str = ""
    min_length: int = 0
    max_length: int = 0
    max_files: int = 8000
    max_depth: int = 0


class PreviewRequest(ScanRequest):
    rules: dict = Field(default_factory=dict)
    selected: list[str] | None = None
    sort_column: str = "name"
    sort_desc: bool = False


class RenameRequest(PreviewRequest):
    confirm: bool = False


class FavoriteRequest(BaseModel):
    name: str
    rules: dict = Field(default_factory=dict)


class MappingRequest(BaseModel):
    text: str


def resolve_user_path(raw: str) -> Path:
    path = Path(raw or ".").expanduser()
    if not path.is_absolute():
        path = Path.cwd() / path
    return path.resolve()


def items_from_scan(req: ScanRequest) -> tuple[list[FileItem], str]:
    return scan_directory(
        resolve_user_path(req.path),
        recurse=req.recurse,
        include_files=req.include_files,
        include_folders=req.include_folders,
        wildcard=req.wildcard,
        name_regex=req.name_regex,
        min_length=req.min_length,
        max_length=req.max_length,
        max_files=req.max_files,
        include_hidden=req.include_hidden,
        max_depth=req.max_depth,
    )


def apply_selection(items: list[FileItem], selected: list[str] | None) -> list[FileItem]:
    if selected is None:
        return items
    wanted = set(selected)
    for item in items:
        item.selected = item.path in wanted
    return items


def row_payload(row) -> dict:
    return {
        "path": row.path,
        "old_name": row.old_name,
        "new_name": row.new_name,
        "new_path": row.new_path,
        "is_dir": row.is_dir,
        "size": row.size,
        "created": row.created,
        "modified": row.modified,
        "accessed": row.accessed,
        "selected": row.selected,
        "changed": row.changed,
        "status": row.status,
        "warning": row.warning,
        "folder": row.folder,
        "ext": row.ext,
        "exif": row.exif,
        "id3": row.id3,
    }


@app.get("/")
def index():
    return FileResponse(WEB_DIR / "index.html")


@app.get("/api/health")
def health():
    return {"ok": True, "name": "HulkReNaymer", "version": "1.0.0"}


@app.get("/api/roots")
def roots():
    home = str(Path.home())
    cwd = str(Path.cwd())
    demo = Path.cwd() / "demo_files"
    entries = [
        {"name": "Home", "path": home},
        {"name": "Current folder", "path": cwd},
    ]
    if demo.is_dir():
        entries.append({"name": "Demo files", "path": str(demo)})
    if Path("/workspace").exists():
        entries.append({"name": "Workspace", "path": "/workspace"})
    if os.name == "nt":
        for letter in "CDEFGHIJ":
            drive = Path(f"{letter}:/")
            if drive.exists():
                entries.append({"name": f"{letter}:", "path": str(drive)})
    else:
        entries.append({"name": "Root", "path": "/"})
    # de-dupe
    seen = set()
    unique = []
    for item in entries:
        if item["path"] not in seen:
            unique.append(item)
            seen.add(item["path"])
    return {"roots": unique, "cwd": cwd, "home": home}


@app.get("/api/tree")
def tree(path: str):
    target = resolve_user_path(path)
    if not target.exists():
        raise HTTPException(404, "Path not found")
    parent = str(target.parent) if target.parent != target else None
    return {
        "path": str(target),
        "name": target.name or str(target),
        "parent": parent,
        "children": list_children(target),
    }


@app.post("/api/scan")
def scan(req: ScanRequest):
    items, warning = items_from_scan(req)
    return {
        "path": str(resolve_user_path(req.path)),
        "count": len(items),
        "warning": warning,
        "items": [item.__dict__ | {"created": item.created.isoformat() if item.created else None, "modified": item.modified.isoformat() if item.modified else None, "accessed": item.accessed.isoformat() if item.accessed else None} for item in items],
    }


@app.post("/api/preview")
def preview(req: PreviewRequest):
    items, warning = items_from_scan(req)
    items = apply_selection(items, req.selected)
    rules = Rules.from_dict(req.rules)
    rows = build_preview(items, rules, req.sort_column, req.sort_desc)
    counts = {
        "total": len(rows),
        "selected": sum(1 for row in rows if row.selected),
        "changed": sum(1 for row in rows if row.changed and row.status == "ok"),
        "unchanged": sum(1 for row in rows if row.status == "unchanged"),
        "conflicts": sum(1 for row in rows if row.status == "conflict"),
        "invalid": sum(1 for row in rows if row.status in {"invalid", "exists"}),
    }
    return {"warning": warning, "counts": counts, "rows": [row_payload(row) for row in rows]}


@app.post("/api/rename")
def rename(req: RenameRequest):
    items, warning = items_from_scan(req)
    items = apply_selection(items, req.selected)
    rules = Rules.from_dict(req.rules)
    rows = build_preview(items, rules, req.sort_column, req.sort_desc)
    if not req.confirm:
        raise HTTPException(400, "Rename requires confirm=true after preview.")
    result = commit_preview(rows, rules)
    return {**result, "warning": warning, "rows": [row_payload(row) for row in rows]}


@app.post("/api/undo")
def undo():
    return undo_last()


@app.get("/api/undo")
def undo_info():
    batches = load_undo()
    latest = batches[-1] if batches else None
    return {
        "count": len(batches),
        "latest": {"id": latest.id, "created_at": latest.created_at, "ops": len(latest.ops)} if latest else None,
    }


@app.get("/api/log")
def read_log():
    path = data_dir() / "hulkrenaymer.log"
    if not path.exists():
        return {"text": ""}
    lines = path.read_text(encoding="utf-8").splitlines()
    return {"text": "\n".join(lines[-400:])}


@app.get("/api/favorites")
def favorites():
    return {"favorites": load_favorites()}


@app.post("/api/favorites")
def add_favorite(req: FavoriteRequest):
    if not req.name.strip():
        raise HTTPException(400, "Favourite name required")
    return {"favorites": save_favorite(req.name.strip(), req.rules)}


@app.delete("/api/favorites/{name}")
def remove_favorite(name: str):
    return {"favorites": delete_favorite(name)}


@app.post("/api/mapping")
def mapping(req: MappingRequest):
    parsed = parse_mapping(req.text)
    return {"count": len(parsed), "mapping": parsed}


@app.post("/api/seed")
def seed():
    path = seed_demo(Path.cwd() / "demo_files")
    append_log(f"SEED  {path}")
    return {"path": str(path)}
