# 原型索引

本目录只保留仍有现行决策引用的一次性原型；它们不属于生产架构，生产实现以 `src/` 为准。其余一次性视觉与交互原型已于 2026-11-16 移除，可在 Git 提交 `2665666b` 的 `prototypes/` 中查阅原稿。除复核视觉或研究证据外，不要从应用代码引用这些文件。

| 原型 | 内容 | 对应决策 |
| --- | --- | --- |
| [fluent-redesign/index.html](fluent-redesign/index.html) | 对话框 / 设置 / 通知三处表面的 Fluent 2 重设计草稿；页面内可切换明暗主题、对话框材质（实心/玻璃）、设置行密度（分组卡片/明细行）、互斥选项形态（单选组/胶囊） | [ADR-070](../docs/design/adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md) 已按草稿落地：对话框（第一批）、设置整窗页与单选组（第二批）、通知（第三批） |
| [GlassMaterialLab/](GlassMaterialLab/) | 可运行的玻璃表面候选对比台：内置 `ExperimentalAcrylicBorder`（Digger）× `Fluid.Avalonia.Acrylic` × `JuffMa.Controls.Acrylic` × 现行实底 token，同屏对照动态壁纸下的模糊质量、交叉淡化残影与帧成本 | 2026-11-20 材质 spike：**B（`Fluid.Avalonia.Acrylic`）人工对比胜出**，决策见 [ADR-071](../docs/design/adr/ADR-071-主壳玻璃表面采用窗口内采样模糊.md)；**该决策已于 2026-11-21 由 [ADR-072](../docs/design/adr/ADR-072-撤销玻璃子系统并回归半透明实底.md) 撤销**，原型仅保留为历史研究证据；观察清单见其 [README](GlassMaterialLab/README.md) |
