# 仓库审计报告（当前状态）

> 本报告为 **full 全量重审**：按用户指令忽略既有审计报告与台账，从零完成仓库发现、风险画像、六域审计通道与发现验证。旧报告（2026-09-12 @ `490c2e5`）与旧台账（101 项）已归档至 `.repository-audit/history/`。

## Audit Metadata

- 日期：2026-09-13（**delta 复核：同日 @ `1619d35`**）
- Commit：基线 `ab156dd`，复核时点 `1619d35`（`main`；最新已发布 tag `v1.1.0-beta.9`）
- 模式：基线 **full** + 同日 **delta 复核**（`ab156dd..1619d35`，19 个提交，架构评审候选 01-11 落地）；复核对全部 17 项开放发现逐项重验
- 上一基线：不适用（full 为全新台账；历史报告见 `history/`）
- 范围：生产源码 219 个 `.cs`（≈32.8k 行）+ 28 个 `.axaml`、174 个测试文件（≈42k 行）、CI 两工作流、打包/安装器脚本、文档契约
- 项目画像：desktop-launcher（`.agents/skills/repository-audit/profiles/desktop-launcher.md` 按仓库证据调整）

## Executive Summary

仓库健康状况：**良好且纪律性显著高于同规模项目**。文档化规则（架构边界、模态隔离裁定、本地化契约、设置兼容、供应链锁定）与实现逐条核对一致；下载完整性、文件系统边界、进程启动、外部链接四个 desktop-launcher 关键风险面全部有真实防御且有测试保护。未发现 Critical 或 High 级别问题。

开放发现（2026-09-13 delta 复核后）：

- Critical：0
- High：0
- Medium：4（复核解决 2：AUD-ARCH-001、AUD-TEST-001）
- Low：10（另 1 项 Informational；AUD-TEST-003 证据经复核修正并收窄）

自上次审计解决项：AUD-ARCH-001（ShellLifecycle 模态注册/Escape 表 → ModalRegistrar）、AUD-TEST-001（HttpMessageHandler 散桩 → RemoteHttpTransport 接缝 + 共享 StubRemoteHttpTransport），证据见「Resolved Findings」。

需要决策的事项：

1. **更新校验的自愈成本（AUD-PERF-001）**：每次更新对全部未变更文件做单线程全量 CRC64 重读，是有文档记录的自愈设计，但在大安装 + 小更新场景是主要等待成本。是否引入修复通道已有的「见证哈希」摊销机制，属于架构决策。
2. **非 Windows 测试执行（AUD-CI-001）**：CI 从不在 Linux/macOS 跑测试，约 6 个平台分支在任何机器上都不执行，而产品实际分发 Linux/macOS 包。是否增加可选 Linux 测试 job 属于 CI 投入决策。
3. **官方协议兼容性依据入库（AUD-MAINT-002）**：436 行协议对比分析（与官方启动器共用游戏目录的逐项核对）仅存在于未跟踪工作树文件中，丢失即失去兼容性论证。

最重要的风险/行动：

1. **AUD-PERF-001**（Medium）— 更新安装阶段的整体重哈希：保留自愈语义的前提下用见证哈希/并行化摊销成本。
2. **AUD-CI-001 + AUD-TEST-003**（Medium + Low）— 非 Windows 分支「看似有覆盖、实则无处执行」；修隐藏跳过并增加可选 Linux 测试 job，二者同一根因。
3. **AUD-ARCH-002**（Medium）— 剩余的最高变更热点（`MainWindow.axaml.cs` 663 行，基线后未动）：操作表面动效套件（:270-420）仍内嵌窗口 code-behind，在下一个动它的功能前先抽协作者（姊妹项 AUD-ARCH-001 已于复核期间解决）。
4. **AUD-MAINT-002**（Low）— 提交或明确弃置官方协议对比分析文档。

## Changes Since Previous Audit

基线 full 审计（`ab156dd`）后同日发生 19 个提交（至 `1619d35`），主体为架构评审候选 01-11 的落地：RemoteHttpTransport 深模块三步走（f123429..f123429，收编 JSON 缓冲与五个远程调用方）、下载接口拥有传输与临时文件状态机（候选 08）、GameOperationPolicy（候选 04）、命名停止意图（候选 05）、检查点终局清除单点化（候选 07）、确认对话框收敛到 ConfirmationDialogViewModel（候选 09）、轮播时钟接缝 CarouselTimer（候选 10）、声明式 ModalRegistrar（候选 06，解决 AUD-ARCH-001）、诊断静态接缝接口化（候选 11）、ADR-021/022/023 与文档全量重写。**delta 复核结论**：重构与既有审计裁定无冲突——模态隔离裁定逐字保持（七个 `Is*Interactive` 门在 `ModalHostViewModel.cs:26-44`，对话框层仍纯可见性标志驱动、无交互闸口），URL 校验器与每跳复验随传输层迁入 `RemoteHttpTransport` 而语义不变；各发现重验结果已逐项更新至本报告。另：仓库根目录新增未跟踪的 `architecture-review-2026-09-13.html`（本次候选评审的工作产物，与 AUD-MAINT-002 同类的入库待决项）。依赖扫描维持基线结论（本复核未引入新包版本变更）。

## Delta Re-verification (2026-09-13 @ `1619d35`)

对基线（`ab156dd`）的 17 项开放发现逐项重验，方法：`git diff ab156dd..HEAD`（50 个生产文件、104 个测试文件变更）映射到各发现证据文件，逐文件重读现行源码/测试并复核关键行。结果汇总：

| 发现 | 复核结论 | 关键证据（现行） |
| --- | --- | --- |
| AUD-PERF-001 | 仍成立 | `DownloadExecutor.cs:285-287` 仍串行 `foreach` + 逐文件 `ComputeFileAsync`；`DownloadPlan.cs` 仍「Empty for the install/update plans」 |
| AUD-ARCH-001 | **已解决** | `2fe3565` → `ModalRegistrar`（95 行）+ `ModalRegistrarTests`；ShellLifecycle 775 行 |
| AUD-ARCH-002 | 仍成立 | `MainWindow.axaml.cs` 663 行，基线后零改动（git diff 为空） |
| AUD-TEST-001 | **已解决** | 桩 53 处/17 文件 → 9 处/1 文件（全在 `RemoteHttpTransportTests.cs`）；共享 `StubRemoteHttpTransport` 落地 |
| AUD-CI-001 | 仍成立 | `build.yml:18` 仅 windows-latest；`release.yml:182/263` 测试仍只在 Windows Release 跑 |
| AUD-TEST-002 | 仍成立 | 无 `SettingsViewModelTests`；`ShellLifecycleTests` 对更新确认接线零覆盖 |
| AUD-PERF-002 | 仍成立 | `GameDownloadService.cs:163` `.GetAwaiter().GetResult()`；`DownloadSession.cs:520,527` `LogSync` |
| AUD-PERF-003 | 仍成立 | `FileDownloadService.cs:145` 仍循环体内 `new byte[1024*256]` |
| AUD-PERF-004 | 仍成立 | `RemoteContentViewModel.cs:159/:515` 每次 Apply 仍销毁+重解码全部横幅 |
| AUD-SEC-001 | 仍成立 | `RemoteHttpTransport` 持 `RemoteHttpUrlValidator` 逐跳校验（:140-183），无 ConnectCallback/IP 钉扎；SocketsHttpHandler 仍在 `HttpClientFactory` |
| AUD-SEC-002 | 仍成立 | `ResourcePanelApiClient.cs:49,72` 仍 `?uid=` 查询串 |
| AUD-TEST-003 | 仍成立，**证据修正收窄** | 有效残留仅 `GameDownloadServiceTests.cs:694-697` 与 `GameShortcutServiceTests.cs:297,318`；原四处为误读（基线时已是可见跳过） |
| AUD-TEST-004 | 仍成立 | `BackgroundViewModelTests.cs:254` 仍硬编码 700ms；`CrossProcessPollingListenerTests.cs` 基线后未变，观察窗顾虑原样 |
| AUD-MAINT-001 | 仍成立 | `SettingsAppearanceViewModel.cs:614-618` 五个静态字段 + `internal static ApplyScheme`（:626）未动 |
| AUD-MAINT-002 | 仍成立 | 归档文件 `2026-09-11-official-launcher-diff-v1.7.2.md` 仍未跟踪（git status `??`）；另新增未跟踪 `architecture-review-2026-09-13.html` |
| AUD-ARCH-003 | 仍成立 | 双构造仍在（`ShellLifecycle.cs:98/:113/:615`；`MainWindowViewModel.cs:99-110` owns:true） |
| AUD-ARCH-004 | 仍成立 | `ViewModels/DesignGalleryViewModel.cs`（69 行）仍在根 ViewModels/ |

复核期间的附加检查（无新发现立案）：模态隔离裁定与 AGENTS.md 重写版逐字相符（见 Architecture 节）；ADR-021/022/023 与落地的接缝一致；`git diff` 确认基线后 `Directory.Packages.props`/lock 文件零变更（依赖结论无需重扫）。局限：本复核为只读 delta，未执行构建/测试套件；对 19 个提交的回归验证依赖其各自配套新增测试（`ModalRegistrarTests`、`ConfirmationDialogViewModelTests`、`RemoteHttpTransportTests` 910 行、`GameOperationPolicyTests` 等）与 CI 记录，未本地复跑。

## Critical Issues

无。


## High Priority Findings

无。

## Medium Priority Findings

### AUD-PERF-001 — 更新安装对全部未变更文件做单线程全量 CRC64 重读（文档化的损坏自愈设计）

- 类别：性能 / 下载完整性
- 严重度：Medium（用户可见等待成本；语义是有意设计）
- 置信度：95（本审计亲自读码确认）
- 状态：open（delta 复核 @ `1619d35` 重验：机制原样，仍成立）
- 处置：Architecture Decision

**证据**：`src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadExecutor.cs:263-336` — `InstallDownloadedFilesAsync` 遍历**完整清单**，凡不在 `verifiedHashes`（本次下载已证）或 `plannedHashes`（修复计划见证）内的已安装文件逐个 `ComputeFileAsync` 全文件 CRC64（:290）。安装/更新计划刻意不填充 `plannedHashes`（`DownloadPlan.cs:29-34`「Empty for the install/update plans, whose diff is size-based」）。:259-261 注释明示意图：「Untouched installed files are otherwise still hashed: this is the only content-corruption self-heal for files an update does not rewrite — the launch check only compares size/existence.」校验阶段为单 `foreach` 串行，而下载阶段为 10 路并行（`MaxParallelDownloads`）。

**影响**：改动 20/10000 文件的小更新仍要完整读盘校验其余 9980 个文件，串行磁盘受限，是下载完成后「FileCheck」阶段的主导等待时间；装机体量大于内存时为真实磁盘时间而非页缓存命中。

**建议**：若保留自愈语义，可复用修复通道已验证的机制摊销：`PlannedFileHash.Capture`（`ManifestDiffCalculator.cs:258`）式的 size+mtime 见证让「上次校验过且未变」的文件跳过重读；或对独立文件做有界并行。两者都以「见证仍匹配才信任跳过」保住自愈能力。

**建议验证**：Strongly Supported（机制本体已在修复路径存在并被 `DownloadExecutor.cs:345-362` 的复验逻辑使用；并行度对磁盘收益需基准实测）。

**建议守卫**：若采纳，为「校验跳过计数」添加 Verbose 日志字段，后续审计可直接量化摊销效果。

### AUD-ARCH-001 — `ShellLifecycle` 为 881 行六职责协调器，变更频率 19 次/180 天【已解决 2026-09-13】

- 类别：架构 / 可维护性
- 严重度：Medium（结构性债务，非规则违规——Shell 聚合本身是 sanctioned 例外）
- 置信度：95
- 状态：**resolved**（`2fe3565`，delta 复核 @ `1619d35` 确认）
- 处置：Refactor（已执行）

**原证据**：`ShellLifecycle.cs` 881 行，含模态注册同步（:736-881）、Escape 路由（:506-571）等六职责。

**解决证据**：提交 `2fe3565`（候选 06）将模态注册同步与 Escape 路由收敛为声明式 `Features/Shell/ModalRegistrar.cs`（95 行）：`ModalRegistration` 记录一处声明 kind/可见性属性/内容/Escape 命令，吞并原 watcher、Escape switch 与 mapping 三个分支块；`ShellLifecycle` 现为 775 行，`TryHandleEscape` 缩为 2 行委托（:599-602），`RegisterModals` 仅组装注册表。配套 `ModalRegistrarTests`（135 行）覆盖嵌套模态交互态。原建议的两个优先块均已拆除。

**残留（advisory，不再立案）**：`Wire/Unwire`（:412-553）与刷新/设置应用仍在 `ShellLifecycle`（775 行）；原建议的 Shell 行数预算源码契约测试未添加，留作后续可选守卫。

### AUD-ARCH-002 — `MainWindow.axaml.cs` 内嵌 ~250 行动效引擎，为全仓库最高变更热点

- 类别：架构 / 可维护性
- 严重度：Medium
- 置信度：85（子代理逐行评审 + 本审计核对行数与热点计数）
- 状态：open
- 处置：Refactor

**证据**：`src/Cafe.Launcher.Avalonia/Views/MainWindow.axaml.cs`（663 行；21 commits/180 天 = 全仓库第一热点，除生成文件外居首）。窗口本体职责（窗口状态持久化 :494-535、托盘 :469-492/:604-630、拖拽命中 :537-602、剪贴板 :639-662）之外，:71-420 嵌入约 250 行动效引擎：入场锚点动画（:71-152）、壁纸交叉淡化所有权交接（:172-231）、操作表面高度形变/沉浸动画套件（:270-420，含两个 `Animation` 工厂与 `DispatcherTimer` 退役方案）。

**影响**：ADR-016 动效语义的每次调整（CONTEXT.md P2 前沿仍开放动效微调）都落在窗口 code-behind 里，与窗口职责互相放大变更；动效代码无法脱离窗口实例复用或测试。

**建议**：将操作表面动效套件（:270-420）提取为独立 helper/behavior（输入为 surface + 参数，可被 Headless 测试直接驱动）。入场动画与壁纸淡化次优先。

**建议验证**：Plausible（提取路径清晰但 Avalonia attached-behavior 的具体形态未实验）。

**建议守卫**：与 AUD-ARCH-001 同一源码契约测试覆盖。

### AUD-TEST-001 — 53 个手写 `HttpMessageHandler` 测试桩分散在 17 个文件，无共享基类【已解决 2026-09-13】

- 类别：测试 / 测试架构
- 严重度：Medium（重复基础设施 + 行为漂移）
- 置信度：95（grep 计数本审计复核）
- 状态：**resolved**（`f123429..f123429` 引入接缝，调用方迁移随后完成；delta 复核 @ `1619d35` 确认）
- 处置：Refactor（已执行）

**原证据**：`grep "sealed class .*: HttpMessageHandler"` 命中 53 处、17 个文件，同名不同实现成对出现。

**解决证据**：`RemoteHttpTransport` 深模块（候选 01，三步 `f123429`→`f123429`→`f123429`）成为唯一出站 HTTP 模块后，五个远程调用方（API、更新、资源面板、图片缓存、清单）的测试改用共享的 `tests/TestDoubles/StubRemoteHttpTransport`（可脚本化 `Responder`、记录 `RequestedUris`、保留调用方序列化器的 wire 契约保真）。复核时 grep 实测：桩从 53 处/17 文件降至 **9 处/1 文件**，且全部位于 `RemoteHttpTransportTests.cs` 内部（`ScriptedHandler`/`RedirectHandler` 系列等，用于驱动传输层自身的重定向/降级/停滞行为——handler 级控制在此处正当且不可替代）。散桩漂移问题整类消除，超出原建议（共享 StubHttpHandler）的预期效果。

### AUD-CI-001 — CI 从不在非 Windows 平台执行测试；约 6 个平台分支在任何机器上都不运行

- 类别：CI / 跨平台行为
- 严重度：Medium（产品实验性分发 Linux/macOS，对应行为零执行验证）
- 置信度：90
- 状态：open
- 处置：Add Guard

**证据**：`build.yml:18`（仅 windows-latest）与 `release.yml:16-18`（ubuntu-24.04 job 只构建分发包与 AppImage 冒烟，**不跑 test.ps1**）；测试唯一在 Release 配置的重跑也在 windows-latest（release.yml installer job :256-263）。测试代码却包含真实的非 Windows 分支：`GamePathValidatorTests.cs:161` 按 `OperatingSystem.IsWindows()` 分支期望值、`GameShortcutServiceTests.cs` 的 `.desktop` 路径、`LauncherSettingsServiceTests.cs:490-509` 用 `isLinuxPlatform` 注入缝模拟 Linux 归一化（好设计，但真平台路径仍无执行）。golden 截图类按设计 Windows-only（`GoldenScreenshot.cs:39-41`），单元套件主体是跨平台的（`Cafe.Launcher.Avalonia.Tests.csproj:11-16` 注释即按 Linux 交叉编译撰写）。

**影响**：Linux/macOS 专属行为（路径分隔符大小写敏感、`.desktop` 快捷方式、runner 归一化）的回归只能靠开发者恰好在这些平台上手跑发现；发布说明标榜的实验性平台没有最低回归保障。

**建议**：为 `tests/Cafe.Launcher.Avalonia.Tests`（单元套件，无 Avalonia 渲染依赖）增加可选/周期性 Linux job（`workflow_dispatch` + `schedule` 即可，不必阻塞 PR）。Headless 套件继续 Windows-only 是合理的（golden 类按平台门控）。

**建议验证**：Strongly Supported（test.ps1 与 csproj 均已支持非 Windows restore，见 csproj 注释；仅 CI 编排缺失）。

**建议守卫**：该 job 本身即守卫；同时落地 AUD-TEST-003 让跳过可见。

### AUD-TEST-002 — 更新检查的「服务→UI」粘合层无测试（`SettingsViewModel.CheckForUpdatesAsync` 与 ShellLifecycle 确认接线）

- 类别：测试 / 关键路径覆盖
- 严重度：Medium（回归表现为用户可见的错误提示/漏提示）
- 置信度：85
- 状态：open（delta 复核 @ `1619d35` 重验仍成立）
- 处置：Fix

**复核附注（2026-09-13）**：`CheckForUpdateAsync` 签名在基线后有微调（去掉 ProxyMode 参数），`CheckForUpdatesAsync` 现于 `SettingsViewModel.cs:240`；仍无 `SettingsViewModelTests`，全测试树对 `CheckForUpdatesAsync`/`CheckForLauncherUpdateCommand` 零命中；`ShellLifecycleTests` 对 `ConfirmUpdateAvailable`/`ExternalLink` 仍零覆盖。新增的 `ConfirmationDialogViewModelTests`（160 行）覆盖的是对话框机制本身，不涉及更新流。

**证据**：`LauncherUpdateService`（26 个测试，含 URL 域钉住、语义化版本矩阵、重定向降级）与 `DialogsViewModel`（对话框打开后的行为）都被充分覆盖，但二者的粘合没有测试：`src/Cafe.Launcher.Avalonia/Features/Settings/SettingsViewModel.cs:239-266` 的 `CheckForUpdatesAsync` 决定「错误 toast / 已最新 toast / 打开更新对话框」的分支及失败消息格式化（:250-254）无任何直接测试（无 `SettingsViewModelTests`；`CheckForLauncherUpdateCommand` 在测试中零命中）；`ShellLifecycle.cs:356,428,465` 的启动期更新检查成功路径与 `ConfirmUpdateAvailableRequested` → `ExternalLinkService.Open` 订阅/退订仅测试了抛异常路径（`ShellLifecycleTests.cs:236`）。

**影响**：这里恰是「吞掉失败当成功」一类回归的典型滋生地；服务与对话框各自正确不等于组合正确。约 25 行未测分支决定用户对「有新版本」的唯一感知通道。

**建议**：为 `CheckForUpdatesAsync` 三分支 + 失败消息格式化补聚焦测试（手写 stub 服务，遵循仓库无 Mock 约定）；ShellLifecycle 侧补「确认事件触发一次 Open 且 Dispose 后退订」一例。

**建议验证**：Verified（纯行为断言，现有测试基建直接可用）。

## Low Priority Findings

### AUD-PERF-002 — 停止/暂停/恢复的 UI 点击路径同步阻塞日志 sink

- 严重度：Low｜置信度：90｜状态：open（delta 复核 @ `1619d35` 重验仍成立）｜处置：Fix
- **证据**：`GameDownloadService.cs:163` `diagnostics.DebugAsync(...).GetAwaiter().GetResult()`（复核时行号自 :152 移至 :163；候选 05/11 重构未触及此调用形态）；`DownloadSession.cs:520,527` 的 `Pause()/Resume()` 内 `LogSync`（原 :533,540）。被阻塞调用经 `LocalDiagnostics.LogSync → UnifiedLogger.LogAsync(...).GetResult()`，其文档自述「sync-over-async stalls the UI thread whenever the Serilog async sink buffer is full」。候选 11 将诊断缝接口化，但 `LogSync` 的阻塞语义保持原样。
- **影响**：常态仅微秒（10,000 缓冲入队即返回）；下载日志风暴 + 日志查看器占用文件的背压场景下「停止」点击出现可感卡顿。
- **建议**：改 fire-and-forget（`_ = DebugAsync(...)`，仓库他处已有先例）。注意 PROJECT_CONVENTIONS §3.2 要求同步上下文用 `LogSync`——本处的正确改法是让该方法上下文变为可异步或显式弃等并注释意图，而非直接换 `LogSync`。
- **建议验证**：Verified。

### AUD-PERF-003 — 下载缓冲每次尝试新分配 256 KiB LOH 数组，未池化

- 严重度：Low｜置信度：90｜状态：open（delta 复核 @ `1619d35` 重验仍成立）｜处置：Fix
- **证据**：`FileDownloadService.cs:145` `var buffer = new byte[1024 * 256];` 位于重试 `for` 循环体内（复核时行号自 :129 移至 :145；候选 08 将传输挪入 `DownloadTransport`，缓冲分配仍在此文件循环体内）。256 KiB > 85 KiB LOH 阈值。对照同类热点 `Crc64Service.cs:29-33` 已显式 `ArrayPool<byte>.Shared` 租赁并注释了同一 LOH 顾虑。
- **影响**：N 个文件的安装产生 ≥N 次 LOH 分配（损坏重试最多 10×）；无正确性问题，属 GC 压力/碎片。
- **建议**：比照 `Crc64Service` 用 ArrayPool 租赁/归还（try/finally 保证归还）。**建议验证**：Verified。

### AUD-PERF-004 — 每次壳层刷新销毁并重新下载/解码全部横幅位图

- 严重度：Low｜置信度：85｜状态：open（delta 复核 @ `1619d35` 重验仍成立）｜处置：Refactor
- **证据**：`RemoteContentViewModel.cs` 的 `Apply`（现 :152，基线后该文件有 48 行改动）在每次刷新（启动、每次设置保存、**每次游戏操作完成**）时 `DisposeBannerBitmaps()`（:159）后经 `PreloadBannerImagesAsync`（:515）重取缓存并重解码。候选 10 的 `CarouselTimer` 时钟接缝只解决轮播时序可测性，位图生命周期未变。解码离线程、生命周期处理（陈旧解码丢弃）堪称范例；成本是 CPU/内存 churn 与操作完成后的轮播「加载中」闪态。
- **建议**：以 ImageCacheService 已有的 URL/CRC 键做每 URL 位图备忘。**建议验证**：Plausible。

### AUD-SEC-001 — URL 校验与实际拨号之间存在 DNS 重绑定 TOCTOU 窗口

- 严重度：Low｜置信度：80｜状态：open｜处置：Accept Risk（或在网络层重构时顺带处理）
- **证据**：`RemoteHttpUrlValidator.cs:109-120` 本地解析 DNS 并拒绝非公网地址；但 `SocketsHttpHandler` 拨号时自行二次解析。远程可控 URL（清单/CDN/重定向 Location）的权威 DNS 可对校验答公网 IP、对拨号答私网 IP。每跳复验（`RemoteHttpRequestService.cs:27-29`）同样受此限制；30s 正缓存（:138-150）不关闭该窗口。
- **影响**：远程方可让启动器对其局域网/本机地址发起 GET。影响有界：GET-only、响应不回传攻击方、已验证签名 Authorization 头绝不跟随重定向转发（`LauncherApiClient.cs:243-250` + 单发 `SendAsync` 不自动重定向）。属经典 validate-then-dial 残余，威胁模型内无直接利用链。
- **建议**：若未来收紧：delegating handler 将连接钉到已校验 IP，或手工解析+连接。当前接受风险合理。**建议验证**：Needs External Verification（修复方案涉及 HttpClient 栈行为细节）。

### AUD-SEC-002 — 资源面板 UID 以 URL 查询串传输，会落入代理/服务器访问日志

- 严重度：Low（隐私）｜置信度：85｜状态：open｜处置：Accept Risk
- **证据**：`Features/ResourcePanel/ResourcePanelApiClient.cs:72,94-98` `?uid=...`。UID 为 8 位大写字母的社区面板标识符（`ResourcePanelUidService.cs:24-25`），协议本身镜像社区面板的 `fetch` 用法；诊断日志已专门剥离查询串（`RemoteHttpRequestService.cs:202-209` `DescribeUri`）。
- **建议**：接受；如社区面板未来支持请求体传参再跟进。**建议验证**：Needs Product Decision（取决于上游面板协议）。

### AUD-TEST-003 — 非 Windows 平台分支以早期 `return` 隐藏跳过，而非 `Assert.Skip`【复核后收窄】

- 严重度：Low｜置信度：90｜状态：open｜处置：Fix
- **复核修正（2026-09-13 @ `1619d35`）**：原证据四处系误读，现予更正——`CrossProcessLaunchSignalTests.cs:171-172,193-194,214-215` 在基线时即已是「`Assert.SkipUnless` 在前 + 早退 return 在后」的形态（return 是给不识别 `SkipUnless` 的 CA1416 平台分析器的守卫，注释明示，跳过已可见）；`GameShortcutServiceTests.cs:580-590` 的 `WindowsFactAttribute` 以 `Skip = ...` 提供可见跳过。这四处不构成假覆盖。
- **仍然成立的证据**：`GameDownloadServiceTests.cs:694-697` 裸 `if (!OperatingSystem.IsWindows()) { return; }`（该文件基线后有 807 行改动，此模式保留）；`GameShortcutServiceTests.cs:297,318` 的 `ReadShortcutIconLocation`/`ReadShortcutTarget` 非 Windows 返回空元组使断言退化。对照正确范式：`GameUninstallServiceTests.cs:29`、`InstallationOperationStateTests.cs:474` 用 `Assert.SkipUnless/SkipWhen`。全仓库 `Assert.Skip*` 现有 13 处。
- **影响**：与 AUD-CI-001 叠加后，`GameDownloadServiceTests.cs:694` 的分支在所有机器上都是静默死代码；两个退化助手使快捷方式读取断言在非 Windows 上恒真。
- **建议**：`GameDownloadServiceTests.cs:694` 替换为 `Assert.SkipUnless`；两个退化助手改为经注入缝或 `Assert.Skip*` 门控（机械修改，范围较原判定显著缩小）。**建议验证**：Verified。

### AUD-TEST-004 — 魔数睡眠编码生产常量；否定断言观察窗短于生产轮询间隔

- 严重度：Low（单边假通过风险，非 flaky）｜置信度：85｜状态：open｜处置：Fix
- **证据**：`BackgroundViewModelTests.cs:253` 固定 `Task.Delay(700ms)` 后断言淡化位图未被释放——700 是生产 overlay 宽限期的硬编码复制品，生产值上调即静默假通过。`CrossProcessPollingListenerTests.cs:38,95-96,121,145` 的否定观察窗 50-80ms 短于生产 `PollInterval` 250ms（`CrossProcessPollingListener.cs:17`），Dispose 测试只能抓住紧旋循环而非「循环仍在 250ms 节奏运行」。
- **建议**：前者从（internal 可见的）生产常量推导延时；后者拉长观察窗至 >1× 轮询间隔或在测试缝注入更短间隔。**建议验证**：Strongly Supported。

### AUD-MAINT-001 — `SettingsAppearanceViewModel` 以静态字段保存主题方案缓存（隐藏全局）

- 严重度：Low｜置信度：90（本审计亲自确认字段与消费点）｜状态：open｜处置：Refactor
- **证据**：`Features/Settings/SettingsAppearanceViewModel.cs:614-618` 五个 `private static` 字段（`lastSchemeApplied/lastThemeMode/lastSchemeSeed/lastSchemeVariant/lastSchemeStrategy`），由 `internal static ApplyScheme` 写（:647-650）、主题模式应用逻辑读（:589-595, :673-682）决定是否跳过重复应用。VM 是 DI 单例（组合根全 Singleton），静态存储在功能上等价于实例状态，但跨实例存续、对测试不可见于对象图。测试侧已有先例为此买单：`BackgroundViewModelHeadlessTests.cs:100-102,142-145` 需在 `finally` 恢复被测静态 `ResizeReloadDebounce`。
- **影响**：当前无生产缺陷路径（单例 + 应用级资源本就是全局态）；成本是隐藏耦合与测试污染面，第二个窗口/预览实例出现时才会变成真问题。
- **建议**：随下次触碰该文件把缓存移入实例字段（`ApplyScheme` 同步改为实例方法，测试调用点经 VM 实例）。**建议验证**：Verified。

### AUD-MAINT-002 — 官方启动器协议对比分析（436 行）未入库，兼容性依据仅存于工作树

- 严重度：Low｜置信度：100｜状态：open｜处置：Document
- **证据**：`docs/official-launcher-diff-v1.7.2.md` 为 git 未跟踪文件（`git status` `??`，未被 ignore）。内容为与官方 Electron 启动器 v1.7.2 的协议兼容层逐项核对（清单格式/`vc`/CRC-64/请求签名/CDN URL 构造/重试阶梯等价性）与行为差异清单，是「Cafe 可与官方启动器安全共用游戏目录」这一核心兼容性主张唯一的书面论证。
- **进展（2026-09-13）**：已按用户裁定移入 `.repository-audit/history/2026-09-11-official-launcher-diff-v1.7.2.md` 归档（作为时点分析快照，不再作为当前态文档维护）。该文件仍为未跟踪状态——归档只有被提交后才真正获得版本保护。
- **影响**：一次 `git clean` 或换机即丢失；后续贡献者无法追溯兼容性决策依据。
- **建议**：~~提交入库（脱敏本机绝对路径），或在团队层面明确决定不入库并记录该决定。~~ 已按裁定于 2026-09-13 移入审计归档目录；剩余动作是把归档文件（含脱敏）提交入库，使兼容性论证获得版本保护。
- **建议验证**：Verified（存在性与内容本审计直接确认）。

### AUD-ARCH-003 — `ShellLifecycle` 存在双构造路径，释放所有权语义在测试与生产间分叉

- 严重度：Low｜置信度：85｜状态：open（delta 复核 @ `1619d35` 重验仍成立）｜处置：Investigate
- **证据**：生产 DI 路径走公开构造 `ownsPresentationCollaborators: false`（现 `ShellLifecycle.cs:98`，原 :97）；`MainWindowViewModel.cs:99-110` internal 构造 `new ShellLifecycle(..., ownsPresentationCollaborators: true)` 仅供测试。`ShellLifecycle.cs:615`（原 :583-594 区块）仅在 `owns==true` 时释放展示 VM——测试行使的所有权制度与生产不同。候选 06 的 ModalRegistrar 重构未触碰该分叉。
- **影响**：与释放顺序相关的回归在测试中不可复现。属受控测试缝（仓库约定 internal 构造注入替身），但「缝改变了被测行为」超出普通替身范畴。
- **建议**：评估让测试路径也走 `owns:false` + 显式管理替身生命周期；若判定现缝可接受，在构造参数注释中写明两种制度差异。**建议验证**：Needs Architecture Decision。

### AUD-ARCH-004 — `DesignGalleryViewModel` 为功能级体量却无功能归属（Informational）

- 严重度：Informational｜置信度：85｜状态：open｜处置：Architecture Decision
- **证据**：`ViewModels/DesignGalleryViewModel.cs:18` 是七个主叠层之一（设计画廊）的模态内容 VM，按规则七个叠层属功能域，但它在根 `ViewModels/` 无 `Features/` 归属。可辩解为「共享展示契约居根目录」，但它是功能体量的 VM 而非契约。无行为影响；后续若出现更多叠层将重演该归类判断。
- **建议**：无需立即行动；下次触碰时决定「归入 `Features/Diagnostics`（其天然宿主）或明文豁免」。**建议验证**：Needs Architecture Decision。

## Architecture

**结论：文档边界与实现高度一致，未发现规则违规。**

已验证的优势（闭合实质关切）：

- **跨功能具体引用为零**（Shell 除外）：全量 `using Cafe.Launcher.*` 扫描，越界仅 `ShellLifecycle`/`ShellPresentationFamily`/`ShellStartup` 三文件 = AGENTS.md 明文 sanctioned 例外；`IGameOperationActivity` 窄抽象实际生效（DebugViewModel 消费接口、组合根绑定 GameOperationsViewModel 实现）。
- **组合根纪律**：全部注册集中于 `ServiceConfiguration.AddLauncherServices()`、全 Singleton、纯构造注入；无运行时服务定位（`GetRequiredService` 仅存在于组合与激活点）；`Program.ServiceProvider` 静态句柄仅用于会话结束释放。
- **ViewModel 归属规则完全落实**：根 `ViewModels/` 14 个类型全部为窗口级 VM 或模态契约，无非 VM 类型。
- **模态隔离裁定逐字落地**（2026-09-12；2026-09-13 delta 复核再确认）：`ModalHostViewModel.cs:26-44` 恰好七个 `Is*Interactive`（另 `IsBaseLayerInteractive`）；对话框/遮罩仍由可见性标志驱动（`ConfirmationDialogViewModel.IsVisible` 实例与 `DialogsViewModel` 标志，复核确认 `MainWindowDialogsOverlay.axaml` 中全部 ConfirmDialog 无交互闸口，候选 09 收敛后裁定不被破坏）；模态栈注册已由 `ShellLifecycle.SyncModal` 演进为声明式 `ModalRegistrar`（候选 06），仍以渲染同源的可见性属性为单一真相来源；`MainWindow.Styles.axaml` 遮罩/ZIndex 次序与文档一致。`ModalHostViewModelTests` 与新增 `ModalRegistrarTests` 含嵌套模态交互态用例。
- 见 Medium/Low 发现（AUD-ARCH-002/003/004；AUD-ARCH-001 已解决）。

## Security

**结论：无 Critical/High。安全工程在桌面启动器威胁模型下成体系且大多有测试。**

信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、本机同用户进程、CI/发布凭据。

已验证的优势：

- **网络**：全仓库无任何 TLS/证书校验覆盖（grep 证实）；重定向手动处理、上限 5、每跳重新过 URL 校验、HTTPS→HTTP 降级阻断（`RemoteHttpRequestService.cs:14,44-48,60-64`）；签名 Authorization 头结构上不可能泄漏给重定向目标（单发 `SendAsync` + 每跳新建请求）；SSRF 分层防护（scheme/userinfo/端口/localhost/IP 字面量/解析地址全公网 + IPv6 映射/链路本地/ULA 等全谱系，`RemoteHttpUrlValidator.cs:67-195`）；响应 64MB / 图片 25MB 封顶 + 空闲停滞超时；代理出口判定精细（真经代理才豁免本地 DNS 检查，`RemoteHttpRequestService.cs:109-117`）。
- **文件系统**：远端清单路径全部规范化 + 根前缀比较（大小写策略分平台）+ reparse point 全组件拒绝（`GamePathValidator.cs`）；`GetSafeFilePath` 拒绝规范化为根自身的条目并有 `<root>.tmp` 越界写注释；卸载删除被本地清单（提交时逐路径校验）+ `IsSystemProtectPath` 双重围栏；所有持久化 temp+Move 原子提交；被篡改的 `download_state.json` 无法把续传导向意外路径（checkpoint 五元组不匹配即清，`DownloadSessionFactory.cs:44-53`）。
- **进程执行**：游戏启动/版本探测/崩溃上报全部 `ProcessStartInfo.ArgumentList`，`UseShellExecute=false`；可执行文件名拒路径分隔符且必须存在于游戏目录；外部链接 http/https/mailto 白名单双层（`ExternalLinkService` + `RemoteContentViewModel`）；自更新仅 host 钉住的 https github.com 下载页跳转，不下载执行自身二进制（`LauncherUpdateService.cs:263-307`）。
- **完整性**：下载后全文件 CRC64 不过即删重试；续传校验 206 Content-Range 三元组；修复对全树哈希并以 size+mtime 见证防跳过被滥用。
- **密钥与日志**：仓库无真实凭据（`AuthorizationSalt` 为官方启动器公开协议常量，代码内明示）；日志/异常/崩溃报告均不落 Authorization/salt/cookie/查询串；日志导出含 UID 与路径仅在用户显式勾选时。
- **反序列化**：纯 System.Text.Json 强类型 POCO；`EnableUnsafeBinaryFormatterSerialization=false`；二进制 cookie 解析有界且失败关闭。
- 接受风险的残余（advisory，不编号立案）：校验-拨号 DNS 重绑定窗口（AUD-SEC-001）；reparse point 检查-使用间隙（要求攻击者已有游戏目录写权限，届时其已控制游戏 exe 本身）；清单无签名（协议继承自官方启动器，`OfficialHashService.cs:9-14` 明示，单方面变更将破坏兼容）。

## Dependencies / Supply Chain

**结论：当前无漏洞包；锁定/钉住/校验闭环完整。**

- `dotnet list package --vulnerable --include-transitive`（本审计实际执行）：三个项目均无漏洞包。
- 中央版本管理 + 三份 `packages.lock.json` 与 `Directory.Packages.props` 同步于同一提交（`cbddf31`，2026-09-12）；CI `RestoreLockedMode` 使 lock 与依赖图不一致即 NU1004 失败（配置存在且**可执行生效**——豁免路径均有一致性理由注释）；Dependabot 编排 + squash 为 `chore(deps)` 的约定。
- CI 动作 4 个全部按 commit SHA 钉住并注明版本；SDK 双固定（global.json + setup-dotnet 显式 10.0.302，注释记录了「runner 当日携带版本决定产物运行时」的历史教训）；AppImage 工具 SHA256 钉住；Inno Setup 7.1.0 经 `gh release verify-asset` attestation 校验；发布产物 SHA256SUMS 数量硬校验（宁可中断发布不出不完整清单）。
- `THIRD-PARTY-NOTICES.md` 与依赖版本同提交再生（无测试守护，靠流程约束——AGENTS.md 已自述此漂移风险，属已知已记录项，不重复立案）。
- 许可面：MIT/Apache-2.0 为主，`AvaloniaUI.DiagnosticsSupport` 标「see package」且仅 Debug 分发（csproj 约束），不进 Release 产物。

## Testing

**结论：纪律性罕见地好：程序集级串行有书面静态状态清单、有界等待为主流范式、测试零真实网络、黄金截图失败工件闭环。** 42k 行测试 vs 33k 行源码；覆盖率棘轮基线 85.85% 行 / 92.70% 分支（`coverage.ps1` 实际执行并打印余量）。

已验证的优势：

- 关键路径保护逐项核实为「重保护」：下载续传/暂停/CRC 三层（service/journey/checkpoint 共 100+ 用例，含 Content-Range 篡改、只读目标、限速下界）、设置兼容（26 用例 + DeepClone 反射棘轮 + 字段序兼容钉）、安装状态损坏矩阵（15 用例分支级）、URL 校验 29 用例（私网 DNS/重定向到 localhost/降级/六跳上限）、卸载边界（锁定文件中止/二次调用幂等/保护路径门）。
- 确定性：`[assembly: CollectionBehavior]` 附共享静态清单；`TestUserDataIsolation` 模块初始化器重定向用户数据到临时 GUID 目录；等待统一 `WaitUntilAsync` 带截止与语义化失败消息；动画零退出时延由 `[ModuleInitializer]` 消除。
- 诊断：黄金失败写 actual/diff PNG 且 CI `if: always()` 上传（路径与写盘路径核实一致）；trx/cobertura 同样失败也上传；`-UpdateGolden` 一条命令再生基线且有基线契约测试守备。
- 缺口见 AUD-CI-001/TEST-002/003/004（AUD-TEST-001 已于 delta 期间解决，TEST-003 经复核收窄）；advisory：`GameDownloadServiceTests.cs` 2134 行混层（单测 handler + journey + 限速采样器），`WaitUntilAsync`/`WaitForGamePathStatusAsync` 各有 3/2 份变体。

## Performance

**结论：启动路径无首帧阻塞（初始化全部后置于 `Opened` + Background 优先级；远程读 30s 总预算并发执行；主题色提取 64px 降采样离线程）；热路径残留成本见各发现。** 代码中大量注释记录过往性能修复（双哈希消除、LOH 池化、离线程解码），修复是体系性的而非点状。

- 主要项 AUD-PERF-001（更新全量重哈希）；其余 AUD-PERF-002/003/004。
- advisory（不立案）：清单 JSON 每次读双解析、每次提交最多 4 次解析（毫秒级、每操作一次）；缓存壁纸加载前整文件 CRC（完整性换 IO，离线程）；`ImageCacheService.cacheLocks` 信号量字典不修剪（会话内有界）；`ResponseBodyReader` 每 256KiB 读分配 CTS+超时定时器（10 流 × ~400 chunk/s 的次要 GC churn）。
- 已核实干净：`MainWindow.axaml.cs` 无同步 IO/`.Result`；HttpClient 池化 + 批次共享单客户端；事件订阅全部镜像退订；`download_state.json` 每会话一次写入；设置仅保存/关机时写；清单 diff/合并全字典化无 O(n²)。

## Maintainability / Technical Debt

- 见 AUD-ARCH-002（剩余热点协调器）、AUD-MAINT-001（静态缓存）、AUD-MAINT-002（未入库分析文档）；AUD-ARCH-001 已解决（残留 advisory 见其小节）。
- Git 热点与结构互相印证：最高变更文件（`MainWindow.axaml.cs` 21、`ShellLifecycle` 19、`ServiceConfiguration.cs` 20、`App.axaml.cs` 17 commits/180 天）恰是窗口/组合/生命周期边界——职责汇聚点，符合「接线处变更多」的正常形态；`ShellLifecycle` 的模态/Escape 内嵌引擎已由候选 06 拆除，`MainWindow.axaml.cs` 的动效引擎仍超出接线范畴。
- 文档漂移检查：本审计抽取的 AGENTS.md/PROJECT_CONVENTIONS.md 关键声明（模态隔离机制、Shell 豁免、main 保护规则实测状态、§12 工具链表）与实现/仓库实际逐一相符；`LauncherStrings` 四语言键对齐由契约脚本 + CI 步骤双重保障。

## Decisions Required

1. **AUD-PERF-001**：是否为更新校验引入见证摊销/并行化（保自愈语义）。选项：a) 维持现状（接受 FileCheck 等待）；b) 见证哈希摊销（复用修复通道机制）；c) 有界并行哈希。b 与 c 可组合。
2. **AUD-CI-001**：是否增加非 Windows 单元测试 job（建议 `workflow_dispatch` + `schedule`，不阻塞 PR）。
3. **AUD-MAINT-002**：协议对比分析文档入库（脱敏路径）还是明确决定不入库。
4. **AUD-ARCH-003**：`ShellLifecycle` 测试缝的所有权制度差异是否收敛，或书面接受。

## Resolved Findings

- **AUD-ARCH-001**（2026-09-13，`2fe3565`）：ShellLifecycle 模态注册同步与 Escape 路由收敛为声明式 `ModalRegistrar`（881→775 行，配套 `ModalRegistrarTests`），原建议的两个优先块均拆除。残留 advisory 见该小节。
- **AUD-TEST-001**（2026-09-13，`f123429..f123429` 及调用方迁移）：`RemoteHttpTransport` 深模块 + 共享 `StubRemoteHttpTransport` 使 HttpMessageHandler 散桩从 53 处/17 文件降至 9 处/1 文件（全部留在传输层自身测试内）。

历史台账（101 项，其中 81 resolved）随 2026-09-12 旧报告归档于 `history/2026-09-12-findings-ledger.json`，供追溯而不作为本报告状态来源。

## Automated Guards Added

本次基线审计为评审性质，未直接添加守卫。delta 复核期间，候选 01-11 的落地顺带解决了 AUD-TEST-001（守卫以 `StubRemoteHttpTransport` + 传输层单测形态实现，优于原建议的共享 StubHttpHandler）。剩余建议守卫汇总：

1. `Assert.Skip*` 统一替换（AUD-TEST-003，复核后仅剩 3 处）+ 可选 Linux 测试 job（AUD-CI-001）——同一根因的组合守卫。
2. Shell 层源码契约测试（行数/职责预算，AUD-ARCH-002）——仓库已有源码断言测试先例（`InstallerContractTests`、`GoldenBaselineContractTests`）；AUD-ARCH-001 解决时未附带此守卫，可一并补上。
3. 新增的 `ModalRegistrarTests`/`ConfirmationDialogViewModelTests` 已构成候选 06/09 的行为守卫。

## Verified Strengths

见各域小节。最高杠杆的三项（防止不必要的重构/担忧）：

1. **模态隔离裁定与实现逐字一致**——任何「对话框层再加一道闸口」的提议都会引入第二真相来源并硬冻结窗口；现状（遮罩 + ZIndex 承担对话框输入拦截）是 2026-09-12 裁定的正确落地，不应动。
2. **网络/文件系统防御纵深真实存在且有测试**——URL 校验 29 用例、卸载边界、续传 Content-Range 校验等不是纸面配置。
3. **供应链闭环可执行**——锁定还原、SHA 钉住、SUMS 数量硬校验、Release 配置重测均为生效机制而非声明。

## Recommended Priorities

1.（低成本高杠杆）AUD-TEST-003（复核后收窄为 3 处）+ AUD-CI-001：让平台分支的跳过可见并给单元套件一个 Linux 执行点。
2.（低成本）AUD-PERF-002/003、AUD-TEST-004：三处机械修复，各自 < 半小时。
3.（中成本，随下次触碰执行）AUD-ARCH-002：抽取操作表面动效套件（姊妹项 AUD-ARCH-001 已解决，模式可复制）。
4.（决策后执行）AUD-PERF-001、AUD-MAINT-002、AUD-TEST-002。

## Audit Method and Limitations

实际执行：

- 仓库发现：README、AGENTS.md、PROJECT_CONVENTIONS.md、CONTEXT.md、CI 两工作流、`Directory.Packages.props`、global.json、installer `.iss`、打包脚本关键行（逐文件阅读）。
- 六域并行审计通道（安全/测试/架构/性能由只读子代理执行「very thorough」广度检索，证据均带 file:line；依赖/供应链与验证由主审计执行）。
- 工具证据：`dotnet list package --vulnerable --include-transitive`（无漏洞）、`git log --name-only` 热点统计、`wc -l` 规模核对、`grep` 桩计数（53 处/17 文件）、锁文件-版本声明同提交核对、`git status`/`git check-ignore`（未跟踪文档）。
- 关键发现亲自复核：AUD-PERF-001（DownloadExecutor :240-350 逐行）、AUD-PERF-002（GameDownloadService :130-155）、AUD-MAINT-001（静态字段与消费点 :589-682）、AUD-ARCH-001（881 行核对）、`InstallerContractTests` 存在性。
- 未执行 `verify.ps1`/`test.ps1` 全量套件（审计为只读评审，构建/测试状态以 CI 工作流定义与上次发布记录为准，未声称本地绿灯）。
- 局限：macOS/Linux 平台行为未在任何非 Windows 环境实测（与 AUD-CI-001 同源）；性能发现均基于代码路径推理，未做运行时测量（报告内无未经测量的倍数/毫秒声明）；安全通道对 DNS 重绑定窗口为设计分析而非复现。
- 子代理产出中的行号引用在关键项上经主审计抽查核实；未抽查项置信度已相应降档（85 而非 90+）。
