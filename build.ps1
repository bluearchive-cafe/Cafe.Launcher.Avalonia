$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet restore .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
