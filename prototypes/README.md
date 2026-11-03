# 原型索引

本目录保留设计决策时的视觉与交互原型。它们均是一次性实验，不属于生产架构；生产实现以 `src/` 为准，决策背景见对应 ADR。除复核视觉外，不要从应用代码引用这些文件。

| 原型 | 内容 | 对应决策 |
| --- | --- | --- |
| [about-page/](about-page/) | 关于分区三种排布：`index.html`（变体 A）、`fluent-variant.html`、`m3-fluent-blend.html` | [ADR-018](../docs/design/adr/ADR-018-关于分区重设计方向.md)，已落地 |
| [dialog-toast-m3-design.html](dialog-toast-m3-design.html) | 对话框家族 v2 视觉稿 | [ADR-015](../docs/design/adr/ADR-015-对话框家族v2统一框架.md) |
| [crash-window-design.html](crash-window-design.html) | 崩溃窗口三种方向，通过 `?variant` 切换 a、b、c | [ADR-019](../docs/design/adr/ADR-019-不可恢复崩溃两级兜底.md)、[ADR-020](../docs/design/adr/ADR-020-崩溃窗口独立Fluent设计系统.md) |
| [uninstall-ui-demo/index.html](uninstall-ui-demo/index.html) | 卸载对话框交互与视觉演示 | 卸载 UI 落地时的对照稿 |
| [FluentMotionLab/](FluentMotionLab/) | 可运行的 Avalonia 动效实验台，提供 Legacy、Fluent、Reduced 三种变体 | [ADR-016](../docs/design/adr/ADR-016-Fluent动效层.md)，见其 [README](FluentMotionLab/README.md) |
