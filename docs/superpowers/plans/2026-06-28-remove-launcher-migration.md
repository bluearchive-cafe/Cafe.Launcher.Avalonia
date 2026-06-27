# Remove Old Launcher Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完整删除旧启动器自动迁移功能及 LevelDB 原生依赖，同时保留手动选择已安装游戏目录的能力。

**Architecture:** 从应用组合根、主窗口编排、设置模型、界面、本地化和 Escape 策略中移除迁移模块。设置页的手动目录选择继续通过 `GameInstallationPath` 规范化目录，不再读取旧启动器数据。

**Tech Stack:** .NET 10、Avalonia 12、CommunityToolkit.Mvvm、xUnit、PowerShell

---

### Task 1: 删除 LevelDB 和旧启动器读取模块

**Files:**
- Delete: `Services/LevelDbReader.cs`
- Delete: `Services/OldLauncherDetectionService.cs`
- Delete: `Services/OriginalLauncherMigrationService.cs`
- Delete: `tests/Cafe.Launcher.Avalonia.Tests/LevelDbReaderTests.cs`
- Delete: `tests/Cafe.Launcher.Avalonia.Tests/OldLauncherDetectionServiceTests.cs`
- Modify: `Cafe.Launcher.Avalonia.csproj`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/Cafe.Launcher.Avalonia.Tests.csproj`
- Modify: `Constants/GamePaths.cs`

- [ ] **Step 1: 删除只覆盖待删除实现的测试**

删除 `LevelDbReaderTests.cs` 和 `OldLauncherDetectionServiceTests.cs`。这些测试的接口随模块一起消失，不保留替代测试。

- [ ] **Step 2: 删除三个生产模块**

删除 `LevelDbReader.cs`、`OldLauncherDetectionService.cs` 和 `OriginalLauncherMigrationService.cs`。

- [ ] **Step 3: 删除项目依赖**

从主项目删除以下完整配置：

```xml
<ProjectReference Include="..\leveldb.net\Leveldb.net\LevelDB.NET.csproj" />
<TrimmerRootAssembly Include="LevelDB.NET" />
```

以及 `..\leveldb.net\runtimes\...\leveldb.dll` 的两个 `Content` 项。

从测试项目删除 `LevelDB.NET.csproj` 引用及两个原生 DLL `Content` 项，只保留：

```xml
<ProjectReference Include="..\..\Cafe.Launcher.Avalonia.csproj" />
```

- [ ] **Step 4: 删除旧启动器路径常量**

从 `GamePaths` 删除：

```csharp
public const string OldLauncherAppName = "BlueArchive_JP_Gamelauncher";
```

- [ ] **Step 5: 验证依赖已经消失**

Run:

```powershell
rg -n "LevelDB|leveldb\.net|leveldb\.dll|OldLauncherAppName" Cafe.Launcher.Avalonia.csproj tests Constants Services
```

Expected: 无匹配。

### Task 2: 删除迁移向导及主窗口编排

**Files:**
- Delete: `ViewModels/MigrationWizardViewModel.cs`
- Delete: `tests/Cafe.Launcher.Avalonia.Tests/MigrationWizardViewModelTests.cs`
- Modify: `Services/ServiceConfiguration.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Views/MainWindow.axaml.cs`
- Modify: `Views/MainWindowDialogsOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: 删除迁移向导测试和实现**

删除 `MigrationWizardViewModelTests.cs` 和 `MigrationWizardViewModel.cs`。

- [ ] **Step 2: 从组合根删除注册**

从 `ServiceConfiguration.AddLauncherServices()` 删除：

```csharp
services.AddSingleton<OldLauncherDetectionService>();
services.AddTransient<MigrationWizardViewModel>();
```

同时删除只描述迁移注册的注释。

- [ ] **Step 3: 从主窗口 ViewModel 删除迁移流程**

删除：

- `oldLauncherService` 字段和构造参数
- `MigrationWizard` 属性和构造函数赋值
- `InitializeAsync()` 中首次启动检测及提前返回
- `RefreshAsync()` 中 `OriginalLauncherMigrationService.TryGetGamePath()` 自动迁移
- `WireChildren()` 和 `Dispose()` 中迁移事件订阅
- `HandleMigrationAppliedAsync()` 与 `HandleMigrationSkippedAsync()`
- 窗口交互状态中的迁移可见性和 `SkipMigration` 分支

`InitializeAsync()` 保留一次性初始化保护，然后直接执行：

```csharp
await RefreshAsync(cancellationToken);
```

- [ ] **Step 4: 删除界面组合**

从 `MainWindow.axaml.cs` 删除 `MigrationWizard.PickGameFolderAsync` 委托配置。

从 `MainWindowDialogsOverlay.axaml` 删除注释 `Migration wizard dialog` 开始的整个迁移弹窗 `Grid`，不得改动其他弹窗。

- [ ] **Step 5: 更新仍保留的测试构造**

从 `MainWindowViewModelTests` 的构造辅助方法中删除 `OldLauncherDetectionService` 和 `MigrationWizardViewModel` 实参，并删除迁移状态设置。

从 Headless 测试删除：

```csharp
context.ViewModel.MigrationWizard.IsVisible = false;
```

- [ ] **Step 6: 运行主窗口相关测试**

Run:

```powershell
dotnet test --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~MainWindowHeadlessTests"
```

Expected: 全部通过。

### Task 3: 简化手动选择已安装游戏流程

**Files:**
- Modify: `ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: 修改目录选择起点**

将：

```csharp
var startPath = OriginalLauncherMigrationService.TryGetGamePath()
    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
```

替换为：

```csharp
var startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
```

保持目录规范化、保存设置和刷新状态逻辑不变。

- [ ] **Step 2: 运行设置相关测试**

Run:

```powershell
dotnet test --filter "FullyQualifiedName~SettingsEditorTests"
```

Expected: 全部通过。

### Task 4: 删除迁移设置字段和 Escape 状态

**Files:**
- Modify: `Models/LauncherStateModels.cs`
- Modify: `Services/WindowEscapeStrategy.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/SettingsEditorTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/WindowEscapeStrategyTests.cs`

- [ ] **Step 1: 删除设置字段**

从 `LauncherSettings` 删除：

```csharp
[ObservableProperty]
[property: JsonPropertyName("hasCompletedFirstLaunchWizard")]
private bool hasCompletedFirstLaunchWizard;
```

删除测试中对 `HasCompletedFirstLaunchWizard` 和 JSON 键 `hasCompletedFirstLaunchWizard` 的设置及断言。

- [ ] **Step 2: 删除 Escape 迁移分支**

从 `WindowEscapeAction` 删除 `SkipMigration`，从 `WindowInteractionState` 删除 `IsMigrationVisible`，并从 `Decide()` 删除迁移判断。

删除 `WindowEscapeStrategyTests` 中仅验证迁移优先级或迁移动作的测试数据。保留并运行其余优先级测试。

- [ ] **Step 3: 验证旧 JSON 兼容**

在 `LauncherSettingsServiceTests` 中保留或增加一个使用精确 JSON 键的测试输入：

```json
{
  "hasCompletedFirstLaunchWizard": true
}
```

读取后断言服务未抛出异常，并返回 `LauncherSettings`。该测试证明旧设置文件中的未知字段被忽略。

- [ ] **Step 4: 运行状态相关测试**

Run:

```powershell
dotnet test --filter "FullyQualifiedName~LauncherSettingsServiceTests|FullyQualifiedName~SettingsEditorTests|FullyQualifiedName~WindowEscapeStrategyTests"
```

Expected: 全部通过。

### Task 5: 删除迁移本地化和文档

**Files:**
- Modify: `Assets/Locales/en.json`
- Modify: `Assets/Locales/zh-Hans.json`
- Modify: `Assets/Locales/ja.json`
- Modify: `Services/LocalizationService.cs`
- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: 删除精确本地化键**

从三份 JSON 删除：

```text
migrationWizardTitle
migrationWizardDescription
migrationGamePathLabel
migrationGamePathBrowse
migrationProxyLabel
migrationCloseBehaviorLabel
migrationClickCodeFound
migrationLevelDbFailed
migrationGamePathNotFound
migrationSkip
migrationApply
migrationApplied
migrationNoOldLauncher
```

从 `LocalizedStrings` 字段和 `Apply()` 删除对应的 `Migration*` 属性赋值。

- [ ] **Step 2: 更新项目文档**

从 README、AGENTS.md 和 CLAUDE.md 删除：

- 首次迁移功能介绍和数据流
- 四个已删除模块的说明
- 三个已删除测试类
- 旧启动器路径常量说明
- LevelDB 读取实现说明

保持三个文档中其余架构事实不变。

- [ ] **Step 3: 搜索残留引用**

Run:

```powershell
rg -n "MigrationWizard|OldLauncher|OriginalLauncherMigration|LevelDbReader|HasCompletedFirstLaunchWizard|hasCompletedFirstLaunchWizard|migrationWizard|migrationGamePath|migrationProxy|migrationClose|migrationClick|migrationLevelDb|migrationSkip|migrationApply|migrationNoOld|leveldb\.net|LevelDB\.NET" -S --glob "!bin/**" --glob "!obj/**" --glob "!docs/superpowers/**"
```

Expected: 无匹配。

### Task 6: 完整验证和提交

**Files:**
- Verify: all changed files

- [ ] **Step 1: 检查补丁格式**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` 无输出；状态只包含本计划范围内的文件。

- [ ] **Step 2: 运行完整测试**

Run:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet test
```

Expected: 退出码 0，0 个失败测试。

- [ ] **Step 3: 运行 Debug 和 Release 构建**

Run:

```powershell
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
```

Expected: 每次构建均为 0 warnings、0 errors。

- [ ] **Step 4: 验证 self-contained publish**

Run:

```powershell
$publishDir = Join-Path $env:TEMP "CafeLauncherPublishMigrationRemoval"
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 --self-contained true -o $publishDir
Get-ChildItem -LiteralPath $publishDir -Recurse |
    Where-Object { $_.Name -match 'leveldb' }
```

Expected: 发布成功；LevelDB 搜索无输出。

- [ ] **Step 5: 提交实现**

Run:

```powershell
git add --all
git commit -m "refactor: 移除旧启动器迁移功能"
```

Expected: 创建一个 Conventional Commit，工作区干净。
