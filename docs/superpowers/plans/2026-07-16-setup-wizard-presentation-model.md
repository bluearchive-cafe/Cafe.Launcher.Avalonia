# 设置向导展示模型修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复设置序列化兼容性、向导缺失的推荐/摘要/路径说明，以及审查发现的日志和代码规范问题。

**Architecture:** 在 `Features/SetupWizard` 中定义展示模型；`SetupWizardViewModel` 保留业务状态，并将其投影为展示模型；XAML 只绑定这些模型。

**Tech Stack:** .NET 10、Avalonia、CommunityToolkit.Mvvm、xUnit v3、Avalonia.Headless.XUnit。

## Global Constraints

- 不改变向导步骤顺序、下载源选择规则、设置默认值或安装路径有效性规则。
- 所有用户可见文本必须同时写入四个 `Assets/Locales/*.json` 文件。
- 每项行为变更必须先有失败的回归测试。
- 设置 JSON 属性顺序是兼容契约；既有属性顺序不得改变。

---

### Task 1: 保持设置序列化顺序

**Files:**
- Modify: `Models/LauncherStateModels.cs:120-230`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs`

**Interfaces:**
- Produces: `LauncherSettings` 既有字段顺序不变，`resourcePanelUidSource` 是最后一个字段。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void LauncherSettings_Serialize_LeavesExistingPropertyOrderUnchanged()
{
    var names = JsonDocument.Parse(JsonSerializer.Serialize(new LauncherSettings()))
        .RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Assert.True(Array.IndexOf(names, "updateChannel") < Array.IndexOf(names, "logLevel"));
    Assert.Equal("resourcePanelUidSource", names[^1]);
}
```

- [ ] **Step 2: 验证 RED**

Run: `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LauncherSettings_Serialize_LeavesExistingPropertyOrderUnchanged" --no-restore`

Expected: FAIL，因为新字段位于 `updateChannel` 前。

- [ ] **Step 3: 最小实现与 GREEN**

将 `[JsonPropertyName("resourcePanelUidSource")]` 声明移到 `LogLevel` 后，不改复制构造函数；重跑 Step 2，Expected: PASS。

- [ ] **Step 4: 提交**

```powershell
git add Models/LauncherStateModels.cs tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs
git commit -m "fix(settings): 保持配置字段序列化顺序"
```

### Task 2: 建立展示模型及资源

**Files:**
- Create: `Features/SetupWizard/SetupWizardDownloadSourceItem.cs`
- Create: `Features/SetupWizard/SetupWizardGamePathPresentation.cs`
- Modify: `Features/SetupWizard/SetupWizardStepItem.cs`
- Modify: `Assets/Locales/en.json`
- Modify: `Assets/Locales/ja.json`
- Modify: `Assets/Locales/zh-Hans.json`
- Modify: `Assets/Locales/zh-Hant.json`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/SetupWizardStepItemTests.cs`

**Interfaces:**
- Produces: `SetupWizardDownloadSourceItem(string Code, string DisplayName, bool IsRecommended, string RecommendationReason)`。
- Produces: `SetupWizardGamePathPresentation(string Title, string Description)`。
- Produces: 可观察的 `SetupWizardStepItem.Summary`，默认空字符串。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void Constructor_SetsSummaryToEmpty()
{
    var item = new SetupWizardStepItem(0, "Language");
    Assert.Equal(string.Empty, item.Summary);
}
```

- [ ] **Step 2: 验证 RED**

Run: `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardStepItemTests.Constructor_SetsSummaryToEmpty" --no-restore`

Expected: FAIL，因为 `Summary` 尚不存在。

- [ ] **Step 3: 最小实现与 GREEN**

```csharp
public sealed record SetupWizardDownloadSourceItem(
    string Code, string DisplayName, bool IsRecommended, string RecommendationReason);
public sealed record SetupWizardGamePathPresentation(string Title, string Description);
```

向四个 locale JSON 增加推荐标签/原因和路径状态标题/说明；添加 `Summary` 后重跑 Step 2，Expected: PASS。

- [ ] **Step 4: 提交**

```powershell
git add Features/SetupWizard Assets/Locales tests/Cafe.Launcher.Avalonia.Tests/SetupWizardStepItemTests.cs
git commit -m "feat(setup): 添加向导展示模型"
```

### Task 3: 投影业务状态

**Files:**
- Modify: `ViewModels/SetupWizardViewModel.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2 的模型。
- Produces: `IReadOnlyList<SetupWizardDownloadSourceItem> DownloadSources` 和 `SetupWizardGamePathPresentation GamePathPresentation`。

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public void DownloadSources_SimplifiedChinese_MarksCafeAsRecommended()
{
    var vm = CreateViewModel();
    vm.Language = LauncherLanguages.SimplifiedChinese;
    var cafe = Assert.Single(vm.DownloadSources, item => item.Code == PatchUrlGroups.Cafe);
    Assert.True(cafe.IsRecommended);
    Assert.NotEmpty(cafe.RecommendationReason);
}
```

另增加测试：进入第二步后 `Steps[0].Summary` 非空；`CorruptedInstallation` 时 `GamePathPresentation.Title` 与 `.Description` 非空。

- [ ] **Step 2: 验证 RED**

Run: `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardViewModelTests.DownloadSources_SimplifiedChinese_MarksCafeAsRecommended" --no-restore`

Expected: FAIL，因为 `DownloadSources` 尚不存在。

- [ ] **Step 3: 最小实现与 GREEN**

在 `RefreshSteps()` 中给已完成步骤设置语言、路径、下载源或代理摘要；当前/锁定步骤置空。`RefreshDownloadSources()` 只对 `zh-Hans`/`zh-Hant` 将 Cafe 标记推荐。`GamePathPresentation` 用现有 `GamePathStatus` 映射本地化标题/说明，并在状态/语言变更时通知。运行本任务的三个测试，Expected: PASS。

- [ ] **Step 4: 提交**

```powershell
git add ViewModels/SetupWizardViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs
git commit -m "feat(setup): 投影向导引导状态"
```

### Task 4: 呈现展示模型

**Files:**
- Modify: `Views/SetupWizardOverlay.axaml`
- Modify: `Views/Styles/SetupWizard.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

**Interfaces:**
- Consumes: `Steps[*].Summary`、`DownloadSources`、`GamePathPresentation`。

- [ ] **Step 1: 写失败 UI 契约测试**

```csharp
[Fact]
public void SetupWizardOverlay_UsesPresentationModelBindings()
{
    var xaml = File.ReadAllText(SetupWizardOverlayPath);
    Assert.Contains("{Binding Summary}", xaml);
    Assert.Contains("{Binding DownloadSources}", xaml);
    Assert.Contains("{Binding GamePathPresentation.Description}", xaml);
}
```

- [ ] **Step 2: 验证 RED**

Run: `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests.SetupWizardOverlay_UsesPresentationModelBindings" --no-restore`

Expected: FAIL，因为目前没有这些绑定。

- [ ] **Step 3: 最小实现与 GREEN**

步骤模板仅在摘要非空时显示摘要；下载源由 `DownloadSources` 呈现，推荐标签仅在 `IsRecommended` 时显示；路径区绑定标题和说明。使用既有样式令牌。重跑 Step 2，Expected: PASS；随后执行对应 Headless 测试。

- [ ] **Step 4: 提交**

```powershell
git add Views/SetupWizardOverlay.axaml Views/Styles/SetupWizard.axaml tests/Cafe.Launcher.Avalonia.HeadlessTests tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "feat(setup): 呈现向导引导信息"
```

### Task 5: 修复同步日志与规范

**Files:**
- Modify: `Services/EasterEggAudioService.cs`
- Modify: `Services/LauncherApiClient.cs`
- Modify: `Controls/ConfirmDialog.axaml.cs`
- Modify: `Controls/LoadingOverlay.axaml.cs`
- Modify: `Controls/SettingRow.axaml.cs`
- Modify: `Converters/ResourcePanelStatusToBrushConverter.cs`
- Modify: `Services/LanguageFontFamilyService.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/EasterEggTests.cs`

**Interfaces:**
- Produces: 同步异常路径使用 `LocalDiagnostics.LogSync(LogEntrySeverity.Error, "EasterEggAudio", message)`，不等待 `ErrorAsync`。

- [ ] **Step 1: 写失败测试与验证 RED**

为播放委托抛出异常的 `EasterEggAudioService` 添加回归测试，验证 `PlayKuyashi()` 不传播异常；运行：`dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~EasterEggTests" --no-restore`。Expected: PASS（现有保护行为）；随后在 Step 2 的代码审查中确认两个同步等待调用已删除。由于 `LocalDiagnostics.LogSync` 是静态入口且服务未注入日志替身，不能用永不完成的异步替身建立有效的 RED 测试。

- [ ] **Step 2: 最小实现与 GREEN**

将两个 `ErrorAsync(...).GetAwaiter().GetResult()` 换为 `LocalDiagnostics.LogSync(LogEntrySeverity.Error, "EasterEggAudio", $"...: {exception}")`；为列出的公开类型补充 XML `summary`；将 `packageRelativePrefix` 改为 `PackageRelativePrefix`。重跑 Step 1 命令，Expected: PASS；审查 diff 确认不再含 `.GetAwaiter().GetResult()`。

- [ ] **Step 3: 提交**

```powershell
git add Services Controls Converters/ResourcePanelStatusToBrushConverter.cs tests/Cafe.Launcher.Avalonia.Tests/EasterEggTests.cs
git commit -m "fix: 修复同步日志与代码规范"
```

### Task 6: 全量验证

**Files:** 无。

- [ ] **Step 1: 本地化与 UI 契约**

Run: `.\scripts\Test-LocalizationContract.ps1; .\dev.ps1 ui`

Expected: exit 0。

- [ ] **Step 2: 测试与构建**

Run: `.\test.ps1; .\build.ps1`

Expected: exit 0。

- [ ] **Step 3: 工作树检查**

Run: `git status --short`

Expected: 仅有预期改动，或各任务已提交后为空。
