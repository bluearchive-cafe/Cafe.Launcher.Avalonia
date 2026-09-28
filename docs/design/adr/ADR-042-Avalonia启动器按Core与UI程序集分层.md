# ADR-042：Avalonia 启动器按 Core 与 UI 程序集分层

- 状态：✅ 已接受（迁移已完成；项目与程序集名随后由 [ADR-043](ADR-043-程序集与命名空间同名.md) 统一，本文中的旧路径只作为当时的记录保留）
- 日期：2026-09-27
- 相关：`src/Cafe.Launcher.Core/`、`src/Cafe.Launcher.UI/`、`src/Cafe.Launcher/App.axaml.cs`、`src/Cafe.Launcher/Composition/ServiceConfiguration.cs`、`src/Cafe.Launcher.Core/Composition/LauncherCoreServiceCollectionExtensions.cs`、`tests/Cafe.Launcher.Tests/AssemblySplitContractTests.cs`、`tests/Cafe.Launcher.Tests/LauncherSettingsDefaultsTests.cs`、`tests/Cafe.Launcher.Tests/TestUserDataIsolationTests.cs`、`coverage.ps1`、`scripts/Test-LocalizationContract.ps1`、[ADR-043](ADR-043-程序集与命名空间同名.md)

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
Cafe.Launcher (WinExe) → Cafe.Launcher.UI → Cafe.Launcher.Core → Cafe.Launcher.Updater.Core
Cafe.Launcher (WinExe) → Cafe.Launcher.Core
Cafe.Launcher (WinExe) → Cafe.Launcher.Updater.Core
Cafe.Launcher.Updater → Cafe.Launcher.Updater.Core
```

- `Cafe.Launcher.Core` 不可引用 Avalonia、MarkView、Material Icons 或 UI 资源；它提供
  `LauncherBuildIdentity` 和后端服务注册入口 `AddLauncherCore`。Core 不上抛已本地化的
  字符串：用户可见文案由表现层按稳定 code 映射到 `LocalizationKeys`；该结构化结果类型
  在第一个真实 Core→UI 结果落地时引入（见「被否决的替代方案」）。
- `Cafe.Launcher.UI` 承载 Views、Controls、Converters、ViewModel、主题、本地化和
  运行时资产。迁移完成后宿主只以 `LauncherPresentationSession` 调用生命周期操作。
- 原宿主工程（当时叫 `Cafe.Launcher.Avalonia`）保留项目路径、程序集名和可执行文件名，只负责
  进程生命周期、单实例信号和顶层 Avalonia 生命周期。**这一条已被 [ADR-043](ADR-043-程序集与命名空间同名.md)
  取代**：宿主现在是 `Cafe.Launcher`，工程路径、程序集名与命名空间同名。
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
钉住 Core 侧只使用 `Cafe.Launcher.Core.*`（ADR-043 起宿主的根命名空间就是 `Cafe.Launcher`）。

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
  宿主与测试工程当时用项目级 `<Using>` 过渡解析（迁移收尾时已全部改为显式 using，见「剩余」第 2 项），`CoreSources_UseOnlyTheCoreNamespaces` 守住回退。
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
- 自更新批次：`Services/Update/*` 与 `LauncherUpdateService` 迁入 Core；它的注册
  （检查服务、宿主信息、下载器、应用器、自更新服务）随实现移到 `AddLauncherCore`；
  `LauncherUpdateService` 不再读宿主 `BuildInfo`，当前版本与 User-Agent 都取注入的
  `LauncherBuildIdentity`（测试按同一方式传入宿主标识）；自更新的告警改走
  `ILauncherDiagnostics.LogMessage`。
- 零散后端批次：`NoticeStateService`、`LauncherCoreService`、`ImageCacheService`、`ShellFolderOpener`、
  `SystemAnimationSettingsProvider`、`GameOperationStopIntent` 迁入 Core；注册随之移到
  `AddLauncherCore`。`ImageCacheService` 的 User-Agent 与 `LauncherCoreService` 的失败日志分别改走
  注入的 `LauncherBuildIdentity` 与 `ILauncherDiagnostics.ErrorAsync(title, message, exception)`。
  设置写入方的持有者声明表同步改指 Core 路径。
- 诊断批次（部分）：`UnifiedLogger`、`LocalDiagnostics`、`LogEntryReader`、`LogExportOptions`、
  `ExportWindow`、`CrashOrigin`、`GraphicsInfoProbe`、`LinuxProcessSnapshot` 迁入 Core
  （Core 因此新增 Serilog 三个包，lock 同步重生成）。`UnifiedLogger` 不再读宿主 `BuildInfo`：
  版本/提交/构建配置由构造参数注入，`Program.cs` 与组合根传宿主身份，测试与辅助宿主留空。
  仍留宿主的诊断文件是崩溃报告族与导出器（`CrashReport`/`CrashReportStore`/`FatalCrashService`/
  `LogExportService`/`CrashReportBootstrap`/`CrashReporterLauncher`/`ICrashReportLocator`/
  `DispatcherExceptionPolicy`）——它们还依赖宿主 `BuildInfo` 与 `Program`，等下一步换成注入身份后再搬。
  `DiagnosticsStaticSealTests` 的扫描域随之扩到 host + Core，声明表改仓库相对路径。
- 诊断家族与宿主解耦：`CrashReport.Build`、`CrashReportStore`、`FatalCrashService`、`LogExportService`
  与 `CrashReportBootstrap.Resolve` 都改为接收注入的 `LauncherBuildIdentity`（缺省留空，不猜），
  崩溃报告与导出内容里的版本/提交/构建配置不再来自宿主 `BuildInfo`；`CrashReportStore` 的注册
  由组合根显式传身份。它们因此可以在 UI 程序集落位时整体搬走——唯一仍与宿主耦合的是
  `CrashReporterLauncher`（拉起独立崩溃报告进程，依赖 `Program`）。
- **第三方通知按生产工程生成**：`New-ThirdPartyNotices.ps1` 不再只读宿主工程，而是把 `src/` 下每个
  生产工程的 `project.assets.json` 取并集（宿主图里看不到只被 Windows 自更新 helper 引用的包），
  逐包列出「Required by」并在文件头列出已扫描工程；同一包在两个工程解析出不同版本时直接失败而不是
  猜一个。`ThirdPartyNoticesContractTests` 从「只比对宿主工程」扩到全部生产工程，并新增
  「已扫描工程清单 == 磁盘上的生产工程」这条断言。
- **UI 公开面收窄**：表现层 172 个顶层 public 类型收到 27 个（internal 184 个，判据：UI 工程里
  顶格的类型声明，`LauncherStrings.Designer.cs` 除外）。仍然 public 的
  只有三类：宿主真正使用的门面与入口（`LauncherPresentationSession`、两个组合扩展、
  `ICrashReporterLauncher`、`FatalCrashService`/`IFatalCrashService`、`CrashReport`/`CrashReportStore`/
  `CrashReportBootstrap`/`CrashReportWindow`、`LocalizationService` 及其签名里出现的
  `SystemCultureSnapshot`/`LanguageOption`/`SelectableOption`），以及测试用 Theory 签名里出现的
  领域枚举（`GameOperationStage`、`DownloadStopReason`、`UninstallScope`、`ModalKind`、`ToastSeverity`
  等——public 测试方法不能带 internal 参数类型）。其余视图、ViewModel、服务与辅助类型全部 internal，
  测试经 UI 程序集的 `InternalsVisibleTo`（只对两个测试程序集）访问。
- **接缝反转完成**：`LauncherPresentationSession` 不再转发宿主回调，而是自己组装
  `MainWindow` 与托盘（`CreateMainWindow`）并接管原来宿主里的
  `InitializeViewModelAsync`/`CompleteShutdownAsync` 两个方法与启动行为挂载
  （`AttachStartupBehavior(firstLaunch, launchGameRequested, shutdownToken)`：首启走向导、否则初始化后
  按 `--launch-game` 自动启动）。它的协作者（`MainWindowViewModel`、取文件/窗口尺寸服务、诊断、设置读写、
  本地化、错误处理、托盘动作与数据根）全部经构造函数注入，由 UI 的登记入口
  `AddLauncherPresentation()` 装配——门面里因此没有 `IServiceProvider`，也没有服务定位
  （`PROJECT_CONVENTIONS.md` §5.3）；协作者里有 internal 类型，所以构造函数是 `internal`，
  只有同程序集的登记方能拼装它。`LauncherPresentationCallbacks` 记录随之删除，
  宿主 `App.axaml.cs` 只剩 277 行，且不再解析任何表现层类型
  （窗口、VM、托盘、文件选择器、窗口尺寸服务都不再出现在宿主里）。宿主保留：应用生命周期、
  跨进程转发信号、崩溃窗口替换（`CreateCrashReportWindow` + `HideMainWindow` + `desktop.Shutdown(1)`）
  与关闭延迟。会话登记并入宿主 `AddLauncherServices`，因此任何从组合根构建的容器
  （含无头测试宿主）都拿到同一条接缝；`AssemblySplitContractTests` 的顺序契约改为
  「宿主不得直接登记表现层」「组合根里 Core 调用点先于表现层调用点」。
- **表现层整体落位 UI 程序集**：`Views/`、`ViewModels/`、`Features/`、`Controls/`、`Converters/`、
  `Models/`、`Helpers/`、表现层 `Services/`（本地化、主题、托盘、取文件/窗口尺寸、设置编辑器、
  下载与启动工作流、崩溃报告族）、运行期 `Assets/`（`app-icon.ico`、`launcher-background.png`）
  全部迁入 UI 工程；**当时命名空间保持不变**（搬迁只换程序集），因此 XAML 的
  `x:Class`、绑定、`avares://` 之外的代码无需改写。宿主只留进程与入口相关：`Program.cs`、
  `App.axaml(.cs)`、`CrashReportApp.axaml(.cs)`、`Composition/`、`Constants/BuildInfo.cs`、
  跨进程转发、`ShutdownDeferral`、`CrashReporterLauncher`（实现 UI 声明的 `ICrashReporterLauncher`）
  以及图标流水线输入 `Assets/app-icon-source.png`。
- **组合根一分为二**：宿主 `ServiceConfiguration` 只解析数据根、登记 Core（`AddLauncherCore`）、
  登记进程级服务，然后调用 UI 的 `AddLauncherPresentationServices(dataRoot, identity, logger,
  fatalCrashService, showHiddenSettings)`——表现层登记不再从宿主逐条写，宿主因此不再引用 UI 内部类型
  （`IGameOperationExecutor` 等随之收回 `internal`）。`LocalDiagnostics.RegisterSharedLogger` 这一
  静态缝的「唯一登记所有方」也随之落在 UI 的登记入口，`DiagnosticsStaticSealTests` 的扫描域与声明表
  已同步。
- 表现层此前直接读宿主的 `BuildInfo`/`Program` 的三处已改为注入：`ShellViewModel`/`DebugViewModel`/
  `SettingsViewModel` 收 `LauncherBuildIdentity`（容器已由 `AddLauncherCore` 登记），
  `MainWindowViewModel` 收 `PresentationOptions`（`bool` 无法由容器解析，故包一层记录类型），
  `GameShortcutService` 用的 CLI 参数常量移到 Core 的 `LauncherConstants`。
- 载体资源随之改指 UI：`Assets/app-icon.ico`（`<ApplicationIcon>` 指 UI 工程路径）、
  `launcher-background.png`、`Views/Styles/*.axaml` 的 `avares://Cafe.Launcher.UI/...`；
  测试的扫描域与声明表切到 `TestRepository.PresentationPath`（含反向失效保护：宿主里再出现
  `Views`/`Controls` 目录即失败）。
- UI 程序集开始承载内容（第一步：本地化资源）：`Resources/*.resx` 与生成的 `LauncherStrings.Designer.cs`
  迁入 `src/Cafe.Launcher.UI/`；UI 的 `RootNamespace` 随后由 ADR-043 定为 `Cafe.Launcher.UI`（表现层
  代码本就沿用该命名空间，搬迁只换程序集），因此 resx 的清单名仍是
  `Cafe.Launcher.UI.Resources.LauncherStrings`，与 Designer 对齐。`LauncherStrings` 在搬迁时
  一度改为 `public`（当时的假设是宿主也要用）；随后的「UI 公开面收窄」批次确认宿主并不消费它
  （宿主只用 `Constants/LocalizationKeys` 与 XAML 的 `Shell.I18n[...]`），于是又改回 `internal`，
  测试经 UI 程序集的 `InternalsVisibleTo` 访问。生成器 `Generate-LauncherStringsDesigner.ps1`
  同步生成 `internal`：产物与脚本一致由 `LauncherStringsDesigner_DeclaresWhatItsGeneratorEmits`
  钉住——否则下次「加了键就重跑脚本」会静默放宽刚收窄的公开面。资源目录的定位点
  （`TestRepository.ResourcesPath`、`TestLocalizationHelper.FindProjectRoot`、三个本地化脚本）一并
  改指 UI 工程；四个语言文件的卫星程序集已验证按文化解析正确。
- 混合模型文件拆分：`ManifestValidationResult`、`GameLaunchResult`、`LauncherRemoteState`、
  `LauncherRuntimeState`、`LauncherStatusSnapshot` 迁到 `Cafe.Launcher.Core.Models`
  （`Models/LauncherStatusModels.cs`）；宿主的 `LauncherRuntimeModels.cs` 只剩表现类型
  （下拉选项族、操作进度、带 Avalonia `Bitmap` 的远程内容卡）。

剩余（2026-09-27 迁移收尾盘点）：S1–S5 列出的迁移项已全部完成。第 1 项（Core 实现收窄）按
「消费方改成只经接口取用」的范围完成，未覆盖的公开实现与缺失的增量守卫已登记为
`CODEBASE_AUDIT.md` 的 `AUD-ARCH-015`；第 2 项（项目级 `<Using>` 收敛）已在本批完成。

1. **Core 公开面收窄（按接口化范围完成）**：已完成两批——`ICrc64Service`、`IDiskSpaceService`、
   `ILocalInstallationStateStore`、`ILauncherSettingsService`（刻意只含 `ReadAsync`，让表现层在
   类型层面无法绕开写入协调器）、`IRemoteHttpUrlValidator` 五个接口就位，对应实现与
   `SavedSettingsWriter` 均收回 `internal`，接口与实现在 `AddLauncherCore` 里映射到同一单例
   （这些服务都持有状态，重复注册会让表现层拿到另一个实例）。`LocalDiagnostics` 也已收回
   `internal`：`ILauncherDiagnostics` 扩成完整门面（Verbose/Warning/Fatal、ErrorAsync(title, exception)、
   `LogFilePath`、`MinimumLevel`、`SetMinimumLevel(LogEntrySeverity)`，级别换算留在实现里），
   另立公开静态入口 `LauncherLog` 供 pre-DI 阶段与静态帮助类使用（含 `CreateDetached()`，替代测试里
   直接 new 实现类的写法），登记移入 `AddLauncherCore`。`LauncherSelfUpdateService` /
   `ImageCacheService` / `GameInstallationPath` 也已收窄（三个窄接口 + 实现 internal）。
   HTTP 族也已完成（登记移入
   `AddLauncherCore`，偏好闭包读 `ISettingsDraftOwner.GetSavedSnapshot()`，缺草稿所有者时退回默认设置），
   `LauncherApiClient`/`HttpClientFactory`/`RemoteHttpTransport` 及一批内部实现（`AuthorizationHeaderFactory`、
   `PatchUrlGroupService`、`PrefixMetadataStore`、`ProxySettingsService`、`DefaultProcessLauncher`、
   `GameRuntime`、`LauncherCoreService` 等）均收回 `internal`：`Models/` 之外的顶层 public class
   由 57 降到 46（判据见守卫 `CorePublicImplementationsOutsideModels_AreTheDeclaredSet`；收窄前
   实测于 `c6455ff3`）。收窄批次当时的记账写作 56 → 45，那条判据略窄，本 ADR 以守卫的判据为准。
   剩下的 public 类除了数据模型/响应体、进程日志器 `UnifiedLogger` 与表现层按批次超时构造的
   `LeaseBackedDownloadTransportSource` 之外，还有一批静态帮助类与工具类型；
   `HttpClientLease` 因是公开接口的返回类型而保留。
   至此 S3 的收窄项完成；项目级 `<Using>` 收敛见下文「剩余」第 2 项（已完成）。原计划里描述的做法（保留作记录）：（`HttpClientFactory` / `RemoteHttpTransport` / `LauncherApiClient` /
   `AuthorizationHeaderFactory` / `PatchUrlGroupService`）。收尾盘点已确定它的做法，不需要新接缝：
   Core 的 `ISettingsDraftOwner.GetSavedSnapshot()` 正是那两个偏好闭包需要的快照（ADR-028 的
   「按使用时机拉取」），因此 `AddLauncherCore` 可以直接登记 `HttpClientFactory`（HTTP/2 偏好）、
   `IRemoteHttpClientLeaseSource`、`IRemoteHttpTransport`（代理模式）与 `LauncherApiClient`，
   草稿所有者缺席（Core-only 容器）时退回 `LauncherSettings.CreateDefaults(identity)`；
   配套改动是 `LeaseBackedDownloadTransportSource` 与 `GameDownloadService` 的构造参数由
   `HttpClientFactory` 换成已有的 `IRemoteHttpClientLeaseSource`（它们只调 `CreateLeaseAsync`），
   并为 `LauncherApiClient` 补一个覆盖下载族与 `LauncherCoreService` 所用成员的 `ILauncherApiClient`。
   其余仍 public 的
   Core 类型是**数据模型**（`LauncherSettings`、`LauncherStatusSnapshot`、`ManifestFile`、
   `LauncherDataRoot` 等）与进程级日志器 `UnifiedLogger`，它们本来就该公开。
   UI 程序集的公开面已收窄（172 → 27 个顶层 public 类型，
   见上），Core 仍是「实现默认 public」。原因是 UI 现在是独立程序集，要跨边界消费 Core 的服务与
   模型；把实现改 `internal` 的前提是消费方只经接口取用，而仍有直接使用具体类型的地方
   （例如 `Crc64Service`、`LauncherSettingsService`、`DiskSpaceService`）。收尾时试过按「Core 之外
   从未出现该类型名」筛出 11 个候选（`GSettingsCli`、`LogRecord`、`PatchUrlGroupDefinition`、
   `BestHttpCookieLibrary`、`ILauncherUpdateDownloader`、`UnixProcessMatch` 等），但它们都出现在
   其它 Core public 成员的签名里（`LauncherApiClient.GetInstallationConfigAsync` 返回
   `InstallationConfigResponse`、`LogEntryReader.Read` 返回 `IEnumerable<LogRecord>`……），
   收窄会逐级级联，因此全部回退——**唯一可行的路径是先把消费方改成只经接口取用**。
   这已超出本次「按程序集分层」的范围，作为独立重构登记为 `CODEBASE_AUDIT.md` 的
   `AUD-ARCH-015`；「没有守卫防止新增 public 实现」这一缺口已补上：
   `CorePublicImplementationsOutsideModels_AreTheDeclaredSet` 把 `Models/` 之外的顶层
   public class 钉成显式声明集，新增或移除都必须同时改那张表。
2. **项目级 `<Using>` 收敛（已完成）**：迁移期四个工程 csproj 里一度加过 33 条
   `<Using Include="Cafe.Launcher.Core.*" />`（相对 `origin/main` 是净新增后净删除，因此没有
   可对比的旧态），现已全部移除，218 个源文件补上显式 using（用
   「类型/方法 → Core 命名空间」映射驱动编译器错误迭代完成）。新增守卫
   `AssemblySplitContractTests.Projects_ResolveCoreNamespacesWithExplicitUsings` 挡住回退。
   AGENTS.md 里「过渡解析」的说明同步改写为「每个工程显式列出自己用到的 Core 命名空间」。

覆盖率（2026-09-27 迁移收尾后全量实测，`coverage.ps1`）：手写代码行 **86.41%** / 分支 **92.83%**，
基线 0.8560 / 0.9180 **未下调**，余量 +0.81pp / +1.03pp，闸口通过。
（迁移中途曾出现行 85.55–85.57% 低于基线 0.03–0.05pp 的状态，随程序集归属变化与收窄后的
接口化自然消失；基线全程未动。）脚本约定「基线 = 实测值再留
0.1–0.25pp 余量」，因此可以把基线**上调**收紧余量；上调是安全方向（不会放过回退），
是否调整留待下次全量 verify 决定。迁移期间行指标曾长期比基线低 0.03–0.05pp，那一状态已随
程序集归属变化自然消失，基线全程保持在 0.8560 / 0.9180。

### 当时的批次结论（已执行，留作记录）

按「宿主类型依赖」逐一盘点剩余后端模块后，批次顺序与已知阻碍如下。六个批次（网络与下载、游戏运行时、诊断、自更新、零散后端、UI 程序集落位）均已执行完毕，本节只记录当时的判断依据：

1. **网络与下载**：代理与连接池一半已完成（见上）。剩下的下载一半
   （`DownloadTransport`、`FileDownloadService`/`IFileDownloadService`/`FileDownloadRequest`/
   `FileDownloadOperationControl`）不引用 Avalonia，只需把 `LocalDiagnostics` 参数换成
   `ILauncherDiagnostics`（`MessageAsync` 已在接缝里）。**下一批做这里。**
2. **游戏运行时**（`Services/GameRuntime/*`）依赖几乎都在自身目录内，但
   `GameRuntimeRunnerDisplay` 依赖 `LocalizationService`/`LocalizationKeys`（表现层）：搬迁时
   要么把该显示映射留在宿主，要么先引入窄的本地化接缝。
3. **诊断收尾已完成到可搬迁状态**：崩溃报告族与导出器已改成消费注入的 `LauncherBuildIdentity`，
   随 UI 程序集落位一起搬（它们被表现层的调试/导出面板直接使用）。唯一例外是
   `CrashReporterLauncher`（拉起独立崩溃报告进程、依赖宿主 `Program`），随落位批次再决定是留宿主
   还是经窄接缝调用。
4. **自更新已完成**（见上）：`LauncherUpdateService` 与 `Services/Update/*` 均在 Core，
   当前版本/User-Agent 取注入的 `LauncherBuildIdentity`，告警走 `ILauncherDiagnostics`。
5. **`GameShortcutService`、`ManifestValidationService`、`RemoteManifestService`、
   `LauncherCoreService`、`NoticeStateService`** 零星依赖表现层（`LocalizationService`、
   `GameDownloadService`），随各自的上游批次一起搬，或先用窄接缝替换依赖。
6. **公开面收窄**（把实现改 `internal`）必须在上述批次完成后做：目前宿主组合根直接构造
   `RemoteHttpTransport`、`LauncherApiClient`、`LauncherSettingsService` 等实现，只有当它们的
   注册也搬进 Core、宿主只经接口消费时，实现才可能 internal。项目级 `<Using>` 也应在这一批
   随调用点收敛为显式 using。