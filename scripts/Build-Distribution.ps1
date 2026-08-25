[CmdletBinding()]
param(
    [string]$Tag
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj"
$InstallerScript = Join-Path $RootDir "installer\Cafe.Launcher.Avalonia.iss"
$ArtifactsDir = Join-Path $RootDir "artifacts"
$PublishDir = Join-Path $ArtifactsDir "publish"
$DistributionDir = Join-Path $ArtifactsDir "distribution"

[xml]$project = Get-Content -Raw -LiteralPath $ProjectPath
$versionNodes = @($project.SelectNodes("/Project/PropertyGroup/VersionPrefix"))
$fileVersionNodes = @($project.SelectNodes("/Project/PropertyGroup/FileVersion"))

if ($versionNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versionNodes[0].InnerText)) {
    throw "Exactly one non-empty VersionPrefix is required."
}

if ($fileVersionNodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($fileVersionNodes[0].InnerText)) {
    throw "Exactly one non-empty FileVersion is required."
}

$versionPrefix = $versionNodes[0].InnerText.Trim()
$fileVersion = $fileVersionNodes[0].InnerText.Trim()
$Tag = if ([string]::IsNullOrWhiteSpace($Tag)) { "v$versionPrefix" } else { $Tag }

if ($Tag -cne "v$versionPrefix") {
    throw "Tag '$Tag' does not exactly match VersionPrefix '$versionPrefix'."
}

Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $DistributionDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $PublishDir, $DistributionDir | Out-Null

& dotnet restore $ProjectPath -r win-x64 | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

& dotnet publish $ProjectPath -c Release -r win-x64 --no-restore -o $PublishDir | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
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

$standaloneName = "Cafe.Launcher.Avalonia_${Tag}_standalone.zip"
$setupName = "Cafe.Launcher.Avalonia_${Tag}_setup.exe"
$setupBaseName = "Cafe.Launcher.Avalonia_${Tag}_setup"
$standalonePath = Join-Path $DistributionDir $standaloneName
$setupPath = Join-Path $DistributionDir $setupName

Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $standalonePath

& $isccPath `
    "-dAPP_VERSION=$versionPrefix" `
    "-dAPP_FILE_VERSION=$fileVersion" `
    "-dPUBLISH_GLOB=$publishGlob" `
    "-o$DistributionDir" `
    "-f$setupBaseName" `
    $InstallerScript | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed."
}

if (-not (Test-Path -LiteralPath $standalonePath -PathType Leaf)) {
    throw "Standalone archive was not created: $standalonePath"
}

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Setup executable was not created: $setupPath"
}

[pscustomobject]@{
    Tag = $Tag
    Version = $versionPrefix
    StandalonePath = $standalonePath
    SetupPath = $setupPath
}
