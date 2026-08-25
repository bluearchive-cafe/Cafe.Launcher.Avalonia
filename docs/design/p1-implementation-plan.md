# P1 实施计划 — Token 体系重构 + 动态色基座

> 范围与目标：P1 = 一次性重命名 token 为点分层命名 + 补齐新族（Elevation/StateLayer/scrim/Thickness）+ AA 对比度契约自动化 + 动态色管线基座（ApplyScheme）+ 画廊 token 表 + 黄金截图基建接线。
> **非目标（P1 不做）**：任何视觉值改动（除对比度不达标 token 的最小修正）、组件形态变更、三个新设置项的 UI（底层就绪，UI 在 P2）、底栏形态决策、文本缩放。

## M0 — Spike：NuGet 包可行性 + 新旧公式对照（1 天）⭐ 先行门禁

> **状态：✅ 已执行（2026-08-25）→ 判定 GO**。core 包采用（`ArgbColor` 角色、±1 tone 容差内 56/60 断言通过）；Avalonia 集成包不采用（方案 B：手写接线；其强制引入 DesignTokens 第三方 token 框架）。**结论全文见 [`color-utilities-spike.md`](color-utilities-spike.md)**；M3 追加动作：显式传 `SpecVersion.Spec2021`、参考值单测以其 §2 fixture 表为基。

- 独立临时工程（不进产品 csproj）引用 `Shirasagi0012.MaterialColorUtilities` 0.2.0（net10.0）：
  - 断言参考值：seed `#6750A4` → SchemeTonalSpot Light/Dark 的 `Primary`、`PrimaryContainer`（≈`#EADDFF`）、`SecondaryContainer`（≈`#E8DEF8`）、`Surface`、`OnPrimary` 与 Material Theme Builder 参考一致（±1 tone 容差）；
  - 验证 Avalonia 集成包在 **Avalonia 12.1.1** 下可构建（依赖区间 `[12.0.0-preview2,)` 满足）；若集成包引入 `DesignTokens.Avalonia`/预览版依赖冲突 → 仅引用 core 包 + 手写 ArgbColor↔Avalonia `Color` 接线（方案 B）。
- **新旧公式对照验证**（2025-02 决策补入）：独立临时工程再引用 `MaterialColorUtilities`（albi005，0.3.0，同谱系前身；两包同名命名空间，**各自独立工程或 extern alias，不得直接同工程混引**），对同一批 seed（如 `#6750A4`、壁纸暖色 `#E8B8A0`、冷色 `#4A6B93`）在 Light/Dark × TonalSpot 下各算一次，产出**新旧公式差异表**（Primary/OnPrimary/PrimaryContainer/SecondaryContainer/OnSecondaryContainer/Surface/OnSurface/Outline 等角色，标记同值/±tone 差异）。
  - 用途：① 量化"若降级用旧包"的观感损失；② 为 vendor 回退保存交叉参考谱系（albi005 源码与 Shirasagi 同源）。
  - 定位：albi005 **不进入产品依赖**，仅为对照数据点。
- 产物：`docs/design/color-utilities-spike.md`（结论 + 参考值断言表 + 新旧公式差异表 + 依赖树 + Go/No-Go 判定）。
- **Go/No-Go**：失败 → 启动回退方案（vendor 裁剪核心，研究已备档，~2–4 人日），P1 目标自动扩展。

## M1 — 一次性全量重命名（2–3 天）

- 输入：`docs/design/token-migration-map.md`（旧键→新键映射表，按 §3.2 十二家族人工分类；含特例：`LauncherSpacingXsThickness`→`Launcher.Spacing.Thickness.Xs`、`LauncherTextBox*`→按家族归位、`LauncherRadiusMd(6)`→`Launcher.Radius.Md` 值暂不变）。
- 脚本：`scripts/Rename-LauncherTokens.ps1`（读映射表 → 替换 `src/` 全部 .axaml/.cs 中的精确键引用 → 输出改动清单 + 旧键残留巡检，失败即退出非零）。映射表为 LF JSON/CSV。
- 同步：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`（键清单、token 值断言）、headless 测试中的资源键、`Features/Settings/SettingsAppearanceViewModel.cs`（动态覆盖的笔刷键）、`Controls/` 与 `Views/` 各样式分片。
- 顺手修复：`Views\Styles\{Toast,RemoteContent,SetupWizard}.axaml` 33 处静态 token 的 `{DynamicResource}` → `{StaticResource}`。
- 门禁：编译零警告、全部测试绿、`grep -r "Launcher\([A-Z]\)" src tests` 旧键 = 0 命中。

## M2 — 新族 token + 对比度契约（2–3 天）

> **状态：✅ 已执行（2026-08-25）**。新族落地（Elevation 4 档/StateLayer 4 档/Overlay.Scrim 三档/Spacing.Thickness 补齐/Typography.LetterSpacing）；2 处硬编码 BoxShadow 迁移为 token 引用；`DesignTokenContrastTests` + 新族/旧键回归/静态族规则契约全绿；最小调色 6 项（before/after 记录于 spec §8）。**M2 门禁通过**。

- 新增族（`App.axaml` 按注释分区）：`Launcher.Elevation.*`（0–3 档阴影值：颜色/偏移/模糊；把 `MainWindow.Styles.axaml` 2 处硬编码 BoxShadow 迁为引用）、`Launcher.StateLayer.*`（8%/12%/16%/24%）、`Launcher.Color.Overlay.Scrim*`（3 档，替代/并列现有 overlay 用途）、`Launcher.Spacing.Thickness.*` 补齐全档、`Launcher.Typography.*` 角色键（P1 仅为别名/新增，消费方 P2 迁移）。
- `UiStyleContractTests` 扩展：新族存在性、旧键清零（回归门）、`{StaticResource}` 规则（静态 token 不得被 DynamicResource 引用）、禁裸值规则覆盖新文件。
- **新增 `DesignTokenContrastTests`**（`tests/Cafe.Launcher.Avalonia.Tests/`）：WCAG 相对亮度计算（`ColorUtils` 内部函数——注意提取可测试的纯函数）、token 对清单驱动断言（Text.Primary×Surface、Text.Secondary×Card、Text.Body×Dialog、OnAccent×Primary、Danger 系、Info/Notice/Warning/DangerSoft 底×文字、按钮文字×fill 等 ≥4.5:1 文本 / ≥3:1 UI）；豁免区清单显式列在测试中。
- 调色：对不达标 token 做**最小**调整（记录 before/after；Light/Dark 双档），不得改变视觉意图（如 `TextSecondary` 只提暗/提亮到阈值）。
- 门禁：契约测试 + 对比度测试全绿；零警告。

## M3 — 动态色管线基座（2–4 天）

> **状态：✅ 已执行（2026-08-25）**。core 包 0.2.0 接入产品 csproj；`MaterialColorMapper`/`MaterialSchemeGenerator` 落地；`ApplyScheme` 替换 `ApplyAccentBrushes`（保留覆盖子集 + Secondary/Tertiary/容器角色 + 可选 Surface/Outline）；on-color 亮度规则（0.45 阈值）测试锁定；三个设置字段持久化 + 规范化 + 旧 JSON 兼容（UI 在 P2）；headless 选择视觉断言改为语义化（对比运行时 Primary 资源）。门禁：0 警告、单测 1098 通过、headless 99 通过。**M3 门禁通过**（人工壁纸冒烟由 headless 选择/刷新流覆盖，完整人工冒烟建议 P2 UI 后统一执行）。

- 依赖：产品 csproj 按 M0 结论加包（core 必加；集成包视 M0）。
- 转换层：`Helpers/` 或 `Services/` 新增 `ArgbColor`↔Avalonia `Color`/`SolidColorBrush` 映射 + 参考值单测（M0 表复用）。
- **`ApplyScheme`**（替代扩展 `SettingsAppearanceViewModel.ApplyAccentBrushes`，保留原 11 笔刷覆盖行为作为子集）：输入（seed、variant、theme、neutralStrategy）→ 输出 primary/secondary/tertiary + containers + （seed 策略时）surface/outline 覆盖；on-color 按亮度计算（替换硬编码 `Launcher.OnAccent` 使用点）。
- `ColorUtils` 扩展：on-color 计算、现有一致性保留（饱和度/明度归一化规则不动），全部纯函数化。
- 单测：参考值断言（seed→roles，含 ±1 tone）、on-color 黑/白选择边界（如亮度 0.5 分界）、neutral 双策略输出、非壁纸模式（系统/默认蓝/自定义）行为等价性。
- 兼容：旧 `settings.json` 无新字段 → 规范化默认值路径；壁纸模式默认算法切换记录（spec §7）。
- 门禁：动态色单测全绿；现有壁纸模式手工冒烟（设置→壁纸色→刷新）行为不回归。

## M4 — 画廊 token 表（1–2 天）

> **状态：✅ 已执行（2026-08-25）**。`DesignGalleryOverlay` + `DesignGalleryViewModel` + `DesignTokenGrouping`（运行时资源枚举 + 键段分类，零漂移）；Debug 面板入口 + `dialog-overlay` 层接入；16 个本地化键（4 语言 + Designer 再生成）；契约测试纳入画廊 ViewFiles；headless 开合冒烟（≥12 族、≥130 token）。门禁：0 警告、单测 1105 通过、headless 100 通过、本地化契约绿。**M4 门禁通过**。

- `Views/DesignGallery.axaml`（+ ViewModel）：按家族分组展示 token（色板 widget、字阶、间距/圆角/动效/elevation 表）；数据源从 `/Application.Resources` 枚举 + 分类元数据（避免手工同步漂移）。
- 接入：`IsDebugFeaturesEnabled` 门（与调试面板同样式），Debug 构建可见；resx 4 语言（画廊标题/分组名）+ `AutomationProperties.Name`；`UiStyleContractTests` 若覆盖画廊 XAML 则补。
- 门禁：Debug 构建画廊可打开、localization 契约绿。

## M5 — 黄金截图基建（1–2 天，小集）

- `tests/Cafe.Launcher.Avalonia.HeadlessTests/`：`AvaloniaHeadlessPlatformOptions` 启用 Skia 渲染（`UseHeadlessDrawing=false`）+ `RenderTargetBitmap` 捕获；基线 3–5 个（壳默认、进度态、设置覆盖层、确认对话框、Toast）√ 与基线平铺存储 + 阈值 diff；字体稳定性：固定测试呈现字体、CI（windows-latest）字体集合风险记录进 README 注释。
- 门禁：基线测试本地绿；CI 跑同环境验证。

## 交付物与门禁汇总

| 里程碑 | 关键交付 | 门禁 |
|---|---|---|
| M0 | spike 结论归档（含新旧公式差异表）✅ 已归档 | Go/No-Go ✅ GO |
| M1 | 映射表 + 脚本 + 全量重命名 commit 序列 | 旧键 0、零警告、测试绿 |
| M2 | 新族 token + 契约/对比度测试 | 契约绿、AA 达标（最小调色记录） |
| M3 | ApplyScheme + 动态色单测 | 单测绿、壁纸模式冒烟通过 |
| M4 | 画廊（token 表） | Debug 可开、本地化绿 |
| M5 | 黄金截图基建 + 基线 | 基线绿 |

总估：**10–15 人日**（含 M0 对照验证；不含回退方案）。建议顺序 M0 →（M1 ∥ M2）→ M3 →（M4 ∥ M5）；M1/M2 与 M3 基本正交可并行，合流前各自门禁。

## 风险表

| 风险 | 缓解 |
|---|---|
| 包"WIP"声明 + Avalonia 12 preview 依赖 | M0 spike 判定；失败 → vendor 回退（已备档） |
| HCT ±1 tone 漂移 | 参考值断言 + ±1 容差；上游单测随 vendor 迁移 |
| 映射遗漏（~120 键 × 多消费方） | 脚本双程校验（替换后旧键归零断言）+ 编译零警告兜底 |
| 测试契约漏同步 | M1 同 PR 更新；旧键清零断言防回归 |
| 对比度调色引发视觉漂移 | 只做阈值最小修正，before/after 记录进 spec §8 |
| Golden 截图字体/平台差异 flaky | 固定渲染字体 + 阈值 + CI 同环境验证；小集起步 |

## P2/P3 预告（不在本计划执行）

- P2：组件 M3 化（A→B→C）、三个新设置项 UI、画廊组件状态矩阵 + 底栏双原型（Q18 仲裁）、`UiStyleContractTests`/走查清单更新。
- P3：表面序列（主壳→设置→对话框+Toast→向导→诊断/日志/资源面板兼容）、底栏形态按仲裁落地、黄金截图扩大。
