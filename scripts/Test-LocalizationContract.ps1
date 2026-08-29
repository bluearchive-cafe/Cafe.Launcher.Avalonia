param(
    [string]$ResourcesDirectory = (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.Avalonia\Resources')
)

$ErrorActionPreference = 'Stop'

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

# Load resx files as key-value hashtables using PowerShell XML parsing.
function Read-ResxHashtable {
    param([string]$Path)

    $doc = [xml](Get-Content -LiteralPath $Path -Raw -Encoding UTF8)
    $result = @{}
    foreach ($data in $doc.root.data) {
        $result[$data.name] = $data.value
    }

    return $result
}

$referencePath = Join-Path $ResourcesDirectory 'LauncherStrings.resx'
$reference = Read-ResxHashtable -Path $referencePath
$placeholderPattern = '\{(\d+)(?:[^}]*)\}'
$hasErrors = $false

function Get-CompositeFormatArgumentIndexes {
    param([string]$Value)

    return @(
        [regex]::Matches($Value, $placeholderPattern) |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object
    )
}

foreach ($fileName in @('LauncherStrings.ja.resx', 'LauncherStrings.zh-Hans.resx', 'LauncherStrings.zh-Hant.resx')) {
    $localizedPath = Join-Path $ResourcesDirectory $fileName
    $localized = Read-ResxHashtable -Path $localizedPath

    foreach ($key in @($reference.Keys | Sort-Object)) {
        if (-not $localized.ContainsKey($key)) {
            Write-Error "${fileName}: Missing key: $key" -ErrorAction Continue
            $hasErrors = $true
            continue
        }

        $referenceIndexes = Get-CompositeFormatArgumentIndexes -Value ([string]$reference[$key])
        $localizedIndexes = Get-CompositeFormatArgumentIndexes -Value ([string]$localized[$key])
        if (($referenceIndexes -join ',') -ne ($localizedIndexes -join ',')) {
            Write-Error "${fileName}: Placeholder mismatch: $key" -ErrorAction Continue
            $hasErrors = $true
        }
    }

    foreach ($key in @($localized.Keys | Sort-Object)) {
        if (-not $reference.ContainsKey($key)) {
            Write-Error "${fileName}: Unexpected key: $key" -ErrorAction Continue
            $hasErrors = $true
        }
    }
}

if ($hasErrors) {
    exit 1
}
