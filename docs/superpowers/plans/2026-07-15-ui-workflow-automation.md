# UI 工作流自动化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Avalonia 小型 UI 改动提供快速验证、本地化资源契约检查和可复用的项目级修复 Skill。

**Architecture:** 根目录 `dev.ps1` 只编排现有 .NET 测试命令，不复制测试逻辑。`Test-LocalizationContract.ps1` 是无副作用的 PowerShell 校验器，以英文资源的扁平 JSON 键集合与复合格式占位符为唯一契约。项目 Skill 仅编排已有仓库规范和新快速命令，不引入运行时依赖。

**Tech Stack:** PowerShell 7、.NET 10、xUnit v3、Avalonia.Headless.XUnit、JSON。

## Global Constraints

- 不新增 NuGet 包、运行时依赖、原始颜色或间距令牌。
- `./dev.ps1 ui` 不得调用 `coverage.ps1`、Release 构建或安装器脚本；合并前仍使用 `./verify.ps1`。
- 本地化校验仅读取 `Assets/Locales/*.json`，不得修改、格式化或重排资源文件。
- 所有失败路径必须返回非零退出码，并采用现有脚本的遥测禁用和 `$ErrorActionPreference = 'Stop'` 约定。
- Skill 只适用于局部 UI 修复；持久化、导航规则、外部集成和跨步骤体验变更必须升级到设计与实施计划。

---

## File Structure

- Create: `dev.ps1` — 快速开发验证入口。
- Create: `scripts/Test-LocalizationContract.ps1` — 四语言 JSON 键与格式化占位符检查。
- Create: `.agents/skills/avalonia-ui-patch/SKILL.md` — 小型 Avalonia UI 修复工作流。
- Modify: `AGENTS.md` — 记录三个入口及其正确使用边界。

### Task 1: 快速 UI 验证入口

**Files:**
- Create: `dev.ps1`

**Interfaces:**
- Consumes: `dotnet test`、`tests/Cafe.Launcher.Avalonia.Tests/Cafe.Launcher.Avalonia.Tests.csproj`、`tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj`。
- Produces: `./dev.ps1 ui`，成功返回 `0`；任一测试失败时返回其退出码。

- [ ] **Step 1: 先确认现有 UI 测试命令是绿色**

运行：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj
```

预期：两个命令均退出 `0`；第一个只运行 `UiStyleContractTests`，第二个运行无头 UI 测试项目。

- [ ] **Step 2: 新建失败优先的命令契约检查**

在 PowerShell 中执行尚不存在的入口：

```powershell
.\dev.ps1 ui
```

预期：PowerShell 报告找不到 `dev.ps1`，退出非零。这是命令尚未实现的基线。

- [ ] **Step 3: 实现最小入口**

创建 `dev.ps1`：

```powershell
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ui')]
    [string]$Task
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj `
    --filter "FullyQualifiedName~UiStyleContractTests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

保持 `$Task` 的 `ValidateSet`，即使当前仅有 `ui`，以确保未知任务在执行测试前被拒绝。

- [ ] **Step 4: 验证入口范围与成功路径**

运行：

```powershell
.\dev.ps1 ui
```

预期：`UiStyleContractTests` 和无头测试均通过，退出 `0`。检查脚本内容不包含 `coverage.ps1`、`-c Release`、`Build-Distribution.ps1` 或 `release.ps1`。

- [ ] **Step 5: 提交独立可用的快速验证入口**

```powershell
git add dev.ps1
git commit -m "test(workflow): 添加快速 UI 验证入口"
```

### Task 2: 本地化资源契约校验器

**Files:**
- Create: `scripts/Test-LocalizationContract.ps1`

**Interfaces:**
- Consumes: `Assets/Locales/en.json`、`Assets/Locales/ja.json`、`Assets/Locales/zh-Hans.json`、`Assets/Locales/zh-Hant.json`。
- Produces: `./scripts/Test-LocalizationContract.ps1`，报告每个资源文件的缺失键、多余键和占位符差异；发生差异时退出 `1`。

- [ ] **Step 1: 建立当前资源的绿色基线**

运行：

```powershell
Get-Content .\Assets\Locales\en.json -Raw | ConvertFrom-Json | Get-Member -MemberType NoteProperty
```

预期：能读取英文资源的顶层键；现有资源文件保持不被修改。

- [ ] **Step 2: 编写并先运行三个反例夹具**

在 `TestResults\LocalizationContract` 创建临时 `en.json` 与目标语言 JSON：一个缺少 `Message`，一个多出 `Unexpected`，一个将英文 `"{0} files"` 改为目标语言 `"files"`。分别调用将要实现的脚本并传入夹具目录：

```powershell
.\scripts\Test-LocalizationContract.ps1 -LocalesDirectory .\TestResults\LocalizationContract
```

预期：实现前找不到脚本；实现后每个夹具均退出 `1` 且分别输出 `Missing key: Message`、`Unexpected key: Unexpected`、`Placeholder mismatch: Count`。

- [ ] **Step 3: 实现只读契约校验器**

创建以下接口与逻辑：

```powershell
param(
    [string]$LocalesDirectory = (Join-Path $PSScriptRoot '..\Assets\Locales')
)

$ErrorActionPreference = 'Stop'
$referencePath = Join-Path $LocalesDirectory 'en.json'
$reference = Get-Content -LiteralPath $referencePath -Raw | ConvertFrom-Json -AsHashtable
$placeholderPattern = '\\{(\\d+)(?:[^}]*)\\}'
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
    $localized = Get-Content -LiteralPath (Join-Path $LocalesDirectory $fileName) -Raw | ConvertFrom-Json -AsHashtable

    foreach ($key in @($reference.Keys | Sort-Object)) {
        if (-not $localized.ContainsKey($key)) {
            Write-Error "$fileName: Missing key: $key" -ErrorAction Continue
            $hasErrors = $true
            continue
        }

        $referenceIndexes = Get-CompositeFormatArgumentIndexes -Value ([string]$reference[$key])
        $localizedIndexes = Get-CompositeFormatArgumentIndexes -Value ([string]$localized[$key])
        if (($referenceIndexes -join ',') -ne ($localizedIndexes -join ',')) {
            Write-Error "$fileName: Placeholder mismatch: $key" -ErrorAction Continue
            $hasErrors = $true
        }
    }

    foreach ($key in @($localized.Keys | Sort-Object)) {
        if (-not $reference.ContainsKey($key)) {
            Write-Error "$fileName: Unexpected key: $key" -ErrorAction Continue
            $hasErrors = $true
        }
    }
}

if ($hasErrors) { exit 1 }
```

为可读诊断，遍历键时使用 `Sort-Object`；所有路径使用 `-LiteralPath`。不得写入任何 JSON。

- [ ] **Step 4: 验证真实资源与反例夹具**

运行：

```powershell
.\scripts\Test-LocalizationContract.ps1
.\scripts\Test-LocalizationContract.ps1 -LocalesDirectory .\TestResults\LocalizationContract
Remove-Item -LiteralPath .\TestResults\LocalizationContract -Recurse -Force
```

预期：真实资源退出 `0`；反例夹具退出 `1` 且包含三个精确诊断；清理后不留下测试夹具。

- [ ] **Step 5: 提交本地化校验器**

```powershell
git add scripts/Test-LocalizationContract.ps1
git commit -m "test(localization): 添加资源契约检查"
```

### Task 3: 项目级 Avalonia UI 修复 Skill 与使用文档

**Files:**
- Create: `.agents/skills/avalonia-ui-patch/SKILL.md`
- Modify: `AGENTS.md`

**Interfaces:**
- Consumes: 截图或文字 UI 反馈、仓库 `App.axaml` 设计令牌、`./dev.ps1 ui` 与 `./scripts/Test-LocalizationContract.ps1`。
- Produces: 可复用的 `avalonia-ui-patch` 工作流，说明小型 UI 修复的适用范围、最小验证和升级条件。

- [ ] **Step 1: 写入 Skill 的明确边界与流程**

创建 `.agents/skills/avalonia-ui-patch/SKILL.md`，内容必须包括：

```markdown
---
name: avalonia-ui-patch
description: Use for a localized Avalonia UI fix such as alignment, spacing, selected state, localized display, or an existing control binding.
---

# Avalonia UI Patch

1. Read `AGENTS.md`, the affected XAML, its ViewModel, and the existing focused tests.
2. Reproduce the reported visual or interaction symptom with the narrowest existing headless/style test; add a focused regression test when the current suite cannot detect it.
3. Reuse tokens from `App.axaml`; do not introduce raw colors, spacing, dependencies, or unrelated refactors.
4. Run `./dev.ps1 ui`; run `./scripts/Test-LocalizationContract.ps1` when a locale JSON file changes.

## Escalate instead

Do not use this skill for settings persistence, navigation rules, external integrations, or a multi-step product-flow change. Create a design and implementation plan for those changes.
```

补充一句：有截图时先检查实际运行时绑定与自动化属性，不能仅凭视觉推断。

- [ ] **Step 2: 将入口加入仓库协作规范**

在 `AGENTS.md` 的 “Build, Test, and Development Commands” 中添加：

```markdown
- `.\dev.ps1 ui` — run UI style-contract and headless UI tests after localized UI changes.
- `.\scripts\Test-LocalizationContract.ps1` — verify keys and composite-format placeholders across all locale JSON files.
```

在 “Testing Guidelines” 增加：修改任意 `Assets/Locales/*.json` 后运行本地化检查；XAML/样式修改后运行 `dev.ps1 ui`，合并或发布前仍执行 `verify.ps1`。

- [ ] **Step 3: 验证 Skill 内容和文档链接**

运行：

```powershell
Get-Content .\.agents\skills\avalonia-ui-patch\SKILL.md -Raw
rg -n "dev\.ps1 ui|Test-LocalizationContract\.ps1|verify\.ps1" AGENTS.md .agents\skills\avalonia-ui-patch\SKILL.md
```

预期：Skill 包含适用范围、最小流程、验证命令和升级条件；`AGENTS.md` 同时提到快速验证、本地化检查与完整验证门槛。

- [ ] **Step 4: 运行最终快速验证**

运行：

```powershell
.\scripts\Test-LocalizationContract.ps1
.\dev.ps1 ui
```

预期：两个命令均退出 `0`；不运行覆盖率、Release 构建或安装器流程。

- [ ] **Step 5: 提交 Skill 与协作规范**

```powershell
git add AGENTS.md .agents/skills/avalonia-ui-patch/SKILL.md
git commit -m "docs(workflow): 说明 UI 修复自动化流程"
```

## Final Verification

- [ ] 运行 `git status --short`，确认仅存在预期改动或工作区干净。
- [ ] 运行 `./scripts/Test-LocalizationContract.ps1`，预期退出 `0`。
- [ ] 运行 `./dev.ps1 ui`，预期 `UiStyleContractTests` 和无头 UI 测试均通过。
- [ ] 合并或发布前额外运行 `./verify.ps1`；该步骤不属于快速 UI 命令的职责。
