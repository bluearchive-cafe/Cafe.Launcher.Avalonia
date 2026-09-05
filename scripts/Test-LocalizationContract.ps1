param(
    [string]$ResourcesDirectory = (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.Avalonia\Resources'),
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.Avalonia')
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

# AGENTS.md 规定：C# 源码中禁止把裸 key 字符串字面量直传 T()/F()，
# 必须引用 Constants/LocalizationKeys 的编译期常量；否则重命名 key 时
# 合约测试扫不到这些调用点，运行期静默回退。此处以 [.[TF](\s*" 形态
# 匹配调用点（含 LocalizationService.T(...) 静态形式），跨行首参也命中。
$sourceFiles = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -Filter *.cs |
    Where-Object {
        ($_.FullName -notmatch '\\(bin|obj)\\') -and
        ($_.FullName -notmatch '\\Constants\\LocalizationKeys\.cs$') -and
        ($_.FullName -notmatch '\\Resources\\.+\.Designer\.cs$')
    }
$rawKeyPattern = '(?s)\.[TF]\(\s*"[A-Za-z]'
foreach ($file in $sourceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    if ($content -match $rawKeyPattern) {
        Write-Error "$($file.FullName): Raw localization key literal passed to T()/F(); use Constants/LocalizationKeys instead." -ErrorAction Continue
        $hasErrors = $true
    }
}

if ($hasErrors) {
    exit 1
}
