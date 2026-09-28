# 项目审计状态

`CODEBASE_AUDIT.md` 是审计发现、接受风险和待办优先级的唯一事实源。这里只维护当前仍有决策价值的内容；已解决事项从正文移除，由 Git 历史保存，不再建立独立台账、快照或候选清单。

## 当前结论

- 审计日期：2026-09-28（增量；基线内容仍为 2026-09-24 的全量审计）
- 审计基线：`f27ce001018984428f8e5ca9ae8c83ba11c861bd`（`d7f5b10` 全量审计后仅追加单适配器接缝收敛；不影响下列发现）
- 增量复核：2026-09-24 对基线后两个依赖升级提交（Test.Sdk 18.10.1、xunit.v3 4.0.1）做了 delta 审计；Linux Arch 打包与安装器资产归组已在全量审计树内覆盖
- 范围：文件结构、架构、可靠性、安全与供应链、依赖、测试、性能、可维护性
- 项目画像：.NET 10 / Avalonia 12 跨平台桌面游戏启动器，含独立 Windows 自更新 helper
- 健康度：良好；没有 Critical 或 High 发现
- 当前开放：2 项（`AUD-ARCH-015`，2026-09-27 由 ADR-042 迁移收尾登记；`AUD-TEST-015`，2026-09-28 本轮登记的一次负载敏感的 Headless 偶发失败。其余开放发现已修复并提交：AUD-ARCH-014、AUD-SEC-007、AUD-ARCH-001、AUD-MAINT-008，见 Git 历史）
- 已接受风险：9 项 Low
- 2026-09-25 文档状态更新：按用户要求已将官方启动器 v1.7.2 与本项目的静态对比写入 `docs/research/official-launcher-v1.7.2-comparison.md`，原 `AUD-MAINT-002` 的“不入库”取舍不再成立；其余审计结论仍以 2026-09-24 基线为准。
- 2026-09-26 打包状态更新：Linux `.deb`/RPM/AppImage/Arch 的元数据、依赖声明与版本同步已按当轮复核补齐（Debian copyright、`changelog.Debian.gz` 与 `-1` 打包修订号，三格式共用 AppStream metainfo，RPM/Arch 声明自包含 .NET 仍需的系统库与真实许可证集，`release.ps1` 在版本提交内同步 Arch `_realver`/`pkgver`/`.SRCINFO`）；发布 CI 已加入 Fedora 容器 RPM 实装与 Arch `makepkg`+`namcap` 门禁，尚未随真实 tag 运行过。本轮发现均在同一变更内修复，不产生开放发现。
- 2026-09-27 分层重构更新：ADR-042 的 Core/UI 程序集拆分与其迁移清单已落地（`verify.ps1` 全绿：单元 2287、Headless 219、覆盖率 86.41%/92.83%）。收尾盘点把「Core 公开面仍为实现默认 public」登记为 `AUD-ARCH-015`——原来的缺口「没有守卫防止新增 public 实现」已在同一变更内补上守卫。
- 2026-09-27 命名收口更新：ADR-043 把工程目录、`.csproj`、`AssemblyName`、`RootNamespace`、源码命名空间与发行资产统一到同一个产品 token（宿主 `Cafe.Launcher`、表现层 `Cafe.Launcher.UI`、`Updater.Core` 的命名空间补齐 `.Core`），`AssemblyNamingContractTests` 逐条钉住这些关系。用户可见的一次性变化（exe 名、release 资产前缀、macOS bundle id）见 ADR-043；GitHub 仓库名保持不变。文档引用随之同步：历史 ADR 与 `docs/research` 的路径/标识符改为当前名（保留当时的决策叙述，被取代的条目加指向 ADR-043 的标注），新增 `DocumentationLinkContractTests` 钉住 Markdown 相对链接不再腐烂——改名那批一次性断掉了 39 条链接而此前无人察觉。本轮不产生新的开放发现。
- 2026-09-28 增量审计（基线 `5b1b487d`，含其后 3 个未推送提交）：对「依赖更新的行为变更」「迁移到多程序集后的缺陷与维护风险」「文档与实现的偏差」三条线各做了一轮定向复核，并实测了构建、两套测试、覆盖率与文档守卫。四个真实缺陷已修复并各自补上守卫（详见「已验证的工程状态」）：安装器 `SetupIconFile` 的断链（ADR-043 改名直接造成，发行断链）、崩溃报告丢失构建身份（程序集拆分直接造成）、标题栏拖动对非主指针抛异常（Avalonia 12.1.3 把抛出前移到调用点）、日志按大小轮转后基名不再存在（日志查看器全空、导出抛异常）。台账自身两处过期按原 ID 就地更正：`AUD-ARCH-015`、`AUD-DEP-002`。两处文档过度承诺已改写：DI 释放顺序（逆序**登记** → 逆序**解析**）、宿主「从不点名 UI 内部实现」（补上 pre-DI 崩溃路径这一条受准例外）。另补齐三处守卫缺口：`coverage.ps1` 的反空转根清单缺 `Cafe.Launcher.Updater.Core`、UI 公开面此前没有声明集（Core 有）、Headless 的程序集并行化形状与 xUnit 主版本没有配对守卫；`ReleaseNotesMarkdownViewer` 的守卫原先钉的是版本相关字面量，已改为结构性断言。本轮修复后开放发现仍为 1 项。

## 开放发现

| ID | 证据 | 影响 | 严重度 | 置信度 | 处置 | 完成条件 |
| --- | --- | --- | --- | --- | --- | --- |
| `AUD-ARCH-015` | `src/Cafe.Launcher.Core` 里 `Models/` 之外仍有 46 个顶层 public class，`AssemblySplitContractTests.CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 把它们钉成显式声明集（新增/移除都会红）。其中 `FileDownloadService`、`NoticeStateService`、`ProtonBuildDiscovery`、`RuntimeVersionProbe` 等仍是服务实现而非数据辅助类型；`GameProcessTracker`、`LinuxProcessScanner`、`RemoteBodyReader` 属「确需公开」的一类。 | 后端实现细节跨程序集可见，UI/测试可以绕过窄接口直接依赖实现；声明集能挡住「顺手加一个 public 实现」，但挡不住「这个类型本来就不该 public」——后者只能逐个收窄。 | Low | 高 | 消费方改经接口的那一批已于 ADR-042 完成，实现收回 `internal` 的批次也已把它能收的都收了；剩下的只能按类型逐个判断：服务实现继续收窄，数据辅助类型与进程级类型留在表内并逐条写明理由。 | 声明表里不再包含任何服务实现（只剩数据模型辅助类型与确需公开的进程级类型），且每条保留项都有注释说明为何必须公开。 |
| `AUD-TEST-015` | `MainWindowHeadlessTests.SaveSettings_FromSettingsPage_ReportsSuccessInsteadOfThreadFailure`（`tests/Cafe.Launcher.HeadlessTests/MainWindowHeadlessTests.Settings.cs:287`）在 2026-09-28 的一次全量 `verify.ps1` 中失败：`Assert.Single(context.ViewModel.Toasts.ActiveToasts)` 收到空集合（该次 headless 222 通过 / 1 失败，退出码 1）。同一提交在随后三次全量运行中各跑一遍（`test.ps1` 双套、手工插桩的单套、`verify.ps1` 双套）均 223 全绿；单独运行该用例亦通过。失败只出现在负载最重（单元套件刚跑完 + 覆盖率插桩）的那一次。 | 该用例断言的是「设置页保存后有且只有一条成功提示」，空集合意味着保存路径上的成功提示没发出来或已被收走。它的失败会让 CI 在无法复现的情况下变红——而 `coverage.ps1` 在第一套测试失败时直接 `exit 1`，覆盖率闸口连带失去结论。 | Medium | 中 | 先按测试侧时序问题调查：该断言对「保存命令的异步落地 → Toast 入栈」这一段没有有界等待（与 AUD-TEST-014 同族，`TestSourceWaitContractTests` 只覆盖 `Pending*` 命名的那一族，这条不在其中）。定位后要么给等待加上限并说明观测点，要么把断言收到确定的接缝上。 | 连续 10 次全量 `verify.ps1`（含覆盖率插桩）该用例全绿，或给出并修复了可复现的根因。 |

## 已接受风险

| ID | 当前取舍 | 重开条件 |
| --- | --- | --- |
| `AUD-DEP-002` | 单元测试已升 xunit.v3 4.0.1，Headless 项目经 `VersionOverride` 钉在 3.2.2。真正的原因是 `Avalonia.Headless.XUnit` **12.1.3** 对 `xunit.v3.extensibility.core` 声明**精确版本** `[3.2.2]`，升级会撞 NU1107/NU1605（不是「不兼容 xUnit 4」）；该约束沿依赖链下传为 MTP v1 1.9.1，与单元工程的 mtp-v2 2.4.0 并存，因此 MTP 迁移也受阻（`Microsoft.Testing.Extensions.CodeCoverage` 18.10.1 要求 MTP ≥ 2.x）。取舍已记录在 `PROJECT_CONVENTIONS.md` §12。 | 上游放宽 `xunit.v3.extensibility.core` 的精确版本约束后移除覆盖，并把程序集并行化配置迁到 xUnit 4 API（`[assembly: CollectionBehavior(DisableTestParallelization = …)]` 在 4.x 已 obsolete 且不可调用；配对关系由 `AssemblySplitContractTests.TestProjects_DeclareTheAssemblyParallelizationFormTheirXunitMajorSupports` 钉住）。 |
| `AUD-ARCH-007` | 系统代理指纹变化会释放旧 handler，使在途下载批次最多失败一轮；下一轮新租约可恢复，引用计数方案的并发复杂度更高。 | 出现不可恢复下载失败，或重试无法切换到新 handler。 |
| `AUD-PERF-001` | 实际更新对约 1.09 GiB 客户端做 CRC64 自愈校验：冷读约 5.9 秒、热读约 0.9 秒；只在真实更新发生，保留内容自愈价值更高。 | 受管理客户端接近 10 GiB，按现有系数冷读将接近 54 秒。 |
| `AUD-PERF-004` | 五张 460×220 横幅每轮重解码约 10 ms 后台 CPU，显著低于同路径网络成本；位图缓存会增加所有权和失效复杂度。 | 横幅数量或尺寸使单轮解码接近 100 ms，或刷新路径其他成本降至同量级。 |
| `AUD-SEC-001` | URL 校验与实际拨号间仍有 DNS 重绑定窗口；风险受 GET-only、80/443 端口和响应不回传约束。 | 增加非 GET、敏感内网动作，或把响应内容返回给不可信调用方。 |
| `AUD-SEC-002` | 上游协议要求资源面板 UID 位于 HTTPS 查询串；应用日志会剥离查询串，但代理和服务端访问日志仍可见。 | 上游支持请求体传输，或 UID 的敏感级别提高。 |
| `AUD-SEC-004` | System 代理模式向 WinINet 配置的代理发送当前用户默认凭据；这是同用户信任边界内的兼容性取舍。 | 支持来自更低信任来源的代理配置，或默认凭据不再是产品要求。 |
| `AUD-SEC-006` | 直接路径容忍 CGNAT/Fake-IP 地址段以兼容 Clash/mihomo 与部分 CDN；请求仍限 GET、80/443，并剥离跨主机重定向凭据。 | 能可靠识别代理接管而不影响双栈/Fake-IP 用户，或请求能力扩大。 |
| `AUD-PKG-001` | Linux 三格式按“随 GitHub Release 分发的上游 vendor 包”定位：RPM 安装在 `/opt`、无 `Source`、关闭 `%__os_install_post`；满足可安装、依赖可解析、元数据可校验，不宣称满足 Fedora/openSUSE/Debian 官方仓库收录规范。 | 计划提交任一官方仓库（含 OBS/COPR/AUR 正式收录）时，按该仓库规范重审。 |

## 已验证的工程状态

- 架构仍以垂直 `Features/` 和共享 `Services/Helpers/Models` 为主；未发现新的非 Shell feature 具体类型越界。
- Windows updater 已独立成项目，下载包与 helper 应用前均验证 SHA-256；便携换位在新版本启动成功前保留备份、失败可恢复旧版，更新版本与 release tag/包名已绑定。
- GitHub Actions 第三方 action 使用完整 SHA，默认权限为 `contents: read`；NuGet 使用中央版本、锁文件、locked restore 和 `NuGetAuditMode=all`。
- 2026-09-24 的依赖查询未发现已知漏洞或弃用包；Avalonia 12.1.3 等可用更新不构成当前缺陷。
- 下载、校验和进度回调有并发上限与回归守卫；本轮没有形成新性能发现。
- 2026-09-28 修复的四个缺陷，均已修复并补上能挡住下一次的守卫（不再是「只改代码」）：
  - 安装器 `SetupIconFile` 指到不存在的路径（ADR-043 改名把工程段改成了宿主，而图标早已随表现层迁走）。Inno 的该指令按 `.iss` 所在目录在编译期解析，因此 ISCC 直接失败、Windows 安装包产不出来。守卫：`InstallerContractTests.WindowsInstaller_SetupIconFileResolvesToACommittedIcon`。
  - 崩溃报告丢失构建身份：`CrashReport.Build` 从读宿主静态改成可选注入后，pre-DI 的四个装配点全部漏传，快照的 `AppVersion`/`BuildSha` 与崩溃窗口的版本/提交 100% 为空。守卫：`CrashReportTests.ProductionSources_PassBuildIdentityIntoEveryCrashReportSeam`。
  - 标题栏拖动未判 `IPointer.IsPrimary`：触摸接触也报 `IsLeftButtonPressed`，而 Win32 的 `BeginMoveDrag` 对非主指针同步抛出（12.1.3 把抛出从 Post 回调前移到调用点），dispatcher 策略把它升级为致命——触屏第二指会崩掉启动器。守卫：Headless 的多指接触用例。
  - 日志按大小轮转后基名 `unified.log` 不再被创建（Serilog 改开 `unified_001.log`），而 `LogFilePath` 一直暴露基名：日志查看器全空、诊断导出抛 `FileNotFoundException`。修复为解析当前活动文件；守卫：`DiagnosticsServicesTests` 4 条 + `LogExportServiceTests` 1 条。
- 2026-09-28 补齐的守卫缺口（此前无守卫，缺陷因此可以静默存在）：`coverage.ps1` 的反空转根清单补 `Cafe.Launcher.Updater.Core`（实测它的 10 个类本来就在比率里，缺的是「掉了会红」）；UI 公开面新增 `AssemblySplitContractTests.PresentationPublicSurface_IsTheDeclaredSet`（28 个顶层公开类型的显式声明集，此前只有 Core 有）；`TestProjects_DeclareTheAssemblyParallelizationFormTheirXunitMajorSupports` 钉住两套并行化 API 与 xUnit 主版本的配对。`ReleaseNotesMarkdownViewer` 的守卫原先钉版本相关字面量，已改为结构性断言（条目数 + 无裸标签）。
- 2026-09-28 更正的文档过度承诺：DI 释放顺序是逆序**解析**而非逆序**登记**（`AGENTS.md` 与 `LauncherCoreServiceCollectionExtensions` 的注释已改写；今天的顺序仍成立，因为表现层门面是第一个被解析的服务）；`AGENTS.md` 的「宿主从不点名 UI 内部实现」补上唯一受准例外（pre-DI 崩溃路径必须直接构造那几个类型并注入身份）；`AGENTS.md` 的「构建强制代码风格」写明边界——`EnforceCodeStyleInBuild` 只覆盖 `.editorconfig` 严重级别到 warning 的 IDE 诊断，空白/缩进与文件编码属 formatter-only，仓库没有任何 formatter 门禁，因此 `dotnet format --verify-no-changes` 今天会报出大量既有偏差（未接入 CI），只应作为「改动到的文件」的工具使用。
- 2026-09-28 清掉的重复实现与死代码：`BuildInfo.AvaloniaVersion`（拆分前由 `ShellViewModel` 读宿主静态；拆分后 UI 侧另写了一份 `ResolveAvaloniaVersion()`，宿主那份从此没有任何消费方）与 `App.axaml.cs` 里重复 `Program.SignalName` 值的未使用私有常量。

## 验证边界

2026-09-24 的修复序列（AUD-ARCH-014 → AUD-SEC-007 → AUD-ARCH-001 → AUD-MAINT-008）每个阶段都在其提交前实测：前三个功能阶段各跑过全量 `test.ps1`（单元 2176→2185 通过、15 跳过，Headless 199 通过），文档阶段仅改注释与 ADR 文本、由编译守卫。此前全量审计轮（基线内容）完成过 Debug 构建、覆盖率棘轮（手写代码行 85.77%、分支 92.17%）；覆盖率棘轮未在修复序列后重跑。

Linux/Proton 真实运行和 Windows 自更新启动握手没有做实机实验：便携换位恢复路径由真实目录集成测试覆盖（`UpdateApplierPortableApplyTests`），信任链绑定由表驱动测试覆盖，但 helper 在真实杀软/UAC 环境下的行为结论仍来自源码与既有测试。

2026-09-26 的 Linux 打包收口在本地完成实测：`.deb` 实构建并解包校验（copyright、`changelog.Debian.gz`、metainfo、desktop 分别通过 `appstreamcli`/`desktop-file-validate`），两次构建哈希一致；Arch 完成 `makepkg` 全量构建，PKGBUILD 与产物 `namcap` 无 Error。RPM 只做规格静态校验，真实依赖安装依赖发布 CI 新增的 Fedora 容器 `dnf` 步骤（该工作流尚未随真实 tag 执行）；所有 Linux 包未在真实桌面环境做过 GUI 实机启动与软件中心展示验证，openSUSE 未覆盖。

2026-09-28 的增量审计在本机完成实测：`verify.ps1` 退出码 0（Debug 构建 0 警告 → 本地化契约 → 覆盖率 → Release 构建 0 警告 → Release 测试 23 通过），单元 2306 通过 / 15 跳过、Headless 223 通过，覆盖率手写行 86.46% / 分支 92.98%（基线 0.8560/0.9180）。这轮共跑了四次全量：其中一次 Headless 出现一条不可复现的失败（记为 `AUD-TEST-015`），其余三次全绿。验证的边界同样明确：**Windows 安装器的真实 ISCC 编译没有执行**（本机未装 Inno Setup，`SetupIconFile` 的结论来自 Inno 官方文档的解析规则 + 新守卫），**日志轮转没有跑满真实 5 MB**（结论来自 Serilog `PathRoller`/`RollingFileSink` 源码语义与磁盘状态构造的用例），**触屏路径没有实机验证**（无头后端的 `BeginMoveDrag` 是空实现，该用例的价值在 Windows CI 上才体现为真实回归）。

## 维护规则

1. 新发现必须包含稳定 ID、证据、影响、严重度、置信度、处置和可验证的完成条件。
2. 同一根因沿用原 ID；不同根因使用新 ID。
3. 修复或接受后直接更新本文件：已解决项从正文移除，接受项移入“已接受风险”。Git 历史承担历史记录，不另建归档报告。
4. 只登记已验证且值得行动或明确接受的问题；候选想法、执行流水和纯风格建议不进入本文件。
5. 每次审计同时刷新顶部日期、基线、计数、优先级和验证边界，确保整份文件描述同一个当前状态。
