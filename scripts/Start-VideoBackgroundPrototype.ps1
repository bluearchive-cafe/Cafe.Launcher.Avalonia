param(
    [string]$VideoPath,
    [string]$LibMpvPath,
    [string]$OutputDirectory,
    [ValidateSet('Video', 'Blank', 'Finish', 'ReuseSurface', 'Production')]
    [string]$ProbeMode = 'Video',
    [ValidateRange(0, 3600)]
    [int]$GpuSampleSeconds = 0,
    [switch]$MinimalShell,
    [switch]$CacheShell = $true,
    [ValidateSet('Default', 'DirectComposition')]
    [string]$CompositionMode = 'Default',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository '.artifacts/video-background' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $VideoPath) { $VideoPath = Join-Path $OutputDirectory 'test-1080p30.mp4' }
if (-not $LibMpvPath) { $LibMpvPath = Join-Path $OutputDirectory 'native/libmpv-2.dll' }
$VideoPath = (Resolve-Path -LiteralPath $VideoPath).Path
$LibMpvPath = (Resolve-Path -LiteralPath $LibMpvPath).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if (-not $SkipBuild) {
    Push-Location $repository
    try {
        & "$repository/build.ps1"
        if ($LASTEXITCODE -ne 0) { throw 'Debug build failed.' }
    }
    finally { Pop-Location }
}

# A distinct apphost name also keeps desktop automation and taskbar identity separate.
$output = Join-Path $repository 'src/Cafe.Launcher/bin/Debug/net10.0'
$runDirectory = Join-Path $OutputDirectory 'app'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $output | Copy-Item -Destination $runDirectory -Recurse -Force
if ($ProbeMode -eq 'Production') {
    $productionLibrary = Join-Path $runDirectory 'libmpv/win-x64/libmpv-2.dll'
    if ((Get-FileHash -LiteralPath $productionLibrary -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $LibMpvPath -Algorithm SHA256).Hash) {
        throw 'Production runtime differs from the requested probe runtime.'
    }
}
$prototypeHost = Join-Path $runDirectory 'Cafe.VideoPrototype.exe'
Copy-Item -LiteralPath (Join-Path $runDirectory 'Cafe.Launcher.exe') -Destination $prototypeHost -Force
$dataRoot = Join-Path $OutputDirectory 'userdata'
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$settings = Join-Path $dataRoot 'settings.json'
if (-not (Test-Path -LiteralPath $settings)) {
    $initialSettings = @{ language = 'zh-Hans'; backgroundSource = 'bundled'; motionMode = 'full'; enableStartupUpdateCheck = $false }
    if ($ProbeMode -eq 'Production') {
        $initialSettings.backgroundSource = 'custom'
        $initialSettings.customBackgroundPath = $VideoPath
    }
    $initialSettings | ConvertTo-Json | Set-Content -LiteralPath $settings -Encoding utf8
}

$environment = @{
    CAFE_VIDEO_PROTOTYPE_PROBE_MODE = $ProbeMode
    CAFE_VIDEO_PROTOTYPE = '1'
    CAFE_VIDEO_PROTOTYPE_MINIMAL = $(if ($MinimalShell) { '1' } else { '0' })
    CAFE_VIDEO_PROTOTYPE_CACHE_SHELL = $(if ($CacheShell) { '1' } else { '0' })
    CAFE_VIDEO_PROTOTYPE_COMPOSITION = $CompositionMode
    CAFE_VIDEO_PROTOTYPE_LIBRARY = $LibMpvPath
    CAFE_VIDEO_PROTOTYPE_FILE = $VideoPath
    CAFE_VIDEO_PROTOTYPE_OUTPUT = Join-Path $OutputDirectory 'samples.csv'
    CAFE_LAUNCHER_TEST_USER_DATA_DIRECTORY = $dataRoot
}
$start = [Diagnostics.ProcessStartInfo]::new($prototypeHost)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $repository
foreach ($key in $environment.Keys) { $start.Environment[$key] = $environment[$key] }
$prototypeProcess = [Diagnostics.Process]::Start($start)
$prototypeProcess.Id | Set-Content -LiteralPath (Join-Path $OutputDirectory 'process-id.txt')
Write-Host "Prototype PID: $($prototypeProcess.Id)"
Write-Host "Samples: $OutputDirectory"
Write-Host 'Ctrl+Shift+B: static/video; S: settings; C: confirmation; D: synthetic DPI; R: resize; T: tray hide, restore after 10 seconds.'

if ($GpuSampleSeconds -gt 0) {
    & "$PSScriptRoot/Measure-VideoBackgroundPrototype.ps1" -ProcessId $prototypeProcess.Id `
        -OutputDirectory $OutputDirectory -Seconds $GpuSampleSeconds
}
