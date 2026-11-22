# Glass Material Lab — PROTOTYPE

> **历史证据（2026-11-21）**：本原型支撑的 [ADR-071](../../docs/design/adr/ADR-071-主壳玻璃表面采用窗口内采样模糊.md) 已由 [ADR-072](../../docs/design/adr/ADR-072-撤销玻璃子系统并回归半透明实底.md) 撤销。生产代码中的 `GlassSurface`、`Fluid.Avalonia.Acrylic` 依赖与视频替身均已移除；本文描述的集成结论不再代表现行实现。

> 四种「玻璃表面」实现同屏对照：Avalonia 内置 `ExperimentalAcrylicBorder`（Digger）、
> `Fluid.Avalonia.Acrylic` 1.4.0、`JuffMa.Controls.Acrylic` 2.0.0、现行生产的半透明实底 token。

这是一次性实验代码，不属于生产架构，应用代码不得引用本目录。它回答**一个问题**：

**在「壁纸铺在窗口内容背景」的现行架构下（`design-system-spec.md` 主窗口条目），哪种玻璃表面实现能对窗口内动态壁纸做出真实、无残影、帧成本可接受的模糊，并且保留实底回退？**

背景结论（2026-11-20 调研）：Avalonia 公开 API 只有整窗 `TransparencyLevelHint`；原生 per-shape 材质（CompositionMaterial.Avalonia）采样的是窗口背后的桌面且硬锁 Avalonia 12.1.4，与本架构不兼容。因此本实验只比较**窗口内采样**路线，并把现行实底 token 作为对照基线。

## 运行

```powershell
.\prototypes\GlassMaterialLab\run.ps1
# 软件渲染回退路径：
.\prototypes\GlassMaterialLab\run.ps1 --software
```

窗口顶部工具栏：

- **动态背景** — 壁纸层持续漂移（等价于视频壁纸的连续帧变化）；
- **切换壁纸（交叉淡化）** — 模拟启动器 `Background` → `BackgroundCrossFade` 的交叉淡化切换；
- 右侧为实时帧读数（每 30 帧汇总：FPS / 平均帧 / 峰值帧）。

## 候选矩阵

| 卡片 | 实现 | 机制 | 关键开关 |
| --- | --- | --- | --- |
| A | 内置 `ExperimentalAcrylicBorder` | 组合渲染 + `BackgroundSource=Digger` 采样当前渲染目标 | 无（实验性控件，`AcrylicBackgroundSource` 枚举已验证存在于 12.1.6） |
| B | `Fluid.Avalonia.Acrylic` `AcrylicSurface` | 节流捕获（~33ms）+ 参数缓存 + Skia shader | 默认持续 live capture；`UseAcrylicPerformanceDefaults()` 已在 Program.cs 启用 |
| C | `JuffMa.Controls.Acrylic` `AcrylicBackdropControl` | 手动快照 + 覆盖层 | 卡片内 `ForcePermanentRender` 复选框：关 = 暴露静态快照残影，开 = 逐帧重采对照帧成本 |
| D | 现行 Glass token（`#E6202020` 实底 + `#1FFFFFFF` 发丝描边） | 无模糊 | 对照基线：任何候选必须显著优于它才值得引入依赖 |

## 观察清单

按以下顺序记录每张卡的表现（截图或文字均可）：

1. **模糊质量** — 静态背景下（关闭「动态背景」），色斑与细线模糊后的观感；文字可读性对比。
2. **动态残影** — 开启动态背景后，B 是否跟随变化；C 在不勾选 ForcePermanentRender 时是否停留在旧快照（预期残影），勾选后是否恢复但帧成本上升。
3. **交叉淡化** — 点击「切换壁纸」后观察 900ms 过渡：各卡片是否平滑、有无闪烁或拖影。
4. **帧成本** — 工具栏读数；对比「全部四卡」与关闭动态背景时的差异。
5. **软件渲染回退** — `run.ps1 --software`：玻璃是否仍可辨认；是否需要实底降级。
6. **基线对比** — D 的实底在动态背景下的可读性是否其实已经足够（若足够，决策应倾向不引入）。

## 自动化验证结论（2026-11-20）

以下结论由脚本在开发机（Windows 11 26100 / 1080p60）上验证，方法可复现：

- **稳定性**：完整配置（4 玻璃卡 + 全窗动态背景）3×25s + 40s 浸泡全部存活，无原生崩溃。
- **帧成本（GPU）**：60 FPS（垂直同步上限）、平均 16.6 ms、峰值 18.6 ms —— 四个玻璃表面同时活动时无可感知成本。
- **场景真值（应用内 `RenderTargetBitmap` 转储，`GLASSLAB_DUMP=1`）**：四张卡内容完整渲染；背景动画持续；**A（Digger）与 B（Fluid）的玻璃亮度实时跟踪动态背景**（A 采样均值 0.27→0.34 随色斑移动），说明两条采样管线在动态背景下均为 live 捕获而非静态。
- **软件渲染（`--software`）**：应用存活、四卡全部渲染；但帧节奏降至 **8 FPS（平均 123 ms、峰值 378 ms）**。结论：玻璃管线在软件回退下功能可用但代价沉重，生产引入时必须保留「实底 token + 降级开关」的既有策略，且软件渲染时应整体退回实底。

### 排障过程中沉淀的两个工程事实

1. **x:Name 字段回填依赖 `InitializeComponent()`**：本仓库约定代码后置一律调用 `InitializeComponent()`（生产与 FluentMotionLab 均如此）。直接调用 `AvaloniaXamlLoader.Load(this)` 在本配置下不会回填 x:Name 字段 → 构造期 `NullReferenceException`。此类崩溃在事件日志（Application / .NET Runtime / **事件 ID 1026**）中有完整托管栈，是首选诊断入口。
2. **WER 会把已崩溃的进程挂起 4–16 秒**：`Process.HasExited` 在此期间仍返回 false，稳定性脚本会误报「存活」。自动化探测应（a）首个轮询间隔 ≤1s；（b）对 "EXIT@6s" 类对称时序保持警惕（启动即崩 + WER 挂起会伪装成运行时崩溃矩阵）。

### 诊断开关

| 环境变量 | 作用 |
| --- | --- |
| `GLASSLAB_SOLO=A/B/C/none` | 跳过主 XAML，程序化构造单一候选的最小窗口 |
| `GLASSLAB_KEEP=ABC`（或 `none`） | 把未列出的卡片**从可视树移除**（`IsVisible=false` 不隔离 attach） |
| `GLASSLAB_DISABLE=ABC` | 仅隐藏卡片（仍 attach，不隔离初始化） |
| `GLASSLAB_STATIC=1` | 以静态背景启动 |
| `GLASSLAB_NO_METER=1` | 停用 FPS RAF 计量循环 |
| `GLASSLAB_NO_PERF_DEFAULTS=1` | 跳过 `UseAcrylicPerformanceDefaults()` |
| `GLASSLAB_NO_BACKDROP` / `GLASSLAB_NO_TOOLBAR`=1 | 把背景控件 / 工具栏移出树 |
| `GLASSLAB_VIDEO_SIM=none\|surrogate` | 用 `AcrylicBackdrop.IsExcludedFromCapture` 的全窗层模拟 mpv 原生表面：`none` = 只有原生表面（玻璃采到纯色，故障形态），`surrogate` = 原生表面之下再铺静态替身（玻璃采到模糊替身，目标形态） |
| `GLASSLAB_DUMP=1` | 2s 与 6s 各把窗口场景自转储为 `%TEMP%\opencode\glass-lab-dump-*.png`（附实时 FPS 文本），随后自动退出 |

### 视频壁纸采样验证（2026-11-20，ADR-071 第 4 条闭合依据）

生产视频壁纸是 mpv 原生 GL 表面，不会进入 Skia 树走快照。用 `GLASSLAB_VIDEO_SIM` 复现：

| 模式 | B（Fluid）玻璃内采样（均值 / 标准差） |
| --- | --- |
| 正常动态壁纸（对照） | 0.196 / 0.016 |
| `none`（只有被排除的原生表面） | **0.050 / 0.000（纯色空采样）** |
| `surrogate`（原生表面 + 静态替身） | **0.575 / 0.045（模糊纹理）** |

结论：原生表面之上加静态替身即可让玻璃得到正确的模糊底；生产实现保留该视频的封面/首帧作为替身（`VideoPosterSurrogate`），替身被视频完全遮挡、用户不可见。

## 与生产的差异（复验清单）

本原型的壁纸层是 **Avalonia 渲染的程序化内容**。生产的视频壁纸走 **mpv + `OpenGlControlBase` 原生 GL 表面**（`src/Cafe.Launcher.UI/Controls/VideoBackground/`），原生表面**可能不参与** Skia 采样库的树走快照（Fluid 的 `AcrylicVisualRenderer`、JuffMa 的背景截图；`RenderTargetBitmap` 转储同样不能代表组合器呈现）。因此：

- [ ] 选定候选后，必须在真实启动器（`CAFE_VIDEO_PROTOTYPE=1` + 真实 mpv 视频）上复验玻璃内的视频是否可见、是否变黑块；
- [ ] 若原生表面不进快照，需要评估「视频区域排除玻璃」或改用可被采样的视频合成路径，再决策。

## 人工对比结论（2026-11-20）

维护者在 GPU 渲染下对比四卡后选择 **B（`Fluid.Avalonia.Acrylic`）**：模糊观感与文字可读性最接近 Fluent 玻璃预期。D（实底 token）保留为回退基线；A、C 不进入生产候选。采纳决定与适用边界见 [ADR-071](../../docs/design/adr/ADR-071-主壳玻璃表面采用窗口内采样模糊.md)（已于 2026-11-21 由 [ADR-072](../../docs/design/adr/ADR-072-撤销玻璃子系统并回归半透明实底.md) 撤销）。

采纳前的剩余闸门见下方「与生产的差异（复验清单）」——尤其是视频壁纸的原生 GL 表面是否参与快照。另有一条设计侧约束在集成时适用：菜单 / 工具提示等弹出层是独立 `TopLevel`，沿窗口内采样管线无法取到主窗口内容，这些表面必须保留实底 token。

## 待人工观察（自动化无法替代）

- 三种玻璃的模糊质感与文字可读性对比（主观，需人眼）。
- 交叉淡化期间 C 的残影表现与勾选 `ForcePermanentRender` 后的帧率代价。
- 多玻璃表面叠放（如菜单 / 浮层再压在资讯面板上）的视觉噪音是否符合 Fluent「Acrylic 仅用于短暂表面」的边界。
