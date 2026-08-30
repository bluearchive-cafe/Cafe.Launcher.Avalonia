# Cafe Launcher

面向 Blue Archive 日服的第三方桌面启动器。使用 .NET 10 与 Avalonia 构建，提供游戏安装、更新、修复、启动和本地诊断，并兼容官方启动器使用的游戏目录与清单。

[![Build](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/actions/workflows/build.yml/badge.svg)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/bluearchive-cafe/Cafe.Launcher.Avalonia_Release?include_prereleases&label=release)](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases)
[![License](https://img.shields.io/github/license/bluearchive-cafe/Cafe.Launcher.Avalonia)](./LICENSE)

[下载](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release/releases) · [使用文档](https://docs.bluearchive.cafe/cafe-launcher/) · [问题反馈](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues) · [官网](https://bluearchive.cafe/)

> [!IMPORTANT]
> Cafe Launcher 是社区维护的第三方项目，与 Nexon、Nexon Games、Yostar 及 Blue Archive 官方无隶属或合作关系。使用前请阅读[隐私政策](./PRIVACY.md)。

## 能做什么

- 安装、增量更新、修复、启动和卸载游戏
- 并发下载、断点续传、暂停与恢复、速度限制和 CRC64 完整性校验
- 在官方 CDN 与 Cafe CDN 之间切换，并在下载失败时尝试备用地址
- 使用本地清单、远程清单或跳过检查三种启动验证模式
- 使用系统、浅色或深色主题，自定义背景、主题色和动态效果
- 使用 English、简体中文、繁體中文和日本語界面
- 查看公告与运营内容，并通过 UID 使用 Cafe 资源面板
- 检查稳定版或测试版更新，导出日志和系统信息用于故障排查

下载任务状态和应用设置保存在本地。启动器不会修改游戏进程，也不会向游戏注入代码。

## 平台与发行包

| 平台 | 支持状态 | 发行包 |
| --- | --- | --- |
| Windows x64 | 正式支持 | 安装程序、便携 ZIP |
| macOS Apple Silicon | 实验性 | `.app` 压缩包 |
| Linux x64 | 实验性 | `.deb`、AppImage、`tar.gz` |

所有发行包均为自包含应用，无需另外安装 .NET Runtime。macOS 与 Linux 构建尚未完成与 Windows 同等程度的适配和测试，请以具体 Release 说明为准。

面向普通用户的安装、首次设置和故障排查说明统一维护在[文档站](https://docs.bluearchive.cafe/cafe-launcher/)。本仓库 README 主要面向参与开发和审阅源码的贡献者。

## 本地开发

### 环境要求

- .NET SDK `10.0.302`（由 `global.json` 固定）
- Windows、macOS 或 Linux 桌面环境
- 构建 Windows 安装程序时需要 Inno Setup 6.3 或更高版本

克隆仓库后，在 PowerShell 中运行：

```powershell
.\build.ps1
dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj
```

仓库脚本会关闭 .NET CLI 与 Avalonia 遥测。项目启用可空引用类型、编译绑定、代码风格检查和 warnings-as-errors。

### 常用命令

| 命令 | 用途 |
| --- | --- |
| `.\build.ps1` | 还原并构建 Debug 配置 |
| `.\test.ps1` | 运行单元测试与 Headless UI 测试 |
| `.\coverage.ps1` | 运行覆盖率门禁 |
| `.\verify.ps1` | 执行 Debug 构建、覆盖率和 Release 构建 |
| `.\dev.ps1 ui` | 验证 XAML 样式契约与 Headless UI |
| `.\scripts\Test-LocalizationContract.ps1` | 验证所有本地化资源键和格式占位符 |

只运行一个测试类：

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"
```

## 代码结构

```text
src/Cafe.Launcher.Avalonia/
├── Composition/     # 依赖注入组合根
├── Features/        # Shell、游戏操作、设置、向导、诊断和资源面板
├── Services/        # 网络、下载、清单、设置、本地化、日志等基础服务
├── Models/          # 设置、API、清单和运行状态模型
├── ViewModels/      # 主窗口及共享界面投影
├── Views/           # Avalonia 视图、覆盖层与样式
├── Resources/       # 多语言 .resx 资源
└── Assets/          # 图标、字体、音频与图片

tests/
├── Cafe.Launcher.Avalonia.Tests/          # xUnit 单元测试
└── Cafe.Launcher.Avalonia.HeadlessTests/  # Avalonia Headless UI 与黄金截图测试
```

应用以 `ServiceConfiguration.AddLauncherServices()` 为组合根。`LauncherCoreService` 汇总远端配置和本地安装状态；游戏安装、更新、修复与卸载由独立操作旅程编排；`MainWindowViewModel` 负责组合各功能 ViewModel，而不是直接承载底层业务流程。

更完整的目录约定、代码风格和验证要求见 [AGENTS.md](./AGENTS.md) 与 [PROJECT_CONVENTIONS.md](./PROJECT_CONVENTIONS.md)。

## 本地数据

Windows 默认将设置和诊断数据写入 `%LOCALAPPDATA%\Cafe Launcher\`，包括 `settings.json`、`download_state.json`、`unified.log` 和日志导出文件。游戏目录中的 `manifest.json` 与 `game-launcher-config.json` 用于记录游戏安装状态，并与官方启动器保持兼容。

卸载启动器不会默认删除游戏文件。详细的数据保留规则见[卸载与数据](https://docs.bluearchive.cafe/cafe-launcher/uninstall)。

## 构建发行包

```powershell
.\scripts\Build-Distribution.ps1
.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64
.\scripts\New-WindowsInstaller.ps1
```

分发产物写入 `artifacts/distribution/`。正式发布由 `.github/workflows/release.yml` 处理，并同步到独立的 [Release 仓库](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release)。

## 参与贡献

提交问题前，请先搜索现有 [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues)，并在可能的情况下附上版本、复现步骤和通过“设置 → 高级 → 导出日志”生成的诊断包。

代码贡献请保持改动聚焦，遵循 Conventional Commits，并在提交前运行与改动范围匹配的验证。UI 改动应附截图，本地化改动应同时更新所有语言资源。

## 许可

源代码基于 [MIT License](./LICENSE) 发布。第三方组件及其许可见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
