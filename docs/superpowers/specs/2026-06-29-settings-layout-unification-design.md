# 设置布局统一与优化设计

## 目标

在已完成的信息架构重组基础上，统一设置对话框内各分类区域的视觉布局，
提升导航与内容的视觉层次，使所有设置项遵循一致的排版模式。

不改变任何设置值、持久化 JSON 键、保存行为、预览行为或游戏操作语义。

## 视觉基准

继续使用项目现有 Fluent 主题、`Launcher*` 设计 token、Material Icons。
不引入新的颜色语义、圆角、阴影或图标库。

## 导航与内容区分隔

**导航背景：**

`ListBox.settings-navigation` 的 `Background` 从 `LauncherTransparentBrush`
改为 `LauncherContentRowBrush`，使导航区与 dialog 底层 overlay 视觉分离。

**内容区左边线：**

`Grid.settings-content` 增加 `BorderBrush="{DynamicResource LauncherCardBorderBrush}"`
+ `BorderThickness="1,0,0,0"`，在导航和内容之间提供一条细分隔线。

## 分类标题

新建 `TextBlock.category-title` 样式：

- `FontSize="16"`
- `FontWeight="SemiBold"`
- `Foreground="{DynamicResource LauncherTextPrimaryBrush}"`

`MainWindowSettingsOverlay.axaml` 中的分类标题 TextBlock 从
`Classes="group-title settings-category-title"` 改为 `Classes="category-title"`。

层次关系：dialog-title (19px) > category-title (16px) > group-title (12px) > section-title (14px, 设置项自身标签)

## 组标题

当前 3 个分类缺少组标题，补齐：

| 分类 | 新增组标题键 | en 值 |
|---|---|---|
| 下载与网络 | `SettingsGroupConnection` | Connection |
| 通知与内容 | `SettingsGroupDisplay` | Display |
| 高级 | `SettingsGroupDiagnostics` | Diagnostics |

已有组标题的分类保持不变（常规 "App Preferences"、外观 "Theme & Color" / "Background"、
游戏 "Game Files"）。

组标题继续使用 `TextBlock.group-title` 样式 (12px, Secondary)。

## 组间间距

新建 `StackPanel.settings-group` 样式：`Margin="0,16,0,0"`。

每个 Section View 内部的顶级 StackPanel 使用 `settings-group` 为各组之间提供 16px 间距，
与行内 8px 间距（`settings-row` margin）形成层级区分。

`StackPanel.settings-category-header` 的 `Spacing="12"` 不变（分类标题到第一个组的间距）。

## 设置行统一模板

所有设置行统一为：

```
Grid (settings-row, ColumnDefinitions="Auto,*,Auto")
  Border (settings-icon) → MaterialIcon (18×18, AccentBrush)
  StackPanel (dialog-heading-copy, Grid.Column="1")
    TextBlock (section-title) → 设置项名称
    TextBlock (caption) → 设置项描述 (可选)
  Control (Grid.Column="2") → ComboBox / ToggleSwitch / Button / ColorPicker
```

### 逐分类变更

**常规：**

- 语言 → 加描述 `LanguageDescription`
- 关闭行为 → 加描述 `CloseBehaviorDescription`
- 动态效果 → 已有描述，不变
- 每行标签从仅 `section-title` 改为 `dialog-heading-copy` (title + description)

**游戏：**

- 游戏路径 → 标签改用 `dialog-heading-copy`，路径文本作为 caption
- 启动检查 → 已有描述，结构不变
- 游戏管理 → 已有描述，结构不变

**下载与网络：**

- 加组标题 "Connection"
- 更新通道 → 加描述 `UpdateChannelDescription`
- 其余行已有描述，不变

**外观：**

- 无结构变化 — 已是最佳状态

**通知与内容：**

- 加组标题 "Display"
- Toast 通知 → caption 改为描述 `ToastNotificationsDescription`
- 远程内容卡片 → caption 改为描述 `RemoteContentCardDescription`
- 标签从仅 `section-title` + 单独 `caption` 改为 `dialog-heading-copy`

**高级：**

- 加组标题 "Diagnostics"
- 日志级别 → 加描述 `LogLevelDescription`
- 标签从仅 `section-title` 改为 `dialog-heading-copy`

**关于：**

- 无变化 — 内容性质不同，独立布局合理

## 新增本地化键

共 9 个新键，需加入 `en.json`、`zh-Hans.json`、`ja.json` 并接入 `LocalizedStrings`：

### 组标题 (3)

| 键 | en |
|---|---|
| `SettingsGroupConnection` | Connection |
| `SettingsGroupDisplay` | Display |
| `SettingsGroupDiagnostics` | Diagnostics |

### 设置项描述 (6)

| 键 | en |
|---|---|
| `LanguageDescription` | Interface display language |
| `CloseBehaviorDescription` | Action when clicking the close button |
| `UpdateChannelDescription` | Select the launcher update channel |
| `LogLevelDescription` | Controls the verbosity of diagnostic logging |
| `ToastNotificationsDescription` | Show toast notifications for events |
| `RemoteContentCardDescription` | Show announcements and banners on the main screen |

注：`MotionModeDescription` 已存在，复用。

## XAML 结构约束

- 各 Section View 内部使用 `<StackPanel Classes="settings-group">` 包裹每个逻辑组
- 组标题为该 StackPanel 的第一个子元素（`TextBlock Classes="group-title"`）
- 设置行跟随在组标题之后
- 单组分类（如高级、通知与内容）也用 `settings-group` 包裹保持一致性
- 分类标题在 Overlay 层，不在各 Section 内
- 不使用直接颜色、裸圆角或未 token 化的图标尺寸

## 涉及文件

| 文件 | 变更 |
|---|---|
| `Views/MainWindow.Styles.axaml` | 导航背景、内容区左边线、新增 category-title 和 settings-group 样式 |
| `Views/MainWindowSettingsOverlay.axaml` | 分类标题改用 category-title |
| `Views/SettingsGeneralSection.axaml` | 语言/关闭行为加描述，dialog-heading-copy |
| `Views/SettingsGameSection.axaml` | 游戏路径行统一 |
| `Views/SettingsDownloadNetworkSection.axaml` | 加组标题、更新通道描述 |
| `Views/SettingsAppearanceSection.axaml` | 无结构变化 |
| `Views/SettingsNotificationsContentSection.axaml` | 加组标题、描述替换 caption |
| `Views/SettingsAdvancedSection.axaml` | 加组标题、日志级别描述 |
| `Views/SettingsAboutSection.axaml` | 无变化 |
| `Assets/Locales/en.json` | +9 keys |
| `Assets/Locales/zh-Hans.json` | +9 keys |
| `Assets/Locales/ja.json` | +9 keys |
| `Services/LocalizationService.cs` | +9 ObservableProperty + Apply() 映射 |
| `tests/.../UiStyleContractTests.cs` | 可能需要更新 token 断言 |

## 完成条件

- 所有 7 个分类的布局遵循统一行模板
- 导航与内容区有视觉分隔
- 分类标题 (16px) 与组标题 (12px) 层级分明
- 组间间距 (16px) 与行间距 (8px) 区分清晰
- 所有现有设置仍可访问和保存
- Debug 和 Release 构建均为 0 warnings、0 errors
- 逻辑测试与 Headless 测试 0 failed
- `UiStyleContractTests` 通过
