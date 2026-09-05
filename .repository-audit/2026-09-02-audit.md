# 仓库审计报告（全仓库）

- 审计日期：2026-09-02
- HEAD：3a10080 `refactor(game-ops): 统一启动与快捷方式的游戏启动目标解析`
- 审计方式：repository-audit 流程（仓库发现 → 规则加载 → 六维并行审计 → 证据验证）
- 规模：src 约 207 个 .cs 文件 / 约 27.6k 行 C#；tests 约 129 个 .cs 文件
- 历史报告：上一次 diff 范围审计（2026-08-25）已归档至 `.repository-audit/2026-08-25-diff-audit.md`

## 摘要

仓库整体健康度**良好**。无关键（Critical）问题：无硬编码密钥、无远程遥测（符合规范）、无 TLS 旁路、端点全 HTTPS、下载与卸载路径有主动的路径穿越防护、进程启动不经过 shell、反序列化全部使用 System.Text.Json。测试质量高（覆盖率基线 84.30% 行 / 88.99% 分支，远高于 50% 门槛），关键业务（安装、启动、卸载、设置、更新、本地化）均有行为级测试。

高置信度问题（≥80）共 6 项，集中在**聚合点膨胀**（`ShellLifecycle`、`MainWindow.axaml.cs`）与**少量复制粘贴模式**；另有若干加固级建议。

## 1. Critical Issues

无。

## 2. Architecture

### 2.1 ViewModel 归属规则缺失（置信度 85）
- 证据：根 `ViewModels/` 放了 10 个 VM（含非 VM 的 `DesignTokenGrouping.cs`），而各 Feature 的 VM 在 `Features/*/ViewModels/`；根 VM 反向 import Features（`ViewModels/WindowChromeViewModel.cs:6-8`）。
- 影响：新 VM 该放哪里无规则可依；根目录实际是未命名的 "Shell feature"。
- 建议：把根 VM 并入 `Features/Shell/`，或在 AGENTS.md 明文规定"根 = 窗口级跨 Feature VM"；移走 `DesignTokenGrouping.cs`。

### 2.2 跨 Feature 横向依赖：Diagnostics → GameOperations（置信度 75）
- 证据：`Features/Diagnostics/DebugViewModel.cs` 有 `using ...Features.GameOperations`。
- 建议：在 Diagnostics（或共享 Services）定义窄接口由 GameOperations 实现，经 DI 注入。

### 2.3 App/MainWindow code-behind 是 Features/Shell 之外的第二个编排器（置信度 70）
- 证据：`App.axaml.cs`（395 行）驱动会话启停并直接推窗口状态；`Views/MainWindow.axaml.cs`（778 行）持有 `SystemTrayService` 并做窗口状态持久化；与 891 行的 `Features/Shell/ShellLifecycle.cs` 职责重叠。
- 建议：把窗口状态捕获/恢复与托盘接线收进 `IShellRuntime`，App 只负责启动容器。

### 2.4 View 向 VM 注入委托回调（轻度分层倒置，置信度 65）
- 证据：`Views/MainWindow.axaml.cs:159,161` 设置 `viewModel.LogViewer.PickExportDirectoryAsync = ...`；`SetupWizardViewModel.cs:55` 亦有同类约定。
- 建议：统一为 `IFilePickerService` 式抽象或单一 `WireChildren` 接缝。

## 3. Security

密钥扫描干净（仅翻译字符串误报）；无 TLS 旁路；`ExternalLinkService` 只放行 http/https/mailto 并显式拦截 `file://`；下载经 `DownloadSession.EnsureGamePath` 约束路径，卸载有删除根守卫。剩余为加固级：

### 3.1 项目级 `AllowUnsafeBlocks=true` 但代码无任何 unsafe 用法（置信度 75）
- 证据：`Cafe.Launcher.Avalonia.csproj:22`；src 内 grep 无 unsafe 块。
- 建议：移除该开关。

### 3.2 远端配置字段缺少使用前校验（置信度 60）
- 证据：`ResourcePanelApiClient.cs:72,106` 将远端 JSON 直接驱动 UI（URL/路径字段）。
- 建议：对远端 URL/路径字段做 scheme/前缀白名单校验。

### 3.3 日志导出使用 `UnsafeRelaxedJsonEscaping`（置信度 65，可接受）
- 证据：`DebugViewModel.cs:377`。仅影响本地导出文件。
- 建议：保持现状，注释说明导出非 HTML-safe 即可。

## 4. Performance

### 4.1 UI 线程同步解码 Bitmap（置信度 75）
- 证据：`ViewModels/BackgroundViewModel.cs:87`（`static path => new Bitmap(path)`），用于 `:127/:205/:260`。
- 影响：应用大背景图时 UI 卡顿，启动时最明显。
- 建议：`Task.Run` + `Bitmap.DecodeToWidth` 离线解码，仅回传 `IImage`。

### 4.2 UI 上下文中的阻塞式 `LogSync`（置信度 65）
- 证据：`Services/Diagnostics/LocalDiagnostics.cs:149,173`（`.GetAwaiter().GetResult()`），调用方含 `DialogsViewModel`、`MainWindow.axaml.cs:765` 等多处 UI 代码。
- 建议：改用已有的 `DebugAsync`/`MessageAsync` 异步变体。

### 4.3 代理模式下每个租约新建 handler+HttpClient（置信度 65）
- 证据：`Services/HttpClientFactory.cs:76-86`，Direct 模式外的 Auto/System 模式按租约新建。
- 建议：按代理配置缓存一个 handler，配置变更时失效。

### 4.4 非事件处理器的 `async void`（置信度 80）
- 证据：`Views/MainWindow.axaml.cs:761` `private async void CopyErrorDetailsToClipboard`。
- 建议：改为 `async Task`，在事件处理器内 try/catch 调用。

### 4.5（备注）启动/关闭的同步日志冲刷（置信度 60）
- `Program.cs:147,151,192` 属有意为之的启停边界阻塞，可保持现状。

已排查干净：轮播定时器全生命周期正确停止/释放；ManifestDiffCalculator 的重 I/O 带 `ConfigureAwait(false)` 跑在线程池；ViewModel 内文件 I/O 仅为存在性检查。

## 5. Dependencies

整体健康：全部包处于当前主线版本，未发现已知 CVE 或弃用包；`AvaloniaUI.DiagnosticsSupport` 正确限制为 Debug-only。建议运行 `dotnet list package --vulnerable --including-transitive` 做权威核验。

### 5.1 未启用 Central Package Management（置信度 99）
- 证据：无 `Directory.Packages.props`；Avalonia 在主 csproj 出现 6 次，测试包在两个测试项目重复声明。
- 建议：启用 CPM（零行为变更，消除版本漂移风险）。

### 5.2 无锁定文件 / 未强制 NuGetAudit（置信度 85）
- 建议：开启 `RestorePackagesWithLockFile` + `NuGetAudit`。

### 5.3 `Shirasagi0012.MaterialColorUtilities 0.2.0` 供应链成熟度（置信度 70）
- 单作者、0.x、低下载量的社区包直接进主进程。
- 建议：换用更广泛使用的库，或将 Material 色彩算法（小型、Apache-2.0）vendor 进仓库。

### 5.4 xunit.runner.visualstudio 3.1.5 与 xunit.v3 3.2.2 版本错位（置信度 65）
- 建议：升级 runner 至 3.2.x 对齐（CPM 后可一并锁齐）。

## 6. Testing

优秀。有效下限是 coverage.ps1 的基线回归门槛（84.30% 行 / 88.99% 分支，`coverage.ps1:7-8`），远高于 50% 名义阈值。`UiStyleContractTests`、`ResxResourceContractTests`、`DesignTokenContrastTests` 强制 XAML 令牌与本地化契约。测试为行为级而非形式化（如 `LauncherCoreServiceTests` 覆盖远端降级、取消传播、缺省回退）。

已验证覆盖充分：`GameDownloadServiceTests`（42 用例）、`DownloadExecutorTests`、`LauncherSettingsServiceTests`、`LauncherUpdateServiceTests`、`VersionComparerTests`、`InstallationOperationStateTests`（启动/卸载）、`LocalizationServiceTests`。

缺口（均为编排/管道类，仅被上层测试间接覆盖）：
- **`GameOperationJourney` 无专属测试**（置信度 80）— 安装/启动/卸载旅程状态机可能静默回归。建议补旅程级测试。
- **`DownloadSession`/`DownloadSessionFactory` 无直接测试**（置信度 75）— 建议补 checkpoint 恢复与取消用例。
- **`CrossProcessPollingListener` 无测试**（置信度 65）— 启动关键管道，建议补信号轮询与陈旧信号用例。
- 基线以字面量硬编码于 coverage.ps1（置信度 70）— 建议改为从上一次 CI 结果生成。

## 7. Maintainability

零 TODO/HACK/FIXME；死代码极少；注释面向维护者。主要风险集中在两个聚合文件与几组复制粘贴模式：

### 7.1 `ShellLifecycle` 上帝类（置信度 90）
- 证据：891 行、构造器 20 个协作者（`ShellLifecycle.cs:73`）、27 个 readonly 字段，独揽启动/刷新/设置保存/向导完成/面板切换/全部跨 Feature 订阅。
- 建议：按内聚类分组协作者（参照 `GameShortcutService.ShortcutEnvironment`），把更新检查与外观预览下沉到既有 `ShellStartup`/`ShellRefreshCoordinator` 接缝。

### 7.2 `MainWindow.axaml.cs` 四份近似的文件选择器 + 混合职责（置信度 90）
- 证据：`PickGameFolderAsync:567`、`PickBackgroundImageAsync:589`、`PickBackgroundFolderAsync:613`、`PickLogExportDirectoryAsync:630` 重复同一脚手架；动画方法（`:67-433`）与状态持久化混在 code-behind。
- 建议：抽一个 `PickAsync(kind, title, startLocation)`；动效逻辑移入独立 behavior 类。

### 7.3 字符串化本地化键（置信度 75）
- 证据：266 处 `T("...")`/`F("...")` 裸字符串，高频键重复 5–6 次；契约脚本只校验 resx 侧。
- 建议：集中为 `LocalizationKeys` 常量类或走生成的 Designer 属性。

### 7.4 "打开路径/URL" 逻辑四份分叉（置信度 70）
- 证据：`ExternalLinkService.cs:30`、`WindowChromeViewModel.cs:47-51`、`MainWindow.axaml.cs:652`、`GameShortcutService.cs:280-296`；仅 ExternalLinkService 有 scheme 白名单。
- 建议：统一经由 `ExternalLinkService` 或共享 `IShellOpen` 接缝。

### 7.5 `GameOperationJourney` 每个操作重复 busy 脚手架（置信度 65）
- 证据：`SetBusy(true)` ×5，`StartGameAsync:66`、`InstallOrUpdateAsync:200`、`RepairAsync:219` 等共用同一 try/catch/finally 模板。
- 建议：一个 `RunOperationAsync(Func<Task>)` 模板方法。

### 7.6 `GameShortcutService` 三个构造器重复平台接线（置信度 60）
- 证据：`GameShortcutService.cs:62-83` 逐行重建 `ShortcutEnvironment.ForCurrentPlatform()`（`:55-59`）。
- 建议：删除中间重载，仅保留环境注入构造器并更新测试。

## 8. Technical Debt

- 债务总量低：无 TODO 标记、无注释掉的代码块、无过时 API 使用。
- 主要"结构债"是 2 个聚合文件（`ShellLifecycle` 891 行 / `MainWindow.axaml.cs` 778 行）持续吸引跨功能改动——每次跨 Feature 变更都会触碰它们，与其继续膨胀不如尽早拆分。
- 上次审计（2026-08-25）发现的"移除功能残留"类问题在本仓库当前状态未复查到同类新增。

## 优先级建议

1. **P1 — 低成本高收益**：移除 `AllowUnsafeBlocks`；修复 `async void CopyErrorDetailsToClipboard`；后台图离线解码；`LogSync` 调用点改异步。
2. **P1 — 流程加固**：启用 CPM + lock file + NuGetAudit；评估 vendor `MaterialColorUtilities`。
3. **P2 — 结构**：拆分 `ShellLifecycle`（协作者分组）与 `MainWindow.axaml.cs`（统一 PickAsync、动效外移）；收敛 4 处 shell-open 逻辑；补 `GameOperationJourney`/`DownloadSession`/`CrossProcessPollingListener` 测试。
4. **P3 — 规范**：在 AGENTS.md 明文 ViewModel 归属规则；本地化键常量化；断开 Diagnostics → GameOperations 横向依赖。

## 修复执行状态（2026-09-02）

上述 P1–P3 各项已全部按阶段实施并逐阶段提交，验证为全量构建 + 单元/Headless 测试通过：

| 阶段 | 提交内容 | 关键产物 |
|---|---|---|
| P1 低成本 | 移除 `AllowUnsafeBlocks`（`LibraryImport` 改经典 `DllImport`）、`async void` 修复、`LocalDiagnostics.LogAsync` 静态异步入口、5 处异步上下文阻塞日志改 await、背景图 `Task.Run` 离线解码 | `WindowsAnimationSettingsProvider`、`LocalDiagnostics`、`BackgroundViewModel` 等 |
| P1 流程 | `Directory.Packages.props`（CPM）、`Directory.Build.props`（lock file + NuGetAudit all）、提交 3 份 `packages.lock.json`；`dotnet list package --vulnerable` 确认无漏洞包 | CPM + 锁定 + 审计 |
| P2 结构 | `ShellPresentationFamily` 聚合 12 个呈现协作者（`ShellLifecycle`/`MainWindowViewModel` 构造器 20→9 参）；MainWindow 四选择器收敛为 `PickFolderAsync`/`PickImageFileAsync`；`ShellFolderOpener` 统一 3 处 shell-open；新增 24 个测试（`GameOperationJourneyTests` 13、`DownloadSessionTests` 8、`CrossProcessPollingListenerTests` 3） | 编排层测试盲区补齐 |
| P3 规范 | 共享 `Services/IGameOperationActivity` 断开 Diagnostics→GameOperations；`DesignTokenGrouping` 移入 Helpers；AGENTS.md 明文 ViewModel 归属规则；`LocalizationKeys` 551 常量 + 生成脚本，重写 392 处裸键字面量并把契约测试反转为"生产源码禁止裸键字面量" | 编译期键拼写保证 |

### 审计修正（实施中发现）

- **发现 3.1（移除 AllowUnsafeBlocks）的原始证据有误**：初审计称"代码无 unsafe 用法"，实际 `WindowsAnimationSettingsProvider` 的 `[LibraryImport]` 源生成 P/Invoke 需要编译器允许 unsafe（初审计的 grep 因工作目录问题漏检）。修复方式为将该 P/Invoke 改为经典 `DllImport`（bool 封送无需 unsafe），结论不变、证据修正。
- **发现 5.4（xunit.runner.visualstudio 3.1.5 与 xunit.v3 3.2.2 版本错位）不可操作**：NuGet 上不存在 runner 3.2.x 版本线（3.x 线最新即 3.1.5，之后直接跳到 4.0.0 主版本），两者本就不共享版本线，维持现状。
- **发现 5.3（MaterialColorUtilities 供应链）处置决议**：其 Quantize/HCT/Scheme/DynamicColors 是整套色彩科学核心（估算数千行算法），vendor 移植风险大于收益；采用 lock file + NuGetAudit(all) + CPM 集中锁版作为缓解。
- **发现 7.6（GameShortcutService 三构造器重复）暂缓**：中间 `Func<string,bool>` 重载被 10+ 处测试用作注入缝，收益/改动比偏低，未纳入本次范围。
