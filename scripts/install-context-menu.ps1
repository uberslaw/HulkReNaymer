param(
    [string]$ExePath,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
# Per-user Explorer verbs only (HKCU). No HKLM, Run keys, or services.

$verb = "HulkReNaymer"
$label = "Rename with HulkReNaymer"
$roots = @(
    "HKCU:\Software\Classes\*\shell\$verb",
    "HKCU:\Software\Classes\Directory\shell\$verb",
    "HKCU:\Software\Classes\Directory\Background\shell\$verb"
)

function Remove-Verb {
    foreach ($key in $roots) {
        if (Test-Path $key) {
            Remove-Item -Path $key -Recurse -Force
            Write-Host "Removed $key"
        }
    }
}

if ($Remove) {
    Remove-Verb
    Write-Host "Explorer context menu verb removed (current user)."
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

$exe = (Resolve-Path $ExePath).Path
$command = "`"$exe`" `"%1`""
$background = "`"$exe`" `"%V`""

function Set-Verb($keyPath, $commandValue) {
    New-Item -Path $keyPath -Force | Out-Null
    New-Item -Path (Join-Path $keyPath "command") -Force | Out-Null
    Set-ItemProperty -Path $keyPath -Name "(default)" -Value $label
    Set-ItemProperty -Path $keyPath -Name "Icon" -Value $exe
    Set-ItemProperty -Path (Join-Path $keyPath "command") -Name "(default)" -Value $commandValue
}

Set-Verb $roots[0] $command
Set-Verb $roots[1] $command
Set-Verb $roots[2] $background
Write-Host "Explorer verb installed for this user (files, folders, folder background)."
Write-Host "Right-click → $label. Use -Remove to delete the verb."
