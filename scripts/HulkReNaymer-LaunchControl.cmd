@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Master-facing Launch Control. Invoke the C# host directly (no `start ""`)
rem so an elevated Master Launch Control keeps its token.

set "EXE=%~dp0..\launch-control\bin\Release\net8.0-windows\HulkReNaymer.LaunchControl.exe"
if not exist "%EXE%" set "EXE=%~dp0..\launch-control\bin\Debug\net8.0-windows\HulkReNaymer.LaunchControl.exe"
if not exist "%EXE%" (
  echo Building HulkReNaymer Launch Control...
  where dotnet >nul 2>&1
  if errorlevel 1 (
    echo .NET 8 SDK is required. Install from https://dotnet.microsoft.com/download
    echo Then run this launcher again.
    pause
    exit /b 1
  )
  if defined MLC_ROOT (
    dotnet build "%~dp0..\launch-control\HulkReNaymer.LaunchControl.csproj" -c Release -p:MlcRoot="%MLC_ROOT%"
  ) else (
    dotnet build "%~dp0..\launch-control\HulkReNaymer.LaunchControl.csproj" -c Release
  )
  if errorlevel 1 (
    echo Build failed. Clone Master Launch Control to %%USERPROFILE%%\Projects\master-launch-control
    echo or set MLC_ROOT to that repo, then try again.
    pause
    exit /b 1
  )
  set "EXE=%~dp0..\launch-control\bin\Release\net8.0-windows\HulkReNaymer.LaunchControl.exe"
)

if not exist "%EXE%" (
  echo Could not find HulkReNaymer.LaunchControl.exe
  pause
  exit /b 1
)

"%EXE%" %*
exit /b 0
