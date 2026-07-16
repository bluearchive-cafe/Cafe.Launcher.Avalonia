$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$threshold = 0.70
$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Coverage'

$runsettings = Join-Path $PSScriptRoot 'coverage.runsettings'
$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)

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

foreach ($projectInfo in $projects) {
    $project = $projectInfo.Project
    $projectResults = $projectInfo.ResultsDirectory

    New-Item -ItemType Directory -Path $projectResults -Force | Out-Null

    dotnet test $project -c Debug --no-restore --settings $runsettings --collect:'XPlat Code Coverage' --results-directory $projectResults
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $reports = @(Get-ChildItem -LiteralPath $projectResults -Recurse -Filter 'coverage.cobertura.xml')
    if ($reports.Count -ne 1) {
        throw "Expected exactly one Cobertura report in '$projectResults', found $($reports.Count)."
    }

    $reportPaths[$projectInfo.Name] = $reports[0].FullName
}

$lineCoverage = @{}
$branchCoverage = @{}

foreach ($reportPath in $reportPaths.Values) {
    [xml]$coverageXml = Get-Content -LiteralPath $reportPath -Raw

    foreach ($package in @($coverageXml.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $relativePath = $class.filename -replace '[\\/]', [string][IO.Path]::DirectorySeparatorChar
            $fullPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relativePath))
            $extension = [IO.Path]::GetExtension($fullPath)

            if (
                $extension -ne '.cs' -or
                -not $fullPath.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -or
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

exit 0
