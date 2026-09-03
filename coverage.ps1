$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
# MSBuild 常驻复用节点会跨构建持有刚拷贝文件的句柄，coverlet 紧随其后的插桩重写
# 会因 "file is being used by another process" 偶发失败并静默降级为无覆盖数据。
$env:MSBUILDDISABLENODEREUSE = '1'
$threshold = 0.50
# ADR-016 游戏操作表面连续转换（435 系列）：净增独立形变管线，实测手写行覆盖地板 84.35%–84.41%
# （三次全量 verify；分支覆盖升至 91.01%）。行基线随之棘轮至 0.8430，禁止继续下探。
$lineBaseline = 0.8430
$branchBaseline = 0.8899
$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Coverage'

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRootPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

# Coverlet reports class filenames relative to the instrumented project directory.
$applicationProjectDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'src\Cafe.Launcher.Avalonia'))

if (Test-Path -LiteralPath $resultsRoot) {
    Remove-Item -LiteralPath $resultsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $resultsRoot | Out-Null

$projects = @(
    @{
        Name = 'Unit'
        Project = '.\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj'
        ResultsDirectory = Join-Path $resultsRoot 'unit'
    },
    @{
        Name = 'Headless'
        Project = '.\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj'
        ResultsDirectory = Join-Path $resultsRoot 'headless'
    }
)

$reportPaths = @{}

function Invoke-CoverageRun {
    param($ProjectInfo)

    $project = $ProjectInfo.Project
    $projectResults = $ProjectInfo.ResultsDirectory
    $coverletOutput = Join-Path $projectResults 'coverage'
    $collectArgs = @(
        '-p:CollectCoverage=true',
        '-p:CoverletOutputFormat=cobertura',
        '-p:ExcludeByFile=**/Resources/LauncherStrings.Designer.cs',
        "-p:CoverletOutput=$coverletOutput"
    )

    # coverlet.msbuild 是编译期插桩，但它在同一构建里对刚拷贝到 bin 的被测 DLL 做
    # 插桩重写时，会与 MSBuild 自身的文件拷贝句柄竞争（"file is being used by
    # another process"，仅告警并静默降级为无覆盖数据）。拆成两步：先普通构建完成
    # 编译与拷贝，再带 CollectCoverage 参数做一次增量构建——此时编译/拷贝全部
    # up-to-date 跳过，coverlet 独占改写 DLL，插桩不再有竞争窗口。
    dotnet build $project -c Debug --no-restore | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build $project -c Debug --no-restore -nodeReuse:false @collectArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test $project -c Debug --no-build --no-restore `
        --results-directory $projectResults `
        --logger "trx;LogFileName=$($ProjectInfo.Name).trx" `
        @collectArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $reports = @(Get-ChildItem -LiteralPath $projectResults -Filter 'coverage.cobertura.xml')
    if ($reports.Count -ne 1) {
        throw "Expected exactly one Cobertura report in '$projectResults', found $($reports.Count)."
    }

    return $reports[0].FullName
}

function Test-ReportHasData {
    param($ReportPath)

    [xml]$coverageXml = Get-Content -LiteralPath $ReportPath -Raw
    $lineCount = 0
    foreach ($package in @($coverageXml.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $lineCount += @($class.lines.line).Count
        }
    }

    return $lineCount -ge 1000
}

foreach ($projectInfo in $projects) {
    $project = $projectInfo.Project
    $projectResults = $projectInfo.ResultsDirectory

    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null

    $reportPath = Invoke-CoverageRun $projectInfo
    if (-not (Test-ReportHasData $reportPath)) {
        # unit/headless 共享同一 src 插桩 DLL：前一个 testhost 的文件句柄在 Windows 上
        # 延迟释放时，下一个项目的插桩重写会偶发锁冲突（coverlet 仅告警），产出空壳
        # 报告。等待句柄释放后重试一次，把偶发抖动从验证失败降级为多一次运行。
        Write-Output "Coverage report for $($projectInfo.Name) has no data; retrying once."
        Start-Sleep -Seconds 2
        $reportPath = Invoke-CoverageRun $projectInfo
    }

    $reportPaths[$projectInfo.Name] = $reportPath
}

$lineCoverage = @{}
$branchCoverage = @{}

foreach ($reportPath in $reportPaths.Values) {
    [xml]$coverageXml = Get-Content -LiteralPath $reportPath -Raw

    foreach ($package in @($coverageXml.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $relativePath = $class.filename -replace '[\\/]', [string][IO.Path]::DirectorySeparatorChar
            $fullPath = [IO.Path]::GetFullPath((Join-Path $applicationProjectDirectory $relativePath))
            $extension = [IO.Path]::GetExtension($fullPath)

            if (
                $extension -ne '.cs' -or
                -not $fullPath.StartsWith($repositoryRootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                $relativePath.StartsWith('obj\', [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $fullPath -PathType Leaf)
            ) {
                continue
            }

            foreach ($line in @($class.lines.line)) {
                $lineNumber = [int]$line.number
                $lineKey = "$fullPath|$lineNumber"
                $lineHit = ([int]$line.hits) -gt 0

                if (-not $lineCoverage.ContainsKey($lineKey)) {
                    $lineCoverage[$lineKey] = $lineHit
                }
                elseif ($lineHit) {
                    $lineCoverage[$lineKey] = $true
                }

                if ($line.branch -ne 'True') {
                    continue
                }

                foreach ($condition in @($line.conditions.condition)) {
                    $branchKey = "$fullPath|$lineNumber|$($condition.number)|$($condition.type)"
                    $coveragePercentText = [string]$condition.coverage
                    $coveragePercent = [int]($coveragePercentText.TrimEnd('%'))
                    $branchHit = $coveragePercent -gt 0

                    if (-not $branchCoverage.ContainsKey($branchKey)) {
                        $branchCoverage[$branchKey] = $branchHit
                    }
                    elseif ($branchHit) {
                        $branchCoverage[$branchKey] = $true
                    }
                }
            }
        }
    }
}

$validLineCount = $lineCoverage.Count
if ($validLineCount -eq 0) {
    throw 'Expected at least one handwritten C# line in coverage reports.'
}

$coveredLineCount = @($lineCoverage.Values | Where-Object { $_ }).Count
$lineRatio = $coveredLineCount / $validLineCount

$validBranchCount = $branchCoverage.Count
if ($validBranchCount -eq 0) {
    throw 'Expected at least one handwritten C# branch in coverage reports.'
}

$coveredBranchCount = @($branchCoverage.Values | Where-Object { $_ }).Count
$branchRatio = $coveredBranchCount / $validBranchCount

Write-Output ("Handwritten C# line coverage: {0:N2}% ({1}/{2})" -f ($lineRatio * 100), $coveredLineCount, $validLineCount)
Write-Output ("Handwritten C# branch coverage: {0:N2}% ({1}/{2})" -f ($branchRatio * 100), $coveredBranchCount, $validBranchCount)
Write-Output ("Unit report: {0}" -f $reportPaths.Unit)
Write-Output ("Headless report: {0}" -f $reportPaths.Headless)

if ($lineRatio -lt $threshold -or $branchRatio -lt $threshold) {
    Write-Error ("Coverage threshold not met. Required {0:P0}; lines {1:N2}%, branches {2:N2}%." -f $threshold, ($lineRatio * 100), ($branchRatio * 100))
    exit 1
}

if ($lineRatio -lt $lineBaseline -or $branchRatio -lt $branchBaseline) {
    Write-Error ("Coverage baseline regressed. Required lines {0:N2}%, branches {1:N2}%; actual lines {2:N2}%, branches {3:N2}%." -f ($lineBaseline * 100), ($branchBaseline * 100), ($lineRatio * 100), ($branchRatio * 100))
    exit 1
}

exit 0
