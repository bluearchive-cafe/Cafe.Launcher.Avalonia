# Repository Map

- 审计日期：2026-09-09（release 增量审计）
- HEAD：`fca9bc0` `chore: 添加发布横幅`
- 发布状态：最新已发布 tag 为 `v1.1.0-beta.7`；`v1.1.0-beta.8` 的版本号、发布说明与横幅已准备，尚未创建 tag

## Project

- 类型：跨平台桌面启动器 / 更新器
- 语言与框架：C#、XAML、.NET 10、Avalonia 12.1.2
- 支持平台：Windows x64（正式）；macOS arm64、Linux x64（实验性）
- 分发：GitHub Actions 构建自包含 ZIP、macOS app ZIP、tar.gz、deb、AppImage 与 Windows Inno Setup 安装器，并发布到源码仓库及独立 Release 仓库

## Structure

- 生产源码：`src/Cafe.Launcher.Avalonia/`
- Feature：`Features/Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
- 共享层：`Services/`、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、根 `ViewModels/`
- 组合根：`Composition/ServiceConfiguration.cs`
- 单元测试：`tests/Cafe.Launcher.Avalonia.Tests/`（当前本机 1524 过 / 2 跳）
- Headless UI：`tests/Cafe.Launcher.Avalonia.HeadlessTests/`（当前本机 167/167）
- CI：`.github/workflows/build.yml`
- 发布：`.github/workflows/release.yml`、`release.ps1`、`scripts/Build-Distribution.ps1`、`scripts/New-WindowsInstaller.ps1`
- 安装资源：`installer/`
- 架构/决策：`CONTEXT.md`、`docs/design/adr/`

## Architecture

- MVVM + 垂直 Feature 切片
- Shell 是窗口壳层，允许通过 `ShellPresentationFamily` 向下聚合具体展示 ViewModel
- 非 Shell Feature 不得互相引用具体类型；共享展示契约位于根 `ViewModels/`
- 持久化经 `AtomicJsonFileStore`；游戏运行器、下载与诊断服务位于共享 Services/Feature 边界内

## Verification Commands

- Build：`.\build.ps1`
- Test：`.\test.ps1`
- Coverage：`.\coverage.ps1`
- Full verification：`.\verify.ps1`
- Localization：`.\scripts\Test-LocalizationContract.ps1`
- Distribution：`.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64`
- Windows installer：`.\scripts\New-WindowsInstaller.ps1`

## Key Repository Rules

- `PROJECT_CONVENTIONS.md` 与 `AGENTS.md` 是主要工程契约
- warnings-as-errors、编译绑定、设计 token、本地化四语言同步
- NuGet 中央版本管理 + committed lock files；CI locked mode，RID restore 明确豁免
- CHANGELOG_RELEASE.md 只保留当前单版本，面向安装用户；版本横幅必须随 tag 提交
- 发布提交遵循 Conventional Commits；`release.ps1` 负责版本提交、tag 与 push

## Risk Profile

- Critical：下载完整性、文件系统安全、发布供应链、更新恢复
- High：跨平台行为、测试确定性、UI 线程性能、持久化/迁移、架构边界
- Medium：本地化契约
- Low：一般代码气味
- 信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、崩溃快照、CI/发布凭据、安装器权限边界

## Previous Audit State

- Last full audit commit：`cffbd4d`（2026-09-09）
- Last audit commit：`fca9bc0`（本轮 release）
- Open Critical / High / Medium：0 / 0 / 0
- 非阻塞 Low：3 deferred + 1 accepted-risk
