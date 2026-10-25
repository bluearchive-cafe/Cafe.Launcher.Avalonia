# ADR-042：Avalonia 启动器按 Core 与 UI 程序集分层

- 状态：✅ 已接受（迁移已完成；项目与程序集名随后由 [ADR-043](ADR-043-程序集与命名空间同名.md) 统一，本文中的旧路径只作为当时的记录保留）
- 日期：2026-09-27
- 相关：`src/Cafe.Launcher.Core/`、`src/Cafe.Launcher.UI/`、`src/Cafe.Launcher/App.axaml.cs`、`src/Cafe.Launcher/Composition/ServiceConfiguration.cs`、`src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs`、`tests/Cafe.Launcher.Tests/AssemblySplitContractTests.cs`、`tests/Cafe.Launcher.Tests/LauncherSettingsDefaultsTests.cs`、`tests/Cafe.Launcher.Tests/TestUserDataIsolationTests.cs`、`coverage.ps1`、`scripts/Test-LocalizationContract.ps1`、[ADR-043](ADR-043-程序集与命名空间同名.md)

## 背景

游戏协议、安装状态、下载和更新逻辑会持续演进，但它们不应因 Avalonia 控件、图片解码或本地化资源而被绑定到桌面表现层。StellaSora 与 Blue Archive 官方启动器共享的 `@launcher/utils`、类型及 preload 层也表明：协议/文件处理与表现层之间有稳定接缝。本项目仍是单进程 Avalonia 应用，不以 Electron 的 IPC 模型作为拆分前提。

迁移只能分批落地：一次把视图、资源与后端同时换程序集，会让编译、XAML 资源 URI、卫星资源程序集与全部源码扫描契约同时失效。评审无法分辨「搬迁」与「改写」。

## 决策

程序集依赖固定为：

```text
Cafe.Launcher (WinExe) → Cafe.Launcher.UI → Cafe.Launcher.Core → Cafe.Launcher.Updater.Core
Cafe.Launcher (WinExe) → Cafe.Launcher.Core
Cafe.Launcher (WinExe) → Cafe.Launcher.Updater.Core
Cafe.Launcher.Updater → Cafe.Launcher.Updater.Core
```

- **Core 边界**：`Cafe.Launcher.Core` 不可引用 Avalonia、MarkView、Material Icons 或 UI 资源；它提供 `LauncherBuildIdentity` 和后端服务注册入口 `AddLauncherCore`。Core 不上抛已本地化的字符串：用户可见文案由表现层按稳定 code 映射到 `LocalizationKeys`；结构化结果类型在第一个真实 Core→UI 结果落地时引入（见「被否决的替代方案」）。
- **UI 边界**：`Cafe.Launcher.UI` 承载 Views、Controls、Converters、ViewModel、主题、本地化和运行时资产。宿主只以 `LauncherPresentationSession` 调用生命周期操作（`CreateMainWindow`、`AttachStartupBehavior`、`InitializeAsync`、`ShowWindow`、`LaunchGameAsync`、`PrepareForShutdownAsync`、`HideMainWindow`、`CreateCrashReportWindow`）。该会话自己组装 `MainWindow` 与托盘，协作者全部经构造函数注入、由 UI 的登记入口 `AddLauncherPresentation()` 装配，门面里因此没有 `IServiceProvider`，也没有服务定位（[开发规范 §7](../../../PROJECT_CONVENTIONS.md#7-di-与生命周期)）；协作者里有 internal 类型，所以构造函数是 `internal`，只有同程序集的登记方能拼装它。
- **宿主边界**：宿主只留进程与入口相关——`Program.cs`、`App.axaml(.cs)`、`CrashReportApp.axaml(.cs)`、`Composition/`、`Constants/BuildInfo.cs`、跨进程转发、`ShutdownDeferral`、`CrashReporterLauncher`（实现 UI 声明的 `ICrashReporterLauncher`）、图标流水线输入 `Assets/app-icon-source.png`；应用生命周期、崩溃窗口替换（`CreateCrashReportWindow` + `HideMainWindow` + `desktop.Shutdown(1)`）与关闭延迟也留在宿主。测试扫描域与声明表切到 `TestRepository.PresentationPath`，宿主里再出现 `Views`/`Controls` 目录即失败。
- **命名（已被 [ADR-043](ADR-043-程序集与命名空间同名.md) 取代）**：原宿主工程（当时叫 `Cafe.Launcher.Avalonia`）保留项目路径、程序集名和可执行文件名，只负责进程生命周期、单实例信号和顶层 Avalonia 生命周期。宿主现在是 `Cafe.Launcher`，工程路径、程序集名与命名空间同名。
- **构建身份**：`LauncherBuildIdentity` 必须由 WinExe 的程序集创建，避免类库把自己的版本误报为产品版本。任何「当前运行版本」的判断（例如默认更新渠道）都消费注入的 identity，不得反射 `Assembly.GetEntryAssembly()`——测试宿主、崩溃报告进程与将来的宿主都不是启动器。它是「运行版本/渠道」的唯一来源；宿主构建时间元数据的 MSBuild 转义缺陷（`%%cI`）已修并有测试钉住。
- **数据根**：仍由组合根解析唯一一次（ADR-025）。`AddLauncherCore` 的 `launcherDataRoot` 是必填参数，Core 不提供 `ForCurrentProcess()` 兜底；宿主因此只有一处注册 Core 的调用点。
- **IVT**：生产程序集不得彼此 `InternalsVisibleTo`；仅各测试程序集可作为 friend。
- **设置**：唯一写入路线整体属于 Core——`ISavedSettingsWriter`/`SavedSettingsWriter` 负责落盘与归一化，草稿与快照由表现层持有，两侧只通过窄接缝 `ISettingsDraftOwner`（取草稿、取快照、可等待地收回落盘值）相连；Core 不引用 Avalonia，UI 线程编排留在实现方 `SettingsEditor`，可等待的接缝就是 ADR-024 要的「通知」，广播事件等出现第二个消费者时再加。设置模型去 MVVM：`LauncherSettings`/`GameRuntimeSettings` 继承 Core 本地的 `SettingsModel`（`INotifyPropertyChanged` + `SetProperty`），Core 因此不引用 `CommunityToolkit.Mvvm`。
- **Core 命名空间**：源命名空间收口为 `Cafe.Launcher.Core.*`，每个工程用显式 using 列出自己用到的 Core 命名空间（迁移期临时加过的 33 条项目级 `<Using Include="Cafe.Launcher.Core.*" />` 已全部移除，218 个源文件补上显式 using）。`AssemblySplitContractTests.Projects_ResolveCoreNamespacesWithExplicitUsings` 挡回退，`CoreSources_UseOnlyTheCoreNamespaces` 钉住 Core 侧只使用 `Cafe.Launcher.Core.*`。
- **注册顺序**：宿主先注册 Core 再注册表现层——组合根 `AddLauncherServices` 内部先 `AddLauncherCore`，随后 `AddLauncherPresentationServices` 与生命周期门面 `AddLauncherPresentation`。MS.DI 的反向释放因此先销毁 UI；`AssemblySplitContractTests.CompositionRoot_RegistersCoreFirstAndOnlyOnce` 钉住这一形状、「只有一个 Core 调用点」以及「宿主不得直接登记表现层」。

### 落位边界

- UI 承担 `Views/`、`ViewModels/`、`Features/`、`Controls/`、`Converters/`、`Models/`、`Helpers/`、表现层 `Services/` 与运行期 `Assets/`；搬迁只换程序集，XAML 的 `x:Class`、绑定与 `avares://` 之外的代码无需改写。载体资源随之改指 UI：`Assets/app-icon.ico`（`<ApplicationIcon>`）、`launcher-background.png`、`Views/Styles/*.axaml` 的 `avares://Cafe.Launcher.UI/...`。
- 本地化资源（`Resources/*.resx` 与 `LauncherStrings.Designer.cs`）随 UI 程序集走；`RootNamespace` 由 ADR-043 定为 `Cafe.Launcher.UI`，resx 清单名仍是 `Cafe.Launcher.UI.Resources.LauncherStrings`。`LauncherStrings` 是 `internal`（宿主只用 `Constants/LocalizationKeys` 与 XAML 的 `Shell.I18n[...]`），生成器 `Generate-LauncherStringsDesigner.ps1` 同步生成 `internal`，两者一致由 `LauncherStringsDesigner_DeclaresWhatItsGeneratorEmits` 钉住（否则重跑脚本会静默放宽刚收窄的公开面）；资源目录定位点（`TestRepository.ResourcesPath`、`TestLocalizationHelper.FindProjectRoot`、三个本地化脚本）一并改指 UI。
- 组合根一分为二：宿主 `ServiceConfiguration` 只解析数据根、登记 Core 与进程级服务，再调用 UI 的 `AddLauncherPresentationServices(dataRoot, identity, logger, fatalCrashService, showHiddenSettings)`；`LocalDiagnostics.RegisterSharedLogger` 的唯一登记所有方落在 UI 登记入口。表现层不再读宿主 `BuildInfo`/`Program`：`ShellViewModel`/`DebugViewModel`/`SettingsViewModel` 收注入的 `LauncherBuildIdentity`，`MainWindowViewModel` 收 `PresentationOptions`（`bool` 无法由容器解析），`GameShortcutService` 的 CLI 参数常量移到 Core 的 `LauncherConstants`。

### 公开面

- **UI 工程**：顶层 public 类型由 172 收到 27（internal 184；判据：UI 工程里顶格的类型声明，`LauncherStrings.Designer.cs` 除外），由 `AssemblySplitContractTests.PresentationPublicSurface_IsTheDeclaredSet` 钉成显式声明集。仍 public 的只有三类：宿主真正使用的门面与入口（`LauncherPresentationSession`、两个组合扩展、`ICrashReporterLauncher`、`FatalCrashService`/`IFatalCrashService`、`CrashReport`/`CrashReportStore`/`CrashReportBootstrap`/`CrashReportWindow`、`LocalizationService` 及其签名里出现的 `SystemCultureSnapshot`/`LanguageOption`/`SelectableOption`），测试 Theory 签名里出现的领域枚举（`GameOperationStage`、`DownloadStopReason`、`UninstallScope`、`ModalKind`、`ToastSeverity` 等——public 测试方法不能带 internal 参数类型），以及无 DI 启动失败路径必须直接构造的崩溃报告族。其余视图、ViewModel、服务与辅助类型全部 internal，测试经 UI 程序集的 `InternalsVisibleTo`（只对两个测试程序集）访问。
- **Core 已收窄的部分**：`ICrc64Service`、`IDiskSpaceService`、`ILocalInstallationStateStore`、`ILauncherSettingsService`（刻意只含 `ReadAsync`，让表现层在类型层面无法绕开写入协调器）、`IRemoteHttpUrlValidator` 五个接口就位，对应实现与 `SavedSettingsWriter` 收回 `internal`，接口与实现在 `AddLauncherCore` 里映射到同一单例（这些服务都持有状态，重复注册会让表现层拿到另一个实例）。`LocalDiagnostics` 收回 `internal`：`ILauncherDiagnostics` 扩成完整门面（Verbose/Warning/Fatal、`ErrorAsync(title, exception)`、`LogFilePath`、`MinimumLevel`、`SetMinimumLevel(LogEntrySeverity)`，级别换算留在实现里），另立公开静态入口 `LauncherLog`（含 `CreateDetached()`）供 pre-DI 阶段与静态帮助类使用；`LauncherSelfUpdateService`、`ImageCacheService`、`GameInstallationPath` 同样收窄。HTTP 族（`LauncherApiClient`/`HttpClientFactory`/`RemoteHttpTransport` 及 `AuthorizationHeaderFactory`、`PatchUrlGroupService`、`PrefixMetadataStore`、`ProxySettingsService`、`DefaultProcessLauncher`、`GameRuntime`、`LauncherCoreService` 等内部实现）同样收回 `internal`，登记移入 `AddLauncherCore`，偏好闭包读 `ISettingsDraftOwner.GetSavedSnapshot()`，草稿所有者缺席（Core-only 容器）时退回 `LauncherSettings.CreateDefaults(identity)`。`Models/` 之外的顶层 public class 因此由 57 降到 46，由 `CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 钉成显式声明集（判据以守卫为准；收窄前实测于 `c6455ff3`）。
- **Core 仍 public 的部分**：数据模型与响应体（`LauncherSettings`、`LauncherStatusSnapshot`、`ManifestFile`、`LauncherDataRoot` 等）、进程级日志器 `UnifiedLogger`、一批静态帮助类与工具类型，以及跨程序集暂需公开、留待重新裁定的成员（`UnixProcessRecord`/`UnixProcessRecordParser`/`UnixGameProcessMatcher`(+`OwnershipMarkerKey`)/`LinuxProcessScanner`/`ProtonBuildDiscovery`）；`HttpClientLease` 因是公开接口的返回类型而保留，`LeaseBackedDownloadTransportSource` 因表现层按批次超时构造而保留。继续收窄的前提是消费方只经接口取用，而仍有直接使用具体类型的地方（例如 `Crc64Service`、`LauncherSettingsService`、`DiskSpaceService`）；收尾时筛出的 11 个候选（`GSettingsCli`、`LogRecord`、`PatchUrlGroupDefinition`、`BestHttpCookieLibrary`、`ILauncherUpdateDownloader`、`UnixProcessMatch` 等）都出现在其它 Core public 成员的签名里（`LauncherApiClient.GetInstallationConfigAsync` 返回 `InstallationConfigResponse`、`LogEntryReader.Read` 返回 `IEnumerable<LogRecord>`……），收窄会逐级级联，因此全部回退。这超出本次「按程序集分层」的范围，作为独立重构登记为 `CODEBASE_AUDIT.md` 的 `AUD-ARCH-015`。

### 迁移例外与修复

- 崩溃报告族与导出器（`CrashReport.Build`、`CrashReportStore`、`FatalCrashService`、`LogExportService`、`CrashReportBootstrap.Resolve`）改为接收注入的 `LauncherBuildIdentity`（缺省留空，不猜），因此可以随 UI 程序集落位；唯一仍与宿主耦合的是 `CrashReporterLauncher`（拉起独立崩溃报告进程，依赖 `Program`）。
- `PrefixMetadataStore`、`LauncherUpdateService`、`ImageCacheService` 等迁入 Core 时不再读宿主 `BuildInfo`，当前版本与 User-Agent 都取注入的 `LauncherBuildIdentity`；系统代理读取失败、自更新告警与失败日志改走 `ILauncherDiagnostics`（`MessageAsync`/`LogMessage`/`ErrorAsync`），不再走表现层静态入口。`GameRuntimeRunnerDisplay` 依赖 `LocalizationService`/`LocalizationKeys`，未随 `Services/GameRuntime/*` 迁入 Core；该目录其余部分与 `GameInstallationPath`、`Helpers/DiagnosticLines` 已在 Core。
- 迁移期间一度红掉的三个契约已修复并补上守卫：版本断言取错程序集（`LauncherConstantsTests`）、数据根重复解析与重复注册（`TestUserDataIsolationTests` + `CompositionRoot_RegistersCoreFirstAndOnlyOnce`）、默认更新渠道靠入口程序集反射（`LauncherSettingsDefaultsTests`）。

## 被否决的替代方案

- **按 `Features/*` 逐个建程序集。** Shell 对表现族的下行聚合会立刻变成循环引用，且会迫使出大量只有单一实现者的浅接口。
- **建立独立 `Contracts` 或 `Yostar.Launcher.Protocol` 程序集。** 在确有第二个 .NET 消费方之前，这只是单消费者的假接缝；协议实现先作为 Core 内部深模块保留。
- **引入进程间 IPC / 插件化加载。** 单进程桌面应用没有第二个进程边界要跨；拆程序集是为了依赖方向与可测试性，不是为了分布式部署。
- **现在就引入 `LauncherMessage` 结构化结果类型。** 迁移当下没有任何生产者或消费者，仓库已有的公开类型会成为零消费者接缝；等第一个 Core→UI 结果（下载/卸载/自更新）落地时再引入，同时给出 code→`LocalizationKeys` 映射。
- **用 `Assembly.GetEntryAssembly()` 反射判定预发布构建。** 迁入 Core 的 `LauncherSettings` 曾用这条反射替代宿主 `BuildInfo`：入口程序集不是启动器时（测试宿主、崩溃报告进程）预发布构建会静默退回 Stable，且没有测试能看见。渠道默认值改由注入的 `LauncherBuildIdentity.IsPrerelease` 决定。
- **让 Core 自己解析数据根作为兜底。** `?? LauncherDataRoot.ForCurrentProcess()` 会把 ADR-025 的单一解析点复制到第二个程序集，并使宿主可以「悄悄」注册两次 Core；参数因此改为必填。

## 后果

- 新增后端代码先放 Core，并把用户可见文本表达为稳定 code + 参数，由 UI 映射到 `LocalizationKeys`。
- 程序集拆分让若干「按单一工程根扫描」的守卫失效（源码扫描、裸资源键字面量、XAML 域、覆盖率路径解析）。它们已改为显式 host/Core/UI 多根，并对「扫描域变空」设了反空转基线；`SettingsWriteOwnershipTests`、`GameOperationStopOwnershipTests`、`DiagnosticsStaticSealTests` 的扫描域随之扩到 host + Core，声明表改为仓库相对路径；覆盖率闸口额外要求每个生产程序集都必须真的出现在 Cobertura 报告里。
- 第三方通知按生产工程生成：`New-ThirdPartyNotices.ps1` 取 `src/` 下每个生产工程 `project.assets.json` 的并集（只被 Windows 自更新 helper 引用的包在宿主图里看不到），逐包列出「Required by」与已扫描工程；同一包在两个工程解析出不同版本时直接失败而不是猜。`ThirdPartyNoticesContractTests` 覆盖全部生产工程，并断言「已扫描工程清单 == 磁盘上的生产工程」。
- 迁移收尾实测覆盖率（`coverage.ps1`）：手写代码行 86.41% / 分支 92.83%，基线 0.8560 / 0.9180 全程未下调，闸口通过。
- 剩余工作：S1–S5 列出的迁移项已全部完成；Core 公开面按「消费方改成只经接口取用」的范围收窄，未覆盖的公开实现与缺失的增量守卫（现已由 `CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 补上）登记为 `CODEBASE_AUDIT.md` 的 `AUD-ARCH-015`。
