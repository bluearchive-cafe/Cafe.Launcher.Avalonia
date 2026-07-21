# Advanced Settings Layout Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将高级设置页的三个日志文件操作归入独立 `SettingRow`，并在不改变共享设置框架的前提下与其他设置分类保持一致。

**Architecture:** 保留 `MainWindowSettingsOverlay`、共享 `SettingRow` 和现有命令不变，只在 `SettingsAdvancedSection` 中把行外按钮组改为第二个设置行。新增两项本地化文案，通过现有 `LocalizedStrings.Apply` 数据流暴露给 XAML；契约测试锁定结构，Headless 测试锁定支持窗口尺寸下的几何关系。

**Tech Stack:** .NET 10、C#、Avalonia XAML、CommunityToolkit.Mvvm、xUnit v3、Avalonia.Headless.XUnit、JSON 本地化资源。

## Global Constraints

- 保留设置内容顶部的启动状态摘要、对话框尺寸、左侧导航和底部取消/保存区域。
- 复用 `StackPanel.settings-group`、`SettingRow`、`flat-action`、`button-content`、`setting-control`、`LauncherSpacingSm`、`LauncherIconSm` 和 `LauncherControlHeightSetting`。
- 只新增语义尺寸令牌 `LauncherSettingRowActionMaxWidth=440`，确保长本地化按钮不会挤入文本列；不增加卡片、背景层、行边框、分隔线、颜色令牌、ViewModel 状态、服务或依赖。
- 不修改 `Controls/SettingRow.axaml`、其他设置分类、日志命令或设置保存逻辑。
- 默认 `1300×754` 与最小 `1024×640` 下，两个设置行及其操作必须保持正尺寸、无重叠且位于设置对话框内。
- 新增 `logFiles`、`logFilesDescription` 两个键，四种语言的键集合和格式占位符必须一致。
- 最终视觉基准为 `docs/superpowers/specs/assets/2026-07-16-settings-advanced-consistent-layout.png`，实现尺寸以现有令牌和 Headless 布局结果为准。

---

## File Structure

- `Assets/Locales/en.json`：英文“日志文件”标题和说明。
- `Assets/Locales/ja.json`：日文“日志文件”标题和说明。
- `Assets/Locales/zh-Hans.json`：简体中文“日志文件”标题和说明。
- `Assets/Locales/zh-Hant.json`：繁体中文“日志文件”标题和说明。
- `Services/LocalizationService.cs`：声明 `LogFiles`、`LogFilesDescription` 可观察属性，并在 `LocalizedStrings.Apply` 中赋值。
- `App.axaml`：定义日志多操作区复用的语义最大宽度。
- `Views/SettingsAdvancedSection.axaml`：把行外 `WrapPanel` 改为第二个 `SettingRow`，并在其操作区内使用受限宽度的 `WrapPanel`。
- `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`：验证四种语言的新键和 `LocalizedStrings` 映射。
- `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`：验证两个设置行、日志按钮归属、顺序和现有绑定。
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`：验证默认与最小窗口尺寸下的对齐、可达性和无重叠。

### Task 1: Add localized log-file row copy

**Files:**
- Modify: `Assets/Locales/en.json:161`
- Modify: `Assets/Locales/ja.json:161`
- Modify: `Assets/Locales/zh-Hans.json:161`
- Modify: `Assets/Locales/zh-Hant.json:161`
- Modify: `Services/LocalizationService.cs:224-235,501-523`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`

**Interfaces:**
- Consumes: `LocalizationService.T(string key)` and the existing `LocalizedStrings.Apply(LocalizationService localizer)` mapping pattern.
- Produces: generated string properties `LocalizedStrings.LogFiles` and `LocalizedStrings.LogFilesDescription`, both of type `string`, for compiled XAML bindings.

- [ ] **Step 1: Write the failing localization mapping test**

Add this test to `LocalizationServiceTests` before the existing game-path mapping theory:

```csharp
[Theory]
[InlineData(
    LauncherLanguages.English,
    "Log Files",
    "View, export, or open the directory containing logs")]
[InlineData(
    LauncherLanguages.SimplifiedChinese,
    "日志文件",
    "查看、导出或打开日志所在目录")]
[InlineData(
    LauncherLanguages.TraditionalChinese,
    "日誌檔案",
    "查看、匯出或開啟日誌所在目錄")]
[InlineData(
    LauncherLanguages.Japanese,
    "ログファイル",
    "ログを表示、エクスポート、または保存先フォルダーを開く")]
public void LocalizedStrings_WhenLogFileKeysApplied_MapsLocalizedValues(
    string language,
    string expectedTitle,
    string expectedDescription)
{
    var service = new LocalizationService();
    service.SetLanguage(language);
    var strings = new LocalizedStrings();

    strings.Apply(service);

    Assert.Equal(expectedTitle, strings.LogFiles);
    Assert.Equal(expectedDescription, strings.LogFilesDescription);
}
```

- [ ] **Step 2: Run the test to verify RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizedStrings_WhenLogFileKeysApplied_MapsLocalizedValues"
```

Expected: build fails with `CS1061` because `LocalizedStrings` does not yet expose `LogFiles` and `LogFilesDescription`.

- [ ] **Step 3: Add the two keys to all locale files in ordinal order**

Insert the keys immediately before `logFilterAll` in each JSON file:

```json
// Assets/Locales/en.json
"logFiles": "Log Files",
"logFilesDescription": "View, export, or open the directory containing logs",

// Assets/Locales/ja.json
"logFiles": "ログファイル",
"logFilesDescription": "ログを表示、エクスポート、または保存先フォルダーを開く",

// Assets/Locales/zh-Hans.json
"logFiles": "日志文件",
"logFilesDescription": "查看、导出或打开日志所在目录",

// Assets/Locales/zh-Hant.json
"logFiles": "日誌檔案",
"logFilesDescription": "查看、匯出或開啟日誌所在目錄",
```

The `//` file labels above are plan annotations only; do not add comments to the JSON files.

- [ ] **Step 4: Expose and populate the localized properties**

Add these fields after `openDataDirectory` in `LocalizedStrings`:

```csharp
[ObservableProperty] private string logFiles = "";
[ObservableProperty] private string logFilesDescription = "";
```

Add these assignments after `OpenDataDirectory = localizer.T("openDataDirectory");` in `Apply`:

```csharp
LogFiles = localizer.T("logFiles");
LogFilesDescription = localizer.T("logFilesDescription");
```

- [ ] **Step 5: Run focused and localization contract tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizedStrings_WhenLogFileKeysApplied_MapsLocalizedValues|FullyQualifiedName~LocaleFiles_HaveMatchingKeys|FullyQualifiedName~LocaleFiles_HaveMatchingFormatPlaceholders|FullyQualifiedName~LocaleFiles_KeepKeysSortedOrdinal"
.\scripts\Test-LocalizationContract.ps1
```

Expected: all four theory cases and all three locale-file facts pass; the PowerShell localization contract exits `0`.

- [ ] **Step 6: Commit the localization slice**

```powershell
git add -- Assets/Locales/en.json Assets/Locales/ja.json Assets/Locales/zh-Hans.json Assets/Locales/zh-Hant.json Services/LocalizationService.cs tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs
git commit -m "feat(i18n): 添加日志文件设置文案"
```

### Task 2: Move log actions into a dedicated SettingRow

**Files:**
- Modify: `App.axaml:44`
- Modify: `Views/SettingsAdvancedSection.axaml:15-56`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs:2410`
- Test: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs:263-290`

**Interfaces:**
- Consumes: `LocalizedStrings.LogFiles`, `LocalizedStrings.LogFilesDescription`, `LogViewer.OpenCommand`, `LogViewer.ExportCommand`, `WindowChrome.OpenDataDirectoryCommand`, and the existing `SettingRow.Action` content property.
- Produces: a second `SettingRow` whose right action area contains the three existing log buttons in purpose-based order.

- [ ] **Step 1: Write the failing XAML structure contract**

Add this fact near `SettingsAboutAndAdvancedActions_UsePurposeBasedOrderAndExclusiveOwnership`:

```csharp
[Fact]
public void AdvancedSettings_LogActionsBelongToDedicatedSettingRow()
{
    var document = XDocument.Load(ProjectFile("Views/SettingsAdvancedSection.axaml"));
    var group = document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "StackPanel"
            && HasClass(element, "settings-group"));
    var rows = group
        .Elements()
        .Where(element => element.Name.LocalName == "SettingRow")
        .ToList();

    Assert.Equal(2, rows.Count);
    var logFilesRow = rows[1];
    Assert.Equal(
        "{Binding Shell.I18n.LogFiles}",
        logFilesRow.Attribute("Title")?.Value);
    Assert.Equal(
        "{Binding Shell.I18n.LogFilesDescription}",
        logFilesRow.Attribute("Description")?.Value);

    var action = logFilesRow
        .Elements()
        .Single(element => element.Name.LocalName == "SettingRow.Action");
    var actionPanel = action
        .Elements()
        .Single(element => element.Name.LocalName == "WrapPanel");
    Assert.Equal(
        "{StaticResource LauncherSpacingSm}",
        actionPanel.Attribute("ItemSpacing")?.Value);
    Assert.Equal(
        "{StaticResource LauncherSpacingSm}",
        actionPanel.Attribute("LineSpacing")?.Value);
    Assert.Equal(
        "{StaticResource LauncherSettingRowActionMaxWidth}",
        actionPanel.Attribute("MaxWidth")?.Value);

    var app = XDocument.Load(ProjectFile("App.axaml"));
    var actionMaxWidth = app
        .Descendants()
        .Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key"
                && attribute.Value == "LauncherSettingRowActionMaxWidth"));
    Assert.Equal("440", actionMaxWidth.Value);

    var commands = actionPanel
        .Elements()
        .Where(element => element.Name.LocalName == "Button")
        .Select(element =>
            element.Attribute("Command")?.Value
            ?? throw new InvalidDataException("Advanced log action is missing Command."))
        .ToArray();
    Assert.Equal(
        [
            "{Binding LogViewer.OpenCommand}",
            "{Binding LogViewer.ExportCommand}",
            "{Binding WindowChrome.OpenDataDirectoryCommand}"
        ],
        commands);
    Assert.DoesNotContain(
        group.Elements(),
        element => element.Name.LocalName == "WrapPanel");
}
```

- [ ] **Step 2: Run the contract test to verify RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~AdvancedSettings_LogActionsBelongToDedicatedSettingRow"
```

Expected: FAIL because the current group contains one `SettingRow` and one row-level `WrapPanel`.

- [ ] **Step 3: Write the failing Headless geometry regression**

Add this theory after `SettingRow_RendersAllFields_WithExplicitActionProperty`:

```csharp
[AvaloniaTheory]
[InlineData(1300, 754)]
[InlineData(1024, 640)]
public void SettingsAdvanced_AtSupportedWindowSizes_AlignsDedicatedLogActionRow(
    double width,
    double height)
{
    using var context = CreateContext();
    context.Window.Width = width;
    context.Window.Height = height;
    OpenSettings(context);
    context.ViewModel.Settings.SelectedCategory = SettingsCategoryCodes.Advanced;
    Dispatcher.UIThread.RunJobs();

    var section = context.Window
        .GetVisualDescendants()
        .OfType<SettingsAdvancedSection>()
        .Single();
    var rows = section
        .GetVisualDescendants()
        .OfType<global::Cafe.Launcher.Avalonia.Controls.SettingRow>()
        .Where(row => row.IsEffectivelyVisible)
        .ToArray();

    Assert.Equal(2, rows.Length);
    var levelControl = rows[0]
        .GetVisualDescendants()
        .OfType<ComboBox>()
        .Single();
    var logButtons = rows[1]
        .GetVisualDescendants()
        .OfType<Button>()
        .ToArray();
    Assert.Equal(3, logButtons.Length);

    var levelTopLeft = levelControl.TranslatePoint(default, context.Window);
    Assert.NotNull(levelTopLeft);
    var levelRight = levelTopLeft.Value.X + levelControl.Bounds.Width;
    var logPresenter = rows[1].FindControl<ContentPresenter>("ActionPresenter");
    Assert.NotNull(logPresenter);
    var logPresenterTopLeft = logPresenter!.TranslatePoint(default, context.Window);
    Assert.NotNull(logPresenterTopLeft);
    var logPresenterRight = logPresenterTopLeft.Value.X + logPresenter.Bounds.Width;
    Assert.InRange(Math.Abs(levelRight - logPresenterRight), 0, 1);

    var description = rows[1].FindControl<TextBlock>("RowDescription");
    Assert.NotNull(description);
    var descriptionTopLeft = description!.TranslatePoint(default, context.Window);
    var firstButtonTopLeft = logButtons[0].TranslatePoint(default, context.Window);
    Assert.NotNull(descriptionTopLeft);
    Assert.NotNull(firstButtonTopLeft);
    Assert.True(
        descriptionTopLeft.Value.X + description.Bounds.Width
        <= firstButtonTopLeft.Value.X);

    AssertControlInsideWindow(levelControl, context.Window);
    Assert.All(logButtons, button => AssertControlInsideWindow(button, context.Window));
}
```

- [ ] **Step 4: Run the Headless test to verify RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~SettingsAdvanced_AtSupportedWindowSizes_AlignsDedicatedLogActionRow"
```

Expected: both cases FAIL at `Assert.Equal(2, rows.Length)` because the current page has only one visible `SettingRow`.

- [ ] **Step 5: Replace the row-level WrapPanel with the second SettingRow**

Replace the existing `WrapPanel` in `Views/SettingsAdvancedSection.axaml` with:

```xml
<controls:SettingRow IconKind="FolderOpen"
                     Title="{Binding Shell.I18n.LogFiles}"
                     Description="{Binding Shell.I18n.LogFilesDescription}">
    <controls:SettingRow.Action>
        <WrapPanel MaxWidth="{StaticResource LauncherSettingRowActionMaxWidth}"
                   ItemSpacing="{StaticResource LauncherSpacingSm}"
                   LineSpacing="{StaticResource LauncherSpacingSm}"
                   HorizontalAlignment="Right"
                   VerticalAlignment="Center">
            <Button Classes="flat-action"
                    Command="{Binding LogViewer.OpenCommand}"
                    AutomationProperties.Name="{Binding Shell.I18n.ViewLog}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="TextBoxOutline"
                                                Width="{StaticResource LauncherIconSm}"
                                                Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.ViewLog}"/>
                </StackPanel>
            </Button>
            <Button Classes="flat-action"
                    Command="{Binding LogViewer.ExportCommand}"
                    AutomationProperties.Name="{Binding Shell.I18n.ExportLogs}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="Export"
                                                Width="{StaticResource LauncherIconSm}"
                                                Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.ExportLogs}"/>
                </StackPanel>
            </Button>
            <Button Classes="flat-action"
                    Command="{Binding WindowChrome.OpenDataDirectoryCommand}"
                    AutomationProperties.Name="{Binding Shell.I18n.OpenDataDirectory}">
                <StackPanel Classes="button-content">
                    <materialIcons:MaterialIcon Kind="FolderOpen"
                                                Width="{StaticResource LauncherIconSm}"
                                                Height="{StaticResource LauncherIconSm}"/>
                    <TextBlock Text="{Binding Shell.I18n.OpenDataDirectory}"/>
                </StackPanel>
            </Button>
        </WrapPanel>
    </controls:SettingRow.Action>
</controls:SettingRow>
```

Add this token after `LauncherSettingRowContentMinWidth` in `App.axaml`:

```xml
<x:Double x:Key="LauncherSettingRowActionMaxWidth">440</x:Double>
```

Do not modify `Controls/SettingRow.axaml` or `Views/MainWindow.Styles.axaml`; the existing shared row and button styles already provide the selected design.

- [ ] **Step 6: Run focused structure and geometry tests to verify GREEN**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~AdvancedSettings_LogActionsBelongToDedicatedSettingRow|FullyQualifiedName~SettingsAboutAndAdvancedActions_UsePurposeBasedOrderAndExclusiveOwnership|FullyQualifiedName~SettingsSections_InteractiveControlsHaveLocalizedAutomationNames"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~SettingsAdvanced_AtSupportedWindowSizes_AlignsDedicatedLogActionRow|FullyQualifiedName~SettingRow_RendersAllFields_WithExplicitActionProperty"
```

Expected: all selected contract tests pass; both new geometry cases pass at `1300×754` and `1024×640`.

- [ ] **Step 7: Run the complete UI verification gate**

Run:

```powershell
.\scripts\Test-LocalizationContract.ps1
.\dev.ps1 ui
git diff --check
```

Expected: localization contract exits `0`; unit/UI style suite reports `94` passed and `0` failed; Headless suite reports `72` passed and `0` failed; `git diff --check` exits `0` with no whitespace errors.

- [ ] **Step 8: Compare the rendered page with the approved design boundary**

Run:

```powershell
dotnet run --project .\Cafe.Launcher.Avalonia.csproj
```

Open Settings → Advanced and verify:

- the top status summary remains unchanged;
- no card, row background, row border, divider, or new surface appears;
- “日志级别”和“日志文件”使用相同的图标列、文本列和右侧操作边界；
- the three buttons appear inside the “日志文件” row in View → Export → Open Directory order;
- the footer, navigation, save state and other settings categories remain unchanged.

Use `docs/superpowers/specs/assets/2026-07-16-settings-advanced-consistent-layout.png` only as an information-hierarchy reference; preserve the runtime tokens and exact existing product styling.

- [ ] **Step 9: Commit the layout slice**

```powershell
git add -- App.axaml Views/SettingsAdvancedSection.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs docs/superpowers/specs/2026-07-16-settings-advanced-layout-design.md docs/superpowers/plans/2026-07-16-settings-advanced-layout.md
git commit -m "fix(ui): 统一高级设置日志操作布局"
```

### Task 3: Final repository state check

**Files:**
- Verify only; no file changes expected.

**Interfaces:**
- Consumes: the two commits produced by Tasks 1 and 2.
- Produces: a clean working tree and fresh verification evidence ready for review.

- [ ] **Step 1: Verify commit scope and working tree cleanliness**

Run:

```powershell
git status --short
git log -2 --oneline
git show --stat --oneline HEAD~1
git show --stat --oneline HEAD
```

Expected: `git status --short` has no output; the latest two commits are the localization and advanced-layout commits; their stats contain only the files declared by Tasks 1 and 2.

- [ ] **Step 2: Record verification evidence in the handoff**

Report the exact outputs of `Test-LocalizationContract.ps1`, `dev.ps1 ui`, the two focused RED/GREEN cycles, the two final commit hashes, and whether the runtime visual comparison found any mismatch. Do not claim completion if any command failed or if the rendered page diverges from the approved consistency constraints.
