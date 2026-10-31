param(
    [string]$OutputDirectory,
    [string]$LibMpvPath,
    [ValidateSet('Video', 'Blank', 'Finish', 'ReuseSurface', 'Production')]
    [string]$ProbeMode = 'Video',
    [ValidateRange(6, 30)] [int]$Cycles = 8,
    [ValidateRange(0, 1800)] [int]$SteadySeconds = 180,
    [double]$MaxRetainedGrowthMB = 32,
    [double]$MaxRetainedGpuGrowthMB = 32,
    [ValidateSet('Default', 'DirectComposition')]
    [string]$CompositionMode = 'Default',
    [switch]$CacheShell = $true,
    [switch]$ExerciseSettings,
    [string]$HeapDumpToolPath,
    [switch]$ContinueAfterMemoryFailure,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if ($ExerciseSettings -and $ProbeMode -ne 'Production') { throw 'ExerciseSettings requires Production mode.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository '.artifacts/video-background/retention' }
if (-not $LibMpvPath) { $LibMpvPath = Join-Path $repository '.artifacts/native/libmpv/win-x64/libmpv-2.dll' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$sampleFile = Join-Path $OutputDirectory 'samples.csv'
$commandFile = Join-Path $OutputDirectory 'samples.command'
$ackFile = Join-Path $OutputDirectory 'samples.ack'
$sequence = 0
$prototypeProcess = $null

function Read-LatestSample {
    $rows = @(Import-Csv -LiteralPath $sampleFile)
    if ($rows.Count -eq 0) { throw 'No prototype samples.' }
    return $rows[-1]
}

function Send-ProbeCommand([string]$Command) {
    $script:sequence++
    $message = "$script:sequence|$Command"
    [IO.File]::WriteAllText($commandFile, $message)
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        if ($prototypeProcess.HasExited) {
            if ($Command -eq 'close' -and $prototypeProcess.ExitCode -eq 0) { return }
            throw "Prototype exited while executing $Command (exit code $($prototypeProcess.ExitCode))."
        }
        if ((Test-Path -LiteralPath $ackFile) -and (Get-Content -LiteralPath $ackFile -Raw).Trim() -eq $message) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Prototype command timed out: $Command"
}

function Wait-ProbeSeconds([double]$Seconds) {
    $startSeconds = [double](Read-LatestSample).seconds
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds + 15)
    do {
        if ($prototypeProcess.HasExited) { throw 'Prototype exited during sampling.' }
        $latest = Read-LatestSample
        if ([double]$latest.seconds -ge $startSeconds + $Seconds) { return $latest }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Prototype sample clock stopped.'
}

try {
    & "$PSScriptRoot/Start-VideoBackgroundPrototype.ps1" -OutputDirectory $OutputDirectory `
        -VideoPath (Join-Path $repository '.artifacts/video-background/test-1080p30.mp4') `
        -LibMpvPath $LibMpvPath `
        -SkipBuild:$SkipBuild -CacheShell:$CacheShell -CompositionMode $CompositionMode -ProbeMode $ProbeMode
    $prototypeProcess = Get-Process -Id ([int](Get-Content (Join-Path $OutputDirectory 'process-id.txt')))
    # 保留进程句柄，确保这个非子进程的 ExitCode 在退出后仍可读取。
    $null = $prototypeProcess.Handle
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath $sampleFile) -or @(Import-Csv -LiteralPath $sampleFile).Count -lt 2) {
        if ($prototypeProcess.HasExited -or [DateTime]::UtcNow -gt $readyDeadline) { throw 'Prototype did not become ready.' }
        Start-Sleep -Milliseconds 250
    }
    $null = Wait-ProbeSeconds 10
    $released = [Collections.Generic.List[object]]::new()
    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        if ($cycle -gt 1) { Send-ProbeCommand 'recreate' }
        $active = Wait-ProbeSeconds 4
        if ($ProbeMode -ne 'Blank' -and
            ([int]$active.player_live -ne 1 -or [int]$active.renderer_live -ne 1 -or [double]$active.frames_per_second -le 0)) {
            throw "Cycle $cycle did not render active video."
        }
        if ($ExerciseSettings -and $cycle % 2 -eq 0) { Send-ProbeCommand 'save' }
        Send-ProbeCommand 'release'
        if ($ExerciseSettings) {
            if ($cycle % 2 -eq 0) {
                Send-ProbeCommand 'discard'
                $null = Wait-ProbeSeconds 4
                Send-ProbeCommand 'release'
            }
            else { Send-ProbeCommand 'save' }
        }
        $null = Wait-ProbeSeconds 2
        Send-ProbeCommand 'gc'
        $row = Wait-ProbeSeconds 2
        if ([int]$row.player_live -ne 0 -or [int]$row.renderer_live -ne 0 -or [int]$row.arena_blocks -ne 0) {
            throw "Cycle $cycle left owned native resources alive."
        }
        $gpu = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory |
            Where-Object Name -like "pid_$($prototypeProcess.Id)_*")
        if ($gpu.Count -eq 0) { throw 'GPU process-memory counters are unavailable; retention is unverified.' }
        $entry = [pscustomobject]@{
            cycle = $cycle
            private_mb = [double]$row.private_mb
            managed_mb = [double]$row.gc_total_memory_mb
            gc_committed_mb = [double]$row.gc_committed_mb
            handles = [int]$row.process_handles
            gpu_dedicated_mb = ($gpu | Measure-Object DedicatedUsage -Sum).Sum / 1MB
            player_live = [int]$row.player_live
            renderer_live = [int]$row.renderer_live
            arena_blocks = [int]$row.arena_blocks
        }
        $released.Add($entry)
        $released | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'released.csv') -NoTypeInformation -Encoding utf8
        Write-Host ($entry | ConvertTo-Json -Compress)
        if ($HeapDumpToolPath -and ($cycle -eq 2 -or $cycle -eq $Cycles)) {
            $dumpPath = Join-Path $OutputDirectory "cycle-$cycle.gcdump"
            & $HeapDumpToolPath collect --process-id $prototypeProcess.Id --output $dumpPath --timeout 30
            if ($LASTEXITCODE -ne 0) { throw 'GC dump collection failed.' }
            & $HeapDumpToolPath report $dumpPath | Set-Content -LiteralPath "$dumpPath.txt" -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw 'GC dump report failed.' }
        }
    }
    # Discard two allocator/device warm-up cycles; compare windows, not individual noisy endpoints.
    $stable = @($released | Select-Object -Skip 2)
    $growth = ($stable | Select-Object -Last 2 | Measure-Object private_mb -Average).Average `
        - ($stable | Select-Object -First 2 | Measure-Object private_mb -Average).Average
    $gpuGrowth = ($stable | Select-Object -Last 2 | Measure-Object gpu_dedicated_mb -Average).Average `
        - ($stable | Select-Object -First 2 | Measure-Object gpu_dedicated_mb -Average).Average
    $result = [ordered]@{
        ProbeMode = $ProbeMode
        ExerciseSettings = [bool]$ExerciseSettings
        RuntimeSha256 = (Get-FileHash -LiteralPath $LibMpvPath -Algorithm SHA256).Hash
        Cycles = $Cycles
        RetainedPrivateGrowthMB = [Math]::Round($growth, 3)
        MaxRetainedGrowthMB = $MaxRetainedGrowthMB
        RetainedGpuGrowthMB = [Math]::Round($gpuGrowth, 3)
        MaxRetainedGpuGrowthMB = $MaxRetainedGpuGrowthMB
        NativeResourcesReleasedEveryCycle = $true
        MemoryBoundsPassed = $growth -le $MaxRetainedGrowthMB -and $gpuGrowth -le $MaxRetainedGpuGrowthMB
        Passed = $false
    }
    $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory 'retention-result.json')
    if (-not $result.MemoryBoundsPassed -and -not $ContinueAfterMemoryFailure) {
        throw 'Repeated release/recreate retained too much private or GPU memory.'
    }
    if ($SteadySeconds -gt 0) {
        Send-ProbeCommand 'recreate'
        $null = Wait-ProbeSeconds 10
        Send-ProbeCommand 'gc'
        $steadyStart = Wait-ProbeSeconds 2
        $null = Wait-ProbeSeconds $SteadySeconds
        Send-ProbeCommand 'gc'
        $steadyEnd = Wait-ProbeSeconds 2
        [ordered]@{ Start = $steadyStart; End = $steadyEnd } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath (Join-Path $OutputDirectory 'steady-result.json') -Encoding utf8
        Send-ProbeCommand 'release'
        $null = Wait-ProbeSeconds 2
        Send-ProbeCommand 'gc'
        $postSteady = Wait-ProbeSeconds 2
        $postSteady | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'post-steady-release.json') -Encoding utf8
    }
    Send-ProbeCommand 'close'
    if (-not $prototypeProcess.WaitForExit(15000)) { throw 'Prototype failed to close cleanly.' }
    if ($prototypeProcess.ExitCode -ne 0) { throw "Prototype exited with code $($prototypeProcess.ExitCode)." }
    $result.ExitCode = $prototypeProcess.ExitCode
    $result.Passed = $result.MemoryBoundsPassed
    $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory 'retention-result.json')
    if (-not $result.Passed) { throw 'Memory bounds failed; post-failure observations were recorded without relaxing the gate.' }
}
finally {
    if ($prototypeProcess) {
        if (-not $prototypeProcess.HasExited) { $prototypeProcess.Kill(); $prototypeProcess.WaitForExit() }
        $prototypeProcess.Dispose()
    }
}
