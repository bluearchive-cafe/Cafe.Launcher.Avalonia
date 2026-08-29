[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$OutputPath
)

# Regenerates THIRD-PARTY-NOTICES.md from the resolved NuGet dependency graph.
# Reads license metadata from each package's nuspec in the global packages folder,
# so the output always reflects what actually restores. Run after adding, removing,
# or upgrading a dependency and commit the result.

$ErrorActionPreference = "Stop"

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $RootDir "src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RootDir "THIRD-PARTY-NOTICES.md"
}

$globalPackagesFolder = & dotnet nuget locals global-packages --list
if ($globalPackagesFolder -match 'global-packages:\s*(.+)$') {
    $globalPackagesFolder = $Matches[1].Trim()
}
else {
    throw "Could not determine the NuGet global packages folder."
}

$assetsPath = Join-Path (Split-Path -Parent $ProjectPath) "obj/project.assets.json"
if (-not (Test-Path -LiteralPath $assetsPath)) {
    throw "project.assets.json not found at '$assetsPath'. Run 'dotnet restore' on the project first."
}

$assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json

$libraries = @{}
foreach ($property in $assets.libraries.PSObject.Properties) {
    # Key format: "PackageName/Version"
    $name, $version = $property.Name -split '/', 2
    if ($property.Value.type -ne 'package') {
        continue
    }
    $libraries[$name] = [pscustomobject]@{
        Name = $name
        Version = $version
    }
}

$entries = @()
foreach ($name in ($libraries.Keys | Sort-Object -CaseSensitive:$false)) {
    $package = $libraries[$name]
    $nuspecPath = Join-Path $globalPackagesFolder ($name.ToLowerInvariant() + "/" + $package.version.ToLowerInvariant() + "/" + $name.ToLowerInvariant() + ".nuspec")
    if (-not (Test-Path -LiteralPath $nuspecPath)) {
        Write-Warning "nuspec not found for $($package.Name) $($package.Version): $nuspecPath"
        continue
    }

    [xml]$nuspec = Get-Content -Raw -LiteralPath $nuspecPath
    $metadata = $nuspec.package.metadata

    $licenseText = $null
    if ($metadata.license) {
        $licenseText = ($metadata.license.'#text' ?? $metadata.license.InnerText ?? [string]$metadata.license).Trim()
    }

    $licenseUrl = $metadata.licenseUrl
    $projectUrl = $metadata.projectUrl
    $copyright = $metadata.copyright

    $entries += [pscustomobject]@{
        Name = $package.Name
        Version = $package.Version
        License = $licenseText
        LicenseUrl = [string]$licenseUrl
        ProjectUrl = [string]$projectUrl
        Copyright = [string]$copyright
    }
}

$lines = @(
    "# Third-Party Notices",
    "",
    "This file lists the NuGet packages distributed with Cafe Launcher and their licenses.",
    "Regenerate with ``scripts/New-ThirdPartyNotices.ps1`` after changing dependencies.",
    "",
    "Cafe Launcher itself is licensed under the MIT License; see ``LICENSE``.",
    "",
    "| Package | Version | License | Source |",
    "| --- | --- | --- | --- |"
)

foreach ($entry in $entries) {
    $license = $entry.License
    if ([string]::IsNullOrWhiteSpace($license)) {
        $license = if ([string]::IsNullOrWhiteSpace($entry.LicenseUrl)) { "see package" } else { "[see license]($($entry.LicenseUrl))" }
    }
    elseif ($entry.LicenseUrl) {
        $license = "$license ([text]($($entry.LicenseUrl)))"
    }

    $source = if ($entry.ProjectUrl) { $entry.ProjectUrl } else { "-" }
    $lines += "| $($entry.Name) | $($entry.Version) | $license | $source |"
}

[System.IO.File]::WriteAllText($OutputPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($entries.Count) package entries to $OutputPath"
