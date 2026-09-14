# HulkReNaymer

Bulk rename files and folders with a live **old name / new name** preview, then commit the change in one shot.

HulkReNaymer is a local web app (plus a CLI) inspired by the Bulk Rename Utility workflow: browse a folder, stack rules, inspect the preview, then rename. It is an original implementation and is not affiliated with TGRMN Software.

## What it can do

- Prefix, suffix, and insert text at a character position
- Find and replace (including blank replace to delete text)
- Remove characters by position, type, word, digit, or symbol
- Move or copy a slice of the filename
- Case conversion: lower, UPPER, Title Case, Sentence case, tOGGLE
- Sequential numbering with start, step, padding, and per-folder reset
- Dates from created / modified / accessed / now / photo EXIF
- Parent folder name in the filename
- Extension change or removal
- Photo EXIF and MP3 ID3 tokens
- Rename from a `old|new` text or CSV list
- Regular expressions (`\1` or `$1` groups)
- Sandboxed JavaScript rules (`newName = ...`)
- Recurse into subfolders, wildcard / regex filters
- Rename in place, or copy / move into another folder
- Optional timestamp and read-only updates
- Favourites, activity log, and multi-file undo

## Install and run

Python 3.11+ is required.

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install -e ".[dev]"
hulkrenaymer serve --seed
```

Then open [http://127.0.0.1:8765](http://127.0.0.1:8765). `--seed` writes a `demo_files/` folder with the worked examples from the usage guide (project documents, export prefixes, photos with EXIF, an MP3 with ID3 tags, and a mapping list).

## Standard workflow

1. Open a folder from Places, the tree, or the path bar.
2. Select the files or folders to change.
3. Turn on one or more numbered rule panels.
4. Check the **New name** column. Conflicts and invalid names are flagged.
5. Click **Smash Rename** only when the preview is right.
6. Use **Undo** if the batch needs to be reversed. Keep the log until you have sampled the result.

Nothing is renamed until you confirm. Typing into a rule only updates the preview.

## CLI companion

```bash
hulkrenaymer preview demo_files/project --case title --find " " --replace _ --prefix PROJECT123_
hulkrenaymer rename  demo_files/exports --find EXPORT_20260914_ --replace ""
hulkrenaymer rename  demo_files/photos --from-list demo_files/asset_register.txt --dry-run
hulkrenaymer undo
```

## Rule order

Rules are applied in this order, matching the numbered panels:

1. Name list (optional exclusive mapping)
2. Regular expression
3. Name / extension
4. Find / replace
5. Case
6. Remove
7. Move / copy characters
8. Add prefix, insert, suffix
9. Parent folder
10. Date
11. Numbering
12. JavaScript

Tokens you can use in prefix, suffix, insert, and fixed names:

`{name}` `{ext}` `{folder}` `{n}` `{n:3}` `{date}` `{date:exif}` `{yyyy}` `{mm}` `{dd}` `{exif.date}` `{exif.width}` `{id3.artist}` `{id3.album}` `{id3.title}` `{size}`

## Tests

```bash
pytest
```

## Licence

MIT. Use it, smash filenames, keep backups.
