from __future__ import annotations

from dataclasses import dataclass, field, fields
from datetime import datetime
from pathlib import Path
from typing import Any, Literal


ApplyTo = Literal["name", "ext", "both", "full"]
CaseMode = Literal["same", "lower", "upper", "title", "sentence", "toggle"]
NameMode = Literal["keep", "fixed", "remove"]
ExtMode = Literal["keep", "fixed", "remove", "lower", "upper"]
Position = Literal["prefix", "suffix", "insert", "replace"]
Operation = Literal["rename", "copy", "move"]
DateSource = Literal["created", "modified", "accessed", "now", "exif"]


@dataclass
class FileItem:
    path: str
    name: str
    stem: str
    ext: str
    folder: str
    parent: str
    is_dir: bool
    size: int
    created: datetime | None
    modified: datetime | None
    accessed: datetime | None
    depth: int = 0
    selected: bool = True
    exif: dict[str, Any] = field(default_factory=dict)
    id3: dict[str, Any] = field(default_factory=dict)
    props: dict[str, Any] = field(default_factory=dict)

    @property
    def path_obj(self) -> Path:
        return Path(self.path)


@dataclass
class Rules:
    regex_enabled: bool = False
    regex_pattern: str = ""
    regex_replace: str = ""
    regex_apply_to: ApplyTo = "name"

    name_enabled: bool = False
    name_mode: NameMode = "keep"
    name_fixed: str = ""
    ext_mode: ExtMode = "keep"
    ext_fixed: str = ""

    replace_enabled: bool = False
    find: str = ""
    replace_with: str = ""
    replace_all: bool = True
    replace_case_sensitive: bool = True
    replace_apply_to: ApplyTo = "name"

    case_mode: CaseMode = "same"
    case_apply_to: ApplyTo = "name"

    remove_enabled: bool = False
    remove_first_n: int = 0
    remove_last_n: int = 0
    remove_from: int = 0
    remove_to: int = 0
    remove_chars: str = ""
    remove_words: str = ""
    remove_first_words: int = 0
    remove_last_words: int = 0
    remove_digits: bool = False
    remove_symbols: bool = False
    remove_letters: bool = False
    remove_trim: bool = False
    collapse_spaces: bool = False

    move_enabled: bool = False
    move_from: int = 0
    move_count: int = 0
    move_to: int = 0
    move_copy: bool = False

    add_enabled: bool = False
    prefix: str = ""
    suffix: str = ""
    insert: str = ""
    insert_at: int = 0

    folder_enabled: bool = False
    folder_mode: Position = "prefix"
    folder_separator: str = "_"

    numbering_enabled: bool = False
    number_start: int = 1
    number_increment: int = 1
    number_pad: int = 3
    number_separator: str = "_"
    number_position: Position = "suffix"
    number_insert_at: int = 0
    number_reset_per_folder: bool = False

    date_enabled: bool = False
    date_source: DateSource = "modified"
    date_format: str = "%Y-%m-%d"
    date_position: Position = "prefix"
    date_separator: str = "_"

    js_enabled: bool = False
    js_code: str = ""

    mapping: dict[str, str] = field(default_factory=dict)
    mapping_exclusive: bool = True

    windows_safe: bool = True
    operation: Operation = "rename"
    dest_dir: str = ""

    set_timestamps: bool = False
    ts_created: str = ""
    ts_modified: str = ""
    ts_accessed: str = ""

    set_readonly: bool = False
    readonly_value: bool = False
    set_hidden: bool = False
    hidden_value: bool = False

    @classmethod
    def from_dict(cls, data: dict[str, Any] | None) -> "Rules":
        if not data:
            return cls()
        allowed = {f.name for f in fields(cls)}
        kwargs: dict[str, Any] = {}
        for key, value in data.items():
            if key in allowed:
                kwargs[key] = value
        return cls(**kwargs)

    def any_active(self) -> bool:
        return any(
            [
                self.regex_enabled and self.regex_pattern,
                self.name_enabled,
                self.replace_enabled and self.find,
                self.case_mode != "same",
                self.remove_enabled,
                self.move_enabled,
                self.add_enabled,
                self.folder_enabled,
                self.numbering_enabled,
                self.date_enabled,
                self.js_enabled and self.js_code.strip(),
                bool(self.mapping),
            ]
        )


@dataclass
class PreviewRow:
    path: str
    old_name: str
    new_name: str
    new_path: str
    is_dir: bool
    size: int
    created: str | None
    modified: str | None
    accessed: str | None
    selected: bool
    changed: bool
    status: str
    warning: str = ""
    folder: str = ""
    ext: str = ""
    exif: dict[str, Any] = field(default_factory=dict)
    id3: dict[str, Any] = field(default_factory=dict)
