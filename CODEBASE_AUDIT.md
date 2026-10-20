# 项目审计状态

`CODEBASE_AUDIT.md` 是审计发现、接受风险和待办优先级的唯一事实源。只维护当前仍有决策价值的内容；已解决事项从正文移除，由 Git 历史保存，不另建台账、快照或候选清单。

## 当前结论

- 审计日期：2026-10-20（增量复核；基线仍为 2026-09-24 全量审计）
- 审计基线：`f27ce001018984428f8e5ca9ae8c83ba11c861bd`
- 范围：文件结构、架构、可靠性、安全与供应链、依赖、测试、性能、可维护性
- 项目画像：.NET 10 / Avalonia 12 桌面游戏启动器，含独立 Windows 自更新 helper；Windows 为支持平台，macOS/Linux 构建为实验性
- 健康度：良好，没有 Critical 或 High 发现
- 当前开放：2 项（`AUD-ARCH-015`、`AUD-TEST-015`）
- 已接受风险：10 项 Low

## 开放发现

| ID | 证据 | 影响 | 严重度 | 置信度 | 处置 | 完成条件 |
| --- | --- | --- | --- | --- | --- | --- |
| `AUD-ARCH-015` | `src/Cafe.Launcher.Core` 的 `Models/` 之外仍有 47 个顶层 public class，`AssemblySplitContractTests.CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 把它们钉成显式声明集。其中 `FileDownloadService`、`NoticeStateService`、`ProtonBuildDiscovery`、`RuntimeVersionProbe` 等仍是服务实现；`GameProcessTracker`、`LinuxProcessScanner`、`RemoteBodyReader` 属确需公开的一类。 | 后端实现细节跨程序集可见，UI/测试可绕过窄接口直接依赖实现。声明集能挡住顺手新增 public 实现，但挡不住「这个类型本来就不该 public」。 | Low | 高 | 服务实现继续按类型逐个收回 `internal`；数据辅助类型与进程级类型留在表内并写明理由。 | 声明表不再包含服务实现，且每条保留项都有注释说明为何必须公开。 |
| `AUD-TEST-015` | `MainWindowHeadlessTests.SaveSettings_FromSettingsPage_ReportsSuccessInsteadOfThreadFailure`（`tests/Cafe.Launcher.HeadlessTests/MainWindowHeadlessTests.Settings.cs:287`）在 2026-09-28 负载最重的一次全量 `verify.ps1` 中失败：`Assert.Single(context.ViewModel.Toasts.ActiveToasts)` 收到空集合；随后多次全量与单跑均通过。 | 该断言检查设置页保存后恰有一条成功提示。空集合意味着提示未发出或已被收走。偶发失败会让 CI 在无法复现时变红，且 `coverage.ps1` 在第一套失败时直接退出，覆盖率闸口连带失去结论。 | Medium | 中 | 按测试侧时序问题调查：该断言对「保存命令异步落地 → Toast 入栈」没有有界等待，与 `AUD-TEST-014` 同族。定位后给等待加上限并说明观测点，或把断言收到确定的接缝。 | 连续 10 次全量 `verify.ps1`（含覆盖率插桩）该用例全绿，或给出并修复可复现的根因。 |

## 已接受风险

| ID | 当前取舍 | 重开条件 |
| --- | --- | --- |
| `AUD-DEP-002` | 单元测试使用 `xunit.v3.mtp-v2` 4.0.2，Headless 经 `VersionOverride` 保留 3.2.2：`Avalonia.Headless.XUnit` 12.1.4 使用 xUnit 3.2.2 的扩展 API，而 xUnit 4 的扩展接口有破坏性变更；其 nuspec 只声明最低 `[3.2.2, )`。两套都经显式 `mtp-v2` 包统一在 MTP 2.5.1。取舍见 `PROJECT_CONVENTIONS.md` §12 与 `docs/research/mtp-migration.md`。 | `Avalonia.Headless.XUnit` 发布支持 xUnit 4 扩展 API 的版本并通过 Headless 套件验证后，升级 Headless 并把并行化声明迁到 `[assembly: Parallelization(...)]`。 |
| `AUD-ARCH-007` | 系统代理指纹变化会释放旧 handler，使在途下载批次最多失败一轮；下一轮新租约可恢复。引用计数方案并发复杂度更高。 | 出现不可恢复下载失败，或重试无法切换到新 handler。 |
| `AUD-PERF-001` | 实际更新对约 1.09 GiB 客户端做 CRC64 自愈校验：冷读约 5.9 秒、热读约 0.9 秒；只在真实更新发生，保留内容自愈价值更高。 | 受管理客户端接近 10 GiB，按现有系数冷读将接近 54 秒。 |
| `AUD-PERF-004` | 五张 460×220 横幅每轮重解码约 10 ms 后台 CPU，显著低于同路径网络成本；位图缓存会增加所有权与失效复杂度。 | 横幅数量或尺寸使单轮解码接近 100 ms，或刷新路径其他成本降至同量级。 |
| `AUD-SEC-001` | URL 校验与实际拨号间仍有 DNS 重绑定窗口；风险受 GET-only、80/443 端口和响应不回传约束。 | 增加非 GET、敏感内网动作，或把响应内容返回给不可信调用方。 |
| `AUD-SEC-002` | 上游协议要求资源面板 UID 位于 HTTPS 查询串；应用日志会剥离查询串，但代理和服务端访问日志仍可见。 | 上游支持请求体传输，或 UID 的敏感级别提高。 |
| `AUD-SEC-004` | System 代理模式向 WinINet 配置的代理发送当前用户默认凭据；这是同用户信任边界内的兼容性取舍。 | 支持来自更低信任来源的代理配置，或默认凭据不再是产品要求。 |
| `AUD-SEC-006` | 直接路径容忍 CGNAT/Fake-IP 地址段以兼容 Clash/mihomo 与部分 CDN；请求仍限 GET、80/443，并剥离跨主机重定向凭据。 | 能可靠识别代理接管而不影响双栈/Fake-IP 用户，或请求能力扩大。 |
| `AUD-PKG-001` | Linux 三格式按「随 GitHub Release 分发的上游 vendor 包」定位：RPM 安装在 `/opt`、无 `Source`、关闭 `%__os_install_post`；满足可安装、依赖可解析、元数据可校验，不宣称满足 Fedora/openSUSE/Debian 官方仓库收录规范。 | 计划提交任一官方仓库（含 OBS/COPR/AUR 正式收录）时，按该仓库规范重审。 |
| `AUD-LNX-001` | Unix 单实例锁套接字仍放在 `$XDG_DATA_HOME/cafe-launcher`，没有迁到规范更对应的 `$XDG_RUNTIME_DIR`（ADR-034、ADR-049）。运行期文件因此留在持久数据根；内核原子锁与跨进程转发语义不变。 | `$XDG_RUNTIME_DIR` 成为运行时信号的硬性要求，或单实例/转发协议本身需要重做。 |

## 已验证的工程状态

- 架构以垂直 `Features/` 和共享 `Services/Helpers/Models` 为主；未发现新的非 Shell feature 具体类型越界。
- Windows updater 已独立成项目，下载包与 helper 应用前均验证 SHA-256；便携换位在新版本启动成功前保留备份、失败可恢复旧版。
- GitHub Actions 第三方 action 使用完整 SHA，默认权限 `contents: read`；NuGet 使用中央版本、锁文件、locked restore 与 `NuGetAuditMode=all`。2026-09-24 的依赖查询未发现已知漏洞或弃用包。
- 下载、校验和进度回调有并发上限与回归守卫。
- 2026-10-20 数据根收口：ADR-048 的 Linux 四类 XDG 根在发布前由 ADR-049 撤销，所有平台保留单一数据根；Linux 使用小写 `cafe-launcher`，旧 `Cafe Launcher` 首次启动逐项并入；`LauncherDataRoot` 恢复单一 `Root`，`LauncherDataRootTests` 与 `LauncherDataRootMigrationTests` 已同步。Unix 套接字留在数据根，登记为 `AUD-LNX-001`。

## 验证边界

- **最近一次记录（2026-10-20，Windows）**：完整 `pwsh ./verify.ps1` 通过。本地化契约通过；Debug/Release 构建均为 0 警告、0 错误；单元测试 2413 项（2399 通过 / 14 项平台跳过），Headless 238 项全部通过，Release 资源契约 23 项全部通过。手写代码行覆盖率 86.24%（18672/21652）、分支覆盖率 92.25%（3310/3588），高于基线 0.8560 / 0.9180。该记录不替代后续改动的验证；Linux 专属路径仍受下列平台验证边界约束。
- **Windows 安装器**：真实 ISCC 编译已在装有 Inno Setup 7 的本机通过，产物 `Cafe.Launcher_setup.exe` 生成。安装器的**运行时**升级行为（`[InstallDelete]`、`skipifnotsilent`）仍无干净环境复现，结论来自 Inno 语义与 issue #23 的现场证据。
- **Linux 打包**：`.deb` 实构建并解包校验，Arch 完成 `makepkg` 全量构建与 `namcap` 无 Error；RPM 只做规格静态校验，真实依赖安装依赖发布 CI 的 Fedora 容器步骤。所有 Linux 包未在真实桌面做 GUI 启动与软件中心展示验证，openSUSE 未覆盖。
- **平台路径**：触屏标题栏拖动没有实机验证（Headless 的 `BeginMoveDrag` 是空实现）；日志轮转没有跑满真实 5 MB，结论来自 Serilog 源码语义与构造用例；Linux/Proton 真实运行和 Windows 自更新启动握手没有实机实验，便携换位恢复路径由真实目录集成测试覆盖。

## 维护规则

1. 新发现必须包含稳定 ID、证据、影响、严重度、置信度、处置和可验证的完成条件。
2. 同一根因沿用原 ID；不同根因使用新 ID。
3. 修复或接受后直接更新本文件：已解决项从正文移除，接受项移入「已接受风险」。Git 历史承担历史记录。
4. 只登记已验证且值得行动或明确接受的问题；候选想法、执行流水和纯风格建议不进入本文件。
5. 每次审计同时刷新顶部日期、基线、计数、优先级和验证边界，确保整份文件描述同一个当前状态。
