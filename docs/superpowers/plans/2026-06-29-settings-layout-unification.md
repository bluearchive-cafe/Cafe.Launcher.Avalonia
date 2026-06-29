# 设置布局统一与优化 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 统一设置对话框内 7 个分类的视觉布局——导航/内容区分隔、分类标题字体升级 (16px)、补齐组标题、统一行模板、组间间距优化。

**架构：** 纯 XAML 样式和视图层变更，不涉及 ViewModel 逻辑、设置持久化或保存行为。新增 `category-title` 和 `settings-group` 两个样式类，9 个本地化键。


---
## 文件结构

| 文件 | 职责 | 变更类型 |
|---|---|---|
| `Views/MainWindow.Styles.axaml` | 所有样式类定义 | 修改导航背景、内容区边框、新增 category-title/settings-group |
| `Views/MainWindowSettingsOverlay.axaml` | 设置弹窗外壳 | 分类标题类名变更 |
| `Views/SettingsGeneralSection.axaml` | 常规分类内容 | 加描述、统一行模板 |
| `Views/SettingsGameSection.axaml` | 游戏分类内容 | 游戏路径行统一 |
| `Views/SettingsDownloadNetworkSection.axaml` | 下载与网络内容 | 加组标题、更新通道描述 |
| `Views/SettingsAppearanceSection.axaml` | 外观分类内容 | 无结构变化 |
| `Views/SettingsNotificationsContentSection.axaml` | 通知与内容 | 加组标题、描述替换 caption |
| `Views/SettingsAdvancedSection.axaml` | 高级分类内容 | 加组标题、日志描述 |
| `Views/SettingsAboutSection.axaml` | 关于分类内容 | 无变化 |
| `Assets/Locales/en.json` | 英文字符串 | +9 keys |
| `Assets/Locales/zh-Hans.json` | 简体中文字符串 | +9 keys |
| `Assets/Locales/ja.json` | 日文字符串 | +9 keys |
| `Services/LocalizationService.cs` | 本地化绑定属性 | +9 ObservableProperty + Apply() |
| `tests/.../UiStyleContractTests.cs` | 样式合约测试 | 更新 2 处断言 |

---

### 任务 1：更新样式合约测试 — 导航背景 + 分类标题类名

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

先更新测试中的旧断言，使其匹配新设计。这是 TDD 的第一步：让测试先"知道"新预期。

- [ ] **步骤 1：修改导航背景断言**

在 `SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens` 方法中，将导航背景从 `LauncherTransparentBrush` 改为 `LauncherContentRowBrush`：

```csharp
// 旧 (行 233)
Assert.Equal(
    "{DynamicResource LauncherTransparentBrush}",
    GetStyleSetters(document, "ListBox.settings-navigation")["Background"]);

// 新
Assert.Equal(
    "{DynamicResource LauncherContentRowBrush}",
    GetStyleSetters(document, "ListBox.settings-navigation")["Background"]);
```

- [ ] **步骤 2：修改分类标题类名断言和 category-header Spacing 断言**

在 `SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace` 方法中，将 `settings-category-title` 改为 `category-title`：

同时，在 `SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens` 方法中，将 `settings-category-header` Spacing 断言从 `LauncherSpacingMd` 改为 `0`：

```csharp
// 旧 (行 257)
Assert.Equal(
    "{StaticResource LauncherSpacingMd}",
    GetStyleSetters(document, "StackPanel.settings-category-header")["Spacing"]);

// 新
Assert.Equal(
    "0",
    GetStyleSetters(document, "StackPanel.settings-category-header")["Spacing"]);
```

分类标题类名断言：

```csharp
// 旧 (行 191-192)
var categoryTitle = content
    .Descendants()
    .Single(element =>
        element.Name.LocalName == "TextBlock"
        && HasClass(element, "settings-category-title"));

// 新
var categoryTitle = content
    .Descendants()
    .Single(element =>
        element.Name.LocalName == "TextBlock"
        && HasClass(element, "category-title"));
```

- [ ] **步骤 3：运行测试确认失败**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests.SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens|FullyQualifiedName~UiStyleContractTests.SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace"
```

预期：2 个测试 FAIL（样式和 XAML 尚未修改）。

- [ ] **步骤 4：Commit**

```bash
git add tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "test(settings): 更新样式合约断言 — 导航背景改为 ContentRowBrush、分类标题类名改为 category-title"
```

---

### 任务 2：新增样式类 — category-title、settings-group、导航背景、内容区左边线

**文件：**
- 修改：`Views/MainWindow.Styles.axaml`

- [ ] **步骤 1：在 `TextBlock.group-title` 样式后新增 `category-title` 样式**

在 `TextBlock.group-title` 样式块（约行 791-795）之后添加：

```xml
<Style Selector="TextBlock.category-title">
    <Setter Property="FontSize" Value="16"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{DynamicResource LauncherTextPrimaryBrush}"/>
</Style>
```

- [ ] **步骤 2：新增 `StackPanel.settings-group` 样式**

在 `StackPanel.settings-category-header` 样式块（约行 596-598）之后添加：

```xml
<Style Selector="StackPanel.settings-group">
    <Setter Property="Margin" Value="0,16,0,0"/>
</Style>
```

此样式用于包裹每个逻辑组，提供 16px 组间间距。单组分类也用它包裹以保持一致性。

- [ ] **步骤 2b：调整 `settings-category-header` 的 Spacing**

将 `StackPanel.settings-category-header` 的 Spacing 从 `LauncherSpacingMd`(12) 改为 `0`：

```xml
<!-- 旧 -->
<Setter Property="Spacing" Value="{StaticResource LauncherSpacingMd}"/>

<!-- 新 -->
<Setter Property="Spacing" Value="0"/>
```

垂直节奏由 `settings-group` 的 `Margin="0,16,0,0"` 和 `settings-row` 的 `Margin="0,8,0,0"` 统一管理，category-header 不再参与。

- [ ] **步骤 3：修改 `ListBox.settings-navigation` 背景**

将 `ListBox.settings-navigation` 样式中的 `Background` 从 `LauncherTransparentBrush` 改为 `LauncherContentRowBrush`（约行 567）：

```xml
<!-- 旧 -->
<Setter Property="Background" Value="{DynamicResource LauncherTransparentBrush}"/>

<!-- 新 -->
<Setter Property="Background" Value="{DynamicResource LauncherContentRowBrush}"/>
```

- [ ] **步骤 4：给 `Grid.settings-content` 加左边线**

在 `Grid.settings-content` 样式（约行 592-594）中添加 `BorderBrush` 和 `BorderThickness`：

```xml
<Style Selector="Grid.settings-content">
    <Setter Property="Background" Value="{DynamicResource LauncherCardBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource LauncherCardBorderBrush}"/>
    <Setter Property="BorderThickness" Value="1,0,0,0"/>
    <Setter Property="RowSpacing" Value="{StaticResource LauncherSpacingMd}"/>
</Style>
```

- [ ] **步骤 5：运行测试确认 2 个测试通过**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests.SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens|FullyQualifiedName~UiStyleContractTests.SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace"
```

预期：`SettingsWorkspaceStyles_UseSemanticBrushesAndDesignTokens` PASS（样式已更新），`SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace` 仍 FAIL（overlay 尚未修改）。

- [ ] **步骤 6：运行全部 UiStyleContractTests 确保无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：仅 `SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace` 失败，其余全部 PASS。

- [ ] **步骤 7：Commit**

```bash
git add Views/MainWindow.Styles.axaml
git commit -m "feat(settings): 新增 category-title/settings-group 样式，导航加背景、内容区加左边线"
```

---

### 任务 3：添加 9 个本地化键

**文件：**
- 修改：`Assets/Locales/en.json`
- 修改：`Assets/Locales/zh-Hans.json`
- 修改：`Assets/Locales/ja.json`
- 修改：`Services/LocalizationService.cs`

- [ ] **步骤 1：在 en.json 中添加 9 个键**

在 `Assets/Locales/en.json` 的合适位置（按字母顺序或逻辑分组）插入：

```json
"settingsGroupConnection": "Connection",
"settingsGroupDisplay": "Display",
"settingsGroupDiagnostics": "Diagnostics",
"languageDescription": "Interface display language",
"closeBehaviorDescription": "Action when clicking the close button",
"updateChannelDescription": "Select the launcher update channel",
"logLevelDescription": "Controls the verbosity of diagnostic logging",
"toastNotificationsDescription": "Show toast notifications for events",
"remoteContentCardDescription": "Show announcements and banners on the main screen"
```

- [ ] **步骤 2：在 zh-Hans.json 中添加对应中文翻译**

```json
"settingsGroupConnection": "连接",
"settingsGroupDisplay": "显示",
"settingsGroupDiagnostics": "诊断",
"languageDescription": "界面显示语言",
"closeBehaviorDescription": "点击关闭按钮时的行为",
"updateChannelDescription": "选择启动器更新通道",
"logLevelDescription": "控制诊断日志的详细程度",
"toastNotificationsDescription": "以弹窗形式显示事件通知",
"remoteContentCardDescription": "在主界面显示公告与横幅"
```

- [ ] **步骤 3：在 ja.json 中添加对应日文翻译**

```json
"settingsGroupConnection": "接続",
"settingsGroupDisplay": "表示",
"settingsGroupDiagnostics": "診断",
"languageDescription": "インターフェースの表示言語",
"closeBehaviorDescription": "閉じるボタンをクリックした時の動作",
"updateChannelDescription": "ランチャーの更新チャンネルを選択",
"logLevelDescription": "診断ログの詳細度を制御",
"toastNotificationsDescription": "イベントをトースト通知で表示",
"remoteContentCardDescription": "メイン画面にお知らせとバナーを表示"
```

- [ ] **步骤 4：在 LocalizedStrings 中添加 9 个 ObservableProperty**

在 `Services/LocalizationService.cs` 中 `LocalizedStrings` 类的属性区域添加：

```csharp
[ObservableProperty] private string settingsGroupConnection = "";
[ObservableProperty] private string settingsGroupDisplay = "";
[ObservableProperty] private string settingsGroupDiagnostics = "";
[ObservableProperty] private string languageDescription = "";
[ObservableProperty] private string closeBehaviorDescription = "";
[ObservableProperty] private string updateChannelDescription = "";
[ObservableProperty] private string logLevelDescription = "";
[ObservableProperty] private string toastNotificationsDescription = "";
[ObservableProperty] private string remoteContentCardDescription = "";
```

- [ ] **步骤 5：在 `Apply()` 方法中添加映射**

在 `Services/LocalizationService.cs` 的 `LocalizedStrings.Apply()` 方法中添加：

```csharp
SettingsGroupConnection = localizer.T("settingsGroupConnection");
SettingsGroupDisplay = localizer.T("settingsGroupDisplay");
SettingsGroupDiagnostics = localizer.T("settingsGroupDiagnostics");
LanguageDescription = localizer.T("languageDescription");
CloseBehaviorDescription = localizer.T("closeBehaviorDescription");
UpdateChannelDescription = localizer.T("updateChannelDescription");
LogLevelDescription = localizer.T("logLevelDescription");
ToastNotificationsDescription = localizer.T("toastNotificationsDescription");
RemoteContentCardDescription = localizer.T("remoteContentCardDescription");
```

- [ ] **步骤 6：运行本地化相关测试确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~LocalizationServiceTests"
```

预期：全部 PASS。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Locales/en.json Assets/Locales/zh-Hans.json Assets/Locales/ja.json Services/LocalizationService.cs
git commit -m "feat(l10n): 添加设置布局优化所需的 9 个本地化键"
```

---

### 任务 4：更新 Overlay 分类标题类名

**文件：**
- 修改：`Views/MainWindowSettingsOverlay.axaml`

- [ ] **步骤 1：将分类标题 TextBlock 的 Classes 从 `group-title settings-category-title` 改为 `category-title`**

```xml
<!-- 旧 (行 78-82) -->
<TextBlock Classes="group-title settings-category-title"
           DataContext="{Binding SelectedItem, ElementName=SettingsNavigation}"
           x:DataType="models:SettingOption"
           Text="{Binding DisplayName}"
           AutomationProperties.Name="{Binding DisplayName}"/>

<!-- 新 -->
<TextBlock Classes="category-title"
           DataContext="{Binding SelectedItem, ElementName=SettingsNavigation}"
           x:DataType="models:SettingOption"
           Text="{Binding DisplayName}"
           AutomationProperties.Name="{Binding DisplayName}"/>
```

注意：移除 `group-title` 类，只保留新的 `category-title`。`AutomationProperties.Name` 保持不变。

- [ ] **步骤 2：运行测试确认 overlay 测试通过**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests.SettingsOverlay_UsesFixedTwoColumnCategoryWorkspace"
```

预期：PASS。

- [ ] **步骤 3：Commit**

```bash
git add Views/MainWindowSettingsOverlay.axaml
git commit -m "feat(settings): 分类标题升级为 category-title 样式 (16px Primary)"
```

---

### 任务 5：统一常规分类 — 语言、关闭行为加描述

**文件：**
- 修改：`Views/SettingsGeneralSection.axaml`

当前代码：语言和关闭行为两行使用仅 `section-title` 的标签（无描述），动态效果行已经有 `dialog-heading-copy` 模式。

- [ ] **步骤 1：根 StackPanel 改用 `settings-group`**

```xml
<!-- 旧 (行 8) -->
x:DataType="vm:MainWindowViewModel"><StackPanel>

<!-- 新 -->
x:DataType="vm:MainWindowViewModel"><StackPanel Classes="settings-group">
```

- [ ] **步骤 2：将语言行的标签从 `section-title` 改为 `dialog-heading-copy`**

```xml
<!-- 旧 (行 15-17) -->
<StackPanel Grid.Column="1" VerticalAlignment="Center">
    <TextBlock Text="{Binding Shell.I18n.Language}" Classes="section-title"/>
</StackPanel>

<!-- 新 -->
<StackPanel Grid.Column="1" Classes="dialog-heading-copy">
    <TextBlock Text="{Binding Shell.I18n.Language}" Classes="section-title"/>
    <TextBlock Text="{Binding Shell.I18n.LanguageDescription}" Classes="caption" TextWrapping="Wrap"/>
</StackPanel>
```

- [ ] **步骤 3：将关闭行为行的标签从 `section-title` 改为 `dialog-heading-copy`**

关闭行为行（行 30-47）：

```xml
<!-- 旧 (行 34-36) -->
<StackPanel Grid.Column="1" VerticalAlignment="Center">
    <TextBlock Text="{Binding Shell.I18n.CloseBehavior}" Classes="section-title"/>
</StackPanel>

<!-- 新 -->
<StackPanel Grid.Column="1" Classes="dialog-heading-copy">
    <TextBlock Text="{Binding Shell.I18n.CloseBehavior}" Classes="section-title"/>
    <TextBlock Text="{Binding Shell.I18n.CloseBehaviorDescription}" Classes="caption" TextWrapping="Wrap"/>
</StackPanel>
```

- [ ] **步骤 4：运行 UiStyleContractTests 确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部 PASS（新增的 I18n 绑定不在 Editor 绑定检查范围内）。

- [ ] **步骤 5：Commit**

```bash
git add Views/SettingsGeneralSection.axaml
git commit -m "feat(settings): 语言和关闭行为行加描述，统一为 dialog-heading-copy 模式"
```

---

### 任务 6：统一游戏分类 — 游戏路径行统一模板

**文件：**
- 修改：`Views/SettingsGameSection.axaml`

当前游戏路径行（行 11-25）有 `VerticalAlignment="Center"` 的 StackPanel。统一为 `dialog-heading-copy`。

- [ ] **步骤 1：根 StackPanel 改用 `settings-group`**

```xml
<!-- 旧 (行 8) -->
x:DataType="vm:MainWindowViewModel"><StackPanel>

<!-- 新 -->
x:DataType="vm:MainWindowViewModel"><StackPanel Classes="settings-group">
```

- [ ] **步骤 2：游戏路径行标签统一为 `dialog-heading-copy`**

```xml
<!-- 旧 (行 15-18) -->
<StackPanel Grid.Column="1" VerticalAlignment="Center">
    <TextBlock Text="{Binding Shell.I18n.GamePath}" Classes="section-title"/>
    <TextBlock Text="{Binding Settings.Editor.Current.GamePath}" Classes="caption" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"/>
</StackPanel>

<!-- 新 -->
<StackPanel Grid.Column="1" Classes="dialog-heading-copy">
    <TextBlock Text="{Binding Shell.I18n.GamePath}" Classes="section-title"/>
    <TextBlock Text="{Binding Settings.Editor.Current.GamePath}" Classes="caption" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"/>
</StackPanel>
```

- [ ] **步骤 3：运行测试确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部 PASS。

- [ ] **步骤 4：Commit**

```bash
git add Views/SettingsGameSection.axaml
git commit -m "feat(settings): 游戏路径行统一为 dialog-heading-copy 模式"
```

---

### 任务 7：统一下载与网络分类 — 加组标题、更新通道描述

**文件：**
- 修改：`Views/SettingsDownloadNetworkSection.axaml`

- [ ] **步骤 1：根 StackPanel 改用 `settings-group`，加组标题 "Connection"**

```xml
<!-- 旧 (行 8) -->
<UserControl ...><StackPanel>

<!-- 新 -->
<UserControl ...><StackPanel Classes="settings-group">
    <TextBlock Text="{Binding Shell.I18n.SettingsGroupConnection}" Classes="group-title"/>
```

- [ ] **步骤 2：将更新通道行的标签统一为 `dialog-heading-copy`（添加描述）**

```xml
<!-- 旧 (行 73-75) -->
<StackPanel Grid.Column="1" VerticalAlignment="Center">
    <TextBlock Text="{Binding Shell.I18n.UpdateChannel}" Classes="section-title"/>
</StackPanel>

<!-- 新 -->
<StackPanel Grid.Column="1" Classes="dialog-heading-copy">
    <TextBlock Text="{Binding Shell.I18n.UpdateChannel}" Classes="section-title"/>
    <TextBlock Text="{Binding Shell.I18n.UpdateChannelDescription}" Classes="caption" TextWrapping="Wrap"/>
</StackPanel>
```

- [ ] **步骤 3：运行测试确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部 PASS。

- [ ] **步骤 4：Commit**

```bash
git add Views/SettingsDownloadNetworkSection.axaml
git commit -m "feat(settings): 下载与网络加组标题 Connection、更新通道加描述"
```

---

### 任务 8：统一通知与内容分类 — 加组标题、caption 改描述

**文件：**
- 修改：`Views/SettingsNotificationsContentSection.axaml`

- [ ] **步骤 1：根 StackPanel 改用 `settings-group`，加组标题 "Display"，将两行的 caption 改为标准描述模式**

完整替换文件内容：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Cafe.Launcher.Avalonia.ViewModels"
             xmlns:models="using:Cafe.Launcher.Avalonia.Models"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="Cafe.Launcher.Avalonia.Views.SettingsNotificationsContentSection"
             x:Name="SettingsRoot"
             x:DataType="vm:MainWindowViewModel"><StackPanel Classes="settings-group">
    <TextBlock Text="{Binding Shell.I18n.SettingsGroupDisplay}" Classes="group-title"/>

    <Grid Classes="settings-row" ColumnDefinitions="Auto,*,Auto">
        <Border Classes="settings-icon">
            <materialIcons:MaterialIcon Kind="BellOutline" Width="{StaticResource LauncherIconMd}" Height="{StaticResource LauncherIconMd}" Foreground="{DynamicResource LauncherAccentBrush}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <StackPanel Grid.Column="1" Classes="dialog-heading-copy">
            <TextBlock Text="{Binding Shell.I18n.NotificationSettings}" Classes="section-title"/>
            <TextBlock Text="{Binding Shell.I18n.ToastNotificationsDescription}" Classes="caption" TextWrapping="Wrap"/>
        </StackPanel>
        <ToggleSwitch Grid.Column="2"
                      IsChecked="{Binding Settings.Editor.Current.ToastNotificationsEnabled, Mode=TwoWay}"
                      OnContent="{Binding Shell.I18n.ToggleOn}"
                      OffContent="{Binding Shell.I18n.ToggleOff}"
                      VerticalAlignment="Center"/>
    </Grid>

    <Grid Classes="settings-row" ColumnDefinitions="Auto,*,Auto">
        <Border Classes="settings-icon">
            <materialIcons:MaterialIcon Kind="ViewDashboardOutline" Width="{StaticResource LauncherIconMd}" Height="{StaticResource LauncherIconMd}" Foreground="{DynamicResource LauncherAccentBrush}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <StackPanel Grid.Column="1" Classes="dialog-heading-copy">
            <TextBlock Text="{Binding Shell.I18n.RemoteContentCard}" Classes="section-title"/>
            <TextBlock Text="{Binding Shell.I18n.RemoteContentCardDescription}" Classes="caption" TextWrapping="Wrap"/>
        </StackPanel>
        <ToggleSwitch Grid.Column="2"
                      IsChecked="{Binding Settings.Editor.Current.ShowRemoteContentCard, Mode=TwoWay}"
                      OnContent="{Binding Shell.I18n.ToggleOn}"
                      OffContent="{Binding Shell.I18n.ToggleOff}"
                      VerticalAlignment="Center"/>
    </Grid>
        </StackPanel>
    </UserControl>
```

核心变更：
- 顶部新增 `<TextBlock Text="{Binding Shell.I18n.SettingsGroupDisplay}" Classes="group-title"/>`
- Toast 行：`caption` `ToastNotifications` → `caption` `ToastNotificationsDescription`
- 远程内容行：`caption` `ShowRemoteContentCard` → `caption` `RemoteContentCardDescription`
- 两行标签从 `section-title` + 独立 `caption` 改为 `dialog-heading-copy` 包裹

- [ ] **步骤 2：运行测试确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部 PASS。新增的是 I18n 绑定，不在 Editor 绑定检查范围内。

- [ ] **步骤 3：Commit**

```bash
git add Views/SettingsNotificationsContentSection.axaml
git commit -m "feat(settings): 通知与内容加组标题 Display、caption 改为标准描述模式"
```

---

### 任务 9：统一高级分类 — 加组标题、日志级别描述

**文件：**
- 修改：`Views/SettingsAdvancedSection.axaml`

- [ ] **步骤 1：根 StackPanel 改用 `settings-group`，加组标题 "Diagnostics"，日志级别加描述**

完整替换文件内容：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Cafe.Launcher.Avalonia.ViewModels"
             xmlns:models="using:Cafe.Launcher.Avalonia.Models"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="Cafe.Launcher.Avalonia.Views.SettingsAdvancedSection"
             x:Name="SettingsRoot"
             x:DataType="vm:MainWindowViewModel"><StackPanel Classes="settings-group">
    <TextBlock Text="{Binding Shell.I18n.SettingsGroupDiagnostics}" Classes="group-title"/>

    <Grid Classes="settings-row" ColumnDefinitions="Auto,*,Auto">
        <Border Classes="settings-icon">
            <materialIcons:MaterialIcon Kind="Bug" Width="{StaticResource LauncherIconMd}" Height="{StaticResource LauncherIconMd}" Foreground="{DynamicResource LauncherAccentBrush}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <StackPanel Grid.Column="1" Classes="dialog-heading-copy">
            <TextBlock Text="{Binding Shell.I18n.LogLevel}" Classes="section-title"/>
            <TextBlock Text="{Binding Shell.I18n.LogLevelDescription}" Classes="caption" TextWrapping="Wrap"/>
        </StackPanel>
        <ComboBox Grid.Column="2" Classes="setting-control"
                  ItemsSource="{Binding Settings.Options.LogLevel}"
                  SelectedValue="{Binding Settings.Editor.Current.LogLevel, Mode=TwoWay}"
                  SelectedValueBinding="{Binding Code}">
            <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="models:SettingOption">
                    <TextBlock Text="{Binding DisplayName}"/>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>
    </Grid>
        </StackPanel>
    </UserControl>
```

- [ ] **步骤 2：运行测试确认无回归**

```powershell
dotnet test --filter "FullyQualifiedName~UiStyleContractTests"
```

预期：全部 PASS。

- [ ] **步骤 3：Commit**

```bash
git add Views/SettingsAdvancedSection.axaml
git commit -m "feat(settings): 高级分类加组标题 Diagnostics、日志级别加描述"
```

---

### 任务 10：全局构建与测试验证

**文件：** 无新修改

- [ ] **步骤 1：Debug 构建 (0 warnings, 0 errors)**

```powershell
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
```

预期：Build succeeded. 0 Warning(s), 0 Error(s).

- [ ] **步骤 2：运行全部测试**

```powershell
dotnet test
```

预期：全部 PASS，0 failed。

- [ ] **步骤 3：运行 verify.ps1 (如果存在)**

```powershell
.\verify.ps1
```

- [ ] **步骤 4：Release 构建**

```powershell
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
```

预期：Build succeeded. 0 Warning(s), 0 Error(s).

- [ ] **步骤 5：Commit (如有 verify 脚本更新)**

```bash
git add -A
git commit -m "chore: 全局构建与测试验证通过"
```
