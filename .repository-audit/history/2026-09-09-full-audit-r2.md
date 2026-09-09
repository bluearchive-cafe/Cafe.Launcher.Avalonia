# 仓库审计报告 — 2026-09-09（full 全量，第二轮：自源码重读）

## Audit Metadata

- 日期：2026-09-09
- Commit：`cffbd4d`（工作树干净；审计中 `verify.ps1` 触发的 `packages.lock.json` RID 段改写已 `git restore` 还原）
- 模式：full（自源码重读，非仅增量复核）
- 上一基线：`f6d44a7`（同日 full；其修复 `d9f2184` / `78c7d67` 落在其后）
- 范围：6 领域全过（架构 / 安全 / 依赖 / 测试 / 性能 / 可维护性）+ 台账全量对账
- 风险画像：`desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical；cross_platform_behavior / testing_determinism / ui_thread_performance / persistence_and_migration / architecture_boundaries = high）

## Executive Summary

**0 Critical / 0 High；2 Medium + 9 Low open。** 本轮与上一轮结论的差异不是新代码引入的缺陷，而是审计方式差异：上一轮以增量为主（`b1f62fa..f6d44a7`），本轮按 full 模式自源码重读，于是（a）重新打开了一个只修了一半的旧发现，（b）浮出一批增量视角看不到的项。其中 `AUD-PERF-003` 是**回归性复核发现**：原发现明确写「DI 构造期 + 每次刷新」两半，修复只覆盖了刷新路径。**本报告产出后同日修复 2 项文档类 Low（`AUD-DOC-001` / `AUD-DOC-002` → `16a5129`），故当前 open 为 2 Medium + 7 Low。**

门禁本机实跑全绿（`verify.ps1` exit 0）：本地化合约通过；Debug 0 警告 0 错误；单元 1484 过 / 2 跳（CI 1486 过 / 0 跳，含 2 例符号链接守卫）；Headless 167/167；合并手写覆盖率 行 85.25%（13703/16073）/ 分支 92.12%（2187/2374），高于棘轮 84.30% / 88.99%；Release win-x64 0 警告 0 错误；Release 下 Resx 契约 18/18。CI 实证：HEAD `cffbd4d` push run `34322709781` success。

Open 项一览：

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-PERF-003 | Medium | open（重开） | 内置壁纸仍在 DI 构造期于 UI 线程同步全尺寸解码；启动期同一 PNG 解码两次 |
| AUD-XPLAT-001 | Medium | open | macOS 无任何可用 runner，游戏永远无法启动，且只给通用失败提示 |
| AUD-ARCH-004 | Low | open | 设置页重置确认未纳入 ModalHost：Escape 关掉下层设置页而非该确认 |
| AUD-SEC-005 | Low | open | 清单空路径条目使下载临时文件写到游戏目录之外 |
| AUD-MTN-011 | Low | open | 两处 MUST 不变量无守卫（清单 JSON 键序 / settings 深克隆完整性） |
| AUD-MTN-013 | Low | open | 死公共 API `GetLanguageOptions()` 无参重载会重定向全局日志到临时目录 |
| AUD-DOC-001 | Low | open | PRIVACY.md 未覆盖崩溃快照 |
| AUD-DOC-002 | Low | open | 文档索引/表格漂移三处（CONTEXT ADR 索引 / CLAUDE 打包与 Z 序 / §12 表） |
| AUD-TST-004 | Low | open | 卸载安全闸口（游戏运行中、驱动器根）无聚焦测试 |
| AUD-TST-005 | Low | open | 约 15 处测试等待无超时；csproj 抑制理由不实 |
| AUD-REL-006 | Low | open | 两处 best-effort catch 无日志，一处使调用方 Warn 兜底成死代码 |

Most important actions：

1. `AUD-PERF-003`：把构造期的内置壁纸解码移出 UI 线程，并在构造期播种 `lastBackgroundSourceKey` / `lastDecodeTarget`，让启动后首次刷新走跳过分支（同时消除二次解码）。
2. `AUD-XPLAT-001`：决定 macOS 的去向（补 runner / 去掉产物 / 显式声明不支持启动），并把结论写进 README 与关于页。
3. `AUD-ARCH-004` + `AUD-TST-004`：各补一条守卫（ModalKind 全枚举 Escape 测试、卸载两道闸口测试），成本极低、直接封住回归。

## Method（与上一轮的差异）

1. 以 `cffbd4d` 为基线**独立重读源码**，不依赖上一轮报告结论；4 条并行审计通道（安全/文件系统、架构/可维护性、测试、性能/跨平台）+ 主审自读关键路径（下载→校验→安装、卸载、启动、设置持久化、崩溃快照、打包脚本）。
2. 仓库原生工具优先：`verify.ps1` 全量实跑、直接解析两份 cobertura 与 trx、`gh run view --log` 取 CI 实测跳过数、`dotnet list package --vulnerable --include-transitive`。
3. 每条候选发现均回溯到源码/调用链/测试/仓库规则后定级；推荐单独验证（见各条 Recommendation validation）。
4. 台账全量对账：逐条复核 `resolved` 项，并按 lifecycle 规则处理复发。

## Changes Since Previous Audit

`f6d44a7..cffbd4d`，1 提交 / 12 文件：`cffbd4d` 为上一轮 2 项 Low 的修复（`AUD-REL-005` 新增 `CrashReportBootstrap` + 10 例测试；`AUD-MTN-010` §10 基线清单改 6 + `GoldenBaselineContractTests`）。复核结论：两项修复与守卫均按声称落地（`CrashReportBootstrap.cs` 62 行、`GoldenBaselineContractTests` 存在且断言 `Baselines/*.png` 与 `GoldenScreenshot.Compare` 调用 1:1）。该提交未触碰任何本轮发现所在代码。

## Critical Issues

无。

## High Priority Findings

无。

## Medium Priority Findings

### AUD-PERF-003 — 内置壁纸仍在 DI 构造期于 UI 线程同步解码（重开）

- Category: performance / ui-thread
- Severity: Medium
- Confidence: 95
- Status: open（重开；上一轮记为 resolved@`85d2266`）
- Disposition: Fix

**Evidence**：`ViewModels/BackgroundViewModel.cs:122` 构造函数体 `backgroundImageSource = bundledImageLoader();` → `:647-662 LoadBundledBackground()`（`AssetLoader.Open` + `new Bitmap(stream)`，全尺寸）。该单例经 `Composition/ServiceConfiguration.cs:123/141/143` 由 `App.axaml.cs:72` 在 UI 线程、首帧之前解析。资产 `Assets/launcher-background.png` 实测 **2560 × 1388 / 3,396,889 字节**（本轮读 PNG 头复核）。另：构造函数从不写 `lastBackgroundSourceKey`（仅 `:185/:221/:245` 三处写入），故启动后第一次 `UpdateBackgroundImageAsync` 的跳过守卫（`:153-160`）必然失配，Bundled 分支再 `await Task.Run(() => bundledImageLoader())` 解码一次。

**Impact**：每次启动在首帧前于 UI 线程付一次全尺寸 PNG 解码（默认壁纸用户）；即使用户保存的是自定义/远程壁纸，这次解码照样发生且结果随即被替换；随后首次刷新再解码一次，同一张图启动期解码两次。

**为什么上一轮漏判**：原始发现 `history/2026-09-04-full-audit.md §4.3` 标题即写「**DI 构造期 + 每次刷新**」，但 `85d2266` 的提交说明与改动只涉及 `UpdateBackgroundImageAsync` 的 Bundled 分支；上一轮（增量视角）按该提交记为 resolved，未回到原发现全文核对另一半。

**Recommendation**：构造期不做同步解码（或改由后台任务），并播种 `lastBackgroundSourceKey`/`lastDecodeTarget`，使首次刷新直接复用；若首帧需要非空位图，可让首次异步刷新尽早完成并保留窗口底色兜底。

**Recommendation validation**：Strongly Supported（刷新路径已有 `Task.Run` + 跳过守卫的现成模式可复用；未做计时，上轮同资产实测约 30–90 ms）。

**Suggested guard**：断言构造后 `BackgroundImageSource` 的取得不发生在调用线程（或断言构造期未调用 `bundledImageLoader`），并补一条「首次刷新不重复解码」的行为测试。

### AUD-XPLAT-001 — macOS 无可用 runner：游戏永远无法启动

- Category: cross-platform / product
- Severity: Medium
- Confidence: 90
- Status: open
- Disposition: Product Decision

**Evidence**：`Services/GameRuntime/GameRunnerDefinition.cs:33-40`（`Native.IsSupportedPlatform = OperatingSystem.IsWindows()`）、`:43-50`（`Umu = IsLinux()`）、`:53-60`（`Wine = IsLinux()`）；`GameRuntime.cs:89-92` 对不支持的 runner 直接跳过，`:121-127` 返回 `GameRuntimeLaunchFailure.NoRunnerSelected`；`GameLaunchService.cs:146-149` 映射为通用 `GameProcessStartFailed` 提示。产物由 `scripts/Build-Distribution.ps1:105-153` 生成，`README.md:32` 列为「实验性」，但全仓库无任何文档说明 macOS 不支持启动游戏（`grep -i macos docs/` 仅见通知计划与 publish 说明）。

**Impact**：osx-arm64 用户可安装/更新/修复，但点「启动游戏」必然失败且提示不解释原因；这是已发布产物上的核心功能缺失。

**Recommendation**：三选一并落到文档与 UI：（a）补 macOS runner；（b）下架 macOS 产物；（c）在启动失败路径显式提示「macOS 暂不支持启动游戏」并在 README/关于页声明。

**Recommendation validation**：Needs Product Decision（技术路径清晰，取舍属产品决定）。

**Suggested guard**：启动失败分支的平台化消息测试（若选 c）。

## Low Priority Findings

### AUD-ARCH-004 — 设置页重置确认未纳入 ModalHost

- Category: architecture / modal-contract ｜ Severity: Low ｜ Confidence: 85 ｜ Disposition: Fix

**Evidence**：`ViewModels/DialogsViewModel.cs:102` 的 `IsResetSettingsConfirmationVisible` 在 `Features/Shell/ShellLifecycle.cs:790-843` 的 `OnDialogsPropertyChanged` 无对应 case（该 switch 同步 11 个属性），`ViewModels/ModalKind.cs` 无该成员；而视图确实渲染在对话框层：`Views/MainWindowDialogsOverlay.axaml:493-499`。逐一遍历 17 个模态表面，仅此一处未注册。

**Impact**：确认框打开时 `ModalHost.Top` 仍为 `Settings`，Escape 走 `ShellLifecycle.cs:547-549` → 关掉下层设置页，确认框因独立 `IsOpen` 继续悬浮；`MainWindow.axaml:39-40` / `MainWindowSettingsOverlay.axaml:12` 的交互门禁对它也不生效。

**Recommendation**：加 `ModalKind` 成员 + `OnDialogsPropertyChanged` case + `TryHandleEscape` case（或走统一对话框工厂）。

**Recommendation validation**：Verified（行为由静态链路完整推得；未在运行中的应用里实操复现）。

**Suggested guard**：把 `MainWindowViewModelTests.WizardDialogs.cs:32` 的 Escape 测试改为枚举 `Enum.GetValues<ModalKind>()`（现为手写 11 种，见 AUD-TST-005 的相邻问题）。

### AUD-SEC-005 — 清单空路径使临时文件写到游戏目录之外

- Category: filesystem_safety ｜ Severity: Low ｜ Confidence: 90 ｜ Disposition: Fix

**Evidence**：`Helpers/GamePathValidator.cs:33-45` 允许 `target == root`（`tests/GamePathValidatorTests.cs:34-42` 把空路径返回根钉为预期）→ `Features/GameOperations/DownloadExecutor.cs:82` `GetTempName(GetSafePath(gamePath, file.Path))` → `:376-379` 拼出 `<游戏目录>.tmp` → `Services/FileDownloadService.cs:57-61` 建父目录、`:124` 写入。可达性：`Models/LocalGameContracts.cs:41` 的 `Path` 缺字段即为空串，`ManifestDiffCalculator.cs:203-221/:225-246` 只做 `GetSafePath` + stat，不校验非空，`NeedDownload` 直接进下载器。

**Impact**：远端清单一个空路径条目即可在游戏目录的父目录写一个以游戏文件夹名命名的 `.tmp`（可覆盖同名既有文件），随后安装阶段 `File.Move` 目标为目录抛 `IOException`，操作以失败告终。越界写 + 操作失败，无代码执行。

**Recommendation**：取临时名之前拒绝 `target == root` 或空相对路径。

**Recommendation validation**：Verified（链路逐段读通；空路径→根的语义有现成测试背书）。

**Suggested guard**：清单含 `path` 为空 / 为 `.` / 为 `sub/..` 时操作以本地化错误失败且不落任何文件。

### AUD-MTN-011 — 两处仓库自述 MUST 不变量无机械守卫

- Category: maintainability / guard ｜ Severity: Low ｜ Confidence: 88 ｜ Disposition: Add Guard

**Evidence**：实例 A（清单键序）——`Models/LocalGameContracts.cs:35-39` 与 `Services/OfficialHashService.cs:25-31` 均注明序列化键序 MUST 与官方清单一致；哈希函数有规范向量测试，但无任何测试断言 `ManifestFile`/`GameLauncherConfig` 的序列化键序（`grep` 全测试目录零命中）。重排属性不会编译失败、不会破坏本仓库自读（Vc 由显式字段数组计算），只会让我们写出的 `manifest.json` 与官方启动器键序不同。实例 B（克隆完整性）——`Models/LauncherSettings.cs:158-203` 复制构造函数是手工清单，注释自述「漏一行会静默浅拷贝」，`GameRuntimeSettings.cs:25-31` 同理；`DeepClone` 是 `NormalizeSettings`（`LauncherSettingsService.cs:146`）与 `SettingsEditor`（`:37/:48/:50/:55/:57/:76`）的必经路径，现有测试仅覆盖 `GameRuntime` 为 null 一例。

**Impact**：A 为与官方启动器的互读契约（一旦重排，官方启动器可能判安装损坏）；B 为新增设置项漏加一行时该设置静默回默认值（用户可见的「设置不生效」）。

**Recommendation**：各加一条 3 行契约测试——序列化 `ManifestFile` 断言 JSON 键序；反射遍历 `LauncherSettings` 公共可写属性、逐个改值后断言克隆体同步。

**Recommendation validation**：Strongly Supported（两条断言均确定性、无框架语义风险）。

### AUD-MTN-013 — 死公共 API 会重定向全局日志

- Category: maintainability / dead-code ｜ Severity: Low ｜ Confidence: 90 ｜ Disposition: Fix

**Evidence**：`Services/LocalizationService.cs:183-184` `public static ... GetLanguageOptions() => GetLanguageOptions(new LocalizationService());`——全仓库唯一命中是定义自身，真实调用点用 `(localizer)` 重载（`SettingsOptionsViewModel.cs:22`、`DialogsViewModel.cs:291`）。无参构造链 `LocalizationService:90-93` → `new LocalDiagnostics()` → `LocalDiagnostics.cs:31-36` 建指向临时目录的 `UnifiedLogger` 并写入进程级静态 `syncLogger`（`:23/:41`）。

**Impact**：未触发的地雷——一旦有人调用，此后全进程 `LogSync` 落点被改到临时目录，`unified.log` 与「导出日志」失去内容。

**Recommendation**：删除无参重载。

**Recommendation validation**：Verified。

### AUD-DOC-001 — PRIVACY.md 未覆盖崩溃快照

- Category: documentation / privacy ｜ Severity: Low ｜ Confidence: 90 ｜ Disposition: Fix

**Evidence**：`Services/Diagnostics/CrashReportStore.cs:18-20/:31-36` 写 `%LOCALAPPDATA%\Cafe Launcher\CrashReports`（主目录不可写降级临时目录），`:47-87` 含崩溃 ID/时间/版本/构建 SHA/OS/UI 文化/异常类型与完整异常 `ToString`（`:214-223` 仅替换用户目录），`:163-212` 保留 10 份 / 30 天——与日志的 5 MB × 4 轮转策略不同。`PRIVACY.md:20-29` 表与 `:33` 保留段只描述日志与导出 ZIP，全文无「崩溃」「CrashReports」字样，「最后更新」仍为 2026-08-29。ADR-019 内部记录完整。

**Impact**：用户可见的隐私政策漏一类本地数据（无遥测事实不变，属披露完整性）。

**Recommendation**：表中加一行 + 保留段补一句。

**Recommendation validation**：Verified。

**修复（`16a5129`）**：本地数据表补「崩溃报告快照」一行（异常类型与详细信息、应用版本、构建 SHA、操作系统与界面语言，用户目录已替换为 `%USERPROFILE%`）；保留段补写 `CrashReports` 目录、10 份 / 30 天与临时目录降级；「最后更新」改为 2026 年 9 月 9 日。

### AUD-DOC-002 — 文档索引/表格漂移三处

- Category: maintainability / doc-drift ｜ Severity: Low ｜ Confidence: 90 ｜ Disposition: Fix

**Evidence**：A) `CONTEXT.md:176-195` ADR 索引止于 ADR-016，而 `docs/design/adr/` 有 ADR-017/018/019/020（后两条被 `design-system-spec.md:141/143` 引用），`:168` 的 P3 状态亦未含关于分区与崩溃窗口。B) `CLAUDE.md:76` 称 `Build-Distribution.ps1` 构建「both distribution formats」（该脚本无安装器步骤；`AGENTS.md:20-21` 与 `release.yml:265-275` 均为独立脚本/job）；`CLAUDE.md:93` 覆盖层顺序漏掉向导 500（`design-system-spec.md:138`、`CONTEXT.md:139` 与 `MainWindow.Styles.axaml` 实值均为 100/200/500/1000）。C) `PROJECT_CONVENTIONS.md:211-231` §12 表未列 `Directory.Packages.props:16` 的 `Avalonia.Controls.ColorPicker` 12.1.2 与 `:19` 的 `AvaloniaUI.DiagnosticsSupport` 2.2.3（`THIRD-PARTY-NOTICES.md:13/:24` 已列）；既有守卫 `InstallerContractTests.cs:500-537` 只校验已存在的行。

**Impact**：按 CONTEXT.md 当索引的维护者可能复用已占用的 ADR 编号；按 CLAUDE.md 操作会用错打包脚本或插入被 500 遮挡的新覆盖层；§12 表继续漂移。

**Recommendation**：三处一并更正；可选把守卫扩展为「ADR 索引覆盖 adr 目录」「§12 表覆盖 props 全部 PackageVersion」。

**Recommendation validation**：Verified。

**修复（`16a5129`）**：CONTEXT.md 索引补 ADR-017…020 四行、P3 完成状态补 014/017/018/019/020；CLAUDE.md 发布流程改为「`Build-Distribution.ps1` 出跨平台归档 + 独立 job 下载并校验 Inno 后用 `New-WindowsInstaller.ps1` 出安装器」、覆盖层顺序补向导 500；§12 表补两行。验证：`InstallerContractTests` 28/28（含 §12 版本一致性与 Inno 最低版本两条文档守卫）+ 单元全量 1484 过 / 2 跳。

### AUD-TST-004 — 卸载安全闸口无聚焦测试

- Category: testing / critical-path ｜ Severity: Low ｜ Confidence: 90 ｜ Disposition: Add Guard

**Evidence**：`Features/GameOperations/GameUninstallService.cs:177-180` 的「游戏运行中拒绝卸载」在测试目录零命中（`grep GameIsRunning` 只命中同名方法测试）；`GameUninstallServiceTests.cs` 仅 3 例（锁定文件、目录缺失、重复调用）。驱动器根未覆盖（`InstallationOperationStateTests.cs:445-463` 只覆盖 `LocalApplicationData` 一例）；`:198-200` 的「路径不存在→保护」在 `ValidateAsync` 中不可达（`:151-154` 先返回 `GamePathMissing`），属无测试的死防御分支。删除语义（部分失败、幂等、仅删清单内文件）覆盖良好。

**Impact**：两道「不该删」的闸口无回归保护。

**Recommendation**：补 3 例：运行中→`GameRunning`；驱动器根→`GamePathProtected`；不存在路径→`GamePathMissing`。

**Recommendation validation**：Strongly Supported（`IGameProcessTracker` 为接口，手写 stub 即可）。

### AUD-TST-005 — 测试等待无超时 + csproj 抑制理由不实

- Category: testing / determinism ｜ Severity: Low ｜ Confidence: 85 ｜ Disposition: Add Guard

**Evidence**：`tests/Cafe.Launcher.Avalonia.Tests/Cafe.Launcher.Avalonia.Tests.csproj:20-26` 以「测试等待已全部有界」为由整体抑制 `xUnit1051`，但存在无超时等待：`ToastHostViewModelTests.cs:529/:619`（`await exitDelayStarted.Task`）、`SettingsCategoryTests.cs:373/:382/:387`、`MotionVisibilityTests.cs:31/:39/:87/:118`（`await pendingExit`）。全仓无 `xunit.runner.json`、测试工程无 `Timeout`，唯一兜底是 `build.yml` 的 `timeout-minutes: 40`。（其余被列出的处所在事件处理器内部、测试随后主动释放，已剔除。）

**Impact**：生产回归若不再触发这些 TCS，红测试变成 40 分钟挂起而非快速失败。

**Recommendation**：给这些等待加 `.WaitAsync(5s)` 或设全局 `Timeout`。

**Recommendation validation**：Strongly Supported。

### AUD-REL-006 — 两处 best-effort catch 无日志

- Category: reliability / diagnostics ｜ Severity: Low ｜ Confidence: 85 ｜ Disposition: Fix

**Evidence**：A) `Services/ClickCodeService.cs:44-48/:66-74` 全静默（类无 `LocalDiagnostics`），而 `App.axaml.cs:62-70` 专门 try/catch 包裹 `SaveClickCode` 并写 Warn——该方法永不抛异常，该兜底对内部失败永不可达。B) `Features/ResourcePanel/ResourcePanelUidService.cs:127-143` catch 返回空串，调用方 `:102-112` 把空串当「无 Cookie UID」静默回落，无法区分「未配置」与「读不出」；仓库对同类场景有明确惯例（`LogViewerDialogViewModel.cs:170-176`）。

**Impact**：归因码写入失败与 Cookie 库读取失败均无日志无提示，支持侧零证据。

**Recommendation**：注入 `LocalDiagnostics` 记 Warn，或按惯例在 catch 处注明「豁免：…」。

**Recommendation validation**：Verified。

## Advisory（不单列 ID，供后续择机处理）

- **性能**：`GamePathValidator.GetSafePath` 每条清单项逐段 stat（`Helpers/GamePathValidator.cs:47-77`，调用点 `ManifestDiffCalculator.cs:210/:233`、`DownloadExecutor.cs:259/:300/:324`、`LocalInstallationStateStore.cs:443`）——同目录链重复校验，可做前缀记忆；`RemoteContentViewModel` 横幅每次刷新重读重解码（`:158-175/:537-550`，壁纸已有身份守卫、横幅没有）；检查/安装/卸载阶段的逐文件进度回调无节流（`GameOperationsViewModel.cs:374-386`，下载字节路径已有 100 ms 合流）；自定义壁纸文件夹扫描在 UI 线程（`BackgroundViewModel.cs:447-452/:528-531`）；本地清单每次刷新全量解析 + 逐条 MD5（`LocalInstallationStateStore.cs:305-356`，`LauncherCoreService.cs:77`）。以上均为结构性观察，本轮未计时。
- **跨平台**：非 Windows 下「再次启动拉起已有窗口」为静默 no-op（`App.axaml.cs:429-437`、`CrossProcessLaunchBridge.cs:77-93`；`--launch-game` 转发已实现）；`ResourcePanelUidService.cs:153-163` 硬编码 Windows Cookie 库路径（Linux/macOS 恒空、静默回落）；`LanguageFontFamilyService.cs:9-12` 仅 Windows 字体族（测试钉住，属有意）；macOS 产物未签名/未公证（`release.yml:105-153` 无 codesign/notarytool，置信度 65，未实测 Gatekeeper）。
- **测试**：`ClickCodeService` 保存路径 0/26 行；`SettingsViewModel.CheckForUpdatesAsync` 0%（3 个分支未验证，底层服务与启动期变体已覆盖）；`GameCompatibilityPaths` 0/15 行（Wine/UMU 前缀布局契约无测试）；`DirectoryWriteProbeTests` 每例泄漏一个临时目录；`GameDownloadServiceTests.cs:545-547` 用早返回代替 `Assert.SkipUnless`（非 Windows 上假绿）；`GameOperationsViewModelTests.cs:743` 测试名承诺断言进度图标、实际只断言面板可见。
- **依赖/供应链**：RID 专属发布还原在 CI 显式豁免 locked mode（RID 闭包不受锁约束）——AGENTS.md 与 `Directory.Build.props` 已记录的已知限制，本轮维持。

## Verified Strengths（本轮实测/实读背书）

- **门禁与 CI**：`verify.ps1` exit 0（数值见 Executive Summary）；`gh run view --log` 实测 CI 单元 **1486 过 / 0 跳**（本机 2 例符号链接测试因权限跳过，CI runner 以管理员运行故执行）——即 `GamePathValidator` 的重解析点防护在 CI 中真被验证；HEAD push run `34322709781` success。
- **供应链**：17 处 `uses:` 全 40 位 SHA；顶层 `permissions: contents: read`，仅发布 job `contents: write`；`RestoreLockedMode: 'true'` + 三份 lock 提交；AppImage 工具 SHA-256 校验、Inno Setup `gh release verify-asset` 实证；`dotnet list package --vulnerable --include-transitive` → 无已知漏洞包；无 `NuGet.config` 额外源。
- **文件系统/路径安全**：`GamePathValidator` 的根前缀 + 逐段重解析点校验（含 Windows/Unix 大小写差异）、`LocalInstallationStateStore` 提交前二次校验并拒绝根路径、卸载的受保护路径清单、安装器 `InitializeUninstall` 的所有权标记 + `{app}` 存在性校验、旧 NSIS 桥的 `InstallLocation` 匹配。
- **下载完整性**：CRC64 校验失败即删除重试（`FileDownloadService.cs:141-159`），空/缺失 hash 不可能匹配（失败而非跳过）；`verifiedHashes` 跨轮次复用且未命中时安装期整读复核；`NeedDownload ⊆ ManifestFiles` 经 `GameResultMerge` 的 `processed` 种子证明。
- **进程执行**：全部 `ArgumentList` 结构化参数 + `UseShellExecute=false`（`GameRuntime.cs:257-272`、`CrashReporterLauncher.cs:23-32`、`ShellFolderOpener.cs:23-34`）；外链仅 http/https/mailto 白名单。
- **测试隔离与确定性**：`TestUserDataIsolation` 链接进两个工程并以源契约测试禁止其他文件直读 `LocalApplicationData`；两程序集全局串行；静态可调状态（去抖窗口、动画时长、CultureInfo、主题变体）均在 `finally` 还原；黄金截图固定墙钟/文化/SHA/窗口尺寸，基线 1:1 契约测试存在；无 mocking 框架。
- **架构边界**：非 Shell 特性之间无具体类型引用（仅 Shell 聚合为 AGENTS.md 明示例外）；`ShellLifecycle.Wire/Unwire` 28 处订阅逐一配对；DI 全单例、无 transient 捕获、逆序释放正确；`LocalizedTextCatalog` 有配对释放。
- **持久化**：settings / download checkpoint / notice state 全走 `AtomicJsonFileStore`（临时文件 + 替换）；`LauncherSettingsService` 信号量串行 + 旧字段兼容 + 非法值归一。

## Resolved / Superseded Since Previous Audit

- `AUD-REL-005` → `d9f2184`（`CrashReportBootstrap` + 10 例测试）；`AUD-MTN-010` → `78c7d67`（§10 基线清单 + 1:1 契约守卫）——本轮复核为最终形态。
- `AUD-DOC-001` / `AUD-DOC-002` → `16a5129`（本轮发现、同日修复）：PRIVACY.md 补崩溃快照披露；CONTEXT.md ADR 索引补 017…020 并更新 P3 状态；CLAUDE.md 修正发布流程与覆盖层顺序（补向导 500）；PROJECT_CONVENTIONS §12 补两个已声明包。验证：`InstallerContractTests` 28/28 + 单元全量 1484 过 / 2 跳 / 1486 总计（与审计基线一致）。
- 维持 4 项有意暂缓/接受项：`AUD-ARCH-003`（deferred）、`AUD-MTN-001`（deferred）、`AUD-DEP-002`（accepted-risk，年度重审）、`AUD-TST-001`（deferred / No Action）。
- **重开**：`AUD-PERF-003`（上一轮记为 resolved；本轮回到原发现全文核对，构造期一半仍在，见上）。

## Decisions Required

1. **AUD-XPLAT-001**：macOS 去向（补 runner / 下架产物 / 显式声明不支持启动）——需产品决定。
2. 其余 10 项均为可直接执行的 Fix / Add Guard，无阻塞决策。

## Recommended Priorities

1. `AUD-PERF-003`（Medium，用户可感，且是重开项）→ 与 `AUD-SEC-005`、`AUD-ARCH-004` 一并作为下一批修复。
2. `AUD-MTN-011` / `AUD-TST-004` / `AUD-TST-005` 三条守卫（各 1–3 行测试或注解），把本轮的漂移类与回归类发现转为机械保护。
3. `AUD-DOC-001` / `AUD-DOC-002` / `AUD-MTN-013` / `AUD-REL-006` 收尾清理。
4. `AUD-XPLAT-001` 决策后再决定是否补对应守卫。

## Audit Method and Limitations

- 本轮为自源码重读的 full 审计：4 条并行只读通道 + 主审独立复核；所有报告级发现均由主审回到源码逐段验证（含对并行通道结论的剔除：例如把「事件处理器内部的 await」从无界等待清单中剔除、把固定 `.tmp` 命名判定为跨会话续传的必要设计而非缺陷）。
- 未实跑：`Build-Distribution.ps1` / `New-WindowsInstaller.ps1`（本增量未触碰打包脚本；有效性由 beta.7 六资产与 CI 绿灯背书）；未执行真实外网更新下载全链路；未在非 Windows 平台实跑（平台行为由代码与 CI 背书）。
- 性能项均为结构性判断，未做计时；`AUD-PERF-003` 的耗时引用上一轮同资产实测值。
- 覆盖率数值为本轮单次实跑（行 85.25% / 分支 92.12%），与上一轮记录（85.09% / 92.08%）的差异属正常抖动与新增测试，不作为发现。
- 审计过程触发的 `packages.lock.json` RID 段改写已 `git restore` 还原，工作树最终仅含审计产物。
