[CmdletBinding()]
param(
    [string]$Tag,
    [string]$PublishDir,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$InstallerScript = Join-Path $RootDir "installer/Cafe.Launcher.Avalonia.iss"

$version = & (Join-Path $ScriptDir "Read-LauncherVersion.ps1") -Tag $Tag
$Tag = $version.Tag

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $RootDir "artifacts/publish/win-x64"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RootDir "artifacts/distribution"
}

if (-not (Test-Path -LiteralPath $PublishDir -PathType Container) -or
    -not (@(Get-ChildItem -LiteralPath $PublishDir -Force).Count -gt 0)) {
    throw "Publish output is required at '$PublishDir'. Run scripts/Build-Distribution.ps1 first."
}

function Assert-InnoSafeDefineValue {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    if ($Value.Contains('"') -or
        $Value.Contains("`r") -or
        $Value.Contains("`n")) {
        throw "Publish path cannot be represented safely as an ISCC define: $Value"
    }
}

function Resolve-Iscc {
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6.3 or newer and add it to PATH."
}

$isccPath = Resolve-Iscc
$isccVersionLine = (& $isccPath --version 2>$null | Select-Object -First 1)
$isccVersion = $null
if ($isccVersionLine -match '^(\d+\.\d+(?:\.\d+)?)') {
    $isccVersion = [version]$Matches[1]
}

if ($null -eq $isccVersion -or $isccVersion -lt [version]"6.3") {
    throw "Inno Setup 6.3 or newer is required (found: '$isccVersionLine')."
}

$publishRoot = [System.IO.Path]::GetFullPath($PublishDir)
$publishGlob = Join-Path $publishRoot "*"
Assert-InnoSafeDefineValue $publishGlob

[void][System.IO.Directory]::CreateDirectory($OutputDir)

$setupName = "Cafe.Launcher.Avalonia_${Tag}_setup.exe"
$setupBaseName = "Cafe.Launcher.Avalonia_${Tag}_setup"
$setupPath = Join-Path $OutputDir $setupName

& $isccPath `
    "-dAPP_VERSION=$($version.VersionPrefix)" `
    "-dAPP_FILE_VERSION=$($version.FileVersion)" `
    "-dPUBLISH_GLOB=$publishGlob" `
    "-o$OutputDir" `
    "-f$setupBaseName" `
    $InstallerScript | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Setup executable was not created: $setupPath"
}

[pscustomobject]@{
    Tag = $Tag
    Version = $version.VersionPrefix
    SetupPath = $setupPath
}
