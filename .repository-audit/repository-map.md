# Repository Map

- 审计日期：2026-09-11（full 全量重审 + 同日按优先度修复）
- HEAD：`66e103a` `refactor(diagnostics): 收敛日志导出标签、ErrorAsync 重载、模态命名与探测读取路径`（本轮修复尚在工作树，未提交）
- 发布状态：最新已发布 tag 为 `v1.1.0-beta.8`（tag 指向 `307edc3`，banner 已提交）；tag 之后 `main` 已累积 13 提交，含两项用户可见特性（HTTP/2 设置、日志导出时间范围与可选内容），下一版本尚未开始准备
- 上次全量审计基线：`cffbd4d`

## Project

- 类型：跨平台桌面启动器 / 更新器（Blue Archive 日服）
- 语言与框架：C#、XAML、.NET 10（SDK 10.0.302，global.json `latestFeature`）、Avalonia 12.1.2
- 支持平台：Windows x64（正式）；macOS arm64（实验性，暂不支持启动游戏）、Linux x64（实验性）
- 分发：GitHub Actions 构建自包含 ZIP、macOS app ZIP、tar.gz、deb、AppImage 与 Windows Inno Setup 安装器，发布到源码仓库及独立 Release 仓库

## Structure

- 生产源码：`src/Cafe.Launcher.Avalonia/`（234 个 `.cs`，约 33k 行；28 个 `.axaml`）
- Feature：`Features/Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
- 共享层：`Services/`（含 `Diagnostics/`、`GameRuntime/`、`Auth/`）、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、根 `ViewModels/`
- 组合根：`Composition/ServiceConfiguration.cs`
- 单元测试：`tests/Cafe.Launcher.Avalonia.Tests/`（本机 1607 过 / 2 跳 / 0 失败）
- Headless UI：`tests/Cafe.Launcher.Avalonia.HeadlessTests/`（本机 173/173，含 7 份黄金基线）
- 原型（不参与发布）：`prototypes/FluentMotionLab`（显式关闭 lock 文件）
- CI：`.github/workflows/build.yml`（windows-latest）
- 发布：`.github/workflows/release.yml`（ubuntu-24.04，三 RID）、`release.ps1`、`scripts/Build-Distribution.ps1`、`scripts/New-WindowsInstaller.ps1`
- 安装资源：`installer/`（Inno Setup 7.x）
- 架构/决策：`CONTEXT.md`、`docs/design/adr/`（ADR-001…020）

## Architecture

- MVVM + 垂直 Feature 切片
- Shell 是窗口壳层，允许通过 `ShellPresentationFamily` 向下聚合具体展示 ViewModel（AGENTS.md 明文豁免）
- 非 Shell Feature 不得互相引用具体类型；共享展示契约位于根 `ViewModels/`（`IModalContentViewModel`/`ModalKind`/`ModalEntry`）
- 持久化经原子 JSON 落盘；下载、校验、诊断服务位于共享 Services 层
- 网络面集中：`HttpClientFactory`（池化 handler + 代理租约）+ `RemoteHttpRequestService`（统一手动重定向、每跳复验）+ `RemoteHttpUrlValidator`（DNS 私网校验 + 短 TTL 缓存）

## Verification Commands

- Build：`.\build.ps1`
- Test：`.\test.ps1`
- Coverage：`.\coverage.ps1`（棘轮基线：手写行 85.85%、分支 92.70%，余量打印于每次运行）
- Full verification：`.\verify.ps1`
- Localization：`.\scripts\Test-LocalizationContract.ps1`
- Distribution：`.\scripts\Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64`
- Windows installer：`.\scripts\New-WindowsInstaller.ps1`

## Key Repository Rules

- `AGENTS.md`、`PROJECT_CONVENTIONS.md`、`CONTEXT.md` 是主要工程契约
- warnings-as-errors + EnforceCodeStyleInBuild、编译绑定、设计 token（禁裸色值/尺寸）、本地化四语言同步且禁裸 key 字面量
- NuGet 中央版本管理 + committed lock files；CI `RestoreLockedMode=true`，仅 RID 发布还原显式豁免（lock 只固定无 RID 依赖图）
- CHANGELOG_RELEASE.md 只保留当前单版本并面向安装用户；版本横幅必须随 tag 提交（release.yml 硬门禁）
- 发布提交遵循 Conventional Commits；`release.ps1` 负责版本提交、tag 与 push
- 无远程遥测：诊断日志只留本地

## Risk Profile

- Critical：下载完整性、文件系统安全、发布供应链、更新恢复
- High：跨平台行为、测试确定性、UI 线程性能、持久化/迁移、架构边界
- Medium：本地化契约
- Low：一般代码气味
- 信任边界：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、崩溃快照、CI/发布凭据、安装器权限边界

## Previous Audit State

- Last full audit commit：`cffbd4d`（2026-09-09）
- Last audit commit：`66e103a`（本轮 full）
- Open Critical / High / Medium：0 / 0 / 0
- 非阻塞：3 deferred + 1 accepted-risk（AUD-ARCH-003、AUD-MTN-001、AUD-TST-001、AUD-DEP-002）+ 本轮 6 项待处理（AUD-ARCH-005、AUD-MTN-017、AUD-PERF-012、AUD-ARCH-006/007、AUD-SEC-008、AUD-DEP-010）+ 1 项产品决策（AUD-DEP-009）
