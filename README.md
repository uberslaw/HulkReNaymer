# HulkReNaymer

A **Windows WPF** bulk rename app. Same Hulk gamma-green look, now as a real .NET desktop program: browse a folder, stack rules, preview old vs new names, then smash rename.

It is an original implementation inspired by the Bulk Rename Utility workflow, not affiliated with TGRMN Software.

## Run on Windows

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then:

```powershell
dotnet restore
dotnet test
dotnet run --project src/HulkReNaymer.App
```

Or publish a folder you can copy around:

```powershell
dotnet publish src/HulkReNaymer.App -c Release -r win-x64 --self-contained false
```

`HulkReNaymer.exe` lands under `src/HulkReNaymer.App/bin/Release/net8.0-windows/win-x64/publish/`.

**Load demo files** writes sample project docs, export CSVs, EXIF JPEGs, and an MP3 into `Documents\HulkReNaymer-Demo`.

## Workflow

1. Open a folder (Places, folder list, or Browse).
2. Select the files to change.
3. Turn on one or more numbered rule panels, or pick a preset.
4. Check the **New name** column. Conflicts light up red.
5. Click **Smash Rename** only when the preview is right.
6. **Undo** reverses the last batch.

Rules reset after a successful rename so the same prefix is not applied twice.

## Rules (same order as the panels)

1. Name list (optional exclusive mapping)
2. Regular expression (`$1` groups work)
3. Name / extension
4. Find / replace
5. Case
6. Remove
7. Move / copy characters
8. Add prefix, insert, suffix
9. Parent folder
10. Date (created / modified / accessed / now / photo EXIF)
11. Numbering
12. JavaScript (`newName = name + '_' + index`)
13. Filters
14. Copy / move to another folder
15. Timestamps
16. Read-only / hidden / Windows-safe names

Tokens in prefix, suffix, insert, and fixed names:

`{name}` `{ext}` `{folder}` `{n}` `{n:3}` `{date}` `{date:exif}` `{yyyy}` `{mm}` `{dd}` `{exif.date}` `{id3.artist}` `{id3.album}` `{id3.title}` `{size}`

## Solution layout

- `src/HulkReNaymer.Core` — rename engine, scan, EXIF/ID3, undo (net8.0, tested on any OS)
- `src/HulkReNaymer.App` — WPF UI (`net8.0-windows`)
- `tests/HulkReNaymer.Tests` — engine and rename/undo tests
- `docs/Bulk_Rename_Utility_Guide.md` — the capability guide this app implements

## Licence

MIT.
