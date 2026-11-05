param(
    [Parameter(Mandatory)][ValidateSet('linux-x64', 'osx-arm64')][string]$Rid,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'native/libmpv/unix-manifest.json') | ConvertFrom-Json -AsHashtable
$destination = Join-Path $OutputDirectory "libmpv/$Rid"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$allowed = @($manifest.runtimes[$Rid].files.Keys) + @('unix-manifest.json')
if ($Rid -eq 'linux-x64') { $allowed += @('libasound.so.2', 'THIRD-PARTY-alsa.txt') }
foreach ($existing in @(Get-ChildItem -LiteralPath $destination -Force)) {
    if ($existing.Name -cnotin $allowed -or $existing.PSIsContainer -or
        ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected item in video runtime directory: $($existing.Name)"
    }
}
foreach ($entry in $manifest.runtimes[$Rid].files.GetEnumerator()) {
    $path = Join-Path $destination $entry.Key
    # Download to a unique temporary file and verify before replacing the destination.
    $temporary = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        & curl --fail --location --retry 3 --connect-timeout 15 --max-time 180 --output $temporary "$($manifest.baseUrl)/$($entry.Key)"
        if ($LASTEXITCODE -ne 0) { throw "Video runtime download failed: $($entry.Key)" }
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $entry.Value) {
            throw "Video runtime SHA-256 mismatch: $($entry.Key)"
        }
        Move-Item -LiteralPath $temporary -Destination $path -Force
    }
    finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
}
if ($Rid -eq 'linux-x64') {
    $alsa = $manifest.runtimes[$Rid].alsaPackage
    $destinationRoot = [IO.Path]::GetFullPath($destination)
    $work = [IO.Path]::GetFullPath((Join-Path $destinationRoot ".alsa-$([Guid]::NewGuid().ToString('N'))"))
    if (-not $work.StartsWith($destinationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) {
        throw 'ALSA extraction directory is outside the runtime directory.'
    }
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        $archive = Join-Path $work 'alsa.deb'
        & curl --fail --location --retry 3 --connect-timeout 15 --max-time 180 --output $archive $alsa.url
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $alsa.sha256) {
            throw 'ALSA package download failed or SHA-256 mismatch.'
        }
        if ($IsWindows) {
            & tar -xf $archive -C $work data.tar.xz
        }
        else {
            # A deb is an ar archive; GNU tar cannot read it directly.
            Push-Location $work
            try { & ar x $archive data.tar.xz }
            finally { Pop-Location }
        }
        if ($LASTEXITCODE -ne 0) { throw 'ALSA package extraction failed.' }
        $payload = Join-Path $work 'data.tar.xz'
        if ($IsWindows) {
            # Windows' built-in tar does not always include an xz decoder.
            $extractor = Get-Command 7z, 7zz -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $extractor) { throw 'Install 7-Zip (7z or 7zz) to restore the Linux audio fallback on Windows.' }
            & $extractor.Source x -y $payload "-o$work" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'ALSA payload decompression failed.' }
            $payload = Join-Path $work 'data.tar'
        }
        & tar -xf $payload -C $work './usr/lib/x86_64-linux-gnu/libasound.so.2.0.0' './usr/share/doc/libasound2/copyright'
        if ($LASTEXITCODE -ne 0) { throw 'ALSA files extraction failed.' }
        $library = Join-Path $work 'usr/lib/x86_64-linux-gnu/libasound.so.2.0.0'
        $copyright = Join-Path $work 'usr/share/doc/libasound2/copyright'
        if ((Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -ne $alsa.librarySha256 -or
            (Get-FileHash -LiteralPath $copyright -Algorithm SHA256).Hash -ne $alsa.copyrightSha256) {
            throw 'Extracted ALSA file SHA-256 mismatch.'
        }
        Copy-Item -LiteralPath $library -Destination (Join-Path $destinationRoot 'libasound.so.2') -Force
        Copy-Item -LiteralPath $copyright -Destination (Join-Path $destinationRoot 'THIRD-PARTY-alsa.txt') -Force
    }
    finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
Copy-Item -LiteralPath (Join-Path $root 'native/libmpv/unix-manifest.json') -Destination $destination -Force
