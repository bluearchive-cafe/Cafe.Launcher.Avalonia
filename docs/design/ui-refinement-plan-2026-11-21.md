# 关于、Toast 与远程内容面板细化方案

- 日期：2026-11-21
- 状态：**第一步（关于分区）与第二步（Toast）已实施**；远程内容面板待做
- 范围：三处**互不相干**的局部修补，不引入全局视觉重构，不动导航、Z 序、DI 与生命周期

本稿是实施前的设计确认件。视觉数值以本文为准，契约行为以
[CONTEXT.md](../../CONTEXT.md)、[设计系统规范](design-system-spec.md) 与相关 ADR 为准；
本稿与它们冲突处，都在第 7 节列为「需同步修改的契约」，不默认以本稿为据。

---

## 1. 共同约束

三处改动共同遵守以下四条，除此之外互不影响：

1. **只用现成的尺子。** 间距取 `Launcher.Spacing.*`（0/4/8/12/16/20/24/32/40），
   圆角取 `Launcher.Radius.*`（`Xs`=4、`Sm`/`Md`=8），控件高度取 `Launcher.Control.*`。
   三处 XAML 内**不再出现裸数字内边距**；确实缺失的档位先加 token 再引用。
2. **颜色只走语义 token。** 品牌强调色仍由 `CustomAccentColor` 驱动；不新增原始色值，
   原始色值只允许出现在 `App.axaml`。
3. **动态主题引用用 `{DynamicResource}`**，静态取值用 `{StaticResource}`，与规范 §2.1 一致。
4. **动效遵循 ADR-016 与根级减弱动效偏好**：新增过渡一律走 `MotionTokens`，
   减弱动效时退化为 `TimeSpan.Zero`。

---

## 2. 关于分区（`SettingsAboutSection`）

### 2.1 结论

关于仍是设置浮层的第六个分区，归属、导航、Z 序不变（ADR-018 范围条款）。
本轮只重排分区内部，共四处改动：

| # | 改动 | 目的 |
|---|---|---|
| A1 | 身份卡加入应用图标，形成「图标 + 身份文字 + 单一主操作」 | 提供视觉锚点，解决「一屏全是字」 |
| A2 | 官方网站、帮助文档移出身份卡，并入下方入口列表 | 身份卡不再像工具栏 |
| A3 | 构建信息值改等宽、整行可点复制，折叠区底部加「复制全部」 | 兑现「复制 Commit 去提 issue」 |
| A4 | 法律区重组为一组，新增「开源许可」「第三方声明」两个入口 | 说明页的完整性 |

### 2.2 版式

```
┌──────────────────────────────────────────────────────────┐  分区内容宽 = 设置页内容列
│ ┌──────────────────────────────────────────────────────┐ │
│ │ ┌────┐  Cafe Launcher                    ← 名称 20/600│ │  身份卡
│ │ │ 48 │  Version 1.2.3 (built 2026-11-20)  ← 12 次级  │ │  padding 24
│ │ │icon│  <aboutDescription>                ← 14 正文  │ │  间距 16
│ │ └────┘                                               │ │
│ │        ┌──────────────┐                              │ │
│ │        │  检查更新     │  ← primary-action, 40 DIP   │ │
│ │        └──────────────┘                              │ │
│ └──────────────────────────────────────────────────────┘ │
│                                                            │
│  常用入口                                                  │  分组标题 14/600
│ ┌──────────────────────────────────────────────────────┐ │
│ │ 官方网站                                     ›       │ │  行高 40
│ │ 帮助文档                                     ›       │ │  左右 16
│ │ GitHub 仓库                                  ›       │ │  1 DIP 分隔
│ │ GitHub Release                               ›       │ │
│ │ 问题反馈                                     ›       │ │
│ └──────────────────────────────────────────────────────┘ │
│                                                            │
│  贡献者                                                    │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ (40) gytxtx          …                               │ │  行高 64
│ │ (40) MF-Dust         …                               │ │
│ └──────────────────────────────────────────────────────┘ │
│                                                            │
│  特别感谢                                                  │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ (40) Avalonia / CommunityToolkit.Mvvm / …            │ │  同上
│ └──────────────────────────────────────────────────────┘ │
│  <aboutCommunityThanks>                        ← 普通文本 │
│                                                            │
│  ▸ 构建与运行信息                                          │  Expander，默认折叠
│    ┌──────────────────────────────────────────────────┐   │
│    │ 提交           1a2b3c4                    ⧉      │   │  行高 40，等宽值
│    │ 构建配置       Release                    ⧉      │   │
│    │ 运行时         .NET 10.0.0 (win-x64)      ⧉      │   │
│    │ Avalonia       12.x                       ⧉      │   │
│    │ 平台           Windows 11 26100 x64       ⧉      │   │
│    │                              [复制全部]          │   │
│    └──────────────────────────────────────────────────┘   │
│                                                            │
│  法律与许可                                                │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ 源代码版权 © … 采用 MIT 许可证授权。  隐私政策       │ │  行内链接保持 2 个
│ │ 默认背景版权 …                        查看原作       │ │
│ │ 开源许可                                     ›       │ │  新增
│ │ 第三方声明                                   ›       │ │  新增
│ └──────────────────────────────────────────────────────┘ │
│  <aboutDisclaimerText>                         ← 12 次级 │
└──────────────────────────────────────────────────────────┘
```

### 2.3 数值

| 元素 | 规格 |
|---|---|
| 身份卡 | `Radius.Md`、1 DIP `DividerStrokeColorDefault`、内边距 24、图标与文字间距 16 |
| 应用图标 | 48 DIP，圆角 8，按 128 px 源缩放，不描边不投影 |
| 产品名 | 20 / SemiBold / `Text.Primary` |
| 版本 caption | 12 / `Text.Secondary` |
| 副标题 | 14 / `Text.Body` |
| 主操作 | 最小高 40、圆角 8、水平内边距 20、14 字号（ADR-018 2026-11-01 修订值） |
| 入口行 | 最小高 40、左右 16、右侧 16 DIP chevron、整行可点、`focus-visible` 描边 2 DIP |
| 贡献者 / 感谢行 | 最小高 64、上下 8、左 16、头像 40 圆（现状保留） |
| 构建信息行 | 最小高 40、左右 16；键 14 次级、值 13 等宽 `Typography.FontFamily.Monospace` |
| 复制反馈 | 复用现有 Toast（`Brief`），文案「已复制」 |

### 2.4 交互

- **点击构建信息行**：把该行的值写入剪贴板并弹 `Brief` 成功 Toast；值区域右端常驻一个
  16 DIP 复制图标，悬停行时由 `Text.Secondary` 提亮至 `Text.Primary`。
- **复制全部**：拼成 `Key: Value` 多行文本，含产品名与版本首行，一次性写入剪贴板。
- **开源许可 / 第三方声明**：整行打开文档查看器（见 2.5），不弹浏览器。
- 检查更新的结果呈现**不变**：仍由 `LauncherUpdatePresenter` 决定 toast 或更新对话框。

### 2.5 实现前提（已核实）

- **图标**：`src/Cafe.Launcher.UI/Assets/app-icon.png` 为 256×256，已作为 `AvaloniaResource`
  嵌入（`Cafe.Launcher.UI.csproj:25`），`MainWindow.axaml:18` 正用作窗口图标。
  无需新增资产，`Source="/Assets/app-icon.png"`，`Stretch="Uniform"`，48 DIP 显示。
- **契约变更**：`UiStyleContractTests.Settings.cs:572-573` 断言分区内不得出现 `Image` /
  `about-app-icon`；本轮改为「恰好一个 `about-app-icon`，且 `Source` 指向该资源」，
  并在 ADR-018 补实施记录。
- **许可文件**：`LICENSE` 与 `THIRD-PARTY-NOTICES.md` **当前既未嵌入程序集，也不随普通发布
  复制**（仅 `scripts/Build-Distribution.ps1:218-220` 打发行包时拷贝）。必须先按
  `TERMS.md` / `PRIVACY.md` 的既有方式（`Cafe.Launcher.UI.csproj:27-28` 的
  `Link="Assets\Legal\..."`）嵌入 `Cafe.Launcher.UI`，否则运行期读不到。
- **查看器**：`RemoteMarkdownView`（`Controls/RemoteMarkdownView.cs`）可渲染 GFM 长文档，
  `DialogSurface` 自带滚动视口（`Controls/DialogSurface.axaml:33-37`），链接仅放行
  `http/https/mailto`。新增第二个查看器实例会打破 `UiStyleContractTests.Dialogs.cs:36`
  的 `Assert.Single`，需改为 2。

### 2.6 法律文档对话框

新对话框只做一件事：显示一篇内嵌 Markdown 文档。

```
┌ 550 DIP ─────────────────────────────────────┐
│  开源许可                                  ✕ │
│ ─────────────────────────────────────────── │
│  MIT License                                 │  DialogSurface Form="Panel"
│  Copyright (c) 2026 …                        │  滚动视口由 DialogSurface 提供
│  …                                           │  RemoteMarkdownView
│ ─────────────────────────────────────────── │
│                                       [关闭] │  动作按钮 28 → 29
└──────────────────────────────────────────────┘
```

- `ModalKind` 新增 `LegalDocument`，`ShellModalCatalog` 注册两侧：`License` 与
  `ThirdPartyNotices`，标题分别取 `licenseDialogTitle` / `thirdPartyNoticesDialogTitle`。
- 文档读取扩展 `Services/AgreementDocuments.cs`（现只读 `TERMS`/`PRIVACY`），
  新增 `License`、`ThirdPartyNotices` 两个成员，沿用同一套 `avares://` 读取与缓存。
- 唯一动作是「关闭」，`esc` 与点击遮罩退出，与其它面板式对话框一致。

---

## 3. Toast 通知卡

### 3.1 结论

**行为契约全部冻结**：右上角、392 DIP 宽、最多 4 条、同 key 合并计数、`Brief/Medium/Extended`
= 4/6/8 秒、悬停与键盘聚焦暂停、有动作不自动消失、动作执行中显示不定进度。
本轮只改**严重级别的表达方式**与卡片排版。

### 3.2 问题

当前 ADR-070 的形态是「整卡实心语义底色 + 同色 1 DIP 描边」。它解决的是「半透明外壳
在壁纸上显脏」，但副作用是四条 severity 会在屏幕上堆成四块纯色彩块——这正是 M3 的
语义色表达习惯，也是「不贴 Fluent」的观感来源。

### 3.3 目标形态

中性卡片 + 左侧 4 DIP 语义条，severity 只体现在条、图标与标题前的层级上。

```
┌─┬──────────────────────────────────────────────────────┐
│ │  ⓘ  警告                                      ✕     │  ← 条 4 DIP（全高）
│ │      消息正文，最多 3 行后截断，悬停可展开            │
│ │                                                      │
│ │                                   [次要]  [主要]      │
│ │ ──────────────────────────────────────────────       │  ← 仅执行动作时
└─┴──────────────────────────────────────────────────────┘
```

### 3.4 数值

| 元素 | 规格 | 变化 |
|---|---|---|
| 卡片宽 | 392 | 不变 |
| 卡片底 | `SolidBackgroundFillColorBase` | **改**（原为 severity 实色） |
| 描边 | 1 DIP `DividerStrokeColorDefault` | **改** |
| 圆角 | `Radius.Md`（8） | 不变 |
| 语义条 | 左缘 4 DIP、全高、随 severity 取色，左侧两角随卡片圆角内收 | **新增** |
| 阴影 | 不加阴影，只用 1 DIP 描边 | **实施期修正**：Setter 无法消费字符串型阴影资源 |
| 内边距 | 左 16（含条后净距 12）、右 16、上下 12 | 微调 |
| 图标 | 20 DIP，颜色同语义条 | 改 |
| 标题 | 14 / SemiBold | 不变 |
| 消息 | 14 / `Text.Secondary`，行高 20，最多 3 行 | 新增截断 |
| 动作区 | 行高 32、顶部间距 12、右对齐 | 不变 |
| 进度条 | 3 DIP、贴底内缩 1 DIP | 不变 |
| 入场 | 透明度 + 右移 12 DIP / `FastDuration` | 不变 |

语义条取色：Info → `InfoBarInformationalSeverityBackgroundBrush`，
Success/Warning/Error 同理取对应 `FAInfoBar` 语义画刷；图标同色。
深色主题下不额外覆盖（`App.axaml` 里为实心底色准备的 `#F7F7F7` / `#2E2E2E` 覆盖可删除）。

### 3.5 截断与展开（实施期撤销）

原计划为 3 行截断 + 悬停展开，落地时发现两处硬约束：

1. 截断需要在模板内改 `TextBlock#Message` 的 `MaxLines`，而 `/template/` 选择器带属性 Setter 被
   `ProductMarkup_UsesFluentControlTemplates` 拦截（ADR-068）。
2. 同理不能把模板底色按严重级覆盖成透明；改为把 `FAInfoBar` 固定到资讯级
   （`Severity="Informational"` + `IsIconVisible="False"`），中性表面由资讯级底色提供。

因此**不实现 3 行截断**，消息长度仍由调用方负责。配套约束保留：安装、修复、更新失败这类
消息必须写成「结论 + 查看日志按钮」，不得把原始异常或文件路径直接铺进正文。

---

## 4. 首页远程内容面板

### 4.1 结论

仍是一块 476 DIP 固定宽、左下贴底的强玻璃面板，四段纵向排列。本轮改三段、删一段：

| 段 | 改动 |
|---|---|
| 公告条 | 裸文字 → 可折叠语义条，默认展开、最多 2 行，带展开箭头 |
| 横幅 | `Uniform` → `UniformToFill` 居中裁切，高度仍 220 DIP |
| 资讯标签列表 | 行高与分隔线对齐 token，其余不变 |
| 社交入口 | 不改；二维码字段确认**不实现**，从取值处移除 |

### 4.2 版式

```
┌─ 476 DIP ────────────────────────────────────────────┐
│ ┌─┬────────────────────────────────────────────────┐ │
│ │ │ 公告                                     ⌄     │ │  公告条：中性底 + 4 DIP 语义条
│ │ │ 维护通知：11/22 03:00–06:00 进行例行维护…      │ │  默认展开，最多 2 行
│ └─┴────────────────────────────────────────────────┘ │  max-height 96
│                      ↕ 12                              │
│ ┌────────────────────────────────────────────────────┐ │
│ │                                                    │ │
│ │              横幅 220 DIP（居中裁切）              │ │  上圆角 8
│ │                                                    │ │
│ │ ◀                                            ▶    │ │  控制件 32 DIP
│ │              ● ○ ○                                 │ │  圆点 6 DIP
│ └────────────────────────────────────────────────────┘ │  下圆角 0（与列表拼接）
│ ┌────────────────────────────────────────────────────┐ │
│ │ 新闻 │ 公告 │ 活动          ← TabControl           │ │  上圆角 0
│ │ ────────────────────────────────────────────────── │ │
│ │ 标题一行…                               2026/11/20  │ │  行高 36
│ │ 标题一行…                               2026/11/18  │ │
│ │ 标题一行…                               2026/11/17  │ │
│ │ ────────────────────────────────────────────────── │ │
│ │                      更多 ⌄                        │ │  视口 184
│ └────────────────────────────────────────────────────┘ │
│                      ↕ 12                              │
│  ◯  ◯  ◯  ◯  ◯                         官网 →        │  社交 32 DIP
└──────────────────────────────────────────────────────┘
```

### 4.3 关键规则

- **横幅裁切**：`Stretch="UniformToFill"`、`HorizontalAlignment/VerticalAlignment="Center"`，
  容器固定 220 DIP。图片比例与容器不一致时**裁切而非留白**；在设计规范中写明
  「官方横幅按 220 DIP 高度居中裁切，不保证完整显示」。
  同步修正 `BannerImageDecoder` 注释中「UniformToFill 全宽」与实际代码不符的历史漂移。
- **拼接**：横幅与资讯列表同时存在时保持既有 `joined` 规则（中间两角 0、外侧 8）。
  公告条与横幅之间**不拼接**，留 12 DIP。
- **公告条**：中性底（`Launcher.Color.Content.Row`）+ 左缘 4 DIP 语义条，与 Toast 同一套语言；
  展开态最多 2 行，点击标题行整行切换折叠；折叠态固定单行高度 44。
  折叠状态不做持久化，每次启动回到展开。
- **资讯行**：标题 1 行截断、日期右对齐 12 号次级；悬停 `StateLayer.Hover`；整行可点。
- **社交行**：保持图标按钮，删除从未渲染的 `ImageUrl`/二维码取值路径。

---

## 5. 状态矩阵

| 场景 | 关于 | Toast | 面板 |
|---|---|---|---|
| 加载中 | — | — | 整面板 `LoadingOverlay`（不变） |
| 加载失败 | — | — | 仅当 `RuntimeState == RemoteUnavailable` 显示错误段（不变） |
| 空内容 | — | — | 面板隐藏（不变） |
| 悬停 | 行底色 `Hover` | 暂停计时 | 轮播暂停 |
| 键盘聚焦 | 2 DIP 焦点描边 | 暂停计时 | 轮播暂停 |
| 减弱动效 | 分区入场退化为无动画 | 立即移除，不做位移动画 | 停止自动轮播（不变） |
| 深色主题 | 全部走语义 token | 中性底 + 语义条自动适配 | 强玻璃底不变 |

---

## 6. 无障碍

- 应用图标 `AutomationProperties.Name` = 产品名；纯装饰则置空。
- 构建信息行可聚焦，`AutomationProperties.HelpText` 说明「回车复制该值」。
- 公告条展开/折叠按钮暴露 `Expanded`/`Collapsed` 状态与本地化名称。
- Toast 区域 `LiveSetting="Polite"`、语义条纯装饰（不参与朗读），严重级别由标题文本承担——
  因此**标题不得为空**，缺省时仍取本地化的 severity 名称（现状逻辑保留）。
- 横幅裁切后仍保留原图替代文本。

---

## 7. 契约影响清单

### 7.1 需要同步修改的测试

| 测试 | 原因 |
|---|---|
| `UiStyleContractTests.Settings.cs` / `AboutSection_WithRedesignedLayout_…` | 允许 `Image`/`about-app-icon`；新增入口行与复制按钮 |
| 同上 / `SettingsAboutAndAdvancedActions_UsePurposeBasedOrderAndExclusiveOwnership` | 文本顺序变为：身份 → 入口（含官网/帮助）→ 贡献者 → 感谢 → 社区致谢 → 构建信息 → 版权/隐私/背景版权/原作 → 开源许可 → 第三方声明 → 免责 |
| 同上 / `AboutSection_LegalLinks_ShareRowsWithTheirCopyrightText` | 保持「恰好 2 个 `inline-legal-link`」不变（新增两项用行式入口，不加行内链接） |
| `UiStyleContractTests.ToastLog.cs` / `ToastCards_UseSolidInfoBarSemanticSurface` | 改为「中性底 + 语义条」，断言目标从「卡片底色 = InfoBar 语义底色」转为「语义条颜色 = 语义底色」 |
| 同上 / `ToastCards_DoNotUseOverlappingBoxShadows` | 允许单层阴影，禁止叠加 |
| `MainWindowHeadlessTests.Settings.cs` | 入口行数量、图标存在性、复制命令 |
| `MainWindowHeadlessTests.RemoteContent.cs` | 横幅 `Stretch` 断言由 `Uniform` 改 `UniformToFill` |
| `UiStyleContractTests.Dialogs.cs:36` | `Assert.Single(RemoteMarkdownView)` → 2（新增法律文档对话框） |
| `DialogActionButtonContractTests` | 对话框动作按钮总数 28 → 29（新增「关闭」） |
| 黄金基线 | 重建 `settings-about-zh.png`、`settings-about-credits-zh.png`、`toast.png`、`toast-actions.png`、`home-news.png`，与新版式同一提交；`shell-default.png` 预期不变（默认捕获无远程内容） |

### 7.2 需要同步修改的文档

| 文档 | 修改点 |
|---|---|
| `docs/design/design-system-spec.md` §4.3 | Toast 形态描述改为中性底 + 语义条；删去「底色即 FAInfoBar 实心语义底色」 |
| 同上 §4.6 | 关于分区顺序、图标、复制、许可入口；顺带修掉「32 DIP」与 ADR-018「40 DIP」的漂移（统一为 40） |
| 同上 §4.x / 第 50 行 | 横幅高度文档值 160 → 220；补「居中裁切」规则 |
| 同上 §4.x 资讯面板条款 | 公告条形态、拼接规则 |
| `docs/design/adr/ADR-070-…md` | 增补修订：severity 表达改为语义条，行为契约不变 |
| `docs/design/adr/ADR-018-…md` | 补实施记录：图标进入身份卡、入口列表合并 |
| `CONTEXT.md` | Toast 表面描述（若含「实心语义底色」表述）；远程面板新增公告条契约 |
| `UBIQUITOUS_LANGUAGE.md` | 新增「公告条」「语义条」两条术语（中/繁/日/英） |
| `docs/cafe-launcher/` 与 `docs/en/cafe-launcher/` | 关于页新增许可入口、构建信息可复制；横幅裁切属视觉细节，不写用户文档 |

### 7.3 新增本地化键（四个 `.resx` 同步、按字母序、经 `Generate-LocalizationCatalog.ps1 -Check`）

`aboutOpenSourceLicense`、`aboutThirdPartyNotices`、`aboutCopyAllBuildInfo`、
`aboutCopyBuildInfoRowHint`、`aboutCopied`（若已有通用「已复制」键则复用）、
`noticeExpand`、`noticeCollapse`、`licenseDialogTitle`、`thirdPartyNoticesDialogTitle`。

---

## 8. 风险

1. **`FAInfoBar` 透明底**：需覆盖 `/template/ Border#ContentRoot.Background` 为
   `Transparent`（仅设控件 `Background` 无效，模板内 severity 样式会覆盖）。图标由
   `IsIconVisible` 控制，不受底色影响。验收时需实测四类 severity 在浅色/深色下的
   图标与关闭按钮对比度。
2. **黄金基线全量重写**：`pwsh ./test.ps1 -UpdateGolden` 会重写**所有**基线，
   必须用 `git status` 逐张确认，只保留三处预期变化。
3. **动作按钮计数与查看器数量**是全局断言，改动前须 grep 全部引用点，避免遗漏第三处断言。
4. **TOAST 对比度**：中性底上一旦去掉整卡换色，warning（`#9D5D00`）在深色主题的
   4 DIP 细条上可辨识度下降，需按四类 severity 逐一取色验证，必要时改用更亮的变体。
5. **`UniformToFill` 裁切**可能切掉官方横幅的关键文字（部分横幅把活动名压在边缘）；
   上线前抽样最近若干期横幅确认，必要时把内容安全区写进设计规范。

---

## 9. 实施顺序

1. 关于分区（隔离度最高，契约改动集中在一条测试）。
2. Toast（涉及 ADR-070 修订与三条样式契约）。
3. 远程内容面板（涉及一条 `Stretch` 断言与 doc 漂移修正）。
4. 三处完成后统一重建黄金基线，再跑 `pwsh ./verify.ps1`。

---

## 10. 逐文件改动清单

### 10.1 关于分区

| 文件 | 改动 |
|---|---|
| `src/Cafe.Launcher.UI/Views/SettingsAboutSection.axaml` | 身份卡加 `Image.about-app-icon`；官网/帮助文档从动作行移入入口列表；构建信息行加复制图标与「复制全部」；法律区加「开源许可」「第三方声明」两行，免责声明移到底部 |
| `src/Cafe.Launcher.UI/Views/MainWindow.Styles.axaml` | `about-*` 样式族：身份卡改为图标 + 文本网格，入口行/构建行 hover 与 `focus-visible`，等宽值样式，复制图标状态 |
| `src/Cafe.Launcher.UI/Features/Shell/ShellViewModel.cs` | 构建信息逐行复制与「复制全部」命令及拼装文本；复制后发 `Brief` Toast |
| `src/Cafe.Launcher.UI/ViewModels/WindowChromeViewModel.cs` | 新增打开法律文档的命令（与现有 `OpenPrivacyPolicy` 等并列） |
| `src/Cafe.Launcher.UI/ViewModels/ModalKind.cs` | 新增 `LegalDocument` |
| `src/Cafe.Launcher.UI/Features/Shell/ShellModalCatalog.cs` | 注册「开源许可」「第三方声明」两个文档模态 |
| `src/Cafe.Launcher.UI/ViewModels/DialogsViewModel.cs` | 法律文档的显示状态、标题与 Markdown 内容 |
| `src/Cafe.Launcher.UI/Services/AgreementDocuments.cs` | 新增 `License`、`ThirdPartyNotices` 读取 |
| `src/Cafe.Launcher.UI/Views/MainWindowDialogsOverlay.axaml` | 新增一个 `Grid.dialog-overlay` + `DialogSurface` + `RemoteMarkdownView`，声明 `ZIndexDialog` |
| `src/Cafe.Launcher.UI/Cafe.Launcher.UI.csproj` | 按 `Assets\Legal\` 方式新增嵌入 `LICENSE`、`THIRD-PARTY-NOTICES.md` |

### 10.2 Toast

| 文件 | 改动 |
|---|---|
| `src/Cafe.Launcher.UI/Views/MainWindowToastOverlay.axaml` | 卡片外包一层 `Grid`，左侧加语义条 `Border`；文案区加 3 行截断与悬停展开 |
| `src/Cafe.Launcher.UI/Views/Styles/Toast.axaml` | 中性底 + 1 DIP 描边 + 单层阴影；四类 severity 的语义条颜色类；删除「卡片底色 = InfoBar 语义底色」的描边规则 |
| `src/Cafe.Launcher/App.axaml` | 新增语义条宽度 token；**保留** `InfoBarInformationalSeverityBackgroundBrush` 覆盖（其它应用内 InfoBar 仍在用） |
| `src/Cafe.Launcher.UI/Features/GameOperations/GameOperationsViewModel.Task.cs` | 失败消息改写为「结论 + 查看日志」 |
| `src/Cafe.Launcher.UI/Features/GameOperations/GameOperationJourney.cs` | 同上 |
| `src/Cafe.Launcher.UI/Services/LauncherUpdatePresenter.cs` | 同上 |

### 10.3 远程内容面板

| 文件 | 改动 |
|---|---|
| `src/Cafe.Launcher.UI/Views/MainWindow.axaml` | 公告条改为带语义条的可折叠行；横幅 `Stretch="UniformToFill"` + 居中；删除社交项的二维码绑定残留 |
| `src/Cafe.Launcher.UI/Views/MainWindow.Styles.axaml` | `notice-card` 新形态、`banner-*` 与拼接圆角复核 |
| `src/Cafe.Launcher.UI/Views/Styles/RemoteContent.axaml` | 公告条折叠态、圆点、社交按钮间距 |
| `src/Cafe.Launcher.UI/ViewModels/RemoteContentViewModel.cs` | 新增公告折叠状态与切换命令；移除 `ImageUrl` 二维码取值路径 |
| `src/Cafe.Launcher.UI/Helpers/BannerImageDecoder.cs` | 修正「UniformToFill 全宽」的历史注释，与实际裁切规则一致 |
| `src/Cafe.Launcher/App.axaml` | `NoticeCard.MaxHeight` 160 → 96；新增公告条折叠行高 token |

### 10.4 本地化与文档

| 文件 | 改动 |
|---|---|
| `src/Cafe.Launcher.UI/Constants/LocalizationKeys.cs` | 新增第 7.3 节列出的 9 个键 |
| `src/Cafe.Launcher.UI/Resources/LauncherStrings{,.zh-Hans,.zh-Hant,.ja}.resx` | 四语言按字母序补齐 |
| `scripts/Generate-LocalizationCatalog.ps1` 产物 | 重新生成 `LauncherResourceCatalog.Generated.cs` |
| `docs/design/design-system-spec.md` | §4.3 Toast 形态、§4.6 关于分区、第 50 行横幅高度 160 → 220、资讯面板条款 |
| `docs/design/adr/ADR-070-*.md` | severity 表达修订记录 |
| `docs/design/adr/ADR-018-*.md` | 图标与入口列表的实施记录 |
| `CONTEXT.md` | Toast 表面描述、远程面板公告条契约 |
| `UBIQUITOUS_LANGUAGE.md` | 「公告条」「语义条」四语言术语 |
| `cafe-docs` 仓库 `docs/` 与 `docs/en/` | 关于页许可入口与构建信息复制；`npm ci` + `npm run docs:build` 校验 |

### 10.5 验证

`pwsh ./test.ps1`（单测 + Headless）→ `pwsh ./test.ps1 -UpdateGolden` 重建五张基线并逐张确认 →
`pwsh ./verify.ps1`。构建把警告视为错误，`build.ps1` 不覆盖更新器，更新器单独构建。
