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
5. Case (optional strip accents: é → e)
6. Remove
7. Move / copy characters, or swap the two sides of a separator
8. Add prefix, insert, suffix
9. Parent folder
10. Date (created / modified / accessed / now / photo EXIF)
11. Numbering, or restyle the last number already in the name
12. JavaScript (`newName = name + '_' + index`)
13. Filters
14. Copy / move to another folder
15. Timestamps
16. Read-only / hidden / Windows-safe names

Tokens work in prefix, suffix, insert, fixed names, find/replace, regex replace, and the copy/move destination folder:

`{name}` `{ext}` `{folder}` `{n}` `{n:3}` `{date}` `{date:exif}` `{yyyy}` `{mm}` `{dd}` `{exif.date}` `{exif.make}` `{exif.model}` `{exif.camera}` `{id3.artist}` `{id3.album}` `{id3.title}` `{size}` `{git.branch}` `{hash:8}` `{video.date}` `{video.duration}`

`{git.branch}` is the current branch when the file lives in a git repo (empty in detached HEAD or when git is missing). `{hash}` / `{hash:8}` is the SHA-256 of that file's bytes (first N lowercase hex characters; empty for folders). `{video.date}` / `{video.duration}` come from TagLib when the file is a readable video. Set `HULKRENAYMER_EXIFTOOL` to an `exiftool` binary to merge extra tags; a copy on `PATH` is **not** auto-run (one spawn per file). ExifTool is never required.

The WPF token palette inserts chips into the focused token box (prefix, suffix, insert, fixed name, replace, regex replace, destination).

PowerRename-style aliases also work: `$YYYY` `$YY` `$MM` `$DD` `$mm` (minutes) `${}` `${n:3}` `${padding=3}`. Regex `$1` / `$2` groups are left alone.

Presets include **kebab-case** and **slug** (safe URL slug: lowercase, strip punctuation/accents, hyphenate). The Case panel can apply the same modes.

## Send To (Explorer)

The installer can add **HulkReNaymer** to the Explorer **Send to** menu. For a portable build, run:

```powershell
./scripts/install-sendto.ps1 -ExePath C:\path\to\HulkReNaymer.exe
```

That creates a shortcut in `%AppData%\Microsoft\Windows\SendTo`. Right-click files → Send to → HulkReNaymer. Use `-Remove` to delete the shortcut.

An optional **Explorer context menu** verb (`Rename with HulkReNaymer`) can be added from the installer (unchecked by default) or, for a portable build, with a per-user HKCU verb (no startup/Run-key persistence):

```powershell
./scripts/install-context-menu.ps1 -ExePath C:\path\to\HulkReNaymer.exe
./scripts/install-context-menu.ps1 -Remove
```

## CLI

`HulkReNaymer.Cli` is a `net8.0` console app that reuses `RenameEngine`, `ApplyService`, and `Scanner` (no WPF). From the repo root (including a Launch Control **Open CLI** prompt):

```powershell
dotnet run --project src/HulkReNaymer.Cli -- preview .\photos --preset kebab-case
dotnet run --project src/HulkReNaymer.Cli -- apply .\docs --find " " --replace _ --collision skip
dotnet run --project src/HulkReNaymer.Cli -- apply file1.txt file2.txt --prefix "{git.branch}_{hash:8}_"
dotnet run --project src/HulkReNaymer.Cli -- undo
```

After `dotnet build src/HulkReNaymer.Cli`, the exe is `HulkReNaymer.Cli.exe` (Windows) / `HulkReNaymer.Cli` under `src/HulkReNaymer.Cli/bin/<config>/net8.0/`. Default command is **preview** (dry-run). Use `apply` / `--apply` to commit. Flags: `--recurse`, `--wildcard`, `--folders`, `--find` / `--replace`, `--prefix`, `--suffix`, `--case`, `--preset`, `--favorite`, `--collision fail|skip|append`, `--from-list`, `--regex`. Tracked files inside a git repo are renamed with `git mv` (falls back to `File.Move`); undo still works.

## Launch Control (Master Launch Control)

HulkReNaymer plugs into [Master Launch Control](https://github.com/uberslaw/master-launch-control) as a **Generic** app. The product LC is a C# host that references the in-repo copy of `LaunchControl.Standard` at `launch-control/LaunchControl.Standard`. You do **not** need to clone Master Launch Control, set `MLC_ROOT`, or be online to build the LC.

The LC is the same `LaunchControl.Standard` chrome as Switcheroo (header, Theme…, status + PID, Run Release / Stop / Restart, Refresh, Follow logs, extra-action groups, event pane). HulkReNaymer has no Windows service, so Run Release / Stop drive the desktop exe.

| Button | What it does |
|--------|----------------|
| **Run Release** | Primary green Start button, labeled Run Release (`src\HulkReNaymer.App\bin\Release\net8.0-windows\HulkReNaymer.exe`). Also listed in the Run extras. |
| **Stop** / **Restart** | Stop that process (and any Debug exe the LC can see), then Run Release again |
| **Rebuild Release** / **Rebuild Debug** | `dotnet build HulkReNaymer.sln -c …` (background) |
| **Run Debug** | Starts the Debug exe |
| **Open CLI** | Opens `cmd.exe` at the repo root with both bin folders on `PATH`, so you can run `HulkReNaymer.exe file1 file2`, `dotnet test`, etc. |

After `Register-HulkReNaymer-MLC.ps1` (or **Add app** / **Scan folder…**), HulkReNaymer is a **Generic** card on that same MLC grid (Heimdall / Switcheroo / Serraview / LaptopBuildWall). MLC does not let a Generic app add extra card buttons — Rebuild / Run Release / Run Debug / Open CLI stay on **Open Launch Control**, same as LaptopBuildWall’s rebuild extras. The card buttons are the stock ones:

| MLC card | HulkReNaymer |
|----------|----------------|
| **Open** | Run Release (`installExe`) |
| **Open Launch Control** | This LC (Run Release / Stop + Rebuild / Run Debug / Open CLI) |
| **Theme this LC…** | Theme the LC chrome (not MLC) |
| **Stop LC** | Close the LC window only — does **not** quit HulkReNaymer |
| **Start** / **Stop** / **Restart** | Run / kill / restart the Release exe (enabled after the first Rebuild Release) |
| **Diagnostics** | Opens `%AppData%\HulkReNaymer` and LC logs |
| **Edit** / **Remove** | Registry row in `%LOCALAPPDATA%\MasterLaunchControl\apps.json` |

No **Update folder** (Heimdall-only). The card shows Running/Stopped + PID + file version from the Release exe, not Unknown, once `launch-control.json` is next to the CMD.

Register once (or use MLC **Add app** / **Scan folder…** on this repo):

```powershell
./scripts/Register-HulkReNaymer-MLC.ps1
```

Then start `HulkReNaymer-LaunchControl.cmd`, or open it from MLC. The CMD builds `launch-control\HulkReNaymer.LaunchControl.csproj` if the exe is missing, using the vendored `launch-control\LaunchControl.Standard` project. Optional: set `MLC_ROOT` / `MlcRoot` to a Master Launch Control clone if you want that repo’s Standard instead. Do **not** use `start ""` in the CMD — MLC must keep the inherited admin token.

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). To run the app while developing:

```powershell
dotnet restore
dotnet test
dotnet run --project src/HulkReNaymer.App
dotnet run --project src/HulkReNaymer.Cli -- --help
```

Launch Control is not part of `HulkReNaymer.sln`. To build it from this repo (no Master Launch Control clone):

```powershell
dotnet build launch-control/HulkReNaymer.LaunchControl.csproj -c Release
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
- `src/HulkReNaymer.Cli` — console preview/apply (`net8.0`, `HulkReNaymer.Cli`)
- `tests/HulkReNaymer.Tests` — engine, CLI, git-mv, and rename/undo tests
- `scripts/build-windows.ps1` — release publish + zip + installer
- `scripts/install-sendto.ps1` — add or remove the Explorer Send To shortcut
- `scripts/install-context-menu.ps1` — optional per-user Explorer verb (files + folders)
- `scripts/HulkReNaymer-LaunchControl.cmd` — Master Launch Control entrypoint
- `scripts/Register-HulkReNaymer-MLC.ps1` — add this repo to MLC `apps.json`
- `launch-control\` — C# LC host (Rebuild / Run Release / Open CLI)
- `launch-control/LaunchControl.Standard` — vendored theme-host library (no MLC clone required)
- `setup/HulkReNaymer.iss` — Inno Setup script
- `docs/Bulk_Rename_Utility_Guide.md` — the capability guide this app implements

## Licence

MIT.
