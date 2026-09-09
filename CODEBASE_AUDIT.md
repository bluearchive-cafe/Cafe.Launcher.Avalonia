# 仓库审计报告（当前状态）

- 审计日期：2026-09-09（full 全量审计 · 第二轮：自源码重读）
- 审计对象：`cffbd4d`（工作树干净）；上一基线 `f6d44a7`（同日 full），增量 1 提交 / 12 文件
- 审计方式：repository-audit 流程（full 模式：6 领域全过 + 4 条并行只读通道 + 主审独立复核 + 门禁实跑 + CI 日志实证 + 台账全量对账）
- 历史报告：`.repository-audit/history/`（最近：2026-09-09 full-r2 / 2026-09-09 full）

## 当前结论

**0 Critical / 0 High；本轮 1 Medium + 7 Low 已全部修复（2026-09-09 同日落地），无 open 项**，另有 4 项有意暂缓/接受。与上一轮的差异源于审计方式：上一轮以增量为主，本轮按 full 模式自源码重读，因此重新打开了一个只修了一半的旧发现（`AUD-PERF-003`），并浮出一批增量视角看不到的项。修复后全量门禁本机实跑通过（`verify.ps1` exit 0：Debug 0 警告 0 错误；单元 1521 过/2 跳；Headless 167/167；合并覆盖率 行 85.16% / 分支 92.16%；Release win-x64 0 警告 0 错误；Resx 18/18）。CI 实证：`cffbd4d` push run `34322709781` success，CI 单元 1486 过 / 0 跳（本机跳过的 2 例符号链接守卫在 runner 上真执行）。

## Open 项（无）

本轮 8 项（1 Medium + 7 Low）全部修复并带守卫，见「本轮修复」。以下 4 项为有意暂缓/接受：

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分 |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` 下限断言 |

## 本轮修复（8 项，同日落地）

### AUD-PERF-003 — 内置壁纸仍在 DI 构造期于 UI 线程同步解码（重开）

- **原发现**：`history/2026-09-04-full-audit.md §4.3` 标题即写「DI 构造期 **+ 每次刷新**」；`85d2266` 只改了 `UpdateBackgroundImageAsync` 的 Bundled 分支（改 `Task.Run`），构造函数那一半原样保留。上一轮（增量视角）记为 resolved，未回到原发现全文核对另一半。
- **现状证据**：`ViewModels/BackgroundViewModel.cs:122` 构造函数体同步调用 `bundledImageLoader()` → `:647-662` `new Bitmap(stream)` 全尺寸解码 `Assets/launcher-background.png`（实测 2560×1388 / 3,396,889 字节）；该单例由 `App.axaml.cs:72` 在 UI 线程、首帧前经 DI 解析（`ServiceConfiguration.cs:123/141/143`）。构造函数从不写 `lastBackgroundSourceKey`（仅 `:185/:221/:245`），故首次刷新的跳过守卫（`:153-160`）必然失配，Bundled 分支再解码一次 → 启动期同一 PNG 解码两次。
- **修复（`ff71eef`）**：构造函数不再解码；初始壁纸由首次 `UpdateBackgroundImageAsync` 在线程池解码后填入（窗口显示主题底色兜底），来源跳过键随首次刷新播种，启动期同一 PNG 只解码一次。黄金截图测试改为显式驱动首次刷新（基线未变，仍在容差内）。新增用例：构造期零解码 + 首次刷新恰好一次 + 同源再刷新不再解码；两向实测（注释掉黄金测试的背景加载 → `shell-default` 失败）。

### 其余 7 项

| ID | 修复 | 提交 | 守卫/验证 |
|---|---|---|---|
| AUD-SEC-005 | `GamePathValidator.GetSafeFilePath` 拒绝归一到游戏根的条目；下载/安装/差异共 7 处调用点改用之 | `60f14fe` | GamePathValidatorTests 5 例 + DownloadExecutorTests 参数化 3 例（无残留文件） |
| AUD-ARCH-004 | 重置确认纳入 `ModalKind` + 同步分支 + Escape 分支 | `d1a4421` | Escape 测试改为枚举 `Enum.GetValues<ModalKind>()`；两向实测 |
| AUD-MTN-011 | 清单/配置序列化键序断言 + `LauncherSettings` 克隆完整性反射测试 | `f8d6329` | 两向实测：移动 `Vc` → 键序失败；删复制构造一行 → 克隆失败 |
| AUD-TST-004 | 卸载三道闸口用例（运行中/驱动器根/路径不存在）+ tracker 替身 | `f8d6329` | 3 例新增，全部执行通过 |
| AUD-TST-005 | 8 处门控等待加 `WaitAsync(5s)`；csproj 抑制理由改为与事实相符 | `f8d6329` | 全量套件回归通过 |
| AUD-MTN-013 | 删除 `GetLanguageOptions()` 无参重载（无调用点且会改全局日志目标） | `77ad317` | 编译期即验证无调用点 |
| AUD-REL-006 | `ClickCodeService` 三处 catch 与 `ResourcePanelUidService` 读 Cookie 的 catch 改记 Warn | `77ad317` | 全量套件回归通过 |

## 更早已修复（本轮审计产物落地）

- **AUD-XPLAT-001**（`c1b3d20` + `79db640` + 文档站 `f25d174`）：启动失败提示由通用文案改为具体原因（本地化 runner 名 + 可用性状态），并抽出共享的 runner 名/状态本地化映射；产品面明确为「macOS 只能安装/更新/修复，暂不支持启动游戏，也暂无支持计划」，写入 README 与文档站五处。
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
- `AUD-XPLAT-001` → `c1b3d20`（提示具体化）+ `79db640` 与文档站 `f25d174`（产品决定：macOS 暂不支持启动游戏且暂无支持计划，写入 README 与文档站）。
- `AUD-PERF-003` → `ff71eef`；`AUD-SEC-005` → `60f14fe`；`AUD-ARCH-004` → `d1a4421`；`AUD-MTN-011` / `AUD-TST-004` / `AUD-TST-005` → `f8d6329`；`AUD-MTN-013` / `AUD-REL-006` → `77ad317`。
- **重开并再次关闭**：`AUD-PERF-003`（上一轮记为 resolved，本轮回到原发现全文核对后重开，随 `ff71eef` 关闭）。

## Decisions Required

无阻塞决策。`AUD-XPLAT-001` 的产品决定已落地（macOS 暂不支持启动游戏且暂无支持计划）。

## Recommended Priorities

1. 本轮 8 项已全部落地；把分支推送并开 PR，让 `build.yml` 在 CI 上复核这批修复。
2. 维持 4 项有意暂缓/接受项至下一轮或年度审。
3. 若后续新增模态表面、清单字段或设置项，本轮新增的三条守卫会分别兜住：Escape 枚举测试、键序断言、克隆完整性反射测试。

---

### 审计方法说明

- 模式：full，自源码重读（非增量复核）。风险画像按 `desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）。
- 4 条并行只读通道（安全/文件系统、架构/可维护性、测试、性能/跨平台）+ 主审独立复核；报告级发现全部由主审回到源码逐段验证，并剔除了并行通道的过报（事件处理器内部的 await、跨会话续传所需的固定 `.tmp` 命名）。
- 门禁实证：`verify.ps1` 本机实跑 exit 0；覆盖率按文件合并两份 cobertura 计算（合并值以 `coverage.ps1` 输出为准）；CI 结论取自 `gh run view --log` 的实测计数。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`；未执行真实外网更新下载全链路；未在非 Windows 平台实跑；性能项为结构性判断，未计时（`AUD-PERF-003` 引用上一轮同资产实测值）。
- 审计过程触发的 `packages.lock.json` RID 段改写已 `git restore` 还原，工作树最终仅含审计产物。
