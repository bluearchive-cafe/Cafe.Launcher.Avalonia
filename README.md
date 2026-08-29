# Cafe Launcher

Blue Archive 日服桌面启动器，基于 .NET 10 与 Avalonia 12 重写，替代原 Yostar Electron 启动器。项目在保留官方启动器安装、更新与启动流程的同时，补齐了下载可靠性、CDN 切换、启动校验和本地诊断等实用能力。

## 特性

- **完整游戏流程** — 安装、更新、修复、启动、卸载 Blue Archive 日服游戏文件
- **启动校验** — 支持本地清单校验、远端清单校验、跳过校验三种模式
- **下载可靠性** — 10 并发 CDN 下载、`.tmp` 临时文件、Range 断点续传、CRC64 校验、主备 CDN 自动回退
- **下载控制** — 暂停 / 继续 / 停止，下载限速（`unlimited` ~ `50MB/s` 六档），进行中状态持久化到 `download_state.json`
- **CDN 切换** — 支持 `official`（yo-star.com）和 `cafe`（bluearchive.cafe）两套下载源
- **多语言** — `auto`（跟随系统）、`en`、`zh-Hans`、`zh-Hant`、`ja`
- **原生 UI** — Avalonia Fluent Theme，系统 / 浅色 / 深色主题，支持系统托盘，关闭时最小化到托盘
- **远端内容** — 公告、活动 Banner、新闻、社交媒体入口
- **背景定制** — 内置 / 远端 / 自定义壁纸，三档契合度（`fill` / `uniform` / `uniformToFill`），染色主题色提取
- **主题色** — 四种模式：默认（`#FF2E7DF6`） / 跟随系统 / 壁纸提取 / 自定义取色
- **自更新** — 服务端代理优先、GitHub Releases API 回退，支持 `stable` / `beta` 频道与启动时后台检查
- **本地诊断** — 运行与异常诊断统一写入 `unified.log`，支持日志轮转、查看与 ZIP 导出
- **Toast 通知**：即时展示操作状态（含运动淡入动画）
- **无障碍**：设置控件和对话框按钮均配有 `AutomationProperties.Name` 标注

## 技术栈

| 项 | 版本 |
|---|---|
| .NET | `net10.0` |
| .NET SDK（仓库固定版本） | `10.0.302` |
| Avalonia | `12.1.1` |
| CommunityToolkit.Mvvm | `8.4.2` |
| Material.Icons.Avalonia | `3.0.2` |
| Microsoft.Extensions.DependencyInjection | `10.0.10` |
| Serilog | `4.4.0` |
| xUnit | `xunit.v3 3.2.2` |
| Coverlet | `coverlet.msbuild 10.0.1` |

Release 分发目标为 `win-x64`、`osx-arm64` 与 `linux-x64` 自包含桌面应用，无需用户额外安装 .NET Runtime。Windows 为正式支持平台；macOS 与 Linux 当前提供实验性构建，尚未完成针对性适配与充分测试。Release 配置关闭调试器、EventSource、元数据热更新等非必要运行时能力；当前未启用 Native AOT 或程序集裁剪。

## 快速开始

需要 .NET 10 SDK。克隆后在仓库根目录执行：

```powershell
dotnet restore .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -r win-x64
.\build.ps1
dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj
```

`build.ps1` 自动关闭遥测：

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
```

## 构建

```powershell
.\verify.ps1                                                             # 完整验证（Debug 构建、覆盖率门禁、Release 构建、本地化产物契约）
.\build.ps1                                                              # Debug 构建（0 警告 0 错误）
dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore       # Debug 构建（跳过还原）
dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Release --no-restore     # Release 构建
dotnet publish .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 -o publish # win-x64 自包含发布
```

项目启用 `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`，分析级别 `latest-recommended`。Debug 构建期望 **0 警告 0 错误**。

### 桌面分发产物

Windows 安装程序使用 Inno Setup 6.3+ 构建（仓库以 Inno Setup 7 验证）。本地构建前需安装 Inno Setup，并确保 `ISCC.exe` 可通过 `PATH`（或默认安装位置 `C:\Program Files\Inno Setup 7`）调用：

```powershell
.\scripts\Build-Distribution.ps1                                    # 发布 win-x64 并打包 zip
.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64  # 跨平台三 RID（CI 在 Linux 上执行）
.\scripts\New-WindowsInstaller.ps1                                  # 依据 artifacts/publish/win-x64 构建 Inno Setup 安装器
```

输出（`artifacts/distribution/`，CI 发版额外产出 AppImage）：

- `Cafe.Launcher.Avalonia_v<version>_win-x64.zip`（Windows 自包含 zip）
- `Cafe.Launcher.Avalonia_v<version>_setup.exe`（Inno Setup 安装器，仅 Windows 宿主）
- `Cafe.Launcher.Avalonia_v<version>_osx-arm64.zip`（内含 `Cafe Launcher.app`，建议在 Linux/macOS 宿主打包以保留可执行位）
- `Cafe.Launcher.Avalonia_v<version>_linux-x64.tar.gz`（建议在 Linux 宿主打包以保留可执行位）

Setup 安装范围为所有用户，默认目录为 `C:\Program Files\Cafe Launcher`，安装、升级和卸载均需要管理员权限。升级会删除旧版本中由安装器管理、但新版本不再发布的文件；安装目录内的其他文件会保留。

交互式卸载可选择删除执行卸载用户的 `%LOCALAPPDATA%\Cafe Launcher`，该选项默认关闭。静默卸载始终保留应用程序数据。安装器不会管理或删除游戏目录。

## 测试

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore                 # 运行单元测试
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore # 运行无头测试
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests" # 运行单个测试类
```

测试工程位于 `tests/Cafe.Launcher.Avalonia.Tests/`，不引入 Moq/NSubstitute 等模拟框架——所有测试通过手写 `HttpMessageHandler` 子类和手动桩实现。源码项目通过 `InternalsVisibleTo` 向测试暴露 `internal` 成员。

当前测试树包含 66 个单元测试类文件和 7 个 Headless UI 测试类文件，覆盖启动器 API、设置规范化、本地安装状态、下载与校验、安全路径、Shell 生命周期、模态栈、本地化资源契约、设置与对话框交互、发布脚本等关键路径。`coverage.ps1` 合并两个测试工程的手写 C# 覆盖率，要求行/分支覆盖率均不低于 50%，且不得低于仓库记录的行 84.43% / 分支 88.99% 基线。

`UiStyleContractTests` 强制执行设计标记契约：禁止视图 XAML 中出现裸色值，强制使用 `LauncherSpacing*` 标记，验证 Z-Index 分层顺序，确保动态主题色笔刷不替代主题字典笔刷。修改 XAML 或样式时务必运行此测试。

### 黄金截图（Headless Skia 渲染）

Headless 测试套件以 Skia 渲染（`UseHeadlessDrawing=false` + `UseSkia()`）运行，`MainWindowHeadlessTests.Golden_*` 对 5 个关键状态（壳默认 / 进度面板 / 设置覆盖层 / 确认对话框 / Toast）做像素级回归比对，基线与阈值 diff 见 `tests/Cafe.Launcher.Avalonia.HeadlessTests/Baselines/`。

- **有意改动视觉后重新生成基线**（提交 PNG 与改动一起）：
  ```powershell
  $env:CAFE_GOLDEN_UPDATE = "1"
  dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~Golden"
  Remove-Item Env:CAFE_GOLDEN_UPDATE
  ```
- **CI（windows-latest）字体稳定性**：对比允许每通道容差（8/255）与 ≤1% 失配像素比例；基线在固定状态（英文、动效关闭、窗口字体固定 Segoe UI）下生成，但 CI 与本地机器的系统字体度量仍可能有亚像素差异——阈值已考虑该风险，若 CI 出现接近阈值的漂移，优先重生成基线而非放宽阈值。

## 配置与本地文件

启动器使用 `%LOCALAPPDATA%\Cafe Launcher\` 保存本地数据：

| 文件 | 用途 |
|---|---|
| `settings.json` | 启动器设置 |
| `unified.log` / `unified_*.log` | 统一运行与异常日志（单文件 5 MB，当前文件加 3 份轮转；查看器按 500 条分页） |
| `download_state.json` | 下载任务状态（断点续传） |
| `shown_notices.json` | 已展示公告 ID |
| `clickCode` | 安装归因码 |
| `log-exports/` | 日志 ZIP 的默认导出目录（导出时可另选位置） |

### 设置项

| 设置 | JSON 键 | 有效值 |
|---|---|---|
| 语言 | `language` | `auto` / `en` / `zh-Hans` / `zh-Hant` / `ja` |
| 主题 | `themeMode` | `system` / `light` / `dark` |
| 动效 | `motionMode` | `system` / `full` / `reduced` |
| 下载源 | `patchUrlGroup` | `official` / `cafe` |
| 启动校验 | `launchCheckMode` | `localManifest` / `remoteManifest` / `none` |
| 下载限速 | `downloadSpeedLimit` | `unlimited` / `1MB/s` / `5MB/s` / `10MB/s` / `25MB/s` / `50MB/s` |
| 启动时检查更新 | `enableStartupUpdateCheck` | `true` / `false` |
| 关闭行为 | `closeBehavior` | `minimize` / `exit` |
| 代理 | `proxyMode` | `auto` / `direct` / `system` |
| 背景来源 | `backgroundSource` | `bundled` / `remote` / `custom` |
| 壁纸契合度 | `backgroundFit` | `fill` / `uniform` / `uniformToFill` |
| 壁纸背景色 | `backgroundFillColor` | 十六进制颜色（如 `#FF000000`） |
| 主题色模式 | `themeColorMode` | `default` / `system` / `wallpaper` / `custom` |
| 壁纸取色算法 | `themeColorExtractionAlgorithm` | `celebiScore` / `wu` / `wsmeans` / `octree` |
| M3 配色变体 | `themeColorVariant` | `tonalSpot` / `vibrant` / `expressive` / `fidelity` / `content` / `monochrome` / `neutral` / `rainbow` |
| 中性色策略 | `neutralColorStrategy` | `brandBlue` / `seedFollowing` |
| 自定义主题色 | `customThemeColor` | 十六进制颜色 |
| 主题色调色板 | `themeColorPalette` | JSON 十六进制数组 |
| 调色板选中索引 | `selectedThemeColorPaletteIndex` | 整数 |
| 游戏路径 | `gamePath` | 绝对路径 |
| 自定义背景 | `customBackgroundPath` | 绝对文件路径 |
| 远端内容卡片 | `showRemoteContentCard` | `true` / `false` |
| 记住窗口位置和大小 | `rememberWindowPositionAndSize` | `true` / `false` |
| 更新频道 | `updateChannel` | `stable` / `beta` |
| 日志级别 | `logLevel` | `verbose` / `debug` / `information` / `warning` / `error` / `fatal` |
| 资源面板 UID | `resourcePanelUid` | 玩家 UID 字符串 |
| 资源面板 UID 来源 | `resourcePanelUidSource` | `auto` / `custom` |
| 状态面板 | `statusDetailMode` | `hidden` / `compact` |

预发布构建默认使用 `beta` 更新频道；稳定构建默认使用 `stable`。中文系统界面首次启动时默认选择 `cafe` 下载源，其他语言环境默认选择 `official`。游戏目录规范化为 `YostarGames\BlueArchive_JP`，本地游戏状态从 `game-launcher-config.json` 和 `manifest.json` 读取。

## 项目结构

```text
.
├── Cafe.Launcher.Avalonia.slnx # XML 解决方案（应用与测试项目）
├── src/
│   └── Cafe.Launcher.Avalonia/
│       ├── App.axaml / App.axaml.cs # 应用入口：主题字典、DI 容器构建、窗口创建
│       ├── Program.cs          # 进程入口：单实例互斥、崩溃日志、会话起止日志
│       ├── Cafe.Launcher.Avalonia.csproj
│       ├── Constants/          # LauncherConstants / ApiConfig / BuildInfo / GamePaths
│       ├── Controls/           # 共享 UI 控件（SettingRow、ConfirmDialog、LoadingOverlay）
│       ├── Converters/         # 值转换器（URL→Bitmap、ToastSeverity→Brush）
│       ├── Helpers/            # 工具类（FileSizeFormatter、GamePathValidator、HttpClientLease）
│       ├── Models/             # 数据模型（API 合约、状态模型、安装状态、清单结构等）
│       ├── Composition/        # DI 组合根（ServiceConfiguration）
│       ├── Features/           # Shell、游戏操作、设置、首次向导、诊断、资源面板
│       ├── Services/           # 共享基础服务（HTTP、设置、本地化、日志、托盘等）
│       │   ├── Auth/           # AuthorizationHeaderFactory（MD5 签名认证头）
│       │   └── Diagnostics/    # 日志、日志轮转、日志导出
│       ├── ViewModels/         # 共享窗口投影（Shell、背景、远端内容、对话框等）
│       ├── Views/              # 主窗口、六类设置区、覆盖层与功能样式
│       ├── Assets/             # 图标、字体、音频与图片
│       └── Resources/          # .resx 原生本地化资源及生成的强类型访问器
├── tests/                      # xUnit v3 单元测试与 Avalonia Headless UI 测试
├── scripts/                    # 分发、本地化校验与 changelog 脚本
├── release.ps1                 # 本地发布脚本
├── build.ps1                   # Debug 构建脚本
└── CLAUDE.md                   # AI 辅助开发指引
```

## 架构

### 入口流程

```
Program.Main()
  ├─ Mutex(Local\Cafe_Launcher_SI) → 已是第二实例 → SignalFirstInstance() → return
  ├─ new UnifiedLogger() → 进程级日志
  ├─ Program.RunSession(UnifiedLogger) → 写会话开始日志 → 运行应用 → 写会话结束日志
  └─ BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
       └─ App.OnFrameworkInitializationCompleted()
            ├─ ServiceConfiguration.AddLauncherServices() → 构建 DI 容器
            ├─ 解析 MainWindowViewModel → 创建 MainWindow
            ├─ 注册 ClickCodeService / SystemTrayService / 单实例信号监听
            └─ MainWindowViewModel.InitializeAsync()
                 ├─ LauncherCoreService.LoadAsync() → 并行 API + 本地状态
                 └─ 更新 UI 状态
```

### 核心数据流

`LauncherCoreService.LoadAsync()` 是核心编排器：

1. 读取 `settings.json`（`LauncherSettingsService`）
2. 并行调用 6 个远端 API：game config / base config / CDN config（必选） + operations / social media / installation config（可选）
3. 读取本地 `game-launcher-config.json` + `manifest.json`（`LocalInstallationStateStore`）
4. 结合本地分类与远端版本比对，计算 `LauncherRuntimeState`
5. 返回 `LauncherStatusSnapshot`，由 ViewModel 消费

### MVVM 架构

`ViewModelBase` 继承 `ObservableObject`（CommunityToolkit.Mvvm 源生成器）。不使用反射式 `ViewLocator`，所有视图-ViewModel 绑定通过显式 XAML 组合实现。

主窗口为 1300×754 无系统边框窗口（MinWidth 1024 / MinHeight 640），ViewModel 拆分为多个子 ViewModel：

| ViewModel | 职责 |
|---|---|
| `ShellViewModel` | 产品名、版本、运行时信息、状态文本、游戏路径显示 |
| `BackgroundViewModel` | 壁纸（内置 / 远端 / 自定义）、主题色提取 |
| `RemoteContentViewModel` | 公告、Banner、新闻、社交媒体 |
| `DialogsViewModel` | 通知弹窗、修复 / 卸载确认 |
| `GameOperationsViewModel` | 安装 / 更新 / 修复 / 启动 / 卸载命令与进度 |
| `ToastHostViewModel` | 即时通知队列 |
| `WindowChromeViewModel` | 标题栏、最小化 / 关闭按钮、窗口拖拽状态 |
| `SettingsViewModel` | 六类设置协调、持久化、更新检查、保存 / 放弃生命周期 |
| `SettingsAppearanceViewModel` | 主题色与背景 UI 投影 |
| `SettingsOptionsViewModel` | 本地化选项集合与摘要显示 |
| `ResourcePanelViewModel` | 资源面板（基于 UID） |

`MainWindowViewModel` 主要聚合各 UI 投影；跨功能生命周期、模态栈、Esc 路由和状态刷新集中在 `ShellRuntime` / `ShellLifecycle`。游戏安装、启动、修复与卸载通过 `IGameOperationJourneyFactory` 组装独立旅程，避免让窗口 ViewModel 直接编排底层服务。

视图文件按职责拆分：
- `MainWindow.axaml` — 窗口壳、标题栏、远端内容、底部面板
- `MainWindow.Styles.axaml` — 共享样式与功能样式入口
- `Views/Styles/` — Diagnostics、RemoteContent、SetupWizard、Toast 功能样式
- `MainWindowSettingsOverlay.axaml` — 设置覆盖层（Z-Index 100）
- `MainWindowDialogsOverlay.axaml` — 对话框覆盖层（Z-Index 200）
- `MainWindowToastOverlay.axaml` — Toast 覆盖层（Z-Index 1000）

### 下载流程

`GameDownloadService` 实现完整的安装 / 更新 / 修复流程：

1. 获取远端清单（`RemoteManifestService`）→ 与本地清单比对差异
2. 通过 `IFileDownloadService` 并行下载（10 并发）
3. 写入 `.tmp` 临时文件，支持 `Range` 断点续传
4. 下载完成后 CRC64 校验
5. 校验通过后 `.tmp` → 最终文件名
6. 全部完成 → 写入 `game-launcher-config.json` + `manifest.json`
7. 安装后最多重试 3 次验证

所有文件操作通过 `GamePathValidator.GetSafePath()` 约束在游戏目录内，拒绝路径穿越。

### DI 注册要点

`ServiceConfiguration.AddLauncherServices()` 统一注册：
- 服务与 ViewModel 全部 `AddSingleton`（单窗口桌面应用，无 Scope 边界）
- `GameDownloadService` 在组合根中显式组装下载、清单、设置、磁盘空间与诊断依赖
- `IDisposable` 服务按反向注册顺序释放，确保使用方先于共享 HTTP 与日志基础设施结束

## 远端接口

`LauncherApiClient` 使用 `https://api-launcher-jp.yo-star.com` 作为 API 基址，通过 `AuthorizationHeaderFactory` 生成 Yostar 风格的 MD5 签名认证头。

调用路径：

| 路径 | 用途 |
|---|---|
| `/api/launcher/game/config` | 游戏配置 |
| `/api/launcher/base/config` | 基础配置 |
| `/api/launcher/advanced/game/download/cdn` | CDN 下载配置 |
| `/api/launcher/operations/resource` | 运营资源（可选） |
| `/api/launcher/social/media/resource` | 社交媒体资源（可选） |
| `/api/launcher/installation/config` | 安装配置（可选） |
| `/api/launcher/game/config/json?version=...&file_path=...` | 按路径获取配置 |

CDN 切换由 `PatchUrlGroupService` 实现：`cafe` 源仅将 `launcher-pkg-ba-jp.yo-star.com` 重写为 `launcher-pkg-ba-jp.bluearchive.cafe`，不影响其他端点。

Launcher 自更新优先请求服务端代理 `ApiConfig.LauncherApiBaseUrl`；代理路径发生 HTTP 错误时回退 GitHub Releases API。返回的下载地址仅接受分发仓库的 HTTPS Release 资产。

## CI 与发布

GitHub Actions 使用 .NET 10.0.x：

- **`build.yml`**（`windows-latest`，push / PR to `main`）：测试 → 覆盖率门禁 → Release `win-x64` 发布冒烟 → 上传测试与覆盖率报告。NuGet 缓存 + concurrency 取消排队运行。
- **`release.yml`**（push `v*` tag / 手动 dispatch 演练）：**`build`** job（`ubuntu-latest`，交叉编译 win-x64 / osx-arm64 / linux-x64 并打包 zip、`.app`、tar.gz、AppImage）→ **`installer`** job（`windows-latest`，Release 测试 + Inno Setup EXE）→ **`release`** job（生成 changelog 并在源仓库和分发仓库 `bluearchive-cafe/Cafe.Launcher.Avalonia_Release` 同时创建 GitHub Release）。预发布版标签含 `-`。

本地发布脚本：

```powershell
.\release.ps1 patch              # 递增 patch 版本，生成 changelog，commit，tag，push
.\release.ps1 minor -DryRun      # 预览 minor 版本提升（不修改文件）
.\release.ps1 2.0.0-beta.1       # 指定版本（含 - 为预发布）
.\release.ps1 patch -SkipPush    # 仅本地 commit + tag，不推送
```

版本号读取自 `.csproj` 的 `<VersionPrefix>`。发布说明优先复用仓库中的 `CHANGELOG_RELEASE.md`，缺失时回退到 `scripts/New-ReleaseChangelog.ps1` 自动生成。commit 消息请遵循 conventional commits 格式（`feat:` / `fix:` / `refactor:` / `perf:`），以确保 changelog 正确分组。

## 相关文档

- [隐私政策](./PRIVACY.md) — Cafe Launcher 的数据处理与隐私说明
- [第三方许可](./THIRD-PARTY-NOTICES.md) — 随发行版分发的 NuGet 依赖及其许可
- [CLAUDE.md](./CLAUDE.md) — AI 辅助开发指引（Claude Code）
- [AGENTS.md](./AGENTS.md) — AI 辅助开发指引（Codex）

## 相关链接

- [Blue Archive Cafe](https://bluearchive.cafe/)
- [Releases](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release)
- [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues)

## 致谢

- 默认壁纸裁剪自 Pixiv 画师 [めるき（Meruki）](https://www.pixiv.net/users/15737611) 的插画 [初めてのゲーム](https://www.pixiv.net/artworks/142932674)，插画版权归原作者所有。
- 壁纸原始素材存档于 [docs/assets/art-sources/](./docs/assets/art-sources/)。

## 源代码

Cafe Launcher 源代码已在本仓库公开，并采用 [MIT License](./LICENSE)。安装包与各平台自包含归档由独立的 [Release 分发仓库](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases) 提供。
