from __future__ import annotations

import re
import string
from collections import defaultdict
from datetime import datetime
from pathlib import Path

from hulkrenaymer.js_sandbox import run_javascript
from hulkrenaymer.models import FileItem, PreviewRow, Rules

WINDOWS_ILLEGAL = re.compile(r'[<>:"/\\|?*]')
WINDOWS_RESERVED = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *(f"COM{i}" for i in range(1, 10)),
    *(f"LPT{i}" for i in range(1, 10)),
}
WORD_SPLIT = re.compile(r"(\s+)")
TITLE_SPLIT = re.compile(r"([_\-\s]+)")
DIGIT_RE = re.compile(r"\d")
SYMBOL_RE = re.compile(rf"[{re.escape(string.punctuation)}]")
LETTER_RE = re.compile(r"[^\W\d_]", re.UNICODE)
DOLLAR_GROUP = re.compile(r"\$(\d+)")


def natural_key(value: str) -> list:
    return [int(part) if part.isdigit() else part.casefold() for part in re.split(r"(\d+)", value)]


def split_name(filename: str) -> tuple[str, str]:
    if filename in {".", ".."}:
        return filename, ""
    path = Path(filename)
    if filename.startswith(".") and path.suffix == "":
        return filename, ""
    return path.stem, path.suffix


def join_name(stem: str, ext: str) -> str:
    if not ext:
        return stem
    if not ext.startswith("."):
        ext = f".{ext}"
    return f"{stem}{ext}"


def _apply_to_parts(stem: str, ext: str, apply_to: str, transform) -> tuple[str, str]:
    ext_body = ext[1:] if ext.startswith(".") else ext
    prefix = "." if ext.startswith(".") and ext_body else ""
    if apply_to == "name":
        return transform(stem), ext
    if apply_to == "ext":
        return stem, f"{prefix}{transform(ext_body)}" if ext else ext
    if apply_to == "both":
        new_ext = f"{prefix}{transform(ext_body)}" if ext else ext
        return transform(stem), new_ext
    full = transform(join_name(stem, ext))
    return split_name(full)


def title_case(value: str) -> str:
    parts = TITLE_SPLIT.split(value)
    out = []
    for part in parts:
        if not part or TITLE_SPLIT.fullmatch(part):
            out.append(part)
        else:
            out.append(part[:1].upper() + part[1:].lower() if part else part)
    return "".join(out)


def sentence_case(value: str) -> str:
    lowered = value.lower()
    for index, char in enumerate(lowered):
        if char.isalpha():
            return lowered[:index] + char.upper() + lowered[index + 1 :]
    return lowered


def toggle_case(value: str) -> str:
    return value.swapcase()


def apply_case(value: str, mode: str) -> str:
    if mode == "lower":
        return value.lower()
    if mode == "upper":
        return value.upper()
    if mode == "title":
        return title_case(value)
    if mode == "sentence":
        return sentence_case(value)
    if mode == "toggle":
        return toggle_case(value)
    return value


def convert_regex_replace(value: str) -> str:
    return DOLLAR_GROUP.sub(r"\\g<\1>", value)


def regex_sub(pattern: str, repl: str, value: str) -> str:
    if not pattern:
        return value
    compiled = re.compile(pattern)
    return compiled.sub(convert_regex_replace(repl), value)


def find_replace(value: str, find: str, repl: str, replace_all: bool, case_sensitive: bool) -> str:
    if not find:
        return value
    if case_sensitive:
        if replace_all:
            return value.replace(find, repl)
        return value.replace(find, repl, 1)
    flags = re.IGNORECASE
    compiled = re.compile(re.escape(find), flags)
    count = 0 if replace_all else 1
    return compiled.sub(lambda _: repl, value, count=count)


def remove_range(value: str, first_n: int, last_n: int, from_pos: int, to_pos: int) -> str:
    chars = list(value)
    if first_n > 0:
        chars = chars[first_n:]
    if last_n > 0:
        chars = chars[:-last_n] if last_n < len(chars) else []
    if from_pos > 0:
        start = from_pos - 1
        end = to_pos if to_pos > 0 else start + 1
        chars = chars[:start] + chars[end:]
    return "".join(chars)


def remove_words_by_count(value: str, first: int, last: int) -> str:
    bits = [part for part in WORD_SPLIT.split(value) if part != ""]
    words = [part for part in bits if not part.isspace()]
    if first:
        words = words[first:]
    if last:
        words = words[:-last] if last < len(words) else []
    # Reconstruct using single spaces between remaining words.
    return " ".join(words)


def apply_remove(value: str, rules: Rules) -> str:
    result = remove_range(
        value,
        rules.remove_first_n,
        rules.remove_last_n,
        rules.remove_from,
        rules.remove_to,
    )
    if rules.remove_chars:
        table = str.maketrans("", "", rules.remove_chars)
        result = result.translate(table)
    if rules.remove_words:
        for word in [item.strip() for item in rules.remove_words.split(",") if item.strip()]:
            result = re.sub(re.escape(word), "", result, flags=re.IGNORECASE)
    if rules.remove_first_words or rules.remove_last_words:
        result = remove_words_by_count(result, rules.remove_first_words, rules.remove_last_words)
    if rules.remove_digits:
        result = DIGIT_RE.sub("", result)
    if rules.remove_symbols:
        result = SYMBOL_RE.sub("", result)
    if rules.remove_letters:
        result = LETTER_RE.sub("", result)
    if rules.collapse_spaces:
        result = re.sub(r"\s+", " ", result)
    if rules.remove_trim:
        result = result.strip()
    return result


def apply_move(value: str, from_pos: int, count: int, to_pos: int, copy: bool) -> str:
    if from_pos <= 0 or count <= 0:
        return value
    start = from_pos - 1
    chunk = value[start : start + count]
    if not chunk:
        return value
    remaining = value if copy else value[:start] + value[start + count :]
    insert_at = max(to_pos - 1, 0)
    insert_at = min(insert_at, len(remaining))
    return remaining[:insert_at] + chunk + remaining[insert_at:]


def insert_at(value: str, text: str, position: int) -> str:
    if not text:
        return value
    if position <= 0:
        return value + text
    index = min(max(position - 1, 0), len(value))
    return value[:index] + text + value[index:]


def apply_position(value: str, extra: str, position: str, separator: str, insert_pos: int = 0) -> str:
    if not extra:
        return value
    if position == "prefix":
        return f"{extra}{separator}{value}" if separator else f"{extra}{value}"
    if position == "suffix":
        return f"{value}{separator}{extra}" if separator else f"{value}{extra}"
    if position == "replace":
        return extra
    if position == "insert":
        glued = f"{separator}{extra}" if separator and insert_pos > 1 else extra
        return insert_at(value, glued, insert_pos)
    return value


def windows_safe_name(name: str) -> str:
    stem, ext = split_name(name)
    stem = WINDOWS_ILLEGAL.sub("_", stem).rstrip(" .")
    ext_body = WINDOWS_ILLEGAL.sub("_", ext.lstrip("."))
    if not stem:
        stem = "_"
    if stem.upper() in WINDOWS_RESERVED:
        stem = f"_{stem}"
    return join_name(stem, f".{ext_body}" if ext_body else "")


def format_dt(value: datetime | None, fmt: str) -> str:
    if value is None:
        return ""
    try:
        return value.strftime(fmt)
    except ValueError:
        return value.strftime("%Y-%m-%d")


def item_date(item: FileItem, source: str) -> datetime | None:
    if source == "created":
        return item.created
    if source == "accessed":
        return item.accessed
    if source == "now":
        return datetime.now()
    if source == "exif":
        raw = item.exif.get("date") or item.exif.get("DateTimeOriginal")
        if raw:
            for fmt in ("%Y:%m:%d %H:%M:%S", "%Y-%m-%d %H:%M:%S", "%Y-%m-%d"):
                try:
                    return datetime.strptime(str(raw), fmt)
                except ValueError:
                    continue
        return item.modified
    return item.modified


def expand_tokens(template: str, item: FileItem, rules: Rules, sequence: int) -> str:
    if not template or "{" not in template:
        return template

    chosen = item_date(item, rules.date_source)
    pad = max(int(rules.number_pad), 1)
    values = {
        "name": item.stem,
        "ext": item.ext.lstrip("."),
        "folder": item.folder,
        "parent": item.folder,
        "n": str(sequence).zfill(pad),
        "N": str(sequence),
        "size": str(item.size),
        "date": format_dt(chosen, rules.date_format),
        "yyyy": format_dt(chosen, "%Y"),
        "mm": format_dt(chosen, "%m"),
        "dd": format_dt(chosen, "%d"),
        "hh": format_dt(chosen, "%H"),
        "nn": format_dt(chosen, "%M"),
        "ss": format_dt(chosen, "%S"),
        "exif.date": str(item.exif.get("date") or ""),
        "exif.width": str(item.exif.get("width") or ""),
        "exif.height": str(item.exif.get("height") or ""),
        "id3.artist": str(item.id3.get("artist") or ""),
        "id3.album": str(item.id3.get("album") or ""),
        "id3.title": str(item.id3.get("title") or ""),
    }

    def replacer(match: re.Match) -> str:
        key = match.group(1)
        if key.startswith("n:"):
            try:
                width = int(key.split(":", 1)[1])
            except ValueError:
                width = pad
            return str(sequence).zfill(max(width, 1))
        if key.startswith("date:"):
            source = key.split(":", 1)[1]
            return format_dt(item_date(item, source), rules.date_format)
        return values.get(key, match.group(0))

    return re.sub(r"\{([^{}]+)\}", replacer, template)


def apply_rules_to_item(item: FileItem, rules: Rules, sequence: int) -> tuple[str, str]:
    """Return (new_filename, warning)."""
    if rules.mapping:
        mapped = rules.mapping.get(item.name) or rules.mapping.get(item.path)
        if mapped:
            if rules.mapping_exclusive:
                return (windows_safe_name(mapped) if rules.windows_safe else mapped), ""
            item = FileItem(**{**item.__dict__, "name": mapped, **dict(zip(("stem", "ext"), split_name(mapped)))})

    stem, ext = item.stem, item.ext
    warning = ""

    if rules.regex_enabled and rules.regex_pattern:
        try:
            stem, ext = _apply_to_parts(
                stem,
                ext,
                rules.regex_apply_to,
                lambda value: regex_sub(rules.regex_pattern, rules.regex_replace, value),
            )
        except re.error as exc:
            warning = f"Invalid regex: {exc}"

    if rules.name_enabled:
        if rules.name_mode == "fixed":
            stem = expand_tokens(rules.name_fixed, item, rules, sequence)
        elif rules.name_mode == "remove":
            stem = ""
        if rules.ext_mode == "fixed":
            fixed = expand_tokens(rules.ext_fixed, item, rules, sequence).lstrip(".")
            ext = f".{fixed}" if fixed else ""
        elif rules.ext_mode == "remove":
            ext = ""
        elif rules.ext_mode == "lower":
            ext = ext.lower()
        elif rules.ext_mode == "upper":
            ext = ext.upper()

    if rules.replace_enabled and rules.find:
        stem, ext = _apply_to_parts(
            stem,
            ext,
            rules.replace_apply_to,
            lambda value: find_replace(
                value,
                rules.find,
                rules.replace_with,
                rules.replace_all,
                rules.replace_case_sensitive,
            ),
        )

    if rules.case_mode != "same":
        stem, ext = _apply_to_parts(stem, ext, rules.case_apply_to, lambda value: apply_case(value, rules.case_mode))

    if rules.remove_enabled:
        stem = apply_remove(stem, rules)

    if rules.move_enabled:
        stem = apply_move(stem, rules.move_from, rules.move_count, rules.move_to, rules.move_copy)

    if rules.add_enabled:
        prefix = expand_tokens(rules.prefix, item, rules, sequence)
        suffix = expand_tokens(rules.suffix, item, rules, sequence)
        inserted = expand_tokens(rules.insert, item, rules, sequence)
        if prefix:
            stem = f"{prefix}{stem}"
        if rules.insert_at > 0 and inserted:
            stem = insert_at(stem, inserted, rules.insert_at)
        if suffix:
            stem = f"{stem}{suffix}"

    if rules.folder_enabled and item.folder:
        stem = apply_position(stem, item.folder, rules.folder_mode, rules.folder_separator)

    if rules.date_enabled:
        stamp = format_dt(item_date(item, rules.date_source), rules.date_format)
        stem = apply_position(stem, stamp, rules.date_position, rules.date_separator)

    if rules.numbering_enabled:
        number = str(sequence).zfill(max(int(rules.number_pad), 1))
        stem = apply_position(
            stem,
            number,
            rules.number_position,
            rules.number_separator,
            rules.number_insert_at,
        )

    new_name = join_name(stem, ext)

    if rules.js_enabled and rules.js_code.strip():
        current = new_name
        js_stem, js_ext = split_name(current)
        result, js_error = run_javascript(
            rules.js_code,
            {
                "name": js_stem,
                "ext": js_ext.lstrip("."),
                "newName": current,
                "index": sequence,
                "folder": item.folder,
                "size": item.size,
                "path": item.path,
                "isDir": item.is_dir,
                "created": item.created.isoformat() if item.created else None,
                "modified": item.modified.isoformat() if item.modified else None,
                "accessed": item.accessed.isoformat() if item.accessed else None,
                "exif": item.exif,
                "id3": item.id3,
                "props": item.props,
            },
        )
        if js_error:
            warning = js_error
        elif result:
            new_name = result

    new_name = new_name.replace("\\", "_").replace("/", "_")
    if not new_name.strip() or new_name in {".", ".."}:
        return new_name, warning or "New name is empty"
    if rules.windows_safe:
        new_name = windows_safe_name(new_name)
    return new_name, warning


def sort_items(items: list[FileItem], column: str, descending: bool) -> list[FileItem]:
    key_map = {
        "name": lambda item: natural_key(item.name),
        "size": lambda item: item.size,
        "modified": lambda item: item.modified or datetime.min,
        "created": lambda item: item.created or datetime.min,
        "path": lambda item: natural_key(item.path),
        "ext": lambda item: item.ext.lower(),
        "folder": lambda item: natural_key(item.folder),
        "type": lambda item: (not item.is_dir, natural_key(item.name)),
    }
    key = key_map.get(column, key_map["name"])
    return sorted(items, key=key, reverse=descending)


def assign_sequences(items: list[FileItem], rules: Rules) -> dict[str, int]:
    sequences: dict[str, int] = {}
    if rules.number_reset_per_folder:
        counters: dict[str, int] = defaultdict(lambda: rules.number_start)
        for item in items:
            if not item.selected:
                continue
            sequences[item.path] = counters[item.parent]
            counters[item.parent] += rules.number_increment
        return sequences
    current = rules.number_start
    for item in items:
        if not item.selected:
            continue
        sequences[item.path] = current
        current += rules.number_increment
    return sequences


def destination_path(item: FileItem, new_name: str, rules: Rules) -> str:
    if rules.operation == "rename" or not rules.dest_dir:
        return str(Path(item.parent) / new_name)
    dest_root = Path(rules.dest_dir)
    if not dest_root.is_absolute():
        dest_root = Path(item.parent) / dest_root
    return str(dest_root / new_name)


def iso(value: datetime | None) -> str | None:
    return value.isoformat(sep=" ", timespec="seconds") if value else None


def build_preview(
    items: list[FileItem],
    rules: Rules,
    sort_column: str = "name",
    sort_desc: bool = False,
) -> list[PreviewRow]:
    ordered = sort_items(items, sort_column, sort_desc)
    sequences = assign_sequences(ordered, rules)
    rows: list[PreviewRow] = []
    proposed: dict[str, list[str]] = defaultdict(list)

    for item in ordered:
        if not item.selected:
            rows.append(
                PreviewRow(
                    path=item.path,
                    old_name=item.name,
                    new_name=item.name,
                    new_path=item.path,
                    is_dir=item.is_dir,
                    size=item.size,
                    created=iso(item.created),
                    modified=iso(item.modified),
                    accessed=iso(item.accessed),
                    selected=False,
                    changed=False,
                    status="skipped",
                    folder=item.folder,
                    ext=item.ext,
                    exif=item.exif,
                    id3=item.id3,
                )
            )
            continue

        new_name, warning = apply_rules_to_item(item, rules, sequences.get(item.path, rules.number_start))
        new_path = destination_path(item, new_name, rules)
        status = "ok"
        if not new_name or new_name in {".", ".."}:
            status = "invalid"
            warning = warning or "New name is empty"
        elif new_name == item.name and rules.operation == "rename":
            status = "unchanged"
        rows.append(
            PreviewRow(
                path=item.path,
                old_name=item.name,
                new_name=new_name,
                new_path=new_path,
                is_dir=item.is_dir,
                size=item.size,
                created=iso(item.created),
                modified=iso(item.modified),
                accessed=iso(item.accessed),
                selected=True,
                changed=new_name != item.name or rules.operation != "rename",
                status=status,
                warning=warning,
                folder=item.folder,
                ext=item.ext,
                exif=item.exif,
                id3=item.id3,
            )
        )
        if status in {"ok", "unchanged"}:
            proposed[str(Path(new_path).resolve()) if Path(new_path).parent.exists() else new_path].append(item.path)

    conflict_paths = {key for key, sources in proposed.items() if len(sources) > 1}
    for row in rows:
        marker = str(Path(row.new_path).resolve()) if Path(row.new_path).parent.exists() else row.new_path
        if row.selected and row.status != "invalid" and marker in conflict_paths:
            row.status = "conflict"
            row.warning = "Duplicate new name"
        elif row.selected and row.status == "ok":
            target = Path(row.new_path)
            if target.exists() and str(target.resolve()) != str(Path(row.path).resolve()):
                if not any(other.path == str(target) for other in ordered if other.selected):
                    row.status = "exists"
                    row.warning = "Target already exists"
    return rows
