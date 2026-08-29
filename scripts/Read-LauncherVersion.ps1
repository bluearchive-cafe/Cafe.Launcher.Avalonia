[CmdletBinding()]
param(
    [string]$Tag
)

$ErrorActionPreference = "Stop"

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj"

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

[pscustomobject]@{
    ProjectPath = $ProjectPath
    Tag = $Tag
    VersionPrefix = $versionPrefix
    FileVersion = $fileVersion
}
