param(
    [string]$OutputDirectory,
    [string]$Proxy
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This prototype preparation script targets Windows x64.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts/video-background' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive = Join-Path $OutputDirectory 'mpv-dev.7z'
$url = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/mpv-dev-x86_64-20261021-git-e471496c56.7z'
$sha256 = 'EFC719EFF3DC5954B4A5EFC800632808BCEC01CC9034940DD14E5EB7FE9EA633'
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sha256) {
    $arguments = @('--fail', '--location', '--retry', '3', '--retry-all-errors', '--connect-timeout', '15', '--max-time', '180')
    if ($Proxy) { $arguments += @('--proxy', $Proxy) }
    & curl.exe @arguments $url --output $archive
    if ($LASTEXITCODE -ne 0) { throw 'libmpv download failed. Supply an existing DLL with Start-VideoBackgroundPrototype.ps1 -LibMpvPath.' }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $sha256) { throw 'libmpv archive SHA-256 mismatch.' }
$native = Join-Path $OutputDirectory 'native'
$nativeDll = Join-Path $native 'libmpv-2.dll'
$nativeSha256 = '04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A'
if (-not (Test-Path -LiteralPath $nativeDll) -or (Get-FileHash -LiteralPath $nativeDll -Algorithm SHA256).Hash -ne $nativeSha256) {
    & 7z x -y $archive "-o$native"
    if ($LASTEXITCODE -ne 0) { throw 'libmpv extraction failed. Install 7-Zip or extract the verified archive manually.' }
}
if ((Get-FileHash -LiteralPath $nativeDll -Algorithm SHA256).Hash -ne $nativeSha256) { throw 'libmpv DLL SHA-256 mismatch.' }

$video = Join-Path $OutputDirectory 'test-1080p30.mp4'
if (-not (Test-Path -LiteralPath $video)) {
    & ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=1920x1080:rate=30' `
        -t 20 -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -an -y $video
    if ($LASTEXITCODE -ne 0) { throw 'Test video generation failed. Supply an existing video with -VideoPath.' }
}
[ordered]@{ Url = $url; ArchiveSha256 = $sha256; Video = 'FFmpeg testsrc2, H.264, yuv420p, 1920x1080, 30 fps, 20 s, no audio' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'input-manifest.json') -Encoding utf8
Write-Host "Ready: pwsh ./scripts/Start-VideoBackgroundPrototype.ps1 -OutputDirectory '$OutputDirectory' -GpuSampleSeconds 180"
