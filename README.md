# Cafe Launcher

Blue Archive 日服桌面启动器，使用 .NET 10 和 Avalonia 12 编写，替代原 Electron 启动器。项目提供原生桌面体验，并在安装、更新、修复、启动校验、下载源切换、本地诊断等流程上补充实用功能。

## 特性

- **完整游戏流程**: 安装、更新、修复、启动和卸载 Blue Archive 日服游戏文件。
- **启动校验**: 支持本地 `manifest.json` 校验、远端清单校验和跳过校验。
- **下载可靠性**: 支持 10 并发、`.tmp` 临时文件、Range 断点续传、CRC64 校验、主备 CDN 重试和下载限速。
- **下载控制**: 支持暂停、继续和停止；进行中的下载状态写入 `%LOCALAPPDATA%\Cafe Launcher\download_state.json`。
- **CDN 切换**: 支持 `official` 和 `cafe` 下载源，`cafe` 源使用 `bluearchive.cafe` 的安装包下载主机。
- **多语言**: 支持 `auto`、`en`、`zh-Hans`、`ja`，`auto` 跟随系统 UI 语言。
- **原生 UI**: 基于 Avalonia Fluent Theme，支持系统、浅色、深色主题，支持系统托盘和关闭时最小化到托盘。
- **远端内容**: 显示公告、活动 Banner、新闻和社交媒体入口。
- **背景设置**: 支持内置背景、远端背景和自定义背景；可配置壁纸契合度（填充/适应/覆盖）和适应模式下的留白颜色。
- **离线诊断**: 崩溃日志写入 `%LOCALAPPDATA%\Cafe Launcher\crash.log`，运行诊断写入 `%LOCALAPPDATA%\Cafe Launcher\diagnostics.log`。
- **无远端遥测**: 不包含远端遥测上报路径。

## 技术栈

| 项目 | 版本 |
| --- | --- |
| .NET | `net10.0` |
| Avalonia | `12.0.4` |
| CommunityToolkit.Mvvm | `8.4.2` |
| Material.Icons.Avalonia | `3.0.2` |
| xUnit | `2.9.3` |
| coverlet.collector | `10.0.1` |

Release 配置使用 `win-x64`、`SelfContained=true`，输出类型为 `WinExe`。

## 快速开始

安装 .NET 10 SDK 后，在仓库根目录执行：

```powershell
dotnet restore .\Cafe.Launcher.Avalonia.csproj
.\build.ps1
dotnet run --project .\Cafe.Launcher.Avalonia.csproj
```

`build.ps1` 会设置：

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
```

## 构建

```powershell
.\build.ps1
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish
```

## 测试

```powershell
dotnet test
dotnet test --filter "FullyQualifiedName~VersionComparerTests"
```

测试工程位于 `tests/Cafe.Launcher.Avalonia.Tests/`。当前测试类包括：

- `VersionComparerTests`
- `LauncherApiClientTests`
- `LauncherConstantsTests`
- `LauncherSettingsServiceTests`
- `LocalizationServiceTests`
- `MainWindowViewModelTests`
- `GameDownloadServiceTests`
- `PatchUrlGroupServiceTests`
- `LocalGameStateServiceTests`
- `BestHttpCookieLibraryServiceTests`
- `ResourcePanelUidServiceTests`
- `ExternalLinkServiceTests`
- `ResourcePanelApiClientTests`

## 配置和本地文件

启动器使用 `%LOCALAPPDATA%\Cafe Launcher\` 保存本地数据：

| 文件 | 用途 |
| --- | --- |
| `settings.json` | 启动器设置 |
| `diagnostics.log` | 运行诊断日志 |
| `crash.log` | 全局未处理异常日志 |
| `download_state.json` | 下载任务状态 |
| `shown_notices.json` | 已展示公告记录 |
| `clickCode` | 安装归因码 |

`settings.json` 中使用的持久化字段包括：

- `gamePath`
- `launchCheckMode`
- `proxyMode`
- `closeBehavior`
- `language`
- `themeMode`
- `themeColorMode`
- `customThemeColor`
- `themeColorPalette`
- `selectedThemeColorPaletteIndex`
- `downloadSpeedLimit`
- `toastNotificationsEnabled`
- `showRemoteContentCard`
- `patchUrlGroup`
- `customBackgroundPath`
- `backgroundSource`
- `backgroundFit`
- `backgroundFillColor`
- `resourcePanelUid`

游戏目录会被规范化为 `YostarGames\BlueArchive_JP`。本地游戏状态读取：

- `game-launcher-config.json`
- `manifest.json`

设置面板中的选项：

| 设置 | 选项 |
| --- | --- |
| 语言 | `auto` / `en` / `zh-Hans` / `ja` |
| 主题 | `system` / `light` / `dark` |
| 下载源 | `official` / `cafe` |
| 启动校验 | `LocalManifest` / `RemoteManifest` / `None` |
| 下载限速 | `unlimited` / `1MB/s` / `5MB/s` / `10MB/s` / `25MB/s` / `50MB/s` |
| 关闭行为 | `minimize` / `exit` |
| 代理 | `direct` / `system` |
| 背景 | `bundled` / `remote` / `custom` |
| 壁纸契合度 | `fill` / `uniform` / `uniformToFill` |
| 壁纸背景色 | 十六进制颜色（如 `#FF000000`） |

## 项目结构

```text
.
├── App.axaml
├── App.axaml.cs
├── Program.cs
├── Cafe.Launcher.Avalonia.csproj
├── Constants/
├── Converters/
├── Helpers/
├── Models/
├── Services/
├── ViewModels/
├── Views/
└── tests/
```

关键入口：

- `Program.cs`: 进程互斥、单实例信号、崩溃日志、Avalonia 启动。
- `App.axaml.cs`: 通过 `ServiceConfiguration.AddLauncherServices()` 构建 DI 容器，初始化主窗口、系统托盘和单实例窗口恢复监听。
- `ServiceConfiguration`: DI 组合根，注册全部服务（Singleton）和 ViewModel（Transient），构造 `MainWindowViewModel`。
- `LauncherCoreService`: 读取设置、并行请求远端配置、读取本地游戏状态，生成 `LauncherStatusSnapshot`。
- `MainWindowViewModel`: 驱动主窗口状态、设置面板、安装/更新/修复/卸载/启动命令、远端内容和本地化。

## 架构说明

项目使用 MVVM。`ViewModelBase` 继承 `ObservableObject`，XAML 默认启用编译绑定。主窗口及覆盖层使用显式 XAML 组合，不依赖反射式 ViewLocator。

主窗口是一个 1300 x 754 的不可调整大小、无系统边框窗口。窗口内容按职责拆分：

- `Views/MainWindow.axaml`: 主窗口壳、标题栏、远端内容、底部操作面板。
- `Views/MainWindow.Styles.axaml`: 主窗口样式。
- `Views/MainWindowSettingsOverlay.axaml`: 设置覆盖层。
- `Views/MainWindowDialogsOverlay.axaml`: 公告、修复、卸载等对话框覆盖层。
- `Views/MainWindowToastOverlay.axaml`: Toast 覆盖层。

下载流程由 `GameDownloadService` 实现。它从远端清单和本地清单计算差异，下载缺失或损坏文件，写入临时配置，校验后再把 `.tmp` 文件移动到正式路径。所有文件路径都通过 `GamePathValidator.GetSafePath()` 限制在游戏目录内。

卸载流程由 `GameUninstallService` 实现。它会拒绝系统保护路径，要求目录名为 `BlueArchive_JP`，检查游戏进程未运行，然后只删除清单记录的文件以及 `manifest.json`、`game-launcher-config.json`。

启动流程由 `GameLaunchService` 实现。启动前会检查安装状态、最低版本、可执行文件名、可执行文件是否存在，并按设置执行本地清单、远端清单或跳过校验。

## 远端接口

`LauncherApiClient` 使用 `https://api-launcher-jp.yo-star.com` 作为 API 根地址，并为请求添加 Yostar 风格的 `Authorization` 头。使用的接口路径包括：

- `/api/launcher/game/config`
- `/api/launcher/base/config`
- `/api/launcher/advanced/game/download/cdn`
- `/api/launcher/operations/resource`
- `/api/launcher/social/media/resource`
- `/api/launcher/installation/config`
- `/api/launcher/game/config/json?version=...&file_path=...`

下载源切换由 `PatchUrlGroupService` 处理。`cafe` 源只把 `launcher-pkg-ba-jp.yo-star.com` 重写为 `launcher-pkg-ba-jp.bluearchive.cafe`。

## 发布

CI 位于 `.github/workflows/`：

- `build.yml`: push 或 pull request 到 `main` 时执行 restore、Debug build、测试、Release build、Release publish，并上传 `publish\`。
- `release.yml`: push `v*` tag 时执行 Release 测试、构建、发布、压缩 ZIP、生成 changelog，并通过 `softprops/action-gh-release@v2` 在源码仓库和分发仓库（`bluearchive-cafe/Cafe.Launcher.Avalonia_Release`，对应常量 `GitHubReleaseRepositorySlug`）同时创建 GitHub Release。分发仓库使用源码仓库 Actions Secret `RELEASE_REPOSITORY_TOKEN`。

本地发布准备脚本：

```powershell
.\release.ps1 patch
.\release.ps1 minor -DryRun
.\release.ps1 2.0.0-beta.1
.\release.ps1 patch -SkipPush
```

脚本会读取并更新 `Cafe.Launcher.Avalonia.csproj` 中的 `<VersionPrefix>`，通过 `scripts/New-ReleaseChangelog.ps1` 生成 `CHANGELOG_RELEASE.md`，提交版本变更，创建 annotated tag，并按参数推送到 `origin`。GitHub Actions 使用同一脚本生成 Release 正文，确保本地预览与两个仓库的 Release 日志格式一致。

## 相关链接

- [Blue Archive Cafe](https://bluearchive.cafe/)
- [Releases](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release)
- [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues)

## 许可证

本项目使用 MIT License。版权信息见 [LICENSE](./LICENSE)。
