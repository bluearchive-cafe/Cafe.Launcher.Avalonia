$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

# 纯文本扫描、无 dotnet 依赖，放在最前 fail-fast。
# 成功路径为自然落空、不设 $LASTEXITCODE：全新会话中它仍是 $null，
# $null -ne 0 恒为真会把整个 verify 短路成静默成功，故用 $? 判定。
& "$PSScriptRoot\scripts\Test-LocalizationContract.ps1"
if (-not $?) { exit $LASTEXITCODE }

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
