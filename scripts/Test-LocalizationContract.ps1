param(
    [string]$LocalesDirectory = (Join-Path $PSScriptRoot '..\Assets\Locales')
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

function ConvertFrom-JsonToStringHashtable {
    param([string]$Json)

    $jsonObject = ConvertFrom-Json -InputObject $Json
    $result = @{}

    foreach ($property in $jsonObject.PSObject.Properties) {
        $result[$property.Name] = [string]$property.Value
    }

    return $result
}

$referencePath = Join-Path $LocalesDirectory 'en.json'
$reference = ConvertFrom-JsonToStringHashtable -Json (Get-Content -LiteralPath $referencePath -Raw -Encoding UTF8)
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
    $localized = ConvertFrom-JsonToStringHashtable -Json (Get-Content -LiteralPath $localizedPath -Raw -Encoding UTF8)

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
