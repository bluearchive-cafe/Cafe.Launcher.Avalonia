param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Tests'
$projects = @(
    @{
        Name = 'Unit'
        Project = '.\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj'
    },
    @{
        Name = 'Headless'
        Project = '.\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj'
    }
)

foreach ($projectInfo in $projects) {
    dotnet test $projectInfo.Project -c $Configuration `
        --results-directory $resultsRoot `
        --logger "trx;LogFileName=$($projectInfo.Name).trx"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
