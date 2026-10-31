param([string]$Proxy)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $repository 'native/libmpv/manifest.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repository '.artifacts/native/libmpv'
$destination = Join-Path $cache 'win-x64'
$library = Join-Path $destination 'libmpv-2.dll'
New-Item -ItemType Directory -Path $destination -Force | Out-Null

# One MSBuild graph normally builds UI only once. A file lock also serializes
# independent IDE / CLI builds so they cannot consume a partially extracted DLL.
$lock = $null
$deadline = [DateTime]::UtcNow.AddMinutes(2)
while ($null -eq $lock) {
    try { $lock = [IO.File]::Open((Join-Path $cache 'restore.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $deadline) { throw }
        Start-Sleep -Milliseconds 100
    }
}
try {
    if ((Test-Path -LiteralPath $library) -and
        (Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -eq $manifest.librarySha256) { return }
    $archive = Join-Path $cache "$($manifest.release).7z"
    if (-not (Test-Path -LiteralPath $archive) -or
        (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.archiveSha256) {
        $arguments = @('--fail', '--location', '--retry', '3', '--connect-timeout', '15', '--max-time', '180')
        if ($Proxy) { $arguments += @('--proxy', $Proxy) }
        & curl @arguments $manifest.url --output $archive
        if ($LASTEXITCODE -ne 0) { throw 'Pinned LGPL libmpv download failed.' }
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $manifest.archiveSha256) {
        throw 'libmpv archive SHA-256 mismatch.'
    }
    $extractor = Get-Command 7z, 7zz -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $extractor) { throw 'Install 7-Zip (7z or 7zz) to restore the Windows video runtime.' }
    & $extractor.Source x -y $archive "-o$destination" libmpv-2.dll | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'libmpv extraction failed.' }
    if ((Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -ne $manifest.librarySha256) {
        throw 'libmpv DLL SHA-256 mismatch.'
    }
    Write-Host "Restored LGPL libmpv $($manifest.release) (win-x64)."
}
finally { $lock.Dispose() }
