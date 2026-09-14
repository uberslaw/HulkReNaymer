#Requires -Version 5.1
<#
.SYNOPSIS
  Registers this repo's Launch Control as a Generic app in Master Launch Control.
#>
param(
    [string]$DisplayName = "HulkReNaymer"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$launchPath = Join-Path $PSScriptRoot "HulkReNaymer-LaunchControl.cmd"
if (-not (Test-Path -LiteralPath $launchPath)) {
    throw "Missing Launch Control entrypoint: $launchPath"
}

$registryDir = Join-Path $env:LOCALAPPDATA "MasterLaunchControl"
$registryPath = Join-Path $registryDir "apps.json"
New-Item -ItemType Directory -Force -Path $registryDir | Out-Null

$apps = @()
if (Test-Path -LiteralPath $registryPath) {
    $raw = Get-Content -LiteralPath $registryPath -Raw
    if ($raw.Trim().Length -gt 0) {
        $parsed = $raw | ConvertFrom-Json
        if ($parsed -is [System.Array]) { $apps = @($parsed) }
        elseif ($null -ne $parsed) { $apps = @($parsed) }
    }
}

$existing = $apps | Where-Object {
    $_.displayName -eq $DisplayName -or
    $_.launchPath -eq $launchPath
} | Select-Object -First 1

$id = if ($existing -and $existing.id) { [string]$existing.id } else { [guid]::NewGuid().ToString("N") }

$entry = [pscustomobject]@{
    id           = $id
    displayName  = $DisplayName
    launchPath   = $launchPath
    adapterKind  = "Generic"
    apiBaseUrl   = $null
    healthHost   = $null
    healthPort   = $null
    appRoot      = $root
}

$apps = @($apps | Where-Object {
    $_.id -ne $id -and
    $_.launchPath -ne $launchPath -and
    $_.displayName -ne $DisplayName
})
$apps += $entry

$json = ConvertTo-Json -InputObject @($apps) -Depth 6
if (@($apps).Count -eq 1 -and $json.TrimStart().StartsWith('{')) {
    $json = "[$json]"
}
Set-Content -LiteralPath $registryPath -Value $json -Encoding UTF8
Write-Host "Registered $DisplayName in $registryPath"
Write-Host "Launch path: $launchPath"
Write-Host "Open Master Launch Control and use Open Launch Control on the HulkReNaymer card."
