# ADR-063 全应用统一 Fluent 2 视觉

- 状态：已接受
- 日期：2026-11-14
- 替代范围：扩展 [ADR-062](ADR-062-主应用采用Fluent2与壁纸首页.md) 的局部迁移范围；更新 [ADR-050](ADR-050-跨功能共享组件视觉基准.md) 的尺寸与圆角数值、[ADR-013](ADR-013-设置页重设计方向.md) 的导航指示条定案。M3 动态色管线、业务契约、独立崩溃窗口资源边界继续有效。

## 背景

ADR-062 只在首页左栏引入局部 FluentAvaloniaTheme，其余界面仍为 FluentTheme 底座 + 自绘模板，导致同一角色在首页与其他表面呈现两种密度和圆角。用户要求全应用统一 Fluent 2。

## 决策

- **主题底座**：`App.axaml` 以 `FluentAvaloniaTheme` 取代全局 `FluentTheme`，下拉框、菜单、展开器、选项卡、复选框、开关、单选与滑块改用 WinUI 模板。独立崩溃报告进程仍按 ADR-020 加载自身 FluentTheme 与 `Crash.*` 资源。
- **动态色保持真源**：`ApplyScheme` 继续写入 `Launcher.*` 画刷，并通过 `AccentFillColor*`、`CheckBox*`、`ToggleSwitch*`、`RadioButton*`、`Slider*`、`ProgressBar*`、`ToggleButton*`、`TextFillColor*`、`ControlFillColor*`、`Card*`、`Divider*` 等资源键桥接到 FluentAvalonia 模板。桥接使用 `StaticResource` 别名指向同一画刷实例，运行时 `SetBrush` 原位改色仍同步生效。
- **密度**：交互控件、菜单行与设置字段统一 32 DIP 最小高度（Toast 动作同为 32 DIP）；主界面任务操作保留 48/58 DIP。高度使用 `MinHeight`，长文案和文字放大自然增高。
- **圆角**：交互面（按钮、菜单行、选项行、字段、分段控件、Toast 动作）4 DIP；卡片、弹层、对话框、面板 8 DIP；状态 chip、进度段与圆形徽章保留整圆。`Launcher.Radius.Md/Lg` 收敛为 8。
- **选中态**：设置导航、通用下拉/菜单项与向导选项行使用中性 `SecondaryContainer` 家族及成对前景；设置页下拉与分段按钮保留 `Primary.Soft` 选中 wash；强调色只用于主操作、选中指示与焦点。普通悬停/按下使用中性 `Button.Secondary.Hover/Pressed` 或状态层。
- **中性色板**：表面与文字改用 Fluent 中性灰阶（浅色 `#F3F3F3` 系、深色 `#202020` 系），文字 `Text.Primary/Secondary/Body` 相应重标；`Field.Border` 提高对比度以维持 3:1。警示色补深色档，避免深色卡片上的 3:1 不足。
- **组件**：`DialogSurface` 8 DIP 外圆角、56 DIP 标题行与发丝底带不变；设置行 48 DIP、标题槽 32 DIP；关于页键值行 40 DIP、贡献者行 64 DIP；向导选项行 4 DIP 圆角、选中为中性填充；崩溃窗口 `Crash.*` 同步 4/8 DIP 圆角与 32 DIP 动作高度。
- **弹层**：`FluentAvaloniaTheme` 不提供 `ContextMenu` 主题，`App.axaml` 自建与 `MenuFlyoutPresenter` 同面的模板；`Avalonia.Controls.ColorPicker` 仍引用 WinUI 2 旧资源键，补 `Default` 档与 Light/Dark 档别名，避免 `KeyNotFound`。
- **壁纸对比度**：加强控制面板底部深色渐变并提高 `Text.OnDark` 不透明度，保证右下状态文字在亮壁纸上可读；标题栏固定白字与三段 scrim 的既有决策不变。

## 后果

- 全应用共享同一套 Fluent 2 密度与中性状态，首页不再有独立的控件语言。
- 模板部件名改由 FluentAvalonia 决定（如 `Border#PopupBorder`、`Border#HighlightBackground`、`Border#PART_LayoutRoot`），契约测试与样式转发以实际模板为准。
- 视觉变化使全部黄金基线按组定向再生；`CAFE_GOLDEN_UPDATE=1` 只用于确认后的受影响方法，不做全量刷新。
- 新增 NuGet 依赖 FluentAvaloniaUI 3.1.0，需与 Avalonia 12.1.6 同步升级。

## 验收

覆盖最小窗口与默认窗口、浅深主题、键盘焦点与文字放大；设置、关于、对话框、Toast、资源面板与向导的无头渲染与黄金基线全绿；`DesignTokenContrastTests` 的文本与 UI 配对保持不变或改善；安装、更新、启动、暂停、重试等业务契约不变。

## 2026-11-15 图标语言

全应用图标语言由 [ADR-064](ADR-064-图标语言统一为Fluent符号.md) 统一为 Fluent 符号，模板部件图标与业务图标不再混用两套字体；本 ADR 的主题底座、密度、圆角与动态色决策不变。