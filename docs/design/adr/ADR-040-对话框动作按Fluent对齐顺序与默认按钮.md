# ADR-040: 对话框动作按 Fluent 对齐 —— 顺序翻转、默认按钮、标准动作底色

- 状态：**已接受**（2026-09-25，用户指示「翻转动作顺序、设置默认按钮、更改动作按钮的背景色」并直接在项目中实现）
- 背景：
  - 现状是 M3 语义：动作顺序「取消在左、确认在右」，默认焦点落在安全操作，动作按钮为「透明描边的次要动作 + 填充的主要动作」（ADR-014 / ADR-015）。
  - 一手依据（[MS Learn · Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)）：WinUI `ContentDialog` 的 **PrimaryButton（do-it）在最左、CloseButton（安全动作）在最右**；`DefaultButton` 会**获得 Accent 外观 + 响应 Enter + 打开时获得初始焦点**；Esc / 系统返回 / 手柄 B 都等价于 CloseButton；每个对话框必须至少有一个安全动作；「焦点所在控件若自行处理 Enter，则默认按钮不响应」。
  - Fluent 基础规范（`.agents/skills/designing-fluent-interfaces/references/foundations.md`）：Windows 的标准控件有静止填充（`ControlFillColorDefault`），不是透明描边；控件圆角 4、浮层圆角 8；焦点要用显式焦点描边。
  - 也就是说，现状的动作顺序与默认动作语义与目标平台规范**正好相反**——这是本轮改动的直接动因。
- 决策：
  1. **动作顺序翻转**（do-it 在左、安全动作在右）：`Controls/ConfirmDialog.axaml`、`Views/MainWindowDialogsOverlay.axaml`（更新对话框）、`Views/MainWindowLogExportOverlay.axaml`。通用不变量「`StackPanel.confirm-actions` 里 do-it 动作必须出现在 flat-action 之前」由契约测试守护，后续新增动作带自动受约束。
  2. **默认按钮**：
     - 非破坏性确认 → 确认动作就是默认按钮：`primary-action` 的强调填充即 Fluent 的 Accent 外观；Enter 由 `ConfirmDialog` 的冒泡 `KeyDown` 处理器承接（已被焦点控件处理的 Enter 不会到达，符合 WinUI 规则）；打开时初始焦点落在确认按钮。
     - 破坏性确认（`IsDangerConfirm`）→ **不设默认按钮**：初始焦点仍在安全动作上，Enter 因此落在安全动作，破坏性操作不会被键盘一次带走（保留仓内「默认焦点落在安全操作」的安全不变量）。
     - **刻意不使用 `Button.IsDefault`**：主窗口同时存在多个 `ConfirmDialog` 实例（各自 `IsOpen` 互斥），而 `IsDefault` 是窗口级候选，隐藏实例之间的默认按钮归属无法在本地约束；改用「对话框自己判定 Enter 落点」这条显式规则，`DefaultActionCommand` 是唯一判定处，行为测试与实现共用。
  3. **动作按钮底色**：新增中性 token `Launcher.Color.Dialog.Action.Background`（Light `#FFF4F8FC` / Dark `#FF222B38`），对话框动作带里的标准（次要）动作从「透明描边」改为**静止填充**（Fluent 标准按钮语言）；强调/危险动作保持纯填充，并把家族基类新增的 1px 描边置空，否则填充按钮会被描一圈灰边；禁用态改用 `Content.Row`。
     - 作用域限定 `StackPanel.confirm-actions`，因此设置区、内容区里同样使用 `flat-action` 的按钮不受影响。
     - 该 token 是中性层色，登记进 `MaterialSchemeGenerator.NeutralContentDefaults`，**不跟随种子**（ADR-010）——动作底色的语义是「中性控件填充」，不是品牌色。
- 结算：本决策**取代** ADR-015 中「取消/次要操作在左，确认/危险确认在右」与「安全钮在左、主钮填充在右」的顺序表述，以及 ADR-014 中同义的那一句。ADR-015 的其余条款（统一外壳、Basic/Panel 两形态、动效通道、尺寸律、关闭矩阵）不变。
- 后果：
  - 契约与行为测试：`UiStyleContractTests.Dialogs` 新增「do-it 先于安全动作」的不变量、确认框按钮命名顺序、以及底色/描边契约；新增 `ConfirmDialogHeadlessTests`（顺序、默认按钮落点、Enter 只触发一次、破坏性确认忽略 Enter）；`DialogActionButtonContractTests` 的类名与计数未变，无需改动。
  - 黄金基线：`Baselines/confirm-dialog.png` 与 `Baselines/log-export.png` 重制。实测 confirm-dialog 的变更量为 **0.55% 像素**，包围盒 `x 745..891 / y 393..434`（即动作带内两个按钮换位），窗口其余部分逐像素未动——这也解释了为什么 1% 容差的黄金比较在重制前仍会「偶然绿」。
  - 已知未覆盖：**设置工作区**（z=100 的常驻表面）的「取消 / 保存」顺序**未翻转**——它是常驻工作区而不是瞬态对话框，翻转属于另一个决策；需要时另开 ADR。
  - 重制基线的操作注意：`.\test.ps1 -UpdateGolden` 会重写**全部**基线，而 toast 的渲染在容差内并非位确定（本次重制后 `toast.png` 出现无意图变化，已 `git` 复原）。重制后必须核对 `git status`，只保留有意图变化的那几张。
  - 明确记录的两处 Fluent 偏离：破坏性动作用 critical 填充而非 Accent（WinUI 没有内置危险按钮样式）、破坏性确认不设默认按钮（不让 Enter 一次带走破坏性操作）。

## 修订（2026-09-25 同日）：动作按钮去边框 + 焦点视觉「默认不显示、键盘导航时显示」

- 追加决策（用户先指示「去除对话框按钮边框和全局默认显示键盘焦点」，随后细化为「默认不显示键盘焦点，但使用键盘导航时仍然显示」）：
  1. **对话框动作按钮不再画边框**：`Button.confirm-dialog-action` 的 `BorderThickness` 由 1 改为 `None`、`BorderBrush` 透明；对话框动作带里的次要动作同样处理。形状完全由底色（中性填充 / Accent / critical）表达。
  2. **焦点视觉 = 只在键盘导航时出现**。分工：
     - 框架默认的焦点矩形（`FocusAdorner`）在 `Views/MainWindow.Styles.axaml` 末尾全局置 `null` —— 它不是本仓语言，且会与应用自己的 FocusRing 叠成双层框。
     - 焦点环本身保留在各控件既有的 `:focus-visible` 规则上（Button / ComboBox / ToggleSwitch / ListBoxItem / RadioButton / 关闭钮 …），**不删不改**。
     - 程序式聚焦不再用 `NavigationMethod.Tab`，改用 `NavigationMethod.Unspecified`：`Views/OverlayFocusBehavior.cs`（打开叠层的自动聚焦 + 关闭时归还焦点）与 `Controls/ConfirmDialog.axaml.cs`（打开确认框时的初始焦点）。这样打开对话框/叠层的一瞬间不画焦点环，鼠标点击也不画，只有用户真按 Tab / 方向键时 Avalonia 才置上 `:focus-visible`。
  3. 动作带按钮是「无边框」的，而家族的无边框规则声明在 `Button:focus-visible` 之后、会把它盖掉，因此额外补一条 `StackPanel.confirm-actions Button:focus-visible` 把 FocusRing 显式写回来。**该条不是冗余**：实测删掉它，键盘导航时按钮的 `BorderBrush` 退回全透明、`BorderThickness` 0，焦点不可见（Avalonia 按声明顺序取最后一个匹配的 Setter）。
- 结论：可达性上满足 WCAG 2.4.7（键盘导航有可见焦点），同时消除了「打开对话框就出现一圈框」和鼠标点击后的焦点环。
- 没有被本决策覆盖的：FluentTheme 在少数控件**模板内部**绘制的焦点反馈（TextBox 底线、CheckBox/ToggleSwitch 模板边框等）本来就只在键盘导航时出现，无需处理。
- 基线影响（与上一版相比）：`confirm-dialog.png` 606 px（0.06%，包围盒 `x 749..813 / y 393..434`，即默认按钮上那圈自动焦点环消失）；`log-export.png` 272 px（0.03%，一处 36×36 的自动焦点环）；`toast.png` 第三次出现同容差无意图漂移，已复原；`settings-overlay` / `shell-default` / `progress-panel` 未变。
- 测试：`UiStyleContractTests.FocusVisual_IsKeyboardOnly`（框架矩形为空 + FocusRing 仍在 + 动作带补回规则声明在无边框规则之后 + 源码中不得出现 `Focus(NavigationMethod.Tab)`）、`UiStyleContractTests.Dialogs` 里 `OverlayFocusBehavior` 的源码契约改为断言 `Unspecified`；无头行为测试 `DialogActionButtons_ShowFocusRingOnlyOnKeyboardNavigation`（打开对话框 → 无 `:focus-visible`、无环；模拟 Tab 键 → `:focus-visible` + 环 alpha > 0）与 `FocusVisual_FollowsKeyboardNavigationOnly`。
- 踩坑记录：无头环境里 `Focus(NavigationMethod.Tab)` **不会**产生 `:focus-visible`（窗口激活也没用），必须用 `window.KeyPress(Key.Tab, …, PhysicalKey.Tab, "")` 模拟真实按键才能测这条行为；另注意 `dotnet build` 单个测试工程才会刷新它 bin 下的应用程序集副本，只跑 `build.ps1` 后 `dotnet test --no-build` 会用到旧 DLL（本次一度造成假绿/假红）。
- 明确记录的两处 Fluent 偏离：破坏性动作用 critical 填充而非 Accent（WinUI 没有内置危险按钮样式）、破坏性确认不设默认按钮（不让 Enter 一次带走破坏性操作）。

## 修订二（2026-09-25 同日）：焦点环厚度常驻预留，聚焦不再改变按钮宽度

- 问题（用户报告）：「对话框的按钮在有焦点环和没有的宽度是有区别的」。根因：焦点环用 `BorderThickness` 画，而按钮宽度是内容驱动的 —— 动作带按钮静止 0px（`flat-action` 为 1px）、聚焦变 2px，于是聚焦那一刻按钮变宽 2～4px，右对齐的动作带整体左移。这是修订一改出来的（把家族静止厚度从 1px 降到 0px，落差从 2px 变成 4px）。
- 决策：动作带内的按钮**常驻预留焦点环厚度**，聚焦只改颜色：
  - 新增作用域规则 `StackPanel.confirm-actions Button`：`BorderBrush` 透明 + `BorderThickness = Launcher.Border.Thickness.Focus`（2）。它同时承担「动作带按钮无可见边框」与「预留厚度」，覆盖全部四种动作（确认 / 危险 / 次要 / 主钮），包括不属于 `confirm-dialog-action` 家族的更新 / 日志导出 / 错误对话框按钮。
  - `StackPanel.confirm-actions Button:focus-visible` 只设 `BorderBrush`（FocusRing），**不再设厚度**；家族规则里的 `BorderBrush` / `BorderThickness` 声明随之删除，避免两处各写一遍。
  - 关闭钮 `.dialog-close` 不需要处理：它有固定 `Width` / `Height`，聚焦时厚度 0→1 只挤压内部内容区，外框不变。
- 代价（如实记录）：静止态每个动作按钮因此宽 2～4px（宽带按钮按内容驱动增长，窄按钮被 `MinWidth` 吸收）。这是「聚焦不跳」的必要成本；若要连这 2～4px 都不动，正确做法是改用 Avalonia 的 `FocusAdorner`（叠加层绘制、不参与布局）而不是描边，但那需要自定义 adorner 模板并做视觉目检，本轮未做。
- 仍未覆盖：动作带**之外**的按钮（设置区页脚、工具栏等）依旧是静止 1px / 聚焦 2px 的落差 —— 这是本次改动之前就有的行为，修它需要给所有按钮家族保留厚度或同样改用 adorner，属于另一个决策。
- 基线影响：`confirm-dialog.png` 1586 px（0.16%，包围盒 `x 742..891 / y 393..434`，动作带两个按钮因预留厚度略有位移）；`log-export.png` 620 px（0.06%，动作带底行）；`toast.png` 第四次同容差无意图漂移（同一处 224 px），已复原；其余基线未变。
- 测试：无头行为测试 `DialogActionButtons_KeepGeometryWhenTheFocusRingAppears`（键盘取焦前后逐个按钮的 `Bounds` 宽度 / 横坐标 / 高度必须完全相等 —— 直接钉住用户报告的现象）；`DialogActionButtons_ShowNoVisibleBorderAndReserveTheFocusRing`（描边 alpha 0 + 厚度 2）；契约测试 `FocusVisual_IsKeyboardOnly` 增加「预留厚度规则声明在静止样式之后、颜色规则在预留之后」与「聚焦规则不得再动 BorderThickness」两条守卫。