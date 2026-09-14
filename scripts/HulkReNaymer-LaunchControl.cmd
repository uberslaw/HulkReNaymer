@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Master-facing Launch Control. Invoke the C# host directly (no `start ""`)
rem so an elevated Master Launch Control keeps its token.
rem
rem LaunchControl.Standard search order (Resolve-MlcRoot.ps1):
rem   1. MLC_ROOT / MlcRoot when it contains src\LaunchControl.Standard\LaunchControl.Standard.csproj
rem   2. %USERPROFILE%\Projects\master-launch-control
rem   3. C:\Users\christopher.owen\Projects\master-launch-control
rem   4. sibling ..\master-launch-control next to this repo
rem   5. %LOCALAPPDATA%\HulkReNaymer\master-launch-control (auto-clone cache)
rem If none exist, git clone --depth 1 https://github.com/uberslaw/master-launch-control.git
rem into that cache, then dotnet build -p:MlcRoot=...

set "REPO_ROOT=%~dp0.."
for %%I in ("%REPO_ROOT%") do set "REPO_ROOT=%%~fI"
set "LC_PROJ=%REPO_ROOT%\launch-control\HulkReNaymer.LaunchControl.csproj"

set "EXE=%REPO_ROOT%\launch-control\bin\Release\net8.0-windows\HulkReNaymer.LaunchControl.exe"
if not exist "%EXE%" set "EXE=%REPO_ROOT%\launch-control\bin\Debug\net8.0-windows\HulkReNaymer.LaunchControl.exe"
if not exist "%EXE%" (
  echo Building HulkReNaymer Launch Control...
  where dotnet >nul 2>&1
  if errorlevel 1 (
    echo .NET 8 SDK is required. Install from https://dotnet.microsoft.com/download
    echo Then run this launcher again.
    pause
    exit /b 1
  )

  set "MLC_RESOLVED="
  for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Resolve-MlcRoot.ps1" -RepoRoot "%REPO_ROOT%"`) do set "MLC_RESOLVED=%%I"
  if not defined MLC_RESOLVED (
    echo Build cannot start until LaunchControl.Standard is available.
    pause
    exit /b 1
  )

  dotnet build "%LC_PROJ%" -c Release -p:MlcRoot="%MLC_RESOLVED%"
  if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
  )
  set "EXE=%REPO_ROOT%\launch-control\bin\Release\net8.0-windows\HulkReNaymer.LaunchControl.exe"
)

if not exist "%EXE%" (
  echo Could not find HulkReNaymer.LaunchControl.exe
  pause
  exit /b 1
)

"%EXE%" %*
exit /b 0
