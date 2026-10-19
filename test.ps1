param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    # 跑哪一套：All（默认，两套都跑）/ Unit / Headless。分套用于本地快速迭代，
    # 提交前的完整验证仍然是一条命令（默认即全量）。
    [ValidateSet('All', 'Unit', 'Headless')]
    [string]$Suite = 'All',
    # xUnit 原生筛选：完整限定类名或方法名，支持首尾 * 通配符。
    [string]$FilterClass,
    [string]$FilterMethod,
    # Regenerate golden screenshot baselines instead of comparing:
    # .\test.ps1 -UpdateGolden  ->  runs the Golden tests with CAFE_GOLDEN_UPDATE=1.
    # Commit the refreshed PNGs under tests/Cafe.Launcher.HeadlessTests/Baselines
    # together with the intentional visual change.
    [switch]$UpdateGolden
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Tests'
$projects = @(
    @{
        Name = 'Unit'
        Project = '.\tests\Cafe.Launcher.Tests\Cafe.Launcher.Tests.csproj'
    },
    @{
        Name = 'Headless'
        Project = '.\tests\Cafe.Launcher.HeadlessTests\Cafe.Launcher.HeadlessTests.csproj'
    }
)

# MTP 的挂起扩展在 5 分钟无测试活动后终止进程树并返回失败；None 保留
# 原来的不生成 dump 策略。Golden 再生也必须先安装同一个挂起防护。
$hangGuard = @('--hangdump', '--hangdump-timeout', '5m', '--hangdump-type', 'None')

if ($UpdateGolden) {
    # 基线再生只对无头工程里的 Golden 用例有意义：与显式筛选混用会得到
    # "看起来跑过了、其实只跑了一条"的结果，宁可直接拒绝。
    if ($FilterClass -or $FilterMethod) {
        throw '-UpdateGolden 与筛选参数不能同时使用：基线再生固定跑 Golden 用例。'
    }

    if ($Suite -eq 'Unit') {
        throw '-UpdateGolden 与 -Suite Unit 不能同时使用：Golden 基线属于无头工程。'
    }

    $env:CAFE_GOLDEN_UPDATE = '1'
    try {
        dotnet test --project $projects[1].Project -c $Configuration `
            --results-directory $resultsRoot `
            --filter-method '*Golden*' `
            --report-trx --report-trx-filename 'Golden.trx' `
            @hangGuard
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
        'test', '--project', $projectInfo.Project, '-c', $Configuration,
        '--results-directory', $resultsRoot,
        '--report-trx', '--report-trx-filename', "$($projectInfo.Name).trx"
    )
    if ($FilterClass) {
        $arguments += @('--filter-class', $FilterClass)
    }
    if ($FilterMethod) {
        $arguments += @('--filter-method', $FilterMethod)
    }

    $arguments += $hangGuard
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
