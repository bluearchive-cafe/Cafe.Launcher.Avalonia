$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet restore .\src\Cafe.Launcher\Cafe.Launcher.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build .\src\Cafe.Launcher\Cafe.Launcher.csproj -c Debug --no-restore
