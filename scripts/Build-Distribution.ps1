[CmdletBinding()]
param(
    [string]$Tag
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "Cafe.Launcher.Avalonia.csproj"
$InstallerScript = Join-Path $RootDir "installer\Cafe.Launcher.Avalonia.nsi"
$ArtifactsDir = Join-Path $RootDir "artifacts"
$PublishDir = Join-Path $ArtifactsDir "publish"
$DistributionDir = Join-Path $ArtifactsDir "distribution"
$GeneratedDir = Join-Path $ArtifactsDir "generated"

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
Remove-Item -LiteralPath $GeneratedDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $PublishDir, $DistributionDir, $GeneratedDir | Out-Null

& dotnet restore $ProjectPath -r win-x64 | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed."
}

& dotnet publish $ProjectPath -c Release -r win-x64 --no-restore -o $PublishDir | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

function Assert-NsisSafeRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ($RelativePath.Contains('$') -or
        $RelativePath.Contains('"') -or
        $RelativePath.Contains("`r") -or
        $RelativePath.Contains("`n")) {
        throw "Publish path cannot be represented safely in NSIS: $RelativePath"
    }
}

$publishRoot = [System.IO.Path]::GetFullPath($PublishDir)
$files = Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Sort-Object FullName
$directories = Get-ChildItem -LiteralPath $publishRoot -Directory -Recurse |
    Sort-Object { $_.FullName.Length } -Descending

$uninstallLines = [System.Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($publishRoot, $file.FullName)
    Assert-NsisSafeRelativePath $relative
    $uninstallLines.Add(('Delete "$INSTDIR\{0}"' -f $relative))
}

$uninstallLines.Add('Delete "$INSTDIR\.cafe-launcher-install"')
$uninstallLines.Add('Delete "$INSTDIR\Uninstall.exe"')

foreach ($directory in $directories) {
    $relative = [System.IO.Path]::GetRelativePath($publishRoot, $directory.FullName)
    Assert-NsisSafeRelativePath $relative
    $uninstallLines.Add(('RMDir "$INSTDIR\{0}"' -f $relative))
}

$uninstallLines.Add('RMDir "$INSTDIR"')

$uninstallInclude = Join-Path $GeneratedDir "UninstallFiles.nsh"
Set-Content -LiteralPath $uninstallInclude -Value $uninstallLines -Encoding utf8NoBOM

$standaloneName = "Cafe.Launcher.Avalonia_${Tag}_standalone.zip"
$setupName = "Cafe.Launcher.Avalonia_${Tag}_setup.exe"
$standalonePath = Join-Path $DistributionDir $standaloneName
$setupPath = Join-Path $DistributionDir $setupName

Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $standalonePath

$makeNsis = Get-Command makensis.exe -ErrorAction SilentlyContinue
if ($null -eq $makeNsis) {
    $makeNsis = Get-Command makensis -ErrorAction SilentlyContinue
}

if ($null -eq $makeNsis) {
    throw "NSIS compiler was not found. Install NSIS 3 and add makensis to PATH."
}

$publishGlob = Join-Path $PublishDir "*"

$definePrefix = if ($IsWindows) { "/D" } else { "-D" }

& $makeNsis.Source `
    "${definePrefix}APP_VERSION=$versionPrefix" `
    "${definePrefix}FILE_VERSION=$fileVersion" `
    "${definePrefix}PUBLISH_GLOB=$publishGlob" `
    "${definePrefix}UNINSTALL_INCLUDE=$uninstallInclude" `
    "${definePrefix}OUTPUT_FILE=$setupPath" `
    $InstallerScript | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "NSIS compilation failed."
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
