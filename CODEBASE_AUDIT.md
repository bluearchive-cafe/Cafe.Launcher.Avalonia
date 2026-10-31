# 项目审计状态

`CODEBASE_AUDIT.md` 是审计发现、接受风险和待办优先级的唯一事实源。只维护当前仍有决策价值的内容；已解决事项从正文移除，由 Git 历史保存，不另建台账、快照或候选清单。

## 当前结论

- 审计日期：2026-10-31（架构、性能、复用与迁移可行性复核，refactor 模式；安全与发行链路为关键路径抽样）
- 审计基线：`f27ce001018984428f8e5ca9ae8c83ba11c861bd`
- 本次源码版本：`2a12919246c9319eac93deb1b7f7689fbe9899da` 加审计开始时已有的工作区改动（含下载校验、UI 调整与视频背景）。本次只更新审计文档，不修复生产代码；保留未受范围影响的既有发现与接受风险。
- 范围：文件结构、架构、可靠性、安全与供应链、依赖、测试、性能、可维护性
- 项目画像：.NET 10 / Avalonia 12 桌面游戏启动器，含独立 Windows 自更新 helper；Windows 为支持平台，macOS/Linux 构建为实验性
- 健康度：已有明确分层与回归保护；本次未验证到 Critical 或 High 问题，新增两项 Medium 接线／生命周期问题；无需据此整体重写。
- 当前待处理：6 项（4 项 Medium、2 项 Low；5 项 `open`、1 项 `deferred`）
- 已接受风险：11 项 Low

当前优先级：关闭等待操作收尾（`AUD-REL-001`）→ 壁纸尺寸服务接线（`AUD-PERF-008`）→ 既有 Headless 时序问题（`AUD-TEST-015`）→ 公开实现收缩（`AUD-ARCH-015`）。Core 独立使用的配置来源（`AUD-ARCH-017`）保持此前暂缓状态，扩展辅助宿主前重新裁决。

## 开放发现

### AUD-PERF-009 — 视频 GL 资源反复重建后仍有 GPU 内存保留

- 严重度：Medium；状态：`open`。原型 8 轮分离／重建中，libmpv player、render context 与参数 arena 的拥有资源每轮释放后均归零，托管存活堆约 26–65 MB，进程私有内存热身后约 400 MB 稳定；没有证据把 1.1 GB 峰值归因为这些对象逐轮泄漏。
- 仍未关闭的边界：热身后约 6 轮 GPU 专用内存累计保留约 48 MB；默认合成与 DirectComposition 均复现，尚未定位驱动缓存还是 GL／合成生命周期保留。不能宣称已排除 GPU 泄漏。
- 当前措施：普通设置切换视频复用播放器与渲染上下文，仅在切回静态背景／清除／关闭时释放；隐藏、最小化和减少动效时暂停，合并帧请求并缓存未变的 Shell 内容。用户已要求作为普通 Release 设置并随包提供运行库，这不代表该性能发现已关闭。
- 完成条件：在随包 LGPL 运行库上对真实设置预览、保存、放弃、图片／视频反复切换做长时 GPU 分配跟踪，证明保留有界或修复对应资源生命周期；覆盖率与拥有资源计数归零不替代该证明。

| ID | 证据 | 影响 | 严重度 | 置信度 | 处置 | 完成条件 |
| --- | --- | --- | --- | --- | --- | --- |
| `AUD-ARCH-015` | `src/Cafe.Launcher.Core` 的 `Models/` 之外仍有 47 个顶层 public class，`AssemblySplitContractTests.CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 把它们钉成显式声明集。其中 `FileDownloadService`、`NoticeStateService`、`ProtonBuildDiscovery`、`RuntimeVersionProbe` 等仍是服务实现；`GameProcessTracker`、`LinuxProcessScanner`、`RemoteBodyReader` 属确需公开的一类。 | 后端实现细节跨程序集可见，UI/测试可绕过窄接口直接依赖实现。声明集能挡住顺手新增 public 实现，但挡不住「这个类型本来就不该 public」。 | Low | 高 | 服务实现继续按类型逐个收回 `internal`；数据辅助类型与进程级类型留在表内并写明理由。 | 声明表不再包含服务实现，且每条保留项都有注释说明为何必须公开。 |
| `AUD-TEST-015` | `MainWindowHeadlessTests.SaveSettings_FromSettingsPage_ReportsSuccessInsteadOfThreadFailure`（`tests/Cafe.Launcher.HeadlessTests/MainWindowHeadlessTests.Settings.cs:287`）在 2026-09-28 负载最重的一次全量 `verify.ps1` 中失败：`Assert.Single(context.ViewModel.Toasts.ActiveToasts)` 收到空集合；随后多次全量与单跑均通过，本次也通过。 | 该断言检查设置页保存后恰有一条成功提示。空集合意味着提示未发出或已被收走。偶发失败会让 CI 在无法复现时变红，且 `coverage.ps1` 在第一套失败时直接退出，覆盖率闸口连带失去结论。 | Medium | 中 | 保留调查，不能仅凭空集合认定缺少等待。当前命令在发出成功提示后还会等待 `SettingsSaved` 后续处理，提示又有自行退出计时；需要同时观察入栈和移除，优先注入可控时钟／延迟或从确定的事件接缝断言，再决定是否增加有界等待。本次未复现历史偶发根因。 | 连续 10 次全量 `verify.ps1`（含覆盖率插桩）该用例全绿，或给出并修复可复现的根因。 |

### AUD-REL-001 — 关闭准备未等待手动游戏操作结束

- 严重度：Medium；置信度：100（缺少等待已复现；未复现数据丢失）；状态：`open`；处置：Fix。
- 证据：`src/Cafe.Launcher.UI/Features/Shell/ShellLifecycle.cs:440–445` 调用同步的 `StopOperation(ProcessExit)` 后，只等待 `ShellRefreshCoordinator` 的刷新与 after-load 工作。经刷新触发的续传可能被这条等待链覆盖，但从安装按钮直接进入的任务没有登记在其中。`Features/GameOperations/GameDownloadService.cs:153–176,285–312,346–358` 取消并摘除活动会话，不提供可等待的终结任务。`src/Cafe.Launcher/App.axaml.cs:126` 以关闭准备任务作为宿主退出屏障。
- 复现：临时 xUnit 探针经既有 `ShellLifecycleTests` 夹具与 `StubGameOperationExecutor.InstallCompletion` 启动手动安装，确认后端已被调用且安装任务未完成，再调用真实 `ShellLifecycle.PrepareForShutdownAsync()`；关闭任务已完成，预期等待的断言失败。释放完成门后安装任务正常收尾；不访问真实游戏目录或外网。
- 影响：正常退出可能先释放窗口、容器、HTTP handler 与日志器，操作的取消处理、结果通知和 finally 却仍在执行。进程退出还可能截断这些收尾。现有原子落盘、退出保留检查点能降低后果，不能替代任务终结屏障；不把它描述为已证实的安装损坏。
- 建议：由操作所有者持有活动任务，区分「已请求取消」和「已终结」；关闭先拒绝新操作，再异步停止并等待任务退出，之后才允许释放依赖。不要用 `.Wait()` 阻塞 Dispatcher；保持用户停止／进程退出的检查点语义，以及卸载中不允许正常关闭的契约。
- 建议验证：Strongly Supported。完成条件／守卫：确定性完成门覆盖手动安装、修复、暂停、续传与取消后的延迟收尾；关闭任务在收尾前保持未完成，依赖释放后没有结果通知或持久化写入。生产下载链路与宿主退出集成需一并验证。

### AUD-PERF-008 — 壁纸构造链丢弃窗口尺寸服务

- 严重度：Medium；置信度：100；状态：`open`；处置：Fix。
- 证据：`src/Cafe.Launcher.UI/ViewModels/BackgroundViewModel.cs:83–94` 的四参数构造器接收 `IWindowMetricsService`，转交给最终构造器时漏传它。公开的 DI 构造器经过该重载，因此最终字段为 null，`PhysicalSizeChanged` 未被订阅（117–122 行），`GetPhysicalSize()` 始终返回 1920×1080 兜底（153–154 行）。最终构造器直注尺寸服务的测试无法保护这条生产转交链。
- 复现：临时 xUnit 探针使用同一四参数构造路径注入计数版尺寸服务，断言事件订阅数应为 1，实际为 0。全部既有测试与完整验证仍通过，说明缺口在构造接线覆盖。
- 影响：远端、自定义或文件夹壁纸的按窗口物理尺寸解码与放大／DPI 变化重解码失效；小窗口可能多解码像素，大窗口可能放大较小位图而模糊。内置壁纸刻意按原始分辨率解码，不受这条尺寸策略影响。本次没有测量实际内存或帧时间增量。
- 建议：在该重载的构造转交中传递 `windowMetrics`，保留最终构造器作为统一初始化点。
- 建议验证：Strongly Supported。完成条件／守卫：覆盖生产构造链的订阅与释放；经真实 DI 登记和窗口尺寸服务，验证首次加载使用实际尺寸，显著增大窗口／DPI 后重解码，内置壁纸仍不重复解码。

### AUD-ARCH-017 — Core 网络偏好依赖表现层草稿，缺席时静默使用默认设置

- 严重度：Low；置信度：100；状态：`deferred`（用户要求暂缓）；处置：Architecture Decision。
- 证据：`src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs` 的 HTTP 登记闭包与末尾 `SavedSettingsSnapshot` 在每次租约／请求时通过容器查找 `ISettingsDraftOwner`，用它提供 HTTP/2 与代理偏好；没有草稿所有者时直接 `CreateDefaults`，不读取 Core 自己的设置服务。生产实现 `SettingsEditor` 位于 UI；Core-only 默认回退在源码中明确用于测试和辅助宿主。
- 复现：临时探针仅调用 `AddLauncherCore`，通过真实设置服务保存 `EnableHttp2=false`，新建直连租约的 `DefaultRequestVersion` 仍为 `2.0`，预期已保存偏好对应的 `1.1`。未发网络请求。
- 影响与边界：Core 的网络配置在运行时依赖表现层是否登记、是否完成快照初始化，缺失登记会被默认值掩盖。当前主程序由 Shell 在网络加载前播种快照，且 ADR-028 已有完整容器接线测试，因此不将其描述为当前主程序忽略用户设置。受影响的是 Core 独立使用和未来辅助宿主。
- 建议：明确 Core-only 是否承诺读取已保存偏好。若承诺，提供由 Core 拥有且显式初始化的偏好来源，UI 草稿同步继续履行 ADR-024；若不承诺，让组合 API 明示默认策略或要求来源，避免把缺少依赖等同于用户选择默认值。无需恢复被 ADR-028 否决的逐调用方推送。
- 建议验证：Needs Architecture Decision；完成条件／守卫：形成明确契约，并覆盖 Core-only、完整宿主、初始化前后与保存后新租约的配置行为。

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
| `AUD-UI-001` | 不提供 Windows 高对比度（`ThemeVariant.HighContrast`）变体：仓库没有 `HighContrast` 的 `ThemeDictionaries`，主题解析只在 Light/Dark 间切换。常规可读性由 AA token 契约（`DesignTokenContrastTests`）与 §8 豁免区走查承担；高对比度专项对比度不作承诺。见 `docs/design/design-system-spec.md` §6。 | 无障碍要求把系统高对比度列为硬性目标，或收到该模式下的可读性缺陷报告。 |

## 已验证的工程状态

- 2026-10-31 复核：宿主 → UI → Core → Updater.Core 的工程依赖与 ADR-042 一致；Core 生产引用只有 DI 与 Serilog 家族，不引用 Avalonia。宿主经 `LauncherPresentationSession` 进入 UI；独立 updater 只引用 Updater.Core。两套既有测试、程序集声明集和公开面守卫均通过。
- 后端复用分为两个层次：HTTP／协议、底层文件下载、安装状态、游戏运行器和自更新已经在 Core；完整安装／修复／卸载／启动工作流仍在 UI 的 `Features/GameOperations`，清单协议聚合仍在 UI 的 `Services/RemoteManifestService`。`DownloadSession`、`GameLaunchService`、`GameUninstallService` 直接依赖 `LocalizationService`；`IGameOperationExecutor` 和带 `Message` 的 `GameOperationResult` 也属 UI。这与当前渐进分层决策相容，但仅引用 Core 的新宿主不能直接复用完整工作流。
- 已保存设置是已知的运行时反向接缝：除 `AUD-ARCH-017` 的网络偏好外，Core 的 `SavedSettingsWriter` 构造器也需要由 UI 实现的 `ISettingsDraftOwner`。类型依赖方向正确不等于独立宿主已具备完整初始化与写入闭包；这属于复用契约需要演进的同一背景，不另登记主程序设置失效问题。
- 架构以垂直 `Features/` 和共享 `Services/Helpers/Models` 为主；未发现新的非 Shell feature 具体类型越界。
- 2026-10-20 专项未将 Shell 的聚合职责、`IGameOperationExecutor` 的转发接缝、`DownloadSession` 的体积判为应拆分的问题，本次维持这一判断：前者是规范声明的聚合边界，后两者受 ADR-021／ADR-022 的测试接缝与会话封装决策保护。文件安全、更新校验和确认后状态复查位于不同信任／时间边界，不因重复出现就视为过度防御。
- Windows updater 已独立成项目，下载包与 helper 应用前均验证 SHA-256；便携换位在新版本启动成功前保留备份、失败可恢复旧版。
- GitHub Actions 第三方 action 使用完整 SHA，默认权限 `contents: read`；NuGet 使用中央版本、锁文件、locked restore 与 `NuGetAuditMode=all`。2026-10-31 的 solution 直接与传递依赖漏洞查询未发现已知漏洞；本次未重新查询弃用状态。
- 下载、校验和进度回调有并发上限与回归守卫。
- 2026-10-20 数据根收口：ADR-048 的 Linux 四类 XDG 根在发布前由 ADR-049 撤销，所有平台保留单一数据根；Linux 使用小写 `cafe-launcher`，旧 `Cafe Launcher` 首次启动逐项并入；`LauncherDataRoot` 恢复单一 `Root`，`LauncherDataRootTests` 与 `LauncherDataRootMigrationTests` 已同步。Unix 套接字留在数据根，登记为 `AUD-LNX-001`。

## 性能与可复用性的工程判断

1. **现有性能保护应保留。** `LauncherCoreService` 并行发起六项远程读取并设整体 30 秒预算；`HttpClientFactory` 复用 handler。下载执行上限 10，安装验证上限按 CPU 数钳制到 1–8；下载进度以 100 ms 门控，文件校验以百分比去重。`Crc64Service` 使用池化缓冲和 slicing-by-8；下载后已验证哈希与修复规划的文件见证在后续安装阶段复用。不能通过删除自愈校验、无限增加并发或重复封装传输层来宣称性能提升。
2. **启动的可操作时间值得单独测量。** `LauncherCoreService.LoadAsync` 的 `Task.WhenAll` 同时等待游戏配置和运营／社交资源，Shell 在快照应用前保持忙碌。因而单个非关键资源迟缓也会拖住首次可操作时间，直至该项结束或远程预算取消；这是源码确定的等待依赖，不是本次测得的真实启动耗时。可评估先交付本地状态与关键游戏配置，再后台补充展示数据；需要更新快照、刷新及取消契约，避免旧结果覆盖新状态。先用受控延迟端点测量，再决定是否改变当前整批快照语义。
3. **有界并发不等于有界任务数量。** `DownloadExecutor` 为整个文件列表创建异步任务，再由信号量限流；下载与验证的等待任务／结果数组随文件数增长。本次未证明当前清单规模存在内存瓶颈，不登记缺陷。若后续管理大型清单，再比较固定 worker 或有界队列的分配量、取消成本和吞吐，保留确定性失败列表与进度语义。
4. **档案化支持换游戏构建，不代表多游戏并行。** `YostarGameProfile` 与 `LauncherProductProfile` 已集中身份信息，底层下载和路径处理可复用。但当前 DI 全单例、设置中的单一 `GamePath`、数据根中的单一续传文件、活动下载与游戏会话看护共同表达「单游戏、单会话」。多游戏产品还需要按游戏标识划分设置、检查点、操作互斥与生命周期；只增加档案会产生状态串用。没有该产品需求时继续保留现状。
5. **Shell 的集中不是独立缺陷。** `ShellLifecycle` 负责跨功能接线、刷新、公告与自更新编排；已有 `ShellRefreshCoordinator`、`ShellSelfUpdateCoordinator` 和模态登记接缝。应按任务所有权与可独立验证的流程演进，而不是按文件行数拆出更多转发类型。既有大量源码／XAML 声明集守卫能保护约定，`AUD-PERF-008` 同时说明关键 DI 转交仍需行为接线测试；覆盖率不能替代这类验证。

## 架构演进可行性

建议继续使用 .NET／Avalonia、MVVM 与垂直功能组织，先演进为**后台工作流可独立运行的模块化单体**。当前架构已经具备迁移起点：注入的数据根与产品档案、Core 的传输与持久化能力、操作 executor 测试接缝、独立 updater，以及现有回归套件。主要成本在设置和操作结果的语义迁移，而非移动目录。

| 路线 | 能解决什么 | 成本与适用条件 | 判断 |
| --- | --- | --- | --- |
| 保持当前分层，修复两项接线／生命周期问题 | 当前可靠性与壁纸行为 | 成本最低；单游戏桌面产品持续维护 | 立即值得做 |
| 将后台操作工作流逐步下沉 Core，UI 保留旅程与呈现 | CLI、辅助宿主、不同 UI 可复用完整操作；独立验证后台生命周期 | 中等；需要结构化结果、设置来源和生命周期契约 | 推荐演进方向 |
| 再拆 Application／Domain／Infrastructure 程序集 | 编译期隔离用例与外部实现，约束多个适配器 | 中高；有真实第二宿主、协议或存储适配器，且当前 Core 依赖成为负担时更有价值 | 条件成熟后再拆 |
| 独立后台进程、微服务、插件化或更换 UI 框架 | 进程隔离、跨进程共享任务或不受信任扩展等新能力 | 高；新增 IPC、协议版本、崩溃恢复、部署与诊断成本 | 当前没有证据支持投入 |

推荐拆成以下可分别验证、可回退的阶段；这是讨论方案，尚未实施或形成替代 ADR：

1. **先建立行为基线并修复具体缺口。** 处理 `AUD-REL-001`、`AUD-PERF-008`；记录启动到可操作、冷／热更新、修复吞吐、峰值内存及操作中关闭的基线。基线需声明硬件、清单、缓存和网络状态。本次覆盖率测试时长不作为产品性能基准。
2. **明确设置真值归属。** 若决定支持 Core 独立运行，由 Core 拥有可初始化、可读取的已保存快照，网络继续按使用时机拉取；UI 持有草稿和预览，写入仍经唯一协调器并可等待地同步编辑器。不能把「落盘完成后编辑器已更新」改成弃等事件，也不能恢复逐调用方推送网络偏好。需修订 ADR-024／028 与 `AUD-ARCH-017`；先做 Core-only、完整宿主、初始化前后与保存后的接线验证。此前暂缓决定保持有效。
3. **先迁移结果契约，再迁移后台实现。** 把游戏操作结果改为稳定错误 code、路径、计数、残留等结构化数据；本地化、Toast、确认框和窗口动作由 UI 映射。迁移现有 `IGameOperationExecutor` 接缝及必要结果类型，再整体迁移 `DownloadSession`／执行器／差异计算／检查点／启动校验／卸载工作流与清单读取到 Core 的游戏操作模块。UI 的 `GameOperationJourney` 保留用户确认、重试提示与启动后窗口行为。实现保持 internal，按调用需要公开契约；保留 ADR-021 的后端接缝和 ADR-022 的会话封装，不升格私有 Plan／Execute 分段。
4. **用最小第二宿主验证复用。** 仅引用 Core，在隔离数据根、假网络和假进程适配器下完成安装、修复、取消、退出续传与卸载，不登记 Avalonia、UI 本地化或编辑器。以这项试验确认接口足够深、依赖闭包完整，再决定是否值得增加 Application／Infrastructure 工程。每批迁移通过受影响用例与 `verify.ps1`，不同时改写下载算法、持久化格式和 XAML 资源位置。

迁移期间必须保持旧 `settings.json` 读取、官方安装状态、续传文件、数据根、用户停止与进程退出的差异、卸载的整树范围与进程复查，以及 updater 私有协议兼容。程序集／公开面／命名空间守卫需随迁移更新，而非通过扩大豁免绕过。阶段二和三改变既定所有权及分层决策，应先记录 ADR，再同步 `PROJECT_CONVENTIONS.md` 与 `CONTEXT.md`。

这一路线的收益是复用、变更局部性与可验证生命周期；不会自动提高下载速度或降低渲染成本。应用逻辑与表现／基础设施分离的方向参考 [Microsoft 架构原则](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles)；生命周期和单例线程安全约束参考 [Microsoft DI 指南](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines)。这些原则可用于桌面产品，不能直接照搬 Web 的请求作用域模型。

## 验证边界

- **本次完整验证（2026-10-31，Windows x64，实际 SDK 10.0.401）**：`pwsh ./verify.ps1` 退出码 0。本地化契约、Debug／Release 构建通过且零警告、零错误；Unit 2526 项（2512 通过／14 项平台跳过），Headless 337 项全部通过，Release 资源契约 23 项全部通过；手写 C# 行覆盖率 86.82%（19534/22500）、分支覆盖率 92.86%（3501/3770）。一次通过不满足 `AUD-TEST-015` 的完成条件，保留该发现。
- **本次独立检查**：`dotnet build src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj -c Release` 零警告、零错误；`dotnet list Cafe.Launcher.slnx package --vulnerable --include-transitive --no-restore` 查询七个项目，当前配置源未报告已知漏洞；临时探针移出后，`test.ps1 -Suite Unit -FilterClass '*DocumentationLinkContractTests'` 1 项通过。构建与测试引入的锁文件副作用已还原至审计开始时状态。
- **本次定向探针**：完整基线验证后临时增加两项 xUnit 探针，经 `test.ps1 -Suite Unit -FilterMethod '*AuditProbe*'` 运行，两项均在各自目标断言失败，确认新增发现；与「既有完整套件通过」分开记录。探针随后移出测试源码，诊断代码副本与输出只留在本地被忽略的 `artifacts/`。未修改生产实现，故没有修复后通过的结论。
- **本次未覆盖**：未做真实网络／大清单性能基准、ETW／分配分析、长时间压力测试、真实安装升级和多平台桌面运行；未执行迁移原型。审计涉及的安全路径为源码与已有回归抽样，不等同于渗透测试。接受风险中的历史性能数字未重新测量。以下 2026-10-20 等记录仅解释既有证据，不视为本次复验。
- **前次修复回归（2026-10-20）**：设置写入覆盖三种入口交错、等待取消、落盘后取消、持久化失败后重试，以及 UI／后台调用交错下的 UI 线程通知。扫描覆盖 Windows 快照与 Linux proc 根整体失败传播、逐进程失效容忍、经真实扫描适配层的监控保留未知／恢复退出、闸门继续放行及存活宿主优先。新增用例在修复前复现丢 UID、错误退出和扫描异常被吞，修复后单元套件 2435 项通过（2421 通过／14 跳过），Headless 设置线程专项 5 项通过。Core-only 偏好保留原问题，未改动对应组合根。
- **前次修复完整复验（2026-10-20，Windows）**：完整 `pwsh ./verify.ps1` 通过。本地化契约通过；Debug/Release 构建均为 0 警告、0 错误；单元测试 2435 项（2421 通过／14 项平台跳过），Headless 239 项全部通过，Release 资源契约 23 项全部通过。手写代码行覆盖率 86.63%（18766/21663）、分支覆盖率 92.62%（3328/3593），高于基线 0.8560／0.9180。Linux 扫描错误由临时 proc 目录夹具在 Windows 上覆盖，未重跑 Linux 实机流程；其余平台边界如下。
- **Windows 安装器**：真实 ISCC 编译已在装有 Inno Setup 7 的本机通过，产物 `Cafe.Launcher_setup.exe` 生成。安装器的**运行时**升级行为（`[InstallDelete]`、`skipifnotsilent`）仍无干净环境复现，结论来自 Inno 语义与 issue #23 的现场证据。
- **Linux 打包**：`.deb` 实构建并解包校验，Arch 完成 `makepkg` 全量构建与 `namcap` 无 Error；RPM 只做规格静态校验，真实依赖安装依赖发布 CI 的 Fedora 容器步骤。所有 Linux 包未在真实桌面做 GUI 启动与软件中心展示验证，openSUSE 未覆盖。
- **平台路径**：触屏标题栏拖动没有实机验证（Headless 的 `BeginMoveDrag` 是空实现）；日志轮转没有跑满真实 5 MB，结论来自 Serilog 源码语义与构造用例；Linux/Proton 真实运行和 Windows 自更新启动握手没有实机实验，便携换位恢复路径由真实目录集成测试覆盖。

## 维护规则

1. 新发现必须包含稳定 ID、证据、影响、严重度、置信度、处置和可验证的完成条件。
2. 同一根因沿用原 ID；不同根因使用新 ID。
3. 修复或接受后直接更新本文件：已解决项从正文移除，接受项移入「已接受风险」。Git 历史承担历史记录。
4. 只登记已验证且值得行动或明确接受的问题；候选想法、执行流水和纯风格建议不进入本文件。
5. 每次审计同时刷新顶部日期、基线、计数、优先级和验证边界，确保整份文件描述同一个当前状态。
