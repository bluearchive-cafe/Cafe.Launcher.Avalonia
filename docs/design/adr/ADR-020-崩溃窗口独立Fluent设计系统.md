# ADR-020: 崩溃窗口采用独立 Fluent 设计系统，不并入主窗口 MD3 令牌体系

- 状态：**已接受**（2026-09-08，用户要求记录）
- 背景：
  - 主窗口的视觉体系是「M3 语义 + Fluent 底座」（[spec §1 原则 2/3](../design-system-spec.md)）：全部颜色/间距/字阶 token 定义在 `App.axaml`，scheme 角色由运行时动态色管线覆盖（壁纸取色 + 中性色策略）。
  - 崩溃窗口必须同时活在两个宿主：主进程（`App`）与隔离报告进程（`CrashReportApp`）。后者只加载 `CrashReportApp.axaml`（`FluentTheme` + `MaterialIconStyles`），没有 `App.axaml`、没有 DI、没有动态色管线——ADR-019 明确崩溃界面不得依赖完整业务依赖图。
  - 故障说明的可读性也不应受壁纸取色/种子跟随影响：崩溃界面需要的是稳定、可预测的观感，而不是随用户壁纸漂移的配色。
- 决策：
  - 崩溃窗口是**独立设计系统**：`FluentTheme` 底座 + 自带 `Crash.*` 令牌命名空间，颜色/间距/圆角/字阶全部就近定义在 `CrashReportWindow.axaml` 的 `Window.Resources`（含 Light/Dark 两档 `ThemeDictionaries`），既不引用也不复用 `Launcher.*`。
  - 视觉语言与主窗口刻意区分：中性冷灰蓝表面（不随动态色）、圆角 4（控件）/8（卡片）、字阶 12/15/20、危险红（Danger 族）与链接蓝（Accent）双语义色；信息层级按 A 版原型（状态行 → 标题 → 说明 → 摘要行 → 折叠技术详情 → 动作带）。
  - 组件只在本窗口定义：`Button.crash-action` 基类 + `crash-secondary` / `crash-danger` / `crash-subtle` 三级；窗口 700 固定宽、高度由内容决定、不可缩放。
  - 边界规则：崩溃窗口不得引用 `Launcher.*`；`Crash.*` 不得出现在崩溃窗口之外的任何视图。`UiStyleContractTests` 按显式文件清单扫描、覆盖不到本窗口，因此这条边界由本 ADR + 走查清单 + 黄金截图共同守护。
- 后果：
  - 这是 [spec §1 原则 3](../design-system-spec.md)「全 token 化」的**显式豁免**，范围仅 `Views/CrashReportWindow.axaml` 与 `CrashReportApp.axaml` 的主题注册；豁免理由是隔离报告进程的资源自包含要求，而不是风格偏好。
  - 视觉回归由 `CrashReportWindowHeadlessTests` 的黄金截图（`Baselines/crash-report-window.png`）与布局断言（折叠/展开高度、宽度不变、按钮内容居中）守护；不进入主窗口的 token 契约测试。
  - 代价：同一应用存在两套视觉语言（主窗口 MD3 / 崩溃窗口 Fluent）。若主窗口未来改走 Fluent，或崩溃窗口需要并入 M3 动态色，本 ADR 必须重新裁决。
- 原型：`prototypes/crash-window-design.html?variant=a`（与 [ADR-019](ADR-019-不可恢复崩溃两级兜底.md) 共用，保留为视觉决策记录）。
