# UI Component Standardization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留现有界面结构、绑定和交互的前提下，统一全部现有界面的组件样式、尺寸、间距、对齐与视觉层级。

**Architecture:** `App.axaml` 继续提供唯一的设计令牌，`Views/MainWindow.Styles.axaml` 继续提供唯一的语义组件样式。先用 `UiStyleContractTests` 固化平衡密度规范，再依次迁移主窗口、设置、弹窗、日志查看器和 Toast，最后使用 HeadlessTests、完整测试和 Debug 构建验证行为未变化。

**Tech Stack:** .NET 10、Avalonia 12.0.4、xUnit 2.9.3、Avalonia Headless、PowerShell

---

## 文件结构

- Modify: `App.axaml` — 保持并补齐全局尺寸令牌；不放置视图专用样式。
- Modify: `Views/MainWindow.Styles.axaml` — 定义文字、按钮、输入控件、卡片、列表、弹窗和 Toast 的语义样式。
- Modify: `Views/MainWindow.axaml` — 主窗口只保留结构、绑定和业务必要尺寸。
- Modify: `Views/MainWindowSettingsOverlay.axaml` — 设置界面改用统一设置行、标题、控件和操作区样式。
- Modify: `Views/MainWindowDialogsOverlay.axaml` — 弹窗改用统一标题、正文、信息卡片、列表和操作区样式。
- Modify: `Views/MainWindowLogViewerOverlay.axaml` — 日志查看器改用统一弹窗、筛选栏、日志条目和操作区样式。
- Modify: `Views/MainWindowToastOverlay.axaml` — Toast 图标、正文和关闭按钮的布局属性迁移到语义样式。
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs` — 固化令牌、语义样式、内联属性和关键类名约束。
- Verify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs` — 验证真实 XAML 加载、命令和覆盖层绑定不变；仅在现有断言无法覆盖新增语义类时补充断言。

### Task 1: 固化平衡密度与语义样式契约

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 添加语义样式属性读取辅助方法**

在 `UiStyleContractTests` 类中添加：

```csharp
private static IReadOnlyDictionary<string, string> GetStyleSetters(
    XDocument document,
    string selector)
{
    return document
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "Style"
            && element.Attribute("Selector")?.Value == selector)
        .Elements()
        .Where(element => element.Name.LocalName == "Setter")
        .ToDictionary(
            element => element.Attribute("Property")?.Value
                ?? throw new InvalidOperationException($"Setter in {selector} has no Property."),
            element => element.Attribute("Value")?.Value
                ?? throw new InvalidOperationException($"Setter in {selector} has no Value."),
            StringComparer.Ordinal);
}
```

- [ ] **Step 2: 添加平衡密度契约测试**

添加以下测试，精确约束已确认的 16px 常规卡片、12px 紧凑内容行、三层圆角和控件高度：

```csharp
[Fact]
public void SemanticComponents_UseBalancedDensityTokens()
{
    var document = XDocument.Load(ProjectFile("Views/MainWindow.Styles.axaml"));

    var settingsSection = GetStyleSetters(document, "Border.settings-section");
    Assert.Equal("16", settingsSection["Padding"]);
    Assert.Equal("{StaticResource LauncherRadiusMd}", settingsSection["CornerRadius"]);

    var contentRow = GetStyleSetters(document, "Border.content-row");
    Assert.Equal("12", contentRow["Padding"]);
    Assert.Equal("{StaticResource LauncherRadiusSm}", contentRow["CornerRadius"]);

    var dialog = GetStyleSetters(document, "Border.dialog");
    Assert.Equal("{StaticResource LauncherRadiusLg}", dialog["CornerRadius"]);

    var settingControl = GetStyleSetters(document, "ComboBox.setting-control");
    Assert.Equal(
        "{StaticResource LauncherControlHeightSetting}",
        settingControl["MinHeight"]);

    var dialogAction = GetStyleSetters(document, "Button.dialog-action");
    Assert.Equal(
        "{StaticResource LauncherControlHeightDialog}",
        dialogAction["MinHeight"]);

    var bottomAction = GetStyleSetters(document, "Button.bottom-action");
    Assert.Equal(
        "{StaticResource LauncherControlHeightBottom}",
        bottomAction["MinHeight"]);

    var launchAction = GetStyleSetters(document, "Button.launcher-control.start");
    Assert.Equal(
        "{StaticResource LauncherControlHeightLaunch}",
        launchAction["MinHeight"]);
}
```

- [ ] **Step 3: 添加视图不得重复内联视觉属性的契约测试**

添加以下测试。窗口尺寸、横幅高度、弹窗边界和绑定尺寸属于业务必要尺寸，不在该测试中禁止：

```csharp
[Fact]
public void Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets()
{
    foreach (var relativePath in ViewFiles)
    {
        var document = XDocument.Load(ProjectFile(relativePath));
        var attributes = document
            .Descendants()
            .SelectMany(element => element.Attributes())
            .ToArray();

        Assert.DoesNotContain(
            attributes,
            attribute => attribute.Name.LocalName is "FontSize" or "FontWeight");
        Assert.DoesNotContain(
            attributes,
            attribute => attribute.Name.LocalName == "Padding");
        Assert.DoesNotContain(
            attributes,
            attribute =>
                attribute.Name.LocalName == "Margin"
                && attribute.Value is "0,0,16,0" or "0,4,0,0");
    }
}
```

- [ ] **Step 4: 运行新增契约测试并确认失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
```

Expected: FAIL；失败信息必须指向当前视图中的 `FontWeight`、`Padding` 或两个待迁移的 `Margin`，或者指向尚未满足平衡密度值的语义样式。

- [ ] **Step 5: 提交测试**

```powershell
git add -- tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "test(ui): 固化组件密度与内联样式契约"
```

### Task 2: 统一全局语义样式

**Files:**
- Modify: `App.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 核对全局令牌值且不新建重复令牌**

确认 `App.axaml` 中以下精确资源仍存在：

```xml
<x:Double x:Key="LauncherSpacingXs">4</x:Double>
<x:Double x:Key="LauncherSpacingSm">8</x:Double>
<x:Double x:Key="LauncherSpacingMd">12</x:Double>
<x:Double x:Key="LauncherSpacingLg">16</x:Double>
<x:Double x:Key="LauncherSpacingXxl">24</x:Double>
<x:Double x:Key="LauncherSpacingSection">40</x:Double>
<x:Double x:Key="LauncherControlHeightSetting">36</x:Double>
<x:Double x:Key="LauncherControlHeightDialog">42</x:Double>
<x:Double x:Key="LauncherControlHeightBottom">48</x:Double>
<x:Double x:Key="LauncherControlHeightLaunch">58</x:Double>
```

这些资源已满足设计要求；没有明确复用点时不增加新的全局尺寸资源。

- [ ] **Step 2: 调整核心容器与控件样式**

在 `Views/MainWindow.Styles.axaml` 中确保以下 setter 为精确值：

```xml
<Style Selector="Border.content-row">
    <Setter Property="Background" Value="{DynamicResource LauncherContentRowBrush}"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusSm}"/>
    <Setter Property="Padding" Value="12"/>
</Style>

<Style Selector="Border.settings-section">
    <Setter Property="Background" Value="{DynamicResource LauncherCardBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherCardBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusMd}"/>
    <Setter Property="Padding" Value="16"/>
</Style>

<Style Selector="Border.dialog">
    <Setter Property="Background" Value="{DynamicResource LauncherDialogBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherCardBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="{StaticResource LauncherRadiusLg}"/>
</Style>
```

- [ ] **Step 3: 将弹窗关闭按钮偏移和 Toast 图标偏移定义为语义样式**

在 `Views/MainWindow.Styles.axaml` 添加：

```xml
<Style Selector="Button.dialog-close.header-action">
    <Setter Property="Margin" Value="0,0,16,0"/>
</Style>

<Style Selector="MaterialIcon.toast-icon">
    <Setter Property="VerticalAlignment" Value="Top"/>
    <Setter Property="Margin" Value="0,4,0,0"/>
</Style>
```

- [ ] **Step 4: 为更新文件文本建立明确层级**

添加：

```xml
<Style Selector="TextBlock.update-file-name">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{DynamicResource LauncherTextPrimaryBrush}"/>
</Style>

<Style Selector="TextBlock.update-file-size-text">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{DynamicResource LauncherTextSecondaryBrush}"/>
</Style>
```

- [ ] **Step 5: 运行令牌与语义样式测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DesignTokens_ContainExactSpacingRadiusIconAndControlHeightValues|FullyQualifiedName~SemanticComponents_UseBalancedDensityTokens"
```

Expected: PASS。

- [ ] **Step 6: 提交语义样式**

```powershell
git add -- App.axaml Views/MainWindow.Styles.axaml
git commit -m "style(ui): 统一全局组件密度与视觉层级"
```

### Task 3: 规范主窗口

**Files:**
- Modify: `Views/MainWindow.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: 检查主窗口的结构性尺寸**

保留以下业务必要尺寸，不迁移到通用样式：

```xml
Width="1300"
Height="754"
MinWidth="1024"
MinHeight="640"
```

保留横幅、进度条、路径字段等只在该结构中使用的尺寸绑定。不得改变 `Grid` 行列、覆盖层顺序、命令或绑定。

- [ ] **Step 2: 统一主内容区节奏**

将主内容区同级区块的 `StackPanel.Spacing` 映射为：

```xml
Spacing="{StaticResource LauncherSpacingXxl}"
```

将区块内部组件组映射为：

```xml
Spacing="{StaticResource LauncherSpacingLg}"
```

将按钮内容、图标文字和紧凑状态组合保持为：

```xml
Spacing="{StaticResource LauncherSpacingSm}"
```

只修改当前承担上述职责的 `StackPanel`；不批量替换不同语义下的所有 `Spacing`。

- [ ] **Step 3: 移除可由现有语义样式提供的重复属性**

当 `Classes` 已包含 `panel-title`、`caption`、`body`、`value`、`content-row`、`bottom-panel`、`control-panel` 或 `launcher-control` 时，删除与对应样式相同的内联字体、颜色、内边距和控件高度。

- [ ] **Step 4: 运行主窗口契约与 HeadlessTests**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
```

Expected: `UiStyleContractTests` 中与主窗口相关的断言 PASS；`MainWindowHeadlessTests` 全部 PASS。

- [ ] **Step 5: 提交主窗口规范化**

```powershell
git add -- Views/MainWindow.axaml Views/MainWindow.Styles.axaml
git commit -m "style(ui): 规范主窗口组件与间距"
```

### Task 4: 规范设置界面

**Files:**
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 保留设置窗口边界与事务操作**

保留：

```xml
MaxWidth="760"
MaxHeight="592"
```

保留 `Settings.SaveCommand`、`Settings.CancelCommand` 及现有关闭命令，不改变设置项顺序。

- [ ] **Step 2: 将关闭按钮偏移迁移到语义类**

将设置标题栏关闭按钮：

```xml
Classes="dialog-close"
Margin="0,0,16,0"
```

改为：

```xml
Classes="dialog-close header-action"
```

- [ ] **Step 3: 统一设置区块、设置行和控件**

确保所有设置分组使用 `settings-section`，所有标签—控件行使用 `settings-row`，下拉框使用 `setting-control`。设置行内部图标文字组合使用 `LauncherSpacingSm`，设置区块之间使用 `LauncherSpacingMd` 或 `LauncherSpacingLg`，不引入数值型 `Spacing`。

移除已由以下样式提供的重复属性：

```xml
Classes="settings-section"
Classes="settings-row"
Classes="setting-control"
Classes="section-title"
Classes="group-title"
Classes="caption"
```

- [ ] **Step 4: 验证设置操作顺序和样式契约**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SettingsPanel_UsesTransactionalSaveAndCancelActions|FullyQualifiedName~SettingsAboutActionsAndVersionChips_UsePurposeBasedOrder|FullyQualifiedName~Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets"
```

Expected: PASS。

- [ ] **Step 5: 提交设置界面规范化**

```powershell
git add -- Views/MainWindowSettingsOverlay.axaml Views/MainWindow.Styles.axaml
git commit -m "style(ui): 规范设置界面组件与布局"
```

### Task 5: 规范弹窗与更新文件列表

**Files:**
- Modify: `Views/MainWindowDialogsOverlay.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 将所有弹窗关闭按钮改用标题栏操作语义类**

对当前同时使用 `Classes="dialog-close"` 和 `Margin="0,0,16,0"` 的按钮，删除内联 `Margin` 并使用：

```xml
Classes="dialog-close header-action"
```

- [ ] **Step 2: 将更新文件文本内联字重迁移到语义样式**

将文件名文本改为：

```xml
<TextBlock Text="{Binding Name}"
           Classes="update-file-name"
           TextTrimming="CharacterEllipsis"/>
```

将文件大小文本改为：

```xml
<TextBlock Text="{Binding DisplaySize}"
           Classes="update-file-size-text"/>
```

必须使用文件中现有的精确绑定表达式；执行前读取对应元素并保留原始 `Text` 值，不根据模型属性名推断。

- [ ] **Step 3: 统一弹窗内容节奏**

确认：

- `confirm-layout` 负责弹窗整体内边距。
- `dialog-heading` 与 `dialog-heading-copy` 负责图标、标题和说明间距。
- `confirm-message` 负责确认信息内边距。
- `dialog-card` 负责迁移配置和更新文件内容卡片。
- `confirm-actions` 负责按钮顺序与按钮间距。
- `dialog-action` 统一使用 `LauncherControlHeightDialog`。

删除视图中与这些样式重复的 `Padding`、`FontSize` 和 `FontWeight`。

- [ ] **Step 4: 运行弹窗和更新列表契约测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UpdateFileList_HoverAndSelectionKeepReadableItemColors|FullyQualifiedName~DialogOverlays_UseSharedDialogLayerWithoutExplicitZIndex|FullyQualifiedName~Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets"
```

Expected: PASS。

- [ ] **Step 5: 提交弹窗规范化**

```powershell
git add -- Views/MainWindowDialogsOverlay.axaml Views/MainWindow.Styles.axaml
git commit -m "style(ui): 规范弹窗与更新列表"
```

### Task 6: 规范日志查看器与 Toast

**Files:**
- Modify: `Views/MainWindowLogViewerOverlay.axaml`
- Modify: `Views/MainWindowToastOverlay.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 规范日志查看器标题栏**

将：

```xml
Classes="dialog-close"
Margin="0,0,16,0"
```

改为：

```xml
Classes="dialog-close header-action"
```

保留 `LauncherLogViewerWidth`、`LauncherLogViewerHeight`、筛选命令、搜索绑定和虚拟化 `ListBox`。

- [ ] **Step 2: 统一日志条目层级**

保持 `dialog-card` 为日志条目容器，使用现有 `caption`、`section-title` 和 `chip-text`，删除视图中的重复字体属性。`FontFamily="Consolas"` 和 `MaxHeight="120"` 是日志详情的业务展示属性，继续保留。

- [ ] **Step 3: 迁移 Toast 图标偏移**

将 Toast 严重性图标：

```xml
VerticalAlignment="Top" Margin="0,4,0,0"
```

替换为：

```xml
Classes="toast-icon"
```

保留严重性画刷转换器、`AutomationProperties.Name`、关闭命令和 `ZIndex`。

- [ ] **Step 4: 运行日志与 Toast 契约测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LogViewer_|FullyQualifiedName~Toast|FullyQualifiedName~Views_DoNotInlineReusableTypographyPaddingOrHeaderOffsets"
```

Expected: PASS。

- [ ] **Step 5: 提交日志与 Toast 规范化**

```powershell
git add -- Views/MainWindowLogViewerOverlay.axaml Views/MainWindowToastOverlay.axaml Views/MainWindow.Styles.axaml
git commit -m "style(ui): 规范日志查看器与通知组件"
```

### Task 7: 完整自动化与视觉验收

**Files:**
- Modify only if a verified regression requires correction: files changed in Tasks 1–6
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: 运行格式与差异检查**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` 无输出；状态中只包含本计划范围内尚未提交的修正。

- [ ] **Step 2: 运行全部单元测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj
```

Expected: 0 failed。

- [ ] **Step 3: 运行全部 HeadlessTests**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj
```

Expected: 0 failed。

- [ ] **Step 4: 执行 Debug 构建**

Run:

```powershell
.\build.ps1
```

Expected: 0 warnings、0 errors。

- [ ] **Step 5: 执行视觉验收**

分别在浅色和深色主题检查：

1. 1300×754 初始窗口。
2. 1024×640 最小窗口。
3. 主窗口远程内容、安装、进度和控制面板。
4. 设置界面的全部分组及底部保存/取消操作。
5. 通知、更新、停止、修复、卸载、迁移和崩溃恢复弹窗。
6. 日志查看器筛选、搜索、空状态和长详情。
7. Toast 的四种严重性及关闭按钮。
8. 默认、悬停、按下和禁用状态。
9. 文字对比度、内容溢出、图标文字垂直对齐和同级按钮尺寸。

发现问题时只修改已确认存在问题的精确元素或语义样式，然后重新运行受影响的契约测试和 HeadlessTests。

- [ ] **Step 6: 提交验收修正**

只有存在验收修正时执行：

```powershell
git add -- App.axaml Views tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
git commit -m "fix(ui): 修正组件规范化验收问题"
```

- [ ] **Step 7: 确认最终工作树**

Run:

```powershell
git status --short
git log -7 --oneline
```

Expected: 工作树无未提交修改；最近提交按任务顺序记录测试、全局样式、主窗口、设置、弹窗、日志与 Toast，以及存在时的验收修正。
