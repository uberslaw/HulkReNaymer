#Requires -Version 5.1
<#
.SYNOPSIS
  Locate (or shallow-clone) Master Launch Control so HulkReNaymer can reference LaunchControl.Standard.

Search order (first hit that contains src\LaunchControl.Standard\LaunchControl.Standard.csproj wins):
  1. -MlcRoot / MLC_ROOT
  2. %USERPROFILE%\Projects\master-launch-control
  3. C:\Users\christopher.owen\Projects\master-launch-control
  4. sibling ..\master-launch-control next to this HulkReNaymer repo
  5. %LOCALAPPDATA%\HulkReNaymer\master-launch-control (auto-clone cache)
     (or launch-control\.mlc when LOCALAPPDATA is unset)

If none exist: git clone --depth 1 https://github.com/uberslaw/master-launch-control.git
into the auto-clone cache.

Writes the resolved repo path to stdout. Status and errors go to stderr.
#>
[CmdletBinding()]
param(
    [string]$MlcRoot = $env:MLC_ROOT,
    [string]$RepoRoot = "",
    [switch]$NoClone,
    [switch]$PrintCandidates
)

$ErrorActionPreference = "Stop"
$MarkerRel = Join-Path "src" (Join-Path "LaunchControl.Standard" "LaunchControl.Standard.csproj")
$CloneUrl = "https://github.com/uberslaw/master-launch-control.git"

function Write-Info([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

function Get-FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    try {
        return [System.IO.Path]::GetFullPath($Path)
    } catch {
        return $Path
    }
}

function Test-MlcRepo([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return Test-Path -LiteralPath (Join-Path $Path $MarkerRel)
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
} else {
    $RepoRoot = Get-FullPath $RepoRoot
}

$cache = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    Join-Path $env:LOCALAPPDATA (Join-Path "HulkReNaymer" "master-launch-control")
} else {
    Join-Path $RepoRoot (Join-Path "launch-control" ".mlc")
}

$candidates = [System.Collections.Generic.List[string]]::new()
function Add-Candidate([string]$Path) {
    $full = Get-FullPath $Path
    if ([string]::IsNullOrWhiteSpace($full)) { return }
    if ($candidates -contains $full) { return }
    [void]$candidates.Add($full)
}

Add-Candidate $MlcRoot
Add-Candidate (Join-Path $env:USERPROFILE "Projects\master-launch-control")
Add-Candidate "C:\Users\christopher.owen\Projects\master-launch-control"
Add-Candidate (Join-Path $RepoRoot "..\master-launch-control")
Add-Candidate $cache

if ($PrintCandidates) {
    $candidates | ForEach-Object { Write-Output $_ }
    exit 0
}

$resolved = $null
foreach ($candidate in $candidates) {
    if (Test-MlcRepo $candidate) {
        $resolved = $candidate
        break
    }
}

if (-not $resolved -and -not $NoClone) {
    Write-Info "LaunchControl.Standard not found. Cloning $CloneUrl into $cache ..."
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        Write-Info "git is not on PATH. Cannot clone Master Launch Control."
    } else {
        $parent = Split-Path -Parent $cache
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }
        $gitDir = Join-Path $cache ".git"
        if ((Test-Path -LiteralPath $cache) -and -not (Test-Path -LiteralPath $gitDir) -and -not (Test-MlcRepo $cache)) {
            Remove-Item -LiteralPath $cache -Recurse -Force -ErrorAction SilentlyContinue
        }
        try {
            if (Test-Path -LiteralPath $gitDir) {
                & git -C $cache pull --ff-only 2>&1 | ForEach-Object { Write-Info "$_" }
            } elseif (-not (Test-MlcRepo $cache)) {
                & git clone --depth 1 $CloneUrl $cache 2>&1 | ForEach-Object { Write-Info "$_" }
            }
        } catch {
            Write-Info "git clone/pull failed: $($_.Exception.Message)"
        }
        if (Test-MlcRepo $cache) {
            $resolved = Get-FullPath $cache
        }
    }
}

if (-not $resolved) {
    Write-Info "LaunchControl.Standard was not found. Tried:"
    foreach ($candidate in $candidates) {
        Write-Info "  $candidate"
    }
    if ($NoClone) {
        Write-Info "Clone skipped (-NoClone). Set MLC_ROOT / MlcRoot to a clone of $CloneUrl"
    } else {
        Write-Info "Clone of $CloneUrl also failed (git missing or offline)."
    }
    exit 1
}

Write-Info "Using Master Launch Control at $resolved"
Write-Output $resolved
exit 0
