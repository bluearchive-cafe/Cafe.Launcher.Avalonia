# Cafe Launcher 设计系统规范（Design System Specification）

> 状态：**已确认**（grilling 会话 Q1–Q24 决策全景，2025-xx 基线）。本文件是 Q4-b 交付物：代码之外的人读设计规范。
> 配套演示稿：[q5-direction-mockup.html](./q5-direction-mockup.html)（Q5 四方向对比 + 组件质感对照）。
> 落地编排见 [p1-implementation-plan.md](./p1-implementation-plan.md)。

---

## 1. 设计原则

1. **壁纸优先**：主壳身份 = 动态壁纸画布 + 悬浮信息层（M3 表面语义）。任何改动不得牺牲壁纸表现力，壁纸区域文字以最小 scrim 保证可读（见 §8 豁免区）。
2. **M3 语义、Fluent 底座**：Material Design 3 是规范与语义模型（tokens、色阶、形状字阶动效）；Avalonia FluentTheme 是实现底座；**不引入 Material.Avalonia**（其为 M2 世代，无 HCT/动态色，主题系统已 Obsolete）。
3. **全 token 化**：视图 XAML 只允许引用 `Launcher.*` token；原始色值、裸间距/圆角只允许出现在 `App.axaml` 定义处（`UiStyleContractTests` 锁定）。
4. **离线自包含**：动态色在本地生成（壁纸取色、M3 scheme 计算均无网络/遥测）。
5. **契约优先、分阶段演进**：AA 对比度、token 存在性、覆盖层 Z 序由测试锁定；视觉变化按 P1（token 体系）→ P2（组件）→ P3（表面）落地，每阶段可独立验收回退。

## 2. 决策日志（grilling 会话，按问题编号）

| Q | 决策 | 影响 |
|---|---|---|
| Q1 | 动因 = 系统化 + 视觉现代化 + 一致性 | 体系先行，观感随后 |
| Q2 | 分阶段：P1 token 体系 → P2 组件 → P3 表面 | 见 §10 |
| Q3 | 范围 = 全部表面分批：第一批主壳/设置/对话框/Toast/向导；第二批诊断/日志/资源面板**仅 token 兼容** | 排障界面不做视觉重设计 |
| Q4 | 交付 = 代码 + 仓库内人读规范（本文件）；无 Figma 工作流 | 活文档随 PR 维护 |
| Q5 | **M3/Material You 为规范骨架**；FluentTheme 底座；不引入 Material.Avalonia | 本节全部后续分支 |
| Q9 | WCAG AA 对比度自动化 + 键盘/焦点/覆盖层审计（硬契约） | §8 |
| Q10 | 验收 = 契约测试扩展 + 文档走查清单 + headless 黄金截图（基建从零接，P1 小集） | §10 |
| Q11 | Dev 模式"设计画廊"页：token 表 + 组件状态矩阵 + 原型对比区（支撑 Q18 仲裁） | §9 |
| Q12 | 表现级布局重构允许；单窗口 + 覆盖层架构、Z 序（100/200/500/1000）、功能分区不变 | §5 |
| Q13 | 动态色 = **accent + container 双色阶**；表面中性（随主题模式）；**on-color 按亮度计算** | §3.4 |
| Q14 | 系统字体 + M3 字阶角色映射；字体栈优先系统可用的 Noto Sans | §3.5 |
| Q15 | **class + ControlTheme 混合**：既有 class 选择器一律不动；新组件走 ControlTheme + 新命名 | §4 |
| Q16/Q20 | **点分层命名** + **一次性全量重命名**（脚本+映射表，不留别名层） | §3.1 |
| Q17 | **NuGet 直用** `Shirasagi0012.MaterialColorUtilities`（core + Avalonia 集成包，net8/net10，Apache-2.0）；先 spike 验证；vendor 裁剪核心为留档回退 | §3.4、P1 M0 |
| Q18 | 底栏形态延后：画廊双原型（浮动玻璃胶囊 vs M3 贴边）仲裁；**倾向浮动胶囊** | §5、P2/P3 |
| Q19 | AA 契约范围 = 面板内全指标 + 交互元素；**over-wallpaper 豁免 + 最小 scrim（token 化）** | §8 |
| Q21 | P2 组件 = A→B→C；诊断/日志列表维持 Fluent 基础模板 | §4 |
| Q22 | P3 表面顺序 = 主壳 → 设置 → 对话框+Toast → 首次向导 → 诊断/日志/资源面板 | §10 |
| Q23 | 中性色策略**可切换**：默认品牌蓝调（固定），可选种子跟随（tone 固定、只漂色相） | §3.4 |
| Q24 | 新增设置项（外观组「主题颜色」）：**取色算法**（octree 保留 / M3 Celebi+Score 默认 / Wu·Wsmeans 可选）+ **配色变体**（8 变体，默认 TonalSpot）+ **中性色策略** | §7 |

## 3. Token 体系

### 3.1 命名规则

- 格式：`Launcher.<Family>.<Role>[.<State>]`，点分层、字符串键、PascalCase 段。示例：`Launcher.Color.Primary`、`Launcher.Spacing.Md`、`Launcher.Motion.Duration.Normal`、`Launcher.Elevation.Shadow.Md`。
- **P1 一次性重命名**全部既有 ~120 个 `Launcher*` 键（脚本 + 映射表，见 P1 计划 M1），不留别名层、不保留旧键；`UiStyleContractTests` 与全部消费方同步，编译零警告收口。
- 引用规则：**静态 token 一律 `{StaticResource}`**；仅主题变体/运行时覆盖（scheme 角色、Dark/Light 分档）用 `{DynamicResource}`。P1 修复 `Views\Styles\{Toast,RemoteContent,SetupWizard}.axaml` 中 33 处静态 token 误用 `{DynamicResource}` 的漂移。
- 原始色值只允许出现在 `App.axaml`（及未来主题字典文件）的 token 定义处；`MainWindow.Styles.axaml` 中 2 处 BoxShadow 与 1 处主题无关渐变（有注释声明）迁为 token 定义后再引用。**（M2 实测限制并记录：`BoxShadow` 是无 `TypeConverter` 的结构体，Avalonia 12.1.1 无法在 `Setter` 处经 `{StaticResource}` 做运行时字符串转换——字面量之所以可用是 XamlIl 编译期解析。故阴影值仍集中在 `Launcher.Elevation.Shadow.*` token（契约测试锁定），消费点暂用字面量 + 注释引用；待 Avalonia 提供转换器后改回 StaticResource。）**

### 3.2 家族清单

| 家族 | 内容 | 示例 |
|---|---|---|
| `Launcher.Color.*` | M3 scheme 角色（primary/secondary/tertiary + containers + surface/outline/error 族）+ 业务语义色（Success/Warning/Info）+ 主题无关特殊色（Chrome/Overlay/scrim/媒体占位） | `Color.Primary`、`Color.OnPrimary`、`Color.PrimaryContainer`、`Color.Surface`、`Color.Outline`、`Color.Danger`、`Color.Success`、`Color.Overlay.Scrim` |
| `Launcher.Text.*` | 应用文本专用角色（Primary/Secondary/Body/Link/OnChrome/OnDark/Placeholder 等） | `Text.Primary`、`Text.OnChrome` |
| `Launcher.Spacing.*` | 间距 4/8/12/16/20/24/40 全档（Double）+ Thickness 全档（`Spacing.Thickness.*`） | `Spacing.Md`、`Spacing.Thickness.Lg` |
| `Launcher.Radius.*` | 目标字阶 4/8/12/16/28（`Xs/Sm/Md/Lg/Xxl`）；P1 仅重命名保留现值，P2 统一取值（6→8 等） | `Radius.Md` |
| `Launcher.Typography.*` | M3 角色字阶（Display/Headline/Title/Body/Label × 字号/字重/字距）+ 字体族 | `Typography.FontSize.Body.Md`、`Typography.FontWeight.Strong`、`Typography.FontFamily.Monospace` |
| `Launcher.Icon.*` | 图标尺寸 16/18/20/22/24 | `Icon.Md` |
| `Launcher.Control.*` | 控件高度/宽度（Setting36/Dialog42/Bottom48/Launch58/Field40/Chip32/Swatch28 等） | `Control.Height.Setting` |
| `Launcher.Layout.*` | 视口级布局常量（banner 高 220、news 视口 184、调试窗 720×540、日志窗 720×592 等） | `Layout.Banner.Height` |
| `Launcher.Motion.*` | 时长/缓动/偏移（见 §3.7） | `Motion.Duration.Normal` |
| `Launcher.Elevation.*` | **新增**：阴影 0–3 档（颜色/偏移/模糊 token 化） | `Elevation.Shadow.Md`、`Elevation.Level.Card` |
| `Launcher.StateLayer.*` | **新增**：状态层不透明度 8%/12%/16%/24% | `StateLayer.Hover` |
| `Launcher.Component.*` | 组件专属覆盖（Toast 宽度、对话框标题高度等） | `Component.Toast.Width` |

### 3.3 语义层级

- **Primitive**（原始刻度：色值、间距、字号、时长）→ **Semantic**（角色：`Color.Primary`、`Text.Secondary`、`Radius.Md`）→ **Component**（组件专属：`Component.Toast.Width`）。
- Light/Dark：由 `ResourceDictionary.ThemeDictionaries` 承载每变体值（沿用现状）；**scheme 角色（Primary/Container 族）由运行时按主题模式与 DynamicScheme 覆盖**——XAML 中声明值仅为占位/回退。

### 3.4 色板与动态色（Q13/Q17/Q23/Q24）

```text
壁纸/系统/自定义 种子 ──取色算法──┐
（算法：Octree[保留] / Celebi+Score[默认] / Wu / Wsmeans）
                                 └──> 种子色 ──配色变体（Variant）──> 完整 M3 DynamicScheme
                                  （TonalSpot[默认]/Vibrant/Expressive/Fidelity/Content/Monochrome/Neutral/Rainbow/FruitSalad）
                                                                          │
                                                     ┌────────────────────┴────────────────────┐
                                    中性色策略=品牌蓝调（默认）                中性色策略=种子跟随
                                    surface/outline 用固定 token 值        surface/outline 由 neutral/neutralVariant 生成
                                                                          （tone 档位固定，仅色相随种子→对比度仍安全）
```

- **角色集**：primary/secondary/tertiary + 各自 `On*`/`*Container`/`On*Container`；surface 族（Surface/SurfaceVariant/Container 档）、outline 族、error 族（映射现有 Danger/DangerSoft/危险图标底）；业务色 Success/Warning/Info（Toast 等，不参与动态色）。
- **on-color 按亮度计算**（P1 必修）：`Launcher.OnAccent` 不再硬编码白；按背景色相对亮度选黑/白（浅种子时用深 on-color），写入 `ColorUtils` 并测试锁定。
- **实现**：`ApplyAccentBrushes`（`Features/Settings/SettingsAppearanceViewModel.cs`）演进为 `ApplyScheme`（输入 seed/variant/theme/neutralStrategy → 覆盖 brush 组）；`ThemeColorExtractionService`（Octree）与新算法共存；`ColorUtils`（`Helpers/ColorUtils.cs`）承载归一化与 on-color 计算。非壁纸模式（系统/自定义/默认色）产出与现状一致或改善。
- **HCT 管线**：`Shirasagi0012.MaterialColorUtilities`（NuGet 直用）；ArgbColor↔Avalonia `Color` 映射封装；±1 tone 漂移以包内上游单测 + 参考值断言为护栏。

### 3.5 字阶与字体（Q14）

| M3 角色 | 字号 | 字重 | 用途映射（现有） |
|---|---|---|---|
| Display | 22 | SemiBold | `heading`（主标题） |
| Headline | 19 / 18 | SemiBold | `dialog-title`、`category-title` |
| Title | 17 / 16 | SemiBold | `progress-title`、`titlebar-brand`、`section-title` |
| Body | 15 / 14 / 13 | Normal | `value`、`body`、正文 |
| Label | 12 / 11 | Normal/SemiBold | `caption`、`chip-text`、`kicker` |

- 字体栈：`Noto Sans`（若系统可用/安装）→ `Segoe UI` → `Microsoft YaHei`（CJK 回退）→ `sans-serif`；等宽 `Consolas`。沿用 `Shell.FontFamily` 机制；**不内嵌字体文件**（Q14-a）。
- 现有 `LauncherFontSizeXs..Display`（11–22 共 9 档）具体归组见 P1 映射表。

### 3.6 尺寸与形状

- 间距：4/8/12/16/20/24/40；Thickness 补齐全档（现状仅 4/8/20 有 Thickness 变体）。
- 圆角目标字阶：4（Xs）/ 8（Sm）/ 12（Md）/ 16（Lg）/ 28（Xxl 或 full）；P1 保持现值，P2 按字阶统一（注意 6→8 的视觉迁移记录）。
- 图标 16/18/20/22/24；控件高度 Setting36/Dialog42/Bottom48/Launch58/Field40/Chip32/Swatch28/DialogTitle56（P2 结合 M3 尺寸规范评审是否微调）。

### 3.7 动效

- 时长：`Fastest` 50ms / `Fast` 167ms / `Content` 200ms / `Normal` 250ms；缓动：`Enter`=ExponentialEaseOut、`Exit`=ExponentialEaseIn、`Linear`；偏移：`Surface` 8 / `Content` 6 / `Bottom` 12 / `Toast` 6（沿用现有值，仅重命名）。
- 全局 `IsMotionEnabled` 开关保留；**系统级 reduced-motion 检测暂不做**（记录为有意搁置）。

## 4. 组件规范（P2，Q15/Q21）

- 既有 class 选择器**一律保留**：`Button.primary-action`/`flat-action`/`danger-action`/`text-link`/`icon-link`/`icon-button`、`Border.*card`、`ListBox.settings-navigation` 等（headless 测试与视图依赖此兼容面）。
- 按钮四型 M3 映射：`primary-action`→filled；`flat-action`→outlined（语境需要时可 tonal）；`text-link`→text；`danger-action`→error-filled。共享模板 `LauncherBorderButtonTemplate` 保留。
- 新组件（Select、Chip、分页、滑块等）以 **ControlTheme + 新命名** 落地，token 走 `Launcher.Component.*`。
- **状态矩阵**（画廊展示 + 走查清单共用）：normal / hover / pressed / disabled / focus-visible / invalid × 各组件；状态层按 `StateLayer.*` 不透明度。
- 例外：诊断面板与日志查看器列表控件维持 Fluent 基础模板，仅 token 兼容（Q3/Q21 例外声明）。

## 5. 表面与布局（Q12/Q18）

- 单窗口 + 覆盖层：Z 序 = 主内容 → 设置 100 → 对话框 200 → 向导 500 → Toast 1000，**不动**。
- 功能分区（Shell / GameOperations / Settings / SetupWizard / Diagnostics / ResourcePanel）与"单窗口内选分类"模型不变；表现级布局自由（如设置页留白、卡片形态）。
- **底栏形态开放**：P2 画廊先做"浮动玻璃胶囊 vs M3 贴边底栏"两个实物原型 → 走查清单仲裁 → P3 落地（倾向浮动胶囊；浮动需统一控制/进度/安装三态，且 scrim 可读性满足 §8）。

## 6. 主题与壁纸

- `RequestedThemeVariant="Default"` 跟随系统不变；主题模式（System/Light/Dark）持久化机制不变。
- 背景来源/适配/填充/自定义图片/文件夹机制不变；`launcher-background.png` 保留。
- 壁纸取色：现有"壁纸模式色板 + 刷新"交互保留并整合进新管线（种子 = 刷新时的算法输出；色板刷新 = 重新取种子 → 重新生成 scheme）。

## 7. 设置变更（动态色相关，Q23/Q24）

外观组「主题颜色」新增 3 个设置项（每个 1 行，走 `SettingOption` 枚举 + 4 语言 resx + `AutomationProperties.Name`）：

| 设置项 | 选项 | 默认 | 说明 |
|---|---|---|---|
| 取色算法 | Octree（现状保留）/ **M3（Celebi+Score）** / Wu / Wsmeans | M3 | 决定壁纸种子来源 |
| 配色变体 | *8 变体（全量，包内现成）* | TonalSpot | 决定种子→色阶风格 |
| 中性色 | **品牌蓝调（固定）** / 种子跟随 | 品牌蓝调 | surface/outline 是否随种子染色 |

- 持久化：`LauncherSettings` 新增字段 + 规范化（默认值填充），向后兼容旧 `settings.json`；遵循 `SettingsEditor` 事务性保存。
- 兼容说明：既有"壁纸模式"用户默认算法从 Octree 切换为 M3 后，accent 会有轻微变化——属视觉漂移，记录为可接受（与 P3 改版同期）。

## 8. 无障碍（Q9/Q19）

- **AA 契约（自动化）**：正文文本 ≥4.5:1、UI 组件/非文本 ≥3:1（WCAG 相对亮度计算）。实现为 `DesignTokenContrastTests`（token 对清单驱动），P1 调色不达标的现役 token（如发现）微调 Light/Dark 双档值。
- **M2 静态 token 最小调色记录（before → after，均经契约测试锁定）**：

  | Token | Light | Dark | 修复对象 |
  |---|---|---|---|
  | `Color.Danger` | `#E5484D` → `#D93840`（白标签 3.91→4.58） | 同单值 | danger-action 白字 ≥4.5 |
  | `Color.Danger.Hover` | `#F15B60` → `#D32F35`（3.28→4.97） | 同单值 | 同上（hover 态） |
  | `Color.Success` | 单值 `#22C55E` → 拆双档：Light `#15803D`（白底 2.22→5.02）/ Dark `#4ADE80`（暗底 9.8） | | 向导就绪状态文字；单值无法同时满足明暗 |
  | `Color.Warning` | `#F59E0B` → `#B45309`（白底 2.15→5.03；暗底 3.07≥3） | 同单值 | toast 警告严重性 UI |
  | `Text.Secondary`（Light） | `#68717D` → `#646D79`（警告面 4.41→4.69；ContentRow 4.51→4.80） | 不变 | 警告/提示底上的次级文字 |
  | `Text.Media.Placeholder` | `#88FFFFFF` → `#99FFFFFF`（4.47→5.19） | 不变 | 媒体占位文字 ≥4.5 |

  `Danger.Pressed` 保留 `#C9353A`（白标签 5.18；其填充态消费由标签对覆盖，不做表面图标对）。全部为同色相单阶加深/提亮，未改视觉意图；UI ≥3:1 额外覆盖 Danger×Card/Dialog 与 Toast 严重性四色。
- **豁免区（显式声明 + 测试用例列出）**：自绘标题栏文字、标题栏按钮（chrome 态）、右侧社交列、底栏渐变/scrim 上的 `on-dark` 文字——豁免前提是叠加 scrim（现有 `LauncherTitleBarGradient` + 新增 `Color.Overlay.Scrim*` token，最小 scrim 语义：保证该区文字与当前壁纸任意色对比度可读，豁免在走查清单中逐项复核）。
- 键盘：全表面 Tab 序、可见焦点环（`Button:focus-visible` 等既有规则随重命名迁移）、覆盖层焦点陷阱（`OverlayFocusBehavior` 保留）。
- 动效：`IsMotionEnabled` 保留；文本缩放支持**有意搁置**（固定控件高度体系改造为 min-height 自适应，放入未来评估，不承诺）。

## 9. 设计画廊（Q11）

- 位置：Debug 构建 `IsDebugFeaturesEnabled` 可见（与现有调试面板同门），`Views/` 新增 `DesignGallery.axaml`。
- 内容：P1 = token 总表（按 §3.2 家族分组：色板 swatch、字阶、间距/圆角/动效表）；P2 = 组件状态矩阵 + 底栏双原型对比区（Q18 仲裁）；组件状态矩阵同时是走查清单的实物载体。
- 画廊文案走本地化契约（resx 4 语言）。

## 10. 验收与流程（Q10）

- **每阶段门禁**：`UiStyleContractTests` 全绿（含新契约）+ 新增测试全绿 + 编译零警告 + `scripts/Test-LocalizationContract.ps1`（涉及新字符串时）+ `.\verify.ps1`；PR 附截图（可见 UI 变更）。
- **契约测试扩展点**：token 存在性/旧键清零、禁裸色值与 `{StaticResource}` 规则、AA 对比度、覆盖层 Z 序（既有）。
- **headless 黄金截图**：P1 接线（`UseHeadlessDrawing=false` + Skia）并建 3–5 个基线（壳默认/进度/对话框/设置）；P3 视需要扩大；CI 字体稳定性作为已知风险管理。
- **走查清单**：随本规范维护（§4 状态矩阵 + §8 豁免区逐项），P3 每表面完成时人工复核。

## 11. 有意搁置（Don't-do 清单）

- 系统级 reduced-motion 检测（Avalonia 暴露面待调研）。
- 文本缩放 125–150% 支持（尺寸体系改造，评估期）。
- 诊断/日志/资源面板的视觉重设计（仅 token 兼容）。
- 独立"画廊预览窗口"（Q11-c 叠加项，暂不启用）。
- 对外部设计工具（Figma 等）的同步链路。
- 若 spike 判定 NuGet 包不可用 → **回退方案**：vendor `Shirasagi0012` 裁剪核心（Apache-2.0，砍 quantize/Score，HCT+palette+scheme+dynamic ≈4–5k 行，2–4 人日），已调研备档；同谱系 `MaterialColorUtilities`（albi005）源码仅作交叉参考，不进入产品依赖（其 API 为 2023 旧世代，差异量化表见 M0 产物）。

---

*维护约定：本规范随 P1–P3 每个阶段更新；变更走 PR 并在 §2 决策日志追加记录。*
