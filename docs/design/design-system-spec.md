# Cafe Launcher 设计系统规范（Design System Specification）

> 状态：**现行设计规范**。§1–§10 是现行规则，§11 是实施现状，附录 A 保留最初决策脉络；后续变更以各节修订及 [ADR 索引](adr/README.md) 为准。
>
> 分工：视觉数值、组件与表面规格归本文件；界面行为语义（模态交互权、ESC、动效模式）归 [CONTEXT.md「设计系统」](../../CONTEXT.md#设计系统)；XAML 与样式写法归[开发规范 §4](../../PROJECT_CONVENTIONS.md#4-xaml-与视觉规范)。

---

## 1. 设计原则

1. **壁纸优先**：主壳身份 = 动态壁纸画布 + 悬浮信息层（M3 表面语义）。任何改动不得牺牲壁纸表现力，壁纸区域文字以最小 scrim 保证可读（见 §7 豁免区）。
2. **M3 语义、Fluent 底座**：Material Design 3 是规范与语义模型（tokens、色阶、形状字阶动效）；Avalonia FluentTheme 是实现底座；**不引入 Material.Avalonia**（其为 M2 世代，无 HCT/动态色，主题系统已 Obsolete）。
3. **全 token 化**：视图 XAML 只允许引用 `Launcher.*` token；原始色值、裸间距/圆角只允许出现在 `App.axaml` 定义处（`UiStyleContractTests` 锁定）。**唯一豁免（[ADR-020](adr/ADR-020-崩溃窗口独立Fluent设计系统.md)）**：崩溃窗口 `Views/CrashReportWindow.axaml` 使用自带的 `Crash.*` 令牌，原始值就近定义在自身 `Window.Resources`——隔离报告进程（`CrashReportApp`）没有 `App.axaml`。
4. **离线自包含**：动态色在本地生成（壁纸取色、M3 scheme 计算均无网络/遥测）。
5. **功能与阅读优先**：保留业务行为、动态主题设置、风险确认与恢复指导；通过留白、字重和分组建立层级，删除重复说明，不使用中点作为普通 UI 分隔符。
6. **契约优先、分阶段演进**：AA 对比度、token 存在性、覆盖层 Z 序由测试锁定；视觉变化按 P1（token 体系）→ P2（组件）→ P3（表面）落地，每阶段可独立验收回退。

## 2. Token 体系

### 2.1 命名与引用

- 格式：`Launcher.<Family>.<Role>[.<State>]`，点分层、字符串键、PascalCase 段。示例：`Launcher.Color.Primary`、`Launcher.Spacing.Md`、`Launcher.Motion.Duration.Normal`、`Launcher.Elevation.Shadow.Md`。
- **P1 一次性重命名**全部既有 ~120 个 `Launcher*` 键（脚本 + 映射表，见 P1 计划 M1），不留别名层、不保留旧键；`UiStyleContractTests` 与全部消费方同步，编译零警告收口。
- 引用规则：**静态 token 一律 `{StaticResource}`**；仅主题变体/运行时覆盖（scheme 角色、Dark/Light 分档）用 `{DynamicResource}`。P1 修复 `Views\Styles\{Toast,RemoteContent,SetupWizard}.axaml` 中 33 处静态 token 误用 `{DynamicResource}` 的漂移。
- 原始色值只允许出现在 `App.axaml`（及未来主题字典文件）的 token 定义处；`MainWindow.Styles.axaml` 中 2 处 BoxShadow 与 1 处主题无关渐变（有注释声明）迁为 token 定义后再引用。**（M2 实测限制并记录：`BoxShadow` 是无 `TypeConverter` 的结构体，Avalonia 12.1.1 无法在 `Setter` 处经 `{StaticResource}` 做运行时字符串转换——字面量可用是因为 XamlIl 编译期解析。故阴影值仍集中在 `Launcher.Elevation.Shadow.*` token（契约测试锁定），消费点暂用字面量 + 注释引用；待 Avalonia 提供转换器后改回 StaticResource。）**

### 2.2 家族清单

| 家族 | 内容 | 示例 |
|---|---|---|
| `Launcher.Color.*` | M3 scheme 角色（primary/secondary/tertiary + containers + surface/outline/error 族）+ 业务语义色（Success/Warning/Info）+ 主题无关特殊色（Chrome/Overlay/scrim/媒体占位） | `Color.Primary`、`Color.OnPrimary`、`Color.PrimaryContainer`、`Color.Surface`、`Color.Outline`、`Color.Danger`、`Color.Success`、`Color.Overlay.Scrim` |
| `Launcher.Text.*` | 应用文本专用角色（Primary/Secondary/Body/Link/OnChrome/OnDark/Placeholder 等） | `Text.Primary`、`Text.OnChrome` |
| `Launcher.Spacing.*` | 间距 0/4/8/12/16/20/24/32/40 全档（Double，`Spacing.None`=0 为零间距显式档）+ Thickness 全档（`Spacing.Thickness.*`） | `Spacing.Md`、`Spacing.Thickness.Lg` |
| `Launcher.Radius.*` | 目标字阶 4/8/12/16（`Xs/Sm/Md/Lg`，含全部四档 token）；28（`Xxl` 或 full）无消费面，暂不落地（ADR-002 只重标四档） | `Radius.Xs`、`Radius.Md` |
| `Launcher.Typography.*` | M3 角色字阶（Display/Headline/Title/Body/Label × 字号/字重/字距）+ 字体族 | `Typography.FontSize.Body.Md`、`Typography.FontWeight.Strong`、`Typography.FontFamily.Monospace` |
| `Launcher.Icon.*` | 图标尺寸 16/18/20/22/24 | `Icon.Md` |
| `Launcher.Control.*` | 控件高度/宽度（Setting36/Dialog40/Bottom48/Launch58/Field40/Chip32/Swatch28 等） | `Control.Height.Setting` |
| `Launcher.Layout.*` | 视口级布局常量（banner 高 220、news 视口 184、调试窗 720×540、日志窗 720×592 等） | `Layout.Banner.Height` |
| `Launcher.Motion.*` | 时长/缓动/偏移（见 §2.7） | `Motion.Duration.Normal` |
| `Launcher.Elevation.*` | 阴影 0–3 档（颜色/偏移/模糊 token 化） | `Elevation.Shadow.Md`、`Elevation.Level.Card` |
| `Launcher.StateLayer.*` | 状态层不透明度 8%/12%/16%/24% + `Disabled.Content` 38%；**消费纪律（统一-0921）**：样式/视图/控件的 `Opacity` 只允许结构性 `0`/`1` 或引用本族——分数透明度的唯一合法来源（契约锁定）；半透明视觉（hover/pressed 叠加、scrim、阴影）以画刷 alpha 在 token 定义处表达，不写元素 Opacity | `StateLayer.Hover`、`StateLayer.Disabled.Content` |
| `Launcher.Border.*` | 边框厚度刻度（Default 1 / Focus 2，2026-08-26 补录） | `Border.Thickness.Default` |
| `Launcher.Component.*` | 组件专属覆盖（Toast 宽度、对话框标题高度等） | `Component.Toast.Width` |

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

- **角色集**：primary/secondary/tertiary + 各自 `On*`/`*Container`/`On*Container`（`PrimaryContainer`/`OnPrimaryContainer` 于 2026-08-26 补齐，占位+运行时覆盖）；surface 族（Surface/SurfaceVariant/Container 档）、outline 族、error 族（映射现有 Danger/DangerSoft/危险图标底）；业务色 Success/Warning/Info（Toast 等，不参与动态色）。**落地范围声明**：`SurfaceVariant`/`OutlineVariant` 由业务 token 承担（hairline 用 `Color.Card.Border`，ADR-013 M3 修正 2）；可见实心表面 = 对话框覆盖层的**表面配色族** `Dialog.Background/Header/Footer/Close.Hover/Close.Pressed`（`Header` 仅供首次设置向导自绘头部使用）（品牌蓝调时五键恒复位为声明默认值，种子跟随时整套随 scheme 中性表面梯度染色——Background/Header/Footer=`SurfaceContainerHigh`，Close.Hover/Pressed 为 `OnSurface` 的预混状态层；2026-08-28 由仅 `Dialog.Background` 扩展为全族并防残留——Q13/Q23 两决策合流）；`Surface/OnSurface/Outline` 保留为语义角色（`Outline` 由 `dialog-card` 的 0.5 DIP hairline 消费；`Surface/OnSurface` 为未来表面预留）。
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
| Title | 17 / 16 | SemiBold | Panel 对话框标题（17）、`titlebar-brand`（16） |
| Body | 15 / 14 / 13 | Normal/SemiBold | 进度标题（15）、正文/设置行标题（14）、说明（13） |
| Label | 12（两个兼容角色键） | Normal/SemiBold | 状态 chip、卸载步骤编号、设计画廊列标题 |

- 字体栈：`Noto Sans`（若系统可用/安装）→ `Segoe UI` → `Microsoft YaHei`（CJK 回退）→ `sans-serif`；等宽 `Consolas`。沿用 `Shell.FontFamily` 机制；**不内嵌字体文件**（Q14-a）。
- 保留现有 10 个 `Typography.FontSize.*` 角色键；2026-10-24 将 `Label.Sm` 从 11 提至 12（与 `Label.Md` 同值，以兼容既有引用），范围为 12–22。普通设置页以 13/14/18 三档为主，优先使用字重与间距建立层级。

### 2.6 尺寸与形状

- 间距：4/8/12/16/20/24/40；Thickness 补齐全档（现状仅 4/8/20 有 Thickness 变体）。
- 圆角目标字阶：4（Xs）/ 8（Sm）/ 12（Md）/ 16（Lg）四档齐备（ADR-002 一次性重标；Xs=4 档已按目标值落地）；28（Xxl 或 full）无消费面，暂不落地。
- 图标 16/18/20/22/24；控件高度 Setting36/Dialog40/Bottom48/Launch58/Field40/Chip32/Swatch28/DialogTitle56（P2 结合 M3 尺寸规范评审是否微调）。

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

唯一载体 = `Controls.DialogSurface` TemplatedControl，视觉契约集中于 `Views/Styles/DialogSurface.axaml`。Basic 和 Panel 的标题行共用对话框表面背景，只显示标题；Panel 保留关闭命令对应的 ✕，Basic 通过动作按钮退出。版本、目标和警示等必要信息放在正文。Panel 保留 56 DIP 标题行、滚动正文和含左辅助槽的发丝底带；无标题的 Panel 由内容自管布局（设置和首次设置向导等）。组件共用 16 DIP 外圆角及双层阴影；关闭矩阵、动效与自适应尺寸详见 ADR。

短文案确认框（重置设置、停止下载、放弃更改、退出向导）宽度上限为 440 DIP，正文上下分别留 8 DIP 和 12 DIP。修复、切换下载源和下载中退出的确认框宽度上限为 540 DIP，为较长的说明或操作按钮留出空间。正文随语言与文字大小换行。

### 3.2 按钮与动作

- 既有 class 选择器**一律保留**：`Button.primary-action`/`flat-action`/`danger-action`/`text-link`/`icon-link`/`icon-button`、`Border.*card`、`ListBox.settings-navigation` 等（headless 测试与视图依赖此兼容面）。
- 按钮四型 M3 映射：`primary-action`→filled；`flat-action`→中性 filled/tonal（跨功能共用）；`text-link`→text；`danger-action`→error-filled。共享模板 `LauncherBorderButtonTemplate` 保留，标准操作通过 `Launcher.Component.Button.Theme` 复用几何与中性外观（[ADR-050](adr/ADR-050-跨功能共享组件视觉基准.md)）。
- 依据 [ADR-050](adr/ADR-050-跨功能共享组件视觉基准.md)：`Launcher.Component.Button.Theme` 负责 40 DIP 标准最小高度、80 DIP 动作最小宽度、14 DIP 字号、8 DIP 圆角、居中与 2 DIP 透明焦点预留。主/次/危险类仅选择语义，`dialog-action` 保留兼容尺寸入口，`confirm-dialog-action` 保留调用和自动化标记。
- 次要操作共用不透明的 `Launcher.Color.Button.Secondary.Background/Hover/Pressed`。BrandBlue 使用固定中性底，SeedFollowing 使用方案的 `SurfaceContainerHighest`；hover/pressed 以当前中性前景预混 8%/16%，三态在每次主题应用时一起重建。`Dialog.Action.Background` 同步为兼容底色。
- 设置、资源面板、确认框、向导和 Toast 复用同一角色。Toast 与显式 compact 操作保留 36 DIP 紧凑密度；主界面任务区 48/58 DIP 尺寸和布局保持现状。按钮采用 `MinHeight`，文字放大与长文案自然增高。
- 键盘焦点只改描边颜色；禁用仍保留 2 DIP 厚度，不因取焦或禁用改变几何。中性焦点在表面、字段与次要操作三态上满足 3:1；实心主操作/危险操作用 `OnPrimary`/`OnError`。跨场景实际渲染与动态方案对比度分别由 `ActionButtonThemeHeadlessTests`、`MaterialSchemeGeneratorTests` 守护。
- 崩溃窗口继续使用独立 `Crash.*` 与本地模板，映射同一 40 DIP 操作高度、14 DIP 正文、8 DIP 控件/16 DIP 面板圆角及键盘焦点规则，不引入 `Launcher.*` 或正常 DI 依赖。

### 3.3 设置行与表单控件

- 设置行与表单行的控件语义：**即时生效**用 `ToggleSwitch.setting-toggle`；**确认/提交时才生效**（表单填写）或按条目独立多选用 `CheckBox`，对话框内以 `setting-checkbox` 限定作用域（描边 `Launcher.Color.Outline`、选中态 `Launcher.Color.Primary`——Fluent 默认选中色硬编码为 `#FF0078D7`，不随动态配色变化，必须覆盖；禁用态显式降 `StateLayer.Disabled.Content` 不透明度，因为 `:checked` 声明在 Fluent 的 `:disabled` 之后会盖掉主题禁用画刷）。**填充与前景必须成对改**：勾号（`Path#CheckGlyph`）与开关旋钮（`Ellipse#SwitchKnobOn`）都必须显式设为 `Launcher.Color.OnPrimary`，因为 Fluent 自带的勾号/旋钮前景是与自身强调色填充配套的白色——换成主题色填充后，暗色主题的 Primary 是 tone 80 浅色，白勾压浅底只有 1.7:1，无法看清（`:checked` 的每个变体 `:pointerover` / `:pressed` / `:disabled` 都必须写，伪类更多的规则会盖过单伪类规则）。先例：日志导出对话框「包含内容」四行（日志和系统信息始终包含，崩溃报告和用户数据在导出时才生效；四行均带本地化 automation name，随 `Baselines/log-export.png` 黄金基线守卫，并有 `MainWindowHeadlessTests.Dialogs` 的渲染对比度断言 ≥3:1 守卫）、设置页布尔选项与资源面板的独立资源选择（均为提交式 `CheckBox.setting-checkbox`，保存后保留；外观即时预览可取消，不据此改为开关）。资源面板的 Switch 决策已由 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md) 取代。
- 字段、下拉选择与取色器触发器使用 40 DIP 标准高度、8 DIP 圆角。确认、表单、通知等 `DialogSurface` 统一为 16 DIP 外圆角，Basic/Panel 结构和模态交互权不变。

- 色板由 `RadioButton.color-swatch-button` 承载互斥选择；保留色块、勾号和索引同步，选中状态通过 `IsChecked` 对辅助技术可见，键盘可切换。色块命中区 36 DIP，2 DIP 焦点预留不改变几何。
- 视频音量滑块保留 Fluent 原生输入模板，将值轨道、滑块与 hover/pressed 映射到 `Primary` 家族，剩余轨道用 `Field.Border`，焦点用 `FocusRing`；禁用统一使用 `StateLayer.Disabled.Content`。原生蓝色不再作为该控件的独立强调色。

### 3.4 下拉选择与菜单（2026-11-01 增补）

- **设置页修订（2026-11-07）**：常规、游戏、下载与网络、外观、高级分区以 `settings-section` 限定新下拉主题，覆盖全部 `SettingSelect` 与主题色来源下拉；向导、日志导出和命令菜单继续沿用下述通用基线。触发器保留 40 DIP 最小高度、14 DIP 字号、8 DIP 圆角、220 DIP 最小宽度和现有箭头／输入模板；静止使用 `Dialog.Background` + `Outline`，悬停强调中性描边，按下／展开不填满灰色。展开与键盘焦点使用既有独立 `HighlightBackground` 绘制 2 DIP `FocusRing`，不参与内容测量；错误态仍用 `Error`，禁用不改几何。
- 设置下拉弹窗保留 8 DIP 外圆角，内缩改为 8 DIP；选项填充圆角为 4 DIP，最小高度仍为 40 DIP，长标签和放大文字自然换行增高，长列表继续原生滚动。前置 16 DIP 勾选槽 + 8 DIP 间距，未选项保持占位；主题色来源按“勾选槽 → 来源色点 → 标签”排列，收起内容保留原有模板。选中底为 `Primary.Soft`，悬停／按下叠加既有中性状态底；`Settings.Select.Selected.Foreground{,.Hover,.Pressed}` 优先取 `Primary`，仅在实际合成背景对比度低于 4.5:1 时使用可读回退色，文字、勾号和选中焦点同步。勾号对辅助技术隐藏，选中语义继续由原生 `ComboBoxItem` 提供。
- 四语言 × 明暗 × 100%／150%／200% 字号的设置下拉渲染矩阵、动态变体／两种中性色策略的状态对比度、Enter 提交与 Escape 放弃均由 Headless 与配色测试覆盖；实际设置页展开截图为 `settings-select-source-light` / `settings-select-source-dark`。
- 全局 `ComboBox` 共用 40 DIP 最小高度、14 DIP 正文、8 DIP 圆角及字段底色／描边；`setting-control` 保留原有 220 DIP 最小宽度。悬停和按下／展开使用共享中性按钮状态色，键盘焦点使用 2 DIP `FocusRing`；选中文字与箭头跟随控件前景，右侧箭头使用现有 Material `ChevronDown`（16 DIP 图标槽），仅替换原生箭头部件的绘制模板，保持位置、命中区域与展开行为；取焦不移动内容，校验错误仍使用 `Error`。
- 下拉弹窗、`MenuFlyoutPresenter`、`ContextMenu` 及子菜单统一使用 `Dialog.Background`、`Card.Border` 的 1 DIP 外描边、8 DIP 圆角和 4 DIP 内缩。选项及命令菜单项共用 40 DIP 最小高度、14 DIP 正文、8 DIP 圆角，文字放大时自然增高；分隔线为 1 DIP，水平／垂直边距为 8／4 DIP。
- 普通悬停／按下使用 `Button.Secondary.Hover/Pressed`；已选下拉项使用 `SecondaryContainer`／`OnSecondaryContainer` 与对应状态色，禁用项使用次要文字和统一禁用透明度。命令菜单保留图标、快捷键、勾选与子菜单，卸载项通过 `danger-menu-item` 使用 `Error`，命令及确认流程不变。
- 保留原生选择框、弹窗、滚动与菜单模板和输入行为；下拉项只替换呈现模板。对原生部件使用具有状态优先级的样式转发，覆盖 Fluent 模板局部赋值与状态颜色，避免只修改控件根属性而保留另一套表面；沿用既有 `Launcher.*` 动态角色，切换主题与动态方案时同步更新。
- `SelectionMenuThemeHeadlessTests` 覆盖实际绘制、几何稳定、禁用项跳过、Enter 提交／Escape 放弃、长列表滚动、文字放大、子菜单及打开时换主题。截图基线包括 `choice-popup-light`、`choice-popup-dark` 与游戏管理 `launch-split-menu`；独立崩溃窗口继续使用自身隔离主题。

### 3.5 新组件、状态矩阵与批次

- 新组件（Select、Chip、分页、滑块等）以 **ControlTheme + 新命名** 落地，token 走 `Launcher.Component.*`。
- **状态矩阵**（画廊展示 + 走查清单共用）：normal / hover / pressed / disabled / focus-visible / invalid × 各组件；状态层按 `StateLayer.*` 不透明度。
- **P2 组件执行决策**（详见 ADR）：四型历史规格 = ADR-004；当前跨功能基准 = ADR-050；批次 A→B→C = ADR-008；画廊矩阵范围 3×6 = ADR-007；状态层 Opacity 8/12/16/24% 见 `Launcher.StateLayer.*`。

### 3.6 例外

- 诊断面板与日志查看器列表控件维持 Fluent 基础模板，仅 token 兼容（Q3/Q21 例外声明）。
- 崩溃窗口（`Views/CrashReportWindow.axaml`）使用独立 Fluent 设计系统与自带 `Crash.*` 令牌，不复用 `Launcher.*`（[ADR-020](adr/ADR-020-崩溃窗口独立Fluent设计系统.md)）。

## 4. 表面规范

### 4.1 主窗口、标题栏与任务表面

- 主窗口标题栏在产品名右侧以 `v{version}` 显示宿主版本（保留完整预发布后缀，语言切换不改变格式）。版本徽标使用 `Label.Sm` 常规字重、`Text.OnChrome.Muted` 前景、`Color.TitleBar.Version.Background` 背景、`Radius.Xs` 圆角及 8×2 DIP 内缩，与产品名保持 `Spacing.Sm` 间距；徽标区域保留标题栏拖动行为。
- 标题栏文字与按钮统一使用 `Text.OnChrome` 族白字，底色固定为 `Color.TitleBar.Gradient` 三段黑色 scrim（alpha 0x8C/0x2E/0x00）：顶部加深并延长过渡，保证白字在任意壁纸上可读，不随壁纸亮度切换前景或 scrim 颜色。
- 单窗口 + 覆盖层：Z 序 = 主内容 → 设置 100 → 对话框 200 → 向导 500 → 向导退出确认 501 → Toast 1000。`ZIndex` 是兄弟节点排序，声明在 `MainWindow` / `MainWindowDialogsOverlay` 的使用点上；覆盖层的样式不设置 `ZIndex`。
- 功能分区（Shell / GameOperations / Settings / SetupWizard / Diagnostics / ResourcePanel）与“单窗口内选分类”模型不变；表现级布局自由（如设置页留白、卡片形态）。
- **统一操作分体按钮（2026-11-07，ADR-057）**：`GameOperationsSplitButton` 在所有安装状态共用。底座为 Avalonia `SplitButton`，`GameOperationsSplitButtonTheme` 承载视觉模板；主区按当前状态提供安装、更新、修复、刷新、开始游戏或查看任务，菜单选择不会替换主操作。
  - 2026-11-08 按用户要求移除主按钮左侧的操作摘要与游戏会话文字组，会话文字并入底部可选状态行首项，复用 `on-dark-caption`；空闲时隐藏，会话启动后显示启动中／运行中／已退出及启动失败，随「不显示」隐藏整行。主操作、悬浮卡片与状态栏保持原有位置，会话看护与托盘状态继续保留。
  - 两区共用 `Primary` / `OnPrimary` 和 `Radius.Md` 外轮廓，不留外部空隙。主区最小宽度 190 DIP，右区宽度与最小高度沿用 58 DIP，常规中文总宽 248 DIP；文字增长时主区和整体高度自然扩展。分隔线 1 DIP、上下内缩 12 DIP，使用 `OnPrimary` 与 `StateLayer.Selected` 透明度。
  - 指针悬停、按下只改变所在区的 `Primary.Hover` / `Primary.Pressed`；菜单打开时右区保持按下色。短操作忙碌时整个控件禁用；活动任务允许「查看进度」，文件操作菜单继续禁用。键盘焦点以 `OnPrimary` 的 2 DIP 内描边覆盖整体，不改变几何；内部两按钮不单独占用 Tab 停靠点。
  - Enter / Space 执行主操作，Alt+↓ / F4 展开菜单，Escape 收起菜单并恢复焦点。两区保留各自本地化 tooltip 和自动化名称。
  - 管理菜单使用 `TopEdgeAlignedRight`，与分体按钮右缘对齐，向上展开并留 8 DIP 间距；屏幕边缘空间不足时沿用原生弹窗避让。保留检查更新、创建快捷方式、打开游戏文件夹、修复与卸载；卸载前额外分隔。所有状态共用稳定面板；安装路径准备进入 `GameTaskDialog`，执行与结果使用主操作上方的 `GameTaskCard`。
  - 自动化覆盖统一布局的分区点击、键盘与焦点、忙碌禁用，以及默认/最小窗口下菜单的右缘对齐与上方间距。亮色与深色中文黄金基线分别为 `launch-split-button` / `launch-split-button-dark-zh`，展开菜单基线为 `launch-split-menu`。
- **安装准备对话框（2026-11-08 修订）**：复用 `DialogSurface` 的 Panel 形态、滚动正文、固定动作区与动效；仅承接安装准备，执行与结果由非模态悬浮卡片承接。安装准备正文为路径标签、40 DIP 最小高度的路径栏与「更改」链接、下载量／所需空间、桌面快捷方式复选框；底部「定位游戏」「开始安装」并排居右，关闭仅保留标题栏入口。长路径省略显示并可悬停查看完整值，容量元数据允许换行。安装准备关闭放弃草稿；执行阶段不打开模态进度框。行为归 [CONTEXT.md「游戏任务对话框」](../../CONTEXT.md#游戏任务对话框)。卸载保留其删除范围、残留信息和不可关闭执行态。
- **任务悬浮卡片（2026-11-08 精简修订）**：固定宽 320 DIP，右边缘与拆分主按钮对齐，位于按钮上方 16 DIP；不参与操作面板测量，不压缩壁纸和资讯区。固定紧凑布局，不提供折叠或展开详情。仅显示阶段、阶段百分比，下载时直接显示已下载大小／总大小和速度，检查与校验显示文件计数；不显示剩余时间、路径或逐文件列表。结果摘要最多两行并提供完整工具提示。暂停／继续、停止、日志、适用的重试与结果关闭直接位于标题旁，均具备本地化名称和工具提示。使用现有 M3 表面、阴影、圆角和文字角色。主按钮名称按任务／状态动态变化，任务执行中点击返回卡片。
- **底栏边距（2026-11-07）**：操作面板左右采用 40 DIP、底部采用 24 DIP 内边距，顶部保留 20 DIP 以维持最小窗口的资讯区域空间，内容组靠底排列；主操作与状态栏右缘对齐，状态栏与按钮之间留 16 DIP。隐藏状态详情时同时去除该行间距，按钮距窗口底部改为 40 DIP，与右侧一致；文字放大或状态换行时面板按内容增高。
- **底栏形态**：当前沿用既有控制面板的稳定布局（2026-11-07，ADR-057），不再按安装或执行状态改变结构。历史上的 Q18 仲裁结论已于 2026-08-25 撤销；材质对比结论仅作历史参考。前期结论（M3 贴边：对比度恒定 ≥7:1、三态一致；浮动胶囊：浅壁纸 ≈4.8:1 边缘 + 安装态高度需验证）保留为决策素材，`prototypes/bottom-bar` 分支已随一次性原型清理移除；重新裁决时按 ADR-001 标准重走（走查/原型流程不变）。

### 4.2 设置页

- **重设计蓝图（P3 表面执行；[ADR-013](adr/ADR-013-设置页重设计方向.md) + M3 审核定稿）**：标题栏拆分（「设置」并入导航列顶部 header；关闭 ✕ 并入内容区标题行右端）；导航列 = SecondaryContainer 激活填充 + leading icon（Material Symbols）+ 标签，无指示条；内容区 = 变体 B 纯列表 + 组间空档 + 行间 0.5 DIP hairline（以 `Color.Card.Border` 为分隔色）；覆盖层由 `DialogSurface` 统一承载 16 DIP 圆角 + 克制的双层 `Elevation.Shadow.Dialog` + `Dialog.Background`（Q13 表面中性；种子跟随时由 scheme neutral 覆盖，见 §2.4）；控件统一 `Field` 形态。**修正清单（随 P2/P3 落地）**：`Field.Border` 双档对比度（浅 `#8290A5`/深 `#7B89A0`，≥3:1）、hairline inset 规则、抽屉 leading icons。
- **信息密度与几何（2026-10-24）**：
  - 设置页保留分类标题和有实际分类价值的组标题；移除复述分类内容的副标题，以及语言、关闭行为、启动后行为、记住窗口位置/大小、主题和壁纸适配方式的重复行说明。下载源、启动校验、网络、风险和恢复说明保留；卸载范围细节使用可换行 caption，避免分类副标题样式移除后丢失换行；未删除任何资源键或设置项。
  - 设置页两侧标题区统一采用 16 DIP 顶部内缩，标题槽最小高度复用关闭按钮的 36 DIP 命中区；「设置」、当前分类标题与关闭按钮按同一条水平中心线对齐，文字放大时标题槽自然增高。
  - `SettingRow` 最小行高 52 DIP，说明换行时自然增高；无说明不预留行。横排文案块与操作相对整行垂直居中，剩余高度上下平分，文案块保留上下 8 DIP 内缩。横排说明区目标最小宽度复用 220 DIP 控件宽度令牌，间距 12 DIP；容纳不了当前操作时改为竖排，不重建控件。竖排操作区底部复用 `Launcher.Component.Settings.Row.Content.Padding` 令牌的 8 DIP 内缩，与下一行分隔线保持间距；恢复横排时清除该内缩，保持原有行高与居中。仍由各页滚动区承载长内容。保存/取消按钮共享 40 DIP 最小高度，基类 filled 按钮也使用最小高度，避免继承的固定高度造成底栏按钮不齐。
  - 装饰边框使用 `Border.Thickness.Hairline`（0.5 DIP）；设置行/对话框底带与向导/卸载分隔线同为 0.5 DIP，细线关闭布局取整。输入边界保持 1 DIP，焦点环保持 2 DIP；不得全局把交互描边减半。
  - 警示以图标、文字与语义背景共同表达，取消装饰左边线，保留全部原因与恢复操作。Markdown 引用的左边线有引用语义，使用独立组件令牌保留。
  - 更新弹窗最小尺寸为 480×320 DIP，最大宽度仍为 620 DIP，长内容沿用共享滚动区。正文加载中显示文字与不定进度条，失败时显示说明及「前往发布页」，操作按钮保持可用。图片独立显示加载与失败提示。
  - 启动器更新说明支持 Markdown 图片与可点击链接：图片按通知可用宽度等比缩小，链接使用 `Launcher.Text.Link` 并保留下划线。图片加载不阻塞正文和更新动作；生命周期与外部地址策略见 [领域契约](../../CONTEXT.md#资源面板与远程内容)。
  - 对话框 16 DIP 外圆角与共享组件圆角体系保留；双层阴影减为 `0 16 40 #33` / `0 2 6 #14`（完整色值见 token），不为页面添加新阴影。
  - 动态状态色沿用原混合配方；仅在文字对比度低于 4.5:1 时，向远离前景亮度的方向混合，使悬停/按下仍可辨认。此修正不改变 M3 基础角色、取色算法、8 个变体或中性色选项。
- （2026-11-07，[ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md))所有 `SettingRow` 行标题统一 14 DIP 普通字重，组标题 14 DIP 强调字重与 `Text.Primary`，说明 13 DIP `Text.Secondary`；分类标题仍为 18 DIP 强调。正文顶部按分类说明用途：常规说明语言、窗口、显示与更新；游戏说明目录、启动校验、运行环境和维护；下载与网络说明来源、限速与代理；外观说明主题、背景、配色、动效与可撤销预览；高级说明日志、数据目录和重置；关于说明版本、更新、帮助与法律信息。保存要求写入相关分类说明，不再重复显示统一保存提示。分类说明复用选项的本地化 Description，随导航和语言切换更新。路径草稿与当前安装维护分组，维护区显示实际安装目录；路径未保存时显示说明并禁用维护操作。

- **分类与保存反馈（2026-11-07）**：保留六分类、双列外壳、固定标题/关闭与底部操作区；分类切换将共享正文滚动位置归零，不改变草稿、外观折叠状态或导航焦点。底栏左侧跨分类显示未保存状态；保存时状态与保存按钮改为「正在保存…」，显示不定进度条，并沿用导航/关闭/编辑的禁用规则。失败后恢复未保存提示并可重试，成功或放弃后清除提示。状态文本可换行，通过 polite live region 暴露给辅助技术；按钮保持右侧位置。保存进度复用 8 DIP `Progress.Bar.Height` 与 220 DIP 设置控件宽度，不另建令牌家族。
- **任务分组（2026-11-07）**：下载与网络按「下载：下载源、下载速度限制」→「网络：代理、HTTP/2」排列；高级按「诊断：日志级别、查看/导出日志」→「数据与恢复：数据目录、重置设置」排列。数据目录独立成行，重置继续保留危险操作样式与确认。各组首行不画顶部分隔线，沿用组间留白和共享设置行。
- **回归范围**：默认/最小窗口下的分类滚动复位与键盘焦点；保存等待、成功、失败重试、放弃与语言刷新；四语言下的 100%/150%/200% 底栏文字布局。新增黄金图 `settings-advanced-zh`、`settings-saving-dark-scaled`，并更新受影响的设置截图；不把无头文字放大等同于系统 DPI 或读屏器实测。

#### 外观设置分组与高级配色

- （2026-11-01）「当前背景」的说明随实际加载结果更新：自带和远程壁纸显示本地化来源名称，远程壁纸通过 tooltip 提供完整图片地址，自定义背景显示所选具体文件路径；远程加载回退到自带壁纸时同步显示自带来源名称。长路径省略显示并通过 tooltip 保留完整内容。
- 常用项按「主题与动效 → 背景 → 主题配色」排列，组标题保留强调字重、行标题使用普通字重；动态效果从常规移到外观，字段、默认值和应用时机不变。背景操作紧邻来源，图片与视频合并为「选择图片或视频」入口，使用同一文件选择器同时筛选两类格式；文件夹与清除入口独立保留，文件夹可随机选择图片或视频。缩略图按图片／视频类型展示，「当前背景」显示本次选中的具体文件路径，长路径省略显示并通过 tooltip 保留完整路径。路径与媒体类型随最新加载结果一起更新，不把随机结果写回文件夹设置。填充色仅在适应模式出现，音量按实际选中的文件类型显示，包含文件夹随机选中的视频。
- （2026-11-07，[ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md)）主题、背景来源、壁纸适配与中性色策略这四组 2–3 项短标签互斥值改用共享分段按钮：整组整圆胶囊描边、段间 1 DIP 竖线、选中段使用 `Primary.Soft` 底并显形勾选图标；`Segmented.Selected.Foreground` 优先取 `Primary`，仅在对话框或资源卡片上的实际合成背景不足 4.5:1 时回退到可读前景，焦点环使用独立覆盖层、不参与内容测量。未选中段的反选不写回来源，双向接缝由 `SegmentOptionConverter` 承担；长标签或 ≥4 项的动态效果、取色算法、配色变体与主题色来源仍用下拉菜单。共享视觉契约在 `Views/Styles/Segmented.axaml`。
- 同一分段门槛适用于其余设置页：常规页的关闭行为、状态面板、更新通道与下载与网络页的下载源（2 项短标签）使用分段按钮；游戏启动后行为、代理、下载限速、日志级别与游戏运行器因标签较长或选项 ≥4 项继续用下拉。
- 主题色来源下拉的每一项前置一枚圆点，预演「该来源当前解析出的主色」；触发器与菜单共用同一 `ItemTemplate`，行内不再单放与选项无关的色块。圆点随亮暗主题、壁纸取色、系统强调色与自定义色即时刷新，包括尚未选中的来源。打开设置时补齐当前壁纸色板，壁纸或算法变化也刷新未选中的壁纸预览；这些后台预览不改变当前主题色来源、不标记草稿为已修改，也不把未选中来源应用到主窗口。系统事件直接使用事件携带的强调色与明暗值，避免资源字典更新时间差导致预览滞后。
- 自定义背景的清除动作收为图标按钮，保留 tooltip 与辅助名称；两个选择动作仍是带文字的 `flat-action`，避免清除与选择争夺视觉重量。
- 取色算法、配色变体、中性色策略及种子跟随提示归入默认收起的「高级配色」。仍保留全部既有主题色来源与枚举值，算法继续只作用于壁纸模式；色板刷新与自定义取色器保持原绑定。
- 视频音量行在滑块与百分比旁提供「静音」复选框，仅自定义视频时显示。静音是独立的草稿设置，静音期间保留并允许调整音量，取消静音后恢复；保存和放弃改动沿用原流程，旧配置缺少静音字段时按 false 读取，默认音量仍为 0。
- 音量百分比按当前字号的 `100%` 测量值预留固定显示槽，并右对齐；数值从 0% 到 100% 切换不改变音量行宽度或移动静音选项，文字放大时预留槽随字体测量增大。
- 不提供独立的「预览效果」区域；外观变更继续即时应用到主窗口，背景组保留当前背景缩略图，不新增独立播放控件或逐帧取色。图片缩略图使用当前图片；视频缩略图异步加载，MP4 优先使用内嵌封面，封面缺失或不可用时提取第一帧；图像叠加 3 DIP 模糊与暗色遮罩，中央显示清晰的视频图标。模糊半径使用 `Launcher.Component.Settings.Appearance.Thumbnail.VideoBlurRadius`，加载失败保留静态回退图及视频标记；视频封面或首帧的原始图像同时供壁纸主题取色使用，等待加载期间保留旧色板；两者均不可用时使用内置图取色。仅切换视频来源或文件内容指纹变化时自动重新取色，音量、静音、适配和窗口尺寸变化不重取，不逐帧更新；手动刷新色板或切换取色算法沿用缓存图像。
- 高级配色原生 `Expander` 的展开状态只属于设置会话：切换分类保留，重新打开设置时复位，不进入设置草稿或 `settings.json`，不启用保存按钮。程序式收起时，若焦点在隐藏的内容中，将焦点归还对应标题。标题使用继承共享按钮几何的 `Launcher.Component.Settings.ExpanderHeader.Theme`：最小高度 48 DIP、内容左右各内缩 12 DIP（`Launcher.Component.Settings.Expander.Header.*`），并保留 2 DIP 焦点预留；静止态融入列表，悬停／按下整行使用中性按钮状态色，箭头共用 Material 图标且没有独立底块，箭头右缘随内容一并内缩。标题模板由本地主题控制，避免 Fluent 内部模板的交互色覆盖；鼠标操作不显示键盘焦点环，禁用和取焦不改变几何。- 背景加载失败状态由现有背景加载结果给出，警示与文件选择入口始终放在折叠区之外。过期加载不更新状态；成功加载或切回内置来源后清除警示，视频失败保持原有不重复重试语义。
- 设置外壳、分类 code、固定标题和关闭按钮、正文滚动、取消／保存及未保存确认均保留。回归覆盖展开不致脏、折叠不丢值与焦点回归、背景缩略图和加载失败恢复；四语言／文字放大矩阵包含展开状态，截图基线为 `settings-appearance` 和 `settings-appearance-advanced-dark`。

### 4.3 对话框、Toast 与更新说明

- 对话框族已由 [ADR-015](adr/ADR-015-对话框家族v2统一框架.md) 统一为 `DialogSurface`；动作顺序、默认按钮、动作底色与边框、焦点视觉由 [ADR-040](adr/ADR-040-对话框动作按Fluent对齐顺序与默认按钮.md) 按 Fluent 裁决。组件解剖见 §3.1。
- **Toast**：删除自动消失进度条，仅保留操作执行中的 indeterminate 进度条；关闭命中区提升到 36px。消失时长、悬停暂停与严重级默认档见 [CONTEXT.md「Toast 消失时长」](../../CONTEXT.md#设计系统)。
- **更新说明**：Markdown 图片与链接的加载、缓存、协议白名单和释放行为见 [领域契约「启动器更新说明」](../../CONTEXT.md#资源面板与远程内容)；视觉使用 `Launcher.Text.Link` 并保留下划线，图片按通知可用宽度等比缩小，不阻塞正文和更新动作。

### 4.4 首次设置向导

设置向导表面为 920×480 DIP（2026-11-07 收紧高度）。2026-11-11 起由 [ADR-059](adr/ADR-059-设置向导统一舞台与双栏解剖.md) 统一解剖，替代 [ADR-017](adr/ADR-017-设置向导动效落地与M3界面细化.md) 的居中单列与 [ADR-058](adr/ADR-058-协议同意并入首次向导并按版本重新征询.md) 修订中的双卡协议版式；2026-11-12 由 [ADR-061](adr/ADR-061-设置向导新增欢迎首步与配置概览.md) 在流程最前新增欢迎页，步骤扩为七步。

- **五段表面**：全幅头部 → 发丝线 → 正文滚动舞台 → 发丝线 → 全幅动作带，头部与动作带贴表面边缘，留白只由 `Wizard.Header.Padding`、`Wizard.Footer.Height/Padding` 消费。
- **头部**：向导标题 + 本地化步骤计数（`setupWizardStepProgress`，「第 N 步，共 7 步」）+ 跳过钮 + 七段进度轨；计数与到达段在换面中点由 `ApplyChromeState` 翻转（不绑定 `Step`），协议更新模式（单步）折叠整条进度轨并计数为「第 1 步，共 1 步」。跳过钮是头带的次要逃逸口，取 `wizard-skip` 安静文本钮（静止透明底、Secondary 前景、Body.Sm 文案，悬停/按压铺 `Content.Row.Hover` 并转 Primary），不与标题和主/次动作争层级。
- **正文舞台**：860 固定舞台 = 300 左引导 + 32 舞台间距 + 528 右面板，七步结构一致，差异仅在右面板。左引导为 56 圆形语义图标（七步依次为挥手、翻译、文档、路径检索、云端下载、路由切换、清单勾选，复核页徽章取 Success 底）+ 步骤标题 + 说明；右面板为 Card 表面（内边距 20），承载欢迎概览、语言选择、协议阅读器、路径输入与状态行、`wizard-option` 选项行或复核 hairline 摘要行。语言与路径字段在控件上方标注本地化字段名，路径字段补充重新检查提示；复核行编辑入口为 Pencil 图标 + 「修改」文本。步骤纵向拉伸铺满视口，正文留白由右面板的 `Wizard.Body.Margin` 承担。
- **欢迎步**：首步只建立预期，不改配置。左引导徽章为 `HandWave`；右面板为只读概览——caption（`setupWizardWelcomePlan`）+ 五行「图标块 + 名称 + 一句话」，顺序与后续步骤一致（语言、协议、游戏路径、下载源、代理），行间发丝线分界且首行之前不画线，行内无按钮与单选。图标块取 `Content.Row` 静底（`Wizard.Welcome.Row.MinHeight`=42、`Wizard.Welcome.Icon.Size`=34），保持预览语气；清单需随步骤增删同步维护。
- **协议步骤**：左引导追加版次胶囊与语言声明（声明图标顶部对齐说明首行，不随换行整块居中）；右面板为 `AgreementScrollViewer`（唯一滚动区）+ 固定底栏同意项。步骤绑定 `ScrollViewer` 视口高度使文档在有界高度内滚动；滚动提示仅保留为复选框的 `AutomationProperties.HelpText`，勾选门禁仍由滚动到底驱动。文档正文以 `Launcher.Typography.FontFamily.Document` 钉住 CJK 字族，不继承随界面语言切换的拉丁字体栈。同意底栏不铺独立底色（透明，与卡片同表面），只以顶缘发丝线分界——卡片 Border 的圆角不裁剪子元素，不透明底会盖住卡片左右/底边描边与圆角。
- **动作带**：上一步与下一步/完成同为 `wizard-action`、`MinWidth`=100（贴近中文动作自然宽度，短文案不拉出多余空档），现有四语文案下两枚动作同宽；下一步/完成叠加 `primary-action`，上一步在左、主操作在右。宽度是表面级布局例外，按钮的角色外观仍由共享主题决定。
- **步骤切换**：沿用 ADR-017 的顺序换页（333ms 对半分、中点换内容与滚动复位、Enter/Exit 曲线、起势帧、±14px 方向滑入、壳层中点翻转）与可中断所有权守卫，仅布局与壳层解剖变更。
- **配色**：向导打开时按设置页「主题色 = 默认」的方案落色（默认种子、TonalSpot、BrandBlue、明暗由当前主题模式决定：首启取系统默认，协议复核取已保存模式，会话在显示前先落该模式）。首启与协议复核都在完整初始化之前显示，不能沿用进程内残留的系统强调色；落色发生在 `ShowSetupWizard` 置可见之前，避免先闪一帧旧色。
- **令牌**：舞台宽、左引导宽、引导图标尺寸、头/动作带留白、正文边距、进度轨高度、动作最小宽度与欢迎概览行度量均为组件令牌；布局间距一律取 `Launcher.Spacing` 标度（舞台间距取 `Spacing.Xxxl`=32、进度轨间距 8），面板内边距取 `Spacing.Thickness.Xl`，组件令牌不得进入 `Spacing`/`Padding`；样张复刻的组件内部内边距（头部 24,16,24,14、选项行 14,13、版次胶囊 8,2）是显式例外，以组件令牌固定。截图基线为 `setup-wizard`（欢迎页）、`setup-wizard-agreement`、`setup-wizard-agreement-read-dark-zh`。

### 4.5 资源面板

资源面板在 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md) 中改为提交式复选框并增加未保存状态；关闭、刷新或更换 UID 前保护草稿，确认使用既有 Basic 对话框和模态栈。资源面板已由 [ADR-041](adr/ADR-041-资源面板MD3结构级重设计.md) 完成 MD3 结构级重设计：UID 单卡三态、分段来源、单卡三行 + 状态 chip，`DialogSurface` 外层骨架与命令接线不动。来源分段控件以独立胶囊焦点层绘制，不参与内容测量；四语言、亮暗主题及 100%／150%／200% 文字下取焦不改变段宽、相邻位置或选择值。

### 4.6 关于分区

依据 [ADR-018](adr/ADR-018-关于分区重设计方向.md) 及 2026-11-01 修订：关于身份卡的检查更新保留 `primary-action` 主按钮强调，官方网站、帮助文档保持 `flat-action` 次要操作；三项共用 40 DIP 最小高度、14 DIP 字号、8 DIP 圆角和内边距，仅移除检查更新的紧凑尺寸例外。身份卡可为半 DIP 外描边关闭布局取整，但其内容容器恢复 `UseLayoutRounding=True`，避免按钮继承半像素偏移而产生模糊直边。关于分区沿用 [ADR-056](adr/ADR-056-提交式选项与维护目标语义统一.md) 的信息层级，当前顺序为文本身份卡（产品名、版本与构建时间 caption、副标题、三个常用动作）→ 仓库/反馈链接 → 贡献者 → 特别感谢 → 默认折叠的「构建与运行信息」（提交、构建、运行时、Avalonia、系统五项，等宽值与 hairline）→ 法律信息。身份卡操作可换行，技术信息折叠复用设置 Expander 表面。贡献者与特别感谢共用双行链接模板，左侧为 40 DIP 圆形图标占位符，右侧依次显示名称与可换行的本地化简介。条目最小高度为 64 DIP，整行打开贡献者主页、项目仓库、蔚蓝咖啡厅官网或应用图标的来源说明。社区致谢作为特别感谢列表下方的普通文本。

### 4.7 崩溃窗口

崩溃窗口（[ADR-019](adr/ADR-019-不可恢复崩溃两级兜底.md) / [ADR-020](adr/ADR-020-崩溃窗口独立Fluent设计系统.md)）是独立于主壳的终端表面，**不参与主窗口 MD3 令牌体系**——中性冷灰蓝表面（不随动态色）+ 8/16 圆角 + 12/14/20 字阶 + 危险红/链接蓝双语义色；700 固定宽、高度随内容、不可缩放；默认折叠技术详情，动作带仅「打开日志目录 / 复制详情 / 退出启动器」。同进程（tier-1）与隔离报告进程（tier-2）共用同一窗口实现。操作高度、正文与键盘焦点规则同 §3.2，但不引入 `Launcher.*` 或正常 DI 依赖。

### 4.8 诊断与日志

诊断面板与日志查看器列表控件维持 Fluent 基础模板，仅 token 兼容；工具栏和表单控件复用共享视觉。日志导出对话框使用 `setting-checkbox`，四行均带本地化 automation name，由 `Baselines/log-export.png` 与渲染对比度断言守护。

## 5. 主题与壁纸

- `RequestedThemeVariant="Default"` 跟随系统不变；主题模式（System/Light/Dark）持久化机制不变。
- 背景来源/适配/填充/自定义图片/图片文件夹机制保留，自定义图片与视频共用文件选择入口，并提供仅选中视频时显示的音量滑块，默认静音；使用既有设置行、令牌和本地化无障碍名称。视频置于真实内容/覆盖层下方，首帧前和失败时保留 `launcher-background.png`；减少动态效果时暂停并显示首帧静帧，最小化和托盘隐藏时暂停。
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

  `Danger.Pressed` 保留 `#C9353A`（白标签 5.18；其填充态消费由标签对覆盖，不做表面图标对）。全部为同色相单阶加深/提亮，未改视觉意图；UI ≥3:1 额外覆盖 Danger×Card/Dialog 与 Toast 严重性四色。
- **Soft 底色配对（2026-11-12）**：`Success`/`Danger` 直接压在同色 `*.Soft` 底上只有 4.32/4.03:1；资源面板状态 chip 的文字与图标改用配对 token——`Color.Success.OnSoft`（Light `#126B33` 5.69 / Dark `#4ADE80` 8.09）与 `Color.Danger.OnSoft`（Light `#C42B33` 4.94 / Dark `#E4676F` 4.69），由 `DesignTokenContrastTests` 的文本对锁定。新增 soft 底色一律同时定义对应 `OnSoft` 前景。
- **豁免区（显式声明 + 测试用例列出）**：自绘标题栏文字、标题栏按钮（chrome 态）、右侧社交列、底栏渐变/scrim 上的 `on-dark` 文字——豁免前提是叠加 scrim（`Color.TitleBar.Gradient` 三段黑色 scrim + `Color.Overlay.Scrim*` token，最小 scrim 语义：保证该区文字与当前壁纸任意色对比度可读，豁免在走查清单中逐项复核）。
- **键盘**：全表面 Tab 序、可见焦点环（`Button:focus-visible` 等既有规则随重命名迁移）、覆盖层焦点陷阱（`OverlayFocusBehavior` 保留）。`wizard-option`（ADR-017）与设置导航/更新文件列表同样接入 token 焦点环（审计-0828 补全）；**动态焦点环单独验证**：`FocusRing` 使用不透明强调色，在对话框/输入表面不足 3:1 时回退到同一 M3 方案的 `OnSurface`；实心主操作/启动按钮的内部焦点描边使用 `OnPrimary`，危险按钮使用 `OnError`，避免描边融入同色填充。选中下拉项使用对应状态的 `OnSecondaryContainer` 前景绘制焦点；独立 2 DIP 描边向内缩 2 DIP，仅邻接容器填充，避免强迫单一颜色同时对比饱和容器与中性弹窗。设置导航保留 Fluent 模板焦点绘制，验收检查实际模板而非只读取控件根画刷。资源面板的来源分段控件以独立胶囊焦点层绘制，不参与内容测量；四语言、亮暗主题及 100%／150%／200% 文字下取焦不改变段宽、相邻位置或选择值。`MaterialSchemeGeneratorTests` 覆盖 8 变体、7 种种子、双主题、双中性策略及状态背景；壁纸与实际键盘路径仍须人工走查。**ESC 语义**：ESC 始终作用于模态栈顶层，焦点在搜索框等可编辑控件内也不改变路由；需要保留未提交改动的表面把确认接在关闭命令上（设置页脏态），既不吞 ESC 也不静默丢弃。
- **动效**：保留 System 默认值、完整动效与降动效语义，以及可中断的向导步骤过渡。
- **内容自适应**：共享设置行按操作实际宽度为说明预留空间，不足时改为上下排列，保持控件实例/焦点/选中值；空说明折叠，提示文本为空或被 `IsHintVisible` 关闭时同样折叠（设置提示无需再显式开启）。对话框文本按钮使用 `MinHeight`，设置操作组、统一操作面板、安装准备对话框和任务悬浮卡片的元数据可换行。无头测试覆盖四语言、1300×754 / 1024×640、亮暗主题与 100%/125%/150% 文字放大。测试直接放大控件文字，不等价于 OS 文字大小偏好已自动接线，也不覆盖物理 DPI 的全部渲染差异。

## 8. 设计画廊（Q11）

- 位置：Debug 构建 `IsDebugFeaturesEnabled` 可见（与现有调试面板同门），`Views/` 新增 `DesignGalleryOverlay.axaml`。
- **M4 状态（已实现）**：`Views/DesignGalleryOverlay.axaml` + `ViewModels/DesignGalleryViewModel.cs`（Debug 面板「打开设计画廊」按钮进入，`dialog-overlay` 层，ZIndex 200，关闭同调试面板）；数据源 = 运行时枚举 `/Application.Resources` 根字典和 Light/Dark `ThemeDictionaries` 中全部 `Launcher.*` 键 + `DesignTokenGrouping` 按 §2.2 十二家族分组（键段解析，零漂移；无法归类的键进「Other」），显示当前主题变体值（`TryGetResource` 当前变体）；色板 swatch + 键名 + 值文本；本地化标题/分组名（4 语言，`designGroup*` 键族）；`UiStyleContractTests` 已将画廊纳入 ViewFiles 规则扫描。**P2 扩展（已实现）**：组件状态矩阵 = 四型按钮（filled/outlined/text/error-filled）/ 卡片（Toast 卡）/ 设置行 × 6 态（normal/hover/pressed/disabled/focus-visible/invalid；invalid 仅设置行/输入类，按钮与卡片以空占位表达——ADR-007），状态列/行标签 4 语言（`designMatrix*`/`designState*` 键族）；矩阵同时是走查清单实物载体（见 `docs/design/design-walkthrough-checklist.md`）。底栏双原型对比区随 Q18 重新裁决再定。
- 内容：P1 = token 总表（按 §2.2 家族分组：色板 swatch、字阶、间距/圆角/动效表）；P2 = 组件状态矩阵（3 组件 × 6 态，ADR-007）与底栏双原型对比区（Q18 仲裁）；组件状态矩阵同时是走查清单的实物载体。
- 画廊文案走本地化契约（resx 4 语言）。

## 9. 验收与流程（Q10）

- **每阶段门禁**：`UiStyleContractTests` 全绿（含新契约）+ 新增测试全绿 + 编译零警告 + `scripts/Test-LocalizationContract.ps1`（涉及新字符串时）+ `.\verify.ps1`；PR 附截图（可见 UI 变更）。
- **契约测试扩展点**：token 存在性/旧键清零、禁裸色值与 `{StaticResource}` 规则、AA 对比度、覆盖层 Z 序（既有）。
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
| 底座 | M3 语义 + FluentTheme；`Launcher.*` 十二家族 token；AA 契约；动态色 `ApplyScheme`；黄金截图；Debug 门画廊 | §2、§7、§8 |
| 设置页 | ADR-013 布局；ADR-056 提交式选项、维护目标、字重层级与折叠技术信息 | §4.2、ADR-013、ADR-056 |
| 对话框 / Toast / 向导 | 已按 ADR-014/015/017/040 落地 | §3.1、§4.3、§4.4 |
| 关于分区 | ADR-018 重设计；ADR-056 调整信息顺序与折叠技术信息 | §4.6、ADR-018、ADR-056 |
| 崩溃窗口 | 独立 `Crash.*` Fluent 系统，同进程与隔离报告进程共用 | §4.7、ADR-019/020 |
| 资源面板 | ADR-041 MD3 结构级重设计；ADR-056 提交式选择与草稿保护 | §4.5、ADR-041、ADR-056 |
| 就绪态启动按钮 | 已合并为 SplitButton | §4.1 |
| 跨功能共享组件 | 按钮、字段、下拉与菜单统一几何和状态 | §3.2–§3.4、ADR-050 |
| 仍开放 | 底栏形态（Q18，ADR-001 仲裁标准保留）；主壳首页重设计（已搁置，无既定蓝图） | §4.1 |

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
| ADR-020 | 2026-09-08 崩溃窗口采用**独立 Fluent 设计系统**：自带 `Crash.*` 令牌（就近定义在窗口 `Window.Resources`）、不引用 `Launcher.*`、不随动态色漂移；`FluentTheme` + `MaterialIconStyles` 由最小 `CrashReportApp` 注册 | §1 原则 3 显式豁免、§3.6/§4.7；视觉由黄金截图守护 |
| 统一-0921 | 2026-09-21 间距/图标/透明度统一收尾（零视觉变化）：补 `Launcher.Spacing.None`=0 Double 档并收编 2 处 Setter 零间距字面量；Crash 窗口 3 处字面量（BorderThickness×2、MinWidth）就近 token 化，Crash 扫描扩到 Setter 形式与 `BorderThickness`；新增 Opacity 契约——样式/视图/控件只允许结构性 0/1 或 `Launcher.StateLayer.*` 引用（StateLayer 五档全保留，分数透明度唯一合法来源；`OperationSurfaceAnimator.DipOpacity`=0.58 为 C# 动效编排值，不在样式词汇内，维持现状） | §2.2/§2.6；契约 = `Opacity_UsesOnlyStructuralValuesOrStateLayerTokens`、`StyleFiles_UseStaticTokensForVisualValues` 补 Spacing 族 |

---

*维护约定：本规范随 P1–P3 每个阶段更新；变更走 PR 并在附录 A 决策记录追加记录。*
