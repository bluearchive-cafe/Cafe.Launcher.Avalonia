# P1/M1: One-shot design-token rename (Launcher* -> Launcher.<Family>.*).
# Reads docs/design/token-migration-map.json, replaces exact old keys across
# src/ and tests/ (.axaml with word boundaries, .cs inside quoted strings),
# removes value-identical duplicate definitions for merged targets in App.axaml,
# then audits residuals. Exits non-zero on any missing mapping or residual.
[CmdletBinding()]
param(
    [string]$MapPath = (Join-Path $PSScriptRoot '..\docs\design\token-migration-map.json'),
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$utf8 = [System.Text.UTF8Encoding]::new($false)
$map = Get-Content $MapPath -Raw -Encoding utf8 | ConvertFrom-Json
$mapByOld = $map.map.PSObject.Properties | ForEach-Object {
    [pscustomobject]@{ Old = $_.Name; New = $_.Value }
}
$byOld = @{}
foreach ($entry in $mapByOld) { $byOld[$entry.Old] = $entry.New }
$preserved = @($map.preserved)

function Get-TargetFileList {
    param([string]$Root, [string[]]$Subdirs)
    $files = foreach ($sub in $Subdirs) {
        $dir = Join-Path $Root $sub
        if (Test-Path $dir) {
            Get-ChildItem $dir -Recurse -File | Where-Object {
                $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.axaml', '.cs'
            }
        }
    }
    return $files
}

# --- Validate map completeness against App.axaml token definitions. ----------
$appPath = Join-Path $RepositoryRoot 'src\Cafe.Launcher.Avalonia\App.axaml'
$appText = [System.IO.File]::ReadAllText($appPath)
$definedKeys = [regex]::Matches($appText, 'x:Key="(Launcher[A-Z][A-Za-z0-9]*)"') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$unmapped = $definedKeys | Where-Object { -not $byOld.ContainsKey($_) -and $_ -notin $preserved }
if ($unmapped) {
    Write-Error "App.axaml token keys missing from the migration map: $($unmapped -join ', ')"
}
$unused = $mapByOld | Where-Object { $_.Old -notin $definedKeys }
if ($unused) {
    Write-Warning "Map entries not found in App.axaml (no definition): $($unused.Old -join ', ')"
}
Write-Host "[INFO] App.axaml defined token keys: $($definedKeys.Count); map entries: $($mapByOld.Count)"

# --- Execute replacements. ----------------------------------------------------
$targets = Get-TargetFileList $RepositoryRoot @('src', 'tests')
$changedFiles = @()
$totalReplacements = 0
foreach ($file in $targets) {
    $path = $file.FullName
    $text = [System.IO.File]::ReadAllText($path)
    $count = 0
    if ($file.Extension -eq '.axaml') {
        foreach ($entry in $mapByOld) {
            $pattern = '\b' + [regex]::Escape($entry.Old) + '\b'
            $matches = [regex]::Matches($text, $pattern)
            if ($matches.Count -gt 0) {
                $count += $matches.Count
                $text = [regex]::Replace($text, $pattern, $entry.New)
            }
        }
    }
    else {
        foreach ($entry in $mapByOld) {
            $pattern = '\b' + [regex]::Escape($entry.Old) + '\b'
            $matches = [regex]::Matches($text, $pattern)
            if ($matches.Count -gt 0) {
                $count += $matches.Count
                $text = [regex]::Replace($text, $pattern, $entry.New)
            }
        }
    }
    if ($count -gt 0) {
        [System.IO.File]::WriteAllText($path, $text, $utf8)
        $changedFiles += [pscustomobject]@{ Path = $path.Replace($RepositoryRoot + '\', ''); Count = $count }
        $totalReplacements += $count
    }
}
Write-Host "[INFO] Files changed: $($changedFiles.Count); total replacements: $totalReplacements"
$changedFiles | Sort-Object Path | ForEach-Object { Write-Host "  $($_.Path) => $($_.Count)" }

# --- Remove duplicate definitions for merged targets (App.axaml only). --------
$targetsByNew = @{}
foreach ($entry in $mapByOld) {
    if (-not $targetsByNew.ContainsKey($entry.New)) { $targetsByNew[$entry.New] = @() }
    $targetsByNew[$entry.New] += $entry.Old
}
$merged = $targetsByNew.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }
Write-Host "[INFO] Merged targets (multiple old keys): $(($merged | ForEach-Object { $_.Key }) -join ', ')"
foreach ($group in $merged) {
    $newKey = $group.Key
    $lines = [System.IO.File]::ReadAllLines($appPath, $utf8)
    $occurrences = for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match [regex]::Escape('x:Key="' + $newKey + '"')) { $i }
    }
    if ($occurrences.Count -le 1) { continue }
    $definitionText = @($occurrences | ForEach-Object { $lines[$_].Trim() } | Sort-Object -Unique)
    if ($definitionText.Count -ne 1) {
        Write-Error "Merged target '$newKey' has non-identical duplicate definitions in App.axaml; manual review required."
    }
    $keep = $occurrences[0]
    $remove = @($occurrences) | Where-Object { $_ -ne $keep } | Sort-Object -Descending
    foreach ($index in $remove) {
        $lines = @($lines[0..($index - 1)]) + @($lines[($index + 1)..($lines.Length - 1)])
    }
    [System.IO.File]::WriteAllLines($appPath, $lines, $utf8)
    Write-Host "[DEDUP] App.axaml: removed $($remove.Count) duplicate definition(s) of '$newKey' (value-identical)."
}

# --- Audit residuals. -----------------------------------------------------------
# A residual is any remaining occurrence of a MAP KEY (an old token key that was
# supposed to disappear). Non-token identifiers that merely start with "Launcher"
# (C# types like LauncherSettingsService, XML doc <see cref>, {x:Static} type
# references, ViewModel members like Shell.LauncherVersionText) are not residuals.
$axamlResidual = @()
$csResidual = @()
foreach ($file in $targets) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    if ($file.Extension -eq '.axaml') {
        $residual = foreach ($entry in $mapByOld) {
            if ([regex]::IsMatch($text, '\b' + [regex]::Escape($entry.Old) + '\b')) { $entry.Old }
        }
        if ($residual) {
            $axamlResidual += "[$($file.FullName.Replace($RepositoryRoot + '\', ''))] $($residual -join ', ')"
        }
    }
    else {
        $residual = foreach ($entry in $mapByOld) {
            if ([regex]::IsMatch($text, '\b' + [regex]::Escape($entry.Old) + '\b')) { $entry.Old }
        }
        if ($residual) {
            $csResidual += "[$($file.FullName.Replace($RepositoryRoot + '\', ''))] $($residual -join ', ')"
        }
    }
}
if ($axamlResidual) {
    Write-Host '[AUDIT] residual old token keys in .axaml:'
    $axamlResidual | ForEach-Object { Write-Host "  $_" }
}
if ($csResidual) {
    Write-Host '[AUDIT] residual old token keys in .cs:'
    $csResidual | ForEach-Object { Write-Host "  $_" }
}
if ($axamlResidual -or $csResidual) {
    Write-Error 'Rename incomplete: see residual audit above.'
}

# Informational listing of remaining Launcher[A-Z] identifiers (non-token by design).
$nonTokenRefs = foreach ($file in $targets) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    [regex]::Matches($text, 'Launcher[A-Z][A-Za-z0-9]*') |
        ForEach-Object { $_.Value } |
        Where-Object { -not $byOld.ContainsKey($_) } |
        Sort-Object -Unique
}
if ($nonTokenRefs) {
    Write-Host "[INFO] non-token 'Launcher*' identifiers (types/members, not renamed): $($nonTokenRefs -join ', ')"
}

Write-Host '[OK] Rename completed.'
