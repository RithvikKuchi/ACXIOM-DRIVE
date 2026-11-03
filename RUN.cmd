@echo off
setlocal
cd /d "%~dp0"
set ASPNETCORE_ENVIRONMENT=Development
set DOTNET_CLI_TELEMETRY_OPTOUT=1
echo.
echo AcxiomCRM - source launch
echo Requires the .NET 10 SDK. The Windows demo package runs without an SDK.
echo Open http://localhost:5080 after the server starts.
echo Demo login: admin@acxiom.local / Acxiom@2026!
echo Press Ctrl+C to stop the server.
echo.
dotnet run --project AcxiomCRM.csproj --no-launch-profile --urls http://localhost:5080
if errorlevel 1 pause
