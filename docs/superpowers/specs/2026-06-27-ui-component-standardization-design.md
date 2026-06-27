# UI 组件规范化设计

## 目标

在不改变现有界面布局结构、数据绑定、命令和交互流程的前提下，统一主窗口、设置界面、各类弹窗、日志查看器和 Toast 的组件样式、尺寸、间距、对齐与视觉层级。

本次采用平衡密度：常规内容保持清晰留白，高密度内容保持扫描效率。允许修正明显不一致的局部布局，但不重新组织现有页面结构。

## 范围

涉及以下视图：

- `Views/MainWindow.axaml`
- `Views/MainWindowSettingsOverlay.axaml`
- `Views/MainWindowDialogsOverlay.axaml`
- `Views/MainWindowLogViewerOverlay.axaml`
- `Views/MainWindowToastOverlay.axaml`

涉及以下样式与测试文件：

- `App.axaml`
- `Views/MainWindow.Styles.axaml`
- `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- 与上述视图直接相关的 HeadlessTests

不修改 ViewModel、命令、绑定、可见性逻辑、用户文案、业务流程及本地或远程数据契约。

## 样式架构

`App.axaml` 是全局设计令牌的唯一来源。继续使用现有令牌体系：

- `LauncherSpacing*`
- `LauncherRadius*`
- `LauncherIcon*`
- `LauncherControlHeight*`
- `Launcher*Brush`

`Views/MainWindow.Styles.axaml` 是语义组件样式的唯一来源。按钮、输入控件、卡片、内容行、标题、对话框、列表、状态区域和 Toast 的可复用视觉属性集中在该文件中。

五个视图文件只保留：

- 控件结构
- 数据绑定
- 命令绑定
- 可见性条件
- 网格行列定义
- 业务必要的局部尺寸

可由语义样式统一提供的 `Margin`、`Padding`、`Width`、`Height`、`MinHeight`、字体和颜色属性不在视图中重复定义。

本次不新增自定义 Avalonia 控件。现有 XAML 组合结构足以承载规范化工作，引入自定义控件会扩大改动范围并增加绑定回归风险。

## 组件规范

### 间距

间距遵循现有 4px 网格。视图中的 `Spacing`、`RowSpacing` 和 `ColumnSpacing` 全部引用 `LauncherSpacing*`。

平衡密度使用以下节奏：

- 外层区块：24px
- 相关组件组：16px
- 组件内部：8px
- 紧凑信息组合：4px

布局中已经承担明确视觉对齐职责的 40px 区域间距继续使用 `LauncherSpacingSection`。

### 圆角与容器

- 常规卡片：16px 内边距，`LauncherRadiusMd`
- 紧凑内容行：12px 内边距，`LauncherRadiusSm`
- 对话框：`LauncherRadiusLg`

设置行、状态详情、新闻行、日志条目和更新文件项分别使用独立语义样式，不通过同一个通用卡片样式覆盖不同的信息结构。

### 控件尺寸

- 设置控件：`LauncherControlHeightSetting`
- 弹窗操作按钮：`LauncherControlHeightDialog`
- 底部操作按钮：`LauncherControlHeightBottom`
- 启动按钮：`LauncherControlHeightLaunch`
- 图标：仅使用现有 `LauncherIconSm`、`LauncherIconMd`、`LauncherIconLg`、`LauncherIconXl` 和 `LauncherIconXxl`

同类按钮具有一致的默认、悬停、按下和禁用状态。输入控件具有一致的背景、边框、焦点状态、内容对齐和高度。

### 文字层级

文字层级统一为：

1. 页面标题
2. 区块标题
3. 分组标题
4. 正文
5. 辅助文字

每一级由语义样式提供字号、字重和颜色。视图不重复设置相同字体属性。

## 局部布局修正规则

保持现有页面与控件的排列结构，仅允许以下修正：

- 同类组件的边缘未对齐
- 相邻区块间距不一致
- 同级按钮尺寸不一致
- 图标与文字未垂直居中
- 内容行内边距不一致
- 标题、正文和辅助文字层级不明确
- 最小窗口尺寸下发生可避免的挤压或溢出

不移动功能入口，不改变操作顺序，不合并或拆分现有功能区。

## 约束测试

扩展 `UiStyleContractTests`，覆盖：

- 视图的间距属性必须使用 `LauncherSpacing*`
- 圆角必须使用已声明的层级令牌
- Material 图标尺寸必须使用 `LauncherIcon*`
- 颜色必须使用语义资源
- 可复用组件属性不得在多个视图中重复内联定义
- 关键组件必须引用对应的语义样式

现有线契约、控件名称、绑定表达式和覆盖层顺序保持不变。

## 验收

必须完成以下验证：

1. 运行全部单元测试。
2. 运行全部 HeadlessTests。
3. 执行 Debug 构建，结果为 0 warnings、0 errors。
4. 在 1300×754 初始窗口检查全部范围内界面。
5. 在 1024×640 最小窗口检查全部范围内界面。
6. 分别检查浅色和深色主题。
7. 检查文字对比度、悬停状态、按下状态、禁用状态、内容溢出和操作按钮对齐。

## 完成标准

所有范围内视图使用统一设计令牌和语义样式；同类组件在不同界面中具有一致的尺寸、间距、对齐和状态反馈；现有布局结构、绑定、交互和业务行为保持不变；全部自动化验证与视觉验收通过。
