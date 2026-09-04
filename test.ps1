param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    # Regenerate golden screenshot baselines instead of comparing:
    # .\test.ps1 -UpdateGolden  ->  runs the Golden tests with CAFE_GOLDEN_UPDATE=1.
    # Commit the refreshed PNGs under tests/Cafe.Launcher.Avalonia.HeadlessTests/Baselines
    # together with the intentional visual change.
    [switch]$UpdateGolden
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

if ($UpdateGolden) {
    $env:CAFE_GOLDEN_UPDATE = '1'
    try {
        dotnet test $projects[1].Project -c $Configuration `
            --results-directory $resultsRoot `
            --filter 'FullyQualifiedName~Golden'
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Remove-Item Env:CAFE_GOLDEN_UPDATE -ErrorAction SilentlyContinue
    }

    exit 0
}

foreach ($projectInfo in $projects) {
    dotnet test $projectInfo.Project -c $Configuration `
        --results-directory $resultsRoot `
        --logger "trx;LogFileName=$($projectInfo.Name).trx"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
