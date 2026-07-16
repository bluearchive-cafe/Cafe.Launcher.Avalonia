# Coverage Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让单元测试和 Headless 测试共同形成项目级 Cobertura 报告，并保证手写 C# 行覆盖率与分支覆盖率均不低于 70%。

**Architecture:** 两个测试项目继续独立运行并各自产生 Cobertura 文件；`coverage.ps1` 读取两份报告，以规范化源文件路径、行号和分支条件编号合并命中数据。阈值只统计仓库中的手写 `.cs` 文件，测试补强严格依据合并报告中列出的精确未覆盖位置。

**Tech Stack:** .NET 10、xUnit 2.9.3、xUnit v3 3.2.2、Avalonia.Headless.XUnit 12.0.4、coverlet.collector 10.0.1、PowerShell、Cobertura XML。

---

## 文件结构

- Create: `coverage.runsettings` — 两个测试项目共享的 Coverlet collector 配置。
- Create: `coverage.ps1` — 运行测试、定位报告、合并行与分支、输出摘要并检查 70% 阈值。
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj` — 引用与单元测试项目相同版本的 `coverlet.collector`。
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/OverlayFocusBehaviorTests.cs` — 补充焦点行为的 Headless 分支。
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/SystemTrayServiceTests.cs` — 补充托盘服务的失败、幂等和释放分支。
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs` — 补充窗口代码后置逻辑的可执行分支。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs` — 补充下载失败、取消、暂停和清理分支。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/RemoteHttpUrlValidatorTests.cs` — 补充报告确认的 URL 校验分支。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/InstallationOperationStateTests.cs` — 补充启动与卸载验证分支。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs` — 补充主题颜色与外观设置分支。
- Modify: 与合并报告中精确未覆盖 ViewModel 对应的现有测试文件 — 只在前述测试仍未达到阈值时使用。
- Modify: `verify.ps1` — 在完整验证中调用项目级覆盖率检查。
- Modify only if a failing behavior test proves isolation impossible: the exact production file named by that test.

### Task 1: 建立双测试项目覆盖率采集

**Files:**
- Create: `coverage.runsettings`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj`

- [ ] **Step 1: 记录配置前失败**

Run:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --collect:'XPlat Code Coverage' --results-directory .\TestResults\Coverage\headless-before
```

Expected: 测试项目能够运行，但输出中没有 `coverage.cobertura.xml` 附件；用以下命令确认文件数为 `0`：

```powershell
@(Get-ChildItem .\TestResults\Coverage\headless-before -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue).Count
```

- [ ] **Step 2: 给 Headless 测试加入 collector**

在 `Avalonia.Headless.XUnit` 引用后加入与单元测试项目完全一致的引用：

```xml
<PackageReference Include="coverlet.collector" Version="10.0.1" />
```

- [ ] **Step 3: 新增共享 runsettings**

创建 `coverage.runsettings`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Include>[Cafe.Launcher.Avalonia]*</Include>
          <ExcludeByFile>**/*.axaml,**/obj/**</ExcludeByFile>
          <ExcludeByAttribute>CompilerGeneratedAttribute,GeneratedCodeAttribute</ExcludeByAttribute>
          <SingleHit>false</SingleHit>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

- [ ] **Step 4: 还原并验证两个项目都生成报告**

Run:

```powershell
dotnet restore .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --settings .\coverage.runsettings --collect:'XPlat Code Coverage' --results-directory .\TestResults\Coverage\unit
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --settings .\coverage.runsettings --collect:'XPlat Code Coverage' --results-directory .\TestResults\Coverage\headless
```

Expected: 两次测试均退出 `0`，两个结果目录中分别且仅有一份 `coverage.cobertura.xml`。

- [ ] **Step 5: 提交采集配置**

```powershell
git add coverage.runsettings tests/Cafe.Launcher.Avalonia.HeadlessTests/Cafe.Launcher.Avalonia.HeadlessTests.csproj
git commit -m "test(coverage): 启用双测试项目覆盖率采集"
```

### Task 2: 实现项目级合并与阈值检查

**Files:**
- Create: `coverage.ps1`

- [ ] **Step 1: 记录入口缺失**

Run:

```powershell
.\coverage.ps1
```

Expected: PowerShell 报告 `coverage.ps1` 不存在。

- [ ] **Step 2: 实现脚本入口和测试执行**

脚本必须设置：

```powershell
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$threshold = 0.70
$resultsRoot = Join-Path $PSScriptRoot 'TestResults\Coverage'
```

删除并重新创建 `$resultsRoot`，随后使用参数数组分别调用两个精确项目路径：

```powershell
dotnet test $project -c Debug --no-restore --settings $runsettings --collect:'XPlat Code Coverage' --results-directory $projectResults
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

每个项目目录必须用以下方式取得唯一报告；数量不是 `1` 时抛出错误：

```powershell
$reports = @(Get-ChildItem -LiteralPath $projectResults -Recurse -Filter 'coverage.cobertura.xml')
if ($reports.Count -ne 1) {
    throw "Expected exactly one Cobertura report in '$projectResults', found $($reports.Count)."
}
```

- [ ] **Step 3: 实现手写 C# 行合并**

对每份 XML 遍历 `coverage.packages.package.classes.class`。将 `class.filename` 中的 `/` 和 `\` 统一为当前平台目录分隔符，并通过 `[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relativePath))` 得到绝对路径。

只接受同时满足以下条件的文件：

```powershell
$extension -eq '.cs'
$fullPath.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)
-not $relativePath.StartsWith('obj\', [StringComparison]::OrdinalIgnoreCase)
Test-Path -LiteralPath $fullPath -PathType Leaf
```

行键必须是 `"$fullPath|$lineNumber"`。同一键在任一报告的 `hits` 大于 `0` 即为覆盖，禁止把同一生产代码行累计两次。

- [ ] **Step 4: 实现分支合并**

只处理 `line.branch -eq 'True'` 的行，遍历其 `conditions.condition`。分支键必须由以下精确字段组成：

```powershell
"$fullPath|$lineNumber|$($condition.number)|$($condition.type)"
```

`condition.coverage` 去除 `%` 后转换为整数；大于 `0` 即为覆盖。同一分支键在任一报告命中即为覆盖。不得直接相加两个报告根节点的 `branches-covered` 或 `branches-valid`。

- [ ] **Step 5: 输出摘要并检查阈值**

输出：

```text
Handwritten C# line coverage: NN.NN% (covered/valid)
Handwritten C# branch coverage: NN.NN% (covered/valid)
Unit report: <absolute path>
Headless report: <absolute path>
```

有效行数或有效分支数为 `0` 时抛出错误。任一比率低于 `0.70` 时写入错误并 `exit 1`；两者均达到阈值时 `exit 0`。

- [ ] **Step 6: 执行脚本并保存基线输出**

Run:

```powershell
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
.\coverage.ps1
```

Expected: 两个测试项目都完成，脚本输出两项项目级指标。若指标未达到 70%，退出 `1` 是此步骤的有效基线结果；不得在补测前降低阈值或扩大排除范围。

- [ ] **Step 7: 提交合并脚本**

```powershell
git add coverage.ps1
git commit -m "test(coverage): 添加项目级覆盖率阈值检查"
```

### Task 3: 补强 Headless 平台交互覆盖

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/OverlayFocusBehaviorTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/SystemTrayServiceTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`
- Modify only after RED proves necessary: `Views/OverlayFocusBehavior.cs`, `Services/SystemTrayService.cs`, `Views/MainWindow.axaml.cs`

- [ ] **Step 1: 从合并报告提取三个文件的精确未覆盖行和条件**

Run:

```powershell
[xml]$unit = Get-Content -Raw (Get-ChildItem .\TestResults\Coverage\unit -Recurse -Filter coverage.cobertura.xml).FullName
[xml]$headless = Get-Content -Raw (Get-ChildItem .\TestResults\Coverage\headless -Recurse -Filter coverage.cobertura.xml).FullName
$targets = 'Views/OverlayFocusBehavior.cs','Services/SystemTrayService.cs','Views/MainWindow.axaml.cs'
@($unit,$headless).coverage.packages.package.classes.class |
  Where-Object { $_.filename -in $targets } |
  ForEach-Object { $_.lines.line | Where-Object { [int]$_.hits -eq 0 -or ($_.branch -eq 'True' -and $_.'condition-coverage' -notlike '100%*') } } |
  Select-Object number,hits,branch,condition-coverage
```

Expected: 输出成为后续测试的唯一目标清单；不得根据方法名推断未覆盖分支。

- [ ] **Step 2: 为每个精确分支写一个最小 Headless 测试**

测试命名格式：

```csharp
[AvaloniaFact]
public void Method_WhenExactPrecondition_ExpectedObservableResult()
```

使用真实 `Window`、`Control`、`Dispatcher.UIThread.RunJobs()` 和现有 `TestTrayPlatform`。每个测试只验证一个可观察行为；每个显示的窗口必须在 `finally` 中关闭。

- [ ] **Step 3: 验证 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
```

Expected: 若测试描述现有正确行为，应先通过并作为覆盖补强；若测试暴露缺少的隔离边界或错误行为，必须因该精确行为失败，而不是编译或初始化错误。

- [ ] **Step 4: 仅对真实 RED 实施最小生产改动**

不得创建第二个托盘接口。若静态依赖阻止测试，只提取该依赖的单一职责接口，并通过现有构造函数链注入；不得增加测试专用方法。

- [ ] **Step 5: 验证 Headless 测试和覆盖率**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
.\coverage.ps1
```

Expected: Headless 测试失败数为 `0`；目标文件的未覆盖清单减少。

- [ ] **Step 6: 提交 Headless 测试**

```powershell
git add tests/Cafe.Launcher.Avalonia.HeadlessTests Views/OverlayFocusBehavior.cs Services/SystemTrayService.cs Views/MainWindow.axaml.cs
git commit -m "test(ui): 补充平台交互分支覆盖"
```

只暂存实际修改的生产文件；未修改的路径不得加入提交。

### Task 4: 补强下载与 URL 安全分支

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/RemoteHttpUrlValidatorTests.cs`
- Modify only after RED: `Services/GameDownloadService.cs`, `Services/FileDownloadService.cs`, `Services/RemoteHttpUrlValidator.cs`, `Services/RemoteHttpRequestService.cs`

- [ ] **Step 1: 从合并报告列出上述四个生产文件的精确未覆盖条件编号**

沿用 Task 3 的 XML 查询，将 `$targets` 精确替换为：

```powershell
$targets = 'Services/GameDownloadService.cs','Services/FileDownloadService.cs','Services/RemoteHttpUrlValidator.cs','Services/RemoteHttpRequestService.cs'
```

- [ ] **Step 2: 逐个写测试**

优先复用 `GameDownloadServiceTests` 中现有的 `HttpMessageHandler`、`IFileDownloadService` 测试实现和临时目录模式。URL 测试必须使用报告显示的精确输入类别；不得从相似地址格式推断额外类别。

- [ ] **Step 3: 每次验证 RED 或既有行为覆盖**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GameDownloadServiceTests|FullyQualifiedName~RemoteHttpUrlValidatorTests"
```

Expected: 行为缺陷对应测试先失败；纯覆盖测试通过。任何失败必须与测试名称描述的结果一致。

- [ ] **Step 4: 对真实失败实施最小修复并验证 GREEN**

Run the same filtered command. Expected: 失败数为 `0`。

- [ ] **Step 5: 重新运行项目级覆盖率**

Run:

```powershell
.\coverage.ps1
```

Expected: 下载和 URL 安全文件的未覆盖分支减少；阈值仍按 70% 固定。

- [ ] **Step 6: 提交**

```powershell
git add tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs tests/Cafe.Launcher.Avalonia.Tests/RemoteHttpUrlValidatorTests.cs
git add Services/GameDownloadService.cs Services/FileDownloadService.cs Services/RemoteHttpUrlValidator.cs Services/RemoteHttpRequestService.cs
git commit -m "test(services): 补充下载与远程地址安全分支"
```

只暂存实际修改文件。

### Task 5: 补强启动、卸载、主题和 ViewModel 分支

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/InstallationOperationStateTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- Modify: 合并报告明确指出的现有 ViewModel 测试文件
- Modify only after RED: 报告和失败测试共同指向的精确生产文件

- [ ] **Step 1: 提取精确未覆盖条件**

目标文件固定从以下集合开始：

```powershell
$targets = @(
  'Services/GameLaunchService.cs',
  'Services/GameUninstallService.cs',
  'Services/ThemeColorExtractionService.cs',
  'ViewModels/SettingsAppearanceViewModel.cs',
  'ViewModels/SettingsViewModel.cs',
  'ViewModels/MainWindowViewModel.cs',
  'ViewModels/ResourcePanelViewModel.cs'
)
```

使用 Task 3 的查询输出行号和条件覆盖。只测试输出中存在的分支。

- [ ] **Step 2: 启动和卸载测试按现有构造方式补充**

在 `InstallationOperationStateTests` 中复用现有 `GameLaunchService`、`GameUninstallService` 创建方式。每个测试构造独立临时目录，并在 `finally` 中删除。禁止实际启动游戏进程或删除清单之外的文件。

- [ ] **Step 3: 主题与 ViewModel 测试按公开状态补充**

在 `MainWindowViewModelTests` 中复用 `SettingsAppearanceViewModel`、`ThemeColorExtractionService.ExtractPaletteFromBgraBuffer` 及现有 ViewModel 工厂。断言公开属性、返回值、集合内容或事件，不通过反射直接调用私有方法。

- [ ] **Step 4: 验证 RED、实施最小修复、验证 GREEN**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~InstallationOperationStateTests|FullyQualifiedName~MainWindowViewModelTests"
```

Expected: 每个行为修复都有先失败后通过的记录；纯覆盖测试直接通过。最终失败数为 `0`。

- [ ] **Step 5: 运行覆盖率并执行阈值循环**

Run:

```powershell
.\coverage.ps1
```

若任一指标低于 70%，按“缺失分支数降序”列出手写 C# 文件，只选择报告中的首个文件，重复“精确未覆盖条件 → 单一行为测试 → RED/GREEN → 覆盖率”循环。达到两项 70% 后立即停止，不添加与阈值无关的测试或重构。

- [ ] **Step 6: 提交**

```powershell
git add tests/Cafe.Launcher.Avalonia.Tests
git add Services ViewModels
git commit -m "test(core): 提升核心业务分支覆盖率"
```

提交前用 `git diff --cached --name-only` 删除所有未实际属于本任务的暂存文件。

### Task 6: 接入完整验证并完成验收

**Files:**
- Modify: `verify.ps1`

- [ ] **Step 1: 在现有 Debug 两个测试步骤之后调用覆盖率脚本**

加入：

```powershell
& "$PSScriptRoot\coverage.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`coverage.ps1` 自己运行两个测试项目，因此为避免 `verify.ps1` 重复测试，应将现有两个无覆盖率的 `dotnet test` 调用替换为该调用；保留 Debug build、win-x64 restore 和 Release build 顺序。

- [ ] **Step 2: 执行完整验证**

Run:

```powershell
.\verify.ps1
```

Expected:

- Debug build：0 warnings，0 errors；
- 单元测试：0 failed；
- Headless 测试：0 failed；
- 手写 C# 行覆盖率：至少 70%；
- 手写 C# 分支覆盖率：至少 70%；
- Release build：0 warnings，0 errors；
- 进程退出码：`0`。

- [ ] **Step 3: 单独复跑最终覆盖率**

Run:

```powershell
.\coverage.ps1
```

Expected: 输出最终两项精确指标和两份报告绝对路径，退出码为 `0`。

- [ ] **Step 4: 检查差异**

Run:

```powershell
git diff --check
git status --short
git diff --stat
```

Expected: `git diff --check` 无输出；没有 `TestResults`、Cobertura XML、`bin` 或 `obj` 进入 Git 状态。

- [ ] **Step 5: 提交验证入口**

```powershell
git add verify.ps1
git commit -m "test(coverage): 将覆盖率阈值接入完整验证"
```

- [ ] **Step 6: 最终提交审计**

Run:

```powershell
git status --short
git log -5 --oneline
```

Expected: 工作区干净；本计划产生的提交均符合 Conventional Commits。
