param(
    [Parameter(Mandatory)]
    [int]$ProcessId,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [ValidateRange(1, 3600)]
    [int]$Seconds = 120,
    [ValidatePattern('^[A-Za-z0-9_-]+\.csv$')]
    [string]$CsvName = 'gpu.csv'
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$gpuPath = Join-Path $OutputDirectory $CsvName
$gpuWriter = [IO.StreamWriter]::new($gpuPath, $false)
$gpuWriter.WriteLine('utc,phase,gpu_3d_percent,gpu_decode_percent,gpu_copy_percent,dedicated_mb,shared_mb')
$gpuWriter.AutoFlush = $true
$samplesPath = Join-Path $OutputDirectory 'samples.csv'
$hardware = [ordered]@{
    SampledAtUtc = [DateTime]::UtcNow.ToString('O')
    ProcessId = $ProcessId
    Cpu = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfLogicalProcessors)
    Gpu = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion)
}
$hardware | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'hardware.json')
$clock = [Diagnostics.Stopwatch]::StartNew()
$rows = [Collections.Generic.List[object]]::new()
try {
    while ($clock.Elapsed.TotalSeconds -lt $Seconds -and (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
        $phase = 'startup'
        if (Test-Path -LiteralPath $samplesPath) {
            $lastSample = Get-Content -LiteralPath $samplesPath -Tail 1
            if ($lastSample -and $lastSample -notlike 'seconds,*') { $phase = $lastSample.Split(',')[1] }
        }
        $engines = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine |
            Where-Object Name -like "pid_$($ProcessId)_*")
        $memory = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory |
            Where-Object Name -like "pid_$($ProcessId)_*")
        $row = [pscustomobject]@{
            utc = [DateTime]::UtcNow.ToString('O')
            phase = $phase
            gpu_3d_percent = ($engines | Where-Object Name -like '*engtype_3D' | Measure-Object UtilizationPercentage -Sum).Sum
            gpu_decode_percent = ($engines | Where-Object Name -like '*engtype_VideoDecode' | Measure-Object UtilizationPercentage -Sum).Sum
            gpu_copy_percent = ($engines | Where-Object Name -like '*engtype_Copy' | Measure-Object UtilizationPercentage -Sum).Sum
            dedicated_mb = ($memory | Measure-Object DedicatedUsage -Sum).Sum / 1MB
            shared_mb = ($memory | Measure-Object SharedUsage -Sum).Sum / 1MB
        }
        $rows.Add($row)
        $csv = @($row | ConvertTo-Csv -NoTypeInformation)
        $gpuWriter.WriteLine($csv[1])
        Start-Sleep -Seconds 1
    }
}
finally {
    $gpuWriter.Dispose()
    $rows | Group-Object phase | ForEach-Object {
        [pscustomobject]@{
            Phase = $_.Name
            Samples = $_.Count
            Gpu3DMean = ($_.Group | Measure-Object gpu_3d_percent -Average).Average
            GpuDecodeMean = ($_.Group | Measure-Object gpu_decode_percent -Average).Average
            DedicatedMBMean = ($_.Group | Measure-Object dedicated_mb -Average).Average
        }
    } | Format-Table
}
