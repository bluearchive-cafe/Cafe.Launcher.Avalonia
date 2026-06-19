<#
.SYNOPSIS
    Generates the release changelog shared by local release preparation and CI.

.PARAMETER CurrentRef
    Git ref whose commits should be included. Defaults to HEAD.

.PARAMETER PreviousTag
    Previous release tag. When omitted, all commits reachable from CurrentRef are included.

.PARAMETER OutputPath
    Optional path where the generated Markdown is written.

.PARAMETER PassThru
    Returns the generated Markdown to the caller.
#>

[CmdletBinding()]
param(
    [string]$CurrentRef = "HEAD",

    [AllowEmptyString()]
    [string]$PreviousTag = "",

    [string]$OutputPath = "",

    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepositoryRoot = Split-Path -Parent $ScriptDir

$remoteUrl = git -C $RepositoryRoot remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0 -and $remoteUrl -match 'github\.com[:/](.+?)(?:\.git)?$') {
    $repositorySlug = $Matches[1] -replace '\.git$', ''
} else {
    $repositorySlug = ""
}

$commitUrlPrefix = if ($repositorySlug) {
    "https://github.com/$repositorySlug/commit/"
} else {
    ""
}

$revisionRange = if ([string]::IsNullOrWhiteSpace($PreviousTag)) {
    $CurrentRef
} else {
    "$PreviousTag..$CurrentRef"
}

$commitLog = git -C $RepositoryRoot log $revisionRange --oneline --no-decorate 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read Git history for '$revisionRange': $($commitLog -join [Environment]::NewLine)"
}

if (-not $commitLog -or $commitLog -notmatch '\S') {
    $changelog = "No changes since previous release."
} else {
    $groups = [ordered]@{
        "Features" = @()
        "Bug Fixes" = @()
        "Refactoring" = @()
        "Performance" = @()
        "Other" = @()
    }

    foreach ($line in ($commitLog -split "`n" | Where-Object { $_ -match '\S' })) {
        $line = $line.Trim()
        if ($line -notmatch '^[a-f0-9]{7,}\s+(.+)$') {
            $groups["Other"] += "- $line"
            continue
        }

        $hash = ($line -split '\s+')[0]
        $message = $line.Substring($hash.Length).Trim()
        $shortHash = $hash.Substring(0, [Math]::Min(7, $hash.Length))
        $linkedHash = if ($commitUrlPrefix) {
            "[$shortHash]($commitUrlPrefix$hash)"
        } else {
            $shortHash
        }

        $stripped = $message -replace '^(feat|feature|fix|refactor|perf|docs|chore|ci|test|build|style)(\(.+?\))?!?:\s*', ''
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
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.AppendLine("## What's Changed")
    [void]$builder.AppendLine("")

    foreach ($group in $groups.GetEnumerator()) {
        if ($group.Value.Count -eq 0) {
            continue
        }

        [void]$builder.AppendLine("### $($group.Key)")
        foreach ($entry in $group.Value) {
            [void]$builder.AppendLine($entry)
        }
        [void]$builder.AppendLine("")
    }

    $changelog = $builder.ToString().TrimEnd()
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath
    } else {
        Join-Path $RepositoryRoot $OutputPath
    }
    $changelog | Set-Content $resolvedOutputPath -Encoding UTF8
}

if ($PassThru) {
    Write-Output $changelog
}
