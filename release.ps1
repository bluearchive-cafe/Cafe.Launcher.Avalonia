<#
.SYNOPSIS
    Prepare a release: bump version, generate changelog, create tag, and push.
    The actual build and GitHub Release creation in both the source and
    distribution repositories is handled by CI (release.yml).

.DESCRIPTION
    This script automates the local portion of the release workflow:
    1. Reads the current version from Cafe.Launcher.Avalonia.csproj (<VersionPrefix>)
    2. Computes the new version based on the bump type
    3. Generates a Markdown changelog from git log since the last tag
    4. Writes the new version back to the .csproj
    5. Commits the version bump
    6. Creates an annotated tag
    7. Pushes the commit and tag to origin

    Once the tag is pushed, .github/workflows/release.yml triggers automatically
    to build the project and create matching GitHub Releases with the artifact in
    the source repository and bluearchive-cafe/Cafe.Launcher.Avalonia_Release.

.PARAMETER VersionBump
    How to bump the version:
      "patch"   → 1.0.0 → 1.0.1  (default)
      "minor"   → 1.0.0 → 1.1.0
      "major"   → 1.0.0 → 2.0.0
      "1.2.3"   → explicit version number

.PARAMETER DryRun
    Preview changes without modifying any files or creating commits/tags.

.PARAMETER SkipPush
    Commit and tag locally but do not push to origin.

.PARAMETER Force
    Skip safety checks: allow dirty working tree, allow overwriting existing tags.

.EXAMPLE
    .\release.ps1 patch
    Bump from 1.0.0 to 1.0.1, generate changelog, commit, tag, and push.

.EXAMPLE
    .\release.ps1 minor -DryRun
    Preview what would happen when bumping to the next minor version.

.EXAMPLE
    .\release.ps1 2.0.0-beta.1
    Set an explicit prerelease version.

.EXAMPLE
    .\release.ps1 patch -SkipPush
    Bump, commit, and tag locally without pushing to the remote.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$VersionBump = "patch",

    [switch]$DryRun,

    [switch]$SkipPush,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ── Paths ──────────────────────────────────────────────────────────────────
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$CsprojPath  = Join-Path $ScriptDir "Cafe.Launcher.Avalonia.csproj"
$CsprojName  = "Cafe.Launcher.Avalonia.csproj"
$ChangelogFile = Join-Path $ScriptDir "CHANGELOG_RELEASE.md"

# ── Helpers ─────────────────────────────────────────────────────────────────
function Write-Step([string]$message) {
    Write-Host ":: " -NoNewline -ForegroundColor Cyan
    Write-Host $message -ForegroundColor White
}

function Write-OK([string]$message) {
    Write-Host "   [$message]" -ForegroundColor Green
}

function Write-Warn([string]$message) {
    Write-Host "   [!] $message" -ForegroundColor Yellow
}

function Write-Fail([string]$message) {
    Write-Host "   [X] $message" -ForegroundColor Red
}

function Invoke-External(
    [string]$filePath,
    [string[]]$arguments,
    [string]$description
) {
    if ($DryRun) {
        Write-Host "   [dry-run] $description" -ForegroundColor DarkGray
        return $true
    }
    Write-Host "   $description" -ForegroundColor DarkGray
    $global:LASTEXITCODE = 0
    & $filePath @arguments *>&1 | ForEach-Object { "   $_" }
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed (exit code: $LASTEXITCODE)"
    }
    return $true
}

# ── Prerequisites ───────────────────────────────────────────────────────────
Write-Step "Checking prerequisites"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Fail "git is not available. Install from https://git-scm.com/"
    exit 1
}
Write-OK "git found"

if (-not (Test-Path $CsprojPath)) {
    Write-Fail "$CsprojName not found at $CsprojPath"
    exit 1
}
Write-OK "$CsprojName found"

# Working tree check — only flag modifications to tracked files, not untracked (??) or ignored (!!)
if (-not $Force) {
    $dirty = git -C $ScriptDir status --porcelain 2>&1 | Where-Object { $_ -match '^\s*[MADRC]' }
    if ($dirty) {
        Write-Fail "Working tree has uncommitted changes to tracked files:"
        Write-Host ($dirty -join "`n")
        Write-Host ""
        Write-Host "  Use -Force to skip this check, or commit/stash your changes first."
        exit 1
    }
}
Write-OK "Working tree clean (no pending changes to tracked files)"

# ── Read current version ─────────────────────────────────────────────────────
Write-Step "Reading current version"

$csprojContent = Get-Content $CsprojPath -Raw
if ($csprojContent -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
    $currentVersion = $Matches[1]
} else {
    Write-Fail "Could not find <VersionPrefix> in $CsprojName"
    exit 1
}
Write-OK "Current version: $currentVersion"

# ── Compute new version ─────────────────────────────────────────────────────
Write-Step "Computing new version"

$newVersion = $null
$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

if ($VersionBump -match $semVerPattern) {
    # Explicit version
    $newVersion = $VersionBump
    Write-OK "Explicit version: $newVersion"
}
else {
    if ($VersionBump -notin @("major", "minor", "patch")) {
        Write-Fail "Unknown bump type '$VersionBump'. Use major, minor, patch, or an exact SemVer value."
        exit 1
    }

    if ($currentVersion -notmatch $semVerPattern) {
        Write-Fail "Could not parse current version '$currentVersion' as major.minor.patch"
        exit 1
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]

    switch ($VersionBump) {
        "major" { $major++; $minor = 0; $patch = 0 }
        "minor" { $minor++; $patch = 0 }
        "patch" { $patch++ }
    }
    $newVersion = "$major.$minor.$patch"
    Write-OK "Bump $VersionBump : $currentVersion → $newVersion"
}

# ── Check existing tag ──────────────────────────────────────────────────────
$tagName = "v$newVersion"

if (-not $Force) {
    $existingTag = git -C $ScriptDir rev-parse --verify "refs/tags/$tagName" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Fail "Tag $tagName already exists (points to $existingTag)"
        Write-Host "  Use -Force to overwrite, or choose a different version."
        exit 1
    }
}
Write-OK "Tag $tagName is available"

# ── Generate changelog ──────────────────────────────────────────────────────
Write-Step "Generating changelog"

# Determine prerelease status (tag name contains hyphen = prerelease)
$isPrerelease = $tagName -match '-'

$lastTag = git -C $ScriptDir describe --tags --abbrev=0 2>$null
if ($LASTEXITCODE -ne 0) {
    $lastTag = $null
}

$remoteUrl = git -C $ScriptDir remote get-url origin 2>$null
if ($remoteUrl -match 'github\.com[:/](.+?)(?:\.git)?$') {
    $repoSlug = $Matches[1] -replace '\.git$', ''
} else {
    $repoSlug = ""
}

$commitUrlPrefix = if ($repoSlug) { "https://github.com/$repoSlug/commit/" } else { "" }

if ($lastTag) {
    Write-OK "Last tag: $lastTag"
    $commitLog = git -C $ScriptDir log "$lastTag..HEAD" --oneline --no-decorate 2>&1
} else {
    Write-OK "No previous tag found — using all commits"
    $commitLog = git -C $ScriptDir log --oneline --no-decorate 2>&1
}

if (-not $commitLog -or $commitLog -notmatch '\S') {
    Write-Warn "No commits since last tag. Changelog will be empty."
    $changelog = "No changes since previous release."
}
else {
    # Group commits by conventional commit prefix
    $groups = @{
        "Features"     = @()
        "Bug Fixes"    = @()
        "Refactoring"  = @()
        "Performance"  = @()
        "Other"        = @()
    }

    foreach ($line in ($commitLog -split "`n" | Where-Object { $_ -match '\S' })) {
        $line = $line.Trim()
        if ($line -match '^[a-f0-9]{7,}\s+(.+)$') {
            $hash = ($line -split '\s+')[0]
            $message = $line.Substring($hash.Length).Trim()
            $shortHash = $hash.Substring(0, [Math]::Min(7, $hash.Length))
            $linkedHash = if ($commitUrlPrefix) { "[$shortHash]($commitUrlPrefix$hash)" } else { $shortHash }

            $stripped = $message -replace '^(feat|feature|fix|refactor|perf|docs|chore|ci|test|build|style)(\(.+?\))?!?:\s*', ''
            # Capitalize first letter
            if ($stripped.Length -gt 0) {
                $stripped = $stripped.Substring(0, 1).ToUpperInvariant() + $stripped.Substring(1)
            }

            $entry = "- $stripped ($linkedHash)"

            if ($message -match '^feat') {
                $groups["Features"] += $entry
            } elseif ($message -match '^fix') {
                $groups["Bug Fixes"] += $entry
            } elseif ($message -match '^refactor') {
                $groups["Refactoring"] += $entry
            } elseif ($message -match '^perf') {
                $groups["Performance"] += $entry
            } else {
                $groups["Other"] += $entry
            }
        } else {
            $groups["Other"] += "- $line"
        }
    }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("## What's Changed")
    [void]$sb.AppendLine("")

    foreach ($groupName in @("Features", "Bug Fixes", "Refactoring", "Performance", "Other")) {
        $items = $groups[$groupName]
        if ($items.Count -gt 0) {
            [void]$sb.AppendLine("### $groupName")
            foreach ($item in $items) {
                [void]$sb.AppendLine($item)
            }
            [void]$sb.AppendLine("")
        }
    }

    $changelog = $sb.ToString().TrimEnd()
}

if (-not $DryRun) {
    $changelog | Set-Content $ChangelogFile -Encoding UTF8
    Write-OK "Changelog written to $ChangelogFile"
} else {
    Write-Host ""
    Write-Host "─── Changelog Preview ────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host $changelog
    Write-Host "─────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""
}

# ── Dry run exit ────────────────────────────────────────────────────────────
if ($DryRun) {
    Write-Host ""
    Write-Step "Dry run summary"
    Write-Host "  Version:       $currentVersion → $newVersion"
    Write-Host "  Tag:           $tagName"
    Write-Host "  Prerelease:    $isPrerelease"
    Write-Host "  Commits since: $($lastTag ?? 'initial')"
    Write-Host "  Changelog:     preview above"
    Write-Host "  Would modify:  $CsprojName"
    Write-Host "  Would commit:  chore: bump version to $tagName"
    exit 0
}

# ── Write new version to .csproj ────────────────────────────────────────────
Write-Step "Writing new version to $CsprojName"

$csprojContent = Get-Content $CsprojPath -Raw
$csprojContent = $csprojContent -replace '<VersionPrefix>[^<]+</VersionPrefix>', "<VersionPrefix>$newVersion</VersionPrefix>"

# Also update AssemblyVersion and FileVersion (numeric major.minor.patch.0)
if ($newVersion -notmatch $semVerPattern) {
    throw "Validated version no longer matches SemVer: $newVersion"
}
$assemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
if ($csprojContent -match '<AssemblyVersion>[^<]+</AssemblyVersion>') {
    $csprojContent = $csprojContent -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>"
}
if ($csprojContent -match '<FileVersion>[^<]+</FileVersion>') {
    $csprojContent = $csprojContent -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$assemblyVersion</FileVersion>"
}

Set-Content $CsprojPath -Value $csprojContent -NoNewline
Write-OK "VersionPrefix updated: $currentVersion → $newVersion"

# ── Commit ──────────────────────────────────────────────────────────────────
Write-Step "Committing version bump"

$commitMsg = "chore: bump version to $tagName"
Invoke-External "git" @("-C", $ScriptDir, "add", $CsprojName) "git add $CsprojName" | Out-Null
Invoke-External "git" @("-C", $ScriptDir, "commit", "-m", $commitMsg) "git commit" | Out-Null
Write-OK "Committed: $commitMsg"

# ── Tag ─────────────────────────────────────────────────────────────────────
Write-Step "Creating tag $tagName"

$tagMsg = "$tagName"
if ($isPrerelease) {
    $tagMsg = "$tagName (prerelease)"
}
Invoke-External "git" @("-C", $ScriptDir, "tag", "-a", $tagName, "-m", $tagMsg) "git tag $tagName" | Out-Null
Write-OK "Tagged: $tagName"

# ── Push ────────────────────────────────────────────────────────────────────
if ($SkipPush) {
    Write-Warn "Skipping push (-SkipPush). Commit and tag are local only."
    Write-Host "  To push manually: git push origin HEAD && git push origin $tagName"
} else {
    Write-Step "Pushing to origin"
    Invoke-External "git" @("-C", $ScriptDir, "push", "origin", "HEAD") "git push HEAD" | Out-Null
    Invoke-External "git" @("-C", $ScriptDir, "push", "origin", $tagName) "git push $tagName" | Out-Null
    Write-OK "Pushed commit and tag to origin"
}

# ── Summary ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Release prepared: $tagName"            -ForegroundColor Green
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Version:    $currentVersion → $newVersion"
Write-Host "  Tag:        $tagName"
Write-Host "  Prerelease: $isPrerelease"
Write-Host "  Changelog:  $ChangelogFile"
if ($SkipPush) {
    Write-Host "  Push:       SKIPPED (commit + tag are local)"
} else {
    Write-Host "  Push:       done"
}
Write-Host ""
Write-Host "  Next: CI (release.yml) will build and create both GitHub Releases."
Write-Host "  Watch:   https://github.com/$repoSlug/actions"
Write-Host "═══════════════════════════════════════════" -ForegroundColor Green
