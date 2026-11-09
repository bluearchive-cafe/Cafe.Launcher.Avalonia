param(
    [string]$ResourcesDirectory = (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.UI\Resources'),
    # 全生产程序集：Core/UI 拆分后 .cs 不再只属于宿主工程，漏扫一处就等于放弃那部分
    # 裸 key 字面量的守卫。
    [string[]]$SourceDirectories = @(
        (Join-Path $PSScriptRoot '..\src\Cafe.Launcher'),
        (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.Core'),
        (Join-Path $PSScriptRoot '..\src\Cafe.Launcher.UI')
    )
)

$ErrorActionPreference = 'Stop'

# Emit and decode console output as UTF-8 so Chinese text (commit messages,
# resx values, tool output) survives the system's active code page.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

& "$PSScriptRoot/Generate-LocalizationCatalog.ps1" -ResourcesDirectory $ResourcesDirectory -Check
if (-not $?) { exit 1 }
$hasErrors = $false

# AGENTS.md 规定：C# 源码中禁止把裸 key 字符串字面量直传 T()/F()/I18n[...]，
# 必须引用 Constants/LocalizationKeys 的类型化枚举；字符串索引器仍需守卫，重命名 key 时
# 合约测试扫不到这些调用点，运行期静默回退。此处匹配两类调用点形态
# （含 LocalizationService.T(...) 静态形式与 Shell.I18n["..."] 索引器
# 形式），跨行首参也命中；XAML 绑定 Shell.I18n[key] 是设计内用法，
# 且本扫描只针对 .cs，不受影响。
$sourceFiles = @($SourceDirectories |
    ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -Filter *.cs } |
    Where-Object {
        ($_.FullName -notmatch '\\(bin|obj)\\') -and
        ($_.FullName -notmatch '\\Constants\\LocalizationKeys\.cs$') -and
        ($_.FullName -notmatch '\\Resources\\.+\.Designer\.cs$')
    })

# 反空转基线：根目录写错时上面的枚举会安静地返回零个文件，"没有裸 key" 与 "什么都没扫"
# 不可区分。真的删除大批文件时同步下调这里。
if ($sourceFiles.Count -lt 250) {
    Write-Error "只枚举到 $($sourceFiles.Count) 个生产 .cs 文件（基线 250）——先确认 SourceDirectories 仍覆盖 host/Core/UI 三棵树。"
    exit 1
}
$rawKeyPatterns = @(
    '(?s)\.[TF]\(\s*"[A-Za-z]',
    '(?s)\.I18n\[\s*"[A-Za-z]'
)
foreach ($file in $sourceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($rawKeyPattern in $rawKeyPatterns) {
        if ($content -match $rawKeyPattern) {
            Write-Error "$($file.FullName): Raw localization key literal passed to T()/F()/I18n[...]; use Constants/LocalizationKeys instead." -ErrorAction Continue
            $hasErrors = $true
        }
    }
}

if ($hasErrors) {
    exit 1
}
