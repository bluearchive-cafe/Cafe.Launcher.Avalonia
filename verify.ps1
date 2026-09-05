$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

# 纯文本扫描、无 dotnet 依赖，放在最前 fail-fast。
& "$PSScriptRoot\scripts\Test-LocalizationContract.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\build.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\coverage.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet restore .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -r win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ResxResourceContractTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
