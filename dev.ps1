param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ui')]
    [string]$Task
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet test .\tests\Cafe.Launcher.Tests\Cafe.Launcher.Tests.csproj `
    --filter "FullyQualifiedName~UiStyleContractTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test .\tests\Cafe.Launcher.HeadlessTests\Cafe.Launcher.HeadlessTests.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
