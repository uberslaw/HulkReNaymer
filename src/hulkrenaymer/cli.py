from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import uvicorn

from hulkrenaymer.apply import commit_preview, undo_last
from hulkrenaymer.engine import build_preview
from hulkrenaymer.favorites import parse_mapping
from hulkrenaymer.models import Rules
from hulkrenaymer.scan import scan_directory
from hulkrenaymer.seed import seed_demo


def _print_table(rows) -> None:
    width_old = max([len(row.old_name) for row in rows] + [8])
    width_new = max([len(row.new_name) for row in rows] + [8])
    print(f"{'STATUS':<10} {'OLD NAME'.ljust(width_old)}  {'NEW NAME'.ljust(width_new)}")
    print("-" * (width_old + width_new + 14))
    for row in rows:
        flag = "*" if row.changed else " "
        print(f"{row.status:<10} {row.old_name.ljust(width_old)}  {row.new_name.ljust(width_new)} {flag}")


def _rules_from_args(args: argparse.Namespace) -> Rules:
    mapping = {}
    if getattr(args, "from_list", None):
        mapping = parse_mapping(Path(args.from_list).read_text(encoding="utf-8"))
    return Rules(
        prefix=args.prefix or "",
        suffix=args.suffix or "",
        insert=args.insert or "",
        insert_at=args.insert_at or 0,
        add_enabled=bool(args.prefix or args.suffix or args.insert),
        find=args.find or "",
        replace_with=args.replace or "",
        replace_enabled=bool(args.find),
        case_mode=args.case,
        regex_enabled=bool(args.regex),
        regex_pattern=args.regex or "",
        regex_replace=args.regex_replace or "",
        numbering_enabled=args.number,
        number_start=args.start,
        number_pad=args.pad,
        number_separator=args.sep,
        date_enabled=args.date,
        date_source=args.date_source,
        date_format=args.date_format,
        folder_enabled=args.folder_name,
        mapping=mapping,
        windows_safe=not args.allow_unsafe,
        js_enabled=bool(args.js),
        js_code=args.js or "",
        name_enabled=bool(args.ext),
        ext_mode="fixed" if args.ext else "keep",
        ext_fixed=args.ext or "",
    )


def cmd_serve(args: argparse.Namespace) -> int:
    os.environ.setdefault("HULKRENAYMER_DATA", str(Path.cwd() / "data"))
    if args.seed:
        seed_demo(Path.cwd() / "demo_files")
    uvicorn.run(
        "hulkrenaymer.server:app",
        host=args.host,
        port=args.port,
        reload=args.reload,
    )
    return 0


def cmd_preview(args: argparse.Namespace) -> int:
    items, warning = scan_directory(args.path, recurse=args.recurse, include_folders=args.folders, wildcard=args.wildcard)
    if warning:
        print(warning, file=sys.stderr)
    rows = build_preview(items, _rules_from_args(args), args.sort, False)
    _print_table(rows)
    changed = sum(1 for row in rows if row.changed and row.status == "ok")
    print(f"\n{changed} file(s) would change.")
    return 0


def cmd_rename(args: argparse.Namespace) -> int:
    items, warning = scan_directory(args.path, recurse=args.recurse, include_folders=args.folders, wildcard=args.wildcard)
    if warning:
        print(warning, file=sys.stderr)
    rules = _rules_from_args(args)
    rows = build_preview(items, rules, args.sort, False)
    if args.dry_run:
        _print_table(rows)
        return 0
    result = commit_preview(rows, rules)
    print(result["message"])
    for fail in result.get("failed") or []:
        print(f"FAILED {fail['path']}: {fail['error']}", file=sys.stderr)
    return 1 if result.get("failed") else 0


def cmd_undo(_: argparse.Namespace) -> int:
    result = undo_last()
    print(result["message"])
    return 0


def cmd_seed(args: argparse.Namespace) -> int:
    path = seed_demo(args.path)
    print(f"Demo files written to {path}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="hulkrenaymer",
        description="HulkReNaymer — bulk rename files and folders with preview.",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    serve = sub.add_parser("serve", help="Start the local web app")
    serve.add_argument("--host", default="127.0.0.1")
    serve.add_argument("--port", type=int, default=8765)
    serve.add_argument("--reload", action="store_true")
    serve.add_argument("--seed", action="store_true", help="Create demo_files before serving")
    serve.set_defaults(func=cmd_serve)

    def add_rule_flags(cmd):
        cmd.add_argument("path", nargs="?", default=".", help="Folder to process")
        cmd.add_argument("--recurse", action="store_true")
        cmd.add_argument("--folders", action="store_true")
        cmd.add_argument("--wildcard", default="*")
        cmd.add_argument("--prefix", default="")
        cmd.add_argument("--suffix", default="")
        cmd.add_argument("--insert", default="")
        cmd.add_argument("--insert-at", type=int, default=0)
        cmd.add_argument("--find", default="")
        cmd.add_argument("--replace", default="")
        cmd.add_argument("--case", default="same", choices=["same", "lower", "upper", "title", "sentence", "toggle"])
        cmd.add_argument("--regex", default="")
        cmd.add_argument("--regex-replace", default="")
        cmd.add_argument("--number", action="store_true")
        cmd.add_argument("--start", type=int, default=1)
        cmd.add_argument("--pad", type=int, default=3)
        cmd.add_argument("--sep", default="_")
        cmd.add_argument("--date", action="store_true")
        cmd.add_argument("--date-source", default="modified")
        cmd.add_argument("--date-format", default="%Y-%m-%d")
        cmd.add_argument("--folder-name", action="store_true")
        cmd.add_argument("--from-list", dest="from_list")
        cmd.add_argument("--js", default="")
        cmd.add_argument("--ext", default="")
        cmd.add_argument("--sort", default="name")
        cmd.add_argument("--allow-unsafe", action="store_true")

    preview = sub.add_parser("preview", help="Print a rename preview")
    add_rule_flags(preview)
    preview.set_defaults(func=cmd_preview)

    rename = sub.add_parser("rename", help="Apply renaming rules")
    add_rule_flags(rename)
    rename.add_argument("--dry-run", action="store_true")
    rename.set_defaults(func=cmd_rename)

    undo = sub.add_parser("undo", help="Undo the last rename batch")
    undo.set_defaults(func=cmd_undo)

    seed = sub.add_parser("seed", help="Write sample files for trying the app")
    seed.add_argument("path", nargs="?", default=None)
    seed.set_defaults(func=cmd_seed)

    dump = sub.add_parser("rules-json", help="Print an empty rules object")
    dump.set_defaults(func=lambda _: print(json.dumps(Rules().__dict__, indent=2)) or 0)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
