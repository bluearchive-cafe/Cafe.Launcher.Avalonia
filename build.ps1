$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet restore .\Cafe.Launcher.Avalonia.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
