from __future__ import annotations

import json
import os
import shutil
import tempfile
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path

from hulkrenaymer.engine import build_preview
from hulkrenaymer.models import FileItem, PreviewRow, Rules


@dataclass
class UndoOp:
    operation: str
    source: str
    dest: str
    is_dir: bool


@dataclass
class UndoBatch:
    id: str
    created_at: str
    ops: list[UndoOp]


def data_dir() -> Path:
    path = Path(os.environ.get("HULKRENAYMER_DATA", Path.home() / ".hulkrenaymer"))
    path.mkdir(parents=True, exist_ok=True)
    return path


def _undo_path() -> Path:
    return data_dir() / "undo.json"


def load_undo() -> list[UndoBatch]:
    path = _undo_path()
    if not path.exists():
        return []
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return []
    batches = []
    for item in raw:
        ops = [UndoOp(**op) for op in item.get("ops", [])]
        batches.append(UndoBatch(id=item["id"], created_at=item["created_at"], ops=ops))
    return batches


def save_undo(batches: list[UndoBatch]) -> None:
    payload = [
        {"id": batch.id, "created_at": batch.created_at, "ops": [asdict(op) for op in batch.ops]}
        for batch in batches[-20:]
    ]
    _undo_path().write_text(json.dumps(payload, indent=2), encoding="utf-8")


def append_log(message: str) -> None:
    log_path = data_dir() / "hulkrenaymer.log"
    stamp = datetime.now().isoformat(sep=" ", timespec="seconds")
    with log_path.open("a", encoding="utf-8") as handle:
        handle.write(f"{stamp}  {message}\n")


def _unique_temp(parent: Path, original: Path) -> Path:
    fd, name = tempfile.mkstemp(prefix=".hulkrenaymer-", suffix=original.suffix, dir=parent)
    os.close(fd)
    path = Path(name)
    path.unlink(missing_ok=True)
    return path


def apply_attributes(path: Path, rules: Rules) -> None:
    if rules.set_readonly:
        mode = path.stat().st_mode
        if rules.readonly_value:
            os.chmod(path, mode & ~0o222)
        else:
            os.chmod(path, mode | 0o222)
    if rules.set_timestamps:
        now = datetime.now().timestamp()

        def parse(value: str, fallback: float) -> float:
            if not value:
                return fallback
            try:
                return datetime.fromisoformat(value).timestamp()
            except ValueError:
                return fallback

        info = path.stat()
        atime = parse(rules.ts_accessed, info.st_atime)
        mtime = parse(rules.ts_modified, info.st_mtime)
        os.utime(path, (atime if atime else now, mtime if mtime else now))


def commit_preview(rows: list[PreviewRow], rules: Rules) -> dict:
    actionable = [row for row in rows if row.selected and row.status in {"ok"} and row.changed]
    if not actionable:
        return {"renamed": 0, "failed": [], "message": "Nothing to rename."}

    conflicts = [row for row in rows if row.status in {"conflict", "invalid", "exists"}]
    if conflicts:
        return {
            "renamed": 0,
            "failed": [{"path": row.path, "error": row.warning or row.status} for row in conflicts],
            "message": "Fix conflicts before renaming.",
        }

    if rules.operation in {"copy", "move"} and rules.dest_dir:
        dest = Path(rules.dest_dir)
        if dest.is_absolute():
            dest.mkdir(parents=True, exist_ok=True)

    # Two-phase rename to handle swaps.
    temps: list[tuple[PreviewRow, Path]] = []
    failed: list[dict] = []
    undo_ops: list[UndoOp] = []

    if rules.operation == "copy":
        for row in actionable:
            source = Path(row.path)
            dest = Path(row.new_path)
            try:
                dest.parent.mkdir(parents=True, exist_ok=True)
                if source.is_dir():
                    shutil.copytree(source, dest)
                else:
                    shutil.copy2(source, dest)
                apply_attributes(dest, rules)
                undo_ops.append(UndoOp("copy", str(source), str(dest), source.is_dir()))
                append_log(f"COPY  {source} -> {dest}")
            except OSError as exc:
                failed.append({"path": row.path, "error": str(exc)})
        batch = UndoBatch(id=datetime.now().strftime("%Y%m%d-%H%M%S"), created_at=datetime.now().isoformat(), ops=undo_ops)
        batches = load_undo()
        batches.append(batch)
        save_undo(batches)
        return {"renamed": len(undo_ops), "failed": failed, "undo_id": batch.id, "message": f"Copied {len(undo_ops)} item(s)."}

    for row in actionable:
        source = Path(row.path)
        try:
            temp = _unique_temp(source.parent, source)
            source.rename(temp)
            temps.append((row, temp))
        except OSError as exc:
            failed.append({"path": row.path, "error": str(exc)})

    renamed = 0
    for row, temp in temps:
        dest = Path(row.new_path)
        try:
            dest.parent.mkdir(parents=True, exist_ok=True)
            temp.rename(dest)
            apply_attributes(dest, rules)
            undo_ops.append(UndoOp(rules.operation, row.path, str(dest), row.is_dir))
            append_log(f"{rules.operation.upper()}  {row.path} -> {dest}")
            renamed += 1
        except OSError as exc:
            try:
                temp.rename(Path(row.path))
            except OSError:
                pass
            failed.append({"path": row.path, "error": str(exc)})

    batch = UndoBatch(id=datetime.now().strftime("%Y%m%d-%H%M%S"), created_at=datetime.now().isoformat(), ops=undo_ops)
    if undo_ops:
        batches = load_undo()
        batches.append(batch)
        save_undo(batches)
    return {
        "renamed": renamed,
        "failed": failed,
        "undo_id": batch.id if undo_ops else None,
        "message": f"Renamed {renamed} item(s)." if rules.operation == "rename" else f"Moved {renamed} item(s).",
    }


def undo_last() -> dict:
    batches = load_undo()
    if not batches:
        return {"undone": 0, "message": "Nothing to undo."}
    batch = batches.pop()
    undone = 0
    failed = []
    for op in reversed(batch.ops):
        source = Path(op.dest)
        dest = Path(op.source)
        try:
            if op.operation == "copy":
                if source.is_dir():
                    shutil.rmtree(source)
                elif source.exists():
                    source.unlink()
            else:
                dest.parent.mkdir(parents=True, exist_ok=True)
                source.rename(dest)
            undone += 1
            append_log(f"UNDO  {source} -> {dest}")
        except OSError as exc:
            failed.append({"path": str(source), "error": str(exc)})
    save_undo(batches)
    return {"undone": undone, "failed": failed, "message": f"Undid {undone} item(s)."}


def rename_items(items: list[FileItem], rules: Rules, sort_column: str = "name", sort_desc: bool = False) -> dict:
    rows = build_preview(items, rules, sort_column, sort_desc)
    return {**commit_preview(rows, rules), "preview": [row.__dict__ for row in rows]}
