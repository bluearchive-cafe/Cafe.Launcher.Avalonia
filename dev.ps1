param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ui')]
    [string]$Task
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

& "$PSScriptRoot\test.ps1" -Suite Unit -FilterClass '*UiStyleContractTests'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot\test.ps1" -Suite Headless
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
