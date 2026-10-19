$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$threshold = 0.50
# 棘轮基线 = 最近一次全量 verify 实测值再留约 0.1–0.25pp 余量，不是「历史地板」。
# 2026-09-23 全量重审实测：手写行 85.71%、分支 92.06%。三个 Linux 专属测试
# （procfs 挂载表解析 ×2、UMU 前缀预检启动报告）按平台门规则改为可见跳过，
# 而覆盖率闸口只在 Windows 作业强制，Linux 专属的预检与 procfs 路径在本平台
# 结构性不可覆盖，基线随实测下移。改基线时请与本次实测值一起更新，并保持
# 余量在同一量级。2026-10-19 迁移到 Microsoft Code Coverage 后三次全量实测：
# 行约 86.3%（18,563–18,571/21,502）、分支 92.29%（3,291/3,566），同一基线仍成立。
$lineBaseline = 0.8560
$branchBaseline = 0.9180
$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Coverage'

$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRootPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

# Cobertura producers differ in how they name class files: Coverlet wrote paths
# relative to the <sources> roots, while Microsoft Code Coverage writes absolute
# paths and leaves <sources> empty. Resolve both against the report instead of
# assuming either shape.

if (Test-Path -LiteralPath $resultsRoot) {
    Remove-Item -LiteralPath $resultsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $resultsRoot | Out-Null

$projects = @(
    @{
        Name = 'Unit'
        Project = '.\tests\Cafe.Launcher.Tests\Cafe.Launcher.Tests.csproj'
        ResultsDirectory = Join-Path $resultsRoot 'unit'
    },
    @{
        Name = 'Headless'
        Project = '.\tests\Cafe.Launcher.HeadlessTests\Cafe.Launcher.HeadlessTests.csproj'
        ResultsDirectory = Join-Path $resultsRoot 'headless'
    }
)

$reportPaths = @{}

function Invoke-CoverageRun {
    param($ProjectInfo)

    $project = $ProjectInfo.Project
    $projectResults = $ProjectInfo.ResultsDirectory
    # Microsoft Code Coverage 通过运行时分析器收集，不重写磁盘程序集。
    # coverlet.MTP 会在控制器已加载 Cafe.Launcher.UI 后跳过该程序集，表现层
    # 因此整体从报告里消失。
    dotnet test --project $project -c Debug --no-restore `
        --results-directory $projectResults `
        --report-trx --report-trx-filename "$($ProjectInfo.Name).trx" `
        --hangdump --hangdump-timeout 5m --hangdump-type None `
        --coverage --coverage-output-format cobertura `
        --coverage-output "$($ProjectInfo.Name).cobertura.xml" | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $reportPath = Join-Path $projectResults "$($ProjectInfo.Name).cobertura.xml"
    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw "Expected a Cobertura report at '$reportPath'."
    }

    return $reportPath
}

# 甄别"收集未生效"的空壳报告：真实报告有数万条 line 记录，空壳只有个位到百位，
# 阈值取 1000 与两侧均差数量级——只用于退化检测，不是覆盖率度量。
$minimumReportedLines = 1000

function Test-ReportHasData {
    param($ReportPath)

    [xml]$coverageXml = Get-Content -LiteralPath $ReportPath -Raw
    $lineCount = 0
    foreach ($package in @($coverageXml.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $lineCount += @($class.lines.line).Count
        }
    }

    return $lineCount -ge $minimumReportedLines
}

foreach ($projectInfo in $projects) {
    $project = $projectInfo.Project
    $projectResults = $projectInfo.ResultsDirectory

    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null

    $reportPath = Invoke-CoverageRun $projectInfo
    if (-not (Test-ReportHasData $reportPath)) {
        # 收集器偶发在测试进程退出时来不及写完报告；等待后重试一次，
        # 把一次性抖动从验证失败降级为多一次运行。
        Write-Output "Coverage report for $($projectInfo.Name) has no data; retrying once."
        Start-Sleep -Seconds 2
        Remove-Item -LiteralPath $reportPath -ErrorAction SilentlyContinue
        $reportPath = Invoke-CoverageRun $projectInfo
    }

    $reportPaths[$projectInfo.Name] = $reportPath
}

$lineCoverage = @{}
$branchCoverage = @{}

# 程序集拆分后每个生产程序集都必须真的出现在报告里。报告里的 <sources> 根一旦退回旧的
# 单项目形状，被移走的代码会从分子和分母同时消失，比例反而可能上升——静默通过闸口。
# 这里按项目目录要求它们各自贡献被计数的行，缺一个就直接失败。
# src\Cafe.Launcher.Updater 不在表内是有意的：没有任何测试工程引用它（它的实现都在
# Updater.Core 里，由单元套件覆盖），所以它本来就不该出现在报告里。
$requiredAssemblyRoots = @(
    'src\Cafe.Launcher.Core',
    'src\Cafe.Launcher',
    'src\Cafe.Launcher.UI',
    'src\Cafe.Launcher.Updater.Core'
)
$countedSourceFiles = @{}
foreach ($requiredRoot in $requiredAssemblyRoots) {
    $countedSourceFiles[$requiredRoot] = @{}
}

foreach ($reportPath in $reportPaths.Values) {
    [xml]$coverageXml = Get-Content -LiteralPath $reportPath -Raw
    $sourceRoots = @($coverageXml.coverage.sources.source |
        ForEach-Object { [string]$_ } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [IO.Path]::GetFullPath($_) })

    foreach ($package in @($coverageXml.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $relativePath = $class.filename -replace '[\\/]', [string][IO.Path]::DirectorySeparatorChar
            # Coverlet 报告相对 <sources> 根；Microsoft Code Coverage 直接写绝对路径。
            $candidates = if ([IO.Path]::IsPathRooted($relativePath)) {
                @($relativePath)
            }
            else {
                @($sourceRoots | ForEach-Object {
                    [IO.Path]::GetFullPath((Join-Path $_ $relativePath))
                })
            }
            $fullPath = $null
            foreach ($candidate in $candidates) {
                if (
                    $candidate.StartsWith($repositoryRootPrefix, [StringComparison]::OrdinalIgnoreCase) -and
                    (Test-Path -LiteralPath $candidate -PathType Leaf)
                ) {
                    $fullPath = $candidate
                    break
                }
            }

            if ($null -eq $fullPath) {
                continue
            }

            $extension = [IO.Path]::GetExtension($fullPath)
            $pathSegments = $relativePath.Split(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar)

            if (
                $extension -ne '.cs' -or
                $pathSegments -contains 'bin' -or
                $pathSegments -contains 'obj' -or
                $fullPath -match '[\\/]Resources[\\/]LauncherStrings\.Designer\.cs$'
            ) {
                continue
            }

            foreach ($requiredRoot in $requiredAssemblyRoots) {
                $requiredPrefix = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $requiredRoot)) +
                    [IO.Path]::DirectorySeparatorChar
                if ($fullPath.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    $countedSourceFiles[$requiredRoot][$fullPath] = $true
                    break
                }
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

foreach ($requiredRoot in $requiredAssemblyRoots) {
    $countedFileCount = $countedSourceFiles[$requiredRoot].Count
    if ($countedFileCount -eq 0) {
        throw ("Coverage report contains no counted source file from {0}. " -f $requiredRoot) +
            'The report <sources> root probably no longer resolves this project''s class filenames; ' +
            'fix the resolution in coverage.ps1 instead of letting the assembly drop out of the ratio.'
    }
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
# Printed every run so a baseline that has drifted below the measured floor is visible in the CI
# log instead of only surfacing when a later change quietly spends the slack.
Write-Output ("Baseline slack: lines {0:+0.00;-0.00;0.00}pp, branches {1:+0.00;-0.00;0.00}pp" -f (($lineRatio - $lineBaseline) * 100), (($branchRatio - $branchBaseline) * 100))
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
