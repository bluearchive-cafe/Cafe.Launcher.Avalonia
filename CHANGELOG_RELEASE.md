## v1.0.0-beta.7

本版本重点增强了诊断能力、网络弹性和代理支持。修复了 manifest 抓取密集重试轰击和下载状态持久化残留导致的无限循环问题，统一了远端 JSON 反序列化的错误上下文，新增代理"自动检测"模式作为默认值，并完善了汉化管理（资源面板）的 UID 来源管理与格式校验。

本次更新新增了 4 项特性，优化了 3 项，修复了 9 个问题。

### 新增

- **代理模式新增"自动检测"选项**：新增 `ProxyModes.Auto` 常量，作为代理设置的默认值（替代原先的 `Direct`）。Auto 模式通过 `WebRequest.GetSystemWebProxy()` 自动检测系统代理配置——Windows 上读取 IE/Edge 代理设置，macOS 上读取系统代理，Linux 上读取环境变量。原有的 `Direct`（直连）和 `System`（手动配置系统代理）模式保持不变，三种模式按 Auto → Direct → System 顺序排列。对应新增 `proxyAuto` 本地化键（en: "Auto-detect"、zh-Hans: "跟随系统"、zh-Hant: "跟隨系統"、ja: "自動検出"）。参见 [dotnet HttpClient proxy 文档](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient#configure-an-http-proxy) 了解 .NET 代理配置机制。

- **汉化管理（资源面板）UID 来源管理**：新增 `ResourcePanelUidSources` 常量和 `resourcePanelUidSource` 设置字段，支持在"自动检测"（从 cookie 或设置文件读取）和"自定义"（手动输入）之间切换 UID 来源。UI 新增 UID 展示/编辑卡片和来源 ComboBox（ToggleSwitch → CheckBox），支持编辑流程（BeginEdit / CancelEdit），自定义输入时先校验格式。

- **汉化管理 UID 格式校验**：新增 `ResourcePanelUidService.IsValidUid` 静态方法，校验 UID 格式为 `/^[A-Z]{8}$/`（8 位大写字母）。`ResolveUidAsync` 对 cookie 和 settings 读取的 UID 做格式校验，无效则视为空；`SaveManualUidAsync` 拒绝无效格式并抛出 `ArgumentException`。UI 层先校验再保存，失败时显示本地化错误提示（`resourcePanelUidInvalidFormat` 键）。

- **汉化管理状态图标刷新**：新增 `ResourcePanelStatusToBrushConverter`，根据加载 / 就绪 / 等待 / 失败状态动态映射图标颜色，替代原有的静态图标显示。

### 优化

- **远端 JSON 反序列化统一化**：将 `DeserializeJsonAsync` sniff helper 从 `LauncherApiClient` 提取到 `RemoteHttpRequestService`，`LauncherApiClient`、`LauncherUpdateService`、`ResourcePanelApiClient` 统一接入。所有远端 JSON 反序列化点现在都产出带 URL、HTTP 状态码、Content-Type 和字节预览的富错误信息，大幅提升网络故障的诊断效率。

- **HTTP 响应自动解压**：`HttpClientFactory` 和 `ProxySettingsService` 创建的 `SocketsHttpHandler` 统一启用 `AutomaticDecompression = DecompressionMethods.All`，透明解压 CDN 或代理返回的 gzip/deflate/Brotli 压缩响应。此前压缩响应无法被反序列化，产生不可诊断的 `ExpectedStartOfValueNotFound` 错误（如日志中出现的 `0x8B` gzip 魔数）。

- **汉化管理网络层重试退避**：`ResourcePanelApiClient` 新增 `SendWithRetryAsync` 方法，实现 2 次重试 + 800ms × attempt 线性退避。仅在网络异常（`HttpRequestException` / `TaskCanceledException`）时重试，HTTP 非 2xx 不重试。三个端点（`/status/list`、`/config/get`、`/config/set`）全部接入，行为与 dashboard 项目 `fetchWithRetry` 保持一致。

### 修复

- **修复 manifest 抓取密集重试轰击**：日志中 manifest 抓取出现 528 次连续失败，根因是 refresh → resume → fail 无限循环——`RunAsync` 在抓取 manifest 前保存 `DownloadTaskState`，网络失败后未清除持久化状态，导致每次 `RefreshAsync` 都重新触发 `ResumePersistedDownloadAsync`。修复方案：(1) `GetRemoteManifestAsync` 改为 3 次有限重试 + 指数退避（500ms / 1000ms），对齐 `FileDownloadService` 的有限重试模式；(2) `RunAsync` 网络失败时调用 `ClearDownloadState()` 打破循环。

- **修复 RunAsync 全部失败路径的持久化下载状态残留**：`RunAsync` 在 manifest 抓取前调用 `SaveDownloadStateAsync`，但原先仅网络失败路径清除了持久化状态。其余 7 条失败路径（游戏运行中、CDN 配置不完整、磁盘空间预检查失败、验证 3 次重试后仍失败、磁盘满 IOException、IO/权限异常、catch-all 意外异常）均未清除，均可能触发 refresh → resume → fail 循环。现已补全所有路径的 `ClearDownloadState()` 调用。`DownloadTaskState` 仅存储元数据（版本/basis/路径/isRepair/patchUrlGroup），不存储下载进度，因此清除状态不丢失已下载文件。

- **修复远端 manifest 非 JSON 响应导致的不可诊断失败**：此前 `GetRemoteManifestAsync` 对非 JSON 响应仅抛出 `ExpectedStartOfValueNotFound` 异常，无 URL、HTTP 状态码、Content-Type 上下文。现在 `DeserializeRemoteJsonAsync` 缓冲响应体后反序列化，失败时抛出带完整上下文的 `JsonException`（URL、状态码、Content-Type、前 16 字节十六进制/ASCII 预览、gzip/zlib/zstd/bzip2 魔数识别），配合 `AutomaticDecompression=All` 自动解压，彻底消除了此类不可诊断失败。

- **修复 ToastSeverityToBrushConverter 主题变体处理**：改为使用 `ActualThemeVariant` 替代 `ThemeVariant.Default`，确保 Toast 图标颜色正确跟随当前应用主题（浅色/深色）。

- **修复 OnSelectedResourcePanelUidSourceChanged 无限递归**：新增 `isSettingUidSource` 守卫标志防止属性变更回调中的递归触发。

- **修复 SaveManualResourcePanelUid 并发加载**：新增属性赋值守卫防止并发调用导致的竞态条件。

- **修复 ResourcePanelUidService 正则表达式性能**：迁移至 `[GeneratedRegex]` 源生成器，消除运行时正则编译开销。

- **修复设置归一化中代理默认值**：`LauncherSettingsService` 和 `SettingsEditor` 的归一化逻辑将无效代理值回退为 `ProxyModes.Auto`（此前回退为 `Direct`），与 `LauncherSettings` 声明默认值保持一致。

- **修复 `HttpClientFactory` 代理判断逻辑**：`CreateLeaseAsync` 中代理模式判断从 `!= System` 改为 `== Direct`，使 Auto 模式正确走代理感知路径（通过 `ProxySettingsService.CreateHttpHandlerAsync` 创建带系统代理检测的 handler）。
