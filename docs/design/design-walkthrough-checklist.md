# 设计走查清单（Design Walkthrough Checklist）

> 状态：**落地**（随 P2 C 批交付；[ADR-008](adr/ADR-008-组件批次映射.md)）。
> 用途：P3 每表面完成时人工复核的实物载体（spec §10）；与 [design-system-spec.md](./design-system-spec.md) §4 状态矩阵、§8 豁免区共同维护。
> 复核方式：逐项走查并记录结果（✓ / ✗ / 备注）；新增表面或调整组件后更新本清单。

## 1. 组件状态矩阵（spec §4）

矩阵实物 = Debug 构建「设计画廊 → 组件状态矩阵」（3×6 以上：四型按钮 / 卡片含 Toast 卡 / 设置行 × normal / hover / pressed / disabled / focus-visible / invalid）。
走查时打开画廊逐格与生产样式对照：

| 组件 | normal | hover | pressed | disabled | focus-visible | invalid |
|---|---|---|---|---|---|---|
| Filled 按钮（`primary-action`） | ✓ | ✓ | ✓ | ✓ | ✓ | 不适用（留空） |
| Outlined 按钮（`flat-action`） | ✓ | ✓ | ✓ | ✓ | ✓ | 不适用（留空） |
| Text 按钮（`text-link`） | ✓ | ✓ | ✓ | ✓ | ✓ | 不适用（留空） |
| Error-filled 按钮（`danger-action`） | ✓ | ✓ | ✓ | ✓ | ✓ | 不适用（留空） |
| 卡片 / Toast 卡 | ✓ | ✓ | ✓ | ✓ | ✓ | 不适用（留空） |
| 设置行（Select 输入） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

- 状态层不透明度产品值：hover 8% / focus 12% / pressed 16% / disabled 内容 38%（`Launcher.StateLayer.*`；pressed 16% 为产品值，M3 官方 12%——记录性偏差，见 ADR-004 落地记录）。
- 状态反馈以「预混色状态变体」实现（`Primary.Hover/.Pressed`、`SecondaryContainer.Hover/.Pressed` 等），未采用叠加状态层机制（ADR-004 落地记录）。

## 2. 无障碍豁免区（spec §8，逐项人工复核）

豁免前提：该区文字叠于 scrim（`Launcher.TitleBar.Gradient` + `Launcher.Color.Overlay.Scrim*`），保证与任意壁纸色可读（最小 scrim 语义）。

| # | 豁免区 | 位置 | scrim 依据 | 复核 |
|---|---|---|---|---|
| 1 | 自绘标题栏文字 | `MainWindow.axaml` 标题栏 | `Launcher.Color.TitleBar.Gradient` | |
| 2 | 标题栏按钮（chrome 态） | `Button.chrome` | 同标题栏渐变 | |
| 3 | 右侧社交列 | `ItemsControl.social-actions` | 壁纸区域 scrim（`Overlay.Scrim.Md` 等） | |
| 4 | 底栏渐变/scrim 上的 on-dark 文字 | `Border.control-panel` / `TextBlock.on-dark-caption` | `Launcher.Color.ControlPanel.Gradient` + `Color.Overlay.Scrim.*` | |

单项复核要点：字体大小 ≥ 正文档、对比度在亮/暗两种壁纸下人工目测且≥可读阈值；避免在该区域引入纯 `Text.Secondary`（除非叠加强 scrim）。

## 3. 表面级走查（P3 顺序）

### 3.1 主壳（首页）——无既定蓝图，**搁置**（2026-08-25 用户撤销首页设计决策，ADR-012 历史记录）
- 仅维护既有功能：横幅/新闻/底栏控制/进度/安装面板不受影响。

### 3.2 设置页（ADR-013）
- 两列自顶到底：导航列 header（「设置」标题）+ 内容区标题行右端 ✕（`content-header-action`）。
- 导航选中 = `SecondaryContainer` 填充 + `OnSecondaryContainer` 文字 + leading icon，无指示条；hover/pressed 用 `.Hover/.Pressed` 变体。
- 内容区 = 纯列表 + 组间空档 + 行间 1px inset hairline（`Color.Card.Border`，左缘对齐 16px 文本缩进）。
- 覆盖层 = `Dialog.Background`（中性策略=种子跟随时由 scheme neutral 覆盖）+ `Radius.Lg` + `Elevation.Shadow.Lg`。
- 控件统一 Field 形态（`Field.Background`/`Field.Border`/`Radius.Md`/2px 聚焦环）；Field.Border 双档 ≥3:1（Light `#788EA7` / Dark `#5E7494`）。
- 底部操作带（取消 / 保存）语义与焦点默认落在安全操作。

### 3.3 对话框族（ADR-014）
- 确认/通知/更新/错误四类统一解剖：头部（icon + 标题 + ✕）→ 可滚动内容 → `dialog-footer` hairline 操作带（取消左、确认右；危险确认用 `danger-action`）。
- 无特殊强调的确认对话框默认焦点落在安全操作（`ConfirmDialog.SafeActionButton` 打开时聚焦）。
- 明示关闭路径完整：✕ 按钮 + 取消/次要操作按钮；不依赖手势或仅有遮罩交互的路径。

### 3.4 Toast（ADR-014）
- 无自动消失进度条；仅操作执行中显示 indeterminate 进度条（底部边缘，厚度 token）。
- 关闭按钮命中区 ≥36px（`Launcher.Control.Height.Setting`）；图标 16px。
- 严重性四色（成功/警告/错误/信息）与系列卡对比度 ≥3:1（契约测试锁定）。

### 3.5 设置向导（ADR-017）
- 模态 `DialogSurface`（920×560）内实验台居中单列解剖：进度行（向导标题 + `StepProgress` caption + 跳过钮）→ 居中单列内容（`wizard-step` ×5，标题 22px 居中，`Wizard.Content.MaxWidth` 520）→ Footer（上一步 outlined 居左 / 下一步·完成 filled 居右）。
- 下载源/代理选项 = `wizard-option` 单选列表行（最小行高 56、整行命中、悬停状态层、可见选中态）；路径状态 = `wizard-status-row`（图标 + 语义色文本，色彩非唯一信号）；复核 = `wizard-review-row` + hairline 分隔；语言 ComboBox 走 `ComboBox.setting-control` Field 形态（宽 280）。
- 完成确认态：最后一步（复核）即完成态，标题 Success 色，Footer 切"完成"，无庆祝动画、不缩放。
- 动效（ADR-016/017）：步骤切换 = 顺序换页——旧内容 125ms 加速淡出（禁命中）→ 中点换内容并复位滚动 → 新内容 125ms 减速淡入 + ±14px 方向滑入；快速连点最新状态生效；外壳遮罩 Fast 淡入、表面单次进出场；降动效瞬切换面并定格。

### 3.6 诊断/日志/资源面板（Q3 例外声明）
- 仅 token 兼容；列表维持 Fluent 基础模板；不做 M3 视觉重设计。

## 4. 契约门禁（自动化，改动后必须全绿）

- `UiStyleContractTests`（token 存在/旧键清零/静态族/禁裸值/覆盖层 Z 序/画廊矩阵）。
- `DesignTokenContrastTests`（AA 文本 ≥4.5:1 / UI ≥3:1 + 豁免清单）。
- Headless 套件（含黄金截图基线，阈值 diff；视觉漂移按 README 流程重生成基线并复核截图）。
- `.\scripts\Test-LocalizationContract.ps1`（新增/改键后）。
- `.\dev.ps1 ui`（XAML/样式改动后）；发布前 `.\verify.ps1`。
