#Requires -Version 5.1
<#
.SYNOPSIS
  Tests, publishes a self-contained win-x64 build, zips it, and optionally compiles Setup.exe.
#>
param(
    [switch]$SkipTests,
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$csproj = Join-Path $root "src\HulkReNaymer.App\HulkReNaymer.App.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "app"
$zipPath = Join-Path $artifacts "HulkReNaymer-portable-win-x64.zip"
$iss = Join-Path $root "setup\HulkReNaymer.iss"

function Get-AppVersion {
    if ($env:GITHUB_REF -match '^refs/tags/v(.+)$') {
        return $Matches[1]
    }
    $text = Get-Content -Raw -Path $csproj
    if ($text -match '<Version>([^<]+)</Version>') {
        return $Matches[1].Trim()
    }
    return "1.0.0"
}

function Find-Iscc {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $paths = @($cmd.Source) + $paths }
    foreach ($path in $paths) {
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    }
    return $null
}

$version = Get-AppVersion
$assemblyVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
Write-Host "HulkReNaymer $version"

if (-not $SkipTests) {
    Write-Host "Running tests..."
    dotnet test (Join-Path $root "tests\HulkReNaymer.Tests\HulkReNaymer.Tests.csproj") --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
}

if (Test-Path $artifacts) {
    Remove-Item -Recurse -Force $artifacts
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Publishing self-contained win-x64..."
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishProfile=Win-x64 `
    -p:PublishTrimmed=false `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Get-ChildItem -Path $publishDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
$exe = Join-Path $publishDir "HulkReNaymer.exe"
if (-not (Test-Path $exe)) { throw "Publish did not produce HulkReNaymer.exe." }

$portableRoot = Join-Path $artifacts "portable"
$portableApp = Join-Path $portableRoot "HulkReNaymer"
New-Item -ItemType Directory -Path $portableApp -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $portableApp -Recurse
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path $portableApp -DestinationPath $zipPath
Write-Host "Wrote $zipPath"

$iscc = Find-Iscc
if ($iscc) {
    Write-Host "Compiling installer with $iscc"
    & $iscc "/DMyAppVersion=$version" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }
    $setup = Join-Path $artifacts "HulkReNaymer-Setup.exe"
    if (-not (Test-Path $setup)) { throw "Expected $setup after Inno Setup." }
    Write-Host "Wrote $setup"
}
elseif ($RequireInstaller) {
    throw "Inno Setup (ISCC.exe) was not found. Install it or run without -RequireInstaller."
}
else {
    Write-Warning "Inno Setup not found; skipped HulkReNaymer-Setup.exe. Install Inno Setup 6 to build the installer."
}

Write-Host "Done. Artifacts in $artifacts"
