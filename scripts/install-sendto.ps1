param(
    [string]$ExePath,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
$sendTo = Join-Path $env:APPDATA "Microsoft\Windows\SendTo"
$shortcutPath = Join-Path $sendTo "HulkReNaymer.lnk"

if ($Remove) {
    if (Test-Path $shortcutPath) {
        Remove-Item $shortcutPath -Force
        Write-Host "Removed Send To shortcut: $shortcutPath"
    }
    else {
        Write-Host "No Send To shortcut found."
    }
    return
}

if (-not $ExePath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles} "HulkReNaymer\HulkReNaymer.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "HulkReNaymer\HulkReNaymer.exe")
    )
    $ExePath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $ExePath -or -not (Test-Path $ExePath)) {
    throw "HulkReNaymer.exe not found. Pass -ExePath to the installed or portable executable."
}

New-Item -ItemType Directory -Force -Path $sendTo | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Resolve-Path $ExePath).Path
$shortcut.WorkingDirectory = Split-Path (Resolve-Path $ExePath).Path
$shortcut.WindowStyle = 1
$shortcut.Description = "Bulk rename with HulkReNaymer"
$shortcut.IconLocation = (Resolve-Path $ExePath).Path
$shortcut.Save()
Write-Host "Send To shortcut created: $shortcutPath"
Write-Host "Right-click files in Explorer → Send to → HulkReNaymer."
