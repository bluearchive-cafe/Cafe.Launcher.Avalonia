$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\coverage.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ResxResourceContractTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
