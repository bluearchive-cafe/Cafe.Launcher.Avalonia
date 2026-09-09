# 仓库审计报告（当前状态）

- 审计日期：2026-09-09（full 全量审计 · 第二轮：自源码重读）
- 审计对象：`cffbd4d`（工作树干净）；上一基线 `f6d44a7`（同日 full），增量 1 提交 / 12 文件
- 审计方式：repository-audit 流程（full 模式：6 领域全过 + 4 条并行只读通道 + 主审独立复核 + 门禁实跑 + CI 日志实证 + 台账全量对账）
- 历史报告：`.repository-audit/history/`（最近：2026-09-09 full-r2 / 2026-09-09 full）

## 当前结论

**0 Critical / 0 High；2 Medium + 7 Low open，另有 4 项有意暂缓/接受。** 与上一轮的差异源于审计方式：上一轮以增量为主，本轮按 full 模式自源码重读，因此重新打开了一个只修了一半的旧发现（`AUD-PERF-003`），并浮出一批增量视角看不到的项。其中 2 项文档类 Low（`AUD-DOC-001` / `AUD-DOC-002`）已随 `16a5129` 修复。全量门禁本机实跑通过（`verify.ps1` exit 0：Debug 0 警告 0 错误；单元 1484 过/2 跳；Headless 167/167；合并覆盖率 行 85.25% / 分支 92.12%；Release win-x64 0 警告 0 错误；Resx 18/18）。CI 实证：HEAD push run `34322709781` success，CI 单元 1486 过 / 0 跳。

## Open 项

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-PERF-003 | Medium | open（重开） | 内置壁纸仍在 DI 构造期于 UI 线程同步全尺寸解码；启动期同一 PNG 解码两次 |
| AUD-XPLAT-001 | Medium | open | macOS 无任何可用 runner，游戏永远无法启动，且只给通用失败提示 |
| AUD-ARCH-004 | Low | open | 设置页重置确认未纳入 ModalHost：Escape 关掉下层设置页而非该确认 |
| AUD-SEC-005 | Low | open | 清单空路径条目使下载临时文件写到游戏目录之外 |
| AUD-MTN-011 | Low | open | 两处 MUST 不变量无守卫（清单 JSON 键序 / settings 深克隆完整性） |
| AUD-MTN-013 | Low | open | 死公共 API `GetLanguageOptions()` 无参重载会重定向全局日志到临时目录 |
| AUD-TST-004 | Low | open | 卸载安全闸口（游戏运行中、驱动器根）无聚焦测试 |
| AUD-TST-005 | Low | open | 约 15 处测试等待无超时；csproj 抑制 xUnit1051 的理由不实 |
| AUD-REL-006 | Low | open | 两处 best-effort catch 无日志，一处使调用方 Warn 兜底成死代码 |
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分 |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` 下限断言 |

## 两项 Medium

### AUD-PERF-003 — 内置壁纸仍在 DI 构造期于 UI 线程同步解码（重开）

- **原发现**：`history/2026-09-04-full-audit.md §4.3` 标题即写「DI 构造期 **+ 每次刷新**」；`85d2266` 只改了 `UpdateBackgroundImageAsync` 的 Bundled 分支（改 `Task.Run`），构造函数那一半原样保留。上一轮（增量视角）记为 resolved，未回到原发现全文核对另一半。
- **现状证据**：`ViewModels/BackgroundViewModel.cs:122` 构造函数体同步调用 `bundledImageLoader()` → `:647-662` `new Bitmap(stream)` 全尺寸解码 `Assets/launcher-background.png`（实测 2560×1388 / 3,396,889 字节）；该单例由 `App.axaml.cs:72` 在 UI 线程、首帧前经 DI 解析（`ServiceConfiguration.cs:123/141/143`）。构造函数从不写 `lastBackgroundSourceKey`（仅 `:185/:221/:245`），故首次刷新的跳过守卫（`:153-160`）必然失配，Bundled 分支再解码一次 → 启动期同一 PNG 解码两次。
- **修复方向**：构造期不做同步解码（或改后台任务），并播种 `lastBackgroundSourceKey`/`lastDecodeTarget` 让首次刷新复用；守卫建议：断言构造期未调用 `bundledImageLoader` + 「首次刷新不重复解码」行为测试。

### AUD-XPLAT-001 — macOS 无可用 runner：游戏永远无法启动

- **证据**：`GameRunnerDefinition.cs:33-60`（`Native=IsWindows`、`Umu`/`Wine`= `IsLinux`）→ `GameRuntime.cs:89-92` 跳过不支持者 → `:121-127` `NoRunnerSelected` → `GameLaunchService.cs:146-149` 映射为通用 `GameProcessStartFailed`。`README.md:32` 仅标注「实验性」，全仓库无文档说明 macOS 不支持启动游戏。
- **影响**：osx-arm64 用户可安装/更新/修复，但点启动必然失败且提示不解释原因。
- **处置**：Product Decision（补 runner / 下架产物 / 显式声明不支持）。

## 七项 Low（要点）

- **AUD-ARCH-004**：`DialogsViewModel.IsResetSettingsConfirmationVisible` 未进 `OnDialogsPropertyChanged`（`ShellLifecycle.cs:790-843`）、`ModalKind` 无成员，但渲染在对话框层（`MainWindowDialogsOverlay.axaml:493-499`）——17 个模态表面仅此一处未注册，Escape 误关下层设置页。
- **AUD-SEC-005**：`GamePathValidator` 允许 `target==root` → `DownloadExecutor.cs:82` 取临时名得 `<游戏目录>.tmp` → `FileDownloadService.cs:57-61/:124` 在游戏目录之外落盘；远端清单空 `path` 条目即可触发（越界写 + 操作失败）。
- **AUD-MTN-011**：`LocalGameContracts.cs:35-39` 的清单键序 MUST 与 `LauncherSettings.cs:158-203` 的克隆 MUST 均无机械守卫（各有 3 行测试可封）。
- **AUD-MTN-013**：`LocalizationService.cs:183-184` 死重载会经 `new LocalDiagnostics()` 把进程级静态日志目标改到临时目录。
- **AUD-TST-004**：卸载「游戏运行中拒绝」「驱动器根保护」无测试；`IsSystemProtectPath` 的不存在路径分支在 `ValidateAsync` 中不可达。
- **AUD-TST-005**：`ToastHostViewModelTests`/`SettingsCategoryTests`/`MotionVisibilityTests` 共约 15 处 `await tcs.Task` 无超时，回归会挂住 CI；csproj 的抑制理由「等待已全部有界」不实。
- **AUD-REL-006**：`ClickCodeService` 两处静默 catch（使 `App.axaml.cs:62-70` 的 Warn 兜底成死代码）；`ResourcePanelUidService` 读 Cookie 失败静默回落空串。

## 已修复（本轮审计产物落地）

- **AUD-DOC-001**（`16a5129`）：PRIVACY.md 本地数据表补「崩溃报告快照」一行、保留段补 CrashReports 目录与 10 份 / 30 天，最后更新改为 2026-09-09。
- **AUD-DOC-002**（`16a5129`）：CONTEXT.md ADR 索引补 ADR-017…020 并更新 P3 状态；CLAUDE.md 修正发布流程描述与覆盖层顺序（补向导 500）；PROJECT_CONVENTIONS §12 补 `Avalonia.Controls.ColorPicker` 与 `AvaloniaUI.DiagnosticsSupport`。验证：`InstallerContractTests` 28/28 + 单元全量 1484 过 / 2 跳。

## Advisory（择机处理，未单列 ID）

- 性能：`GetSafePath` 逐段 stat 的 N+1 元数据调用；横幅每次刷新重读重解码（壁纸已有身份守卫）；检查/安装/卸载阶段逐文件进度回调无节流；自定义壁纸文件夹扫描在 UI 线程；本地清单每次刷新全量解析 + 逐条 MD5。
- 跨平台：非 Windows 下再次启动不拉起已有窗口（`--launch-game` 转发已实现）；资源面板 UID 硬编码 Windows Cookie 库路径；仅 Windows 字体族（有意，测试钉住）；macOS 产物未签名/未公证。
- 测试：`ClickCodeService` 保存路径 0/26 行、`SettingsViewModel.CheckForUpdatesAsync` 0%、`GameCompatibilityPaths` 0/15 行；`DirectoryWriteProbeTests` 泄漏临时目录；`GameDownloadServiceTests.cs:545-547` 用早返回代替 `Assert.SkipUnless`；`GameOperationsViewModelTests.cs:743` 测试名与断言不符。

## Verified Strengths（本轮实测背书）

- `verify.ps1` exit 0（数值见上）；CI 日志实测单元 1486 过 / **0 跳**——本机跳过的 2 例符号链接测试在 CI 真被验证。
- 供应链：17 处 `uses:` 全 SHA 固定；最小权限；locked mode + 三份 lock；AppImage 工具 SHA-256、Inno `verify-asset`；`dotnet list package --vulnerable --include-transitive` 无已知漏洞包。
- 路径安全：根前缀 + 逐段重解析点校验（含平台大小写差异）、提交前二次校验、卸载受保护路径清单、安装器所有权标记与旧 NSIS 桥匹配。
- 下载完整性：CRC 失败即删重试、空/缺 hash 不可能匹配、跨轮次哈希复用 + 安装期整读复核、`NeedDownload ⊆ ManifestFiles` 经 `processed` 种子证明。
- 进程执行全 `ArgumentList` + `UseShellExecute=false`；外链仅 http/https/mailto。
- 测试隔离（`TestUserDataIsolation` + 源契约测试）、全局串行、静态可调状态 `finally` 还原、黄金截图固定墙钟/文化/SHA 且 1:1 契约守卫在位、无 mocking 框架。
- 架构边界：非 Shell 特性间无具体类型引用；`ShellLifecycle.Wire/Unwire` 28 处订阅配对；DI 全单例、逆序释放正确。
- 持久化全走 `AtomicJsonFileStore`；设置服务信号量串行 + 旧字段兼容 + 非法值归一。

## Resolved Since Previous Audit

- `AUD-REL-005` → `d9f2184`；`AUD-MTN-010` → `78c7d67`（均复核为最终形态）。
- `AUD-DOC-001` / `AUD-DOC-002` → `16a5129`（本轮发现、同日修复并复核：`InstallerContractTests` 28/28 + 单元全量 1484 过 / 2 跳）。
- **重开**：`AUD-PERF-003`。

## Decisions Required

1. `AUD-XPLAT-001`：macOS 去向——需产品决定。
2. 其余 9 项均为可直接执行的 Fix / Add Guard，无阻塞决策。

## Recommended Priorities

1. `AUD-PERF-003` → `AUD-SEC-005` → `AUD-ARCH-004`（用户可感 / 越界写 / 模态误路由）。
2. `AUD-MTN-011` + `AUD-TST-004` + `AUD-TST-005` 三条守卫（各 1–3 行），把漂移与回归转为机械保护。
3. `AUD-MTN-013` / `AUD-REL-006` 收尾。
4. `AUD-XPLAT-001` 决策后补对应守卫。

---

### 审计方法说明

- 模式：full，自源码重读（非增量复核）。风险画像按 `desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）。
- 4 条并行只读通道（安全/文件系统、架构/可维护性、测试、性能/跨平台）+ 主审独立复核；报告级发现全部由主审回到源码逐段验证，并剔除了并行通道的过报（事件处理器内部的 await、跨会话续传所需的固定 `.tmp` 命名）。
- 门禁实证：`verify.ps1` 本机实跑 exit 0；覆盖率按文件合并两份 cobertura 计算（合并值以 `coverage.ps1` 输出为准）；CI 结论取自 `gh run view --log` 的实测计数。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`；未执行真实外网更新下载全链路；未在非 Windows 平台实跑；性能项为结构性判断，未计时（`AUD-PERF-003` 引用上一轮同资产实测值）。
- 审计过程触发的 `packages.lock.json` RID 段改写已 `git restore` 还原，工作树最终仅含审计产物。
