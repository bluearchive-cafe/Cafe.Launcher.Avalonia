param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    # 跑哪一套：All（默认，两套都跑）/ Unit / Headless。分套用于本地快速迭代，
    # 提交前的完整验证仍然是一条命令（默认即全量）。
    [ValidateSet('All', 'Unit', 'Headless')]
    [string]$Suite = 'All',
    # 只跑匹配的用例（`dotnet test --filter` 的表达式，如 "FullyQualifiedName~VersionComparerTests"）。
    # 全量运行不带它，避免"我以为跑了全套"。
    [string]$Filter,
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
    # 基线再生只对无头工程里的 Golden 用例有意义：与显式筛选混用会得到
    # "看起来跑过了、其实只跑了一条"的结果，宁可直接拒绝。
    if ($Filter) {
        throw '-UpdateGolden 与 -Filter 不能同时使用：基线再生固定跑 Golden 用例。'
    }

    if ($Suite -eq 'Unit') {
        throw '-UpdateGolden 与 -Suite Unit 不能同时使用：Golden 基线属于无头工程。'
    }

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

$selected = if ($Suite -eq 'All') { $projects } else { $projects | Where-Object { $_.Name -eq $Suite } }

foreach ($projectInfo in $selected) {
    $arguments = @(
        'test', $projectInfo.Project, '-c', $Configuration,
        '--results-directory', $resultsRoot,
        '--logger', "trx;LogFileName=$($projectInfo.Name).trx"
    )
    if ($Filter) {
        $arguments += @('--filter', $Filter)
    }

    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
