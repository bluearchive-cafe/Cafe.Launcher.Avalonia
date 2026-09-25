# ADR-041: 资源面板 MD3 结构级重设计 —— 单卡三态、分段来源、行式条目

- 状态：**已接受**（2026-09-28，用户指示「实现资源面板 UI 的重新设计工作，不改动对话框外层骨架」；像素契约来自评审过的原型 `prototypes/resource-panel/index.html`，本 ADR 回答原型遗留的提案与开放问题）
- 背景：
  - 资源面板是 P3 表面序列中最后仍处于「仅 token 兼容」的常驻交互叠层之一（Q3；ADR-014 把它排除在重设计范围外）。本轮按原型把它的**内容解剖**压成三块：UID 单卡三态、资源单卡三行、消息/提示两条语调条；来源选择从 ComboBox 换成 MD3 分段按钮，条目开关从 CheckBox 换成 Switch。
  - 原型红线（全部沿用）：`ResourcePanelViewModel` 状态机、命令接线（Close / Refresh / Save / SaveManualUid / BeginEdit / CancelEdit / DismissHint / SetUidSource）、模态闸口 `ModalHost.IsResourcePanelInteractive`、`dialog-overlay` 类名与 Z 序、每会话一次的 UID 提示关闭行为、版本对齐折叠分支与失败保留上次数据行为、全部文案取自既有 `resourcePanel*` key（本轮不新增字符串）。
  - 对话框外层骨架不动：`DialogSurface Form="Panel"` 的头带（badge + 标题/副标题 + 关闭）、滚动正文、发丝底带（FooterLeading=刷新 / Footer=保存）、`Launcher.Layout.ResourcePanel.Width/Height` 尺寸律、MotionVisibility 通道全部保持 ADR-015 形态。
- 决策：
  1. **UID 单卡三态**：三张互斥 `dialog-card`（缺失 / 编辑 / 展示）合并为一张卡内的三个互斥可见面板（`IsResourcePanelUidMissing` / `IsResourcePanelUidEditing` / `IsResourcePanelUidPresent` 驱动，互斥关系不变）。三态共用同一个 `ManualResourcePanelUid`，视觉上只有一处 UID 输入。UID 展示值改绑裸值 `ResourcePanel.ResourcePanelUid`（等宽字体 + 字距），替代合成句 `ResourcePanelUidText`（VM 属性保留，仍是 VM 自己的本地化状态；视图不再渲染它，因此状态条契约里「不得渲染 UID 事实」的断言自然继续成立）。
  2. **UID 来源 = 分段按钮**：`RadioButton.segment-option` ×2（GroupName 互斥、`IsChecked` 经新转换器 `ResourcePanelSourceSegmentConverter` 与 `SelectedResourcePanelUidSource` 双向绑定，`ConverterParameter` 用 `{x:Static models:ResourcePanelUidSources.*}`）。选中段 = accent 家族（`Primary.Soft` 底 + `Primary` 字）——**不**用 `Launcher.Color.SecondaryContainer`（静态 M3 基线紫，与运行时 accent scheme 不同源，提案 3 采纳）；段内 16px 勾选图标仅在 `:checked` 显形，提供非颜色线索（提案 4 采纳带勾变体）。busy 时整组 `IsEnabled=!IsResourcePanelBusy`，禁用观感 = `StateLayer.Disabled.Content`。
  3. **资源条目 = 单卡三行**：三张独立 `dialog-card` 合并为一张卡，三行之间用发丝分隔线（显式 `Border.resource-row-divider`，与设置向导复核列表的 `wizard-review-divider` 同一手法——条目集合按 D11 按位对齐且恒为三项，行经共享 DataTemplate 渲染）。行 = 标题 + 状态 chip + 版本支撑行 + trailing Switch。资源面板的两张卡圆角升到 `Radius.Md`（12），做法是**新增资源面板专属类**叠加在 `dialog-card` 上，共享 `dialog-card` 类本身不动（提案 6 按作用域收窄采纳，其他视图不受影响）。
  4. **条目开关 = `ToggleSwitch.setting-toggle`**：设置语义而非多选；沿用全局 `ToggleSwitch:checked` 的 Primary 填充 + `OnPrimary` 旋钮规则，不自造模板（原型 token 清单里的 `Launcher.Component.Switch.*` 因此不需要）。**`IsOperable` 语义不变**（Ready or Waiting，见开放问题 2 的裁决）。
  5. **状态 chip**：图标 + 文案装进整圆 pill（高 24）。Ready = `Success` 前景 + 新 token `Success.Soft` 底（提案 1 采纳）；Loading / Waiting = `Text.Secondary` + `Content.Row` 底；Failed = `Danger` + `Danger.Soft` 底。状态→样式经 `ResourcePanelItem` 新增的四个只读表象布尔（`IsStatusLoading/Ready/Waiting/Failed`，随 `Status` 变更通知）映射成类，`ResourcePanelStatusToBrushConverter`（只染图标前景）删除。Loading 不做旋转动画：动画需要接入 motion-reduced 通道才能符合 §动效纪律，而「加载中」文案已承载状态语义，不值得为它开这个口。
  6. **消息/提示条加前导图标**（提案 5 采纳）：提示条 = `InformationOutline`（`Text.Info` 调）；消息条中性 = `CheckCircle`（`Text.Body` 调）、危险 = `AlertCircle`（`Danger` 调，非颜色线索补齐）。条形类体系不变：`info-strip` / `info-strip.danger` / `resource-panel-status` 及其契约值原样保留。
  7. **UID 展示值字阶**：复用 `Headline.Lg`（19px）而非新增 20px 字阶（原型两案取其一）；等宽 + `LetterSpacing.Lg`（Typography 家族既有刻度首次落地消费）。`uid-input` 同步改等宽 + `Title.Md`（16）+ 字距。
  8. **卡内动作按钮沿用统一 `dialog-action` 度量**（42px 高、`Dialog.Action.MinWidth`），不引入原型的 `btn-sm` 紧凑变体：对话框动作家族的统一度量由 `DialogActionButtonContractTests` 守护（本面板 6 枚动作钮计数不变），且「修改 UID」与编辑/缺失态的取消/保存同高，卡的三态行高一致。
- 原型遗留问题的裁决：
  1. **Waiting 行的版本行**：原型的前提已过时——`ResourcePanelItem.IsVersionAligned` 早已把 `"--"` 排除在「一致」之外，未加载行走的是「官方 -- / 本地化 --」两列分支而非「版本一致 --」。维持现状分支与呈现，不改。
  2. **Waiting 的 Switch 是否禁用**：**不禁用**，`IsOperable` 保持 `Ready or Waiting`。理由：Waiting 只是「暂无可启用内容」，但服务器可能返回 `IsEnabled=true` 的存量状态，禁用会让用户无法在维护期把它关掉；且原型为禁用态配的解释文案（「该资源暂无可启用内容」）没有既有 key，红线禁止新增字符串，无解释的禁用控件会被读成损坏。状态 chip（待维护）已携带状态语义。
  3. **Missing 态与提示条并存**：保留。提示条解释「UID 何时生成」（每会话可关闭），缺失卡承载「手动输入继续」的动作，两者互补；原型自己也把这列为待裁决而非既定方向。
  4. **面板高度**：`Launcher.Layout.ResourcePanel.Height`（592）是封顶不是定值，内容变矮时面板自然收缩（ADR-015 尺寸律），保持不变；`UiStyleContractTests` 对该值的钉住不动。
- 后果：
  - 新 token（App.axaml）：`Launcher.Radius.Full`、`Launcher.Component.Segmented.Segment.MinWidth/Padding`、`Launcher.Component.ResourcePanel.StatusChip.Height/Padding`、`Launcher.Component.ResourcePanel.Row.Padding`、`Launcher.Color.Success.Soft`（双主题）、`Launcher.Color.Dialog.Section.Divider`（双主题，正文内发丝线，弱于 `Card.Border`）；删除孤儿 token `Launcher.Component.ResourcePanel.UidSource.MinWidth`（ComboBox 移除后无引用）。
  - 新样式文件 `Views/Styles/ResourcePanel.axaml`（经 `MainWindow.Styles.axaml` 的 StyleInclude 挂载），承载 uid-card / resource-card / resource-row / status-chip / segmented / 条内分隔线；`TextBox.uid-input` 在原处补等宽字阶。
  - 契约与测试：`UiStyleContractTests.Dialogs` 现有三条资源面板契约原样成立（动作钮计数 31 不变、`BeginEditResourcePanelUidCommand` 唯一且无 `IsVisible`、状态条类名与绑定不变）；新增结构守卫（UID 单卡三态、单卡三行 + 分隔线、分段按钮替代 ComboBox、条目行用 ToggleSwitch、chip 状态类映射）；`ResourcePanelItem` 表象布尔与转换器的单元测试。
  - 黄金基线无影响：仓库没有资源面板基线，本轮也不触碰任何有基线的表面。
  - 已知偏离原型的三处（均有据）：卡内按钮 42px（统一度量，决策 8）；Waiting 开关不禁用（裁决 2）；Loading 无旋转动画（决策 5）。

## 修订（2026-09-28，用户运行实机后反馈两项）

1. **UID 提示条移除关闭钮 → 成为常驻说明行**。用户指示「去除横幅关闭按钮」，据此推翻本 ADR 红线中的「每会话一次的 UID 提示关闭行为」条款：提示条不再是可关闭的一次性横幅，而是与副标题同级的常驻说明（信息本身——「通过 Cafe 源首次启动游戏后才会生成 UID」——在 UID 缺失时依然是关键指引，常驻无害）。随行为退场的还有：`ResourcePanelViewModel.IsUidGenerationHintVisible` 与 `DismissUidGenerationHintCommand`（不可达即死代码，一并删除）、本地化 key `resourcePanelDismissUidHint`（四份 resx + Designer + LocalizationKeys 重生成）、headless 用例 `ResourcePanel_DismissUidHint_HidesBannerUntilNextSession`（替换为 `ResourcePanel_HintStrip_IsAPermanentNoteWithoutDismissAffordance`，钉住「提示条渲染且不含任何按钮」）。
2. **分段勾选图标改用 `IsVisible` 而非 `Opacity`**。用户指出「自定义」段左侧有一截空白：勾选图标此前以 `Opacity=0` 隐藏，透明但仍占位，未选中段因此多出「图标宽 + 间距」的空缺。改为样式切换 `IsVisible`，未选中段收回该空间；切换来源时的段宽跳变（约 20px）接受——M3 分段按钮本就允许带图标段与纯文字段并存。
