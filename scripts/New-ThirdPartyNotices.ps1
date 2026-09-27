[CmdletBinding()]
param(
    [string[]]$ProjectPaths,
    [string]$OutputPath
)

# Regenerates THIRD-PARTY-NOTICES.md from the resolved NuGet dependency graphs of
# every production project. Reads license metadata from each package's nuspec in the
# global packages folder, so the output always reflects what actually restores.
# Run after adding, removing, or upgrading a dependency and commit the result.
#
# Scanning every production project (not just the WinExe host) is what keeps the
# disclosure honest: a package referenced only by the Windows self-update helper
# never appears in the host's graph, and the host's graph only covers a project's
# packages transitively as long as the project is referenced at all.

$ErrorActionPreference = "Stop"

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

if (-not $ProjectPaths -or $ProjectPaths.Count -eq 0) {
    $ProjectPaths = Get-ChildItem -Path (Join-Path $RootDir "src") -Recurse -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object FullName |
        Select-Object -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RootDir "THIRD-PARTY-NOTICES.md"
}

# Short label used in the "Required by" column: "Cafe.Launcher.UI" -> "Avalonia.UI".
function Get-ProjectLabel([string]$path) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
    if ($name.StartsWith("Cafe.Launcher.", [System.StringComparison]::Ordinal)) {
        return $name.Substring("Cafe.Launcher.".Length)
    }
    return $name
}

$globalPackagesFolder = & dotnet nuget locals global-packages --list
if ($globalPackagesFolder -match 'global-packages:\s*(.+)$') {
    $globalPackagesFolder = $Matches[1].Trim()
}
else {
    throw "Could not determine the NuGet global packages folder."
}

# The archives are self-contained, so the disclosure must name the runtime they
# redistribute even though it never appears in project.assets.json. Do not copy
# the newest globally installed runtime into the notice: that may be unrelated
# to the pinned SDK and makes regeneration host-dependent.
$globalJsonPath = Join-Path $RootDir "global.json"
$pinnedSdkVersion = if (Test-Path -LiteralPath $globalJsonPath) {
    (Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json).sdk.version
}
else {
    $null
}
$runtimeDescription = if ($pinnedSdkVersion) {
    "``Microsoft.NETCore.App`` from pinned .NET SDK ``$pinnedSdkVersion``"
}
else {
    "``Microsoft.NETCore.App`` from the publishing SDK"
}

$scannedProjects = @()
$libraries = [ordered]@{}
foreach ($projectPath in $ProjectPaths) {
    $resolvedProject = (Resolve-Path -LiteralPath $projectPath).Path
    $label = Get-ProjectLabel $resolvedProject
    # The disclosure header names the engineering project (the .csproj name); the table's
    # "Required by" column uses the short label. The host project's label is its own name,
    # so the header must not re-prefix it.
    $scannedProjects += [System.IO.Path]::GetFileNameWithoutExtension($resolvedProject)

    $assetsPath = Join-Path (Split-Path -Parent $resolvedProject) "obj/project.assets.json"
    if (-not (Test-Path -LiteralPath $assetsPath)) {
        throw "project.assets.json not found at '$assetsPath'. Run 'dotnet restore' on the project first."
    }

    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
    foreach ($property in $assets.libraries.PSObject.Properties) {
        # Key format: "PackageName/Version"
        $name, $version = $property.Name -split '/', 2
        if ($property.Value.type -ne 'package') {
            continue
        }

        if ($libraries.Contains($name)) {
            $existing = $libraries[$name]
            if ($existing.Version -ne $version) {
                # Two projects resolving different versions of one package means the
                # notice cannot name a single version; fail instead of guessing.
                throw "Package '$name' resolves to '$($existing.Version)' in one project and '$version' in '$label'."
            }
            if (-not $existing.RequiredBy.Contains($label)) {
                $existing.RequiredBy += $label
            }
        }
        else {
            $libraries[$name] = [pscustomobject]@{
                Name = $name
                Version = $version
                RequiredBy = @($label)
            }
        }
    }
}

$entries = @()
foreach ($name in ($libraries.Keys | Sort-Object -CaseSensitive:$false)) {
    $package = $libraries[$name]
    $nuspecPath = Join-Path $globalPackagesFolder ($name.ToLowerInvariant() + "/" + $package.Version.ToLowerInvariant() + "/" + $name.ToLowerInvariant() + ".nuspec")
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
        RequiredBy = ($package.RequiredBy -join ", ")
        License = $licenseText
        LicenseUrl = [string]$licenseUrl
        ProjectUrl = [string]$projectUrl
        Copyright = [string]$copyright
    }
}

# Built as an explicit list: an array literal containing a joined expression is parsed
# ambiguously (the join bound across the whole literal and collapsed the header).
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Third-Party Notices")
$lines.Add("")
$lines.Add("This file lists the NuGet packages distributed with Cafe Launcher and their licenses.")
$lines.Add("Regenerate with ``scripts/New-ThirdPartyNotices.ps1`` after changing dependencies.")
$lines.Add("")
$lines.Add("Cafe Launcher itself is licensed under the MIT License; see ``LICENSE``.")
$lines.Add("")
$lines.Add("## Production projects scanned")
$lines.Add("")
$lines.Add("The table below is the union of the resolved dependency graphs of every production")
$lines.Add("project, so a package referenced by only one of them is still disclosed:")
$lines.Add("")
foreach ($projectName in ($scannedProjects | Sort-Object -CaseSensitive:$false)) {
    $lines.Add("- ``$projectName``")
}
$lines.Add("")
$lines.Add("## Self-contained .NET runtime")
$lines.Add("")
$lines.Add("Release archives are self-contained: besides the packages below they redistribute the .NET")
$lines.Add("runtime and apphost bundled with the publishing SDK — $runtimeDescription.")
$lines.Add("Both are MIT-licensed (https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and are not")
$lines.Add("resolved as NuGet packages, so they cannot appear in the table below: the table lists exactly")
$lines.Add("what ``dotnet restore`` resolves, and the RID-specific publish closure is outside its scope.")
$lines.Add("The archives carry this file and ``LICENSE`` next to the binaries.")
$lines.Add("")
$lines.Add("| Package | Version | License | Required by | Source |")
$lines.Add("| --- | --- | --- | --- | --- |")
foreach ($entry in $entries) {
    $license = $entry.License
    if ([string]::IsNullOrWhiteSpace($license)) {
        $license = if ([string]::IsNullOrWhiteSpace($entry.LicenseUrl)) { "see package" } else { "[see license]($($entry.LicenseUrl))" }
    }
    elseif ($entry.LicenseUrl) {
        $license = "$license ([text]($($entry.LicenseUrl)))"
    }

    $source = if ($entry.ProjectUrl) { $entry.ProjectUrl } else { "-" }
    $lines.Add("| $($entry.Name) | $($entry.Version) | $license | $($entry.RequiredBy) | $source |")
}

[System.IO.File]::WriteAllText($OutputPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($entries.Count) package entries from $($scannedProjects.Count) production projects to $OutputPath"
