# 原型索引

本目录保留设计决策时的视觉与交互原型。它们均是一次性实验，不属于生产架构；生产实现以 `src/` 为准，决策背景见对应 ADR。除复核视觉外，不要从应用代码引用这些文件。

| 原型 | 内容 | 对应决策 |
| --- | --- | --- |
| [setup-wizard-redesign/](setup-wizard-redesign/) | 首次设置向导统一舞台样张：全幅头部（计数 + 跳过 + 六段进度）/ 300 左引导与右面板双栏 / 全幅动作带，六步仅右面板不同 | [ADR-059](../docs/design/adr/ADR-059-设置向导统一舞台与双栏解剖.md)，已落地 |
| [progress-inline-design/](progress-inline-design/) | 安装、更新、修复进度的历史悬浮卡片原型，位于右下角拆分按钮上方；保留详情与明暗/小窗口预览 | 2026-11-08 已落实并再次精简为无折叠卡片，直接显示下载大小；见 [ADR-057 修订](../docs/design/adr/ADR-057-统一操作面板与任务对话框.md) |
| [shell-redesign-prototype/](shell-redesign-prototype/) | 全界面结构图、关键状态清单与两套可切换状态的首页原型：A 底部操作区 / B 固定侧栏 | 2026-11-07 结构提案，尚未选型；按 ADR-001 比较 |
| [settings-select-md3/](settings-select-md3/) | 设置页下拉 A 方案真实 Avalonia 样张：动态效果、带色点的主题色来源，明暗主题及键盘焦点 | 2026-11-07 第二阶段已推广；保留第一阶段样式对照与当前生产截图 |
| [about-page/](about-page/) | 关于分区三种排布：`index.html`（变体 A）、`fluent-variant.html`、`m3-fluent-blend.html` | [ADR-018](../docs/design/adr/ADR-018-关于分区重设计方向.md)，已落地 |
| [dialog-toast-m3-design.html](dialog-toast-m3-design.html) | 对话框家族 v2 视觉稿 | [ADR-015](../docs/design/adr/ADR-015-对话框家族v2统一框架.md) |
| [settings-general-md3/index.html](settings-general-md3/index.html) | 设置·常规页 M3 语义样张：小枚举分段按钮、长枚举暴露式菜单、提交式 CheckBox | [ADR-013](../docs/design/adr/ADR-013-设置页重设计方向.md)、[ADR-056](../docs/design/adr/ADR-056-提交式选项与维护目标语义统一.md)（待确认方向） |
| [settings-appearance-md3/index.html](settings-appearance-md3/index.html) | 设置·外观页 M3 语义样张：主题/背景分段、带色点的主题色菜单、壁纸色板、高级配色折叠区；`?theme=dark`、`?advanced=1`、`?open=theme\|algorithm\|variant` | [ADR-013](../docs/design/adr/ADR-013-设置页重设计方向.md)、[ADR-056](../docs/design/adr/ADR-056-提交式选项与维护目标语义统一.md)、[ADR-010](../docs/design/adr/ADR-010-中性色切换交互.md)（待确认方向） |
| [crash-window-design.html](crash-window-design.html) | 崩溃窗口三种方向，通过 `?variant` 切换 a、b、c | [ADR-019](../docs/design/adr/ADR-019-不可恢复崩溃两级兜底.md)、[ADR-020](../docs/design/adr/ADR-020-崩溃窗口独立Fluent设计系统.md) |
| [uninstall-ui-demo/index.html](uninstall-ui-demo/index.html) | 卸载对话框交互与视觉演示 | 卸载 UI 落地时的对照稿 |
| [FluentMotionLab/](FluentMotionLab/) | 可运行的 Avalonia 动效实验台，提供 Legacy、Fluent、Reduced 三种变体 | [ADR-016](../docs/design/adr/ADR-016-Fluent动效层.md)，见其 [README](FluentMotionLab/README.md) |
