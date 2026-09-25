# Cafe Launcher 与官方 Blue Archive JP 启动器 v1.7.2 对比

> 静态对比日期：2026-09-25。官方样本为 Windows 32 位 Electron 安装包中的 `app.asar`，版本 `1.7.2`；Cafe 基线为 `2af35fd727106b7a17f419fc739bba99e2877865`。下文的“官方”仅指该历史版本，不代表官方现行行为。

## 样本、范围与读法

用户给出的原路径 `E:\Repos\BlueArchive\_JP\_Launcher\app-32\_v1.7.2\resources\app\_unpacked` 在本机不存在；实际对比使用用户复制到仓库的 `artifacts/original-launcher.asar`（SHA-256：`FD243A514FDCD5EA9CF91C2A4557C560F9023B59D8DD877312410B0C665CA4F7`）。它与本机 `E:\Repos\BlueArchive_JP_Launcher\app-32_v1.7.2\resources\app.asar` 的哈希一致；包内 `package.json` 声明 `version: 1.7.2`。可用 `7z x artifacts/original-launcher.asar 'out\*' 'package.json' 'node_modules\@launcher\utils\dist\index.cjs'` 重现取证。解包所得相对路径和行号是下文的官方侧证据。样本包是本机 `artifacts/` 下的忽略文件，不随本仓库提交；未来复核须另行取得相同哈希的包。

扫描覆盖本仓库应用、updater、测试、脚本、打包配置与现有设计约定；官方侧检查 `out/main`、`out/preload`、`out/renderer` 的第一方构建产物和 `@launcher/utils/dist/index.cjs`。官方 `node_modules` 的其他第三方实现、图片逐像素差异、运行时网络响应及实机行为不在本轮结论内。源码层面的“未见”只表示这份构建产物没有对应路径，不能推断其他官方版本的功能。

两者不是逐文件移植关系：官方是 Electron + Vue/Pinia + worker threads，Cafe 是 .NET 10 + Avalonia，另有 Windows 自更新 helper。对比以用户流程和持久化/协议边界为单位。下面“共同”表示能从两边源码找到对应能力，“不同”不自动表示缺陷。

| 仓库扫描区域 | 官方样本中可对照的区域 | 本文落点 |
| --- | --- | --- |
| `Features/GameOperations`、游戏运行时、文件/清单服务 | `out/main/index.js`、下载 worker、`@launcher/utils` | 安装、修复、启动、卸载与共享协议 |
| `Features/Shell`、设置、向导、资源面板、`Views`、本地化 | Vue/Pinia 界面、Electron 主窗口和 preload IPC | 界面、设置、托盘、Cafe 特有能力 |
| 网络传输、更新、诊断、持久化服务及 `Cafe.Launcher.Updater` | 官方主进程、更新窗口、日志 worker、浏览器存储 | 更新、数据与诊断差异 |
| 两个测试项目、`scripts/`、`installer/`、`.github/` | `app.asar` 不包含官方源码测试、CI 或完整安装器脚本 | 用本仓库测试确认兼容契约；不推断官方测试覆盖率、CI 质量或安装器外部行为 |

## 结论速览

1. **可共用游戏安装目录的关键协议仍对齐。** 游戏 API 路径、`YostarGames/BlueArchive_JP` 目录、`manifest.json` / `game-launcher-config.json`、`vc` 字段计算和 CRC64 文件校验都有对应实现与契约测试。
2. **游戏操作的安全与恢复策略明显分叉。** 官方下载 worker 在安装阶段尝试强杀游戏目录内的 `.exe` 进程；Cafe 在预检及写入边界拒绝覆盖运行中游戏，并提供暂停、限速及持久化恢复。官方的退出/中止可凭 `.tmp` 和浏览器 `download-task` 重新进入任务，但没有 Cafe 的会话级暂停控制。
3. **卸载默认兼容，但 Cafe 增加范围选择。** 官方删除清单文件和两个状态文件；Cafe 默认范围相近，另外删除桌面快捷方式，并可选择删除安装目录与受管兼容前缀。
4. **官方特有的安装包配套流程没有移植。** v1.7.2 包含 `clickCode` 文件转移、与自更新配合的 `YostarGames` 目录暂移/恢复、AIHelp 客服入口和可配置的远程日志上传。Cafe 采用自己的安装、反馈、日志和更新路径；若要直接替换官方安装器，这些边界须单独决策。

## 共享协议与兼容点

| 主题 | 官方 v1.7.2 | Cafe 当前实现 | 判定与证据 |
| --- | --- | --- | --- |
| 游戏与配置 API | 获取 `/api/launcher/game/config`、`/api/launcher/base/config`、`/api/launcher/operations/resource`、`/api/launcher/social/media/resource`；清单 URL 走 `/api/launcher/game/config/json`，CDN 走 `/api/launcher/advanced/game/download/cdn`。 | `LauncherApiClient` 包含上述路径，并增加 `/api/launcher/installation/config` 与可选下载源改写。 | **共同，Cafe 扩展。** 官方 `out/renderer/assets/main-2b0108e0.js:26-40`、`out/main/index.js:928-948`；[LauncherApiClient.cs](../../src/Cafe.Launcher.Avalonia/Services/LauncherApiClient.cs)。 |
| 认证头 | `head={game_tag,time,version}`，`sign=hex(MD5(JSON(head)+data+salt))`。 | `AuthorizationHeaderFactory` 按同一字段顺序生成；有固定向量测试。 | **共同。** 官方 `node_modules/@launcher/utils/dist/index.cjs:2859-2872`；[AuthorizationHeaderFactory.cs](../../src/Cafe.Launcher.Avalonia/Services/Auth/AuthorizationHeaderFactory.cs)、[AuthorizationHeaderFactoryTests.cs](../../tests/Cafe.Launcher.Avalonia.Tests/AuthorizationHeaderFactoryTests.cs)。 |
| 安装目录与状态文件 | 默认 `YostarGames/BlueArchive_JP`；以 `manifest.json` 和 `game-launcher-config.json` 记录安装。 | 相同目录和文件名；状态由 `LocalInstallationStateStore` 协调读写。 | **共同。** 官方 `out/main/index.js:569-571, 19936-19938`、`out/main/constant-a9fc53dd.js`；[GameInstallationPath.cs](../../src/Cafe.Launcher.Avalonia/Services/GameInstallationPath.cs)、[LocalInstallationStateStore.cs](../../src/Cafe.Launcher.Avalonia/Services/LocalInstallationStateStore.cs)。 |
| `vc` 签名与字段顺序 | `Object.values(obj).join(';')` 后 MD5/Base64；清单头、逐文件项与游戏配置分别签名。 | `OfficialHashService` 对相同字段顺序计算并校验，固定官方 v1.7.2 样本值有测试。 | **共同，兼容性关键。** 官方 `node_modules/@launcher/utils/dist/index.cjs:2852-2916`、`out/main/index-f057dd97.js:18902-18917`；[OfficialHashService.cs](../../src/Cafe.Launcher.Avalonia/Services/OfficialHashService.cs)、[OfficialHashServiceTests.cs](../../tests/Cafe.Launcher.Avalonia.Tests/OfficialHashServiceTests.cs)。 |
| 下载文件格式 | 按主/备用 CDN 及清单 `source` 构造 URL，下载到 `*.tmp`，支持 `Range` 续传，按 CRC64 校验，再改名到目标文件。 | 同样使用两组 CDN、`*.tmp`、`Range`、CRC64 和改名；还检查续传响应的 `Content-Range`。 | **共同，Cafe 增加响应约束。** 官方 `out/main/index-f057dd97.js:18670-18778, 18919-18946`；[FileDownloadService.cs](../../src/Cafe.Launcher.Avalonia/Services/FileDownloadService.cs)、[DownloadExecutor.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadExecutor.cs)。 |
| 重试次序与并发 | 最多 10 个并行下载；单文件主/备 CDN 顺序 `[1,1,1,1,0,0,0,1,1,1]`，安装校验失败再重试最多 3 轮。 | 保留 10 并发及相同 CDN 次序；安装验证亦有重试。 | **共同。** 官方 `out/main/index-f057dd97.js:18592-18618, 18949-19080`；[FileDownloadService.cs](../../src/Cafe.Launcher.Avalonia/Services/FileDownloadService.cs)、[DownloadSession.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadSession.cs)。 |

## 行为差异

### 安装、更新、修复与启动

| 对比项 | 官方 v1.7.2 | Cafe 当前实现 | 影响与证据 |
| --- | --- | --- | --- |
| 增量更新 | 先以本地版本对应远端清单和本地文件大小求差，再与目标清单求增删。 | 采用远端清单差异，并由 `GamePathValidator` 限定所有目标路径。 | 主流程相近，Cafe 对远端清单路径设更严格的文件系统边界。官方 `out/main/index.js:19505-19631`；[DownloadSession.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadSession.cs)、[GamePathValidator.cs](../../src/Cafe.Launcher.Avalonia/Helpers/GamePathValidator.cs)。 |
| 本地版本远端清单不可用 | `getCurrentManifestFiles` 失败后返回空列表；更新差异会把目标清单文件全部列为待下载。 | 回退到本地 `manifest.json` 的文件列表，继续做目标版本差异与大小检查。 | 离线/旧版本清单被下架时，Cafe 可能避免整包重下；两边都仍要求目标清单可获取。官方 `out/main/index.js:19596-19631`；[ManifestDiffCalculator.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/ManifestDiffCalculator.cs)。 |
| 修复 | 对目标清单中的文件逐个 CRC64 复核，差异文件进入下载；安装后再验证。 | 同样按 CRC64 修复；计划阶段和下载阶段的已验证哈希可复用，避免不必要的整文件重读。 | 结果目标相同，Cafe 减少重复 I/O。官方 `out/main/index.js:19518-19546, 19633-19651`；[DownloadExecutor.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadExecutor.cs)。 |
| 下载中止/恢复 | UI 将版本、basis、路径写入浏览器 `localStorage` 的 `download-task`，重启时匹配远端版本后自动再进入下载；磁盘 `.tmp` 用于续传。 | 将会话检查点写入 `download_state.json`，支持暂停/继续、限速、退出后恢复，并区分用户停止与进程退出。 | Cafe 的操作控制更细；两个客户端的任务元数据不互通，但均可识别已存在的临时下载文件。官方 `out/renderer/assets/main-2b0108e0.js:65-90, 8093-8114, 8228-8247`；[DownloadCheckpointStore.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadCheckpointStore.cs)、[GameDownloadService.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/GameDownloadService.cs)。 |
| 安装状态提交 | worker 先生成两个 `*.tmp` 状态文件，校验/改名游戏文件后将状态文件依次改名；没有在提交前统一复读两个临时状态文件。 | `LocalInstallationStateStore` 写两个临时状态文件、复读并校验，再串行替换目标文件。 | 文件格式相同，提交前校验和同目录并发控制不同；两边都不是跨两个文件的事务性原子提交。官方 `out/main/index-f057dd97.js:18902-18946, 18950-19024`；[LocalInstallationStateStore.cs](../../src/Cafe.Launcher.Avalonia/Services/LocalInstallationStateStore.cs)。 |
| 游戏运行时更新 | worker 安装阶段遍历游戏目录根部 `.exe`，发现同名进程后调用 `taskkill /F /T`，再继续覆盖文件。 | 安装、更新、修复、卸载先检查游戏进程家族；下载结束后、真正写入前再检查，命中则向用户报告并停止写入。 | **有意的用户行为差异**：Cafe 不代用户结束游戏。官方 `out/main/index-f057dd97.js:18976-19017`、`node_modules/@launcher/utils/dist/index.cjs:3106-3125`；[RunningGameGate.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/RunningGameGate.cs)、[DownloadSession.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/DownloadSession.cs)、[ADR-032](../design/adr/ADR-032-游戏进程按名字家族识别.md)。 |
| 启动前检查 | 下载本地版本对应远端清单，逐文件只核对存在与大小；取清单失败时回落为空列表。 | 默认用本地清单做存在/大小检查；用户可选远端清单或跳过。远端清单失败时有意放行。 | 默认请求量与离线行为不同；Cafe 的远端模式与官方更接近。官方 `out/main/index.js:19596-19619, 19691-19712`；[ManifestValidationService.cs](../../src/Cafe.Launcher.Avalonia/Services/ManifestValidationService.cs)。 |
| 游戏启动与会话 | 有参数时 `execFile`，无参数时 `shell.openPath`；启动后最小化窗口，异步失败只写日志。 | 经 `IGameRuntime` 启动；可选择启动后保留/最小化/退出，跟踪启动、运行、退出和失败状态，支持 Linux Wine/UMU/Proton。macOS 当前不支持启动游戏。 | Cafe 多了会话可见性和兼容层诊断；平台能力不等同。官方 `out/main/index.js:19691-19732`；[GameLaunchService.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/GameLaunchService.cs)、[GameSessionMonitor.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/GameSessionMonitor.cs)、[README.md](../../README.md)。 |
| 卸载范围 | 校验目录名和保护目录，检查配置声明的主 `.exe` 是否运行，删除清单所列文件及两个状态文件；保留目录与清单外残留。 | 默认删除上述游戏状态与清单文件，另处理桌面快捷方式；可选彻底清除安装目录和受管兼容前缀，并报告残留/保留的自定义前缀。进程判据含宿主与参数中的游戏进程。 | 默认范围接近，但快捷方式和进程判据不同；彻底清除是 Cafe 扩展。官方 `out/main/index.js:19978-20024`；[GameUninstallService.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/GameUninstallService.cs)、[UninstallScope.cs](../../src/Cafe.Launcher.Avalonia/Features/GameOperations/UninstallScope.cs)。 |

### 界面、配置、更新与数据

| 对比项 | 官方 v1.7.2 | Cafe 当前实现 | 影响与证据 |
| --- | --- | --- | --- |
| 设置与语言 | 构建产物中的设置主要有安装目录、代理 `direct/system`、关闭时最小化/退出、更新/修复/卸载和关于页；该日服包主进程语言选择固定 `ja`，保留英语回退。 | 提供英语、简/繁中文、日语切换，以及主题、配色、动效、下载限速、HTTP/2、检查模式、启动行为等设置。 | Cafe 的本地设置表面更广；不能把官方包里未启用的通用韩/中文资源视为此日服包已提供的界面语言。官方 `out/renderer/assets/main-2b0108e0.js:8570-8740`、`out/main/index.js:20052-20137`；[LauncherSettings.cs](../../src/Cafe.Launcher.Avalonia/Models/LauncherSettings.cs)、[LauncherEnums.cs](../../src/Cafe.Launcher.Avalonia/Models/LauncherEnums.cs)。 |
| 本地持久化 | 界面设置和 `download-task` 在浏览器 `localStorage`；背景图在 IndexedDB；Electron `userData` 保存日志等。 | `LauncherDataRoot` 下的 `settings.json`、`download_state.json`、日志、图片缓存与公告状态；游戏目录共享官方格式。 | 两者的应用设置与任务记录不能直接互导；共享的是游戏安装状态，不是启动器偏好。官方 `out/renderer/assets/main-2b0108e0.js:64-113, 2472-2493`；[LauncherDataRoot.cs](../../src/Cafe.Launcher.Avalonia/Services/LauncherDataRoot.cs)、[LauncherSettingsService.cs](../../src/Cafe.Launcher.Avalonia/Services/LauncherSettingsService.cs)。 |
| 远端内容 | 拉取背景、公告、横幅、新闻、社媒信息；背景以服务端 CRC64 键缓存。 | 读取相同的基础与运营资源，提供壁纸/横幅/公告展示及自己的缓存、主题处理。 | **共同，呈现不同。** 官方 `out/renderer/assets/main-2b0108e0.js:26-40, 8164-8209, 16331-16376`；[RemoteContentViewModel.cs](../../src/Cafe.Launcher.Avalonia/ViewModels/RemoteContentViewModel.cs)、[ImageCacheService.cs](../../src/Cafe.Launcher.Avalonia/Services/ImageCacheService.cs)。 |
| 启动器自更新 | `electron-updater` 下载并在退出时安装；安装期间可暂移同目录下 `YostarGames`，下次启动恢复。 | 从 Cafe 发行版选择 Windows 安装包或便携 ZIP，使用 `SHA256SUMS` 校验，交给独立 helper 应用；非 Windows 交给浏览器。 | 发布源、包格式和恢复机制不同；两者不互为升级通道。官方 `out/main/index.js:205-334, 19780-19794`；[LauncherSelfUpdateService.cs](../../src/Cafe.Launcher.Avalonia/Services/Update/LauncherSelfUpdateService.cs)、[WindowsLauncherUpdateApplier.cs](../../src/Cafe.Launcher.Avalonia/Services/Update/WindowsLauncherUpdateApplier.cs)。 |
| 系统托盘与单实例 | Electron 单实例锁，托盘含“显示/退出”。 | 跨平台单实例转发；托盘还复用游戏启动、设置等命令并按当前状态启用。 | Cafe 扩展桌面控制。官方 `out/main/index.js:20139-20184, 20359-20364`；[Program.cs](../../src/Cafe.Launcher.Avalonia/Program.cs)、[SystemTrayActions.cs](../../src/Cafe.Launcher.Avalonia/Features/Shell/SystemTrayActions.cs)。 |
| 诊断与日志 | 本地 logger、错误捕获，另有按远端配置启用的阿里云 SLS 日志上报 worker；界面引入 AIHelp Web SDK。 | 本地日志查看/导出、崩溃报告与调试界面；项目规则禁止远程遥测。 | 这是产品与隐私取舍，不是协议兼容缺口。官方 `out/main/index.js:20285-20346`、`out/main/logReport-600fb64e.js`、`out/renderer/assets/main-2b0108e0.js:9492-9514`；[LocalDiagnostics.cs](../../src/Cafe.Launcher.Avalonia/Services/Diagnostics/LocalDiagnostics.cs)、[LogExportService.cs](../../src/Cafe.Launcher.Avalonia/Services/Diagnostics/LogExportService.cs)、[PROJECT_CONVENTIONS.md](../../PROJECT_CONVENTIONS.md)。 |
| Cafe 独有资源面板 | 该官方包未见 UID 配置、Cafe 资源服务或对应 UI。 | 可查看/配置 UID 关联的 Cafe 本地化资源。 | Cafe 自有服务，不属于官方协议兼容要求。[ResourcePanelService.cs](../../src/Cafe.Launcher.Avalonia/Features/ResourcePanel/ResourcePanelService.cs)、[ResourcePanelApiClient.cs](../../src/Cafe.Launcher.Avalonia/Features/ResourcePanel/ResourcePanelApiClient.cs)。 |

## 官方安装包配套行为：替换场景需单独确认

- **`clickCode`**：官方在安装目录、Electron `userData` 与游戏目录之间搬运并记录该文件；Cafe 的应用代码没有对应消费路径。证据：官方 `out/main/index.js:340-410, 19743-19746`；Cafe 的 `src/`、`tests/` 未找到 `clickCode` 引用。现有对比不能证明该文件对游戏运行是否必需；若目标是“原位替换官方安装程序”，应先做真实安装/启动实验，再决定是否接入。
- **官方安装器目录迁移**：官方自更新前可能把启动器同目录的 `YostarGames` 暂移到父级并写临时路径，下次启动恢复；Cafe 不使用该 Electron 安装器更新链。证据：官方 `out/main/index.js:205-264, 19780-19794`；Cafe 更新实现见上表。
- **官方日志上传与客服**：官方按服务端开关上传本地日志并加载 AIHelp；Cafe 的反馈/诊断流程留在本地，由用户主动导出。此项不应因“对齐官方”而自动移植，须遵守仓库的无远程遥测约定。

## 对后续兼容工作的判断

| 优先级 | 建议 | 理由 |
| --- | --- | --- |
| 保持 | 保留 `vc` 字段顺序、认证头、安装目录和清单格式的固定向量测试；升级官方参考版本时重新取样。 | 这些直接决定双启动器能否共用同一游戏目录。 |
| 验证后决定 | 若计划支持“替换官方安装程序且沿用安装器目录”，对 `clickCode` 与 `YostarGames` 暂移/恢复做实机兼容测试。 | 静态包能证明官方存在该流程，不能证明当前游戏或 Cafe 分发方式仍依赖它。 |
| 保持现有取舍 | 运行中游戏的写入拒绝、路径校验、无远程遥测、独立自更新校验。 | 这些是 Cafe 已记录的安全/产品边界，不能因为官方实现不同而回退。 |

## 验证边界

本报告是静态源码/构建产物对比，不等于官方与 Cafe 的端到端互操作测试。官方 JS 包含生成的 CRC64/Emscripten 代码与第三方库；只把入口、调用链和第一方行为列为证据。没有连接官方线上 API、执行官方 EXE、安装或卸载真实游戏、运行 Linux/Proton 或 macOS 游戏。对照版本固定为官方 v1.7.2；服务端配置可使某些入口显示或隐藏。Cafe 侧结论按上述 Git 提交和现有测试代码记录，后续变更需重新核对。

2026-09-25 验证：样本 SHA-256 与本机同版本包一致；文档内 42 个仓库相对链接均可解析；`OfficialHashServiceTests`、`AuthorizationHeaderFactoryTests`、`GameInstallationPathTests`、`ManifestValidationServiceTests`、`FileDownloadServiceTests` 和 `GameUninstallServiceTests` 共 58 个测试通过（0 失败、0 跳过）。未运行全部测试套件，因为本次只修改文档。
