# ADR-068：优先使用 FluentAvalonia 默认控件

- 状态：已接受
- 日期：2026-11-17
- 修订 [ADR-063](ADR-063-全应用统一Fluent2视觉.md) 与 [ADR-067](ADR-067-FluentAvalonia共享控件模板.md) 的控件模板、资源桥接与状态覆盖规则。

使用 FluentAvalonia 默认输入、选择、按钮、菜单和焦点样式。主题外观通过公开的
`CustomAccentColor` 入口设置。设置行使用 `FASettingsExpanderItem` 的自适应动作槽，Toast 使用
`FAInfoBar` 提供图标、严重级别和关闭按钮；自动消失与操作执行仍归通知模型管理。
游戏任务、向导、诊断与设置等业务表面通过标准 Avalonia 面板组合。对话框继续由现有模态栈
调度，使用 Fluent 表面画刷及按钮；独立崩溃报告窗口使用同一主题。

回归检查以功能为主，包括设置草稿、游戏切换、任务命令、通知操作、模态焦点与窄窗口布局。
旧控件模板部件名和专属颜色值不再是产品契约。

## 2026-11-20 材质修订

对话框、设置与通知不再使用壁纸玻璃，改为实心中性表面；控件模板选择、设置行 `FASettingsExpanderItem` 与通知 `FAInfoBar` 不变。见 [ADR-070](ADR-070-对话框设置与通知统一为实心Fluent表面.md)。
