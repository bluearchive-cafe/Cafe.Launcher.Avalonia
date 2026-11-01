[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $repository 'native/libmpv/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

foreach ($file in @('Cafe.Launcher.exe', 'Cafe.Launcher.Updater.exe', 'libmpv/win-x64/libmpv-2.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $file) -PathType Leaf)) {
        throw "Windows publish output is missing required file: $file"
    }
}

$library = Join-Path $PublishDir 'libmpv/win-x64/libmpv-2.dll'
if ((Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash -ne $manifest.librarySha256) {
    throw 'Windows publish output contains an unexpected libmpv DLL (SHA-256 mismatch).'
}

# The library, provenance and license texts must travel together, including when
# packaging an existing output with -SkipPublish or building an installer separately.
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repository 'native/libmpv') -File) {
    $destination = Join-Path $PublishDir "libmpv/$($source.Name)"
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        throw "Windows publish output is missing libmpv disclosure: $($source.Name)"
    }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash) {
        throw "Windows publish output contains an unexpected libmpv disclosure: $($source.Name)"
    }
}

Write-Host 'Windows publish output: launcher, updater, pinned libmpv and disclosures verified.'
