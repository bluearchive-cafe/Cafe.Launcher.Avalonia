$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\coverage.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
