@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Master-facing Launch Control. Invoke the C# host directly (no `start ""`)
rem so an elevated Master Launch Control keeps its token.
rem
rem Builds launch-control\HulkReNaymer.LaunchControl.csproj against the
rem in-repo LaunchControl.Standard. No MLC clone or MLC_ROOT is required.

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

  dotnet build "%LC_PROJ%" -c Release
  if errorlevel 1 (
    echo.
    echo Build failed. See the dotnet errors above.
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
