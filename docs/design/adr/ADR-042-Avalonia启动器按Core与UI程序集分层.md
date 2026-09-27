# ADR-042：Avalonia 启动器按 Core 与 UI 程序集分层

- 状态：✅ 已接受（分批迁移进行中，剩余清单见文末）
- 日期：2026-09-27
- 相关：`src/Cafe.Launcher.Core/`、`src/Cafe.Launcher.Avalonia.UI/`、`src/Cafe.Launcher.Avalonia/App.axaml.cs`、`src/Cafe.Launcher.Avalonia/Composition/ServiceConfiguration.cs`、`src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs`、`tests/Cafe.Launcher.Avalonia.Tests/AssemblySplitContractTests.cs`、`tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsDefaultsTests.cs`、`tests/Cafe.Launcher.Avalonia.Tests/TestUserDataIsolationTests.cs`、`coverage.ps1`、`scripts/Test-LocalizationContract.ps1`

## 背景

游戏协议、安装状态、下载和更新逻辑会持续演进，但它们不应因 Avalonia 控件、图片解码或
本地化资源而被绑定到桌面表现层。StellaSora 与 Blue Archive 官方启动器共享的
`@launcher/utils`、类型及 preload 层也表明：协议/文件处理与表现层之间有稳定接缝。

本项目仍是单进程 Avalonia 应用；不以 Electron 的 IPC 模型作为拆分前提。

迁移只能分批落地：一次把视图、资源与后端同时换程序集，会让编译、XAML 资源 URI、
卫星资源程序集与全部源码扫描契约同时失效，评审无法分辨「搬迁」与「改写」。

## 决策

程序集依赖固定为：

```text
Cafe.Launcher.Avalonia (WinExe) → Cafe.Launcher.Avalonia.UI → Cafe.Launcher.Core → Cafe.Launcher.Updater.Core
Cafe.Launcher.Avalonia (WinExe) → Cafe.Launcher.Core
Cafe.Launcher.Avalonia (WinExe) → Cafe.Launcher.Updater.Core
Cafe.Launcher.Updater → Cafe.Launcher.Updater.Core
```

- `Cafe.Launcher.Core` 不可引用 Avalonia、MarkView、Material Icons 或 UI 资源；它提供
  `LauncherBuildIdentity` 和后端服务注册入口 `AddLauncherCore`。Core 不上抛已本地化的
  字符串：用户可见文案由表现层按稳定 code 映射到 `LocalizationKeys`；该结构化结果类型
  在第一个真实 Core→UI 结果落地时引入（见「被否决的替代方案」）。
- `Cafe.Launcher.Avalonia.UI` 承载 Views、Controls、Converters、ViewModel、主题、本地化和
  运行时资产。迁移完成后宿主只以 `LauncherPresentationSession` 调用生命周期操作。
- 原 `Cafe.Launcher.Avalonia` 保留项目路径、程序集名和可执行文件名，只负责进程生命周期、
  单实例信号和顶层 Avalonia 生命周期。
- `LauncherBuildIdentity` 必须由 WinExe 的程序集创建，避免类库把自己的版本误报为产品版本。
  任何"当前运行版本"的判断（例如默认更新渠道）都消费注入的 identity，不得反射
  `Assembly.GetEntryAssembly()`——测试宿主、崩溃报告进程与将来的宿主都不是启动器。
- 数据根仍由组合根解析唯一一次（ADR-025）：`AddLauncherCore` 的 `launcherDataRoot` 是必填
  参数，Core 不提供 `ForCurrentProcess()` 兜底；宿主因此只有一处注册 Core 的调用点。
- 生产程序集不得彼此 `InternalsVisibleTo`；仅各测试程序集可作为 friend。
- 设置的唯一写入路线整体属于 Core：`ISavedSettingsWriter` / `SavedSettingsWriter` 负责落盘与
  归一化，草稿与快照由表现层持有，两侧只通过窄接缝 `ISettingsDraftOwner`（取草稿、取快照、
  可等待地收回落盘值）相连。Core 不引用 Avalonia：UI 线程编排留在实现方（`SettingsEditor`）。
  可等待的接缝就是 ADR-024 要的"通知"；广播事件等出现第二个消费者时再加。

Core 的源命名空间已收口为 `Cafe.Launcher.Core.*`。为避免 234 个调用点做一次无意义的引用改写，
宿主与两个测试工程通过各自 csproj 里的项目级 `<Using>` 解析这些命名空间；调用方迁移到窄
Core API 后应改回显式 using。`AssemblySplitContractTests.CoreSources_UseOnlyTheCoreNamespaces`
钉住 Core 侧不再回退到 `Cafe.Launcher.Avalonia.*`。

## 被否决的替代方案

- **按 `Features/*` 逐个建程序集。** Shell 对表现族的下行聚合会立刻变成循环引用，且会迫使
  出大量只有单一实现者的浅接口。
- **建立独立 `Contracts` 或 `Yostar.Launcher.Protocol` 程序集。** 在确有第二个 .NET 消费方
  之前，这只是单消费者的假接缝；协议实现先作为 Core 内部深模块保留。
- **引入进程间 IPC / 插件化加载。** 单进程桌面应用没有第二个进程边界要跨，拆程序集是为了
  依赖方向与可测试性，不是为了分布式部署。
- **现在就引入 `LauncherMessage` 结构化结果类型。** 迁移当下没有任何生产者或消费者，
  仓库已有的公开类型会成为零消费者接缝（与上一条同一原则）；等第一个 Core→UI 结果
  （下载/卸载/自更新）落地时再引入，同时给出 code→`LocalizationKeys` 映射。
- **用 `Assembly.GetEntryAssembly()` 反射判定预发布构建。** 迁入 Core 的 `LauncherSettings`
  曾用这条反射替代宿主 `BuildInfo`：入口程序集不是启动器时（测试宿主、崩溃报告进程）
  预发布构建会静默退回 Stable，且没有测试能看见。渠道默认值改由注入的
  `LauncherBuildIdentity.IsPrerelease` 决定。
- **让 Core 自己解析数据根作为兜底。** `?? LauncherDataRoot.ForCurrentProcess()` 会把 ADR-025
  的单一解析点复制到第二个程序集，并使宿主可以"悄悄"注册两次 Core；参数改为必填。

## 后果

- 宿主必须先注册 Core，再注册表现层；MS.DI 的反向释放因此先销毁 UI。当前唯一调用链是
  宿主调用组合根 `AddLauncherServices`（内部先 `AddLauncherCore`），随后
  `AddLauncherPresentation`；`AssemblySplitContractTests` 钉住这一形状与"只有一个调用点"。
- 新增后端代码先放 Core，并把用户可见文本表达为稳定 code + 参数；UI 映射到
  `LocalizationKeys`。
- 程序集拆分让若干"按单一工程根扫描"的守卫失效（源码扫描、裸资源键字面量、XAML 域、
  覆盖率路径解析）。它们已改为显式 host/Core/UI 多根，并对"扫描域变空"设了反空转基线；
  覆盖率闸口额外要求每个生产程序集都必须真的出现在 Cobertura 报告里。
- 文档中的 UI 归属描述是**目标态**：在阶段 4 完成前，Views/ViewModels/Features/Resources
  仍在宿主工程内。

## 迁移状态与剩余清单

已完成（本批提交）：

- `.slnx`、五个工程的单向依赖图、三份 lock 文件、发布/安装器链路（无文件白名单，新增
  DLL 自动随包）。
- 契约测试：依赖图、Core 禁 Avalonia/MarkView/Material Icons、Core 注册顺序与单点注册、
  生产 IVT 禁令、`LauncherBuildIdentity` 取自宿主程序集、默认更新渠道归属。
- `TestRepository` 的 Host/Core/Presentation 三根，以及数据根扫描、裸资源键扫描、XAML 键
  扫描的多根化（含反空转基线）。
- Core 已迁入：官方协议模型与 API 客户端、远程地址校验、传输安全原语、请求发送器、
  远程传输、补丁源规则、安装状态存储与哈希、CRC64、磁盘空间、设置持久化与存储、
  文件系统助手、游戏进程名字族。
- `LauncherBuildIdentity` 成为"运行版本/渠道"的唯一来源；宿主构建时间元数据的
  MSBuild 转义缺陷已修（`%%cI`），并有测试钉住。
- 迁移期间红掉的三个契约已修复并补上守卫：版本断言取错程序集（`LauncherConstantsTests`）、
  数据根重复解析与重复注册（`TestUserDataIsolationTests` +
  `AssemblySplitContractTests.CompositionRoot_RegistersCoreFirstAndOnlyOnce`）、默认更新渠道
  靠入口程序集反射（`LauncherSettingsDefaultsTests`）。Unit、Headless 与覆盖率闸口恢复全绿。
- Core 源命名空间收口为 `Cafe.Launcher.Core.*`（46 个文件、10 处 XAML `xmlns` 随之调整），
  宿主与测试工程用项目级 `<Using>` 过渡解析，`CoreSources_UseOnlyTheCoreNamespaces` 守住回退。
- 设置模型去 MVVM：新增 Core 本地的 `SettingsModel`（`INotifyPropertyChanged` + `SetProperty`），
  `LauncherSettings` / `GameRuntimeSettings` 改继承它，Core 不再引用 `CommunityToolkit.Mvvm`。
- 设置写入协调器落到 Core：`SavedSettingsWriter` 与 `ISavedSettingsWriter` 迁入
  `Cafe.Launcher.Core.Services`，Avalonia 的 UI 线程编排移进 `SettingsEditor.ApplyPersistedAsync`；
  `ISettingsDraftOwner` 是 Core 到表现层的唯一反向接缝（窄且可等待）。
  `SettingsWriteOwnershipTests` 的扫描域随之扩到 host + Core，路径改为仓库相对。
- 网络与代理半批：`HttpClientFactory`、`ProxySettingsService`、`SystemProxySettingsProvider`、
  `GSettingsCli`、`LogEntry`（含 `LogEntrySeverity`）迁入 `Cafe.Launcher.Core.Services`；
  `ILauncherDiagnostics` 扩出 `MessageAsync`/`LogMessage`（后者是同步入口，因为实现类已有同签名的
  静态 `LogSync`，接口成员不能同名），系统代理读取失败的告警由此走 Core 接缝而不是表现层静态入口。
  `ProxySettingsService` 的注册随实现移到 `AddLauncherCore`；`HttpClientFactory` 的注册留在组合根
  （它的偏好闭包读表现层设置快照，ADR-028 的按使用时机拉取）。
- 游戏运行时批次：`Services/GameRuntime/*` 除 `GameRuntimeRunnerDisplay.cs`（它是依赖
  `LocalizationService`/`LocalizationKeys` 的显示映射，留在宿主）全部迁入 Core，
  `GameInstallationPath` 与 `Helpers/DiagnosticLines` 随迁；运行时的 DI 登记
  （`IProcessLauncher`、`RunnerOutputCapture`、`CompatibilityEnvironmentPrecheck`、
  `PrefixMetadataStore`、`IGameProcessTracker`、`IGameRuntime`）从宿主移到 `AddLauncherCore`。
  `PrefixMetadataStore` 不再读宿主 `BuildInfo`，改用注入的 `LauncherBuildIdentity`。
  跨程序集暂需公开的成员（`UnixProcessRecord`/`UnixProcessRecordParser`/
  `UnixGameProcessMatcher`(+`OwnershipMarkerKey`)/`LinuxProcessScanner`/`ProtonBuildDiscovery`）
  记在公开面收窄那一批重新裁定。`GameOperationStopOwnershipTests` 的扫描域随之扩到 host + Core。
- 混合模型文件拆分：`ManifestValidationResult`、`GameLaunchResult`、`LauncherRemoteState`、
  `LauncherRuntimeState`、`LauncherStatusSnapshot` 迁到 `Cafe.Launcher.Core.Models`
  （`Models/LauncherStatusModels.cs`）；宿主的 `LauncherRuntimeModels.cs` 只剩表现类型
  （下拉选项族、操作进度、带 Avalonia `Bitmap` 的远程内容卡）。

未完成：

1. Core 公开面收窄：96 个顶层类型中 95 个是 `public`；"实现默认 internal、只公开窄接口"
   未做，也无守卫。
2. 后端模块继续迁入 Core：`Services/Update/*`（自更新）、`FileDownloadService` /
   `DownloadTransport`、`Services/GameRuntime/*`、`Services/Diagnostics/*`、
   `HttpClientFactory`、`GameShortcutService`、`ManifestValidationService`、
   `RemoteManifestService`、`ProxySettingsService` 等，以及全部 `Features/*`；
   随之把 `GameOperationExecutor` 等实现内部化，并把它们的注册从宿主组合根搬进 Core。
3. UI 程序集落位：Views/Controls/Converters/ViewModels/Features/Resources/Assets 搬迁；
   9 处 `avares://Cafe.Launcher.Avalonia/...` 改指 UI；`AvaloniaResource` 与
   `EmbeddedResource` 从宿主 csproj 迁走；卫星资源程序集断言改指 UI。
4. 表现层接缝反向：当前 `LauncherPresentationSession.CreateMainWindow` 无人调用，宿主仍
   `new MainWindow(...)` 并直接解析 `MainWindowViewModel`；阶段 4 要由 UI 自己构造窗口与
   ViewModel，宿主只保留生命周期调用。
5. 扫描契约随文件搬迁：`UiStyleContractTests` 的扫描域与声明表切到 `UI`（`FindXamlFiles`
   已加 fail-loud 守卫，搬迁时会红而不是静默缩小），约 230 处
   `TestRepository.FromApplicationRoot("Views/…")` 与 `scripts/` 内宿主路径同步迁移。
   生命周期门面目前由 Headless 套件的 `LauncherPresentationSessionTests` 覆盖（UI 程序集
   在该跑批里 100% 行覆盖），但视图形状本身还没有 UI 测试。
6. 第三方通知生成按生产工程逐个进行：`New-ThirdPartyNotices.ps1` 目前只读宿主 csproj，
   `ThirdPartyNoticesContractTests` 也只检查宿主的 `PackageReference`。
7. 覆盖率基线重锚：闸口已加"每个生产程序集必须出现在报告里"的断言，拆分后实测
    （2026-09-27 全量 verify）为行 85.63% / 分支 91.85%，基线 0.8560 / 0.9180 未下调，
    余量 +0.03pp / +0.05pp。脚本自己的约定是"基线 = 实测值再留 0.1–0.25pp 余量"，
    是否按该约定重锚（会下调数值）留待下次全量 verify 决定。

### 下一批搬迁的依赖结论（2026-09-27 盘点）

按「宿主类型依赖」逐一盘点剩余后端模块后，批次顺序与已知阻碍如下：

1. **网络与下载**：代理与连接池一半已完成（见上）。剩下的下载一半
   （`DownloadTransport`、`FileDownloadService`/`IFileDownloadService`/`FileDownloadRequest`/
   `FileDownloadOperationControl`）不引用 Avalonia，只需把 `LocalDiagnostics` 参数换成
   `ILauncherDiagnostics`（`MessageAsync` 已在接缝里）。**下一批做这里。**
2. **游戏运行时**（`Services/GameRuntime/*`）依赖几乎都在自身目录内，但
   `GameRuntimeRunnerDisplay` 依赖 `LocalizationService`/`LocalizationKeys`（表现层）：搬迁时
   要么把该显示映射留在宿主，要么先引入窄的本地化接缝。
3. **诊断**：`UnifiedLogger`/`LocalDiagnostics`/`LogEntry`/`LogEntryReader` 可先走；
   `LogExportService` 还依赖 `GraphicsInfoProbe`、`LinuxProcessScanner`、`CrashReportStore`，
   因此诊断必须排在运行时之后，或与该批一起搬。
4. **自更新**（`Services/Update/*`）当前读宿主 `Constants/BuildInfo`；搬迁前必须改用注入的
   `LauncherBuildIdentity`（`LauncherUpdateHostInfo`/`LauncherUpdateSelection`/`LauncherUpdateTarget`
   同理），否则 Core 会被迫引用宿主类型。`WindowsLauncherUpdateApplier` 依赖进程启动与
   `LocalDiagnostics`，与第 1 批同法处理。
5. **`GameShortcutService`、`ManifestValidationService`、`RemoteManifestService`、
   `LauncherCoreService`、`NoticeStateService`** 零星依赖表现层（`LocalizationService`、
   `GameDownloadService`），随各自的上游批次一起搬，或先用窄接缝替换依赖。
6. **公开面收窄**（把实现改 `internal`）必须在上述批次完成后做：目前宿主组合根直接构造
   `RemoteHttpTransport`、`LauncherApiClient`、`LauncherSettingsService` 等实现，只有当它们的
   注册也搬进 Core、宿主只经接口消费时，实现才可能 internal。项目级 `<Using>` 也应在这一批
   随调用点收敛为显式 using。