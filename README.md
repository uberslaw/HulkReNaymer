# HulkReNaymer

A **Windows WPF** bulk rename app. Same Hulk gamma-green look, now as a real .NET desktop program: browse a folder, stack rules, preview old vs new names, then smash rename.

It is an original implementation inspired by the Bulk Rename Utility workflow, not affiliated with TGRMN Software.

## Install on Windows

You do **not** need Visual Studio or the .NET SDK. Grab a release from [GitHub Releases](https://github.com/uberslaw/HulkReNaymer/releases):

1. **Installer (recommended)** — download `HulkReNaymer-Setup.exe`, run it, then start **HulkReNaymer** from the Start menu. Uninstall it from Settings → Apps.
2. **Portable** — download `HulkReNaymer-portable-win-x64.zip`, unzip it, and double-click `HulkReNaymer\HulkReNaymer.exe`.

Both packages are self-contained for 64-bit Windows 10/11. The .NET runtime is included.

**Load demo files** writes sample project docs, export CSVs, EXIF JPEGs, and an MP3 into `Documents\HulkReNaymer-Demo`.

## Workflow

1. Open files: Places, Browse, drag-and-drop files or folders onto the window, or pass paths on the command line / Explorer **Send to**.
2. Select the files to change.
3. Turn on one or more numbered rule panels, or pick a preset (including **Search and replace**).
4. Check the **New name** column. Conflicts light up red. Choose **On collision**: Stop, Skip, or Append number (`stem_001.ext`).
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

Tokens work in prefix, suffix, insert, fixed names, find/replace, regex replace, and the copy/move destination folder:

`{name}` `{ext}` `{folder}` `{n}` `{n:3}` `{date}` `{date:exif}` `{yyyy}` `{mm}` `{dd}` `{exif.date}` `{id3.artist}` `{id3.album}` `{id3.title}` `{size}`

PowerRename-style aliases also work: `$YYYY` `$YY` `$MM` `$DD` `$mm` (minutes) `${}` `${n:3}` `${padding=3}`. Regex `$1` / `$2` groups are left alone.

## Send To (Explorer)

The installer can add **HulkReNaymer** to the Explorer **Send to** menu. For a portable build, run:

```powershell
./scripts/install-sendto.ps1 -ExePath C:\path\to\HulkReNaymer.exe
```

That creates a shortcut in `%AppData%\Microsoft\Windows\SendTo`. Right-click files → Send to → HulkReNaymer. Use `-Remove` to delete the shortcut.

## Launch Control (Master Launch Control)

HulkReNaymer plugs into [Master Launch Control](https://github.com/uberslaw/master-launch-control) as a **Generic** app. The product LC is a C# host that references `LaunchControl.Standard` from that repo.

The LC is the same `LaunchControl.Standard` chrome as Switcheroo (header, Theme…, status + PID, Start / Stop / Restart, Refresh, Follow logs, extra-action groups, event pane). HulkReNaymer has no Windows service, so Start/Stop drive the desktop exe.

| Button | What it does |
|--------|----------------|
| **Start** | Run Release (`src\HulkReNaymer.App\bin\Release\net8.0-windows\HulkReNaymer.exe`) |
| **Stop** / **Restart** | Stop that process (and any Debug exe the LC can see), then Start again |
| **Rebuild Release** / **Rebuild Debug** | `dotnet build HulkReNaymer.sln -c …` (background) |
| **Run Debug** | Starts the Debug exe |
| **Open CLI** | Opens `cmd.exe` at the repo root with both bin folders on `PATH`, so you can run `HulkReNaymer.exe file1 file2`, `dotnet test`, etc. |

MLC card actions (no MLC rebuild):

- **Open** / **Start** — the Release `HulkReNaymer.exe` from `launch-control.json` `installExe`
- **Open Launch Control** — this LC (the five buttons above)
- **Diagnostics** — opens the log folders

Register once (or use MLC **Add app** / **Scan folder…** on this repo):

```powershell
./scripts/Register-HulkReNaymer-MLC.ps1
```

Then start `HulkReNaymer-LaunchControl.cmd`, or open it from MLC. The CMD builds `launch-control\HulkReNaymer.LaunchControl.csproj` if the exe is missing. Clone MLC to `%USERPROFILE%\Projects\master-launch-control`, or set `MLC_ROOT` to that clone, so the LC project can reference `LaunchControl.Standard`. Do **not** use `start ""` in the CMD — MLC must keep the inherited admin token.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). To run the app while developing:

```powershell
dotnet restore
dotnet test
dotnet run --project src/HulkReNaymer.App
```

To produce the same installer and portable zip that Releases publish, on Windows:

```powershell
./scripts/build-windows.ps1
```

That script tests, publishes a self-contained `win-x64` build, writes `artifacts/HulkReNaymer-portable-win-x64.zip`, and compiles `artifacts/HulkReNaymer-Setup.exe` if [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed.

Tag a version (`v1.0.0`) and push it to trigger the release workflow.

## Solution layout

- `src/HulkReNaymer.Core` — rename engine, scan, EXIF/ID3, undo (net8.0, tested on any OS)
- `src/HulkReNaymer.App` — WPF UI (`net8.0-windows`)
- `tests/HulkReNaymer.Tests` — engine and rename/undo tests
- `scripts/build-windows.ps1` — release publish + zip + installer
- `scripts/install-sendto.ps1` — add or remove the Explorer Send To shortcut
- `scripts/HulkReNaymer-LaunchControl.cmd` — Master Launch Control entrypoint
- `scripts/Register-HulkReNaymer-MLC.ps1` — add this repo to MLC `apps.json`
- `launch-control\` — C# LC host (Rebuild / Run / Open CLI)
- `setup/HulkReNaymer.iss` — Inno Setup script
- `docs/Bulk_Rename_Utility_Guide.md` — the capability guide this app implements

## Licence

MIT.
