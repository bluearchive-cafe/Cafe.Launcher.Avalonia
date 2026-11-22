# Cafe Launcher 设计系统规范（Design System Specification）

> 状态：**现行设计规范**。当前控件基准、设计原则和各表面条目的最新修订用于验收。旧控件模板和视觉数值记录保留设计沿革。附录 A 与 [ADR 索引](adr/README.md) 记录决策。
>
> 分工：视觉数值、组件与表面规格归本文件；界面行为语义（模态交互权、ESC、动效模式）归 [CONTEXT.md「设计系统」](../../CONTEXT.md#设计系统)；XAML 与样式写法归[开发规范 §4](../../PROJECT_CONVENTIONS.md#4-xaml-与视觉规范)。

---

## 当前控件基准（2026-11-17）

应用与独立崩溃报告进程使用 FluentAvalonia 默认控件模板。按钮、下拉项、菜单、标签页、
输入、焦点以及禁用状态由主题绘制。设置行使用 `FASettingsExpanderItem`，设置和共享对话框的动作组使用
`WrapPanel.dialog-actions`，Toast 使用 `FAInfoBar`。业务视图继续通过标准 Avalonia 面板组合；模态栈保持原有
ESC、焦点恢复和命令语义。壁纸与动效仍由启动器管理。

外观设置向 Fluent 传递强调色；现有中性色设置仍影响引用启动器画刷的背景与业务内容。
下文的自绘控件模板、交互状态、数值和色板桥接规格为迁移前记录，现行规则以本节和
[ADR-068](adr/ADR-068-优先使用FluentAvalonia默认控件.md) 为准。业务表面结构和交互行为
以 [领域契约](../../CONTEXT.md) 为准。

## 1. 设计原则

1. **壁纸优先**。主壳用整窗壁纸承载游戏身份和资讯。壁纸上的文字和控件须在明暗图片上保持可读。
2. **使用 FluentAvalonia 默认控件**。主窗口和独立崩溃报告进程都加载 `FluentAvaloniaTheme`。控件交互、焦点、字阶和禁用状态沿用主题资源。
3. **通过语义资源组织业务表面**。标准 Avalonia 面板负责页面布局，控件和业务容器分别复用 Fluent 主题资源与相应的布局令牌。启动器通过 `CustomAccentColor` 设置主题强调色，M3 动态色和壁纸取色均在本地计算。
4. **保留任务与阅读层级**。用留白、字重、分组和单个主要动作建立层级，保留必要的确认和故障恢复信息。
5. **检验真实状态**。亮暗主题、四语言、文字放大、最小窗口、键盘焦点和模态层顺序均纳入验收。

## 2. Token 体系

### 2.1 命名与引用

- 格式：`Launcher.<Family>.<Role>[.<State>]`，点分层、字符串键、PascalCase 段。示例：`Launcher.Color.Primary`、`Launcher.Spacing.Md`、`Launcher.Motion.Duration.Normal`、`Launcher.Elevation.Shadow.Md`。
- **P1 一次性重命名**全部既有 ~120 个 `Launcher*` 键（脚本 + 映射表，见 P1 计划 M1），不留别名层、不保留旧键；`UiStyleContractTests` 与全部消费方同步，编译零警告收口。
- 引用规则：**静态 token 一律 `{StaticResource}`**；仅主题变体/运行时覆盖（scheme 角色、Dark/Light 分档）用 `{DynamicResource}`。P1 修复 `Views\Styles\{Toast,RemoteContent,SetupWizard}.axaml` 中 33 处静态 token 误用 `{DynamicResource}` 的漂移。
- 原始色值只允许出现在 `App.axaml`（及未来主题字典文件）的 token 定义处；`MainWindow.Styles.axaml` 中 2 处 BoxShadow 与 1 处主题无关渐变（有注释声明）迁为 token 定义后再引用。**（M2 实测限制并记录：`BoxShadow` 是无 `TypeConverter` 的结构体，Avalonia 12.1.1 无法在 `Setter` 处经 `{StaticResource}` 做运行时字符串转换——字面量可用是因为 XamlIl 编译期解析。故阴影值仍集中在 `Launcher.Elevation.Shadow.*` token（契约测试锁定），消费点暂用字面量 + 注释引用；待 Avalonia 提供转换器后改回 StaticResource。）**

### 2.2 家族清单

| 家族 | 内容 | 示例 |
|---|---|---|
| `Launcher.Color.*` | M3 scheme 角色（primary/secondary/tertiary + containers + surface/outline/error 族）+ 业务语义色（Success/Warning/Info）+ 主题无关特殊色（Chrome/Overlay/scrim/媒体占位）+ 壁纸上的半透明表面（Glass.Background/Glass.Card.Background）及其语义前景（Danger/Info.OnGlass） | `Color.Primary`、`Color.OnPrimary`、`Color.PrimaryContainer`、`Color.Surface`、`Color.Outline`、`Color.Danger`、`Color.Danger.OnGlass`、`Color.Success`、`Color.Overlay.Scrim`、`Color.Glass.Background` |
| `Launcher.Text.*` | 应用文本专用角色（Primary/Secondary/Body/Link/OnChrome/OnDark/Placeholder 等） | `Text.Primary`、`Text.OnChrome` |
| `Launcher.Spacing.*` | 间距 0/4/8/12/16/20/24/32/40 全档（Double，`Spacing.None`=0 为零间距显式档）+ Thickness 全档（`Spacing.Thickness.*`） | `Spacing.Md`、`Spacing.Thickness.Lg` |
| `Launcher.Radius.*` | Fluent 2 圆角：`Xs`=4（交互面）、`Sm`/`Md`/`Lg`=8（卡片与弹层）、`Full`=999（整圆） | `Radius.Xs`、`Radius.Sm` |
| `Launcher.Typography.*` | M3 角色字阶（Display/Headline/Title/Body/Label × 字号/字重/字距）+ 字体族 | `Typography.FontSize.Body.Md`、`Typography.FontWeight.Strong`、`Typography.FontFamily.Monospace` |
| `Launcher.Icon.*` | 图标尺寸 16/18/20/22/24 | `Icon.Md` |

| `Launcher.Control.*` | 控件高度/宽度（Setting32/Dialog32/Bottom48/Launch58/Field32/Chip32/Swatch28 等） | `Control.Height.Setting` |
| `Launcher.Layout.*` | 视口级布局常量（横幅高 220、资讯视口 184、调试窗 720×540、日志窗 720×592 等） | `Layout.Banner.Height` |
| `Launcher.Motion.*` | 时长/缓动/偏移（见 §2.7） | `Motion.Duration.Normal` |
| `Launcher.Elevation.*` | 阴影 0–3 档（颜色/偏移/模糊 token 化） | `Elevation.Shadow.Md`、`Elevation.Level.Card` |
| `Launcher.StateLayer.*` | 状态层不透明度 8%/12%/16%/24% + `Disabled.Content` 38%；**消费纪律（统一-0921）**：样式/视图/控件的 `Opacity` 只允许结构性 `0`/`1` 或引用本族——分数透明度的唯一合法来源（契约锁定）；半透明视觉（hover/pressed 叠加、scrim、阴影）以画刷 alpha 在 token 定义处表达，不写元素 Opacity | `StateLayer.Hover`、`StateLayer.Disabled.Content` |
| `Launcher.Border.*` | 边框厚度刻度（Default 1 / Focus 2，2026-08-26 补录） | `Border.Thickness.Default` |
| `Launcher.Component.*` | 组件专属覆盖（Toast 宽度、对话框标题高度等） | `Component.Toast.Width` |

启动器图标使用 `FluentIcon`，游戏管理菜单经 `FluentIconSource` 创建同一控件。控件从 `SymbolThemeFontFamily` 提取并缓存矢量轮廓，绘制时按窗口 DPI 对齐像素。`Constants/FluentGlyphs.cs` 统一定义 C# 与 XAML 使用的码点。FluentAvalonia 主题负责绘制控件模板自带的图标。[ADR-064](adr/ADR-064-图标语言统一为Fluent符号.md)

### 2.3 语义层级

- **Primitive**（原始刻度：色值、间距、字号、时长）→ **Semantic**（角色：`Color.Primary`、`Text.Secondary`、`Radius.Md`）→ **Component**（组件专属：`Component.Toast.Width`）。
- Light/Dark：由 `ResourceDictionary.ThemeDictionaries` 承载每变体值（沿用现状）；**scheme 角色（Primary/Container 族）由运行时按主题模式与 DynamicScheme 覆盖**——XAML 中声明值仅为占位/回退。

### 2.4 色板与动态色（Q13/Q17/Q23/Q24）

```text
壁纸/系统/自定义 种子 ──取色算法──┐
（算法：Octree[保留] / Celebi+Score[默认] / Wu / Wsmeans）
                                 └──> 种子色 ──配色变体（Variant）──> 完整 M3 DynamicScheme
                                  （TonalSpot[默认]/Vibrant/Expressive/Fidelity/Content/Monochrome/Neutral/Rainbow，共 8 变体）
                                                                          │
                                                     ┌────────────────────┴────────────────────┐
                                    中性色策略=品牌蓝调（默认）                中性色策略=种子跟随
                                    surface/outline 用固定 token 值        surface/outline 由 neutral/neutralVariant 生成
                                                                          （tone 档位固定，仅色相随种子→对比度仍安全）
```

- **角色集**：primary/secondary/tertiary + 各自 `On*`/`*Container`/`On*Container`（`PrimaryContainer`/`OnPrimaryContainer` 于 2026-08-26 补齐，占位+运行时覆盖）；surface 族（Surface/SurfaceVariant/Container 档）、outline 族、error 族（映射现有 Danger/DangerSoft/危险图标底）；业务色 Success/Warning/Info（Toast 等，不参与动态色）。**落地范围声明**：`SurfaceVariant`/`OutlineVariant` 由业务 token 承担（hairline 用 `Color.Card.Border`，ADR-013 M3 修正 2）；实心表面配色包括 `Dialog.Background/Header/Footer/Close.Hover/Close.Pressed`（`Header` 仅供首次设置向导自绘头部使用）（默认中性色时五键复位为声明默认值，跟随主题色时整套随 scheme 中性表面梯度染色——Background/Header/Footer=`SurfaceContainerHigh`，Close.Hover/Pressed 为 `OnSurface` 的预混状态层；2026-08-28 由仅 `Dialog.Background` 扩展为全族并防残留——Q13/Q23 两决策合流）；`Surface/OnSurface/Outline` 保留为语义角色（`Outline` 由 `dialog-card` 的 1 DIP 装饰描边使用；`Surface/OnSurface` 为未来表面预留）。
- 系统来源读取平台设置的原始强调色。主题方案通过 `CustomAccentColor` 和 Fluent 公开的 `SystemAccentColor*` 色阶资源更新控件。色阶取自 HCT 主色色板，浅色和深色模式的实心强调状态使用当前主色。首页主分体按钮在自己的资源范围内复用 Fluent 强调按钮的常态、悬停、按下和禁用画刷。
- 主界面使用半透明实底表面；对话框与设置使用实心中性表面，通知使用 `FAInfoBar` 的实心语义底色与同色收边（[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)）。首页左侧图标栏使用 `Launcher.Color.Glass.Rail.Background`（约 92% 不透明），资讯面板、浮动任务卡、管理菜单与首页工具提示使用 `Launcher.Color.Glass.Strong.Background`（浅色约 79%、深色约 82% 不透明）；其余共享外壳继续使用 `Launcher.Color.Glass.Background`（约 90%）与内嵌卡片 `Launcher.Color.Glass.Card.Background`（约 94%）。这些表面由 `Border` 直接以 token 绘制底色与发丝描边，不做窗口内采样模糊，因此不依赖 GPU 捕获管线，也没有软件渲染、远程会话或高对比下的材质降级分支；视觉上与壁纸叠加，而不是让桌面透出。不透明度下限按极端明暗壁纸校准，保证正文仍满足 §7 的 AA 对比度。选择「跟随主题色」时，表面色相分别跟随 `SurfaceContainerHigh` 与 `SurfaceContainerHighest`，切回默认中性色时恢复各自的明暗配色。窗口自身与媒体占位仍使用不透明的 `Launcher.Color.Dialog.Background`，避免在壁纸尚未加载时露出桌面。半透明表面上的语义前景另用 `Launcher.Color.Danger.OnGlass` 与 `Launcher.Color.Info.OnGlass`：根档颜色按各自主题的不透明表面校准，透到极端明暗壁纸时危险文字仍满足 4.5:1、提示/警告/危险图标仍满足 3:1；其余正文使用 `Launcher.Text.*`。
- **on-color 按亮度计算**（P1 必修）：`Launcher.Color.OnPrimary` 不硬编码白；按背景色相对亮度选黑/白（浅种子时用深 on-color），写入 `ColorUtils` 并测试锁定。
- **选中容器三态前景**：`OnSecondaryContainer`、`.Hover`、`.Pressed` 分别与 `SecondaryContainer` 同状态配对。保留方案原前景；若该状态不足 4.5:1，先回退同方案 `OnSurface`，仍不足时选对比度更高的纯黑／白。背景继续按方案原前景预混 8%／16%，避免修复文字时改变状态层强度；每次主题／策略切换重建全部配对。
- **实现**：`ApplyAccentBrushes`（`Features/Settings/SettingsAppearanceViewModel.cs`）演进为 `ApplyScheme`（输入 seed/variant/theme/neutralStrategy → 覆盖 brush 组）；`ThemeColorExtractionService`（Octree）与新算法共存；`ColorUtils`（`Helpers/ColorUtils.cs`）承载归一化与 on-color 计算。非壁纸模式（系统/自定义/默认色）产出与现状一致或改善。
- **M3 落地记录（已实现并测试锁定）**：`ApplyScheme` 已替换 `ApplyAccentBrushes`；转换层 = `Helpers/MaterialColorMapper.cs`（ArgbColor↔Avalonia Color/Brush），scheme 生成 = `Services/MaterialSchemeGenerator.cs`（8 变体 × Spec2021 × Platform.Phone；`BuildRoleBrushes` 保留既有 accent 族覆盖子集并新增 Secondary/Tertiary/容器角色键）。**行为变更**：① `Launcher.Color.Info` 不再跟随 accent（业务色固定，见本节）；② accent 由“归一化种子”变为“scheme Primary（tone 40/80）”——非壁纸模式亦经统一管线，存在 M0 记录的小幅色相/彩度随动（±1 tone，ΔE≈15）；③ 主题模式切换时经 `ApplyTheme` 按实际明暗重放 scheme（`ActualThemeVariant`），System 模式随系统变体；④ 新角色键（Secondary/Tertiary/容器/Surface/Outline）在 App.axaml 中以占位值声明、运行时覆盖（§2.3）。
- **HCT 管线**：`Shirasagi0012.MaterialColorUtilities`（NuGet 直用）；ArgbColor↔Avalonia `Color` 映射封装；±1 tone 漂移以包内上游单测 + 参考值断言为护栏。

- （2026-11-07）`ThemeApplier` 原位更新已有方案画刷时同时同步 `Color` 与 `Opacity`，保留画刷实例；生成颜色中的 alpha 不再乘以静态占位画刷的旧透明度。`Primary.Soft` 的有效 alpha 为 `0x24`（约 14%），分段按钮、设置下拉和其他消费者共用该修正；`Primary.Border` 与 `FocusRing` 同样不保留过时的额外透明度。

### 2.5 字阶与字体（Q14）

| M3 角色 | 字号 | 字重 | 用途映射（现有） |
|---|---|---|---|
| Display | 22 | SemiBold | `heading`（主标题） |
| Headline | 19 / 18 | SemiBold | Basic 对话框标题（19）、分类/设置窗口标题（18） |
| Title | 17 / 16 | SemiBold | Panel 对话框标题（17）、面板分组标题（16） |
| Body | 15 / 14 / 13 | Normal/SemiBold | 进度标题（15）、正文/设置行标题（14）、说明（13） |
| Label | 12（两个兼容角色键） | Normal/SemiBold | 状态 chip、卸载步骤编号、设计画廊列标题 |

- 字体栈：`Noto Sans`（若系统可用/安装）→ `Segoe UI` → `Microsoft YaHei`（CJK 回退）→ `sans-serif`；等宽 `Consolas`。沿用 `Shell.FontFamily` 机制；**不内嵌字体文件**（Q14-a）。
- 保留现有 10 个 `Typography.FontSize.*` 角色键；2026-10-24 将 `Label.Sm` 从 11 提至 12（与 `Label.Md` 同值，以兼容既有引用），范围为 12–22。普通设置页以 13/14/18 三档为主，优先使用字重与间距建立层级。

### 2.6 尺寸与形状

- 间距：4/8/12/16/20/24/40；Thickness 补齐全档（现状仅 4/8/20 有 Thickness 变体）。
- 圆角按 Fluent 2 语义分两档：交互面（按钮、菜单行、选项行、字段、分段控件、Toast 动作）4 DIP（`Xs`）；卡片、弹层、对话框与面板 8 DIP（`Sm`/`Md`/`Lg`）；状态 chip、进度段与圆形徽章用 `Full` 整圆。`Radius.Sm`、`Md`、`Lg` 保持同值 8。
- 图标 16/18/20/22/24；控件高度 Setting32/Dialog32/Bottom48/Launch58/Field32/Chip32/Swatch28/DialogTitle56。

#### 描边、分隔线与像素对齐

- 生产界面的装饰描边和分隔线统一为 **1 DIP**，不得使用 **0.5 DIP**。输入边框为 **1 DIP**，键盘焦点环为 **2 DIP**。本规则适用于线宽、单边边框厚度和用作分隔线的元素宽度或高度。字距按字阶规范，透明度按状态层规范。
- 主界面完整描边复用 `Launcher.Border.Thickness.Default`，顶边分隔复用 `Launcher.Component.Dialog.Footer.BorderThickness`，向导和卸载的水平分隔线复用 `Launcher.Component.Wizard.Divider.Height`。独立崩溃窗口使用本地 `Crash.*` 令牌。
- 保留 Avalonia 默认的 `UseLayoutRounding`。DIP 是逻辑单位，1 DIP 在 150% 缩放时取整前对应 1.5 个物理像素。验收时检查静止状态下直边的边界坐标是否落在整数物理像素上，并复核实际截图。无头回归测试 `Visual_DecorativeBordersAndDividers_SnapToDevicePixels` 覆盖 100%、125%、150%、175%、200% 显示缩放。
- `UiStyleContractTests.ProductionMarkup_DoesNotUseHalfDipStrokesOrDividers` 检查生产 XAML 的描边和分隔线尺寸，包括直接赋值与资源引用。用普通元素绘制分隔线时，以含 `divider` 或 `separator` 的类名标识用途，便于检查其宽度和高度。

### 2.7 动效

- 本节旧有 M3 数值已由 [ADR-016](adr/ADR-016-Fluent动效层.md) 取代；视觉层继续使用 M3 语义，行为型动效改用 Windows Fluent 原则并映射为 Avalonia 资源。动效模式与家族的行为语义见 [CONTEXT.md「设计系统」](../../CONTEXT.md#设计系统)。
- 时长收敛为 83ms（即时反馈）/ 167ms（快速转换）/ 250ms（标准转换）；仅较大的连续空间变化允许 333ms。进入、退出和点到点移动分别使用 Windows 减速、加速与点到点语义曲线，不再使用 `ExponentialEaseOut/In`。
- 动效家族统一为即时反馈、内容切换、空间连续、临时表面与重要完成。常规控件不缩放、不弹跳；模态表面只移动外壳一次，内部内容不重复移动；自动轮播只淡化。向导步骤切换属空间连续家族的方向变体：以空间档 333ms 对半分做顺序两段换页（先退出加速淡出、后进入减速滑入 ±14px；入场前以起势帧消化布局、收尾留缓冲再结算，换页墙钟约 370ms），由后置代码编排并可中断（ADR-017）。
- `Full` 始终启用完整动效，`System` 跟随系统动画开关，`Reduced` 关闭空间移动、自动轮播和大面积淡化，仅允许临时表面保留最多 83ms 的纯透明度变化。**显式豁免（审计-0828）**：Fluent 基础模板内建的 indeterminate 进度条循环（`LoadingOverlay`、Toast 操作进度条与设置保存进度条）在 Reduced 下保留——“进行中”语义必需，且无空间位移或大面积淡化；如未来需要更强降级，应替换为静态“进行中”呈现而非直接隐藏。
- 动画始终以最新状态为准且不得排队。连续容器变化无法保持流畅或正确中断时，降级为交叉淡化或立即切换。
- 入场锚点一次性：叠层（`MotionVisibility`）与常驻内容（设置分类、Toast 卡片，`MotionEntrance`）的 `motion-enter` 只在打开/显现时挂上，并在对应时长（快速或标准档）后由后置代码摘除。锚点只在控件真正呈现后挂上：叠层首次打开时若让可见性与锚点同批次生效，样式动画会随首次布局直接跳到终态（实测 scrim 与表面都不动），因此 `MotionVisibility` 在首次呈现前先跑一遍布局再挂类；常驻内容的触发若发生在控件仍被隐藏时（启动时默认分类、叠层尚未打开），直接丢弃本次入场、等下一次触发——Avalonia 不为 `IsEffectivelyVisible` 发属性通知，无法在转为可见时补挂。摘除时同步复位 `Opacity` 与既有 `TranslateTransform` 位移，防止动画未渲染即被摘除后内容停留在不可见状态。此后运行期重开动效偏好只恢复后续动画能力，不得让已显示内容重播入场；Reduced 档打开的表面也不携带锚点。
- 进度条厚度：主界面 `Component.Progress.Bar.Height` = 8px、Loading = 4px、Toast = 3px（M3 LinearProgressIndicator 4dp；主进度条 8px 为产品加粗值，记录性偏差）。

## 3. 组件规范

### 3.1 对话框家族 v2（ADR-015）

`Controls/DialogSurface.axaml` 使用启动器实心对话框表面（`Launcher.Color.Dialog.Background`）、Fluent 的 1 DIP 描边、8 DIP 圆角、双层阴影与字阶。Basic 通过底栏动作退出，Panel 在标题旁提供有命令的关闭按钮。标题和正文共用表面；版本、目标与警示放在正文。设置和向导使用无标题的 Panel 承载各自的标题、正文和动作区。遮罩下方不再需要壁纸透出，材质统一由实心表面承担（[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)）。

共享标题、滚动正文与底栏的左右内边距均为 24 DIP。标题与正文之间留 12 DIP，正文底部留 8 DIP，底栏上下各留 16 DIP。动作组用 `WrapPanel.dialog-actions`，按钮间距 8 DIP、行距 4 DIP。尾部动作组整体靠右，空间不足时按原有动作顺序换行。辅助槽靠左，先给尾部操作留出空间，辅助按钮和状态说明使用剩余宽度。设置底栏复用相同的动作组样式。

确认框（重置设置、停止下载、放弃更改、退出向导、修复、切换下载源与下载中退出）统一上限为 440 DIP；内容型对话框（公告、错误、安装准备、卸载与更新说明）上限为 560 DIP。日志查看器、资源面板与调试面板为 720 DIP，设计画廊为 1000 DIP，三者属面板，不参与对话框宽度律。正文随语言和文字大小换行。

### 3.2 按钮与动作

- 每个动作组突出一项主要操作，使用 Fluent `accent` 类。次要操作使用默认按钮，图标关闭按钮使用 `TransparentButton`，行内链接使用 `HyperlinkButton`。
- 破坏性确认沿用安全动作初始焦点和显式确认流程。悬停、按下、焦点和禁用状态由 FluentAvalonia 绘制。
- `dialog-action` 提供共享的最小宽度。按钮保留本地化的操作名称，只有图标时用 `AutomationProperties.Name` 提供名称。

### 3.3 设置行与表单控件

- 用 `SettingRow` 组合标题、可选说明和操作。`FASettingsExpanderItem` 提供操作槽的自适应排布，标题和提示支持换行。
- 即时生效的布尔选项用 `ToggleSwitch`，保存或确认时提交的选项以及独立多选用 `CheckBox`。外观的即时预览仍按设置会话保存。提交语义见 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md)。
- 互斥选项和色板使用 `RadioButton`。滑块、输入框和取色器沿用原生模板。

### 3.4 下拉选择与菜单（2026-11-01 增补）

- 设置选项用 `SettingSelect` 承载原生 `ComboBox`。数据模板显示名称与可选说明，名称和说明均可换行。菜单、选中指示与开合交互使用 FluentAvalonia 默认模板。
- 游戏操作使用 `SplitButton` 与 `FAMenuFlyout`。菜单项保留图标、名称、命令和可用状态，卸载入口与日常管理命令之间使用分隔线。
- 复核方向键导航、Enter 提交、Escape 收起、禁用项和长列表滚动。`SelectionMenuThemeHeadlessTests` 及设置分区的无头测试覆盖实际控件。

### 3.5 新组件、状态矩阵与批次

- 新业务组件使用标准 Avalonia 控件和布局面板组合，交互状态沿用默认模板。布局数值复用语义资源。
- 以实际控件验证默认、悬停、按下、禁用、键盘聚焦与适用的校验状态。设计画廊用于参照，页面验收见[设计走查清单](design-walkthrough-checklist.md)。

### 3.6 例外

- 诊断和日志使用相同的默认模板和业务布局资源。
- 独立崩溃窗口加载 FluentAvalonia 主题，并使用就地定义的 `Crash.*` 布局令牌。

### 3.7 工具提示

- `ToolTip.Tip` 由默认主题绘制。图标操作使用本地化提示，省略显示的路径可以通过工具提示查看完整值。

## 4. 表面规范

### 4.1 主窗口、标题栏与任务表面

- 首页分为 64 DIP 纯图标游戏库侧栏、无底板 48 DIP 顶部工具行、左侧身份区与右下主操作。侧栏使用 Fluent 单选列表，条目为 44 DIP 图标按钮；游戏名、区服与任务进度通过悬停和键盘焦点工具提示显示，后台任务与失败以 16 DIP 角标提示。诊断与启动器设置用图标按钮放在栏底。
- 顶部工具行没有底板、边框或独立标题栏底色。当前游戏的官方字标或品牌文字移入内容区左上，作为游戏标题的主元素：官方字标高 80 DIP，品牌文字使用 42 DIP 强调字重。工具行右侧用带图标与本地化文字的 `TransparentButton` 呈现「游戏设置」与适用的「资源面板」，右侧预留 138 DIP 给平台窗口按钮。
- 标题下方的状态行按「状态面板」设置显示或隐藏，顺序为游戏会话、启动校验、网络、下载源、版本；状态行归左侧身份区，不参与主操作测量。
- 资讯区位于标题下方的左侧身份区，宽 476 DIP，底边与主操作底边对齐；整组资讯使用强档半透明表面（`Launcher.Color.Glass.Strong.Background`）与发丝描边，横幅高 220 DIP，与公告面板无缝拼接；公告行使用 `Launcher.Color.Content.Row` 独立圆角底色，分类使用 14 DIP 正文字阶，默认和最小窗口的常规首屏保留前三条公告，其余可在列表下方展开。官网与社交链接置于滚动区末尾，数量增加时可换行。
- 主操作浮在窗口右下角，距右/下 12 DIP；分体按钮最小宽度 180 DIP、高度 48 DIP，菜单向上展开。任务卡固定宽 382 DIP，右缘与按钮对齐，位于按钮上方 16 DIP，不占布局、不压缩壁纸和资讯。资讯与主操作以水平分离保证不互相遮挡。
- 展开资讯不会覆盖主操作；失败的后台任务以带本地化摘要的角标标在游戏图标上。

- 主窗口使用 FluentAvalonia `FAAppWindow` 的扩展标题栏：`TitleBar.ExtendsContentIntoTitleBar = true`、标题栏高度 48 DIP、背景透明；窗口按钮仍由 FluentAvalonia/平台绘制，贴在窗口右上角，应用不重复绘制最小化、最大化、关闭按钮。页头容器声明 `WindowDecorationProperties.ElementRole="TitleBar"`，空白处由系统按标题栏拖动，双击最大化并保留右键系统菜单；页头内按钮声明 `User`，点击与键盘焦点不因拖动判定丢失。壁纸铺在窗口内容背景，顶部工具行、资讯面板与主操作使用半透明表面；表面由 `Border` 直接以 `Launcher.Color.Glass.*` token 绘制，不使用系统 Acrylic/Mica 整窗材质，也不做窗口内采样模糊，因为壁纸层在窗口内部且始终铺满时原生材质不可见。左侧图标栏、资讯整组与浮动任务卡直接使用半透明 token；菜单、工具提示、对话框与设置页保持实底。macOS 和 Linux 使用平台标题栏。应用在窗口关闭事件中执行已保存的关闭策略。调试和启动器设置入口位于左栏底部。
- 单窗口 + 覆盖层：Z 序 = 主内容 → 设置 100 → 对话框 200 → 向导 500 → 向导退出确认 501 → Toast 1000。`ZIndex` 是兄弟节点排序，声明在 `MainWindow` / `MainWindowDialogsOverlay` 的使用点上；覆盖层的样式不设置 `ZIndex`。
- 功能分区（Shell / GameOperations / Settings / SetupWizard / Diagnostics / ResourcePanel）与“单窗口内选分类”模型不变；表现级布局自由（如设置页留白、卡片形态）。
- **统一操作分体按钮（2026-11-07，ADR-057）**：`GameOperationsSplitButton` 在所有安装状态共用。使用 FluentAvalonia 的 `SplitButton` 模板与 `FAMenuFlyout`，以资源和样式适配尺寸、分隔线和状态色；主区按当前状态提供安装、更新、修复、刷新、开始游戏或查看任务，菜单选择不会替换主操作。
  - 会话文字为标题下方状态行首项，显示启动中／运行中／已退出及启动失败；空闲时隐藏。状态详情设为「不显示」时隐藏整组文字。
  - 浮动主操作使用 48 DIP 最小高度、180 DIP 最小宽度，宽度随本地化文字扩展。分隔线、悬停、按下和键盘焦点由默认模板绘制。
  - 短操作忙碌时整个控件禁用；活动任务允许「查看进度」，文件操作菜单继续禁用。内部两按钮不单独占用 Tab 停靠点。
  - Enter / Space 执行主操作，Alt+↓ / F4 展开菜单，Escape 收起菜单并恢复焦点。两区保留各自本地化 tooltip 和自动化名称。
  - 管理菜单使用 `TopEdgeAlignedRight`，与分体按钮右缘对齐，向上展开并留 8 DIP 间距；屏幕边缘空间不足时沿用原生弹窗避让。保留检查更新、创建快捷方式、打开游戏文件夹、修复与卸载；卸载前额外分隔。所有状态共用稳定面板；安装路径准备进入 `GameTaskDialog`，执行与结果使用主操作上方的 `GameTaskCard`。
  - 自动化覆盖统一布局的分区点击、键盘与焦点、忙碌禁用，以及默认/最小窗口下菜单的右缘对齐与上方间距。亮色与深色中文黄金基线分别为 `launch-split-button` / `launch-split-button-dark-zh`，展开菜单基线为 `launch-split-menu`。
- **安装准备对话框（2026-11-08 修订）**：复用 `DialogSurface` 的 Panel 形态、滚动正文、固定动作区与动效；仅承接安装准备，执行与结果由非模态悬浮卡片承接。安装准备正文为路径标签、32 DIP 最小高度的路径栏与「更改」链接、下载量与所需空间（留白分组，无字符分隔符）、桌面快捷方式复选框；底部「定位游戏」「开始安装」并排居右，关闭仅保留标题栏入口。长路径单行省略，可悬停查看完整值。容量元数据允许换行，已知的空间不足原因以带图标的警示显示在容量行下方，并通过自动化帮助文本供辅助技术读取。安装准备关闭放弃草稿；执行阶段不打开模态进度框。行为归 [CONTEXT.md「游戏任务对话框」](../../CONTEXT.md#游戏任务对话框)。卸载保留其删除范围、残留信息和不可关闭执行态。
- **任务悬浮卡片（2026-11-19 修订）**：固定宽 382 DIP，右边缘与拆分主按钮对齐，位于按钮上方 16 DIP；不参与操作面板测量，不压缩壁纸和资讯区。固定紧凑布局，不提供折叠或展开详情。仅显示阶段、阶段百分比，下载时直接显示已下载大小／总大小和速度，检查与校验显示文件计数；不显示剩余时间、路径或逐文件列表。结果摘要最多两行并提供完整工具提示。暂停／继续、停止、日志、适用的重试与结果关闭直接位于标题旁，均具备本地化名称和工具提示。使用强档半透明表面（`Launcher.Color.Glass.Strong.Background`）、发丝描边、阴影、8 DIP 圆角和 §2.5 文字角色。主按钮名称按任务／状态动态变化，任务执行中点击返回卡片。
- **首页留白**：侧栏贯穿窗口左侧，图标距栏边 10 DIP、上下 8 DIP；游戏图标 30 DIP，按 8 DIP 圆角裁切方形官方位图；侧栏与内容区之间留 12 DIP。顶部工具行高 48 DIP，标题距工具行 12 DIP；内容区右侧与底部留白 12 DIP。资讯卡与标题左缘对齐，底边与主操作底边同为 12 DIP。横幅与资讯列表相接时接缝保留直角，组合外缘采用 8 DIP 圆角。
- **主操作**：浮动在右下角，不占整行；工作区不再保留横跨底部的操作栏。状态信息归标题下方左侧身份区。隐藏状态详情时只隐藏该行，主操作位置不变。
- 主操作结构不随安装或执行状态改变（ADR-057）；Q18 的历史材质对比结果仅作归档参考。

### 4.2 设置页

- **设置整窗页（ADR-070 第二批，2026-11-20）**：设置不再是 960×620 的居中浮层，而是标题栏以下、64 DIP 图标栏右侧的整窗页面。页面根网格以 `Launcher.Component.Settings.Page.Offset`（64,48,0,0）贴住工作区，表面为实心 `Launcher.Color.Dialog.Background` 加上缘发丝线；图标栏保持可见，齿轮钮带 `settings-open` 选中态，任务角标常驻可读。页头跨两列显示「启动器设置」或包含固定目标游戏名称的标题（56 DIP 最小高、24 DIP 横向内边距、下缘发丝线），关闭按钮在右侧。左栏是 216 DIP 分类导航（Fluent 层填充与右缘分隔线，选中项加粗），使用 Fluent 单选列表承载分类、保留字形与键盘焦点；切换设置范围后，分类选中态、标题和正文随该范围的上次选择恢复，列表项自身使用本地化分类名。右侧固定显示当前分类标题，下方正文独立滚动。页脚跨两列（64 DIP 最小高、上缘发丝线），左侧是保存状态，右侧是预览（适用时）、放弃更改和保存。
- **信息密度与几何（2026-10-24）**：
  - 设置页保留分类标题和有实际分类价值的组标题；移除复述分类内容的副标题，以及语言、关闭行为、启动后行为、记住窗口位置/大小、主题和壁纸适配方式的重复行说明。下载源、启动校验、网络、风险和恢复说明保留；卸载范围细节使用可换行 caption，避免分类副标题样式移除后丢失换行；未删除任何资源键或设置项。
  - 页头范围名称与关闭按钮垂直居中。分类标题另起一行并与正文左缘对齐（24 DIP 横向节奏与滚动区内边距同源），可随长译文或文字缩放换行。分类标题与正文间留 8 DIP，组前留 20 DIP，组标题与卡片之间留 8 DIP。
  - 行形态为**分组卡片 + 原生单选组**：组标题在卡外，`SettingRow` / `SettingSelect` 收纳进 `Border.settings-group-card`（`Card.Background` 实心底、发丝描边、4 DIP 圆角），卡内行间以 `settings-row-divider` 上缘发丝线分隔，条件行与它前方的分隔线共用同一可见性绑定，像素对齐由 `UseLayoutRounding` 契约锁定。`SettingRow` 采用 `FASettingsExpanderItem` 的内容区和操作槽，空白描述在接缝处归一为 null 折叠、不占一行。空间不足时操作槽换行，说明只在有内容时显示。正文滚动区承载长内容，保存与放弃更改按钮使用共享动作组。
  - 设置行分隔线和卡片描边遵循 [§2.6 描边、分隔线与像素对齐](#描边分隔线与像素对齐)。
  - 警示以图标、文字与语义背景共同表达，取消装饰左边线，保留全部原因与恢复操作。Markdown 引用的左边线有引用语义，使用独立组件令牌保留。
  - 更新弹窗最小尺寸为 480×320 DIP，最大宽度仍为 620 DIP，长内容沿用共享滚动区。正文加载中显示文字与不定进度条，失败时显示说明及「前往发布页」，操作按钮保持可用。图片独立显示加载与失败提示。
  - 启动器更新说明支持 Markdown 图片与可点击链接：图片按通知可用宽度等比缩小，链接使用 `Launcher.Text.Link` 并保留下划线。图片加载不阻塞正文和更新动作；生命周期与外部地址策略见 [领域契约](../../CONTEXT.md#资源面板与远程内容)。
  - 设置页头、正文和页脚共用对话框底色；分类导航经 Fluent 层填充与右缘分隔线区分，页头/页脚经上缘发丝线与正文分界。
  - Fluent 控件从主题获取强调色和交互状态，业务表面继续使用相应的语义画刷；设置下拉与折叠区的指针态经 FA 资源键覆盖接入应用档位（下拉为实心 Field 底、悬停不换底、`:error` 取应用错误档；折叠区头部静止面透明、悬停/按压描边取 Field.Border）。
- （2026-11-07，[ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md))所有 `SettingRow` 行标题统一 14 DIP 普通字重，组标题 14 DIP 强调字重与 `Text.Primary`，说明 13 DIP `Text.Secondary`；分类标题仍为 18 DIP 强调。正文顶部按分类说明用途：常规说明语言、窗口、显示与更新；游戏说明目录、启动校验、运行环境和维护；游戏启动与运行说明引导方式、运行器与启动后行为；游戏下载与资源说明下载源与适用资源入口；游戏背景说明专属覆盖与媒体；下载与网络说明代理、HTTP/2 与限速；外观说明主题、配色、背景媒体与动效的可撤销预览；默认背景说明全局默认规则；高级说明日志、网络兼容、数据目录和重置；关于说明版本、更新、帮助与法律信息。保存要求写入相关分类说明，不再重复显示统一保存提示。分类说明复用选项的本地化 Description，随导航、目标和语言切换更新。路径草稿与当前安装维护分组，维护区显示实际安装目录。常用按钮在正文全宽区域内换行，「卸载」单列一行。路径未保存时显示说明并禁用维护操作。

- **分类与保存反馈（2026-11-07）**：保留六分类、双列外壳、固定标题/关闭与底部操作区；分类切换将共享正文滚动位置归零，不改变草稿、外观折叠状态或导航焦点。底栏左侧跨分类显示未保存状态；保存时状态与保存按钮改为「正在保存…」，显示不定进度条，并沿用导航/关闭/编辑的禁用规则。失败后恢复未保存提示并可重试，成功或放弃后清除提示。状态文本可换行，通过 polite live region 暴露给辅助技术；按钮保持右侧位置。保存进度复用 8 DIP `Progress.Bar.Height` 与 220 DIP 设置控件宽度，不另建令牌家族。
- 页脚并排呈现可换行的保存状态和操作组，底栏的「取消」自 ADR-070 起改称「放弃更改」（关闭命令仍先经未保存确认，改动不会静默丢弃）。外观预览使用独立底栏；返回按钮获得焦点，退出预览时恢复原设置焦点。
- **任务分组（2026-11-16，[ADR-065](adr/ADR-065-单窗口多游戏管理.md)/[ADR-066](adr/ADR-066-设置编辑与背景配置按游戏范围隔离.md)）**：启动器设置为六分类（常规、外观、默认背景、下载与网络、高级、关于）；游戏设置为固定目标的四分类（安装与维护、启动与运行、下载与资源、游戏背景），标题带目标游戏名称。下载与网络按「下载：下载速度限制」→「网络：代理、HTTP/2」排列；游戏下载与资源按「下载源 → 适用资源入口」排列，下载源只列当前游戏声明支持的来源（蔚蓝档案为官方／Cafe，星塔旅人为官方）；高级按「诊断：日志级别、查看/导出日志」→「网络兼容：允许本机或内网地址」→「数据与恢复：数据目录、重置设置」排列。各组首行不画顶部分隔线，沿用组间留白和共享设置行。两个对话框共享一次编辑会话与唯一写入方；保存只提交当前范围，切换目标前先保存或放弃。文件任务进行期间，所属游戏的配置区禁用并说明原因，另一款游戏与全局偏好仍可编辑。背景专属覆盖整组启停，关闭时继承全局默认。
- **回归范围**：默认/最小窗口下的分类滚动复位与键盘焦点；保存等待、成功、失败重试、放弃与语言刷新；四语言下的 100%/150%/200% 底栏文字布局。新增黄金图 `settings-advanced-zh`、`settings-saving-dark-scaled`，并更新受影响的设置截图；不把无头文字放大等同于系统 DPI 或读屏器实测。

#### 外观设置分组与高级配色

- （2026-11-01）「当前背景」的说明随实际加载结果更新：自带和远程壁纸显示本地化来源名称，远程壁纸通过 tooltip 提供完整图片地址，自定义背景显示所选具体文件路径；远程加载回退到自带壁纸时同步显示自带来源名称。长路径省略显示并通过 tooltip 保留完整内容。
- 外观页按「主题与动效 → 主题配色 → 高级配色」排列，默认背景页单独承载背景来源与媒体选项；组标题保留强调字重、行标题使用普通字重；动态效果从常规移到外观，字段、默认值和应用时机不变。背景操作紧邻来源，图片与视频合并为「选择图片或视频」入口，使用同一文件选择器同时筛选两类格式；文件夹与清除入口独立保留，文件夹可随机选择图片或视频。缩略图按图片／视频类型展示，「当前背景」显示本次选中的具体文件路径，长路径省略显示并通过 tooltip 保留完整路径。路径与媒体类型随最新加载结果一起更新，不把随机结果写回文件夹设置。填充色仅在适应模式出现，音量按实际选中的文件类型显示，包含文件夹随机选中的视频。游戏背景页以「使用专属背景」整组开启覆盖，关闭时只读展示继承的全局默认；自定义图片、视频与文件夹只在覆盖开启后提供。
- （2026-11-20，[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)）主题、背景来源、壁纸适配、中性色策略与更新通道这组 2–3 项短标签互斥值改用**原生单选组**：`RadioButton.setting-radio` 带 `WrapPanel.setting-radio-group`（20 DIP 项距、4 DIP 行距、32 DIP 最小命中高），选中态由 FluentAvalonia 原生圆点与文字加粗表达，不再自绘分段胶囊；`Segmented.axaml`、`Launcher.Component.Segmented.*` 与 `.segment-*` 退场，资源面板 UID 来源同样回到原生单选。未选中段的反选不写回来源，双向接缝仍由 `SegmentOptionConverter` 承担；长标签或 ≥4 项的动态效果、取色算法、配色变体与主题色来源仍用下拉菜单。
- 同一门槛适用于其余设置页：常规页的关闭行为、状态面板与游戏下载与资源页的下载源（当前游戏声明支持两项时）使用原生单选组；游戏启动后行为、代理、下载限速、日志级别与游戏运行器因标签较长或选项 ≥4 项继续用下拉。
- 主题色来源下拉的每一项前置一枚圆点，预演「该来源当前解析出的主色」；触发器与菜单共用同一 `ItemTemplate`，行内不再单放与选项无关的色块。圆点随亮暗主题、壁纸取色、系统强调色与自定义色即时刷新，包括尚未选中的来源。打开设置时补齐当前壁纸色板，壁纸或算法变化也刷新未选中的壁纸预览；这些后台预览不改变当前主题色来源、不标记草稿为已修改，也不把未选中来源应用到主窗口。系统事件直接使用事件携带的强调色与明暗值，避免资源字典更新时间差导致预览滞后。
- 自定义背景的清除动作收为图标按钮，保留 tooltip 与辅助名称；两个选择动作仍是带文字的 `flat-action`，避免清除与选择争夺视觉重量。
- 取色算法、配色变体、中性色策略及种子跟随提示归入默认收起的「高级配色」。仍保留全部既有主题色来源与枚举值，算法继续只作用于壁纸模式；色板刷新与自定义取色器保持原绑定。
- 视频音量行在滑块与百分比旁提供「静音」复选框，仅自定义视频时显示。静音是独立的草稿设置，静音期间保留并允许调整音量，取消静音后恢复；保存和放弃改动沿用原流程，旧配置缺少静音字段时按 false 读取，默认音量仍为 0。
- 音量百分比按当前字号的 `100%` 测量值预留固定显示槽，并右对齐；数值从 0% 到 100% 切换不改变音量行宽度或移动静音选项，文字放大时预留槽随字体测量增大。
- 不提供独立的「预览效果」区域；外观变更继续即时应用到主窗口，背景组保留当前背景缩略图，不新增独立播放控件或逐帧取色。图片缩略图使用当前图片；视频缩略图异步加载，MP4 优先使用内嵌封面，封面缺失或不可用时提取第一帧；图像叠加 3 DIP 模糊与暗色遮罩，中央显示清晰的视频图标。模糊半径使用 `Launcher.Component.Settings.Appearance.Thumbnail.VideoBlurRadius`，加载失败保留静态回退图及视频标记；视频封面或首帧的原始图像同时供壁纸主题取色使用，等待加载期间保留旧色板；两者均不可用时使用内置图取色。仅切换视频来源或文件内容指纹变化时自动重新取色，音量、静音、适配和窗口尺寸变化不重取，不逐帧更新；手动刷新色板或切换取色算法沿用缓存图像。
- 高级配色原生 `Expander` 的展开状态只属于设置会话：切换分类保留，重新打开设置时复位，不进入设置草稿或 `settings.json`，不启用保存按钮。程序式收起时，若焦点在隐藏的内容中，将焦点归还对应标题。标题模板与箭头状态来自 FluentAvalonia；头部静止面保持透明，悬停/按压描边经 FA 资源键覆盖取 `Field.Border`（2026-11-20，ADR-070），其余描边与折叠箭头底保持安静。
- 背景加载失败状态由现有背景加载结果给出，警示与文件选择入口始终放在折叠区之外。过期加载不更新状态；成功加载或切回内置来源后清除警示，视频失败保持原有不重复重试语义。
- 设置外壳、分类 code、固定标题和关闭按钮、正文滚动、放弃更改／保存及未保存确认均保留。回归覆盖展开不致脏、折叠不丢值与焦点回归、背景缩略图和加载失败恢复；四语言／文字放大矩阵包含展开状态，截图基线为 `settings-appearance` 和 `settings-appearance-advanced-dark`。

### 4.3 对话框、Toast 与更新说明

- 对话框族已由 [ADR-015](adr/ADR-015-对话框家族v2统一框架.md) 统一为 `DialogSurface`；动作顺序和默认按钮见 [ADR-040](adr/ADR-040-对话框动作按Fluent对齐顺序与默认按钮.md)。按钮、禁用态与键盘焦点由 FluentAvalonia 主题绘制。组件布局见 §3.1。对话框外壳使用不透明的 `Launcher.Color.Dialog.Background` 与双层阴影，不再透出壁纸；内嵌业务卡片使用 `Launcher.Color.Card.Background`，动作带 `PART_FooterBand` 以 `Launcher.Color.Dialog.Section.Divider` 上缘发丝线与正文分界（2026-11-20，[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)）。
- **Toast**：392 DIP 宽的通知卡右对齐，顶部留出游戏页头空间。卡片固定使用资讯级的中性实心表面，严重级别不再铺满整卡换色，只由左缘 4 DIP 语义条与本地化的严重级别标题表达：语义条取 `Launcher.Color.Info`／`Danger`／`Warning`／`Success`，`FAInfoBar` 以 `Severity="Informational"` 与 `IsIconVisible="False"` 提供标题、消息、关闭按钮与 Live 区域，外壳用中性 `DividerStrokeColorDefaultBrush` 描 1 DIP 边（2026-11-21，[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md) 修订）。资讯级底色仍由宿主覆盖为实心注意力底色（浅色 `#F7F7F7`、深色 `#2E2E2E`），避免默认通知在壁纸上呈透明卡。沿用模板覆盖的禁则：通知卡不通过 `/template/` 选择器改模板部件，也不为阴影使用字符串资源（Avalonia 12.1.1 无法在 Setter 中解析），因此通知卡只用描边表达高度。主要动作使用强调样式，次要动作使用透明样式；动作组在消息下方靠右，空间不足可换行。仅执行动作时显示不定进度条。悬停或焦点在通知内时暂停消失，恢复条件见 [CONTEXT.md「Toast 消失时长」](../../CONTEXT.md#设计系统)；栈上限 4 条与同 key 合并计数见 [CONTEXT.md「Toast 栈上限与同 key 合并」](../../CONTEXT.md#设计系统)。
- **更新说明**：Markdown 图片与链接的加载、缓存、协议白名单和释放行为见 [领域契约「启动器更新说明」](../../CONTEXT.md#资源面板与远程内容)；视觉使用 `Launcher.Text.Link` 并保留下划线，图片按通知可用宽度等比缩小，不阻塞正文和更新动作。

### 4.4 首次设置向导

向导最大尺寸为 920×560 DIP。固定页头包含向导标题、步骤计数、进度条和适用的跳过操作，底栏保留上一步与下一步／完成。协议更新使用单步模式，隐藏进度条。

- 当前步骤采用一列，最多 680 DIP 宽。先是标题与说明，再是语言、选项、阅读器或复核内容。长步骤使用共享正文滚动区，操作区一直可见。
- 协议版本和语言说明与步骤标题相邻。正文和下方的同意复选框共用最大高度为 260 DIP 的阅读面板。滚动正文到底后才能勾选。放大文字后，外层可以滚动到阅读面板，向导底栏保持固定。文档字体沿用 `Launcher.Typography.FontFamily.Document`。
- 换步时仍按 [ADR-017](adr/ADR-017-设置向导动效落地与M3界面细化.md) 在过渡中点更新进度及内容，正文复位。[ADR-059](adr/ADR-059-设置向导统一舞台与双栏解剖.md) 记录先前的双栏布局，[ADR-061](adr/ADR-061-设置向导新增欢迎首步与配置概览.md) 记录欢迎步骤。亮暗截图覆盖欢迎页与协议阅读前后。

### 4.5 资源面板

资源面板先显示操作反馈和 UID 设置，再显示常驻 UID 说明与三项资源偏好。UID 来源在标签下使用 Fluent 单选按钮，宽度不足可换行。每条资源把状态放在名称旁，版本放在下方，右侧保留提交式复选框；相同版本共用一条摘要。底部提供刷新、未保存状态和「保存设置」。关闭、刷新与更换 UID 前的草稿保护依据 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md)。

### 4.6 关于分区

依据 [ADR-018](adr/ADR-018-关于分区重设计方向.md) 及 2026-11-21 修订：身份卡为「应用图标 + 产品名 + 版本与构建时间 caption + 副标题 + 单一主操作」。图标取打包的 `Assets/app-icon.png`（256 px 源），48 DIP 显示、8 DIP 圆角裁切，不描边不投影。检查更新是身份卡内唯一动作，保留 `primary-action` 强调，40 DIP 最小高度、8 DIP 圆角、20 DIP 水平内边距；官方网站与帮助文档不再与它并排，改为下沉到入口列表。身份卡和构建信息列表使用 1 DIP 外描边，与内容一起按物理像素取整。关于分区沿用 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md) 的信息层级，当前顺序为：身份卡 → 入口列表（官方网站、帮助文档、仓库、Release 仓库、问题反馈）→ 贡献者 → 特别感谢 → 默认折叠的「构建与运行信息」→ 法律与许可 → 免责声明。构建与运行信息为五项等宽值行（提交、构建配置、运行时、Avalonia、平台），每行右侧提供单值复制，折叠区底部提供「复制全部」；复制结果经应用内 Toast 反馈，空值行不写入剪贴板也不参与合并文本。法律与许可把版权、隐私政策、背景原作版权与「开源许可」「第三方声明」收在同一张 1 DIP 描边卡片内；后两项以行式入口打开应用内只读文档对话框，`LICENSE` 与 `THIRD-PARTY-NOTICES.md` 因此随表现层资源嵌入，与 `TERMS.md` / `PRIVACY.md` 同一条路径。免责声明作为分区末尾的 12 号次级文本。技术信息折叠复用设置 Expander 表面。贡献者与特别感谢共用双行链接模板，左侧为 40 DIP 圆形图标占位符，右侧依次显示名称与可换行的本地化简介。相邻条目以细线分隔，悬停和按下时的背景铺满整行并贴齐分割线。键盘焦点描边不改变内容布局。条目最小高度为 64 DIP，整行打开贡献者主页、项目仓库、蔚蓝咖啡厅官网或应用图标的来源说明。贡献者简介概述其在项目中的职责或主要贡献。社区致谢作为特别感谢列表下方的普通文本。

### 4.7 崩溃窗口

崩溃窗口（[ADR-019](adr/ADR-019-不可恢复崩溃两级兜底.md) / [ADR-020](adr/ADR-020-崩溃窗口独立Fluent设计系统.md)）是独立于主壳的终端表面，**不参与主窗口 MD3 令牌体系**——中性冷灰蓝表面（不随动态色）+ 4/8 圆角 + 12/14/20 字阶 + 危险红/链接蓝双语义色；700 固定宽、高度随内容、不可缩放；默认折叠技术详情，动作带仅「打开日志目录 / 复制详情 / 退出启动器」。同进程（tier-1）与隔离报告进程（tier-2）共用同一窗口实现。操作高度、正文与键盘焦点规则同 §3.2，但不引入 `Launcher.*` 或正常 DI 依赖。

### 4.8 诊断与日志

诊断面板与日志查看器列表控件维持 Fluent 基础模板，仅 token 兼容；工具栏和表单控件复用共享视觉。日志导出对话框使用 `setting-checkbox`，四行均带本地化 automation name，由 `Baselines/log-export.png` 与渲染对比度断言守护；导出前置警示使用共享 `dialog-alert warning` 语义底色（`Warning.Background`），不以中性卡片承载。

## 5. 主题与壁纸

- `RequestedThemeVariant="Default"` 跟随系统不变；主题模式（System/Light/Dark）持久化机制不变。
- 背景采用全局默认规则加整组游戏覆盖。启动器设置的「默认背景」定义默认来源、适配、填充、视频音量与静音；游戏设置的「游戏背景」以「使用专属背景」整组覆盖，关闭时继承默认。自定义图片、视频与文件夹只在专属覆盖中提供；关闭覆盖保留已选值，再开启恢复。自定义图片与视频共用文件选择入口，并提供仅选中视频时显示的音量滑块，默认静音；使用既有设置行、令牌和本地化无障碍名称。视频置于真实内容/覆盖层下方，首帧前和失败时保留该游戏内置图，内置图不可用再回退共享通用背景；减少动态效果时暂停并显示首帧静帧，最小化和托盘隐藏时暂停。主题与配色策略仍全局共享，取色跟随当前有效背景。
- 旧版全局自定义媒体迁移为各游戏专属值、共用原文件路径，覆盖开关保持关闭；迁移前保留原文档备份。
- 壁纸取色：现有“壁纸模式色板 + 刷新”交互保留并整合进新管线（种子 = 刷新时的算法输出；色板刷新 = 重新取种子 → 重新生成 scheme）。
- **高对比度变体不支持**：仓库没有 `HighContrast` 的 `ThemeDictionaries`，主题解析只在 Light/Dark 之间切换；Windows 高对比度模式下应用继续使用自身 token 与 AA 契约（§7）保证的可读性，但不宣称满足系统高对比度主题的专项对比度要求。登记为接受风险 `AUD-UI-001`（[审计台账](../../CODEBASE_AUDIT.md)）。

## 6. 设置项（动态配色）

外观组「主题颜色」新增 3 个设置项（每个 1 行，走 `SettingOption` 枚举 + 4 语言 resx + `AutomationProperties.Name`）：

| 设置项 | 选项 | 默认 | 说明 |
|---|---|---|---|
| 取色算法 | Octree（现状保留）/ **M3（Celebi+Score）** / Wu / Wsmeans | M3 | 决定壁纸种子来源 |
| 配色变体 | *8 变体（全量，包内现成）* | TonalSpot | 决定种子→色阶风格 |
| 中性色 | **品牌蓝调（固定）** / 种子跟随 | 品牌蓝调 | surface/outline 是否随种子染色 |

- 持久化：`LauncherSettings` 新增字段 + 规范化（默认值填充），向后兼容旧 `settings.json`；遵循 `SettingsEditor` 事务性保存。
- **M3 状态**：三字段已持久化（`themeColorExtractionAlgorithm`/`themeColorVariant`/`neutralColorStrategy`，默认 CelebiScore/TonalSpot/BrandBlue）并接入 `ApplyScheme` 管线（variant/strategy 已生效）；三个设置行 UI 已落地（P2，`SettingSelect`，次序 = 取色算法 → 配色变体 → 中性色）；中性色「种子跟随」选中时显示条件 caption 提示（ADR-010，4 语言）。壁纸取色算法默认 = **M3（Celebi+Score）**，Octree/Wu/Wsmeans 可选（Octree 保留为降级/对照）；既有壁纸模式用户默认算法切换后 accent 轻微变化——属视觉漂移，记录为可接受（与 P3 改版同期，已记录于本节）。**变更即预览**：三个设置行（模式/变体/中性）变更即时重绘主界面（ADR-009），保存时持久化。

## 7. 无障碍

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

  `Danger.Pressed` 保留 `#C9353A`（白标签 5.18；其填充态消费由标签对覆盖，不做表面图标对）。全部为同色相单阶加深/提亮，未改视觉意图；UI ≥3:1 额外覆盖 Danger×Card/Dialog。Toast 卡片整面使用 FluentAvalonia 的 InfoBar 语义资源（WinUI 派生），对比度由控件库契约承担，不进应用 token 对比表（[ADR-070](adr/ADR-070-对话框设置与通知统一为实心Fluent表面.md)）。
- **Soft 底色配对（2026-11-12）**：`Success`/`Danger` 直接压在同色 `*.Soft` 底上只有 4.32/4.03:1；资源面板状态 chip 的文字与图标改用配对 token——`Color.Success.OnSoft`（Light `#126B33` 5.69 / Dark `#4ADE80` 8.09）与 `Color.Danger.OnSoft`（Light `#C42B33` 4.94 / Dark `#E4676F` 4.69），由 `DesignTokenContrastTests` 的文本对锁定。新增 soft 底色一律同时定义对应 `OnSoft` 前景。
- **半透明表面语义前景（2026-11-18）**：表面 alpha 让极端壁纸参与底色，根档 `Danger` 在 Dark + 浅壁纸只有 2.64:1，`Info` 在 Light + 深壁纸只有 2.82:1。半透明表面宿主的危险文字/图标与提示图标改用 `Color.Danger.OnGlass`（Light `#A3222A` / Dark `#F58A91`）与 `Color.Info.OnGlass`（Light `#1A56B8` / Dark `#6EA8FF`）；`Success`/`Warning` 根档在两种极端下已满足 3:1，继续复用。`DesignTokenContrastTests` 以亮/暗两种壁纸极端锁定危险文字 4.5:1、图标 3:1；新增语义色若用于半透明表面须同时验证两种极端。
- 游戏品牌文字、社交链接与底栏状态使用 Fluent 主题表面和文字颜色；横幅图上的翻页控件保留独立底色。
- **键盘**：全表面 Tab 序、可见焦点环（`Button:focus-visible` 等既有规则随重命名迁移）、覆盖层焦点陷阱（`OverlayFocusBehavior` 保留）。`wizard-option`（ADR-017）与设置导航/更新文件列表同样接入 token 焦点环（审计-0828 补全）；**动态焦点环单独验证**：`FocusRing` 使用不透明强调色，在对话框/输入表面不足 3:1 时回退到同一 M3 方案的 `OnSurface`；实心主操作/启动按钮的内部焦点描边使用 `OnPrimary`，危险按钮使用 `OnError`，避免描边融入同色填充。选中下拉项使用对应状态的 `OnSecondaryContainer` 前景绘制焦点；独立 2 DIP 描边向内缩 2 DIP，仅邻接容器填充，避免强迫单一颜色同时对比饱和容器与中性弹窗。设置导航保留 Fluent 模板焦点绘制，验收检查实际模板而非只读取控件根画刷。资源面板的来源分段控件以独立焦点层（4 DIP 圆角）绘制，不参与内容测量；四语言、亮暗主题及 100%／150%／200% 文字下取焦不改变段宽、相邻位置或选择值。`MaterialSchemeGeneratorTests` 覆盖 8 变体、7 种种子、双主题、双中性策略及状态背景；壁纸与实际键盘路径仍须人工走查。**ESC 语义**：ESC 始终作用于模态栈顶层，焦点在搜索框等可编辑控件内也不改变路由；需要保留未提交改动的表面把确认接在关闭命令上（设置页脏态），既不吞 ESC 也不静默丢弃。
- **动效**：保留 System 默认值、完整动效与降动效语义，以及可中断的向导步骤过渡。
- **内容自适应**：共享设置行按操作实际宽度为说明预留空间，不足时改为上下排列，保持控件实例/焦点/选中值；空说明折叠，提示文本为空或被 `IsHintVisible` 关闭时同样折叠（设置提示无需再显式开启）。对话框文本按钮使用 `MinHeight`，设置操作组、统一操作面板、安装准备对话框和任务悬浮卡片的元数据可换行。无头测试覆盖四语言、1300×754 / 1024×640、亮暗主题与 100%/125%/150% 文字放大。测试直接放大控件文字，不等价于 OS 文字大小偏好已自动接线，也不覆盖物理 DPI 的全部渲染差异。

## 8. 设计画廊（Q11）

Debug 构建可从调试面板打开设计画廊。上方展示默认、强调、透明和禁用按钮，下方按家族列出当前主题下可解析的 `Launcher.*` 资源。打开后在实际使用这些控件的页面核对键盘焦点、选择状态和窄窗布局。画廊文案遵循四语言本地化契约。

## 9. 验收与流程（Q10）

- **每阶段门禁**：`UiStyleContractTests` 全绿（含新契约）+ 新增测试全绿 + 编译零警告 + `scripts/Test-LocalizationContract.ps1`（涉及新字符串时）+ `.\verify.ps1`；PR 附截图（可见 UI 变更）。
- **契约测试扩展点**：token 存在性/旧键清零、禁裸色值与 `{StaticResource}` 规则、AA 对比度、覆盖层 Z 序（既有）。2026-11-16 起裸视觉值扫描覆盖样式文件 `ControlTemplate` 部件上的直接属性（此前仅 Setter），圆角层级扫描覆盖 `Views/Styles/`，并新增禁标点字形分隔符契约（`ProductionMarkup_DoesNotUseGlyphSeparators`）。
- **headless 黄金截图**：已接线（`UseHeadlessDrawing=false` + `UseSkia()` + `RenderTargetBitmap`），27 个基线（7 类基础表面、5 个卸载状态、2 个缩放设置面、3 个就绪态分体按钮/菜单、更新/错误/设置向导 3 个对话框表面、3 个外观设置状态和 4 个下拉列表表面（亮／暗、Fidelity 焦点与悬停））平铺存储于 `tests/Cafe.Launcher.HeadlessTests/Baselines/`，阈值 diff（每通道容差 8/255、失配 ≤1%）、`CAFE_GOLDEN_UPDATE=1` 重新生成；字体稳定性：常规基线固定英文/降动效/Segoe UI；扩展基线显式固定语言、主题与文字倍率，CI（windows-latest）字体集合风险已在 README 记录，漂移时优先重生成基线。P3 视需要扩大。
- **走查清单**：已落地于 [`design-walkthrough-checklist.md`](./design-walkthrough-checklist.md)（状态矩阵 + §7 豁免区逐项 + P3 表面逐项），P3 每表面完成时人工复核；随本规范共同维护。

## 10. 有意搁置（Don't-do 清单）

- OS 独立文字大小偏好的自动接线与全应用文字缩放矩阵；共享设置行和文本按钮的自适应及设置页 125–150% 文字放大验证已落地（§7），不再整体搁置。
- Windows 高对比度主题变体（§5 已记录边界，接受风险 `AUD-UI-001`）。
- 诊断/日志的视觉重设计（仅 token 兼容；资源面板已由 [ADR-041](adr/ADR-041-资源面板MD3结构级重设计.md) 完成）。
- 独立“画廊预览窗口”（Q11-c 叠加项，暂不启用）。
- 对外部设计工具（Figma 等）的同步链路。
- 若 spike 判定 NuGet 包不可用 → **回退方案**：vendor `Shirasagi0012` 裁剪核心（Apache-2.0，砍 quantize/Score，HCT+palette+scheme+dynamic ≈4–5k 行，2–4 人日），已调研备档；同谱系 `MaterialColorUtilities`（albi005）源码仅作交叉参考，不进入产品依赖（其 API 为 2023 旧世代，差异量化表见 M0 产物）。

## 11. 实施现状

规格与代码的当前对应关系；实现细节见各节与 ADR。

| 范围 | 状态 | 依据 |
| --- | --- | --- |
| 底座 | FluentAvalonia 默认模板、M3 动态色、黄金截图、Debug 设计画廊 | ADR-068、§7–§9 |
| 设置页 | ADR-013 布局；ADR-056 提交式选项、维护目标、字重层级与折叠技术信息 | §4.2、ADR-013、ADR-056 |
| 对话框 / Toast / 向导 | `DialogSurface` 组合标题、正文与动作，`FAInfoBar` 显示通知，向导用标准面板组织步骤 | §3.1、§4.3、§4.4 |
| 关于分区 | ADR-018 重设计；ADR-056 调整信息顺序与折叠技术信息 | §4.6、ADR-018、ADR-056 |
| 崩溃窗口 | FluentAvalonia 默认控件与本地 `Crash.*` 布局资源，同进程与隔离报告进程共用 | §4.7、ADR-068 |
| 资源面板 | ADR-041 MD3 结构级重设计；ADR-056 提交式选择与草稿保护 | §4.5、ADR-041、ADR-056 |
| 就绪态启动按钮 | 已合并为 SplitButton | §4.1 |
| 跨功能共享组件 | 按钮、字段、下拉与菜单由默认主题绘制，设置与对话框共用动作组布局 | ADR-068、§3.1 |
| 仍开放 | 底栏形态（Q18，ADR-001 仲裁标准保留）；主壳首页已由 ADR-062 落地壁纸首页 | §4.1 |

## 附录 A. 决策记录

Q1–Q24 表保留最初决策脉络；后续变更以正文修订及 [ADR 索引](adr/README.md) 为准。

| Q | 决策 | 影响 |
|---|---|---|
| 设置-1107 | 2026-11-07 设置页局部改进：分类切换回到顶部，固定底栏显示跨分类草稿与保存状态，下载/网络及诊断/数据恢复分组；六分类外壳与事务保持现状 | §4.2；行为契约见 CONTEXT「设置与操作」 |
| 外观-1101 | 2026-11-01 用户确认外观页分组列表视觉稿：主题与动效 → 背景 → 主题配色；高级配色按需展开；独立效果预览随后按用户要求移除，选择与保存沿用既有草稿事务 | §4.2；其余设置分类和主界面布局保持现状 |
| 组件-1101 | 2026-11-01 用户确认跨功能统一对话框、按钮、字段与状态：共享主题承载几何；次要操作统一为中性实底；标准 40 DIP、控件圆角 8 DIP、对话框圆角 16 DIP；主界面布局和业务保持现状 | §3.2–§3.4、[ADR-050](adr/ADR-050-跨功能共享组件视觉基准.md)；替代此前各功能独立的按钮外观和尺寸记录 |
| 启动-1101 | 2026-11-01 用户确认将就绪态「开始游戏」与游戏管理入口合并为原生 SplitButton：主区直接启动，右侧省略号向上展开管理菜单；统一主色与外轮廓、分区反馈，卸载单独分隔 | 仅调整就绪态操作入口；普通与仅按钮布局共用 `GameOperationsSplitButton`，安装与进度态沿用现有布局；细节见 §4.1 |
| 优化-1024 | 2026-10-24 用户确认保留 Material 令牌与动态色管线；精简设置页重复说明与分类副标题，空说明不占位；设置行根据实际操作宽度换行，操作组可换行；装饰线 0.5 DIP，交互边框 1 DIP、焦点环 2 DIP；Label.Sm 最小 12；减弱对话框阴影，警示保留图标/文字/背景并移除额外左边线 | 本次修订取代 ADR-005 的 11px 下限、ADR-013 的 1px 装饰线及后续相关历史视觉值；不替换 Material 设计系统，不改变主题设置、命令、持久化与模态行为 |
| 修复-1102 | 2026-11-02 设计审查修复：选中容器三态分别校正文字前景；下拉项焦点使用对应 on-container 状态色并内缩 2 DIP；资源来源分段控件用独立胶囊焦点层保持测量稳定 | §2.4、§7；维持既有容器底色与 8%／16% 状态层，补齐动态矩阵和实际模板回归 |
| 对齐-1113 | 2026-11-13 装饰描边和分隔线统一为 1 DIP，启用 Avalonia 默认布局取整 | 取代优化-1024 的 0.5 DIP 装饰线；无头测试覆盖 100%、125%、150%、175%、200% 显示缩放 |
| 全量-1114 | 2026-11-14 全应用统一 Fluent 2：全局 FluentAvaloniaTheme，动态色经资源桥接进入模板；交互控件 32 DIP、交互面圆角 4 DIP、卡片/弹层/对话框 8 DIP；导航/菜单/向导选项选中态改用中性 SecondaryContainer，设置下拉与分段保留 Primary.Soft wash | §1、§2、§3、§4；见 [ADR-063](adr/ADR-063-全应用统一Fluent2视觉.md)，更新 ADR-050 的尺寸与圆角数值 |
| 走查-1114 | 2026-11-14 Fluent 2 全应用走查：日志导出前置警示改用共享 `dialog-alert warning` 语义底色；分段控件圆角确认按 Fluent 交互面取 4 DIP，设置导航保留 FluentAvalonia 列表项左侧选中指示条；规范、ADR 与检查表同实现对齐 | §2、§4.2；仅日志导出基线 `log-export.png` 定向再生 |
| 图标-1115 | 2026-11-15 图标语言统一为 Fluent 符号：`FluentGlyphs` 成为唯一字形源，视图与视图模型共用常量；移除 `Material.Icons.Avalonia` 与关于页致谢行 | §2、§3.1–§3.7；见 [ADR-064](adr/ADR-064-图标语言统一为Fluent符号.md)，四个受影响的黄金基线定向再生 |
| 走查-1116 | 2026-11-16 Fluent 走查补漏：安装准备容量元数据移除 `\|` 字符分隔符、改 12 DIP 留白分组；TabItem 选中指示条高度与圆角 token 化（`Component.Tabs.Indicator.Height` + `Radius.Full`）；契约裸值/圆角扫描域扩到样式文件模板部件，新增禁字符分隔符契约 | §1、§4.1、§9；安装准备亮/暗基线定向再生 |
| Q1 | 动因 = 系统化 + 视觉现代化 + 一致性 | 体系先行，观感随后 |
| Q2 | 分阶段：P1 token 体系 → P2 组件 → P3 表面 | 见 §9 |
| Q3 | 范围 = 全部表面分批：第一批主壳/设置/对话框/Toast/向导；第二批诊断/日志**仅 token 兼容**；资源面板后由 [ADR-041](adr/ADR-041-资源面板MD3结构级重设计.md) 补做结构级重设计 | 排障界面不做视觉重设计 |
| Q4 | 交付 = 代码 + 仓库内人读规范（本文件）；无 Figma 工作流 | 活文档随 PR 维护 |
| Q5 | **M3/Material You 为规范骨架**；FluentTheme 底座；不引入 Material.Avalonia | 本节全部后续分支 |
| Q9 | WCAG AA 对比度自动化 + 键盘/焦点/覆盖层审计（硬契约） | §7 |
| Q10 | 验收 = 契约测试扩展 + 文档走查清单 + headless 黄金截图（基建从零接，P1 小集） | §9 |
| Q11 | Dev 模式"设计画廊"页：token 表 + 组件状态矩阵 + 原型对比区（支撑 Q18 仲裁） | §8 |
| Q12 | 表现级布局重构允许；单窗口 + 覆盖层架构、Z 序（100/200/500/1000）、功能分区不变 | §4 |
| Q13 | 动态色 = **accent + container 双色阶**；表面中性（随主题模式）；**on-color 按亮度计算** | §2.4 |
| Q14 | 系统字体 + M3 字阶角色映射；字体栈优先系统可用的 Noto Sans | §2.5 |
| Q15 | **class + ControlTheme 混合**：既有 class 选择器一律不动；新组件走 ControlTheme + 新命名 | §3 |
| Q16/Q20 | **点分层命名** + **一次性全量重命名**（脚本+映射表，不留别名层） | §2.1 |
| Q17 | **NuGet 直用** `Shirasagi0012.MaterialColorUtilities`（core + Avalonia 集成包，net8/net10，Apache-2.0）；先 spike 验证；vendor 裁剪核心为留档回退 | §2.4 |
| Q18 | 底栏形态延后：画廊双原型（浮动玻璃胶囊 vs M3 贴边）仲裁；**倾向浮动胶囊**；仲裁结论已撤销，形态重新开放 | §4.1 |
| Q19 | AA 契约范围 = 面板内全指标 + 交互元素；**over-wallpaper 豁免 + 最小 scrim（token 化）** | §7 |
| Q21 | P2 组件 = A→B→C；诊断/日志列表维持 Fluent 基础模板 | §3 |
| Q22 | P3 表面顺序 = 主壳 → 设置 → 对话框+Toast → 首次向导 → 诊断/日志/资源面板 | §9 |
| Q23 | 中性色策略**可切换**：默认品牌蓝调（固定），可选种子跟随（tone 固定、只漂色相） | §2.4 |
| Q24 | 新增设置项（外观组「主题颜色」）：**取色算法**（octree 保留 / M3 Celebi+Score 默认 / Wu·Wsmeans 可选）+ **配色变体**（8 变体，默认 TonalSpot）+ **中性色策略** | §6 |
| 审计-0828 | 2026-08-28 设计系统一致性核查修复：向导 filled 型补 `:disabled` 组合、carousel 箭头 hover/pressed 改 chrome 态层、`wizard-option` 补 token 焦点环、DialogSurface 表面名 = 标题（无标题外壳显式给名 + Toast 宿主 live region）、对比度契约 +12 对并显式豁免 FocusRing/SecondaryContainer 族、时长双源同步测试、清理死 token（`Easing.Linear`/`IconButton.Compact/.Dot`，RowPanel 面板迁入 App.axaml） | 状态矩阵闭环；静态契约与运行时 M3 配对的分工显式化 |
| ADR-018 | 2026-08-30 关于分区重设计（变体 A：身份 Hero + 分组行）：Hero（图标/产品名/副标题/版本+构建时间双徽章）→ 常规操作（检查更新唯一 compact filled + 3 outlined）→ 版本信息 key-value 行（7 项，等宽值，hairline）→ 法律信息不变；已落地并同步契约测试 | 设置浮层内身份表面获得与 ADR-013/014 一致的 M3 语言；原型 `prototypes/about-page/index.html` |
| ADR-020 | 2026-09-08 崩溃窗口采用**独立 Fluent 设计系统**：自带 `Crash.*` 令牌（就近定义在窗口 `Window.Resources`）、不引用 `Launcher.*`、不随动态色漂移；`FluentTheme` + `SymbolThemeFontFamily` 资源与 `FAFontIcon` 字体样式由最小 `CrashReportApp` 注册 | §1 原则 3 显式豁免、§3.6/§4.7；视觉由黄金截图守护 |
| 统一-0921 | 2026-09-21 间距/图标/透明度统一收尾（零视觉变化）：补 `Launcher.Spacing.None`=0 Double 档并收编 2 处 Setter 零间距字面量；Crash 窗口 3 处字面量（BorderThickness×2、MinWidth）就近 token 化，Crash 扫描扩到 Setter 形式与 `BorderThickness`；新增 Opacity 契约——样式/视图/控件只允许结构性 0/1 或 `Launcher.StateLayer.*` 引用（StateLayer 五档全保留，分数透明度唯一合法来源；`OperationSurfaceAnimator.DipOpacity`=0.58 为 C# 动效编排值，不在样式词汇内，维持现状） | §2.2/§2.6；契约 = `Opacity_UsesOnlyStructuralValuesOrStateLayerTokens`、`StyleFiles_UseStaticTokensForVisualValues` 补 Spacing 族 |

---

*维护约定：本规范随 P1–P3 每个阶段更新；变更走 PR 并在附录 A 决策记录追加记录。*
