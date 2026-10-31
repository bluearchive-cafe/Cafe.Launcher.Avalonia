param(
    [string]$OutputDirectory,
    [ValidateRange(3, 300)] [int]$WarmupSeconds = 5,
    [ValidateRange(5, 1800)] [int]$MeasureSeconds = 20,
    [double]$MaxCpuPercent = 2,
    [double]$MaxGrowthMBPerMinute = 60,
    [switch]$MinimalShell,
    [switch]$CacheShell = $true,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository '.artifacts/video-background/performance' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$video = Join-Path $repository '.artifacts/video-background/test-1080p30.mp4'
$library = Join-Path $repository '.artifacts/video-background/native/libmpv-2.dll'
& "$PSScriptRoot/Start-VideoBackgroundPrototype.ps1" -OutputDirectory $OutputDirectory `
    -VideoPath $video -LibMpvPath $library -SkipBuild:$SkipBuild -MinimalShell:$MinimalShell -CacheShell:$CacheShell
$prototypeId = [int](Get-Content -LiteralPath (Join-Path $OutputDirectory 'process-id.txt'))
$prototypeProcess = Get-Process -Id $prototypeId
$clock = [Diagnostics.Stopwatch]::StartNew()
$sampleFile = Join-Path $OutputDirectory 'samples.csv'
try {
    while ($clock.Elapsed.TotalSeconds -lt $WarmupSeconds + $MeasureSeconds + 10) {
        if ($prototypeProcess.HasExited) { throw 'Prototype exited before the performance window completed.' }
        if (Test-Path -LiteralPath $sampleFile) {
            $allRows = @(Import-Csv -LiteralPath $sampleFile)
            if ($allRows.Count -gt 0 -and [double]$allRows[-1].seconds -ge $WarmupSeconds + $MeasureSeconds) { break }
        }
        Start-Sleep -Milliseconds 500
    }
    $rows = @(Import-Csv -LiteralPath $sampleFile | Where-Object { [double]$_.seconds -ge $WarmupSeconds })
    if ($rows.Count -lt $MeasureSeconds / 2) { throw 'Too few performance samples.' }
    $cpu = ($rows | Measure-Object cpu_percent_machine -Average).Average
    $elapsed = [double]$rows[-1].seconds - [double]$rows[0].seconds
    $growth = ([double]$rows[-1].private_mb - [double]$rows[0].private_mb) / $elapsed * 60
    $result = [ordered]@{
        Samples = $rows.Count
        MeanCpuPercentMachine = [Math]::Round($cpu, 3)
        PrivateGrowthMBPerMinute = [Math]::Round($growth, 3)
        PrivateStartMB = [double]$rows[0].private_mb
        PrivateEndMB = [double]$rows[-1].private_mb
        MeanFps = [Math]::Round(($rows | Measure-Object frames_per_second -Average).Average, 3)
        CpuBudget = $MaxCpuPercent
        GrowthBudget = $MaxGrowthMBPerMinute
        Passed = $cpu -le $MaxCpuPercent -and $growth -le $MaxGrowthMBPerMinute
    }
    $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory 'result.json')
    if (-not $result.Passed) { throw 'Video prototype exceeded the CPU or steady-state private-memory growth budget.' }
}
finally {
    # Only the exact process started above is terminated; never touch the installed launcher.
    if (-not $prototypeProcess.HasExited) { $prototypeProcess.Kill(); $prototypeProcess.WaitForExit() }
    $prototypeProcess.Dispose()
}
