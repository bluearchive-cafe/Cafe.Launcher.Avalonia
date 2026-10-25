# 开发规范

本文件维护实现规则。环境、命令与交付流程见[开发指南](docs/DEVELOPMENT.md)，领域行为见 [CONTEXT.md](CONTEXT.md)，界面译名见 [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md)，代理入口见 [AGENTS.md](AGENTS.md)。

## 1. 基本原则

1. **行为有测试保护** — 新功能和行为变更附聚焦测试；先固定缺陷或风险边界，再改实现。
2. **验证先于完成** — 运行受影响的检查并报告结果，完整交付流程见开发指南。
3. **向后兼容** — `settings.json` 的新旧格式都要能读；公共 API 签名变更同步调用点。
4. **零警告** — `TreatWarningsAsErrors` 与 `EnforceCodeStyleInBuild` 已启用，warning 即 error。
5. **无远程遥测** — 诊断日志只留本地，不向第三方发送日志或遥测。

## 2. 程序集与命名边界

`Cafe.Launcher.Core` 是无表现依赖的后端边界：不得引用 Avalonia、MarkView、Material Icons 或 UI 资源；用户可见文案以稳定 code 或领域结果传给 UI，由 UI 本地化。Core 不解析进程数据根，也不反射入口程序集取版本。数据根由组合根解析一次后经 `AddLauncherCore` 的必填参数注入（[ADR-025](docs/design/adr/ADR-025-数据根由组合根解析并注入.md)）；版本来自注入的 `LauncherBuildIdentity`。

`Cafe.Launcher.UI` 承载全部 Avalonia 控件、ViewModel、主题、资源与运行时资产（`Views/`、`ViewModels/`、`Features/`、`Controls/`、`Converters/`、`Models/`、`Helpers/`、表现层 `Services/`、`Resources/`、`Assets/`）。宿主只保留进程生命周期与入口文件。分层见 [ADR-042](docs/design/adr/ADR-042-Avalonia启动器按Core与UI程序集分层.md)，命名见 [ADR-043](docs/design/adr/ADR-043-程序集与命名空间同名.md)。

| 工程 | 职责 |
| --- | --- |
| `src/Cafe.Launcher` | WinExe 宿主：入口、进程生命周期、组合根、单实例转发、关闭延迟、崩溃报告进程、图标流水线输入 |
| `src/Cafe.Launcher.UI` | Avalonia 表现层 |
| `src/Cafe.Launcher.Core` | 无表现依赖的后端：协议、HTTP、游戏运行时、安装状态、设置、诊断、更新选择 |
| `src/Cafe.Launcher.Updater` | 独立 Windows 自更新 helper 宿主 |
| `src/Cafe.Launcher.Updater.Core` | 更新参数与路径契约、校验和应用实现 |

- **依赖单向**：host → UI → Core → Updater.Core；host 也直接引用 Core 与 Updater.Core。
- **同名契约**：工程目录、`.csproj`、`AssemblyName`、`RootNamespace`、源码命名空间与发行资产前缀使用同一 token（宿主 `Cafe.Launcher`、表现层 `Cafe.Launcher.UI` 等）；测试程序集名以 `Tests` 结尾。GitHub 仓库名 `Cafe.Launcher.Avalonia` 是历史标识，明确保留。
- **命名空间指明属主**：同一命名空间不得出现在两个程序集；`InternalsVisibleTo` 仅向以 `Tests` 结尾的测试程序集开放。
- **显式 using**：每个工程在源文件中显式列出 Core 命名空间，不使用项目级 `<Using>`；Core 源码只使用 `Cafe.Launcher.Core.*`。
- **公开面受控**：Core 与 UI 的顶层 public 类型都有显式声明集测试；新增实现优先收回 `internal`，确实需要公开时在声明表中登记理由。
- **功能垂直组织**：`Features/` 分为 Shell、GameOperations、Settings、SetupWizard、Diagnostics、ResourcePanel；功能 ViewModel 就近放置，根 `ViewModels/` 只放窗口级、跨功能及模态契约。非 Shell 功能通过共享 `Services/` 的窄接口协作，不引用彼此的具体类型；Shell 作为聚合者是既定例外。

## 3. 代码风格

### 3.1 命名

| 实体 | 规范 | 示例 |
|---|---|---|
| 类 / 结构体 / 枚举 | PascalCase | `LauncherApiClient` |
| 异步方法 | PascalCase + `Async` | `LoadAsync()` |
| 私有字段 | camelCase | `private readonly LocalDiagnostics diagnostics;` |
| 公共属性 / 枚举值 | PascalCase | `IsVisible`, `Verbose` |
| 本地常量 | PascalCase | `MaxRetry` |
| 测试方法 | `Method_State_ExpectedResult` | `SeverityFilter_WhenNoMatch_ReturnsEmpty` |

源码/契约类守卫测试可用两段式 `Subject_Expectation`。

### 3.2 文件组织

- 默认一个文件一个主要类型；紧密协作的支持类型可同文件，但保持同一职责。
- `Services/` 下每个 Service 一个文件，文件名与类名一致。
- `Models/` 按领域分组，每个文件一个 DTO 或一组强相关 DTO。
- `Constants/` 每个文件一个常量类别；测试文件名为 `{TargetClassName}Tests.cs`。

### 3.3 注释

公共类型和跨功能边界的 public/internal 成员应有 `/// <summary>`，说明稳定合约。线程安全、disposal order、线序等不变量必须写明。注释面向下一个维护者，解释 **why**，不只解释 **what**；分析器抑制项用单行注释说明原因。

## 4. XAML 与视觉规范

- 所有可视值使用 `StaticResource`/`DynamicResource` 设计 token（`App.axaml` 的 `Launcher.*` 家族）。禁止裸色号、`Transparent`、裸图标尺寸、裸 `4`/`6`/`8` 圆角半径；`Spacing`/`ColumnSpacing`/`RowSpacing` 也引用 `Launcher.Spacing.*`（含 `Spacing.None`）。
- 透明度只允许结构性 `0`/`1`（可见性、动画关键帧）或 `Launcher.StateLayer.*`；分数透明度以 `Launcher.StateLayer.*` 五档为唯一来源。
- 主题无关的渐变和阴影只允许定义在 `App.axaml` 或 `MainWindow.Styles.axaml`。
- 每个交互控件都必须有绑定到本地化字符串的 `AutomationProperties.Name`；契约测试全量扫描 `Views/` 与 `Controls/` 下的 XAML（`IsTabStop="False"` 的设计画廊展示件除外）。
- 控件使用语义化 `Classes`，不写内联 Style；大块样式或覆盖层提取到独立 `.axaml`，`MainWindow.axaml` 只留窗口外壳与内容网格。
- Avalonia 使用编译绑定与显式绑定，没有反射式 view locator。
- 崩溃窗口独立使用 `Crash.*` 令牌，就近定义在自身资源中；隔离报告进程没有 `App.axaml`，见 [ADR-020](docs/design/adr/ADR-020-崩溃窗口独立Fluent设计系统.md)。
- 模态栈交互权及对话框例外见 [CONTEXT.md「模态交互权」](CONTEXT.md#设计系统)。叠层次序为内容 → 设置（100）→ 对话框（200）→ 向导（500）→ 向导退出确认（501）→ Toast（1000）。`ZIndex` 只排序兄弟节点，必须声明在覆盖层元素的**使用点**（`MainWindow` / `MainWindowDialogsOverlay` 的直接子项）上；写进样式里的覆盖层内部根 Grid 是无效的。常量取 `LauncherConstants`（`ZIndexSettings` / `ZIndexDialog` / `ZIndexSetupWizard` / `ZIndexSetupWizardConfirmation` / `ZIndexToast`）。
- `BackgroundViewModel` 在构造器中同步解码内置壁纸，以保证首帧；改为异步前先明确首帧壁纸来源。

组件、动态色及动效规格见[设计规范](docs/design/design-system-spec.md)与[动效交接](docs/design/fluent-motion-prototype-handoff.md)。

## 5. 日志

### 5.1 级别

| 场景 | 级别 |
|---|---|
| 启动/退出/会话边界、关键流程成功 | Info |
| 关键流程失败（需人工关注）、未处理异常 | Error |
| 可恢复的非严重问题 | Warn |
| 关键流程中间状态、耗时、diff 结果 | Debug |
| 极细粒度诊断（默认关闭） | Verbose |
| 致命错误（进程即将退出） | Fatal |

### 5.2 写法

- `LocalDiagnostics` 是唯一入口；只有 `Program.cs` 和 `UnifiedLogger` 自身可直接调 `UnifiedLogger.LogAsync`。
- 通过构造函数注入 `LocalDiagnostics`，存为 `private readonly` 字段。
- `title` 是日志行的 `[LogTitle]` 标签，用简短 PascalCase 模块名（如 `GameDownload`、`ApiClient`）；受 `DiagnosticsLogTitleContractTests` 守护。
- `message` 放上下文：路径、耗时、计数、状态码。不记密钥、盐、Authorization 头或 cookie。
- 非热路径可用 `LocalDiagnostics.LogSync`。UI 点击路径（如 `GameDownloadService.Stop()`、`DownloadSession.Pause()/Resume()`）不得 sync-over-async 阻塞 UI 线程，改用显式弃等的 `_ = diagnostics.DebugAsync(...)` 并注释意图。
- 异步上下文用 `await diagnostics.DebugAsync(..., CancellationToken.None)`；日志不传播调用方的取消令牌。

按事件记录，避免逐块下载进度、纯静态工具日志及同一事件的重复日志。

## 6. 本地化

1. 在四个资源文件（`src/Cafe.Launcher.UI/Resources/LauncherStrings{,.zh-Hans,.zh-Hant,.ja}.resx`）中按字母序添加 key-value。
2. XAML 绑定 `{Binding Shell.I18n[newKey]}`；键存在性由 `ResxResourceContractTests.XamlResourceBindings_UseOnlyKeysThatExistInNeutralResources` 守护，`XamlKeyScan_StillCoversTheKeyBearingFiles` 防止扫描域退化。
3. C# 中引用 `Constants/LocalizationKeys` 的编译时常量，禁止向 `T()`/`F()`/`I18n[...]` 传裸 key 字面量。
4. 四种语言都必须有文本（专有名词可回退英文，但不得留空）；译名以 [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) 为准。C# 用 `LocalizationService.T()`/`F()` 获取文案。
5. 新增或重命名 key 后运行 `scripts/Generate-LauncherStringsDesigner.ps1`、`scripts/Generate-LocalizationKeys.ps1`，再运行 `scripts/Test-LocalizationContract.ps1`。
6. 测试中通过 `TestLocalizationHelper.Initialize()` 或 `LocalizationService.InitializeForTesting(...)` 提供资源；不要写死预期中文。

## 7. DI 与生命周期

- 所有 DI 管理的 Service 与 ViewModel 都是 `AddSingleton`；没有 scoped 边界。
- `UnifiedLogger` 在 `Program.cs` 预创建，经 `existingLogger:` 注入并复用同一实例。
- Microsoft DI 按**解析**的逆序释放服务，不是按登记逆序。新增 `IDisposable` 时检查解析顺序；`Program.RunSession` 在会话结束日志之后显式释放共享的预 DI 日志器。
- 组合根解析 `LauncherDataRoot` 与 `BuildInfo.Identity` 一次，调用 `AddLauncherCore` 后登记宿主，再交给 `AddLauncherPresentationServices` 与 `AddLauncherPresentation()`。先解析 `LauncherPresentationSession`，由它拉入 Core 依赖；提前解析 Core 服务可能颠倒释放顺序，仅保持登记顺序不足以保证安全。
- 组合完成后，宿主仅通过 `LauncherPresentationSession` 调用 UI 生命周期。pre-DI 崩溃路径为既定例外，可直接构造 crash-store、fatal-crash、bootstrap 和 crash-window，以便容器不可用时仍能报告启动失败。
- 依赖一律构造函数注入，不用属性注入或 Service Locator。测试用构造函数标记 `internal`。
- 单实例所有权在 Windows 使用命名 mutex，Unix 使用数据根中的锁套接字；.NET 的 `Local\` 命名空间在 Unix 仅覆盖同一 POSIX 会话。套接字保留在数据根而非 `XDG_RUNTIME_DIR` 是已接受的取舍，见 [ADR-034](docs/design/adr/ADR-034-Unix单实例所有权用数据根内锁套接字判定.md) 与[审计台账](CODEBASE_AUDIT.md)。

## 8. 测试

- 单元测试：`tests/Cafe.Launcher.Tests/`（xUnit v3 + Microsoft.Testing.Platform 2 + Microsoft Code Coverage）。
- Headless UI 测试：`tests/Cafe.Launcher.HeadlessTests/`（xUnit v3 + Avalonia.Headless.XUnit，含黄金截图基线）。

规则：

- 一个测试类对应一个被测试类，文件名为 `{Target}Tests.cs`；使用 `Fact`/`Theory`。
- 每个新功能必须有测试；修改日志、本地化、DI 等基础设施时先跑现有全套，再补新增行为。
- 不引入 Mocking 框架；用手写 stub/fake/`HttpMessageHandler` 子类，共享替身放 `tests/TestDoubles/` 并 Compile-Link 到两个工程。
- 运行入口、UI 验证和黄金截图更新见[开发指南「命令与验证」](docs/DEVELOPMENT.md#命令与验证)。
- 平台门控测试用 `Assert.SkipUnless`/`Assert.SkipWhen` 显式跳过，禁止早期 `return`。
- 等待异步状态用 `TestWait.UntilAsync`（Headless 用 `HeadlessTestHost.WaitUntilAsync`，额外推进 UI 调度）；能直接 `await` 就直接等待。有界轮询只走这些设施，测试运行使用仓库入口的挂起保护，不另加临时超时。负向断言确需固定延时时，从 internal 生产常量推导。
- 新服务覆盖成功路径、典型失败路径和关键边界（null、空集合等）。
- 临时目录用 `TestDirectory`（拥有创建与清理，派生 `LauncherDataRoot`）；释放顺序是先容器/服务、后目录。资源定位用 `TestRepository`，它每次调用都重装资源快照。
- 主窗口 ViewModel 用 `MainWindowTestContext` 装配；它只释放自己创建的对象，调用方传入的替身与工厂归调用方，日志器必须活到用例结束。
- Headless 每个用例使用独立数据根（`HeadlessTestHost.CreateServiceProvider(dataRoot:)`），避免同程序集上下文互相看见设置、检查点与崩溃快照。
- 共享设施 `tests/Support/` 的门槛是「两套件都需要、且不含工程专属知识」；场景专属故障模拟留在使用它的测试旁。

## 9. Settings 与持久化兼容性

- 持久化落点一律经注入的 `LauncherDataRoot`；进程根只由组合根与 ADR-019 保护的 pre-DI 路径解析，`TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites` 守护边界。新增落点放进 `LauncherDataRoot.Root` 下的知名路径；文件名声明在 `Constants/LauncherPaths.cs`。随游戏或产品变化的值归 `Models/YostarGameProfile`、`Models/LauncherProductProfile`，唯一出处是 `Constants/LauncherProfiles.cs`（[ADR-025](docs/design/adr/ADR-025-数据根由组合根解析并注入.md)、[ADR-049](docs/design/adr/ADR-049-Linux保留单一数据根并统一小写目录名.md)）。
- `settings.json` 字段名向后兼容：新增字段在 `LauncherSettings` 提供默认值；重命名/删除前明确旧 JSON 的读取策略；`LauncherSettingsService.NormalizeSettings()` 把未知或非法值兜底为有效默认值。
- `LauncherSettings` 新增字段必须同步 `DeepClone()` 与 `ComparedProperties`。漏前者会浅拷贝，漏后者会让保存按钮不再跟踪该字段；`LauncherSettingsTests` 用反射守护两张清单。
- 写入所有权与草稿同步语义见 [CONTEXT.md「已保存设置」](CONTEXT.md#设置与操作)。读取经 `LauncherSettingsService`，写入方由 `AddLauncherCore` 登记；调用方使用 `SaveDraftAsync`/`UpdateAsync`/`ReplaceAsync` 描述变更。`SettingsWriteOwnershipTests` 守护调用方唯一性与持有者声明表。

## 10. 网络与异步

- 出站 HTTP 一律通过 `RemoteHttpTransport` 与 `HttpClientFactory`：共享代理感知租约、SSRF 校验、手动重定向、停滞预算和重试；不另建 `HttpClient` 或长寿命 handler。
- 本地 DNS 预校验的 `SocketException` 在 URL 校验边界转换为 `HttpRequestException`（`NameResolutionError`），保留原始异常且不缓存失败。调用方复用既有重试、备用端点与失败提示；取消仍原样传播。
- 客户端偏好在租约取得时读取已保存设置，语义见 [CONTEXT.md「客户端偏好」](CONTEXT.md#设置与操作)。直连容忍 Clash/mihomo 的 Fake-IP 地址段，代理出口不容忍；直连请求失败时保留针对 Fake-IP 的诊断指引。
- 异步调用直接 `await`，避免 `.Result`、`.Wait()` 或 `Thread.Sleep` 阻塞。取消令牌显式传播；明确不传播时使用 `CancellationToken.None`。

## 11. 文件与文档

- C# 使用 UTF-8/CRLF；`.editorconfig` 声明的 XAML、工程/props/targets、YAML、JSON、Markdown、PowerShell 使用 UTF-8/LF，`coverage.ps1` 保留其 UTF-8 BOM 例外。`.editorconfig` 与 `.gitattributes` 必须一致。
- 文档分工、维护与链接检查见[开发指南「文档职责」](docs/DEVELOPMENT.md#文档职责)。审计发现只维护在 [CODEBASE_AUDIT.md](CODEBASE_AUDIT.md)。

## 12. 工具链与依赖

| 工具/库 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 10.0.302 | Runtime / SDK（`global.json` 钉住，`latestFeature` 滚动） |
| Avalonia / Avalonia.Desktop | 12.1.4 | UI Framework |
| Avalonia.Controls.ColorPicker | 12.1.4 | 自定义主题色取色器 |
| MarkView.Avalonia | 12.3.0 | 基于 Markdig 的更新说明 Markdown 预览 |
| Avalonia.Themes.Fluent | 12.1.4 | Fluent 主题 |
| Avalonia.Headless.XUnit | 12.1.4 | Headless UI testing |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 调试期 UI 诊断（Debug 专用，Release 不分发） |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators |
| Material.Icons.Avalonia | 3.0.2 | Icon library |
| Microsoft.Extensions.DependencyInjection | 10.0.13 | DI 容器 |
| Shirasagi0012.MaterialColorUtilities | 0.2.0 | Material 色彩工具 |
| Serilog | 4.4.0 | Logging pipeline |
| Serilog.Sinks.Async | 2.1.0 | 异步日志 sink |
| Serilog.Sinks.File | 7.0.0 | 文件日志 sink |
| xunit.v3.mtp-v2 | 4.0.2 | Test framework / MTP 2 runner（Headless 覆盖为 3.2.2） |
| Microsoft.Testing.Extensions.TrxReport | 2.5.1 | TRX 测试报告 |
| Microsoft.Testing.Extensions.HangDump | 2.5.1 | 无活动超时保护（不生成 dump） |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | 运行时覆盖率（Cobertura） |
| Inno Setup | 7.0+ | Windows installer（脚本强制最低 7.0，CI 安装 7.1.0） |

版本以 `Directory.Packages.props` 为准；升级时同步本表（受 `InstallerContractTests` 守护），流程见[开发指南「依赖与 CI」](docs/DEVELOPMENT.md#依赖与-ci)。

测试基于 `global.json` 选择的原生 Microsoft.Testing.Platform 2。两个测试工程使用 `xunit.v3.mtp-v2`、`OutputType=Exe` 和 `UseMicrosoftTestingPlatformRunner=true`，不依赖 VSTest 适配器。Unit 使用中央 xUnit 版本，Headless 通过 `VersionOverride` 保留 3.2.2 扩展 API；升级前验证 Avalonia 扩展兼容性。并行化声明分别为 `[assembly: Parallelization(Mode = ParallelMode.None)]` 与 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`。

覆盖率使用 Microsoft Code Coverage 的运行时分析器与 Cobertura 输出，保留生产程序集范围、设计器排除、四个程序集实际计数及基线检查。选择该收集器是因为 coverlet.MTP 静态插桩会跳过已被控制器加载的 UI 程序集。CLI 与迁移依据见 [MTP 迁移核对](docs/research/mtp-migration.md)，Headless 兼容性风险见[审计台账](CODEBASE_AUDIT.md)。
