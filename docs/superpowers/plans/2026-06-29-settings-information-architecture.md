# Settings Information Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将设置页重组为左侧分类导航和独立分类内容，让任一设置最多通过一次分类选择即可找到，同时保持现有草稿、保存、预览和持久化行为。

**Architecture:** `SettingsViewModel` 保存仅限当前会话的分类 code，`SettingsOptionsViewModel` 提供本地化分类项。七个分类拆成显式 `UserControl`，继续共享 `MainWindowViewModel` 和同一个 `SettingsEditor.Current`，不新增设置状态副本或 ViewLocator。

**Tech Stack:** .NET 10、C#、Avalonia 12.0.4、CommunityToolkit.Mvvm 8.4.2、xUnit 2.9.3、Avalonia Headless XUnit 3.2.2。

---

## 文件结构

### 新建

- `Models/SettingsCategoryCodes.cs` — 七个精确分类 code 与规范化。
- `Views/SettingsGeneralSection.axaml`
- `Views/SettingsGeneralSection.axaml.cs`
- `Views/SettingsGameSection.axaml`
- `Views/SettingsGameSection.axaml.cs`
- `Views/SettingsDownloadNetworkSection.axaml`
- `Views/SettingsDownloadNetworkSection.axaml.cs`
- `Views/SettingsAppearanceSection.axaml`
- `Views/SettingsAppearanceSection.axaml.cs`
- `Views/SettingsNotificationsContentSection.axaml`
- `Views/SettingsNotificationsContentSection.axaml.cs`
- `Views/SettingsAdvancedSection.axaml`
- `Views/SettingsAdvancedSection.axaml.cs`
- `Views/SettingsAboutSection.axaml`
- `Views/SettingsAboutSection.axaml.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/SettingsCategoryCodesTests.cs`

### 修改

- `ViewModels/SettingsViewModel.cs` — 当前分类与可见状态。
- `ViewModels/SettingsOptionsViewModel.cs` — 本地化分类选项。
- `Services/LocalizationService.cs` — 分类名称和说明。
- `Assets/Locales/en.json`
- `Assets/Locales/zh-Hans.json`
- `Assets/Locales/ja.json`
- `Views/MainWindowSettingsOverlay.axaml` — 新设置 shell。
- `Views/MainWindow.Styles.axaml` — 侧栏、状态摘要和分类内容样式。
- `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`
- `AGENTS.md` — 更新设置页 View 文件说明。

## Task 1：建立分类 code 与会话状态

**Files:**
- Create: `Models/SettingsCategoryCodes.cs`
- Modify: `ViewModels/SettingsViewModel.cs`
- Create: `tests/Cafe.Launcher.Avalonia.Tests/SettingsCategoryCodesTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: 写分类 code 红灯测试**

```csharp
public sealed class SettingsCategoryCodesTests
{
    [Theory]
    [InlineData(SettingsCategoryCodes.General)]
    [InlineData(SettingsCategoryCodes.Game)]
    [InlineData(SettingsCategoryCodes.DownloadNetwork)]
    [InlineData(SettingsCategoryCodes.Appearance)]
    [InlineData(SettingsCategoryCodes.NotificationsContent)]
    [InlineData(SettingsCategoryCodes.Advanced)]
    [InlineData(SettingsCategoryCodes.About)]
    public void Normalize_WhenCodeIsKnown_ReturnsCode(string code)
    {
        Assert.Equal(code, SettingsCategoryCodes.Normalize(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("General")]
    [InlineData("unknown")]
    public void Normalize_WhenCodeIsUnknown_ReturnsGeneral(string? code)
    {
        Assert.Equal(SettingsCategoryCodes.General, SettingsCategoryCodes.Normalize(code));
    }
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~SettingsCategoryCodesTests"
```

Expected: FAIL，`SettingsCategoryCodes` 尚不存在。

- [ ] **Step 3: 实现精确 code**

```csharp
namespace Cafe.Launcher.Avalonia.Models;

public static class SettingsCategoryCodes
{
    public const string General = "general";
    public const string Game = "game";
    public const string DownloadNetwork = "download-network";
    public const string Appearance = "appearance";
    public const string NotificationsContent = "notifications-content";
    public const string Advanced = "advanced";
    public const string About = "about";

    public static string Normalize(string? code) => code switch
    {
        General or Game or DownloadNetwork or Appearance
            or NotificationsContent or Advanced or About => code,
        _ => General
    };
}
```

- [ ] **Step 4: 写 ViewModel 红灯测试**

```csharp
[Fact]
public void SettingsCategory_DefaultsToGeneralAndNormalizesUnknownCode()
{
    using var viewModel = CreateSettingsViewModel();

    Assert.Equal(SettingsCategoryCodes.General, viewModel.SelectedCategory);
    Assert.True(viewModel.IsGeneralCategorySelected);

    viewModel.SelectedCategory = "unknown";

    Assert.Equal(SettingsCategoryCodes.General, viewModel.SelectedCategory);
}

[Fact]
public void SettingsCategory_ChangesVisibilityWithoutChangingDraft()
{
    using var viewModel = CreateSettingsViewModel();
    viewModel.Editor.Current.Language = LauncherLanguages.Japanese;

    viewModel.SelectedCategory = SettingsCategoryCodes.Appearance;

    Assert.True(viewModel.IsAppearanceCategorySelected);
    Assert.False(viewModel.IsGeneralCategorySelected);
    Assert.Equal(LauncherLanguages.Japanese, viewModel.Editor.Current.Language);
    Assert.True(viewModel.IsSettingsDirty);
}
```

- [ ] **Step 5: 实现会话状态**

在 `SettingsViewModel` 增加：

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsGeneralCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsGameCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsDownloadNetworkCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsAppearanceCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsNotificationsContentCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsAdvancedCategorySelected))]
[NotifyPropertyChangedFor(nameof(IsAboutCategorySelected))]
private string selectedCategory = SettingsCategoryCodes.General;

public bool IsGeneralCategorySelected => SelectedCategory == SettingsCategoryCodes.General;
public bool IsGameCategorySelected => SelectedCategory == SettingsCategoryCodes.Game;
public bool IsDownloadNetworkCategorySelected => SelectedCategory == SettingsCategoryCodes.DownloadNetwork;
public bool IsAppearanceCategorySelected => SelectedCategory == SettingsCategoryCodes.Appearance;
public bool IsNotificationsContentCategorySelected => SelectedCategory == SettingsCategoryCodes.NotificationsContent;
public bool IsAdvancedCategorySelected => SelectedCategory == SettingsCategoryCodes.Advanced;
public bool IsAboutCategorySelected => SelectedCategory == SettingsCategoryCodes.About;

partial void OnSelectedCategoryChanged(string value)
{
    var normalized = SettingsCategoryCodes.Normalize(value);
    if (!string.Equals(value, normalized, StringComparison.Ordinal))
    {
        SelectedCategory = normalized;
    }
}
```

不得在 `LoadFromSnapshot()`、`SaveSettingsAsync()` 或 `DiscardChangesAsync()` 中重置分类。

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~SettingsCategoryCodesTests|FullyQualifiedName~MainWindowViewModelTests"
git add -- Models/SettingsCategoryCodes.cs ViewModels/SettingsViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/SettingsCategoryCodesTests.cs tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs
git commit -m "feat(settings): 增加设置分类会话状态"
```

Expected: PASS。

## Task 2：增加本地化分类导航

**Files:**
- Modify: `Assets/Locales/en.json`
- Modify: `Assets/Locales/zh-Hans.json`
- Modify: `Assets/Locales/ja.json`
- Modify: `Services/LocalizationService.cs`
- Modify: `ViewModels/SettingsOptionsViewModel.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`

- [ ] **Step 1: 写本地化红灯测试**

```csharp
[Theory]
[InlineData("en")]
[InlineData("zh-Hans")]
[InlineData("ja")]
public void SettingsCategories_HaveLocalizedNamesAndDescriptions(string language)
{
    var localizer = new LocalizationService();
    localizer.SetLanguage(language);

    foreach (var key in new[]
    {
        "settingsCategoryGeneral",
        "settingsCategoryGame",
        "settingsCategoryDownloadNetwork",
        "settingsCategoryAppearance",
        "settingsCategoryNotificationsContent",
        "settingsCategoryAdvanced",
        "settingsCategoryAbout",
        "settingsCategoryGeneralDescription",
        "settingsCategoryGameDescription",
        "settingsCategoryDownloadNetworkDescription",
        "settingsCategoryAppearanceDescription",
        "settingsCategoryNotificationsContentDescription",
        "settingsCategoryAdvancedDescription",
        "settingsCategoryAboutDescription"
    })
    {
        Assert.NotEqual(key, localizer.T(key));
    }
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~SettingsCategories_HaveLocalizedNamesAndDescriptions"
```

Expected: FAIL，分类键尚不存在。

- [ ] **Step 3: 增加三语言键**

在三个 JSON 文件加入上述 14 个精确键。中文名称固定为：

```text
常规
游戏
下载与网络
外观
通知与内容
高级
关于
```

英文和日文必须表达相同分类语义，不得复用值不匹配的旧键。

- [ ] **Step 4: 接入 `LocalizedStrings`**

为 14 个键增加 `[ObservableProperty]` 字段，并在 `LocalizedStrings.Apply()` 中逐项赋值。

- [ ] **Step 5: 增加分类选项**

`SettingsOptionsViewModel` 增加：

```csharp
public ObservableCollection<SettingOption> SettingsCategories { get; } = [];
```

在 `RefreshDisplayNames()` 中按固定顺序重建：

```csharp
SettingsCategories.Clear();
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.General,
    DisplayName = localizer.T("settingsCategoryGeneral")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.Game,
    DisplayName = localizer.T("settingsCategoryGame")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.DownloadNetwork,
    DisplayName = localizer.T("settingsCategoryDownloadNetwork")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.Appearance,
    DisplayName = localizer.T("settingsCategoryAppearance")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.NotificationsContent,
    DisplayName = localizer.T("settingsCategoryNotificationsContent")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.Advanced,
    DisplayName = localizer.T("settingsCategoryAdvanced")
});
SettingsCategories.Add(new SettingOption
{
    Code = SettingsCategoryCodes.About,
    DisplayName = localizer.T("settingsCategoryAbout")
});
```

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalizationServiceTests|FullyQualifiedName~SettingsOptionsViewModel"
git add -- Assets/Locales/en.json Assets/Locales/zh-Hans.json Assets/Locales/ja.json Services/LocalizationService.cs ViewModels/SettingsOptionsViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs
git commit -m "feat(settings): 增加本地化分类导航"
```

Expected: PASS。

## Task 3：提取常规、游戏、下载与网络 section

**Files:**
- Create: `Views/SettingsGeneralSection.axaml`
- Create: `Views/SettingsGeneralSection.axaml.cs`
- Create: `Views/SettingsGameSection.axaml`
- Create: `Views/SettingsGameSection.axaml.cs`
- Create: `Views/SettingsDownloadNetworkSection.axaml`
- Create: `Views/SettingsDownloadNetworkSection.axaml.cs`
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 写 XAML 归属红灯测试**

增加一个读取 XAML 文本的测试，精确断言：

```csharp
Assert.Contains("Settings.Editor.Current.Language", generalMarkup);
Assert.Contains("Settings.Editor.Current.CloseBehavior", generalMarkup);
Assert.Contains("Settings.Editor.Current.MotionMode", generalMarkup);

Assert.Contains("Settings.Editor.Current.GamePath", gameMarkup);
Assert.Contains("Settings.Editor.Current.LaunchCheckMode", gameMarkup);
Assert.Contains("Operations.RequestRepairCommand", gameMarkup);
Assert.Contains("Operations.RequestUninstallCommand", gameMarkup);

Assert.Contains("Settings.Editor.Current.ProxyMode", downloadMarkup);
Assert.Contains("Settings.Editor.Current.PatchUrlGroup", downloadMarkup);
Assert.Contains("Settings.Editor.Current.DownloadSpeedLimit", downloadMarkup);
Assert.Contains("Settings.Editor.Current.UpdateChannel", downloadMarkup);
```

同时断言这些绑定不再出现在 `MainWindowSettingsOverlay.axaml`。

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

Expected: FAIL，section 文件尚不存在。

- [ ] **Step 3: 创建三个 compiled-binding View**

每个 View 根元素使用：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Cafe.Launcher.Avalonia.ViewModels"
             x:DataType="vm:MainWindowViewModel">
```

将现有对应 `settings-row` 原样移动；不得修改绑定、命令、条件可见性、图标或说明文本。

code-behind 只包含：

```csharp
public partial class SettingsGeneralSection : UserControl
{
    public SettingsGeneralSection()
    {
        InitializeComponent();
    }
}
```

其他两个类使用各自精确类名。

- [ ] **Step 4: 在 overlay 临时引用**

在原滚动区中按顺序加入三个 View，并绑定对应 `IsVisible`。其他未提取内容暂时保留，
保证中间提交可编译。

- [ ] **Step 5: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
git add -- Views/SettingsGeneralSection.axaml Views/SettingsGeneralSection.axaml.cs Views/SettingsGameSection.axaml Views/SettingsGameSection.axaml.cs Views/SettingsDownloadNetworkSection.axaml Views/SettingsDownloadNetworkSection.axaml.cs Views/MainWindowSettingsOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "refactor(settings): 提取常规游戏与下载分类"
```

Expected: PASS，0 warnings、0 errors。

## Task 4：提取外观 section

**Files:**
- Create: `Views/SettingsAppearanceSection.axaml`
- Create: `Views/SettingsAppearanceSection.axaml.cs`
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 写外观完整性红灯测试**

断言新 View 包含以下精确绑定：

```text
Settings.Editor.Current.ThemeMode
Settings.Editor.Current.ThemeColorMode
Settings.Appearance.ThemeColorPaletteItems
Settings.Appearance.SelectedCustomThemeColor
Settings.Editor.Current.BackgroundSource
Settings.Editor.Current.BackgroundFit
Settings.Appearance.SelectedBackgroundFillColor
Settings.ChooseBackgroundImageCommand
Settings.ChooseBackgroundFolderCommand
Settings.ClearBackgroundCommand
```

断言包含现有四个条件可见状态：

```text
Settings.Appearance.IsWallpaperThemeColorSelected
Settings.Appearance.IsCustomThemeColorSelected
Settings.Appearance.IsBackgroundFitSelected
Settings.Appearance.IsCustomBackgroundSelected
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
```

Expected: FAIL。

- [ ] **Step 3: 原样移动外观 XAML**

创建 `SettingsAppearanceSection`，原样移动全部外观行和条件 View。不得合并主题色和背景
状态，不改变预览触发绑定。

- [ ] **Step 4: 验证现有预览测试**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~UiStyleContractTests"
```

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add -- Views/SettingsAppearanceSection.axaml Views/SettingsAppearanceSection.axaml.cs Views/MainWindowSettingsOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "refactor(settings): 提取外观分类"
```

## Task 5：提取通知、高级和关于 section

**Files:**
- Create: `Views/SettingsNotificationsContentSection.axaml`
- Create: `Views/SettingsNotificationsContentSection.axaml.cs`
- Create: `Views/SettingsAdvancedSection.axaml`
- Create: `Views/SettingsAdvancedSection.axaml.cs`
- Create: `Views/SettingsAboutSection.axaml`
- Create: `Views/SettingsAboutSection.axaml.cs`
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 写归属红灯测试**

```csharp
Assert.Contains("Settings.Editor.Current.ToastNotificationsEnabled", notificationsMarkup);
Assert.Contains("Settings.Editor.Current.ShowRemoteContentCard", notificationsMarkup);
Assert.Contains("Settings.Editor.Current.LogLevel", advancedMarkup);
Assert.Contains("Settings.CheckForUpdatesCommand", aboutMarkup);
Assert.Contains("WindowChrome.OpenOfficialSiteCommand", aboutMarkup);
Assert.Contains("WindowChrome.OpenGitHubRepositoryCommand", aboutMarkup);
Assert.Contains("LogViewer.OpenCommand", aboutMarkup);
Assert.Contains("LogViewer.ExportCommand", aboutMarkup);
Assert.Contains("WindowChrome.OpenDataDirectoryCommand", aboutMarkup);
```

- [ ] **Step 2: 验证红灯**

运行 `UiStyleContractTests`，确认因文件不存在失败。

- [ ] **Step 3: 创建三个 View**

原样移动设置行、版本 chips、操作按钮、版权和免责声明。`About` 不得复制
`MainWindowViewModel` 状态。

- [ ] **Step 4: 清除 overlay 长列表**

`MainWindowSettingsOverlay.axaml` 中部只保留状态摘要、分类导航容器、七个 section View
和 footer。断言原始设置绑定已全部离开 overlay。

- [ ] **Step 5: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStyleContractTests|FullyQualifiedName~LocalizationServiceTests"
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
git add -- Views/SettingsNotificationsContentSection.axaml Views/SettingsNotificationsContentSection.axaml.cs Views/SettingsAdvancedSection.axaml Views/SettingsAdvancedSection.axaml.cs Views/SettingsAboutSection.axaml Views/SettingsAboutSection.axaml.cs Views/MainWindowSettingsOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "refactor(settings): 提取通知高级与关于分类"
```

Expected: PASS，0 warnings、0 errors。

## Task 6：实现侧栏 shell 与紧凑状态摘要

**Files:**
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 写 shell 合约红灯测试**

断言：

```csharp
Assert.Contains("MaxWidth=\"900\"", markup);
Assert.Contains("Classes=\"settings-navigation\"", markup);
Assert.Contains("ItemsSource=\"{Binding Settings.Options.SettingsCategories}\"", markup);
Assert.Contains("SelectedValue=\"{Binding Settings.SelectedCategory", markup);
Assert.Contains("Classes=\"settings-status-summary\"", markup);
Assert.Contains("Classes=\"dialog-footer\"", markup);
```

断言样式使用 `Launcher*` brush、spacing、radius 和 icon token。

- [ ] **Step 2: 验证红灯**

运行 `UiStyleContractTests`，确认 shell 尚未实现。

- [ ] **Step 3: 实现两列工作区**

中部结构固定为：

```xml
<Grid Grid.Row="1" ColumnDefinitions="176,*">
    <ListBox Classes="settings-navigation"
             ItemsSource="{Binding Settings.Options.SettingsCategories}"
             SelectedValue="{Binding Settings.SelectedCategory, Mode=TwoWay}"
             SelectedValueBinding="{Binding Code}"
             IsEnabled="{Binding Settings.IsSaving, Converter={x:Static BoolConverters.Not}}">
        <ListBox.ItemTemplate>
            <DataTemplate x:DataType="models:SettingOption">
                <TextBlock Text="{Binding DisplayName}"
                           AutomationProperties.Name="{Binding DisplayName}"/>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
    <Grid Grid.Column="1" RowDefinitions="Auto,*">
        <!-- 紧凑状态摘要 -->
        <!-- 当前分类 ScrollViewer -->
    </Grid>
</Grid>
```

分类内容区使用一个 `ScrollViewer` 包含七个 View。每个 View 绑定对应
`Settings.Is*CategorySelected`，一次只显示一个。

- [ ] **Step 4: 实现紧凑状态摘要**

必须显示：

```text
Shell.CurrentViewTitle
Shell.VersionText
Shell.NetworkStatusValueText
Shell.DiskSpaceText
Shell.ExecutableNameText
Shell.LaunchCheckValueText
Shell.OperationNote
```

程序名和启动检查使用 `TextTrimming="CharacterEllipsis"` 与相同文本 Tooltip。

- [ ] **Step 5: 增加 token 化样式**

增加以下 class 样式：

```text
settings-workspace
settings-navigation
settings-navigation-item
settings-content
settings-category-header
settings-status-summary
settings-status-primary
settings-status-meta
```

不得在 View XAML 中增加直接颜色、裸 `4/6/8` 圆角或裸图标尺寸。

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~UiStyleContractTests"
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
git add -- Views/MainWindowSettingsOverlay.axaml Views/MainWindow.Styles.axaml tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "feat(settings): 增加侧栏分类设置布局"
```

Expected: PASS，0 warnings、0 errors。

## Task 7：验证分类交互、草稿和键盘行为

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: 写默认可见性红灯测试**

```csharp
[Fact]
public void SettingsCategories_DefaultsToGeneralAndShowsOnlySelectedSection()
{
    using var context = CreateWindow();
    context.ViewModel.WindowChrome.IsSettingsVisible = true;
    Dispatcher.UIThread.RunJobs();

    Assert.True(FindSection<SettingsGeneralSection>(context.Window).IsVisible);
    Assert.False(FindSection<SettingsGameSection>(context.Window).IsVisible);
    Assert.False(FindSection<SettingsAppearanceSection>(context.Window).IsVisible);
}
```

为七个 section 逐项选择并断言只有目标 View 可见。

- [ ] **Step 2: 写共享草稿测试**

在常规分类修改 `Language`，切换到外观再切回常规，断言值和 dirty 状态保持；
断言 `SettingsSaved` 没有触发。

- [ ] **Step 3: 写会话记忆测试**

选择 `appearance`，关闭并重新打开 overlay，断言仍为 `appearance`。创建新的
`MainWindowViewModel`，断言回到 `general`。

- [ ] **Step 4: 写保存中禁用测试**

使 `Settings.IsSaving = true`，定位 `settings-navigation` ListBox，断言
`IsEnabled == false`；状态摘要和 footer 仍可见。

- [ ] **Step 5: 写键盘测试**

聚焦分类 `ListBox`，发送 `Key.Down`，断言 `SelectedCategory` 从 `general` 直接变为
`game`；再发送 `Key.Up`，断言选择返回 `general`。验证 `Tab` 从分类导航进入当前内容。
不得直接赋值替代键盘路径。

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --no-restore --filter "FullyQualifiedName~Settings"
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~SettingsCategoryCodesTests"
git add -- tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs
git commit -m "test(settings): 覆盖分类导航与共享草稿"
```

Expected: PASS。

## Task 8：文档与完整验证

**Files:**
- Modify: `AGENTS.md`
- Verify: all files above

- [ ] **Step 1: 更新项目结构文档**

在 `AGENTS.md` 的 View 文件列表中记录七个设置 section View，并说明：

- overlay 只负责 shell；
- section View 共享 `MainWindowViewModel`；
- 分类选择仅限应用会话；
- 新增设置必须放入唯一对应分类。

- [ ] **Step 2: 检查设置绑定唯一归属**

```powershell
rg -n "Settings\\.Editor\\.Current\\.|Settings\\.Appearance\\.|Settings\\.(ChooseBackground|ClearBackground|CheckForUpdates)" Views\\Settings*Section.axaml
```

Expected: 每个绑定只存在于设计指定分类；不存在大小写、路径或结构猜测。

- [ ] **Step 3: 检查 overlay 不含长列表**

```powershell
rg -n "Settings\\.Editor\\.Current\\.|settings-row" Views\\MainWindowSettingsOverlay.axaml
```

Expected: 无匹配。

- [ ] **Step 4: 执行完整验证**

```powershell
.\verify.ps1
```

Expected:

- Debug build：0 warnings、0 errors；
- logic tests：0 failed；
- Headless tests：0 failed；
- Release build：0 warnings、0 errors。

- [ ] **Step 5: 检查差异**

```powershell
git diff --check
git status --short
git log --oneline -10
```

Expected: `git diff --check` 无输出；工作区干净；提交顺序与本计划一致。

- [ ] **Step 6: 提交文档**

```powershell
git add -- AGENTS.md
git commit -m "docs(settings): 记录分类设置页结构"
```
