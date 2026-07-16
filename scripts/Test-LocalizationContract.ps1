param(
    [string]$LocalesDirectory = (Join-Path $PSScriptRoot '..\Assets\Locales')
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

$referencePath = Join-Path $LocalesDirectory 'en.json'
$reference = Get-Content -LiteralPath $referencePath -Raw | ConvertFrom-Json -AsHashtable
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

foreach ($fileName in @('ja.json', 'zh-Hans.json', 'zh-Hant.json')) {
    $localizedPath = Join-Path $LocalesDirectory $fileName
    $localized = Get-Content -LiteralPath $localizedPath -Raw | ConvertFrom-Json -AsHashtable

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
