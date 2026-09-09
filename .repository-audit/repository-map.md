# Repository Map

- 审计日期：2026-09-09（full 全量审计，自 `cffbd4d` 起独立重读源码）
- HEAD：cffbd4d `fix(diagnostics): 崩溃报告容忍快照缺失 UI 文化并补黄金基线契约守卫`
- 发布状态：v1.1.0-beta.7 仍为最新 tag（2026-09-07 15:02Z，指向 6bd2a8f）；其后 4 提交为审计整改与崩溃诊断特性，无版本变更

## Language / Framework

- 语言：C#（.NET 10，`net10.0`），XAML（Avalonia 12.1.2）
- 框架：Avalonia UI + CommunityToolkit.Mvvm（MVVM）
- 规模：src 211 个 .cs 文件（不含 Designer/obj/bin）/ 30,267 行 + 27 个 .axaml；tests 单元 1486 例（本机 1484 过 + 2 跳，CI 1486 过）/ Headless 167 例（2026-09-09 本机与 CI 实跑）

## Architecture

- MVVM + 垂直分 Feature 切片：
  - 应用项目：`src/Cafe.Launcher.Avalonia/`
  - 组合根：`Composition/ServiceConfiguration.cs`（Microsoft.Extensions.DependencyInjection）
  - Features：`Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
  - 共享设施：`Services/`（含 `GameRuntime/`、`Diagnostics/`、`Auth/`）、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、`ViewModels/`、`Views/`
- 入口：`Program.cs` → `App.axaml(.cs)`；隔离崩溃报告模式另有最小入口 `CrashReportApp.axaml(.cs)`（`--crash-report [path]`，绕过单实例互斥与 DI）
- 崩溃诊断：`Services/Diagnostics/`（`FatalCrashService` / `CrashReportStore` / `CrashReportBootstrap` / `CrashReporterLauncher` / `CrashOrigin` / `CrashReport`）+ 根 `ViewModels/CrashReportWindowViewModel.cs` 与 `Views/CrashReportWindow.axaml`（ADR-019/020）
- 游戏运行时：`Services/GameRuntime/`（runner 定义 `Native`/`Umu`/`Wine`、可用性探测、进程跟踪）；平台矩阵为 Windows=native、Linux=umu/wine、**macOS 无 runner**（见 AUD-XPLAT-001）

## Tests

- `tests/Cafe.Launcher.Avalonia.Tests`（xUnit v3 单元测试，1486 例含 2 跳）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests`（Avalonia.Headless.XUnit UI 测试，167 例）
- 共享隔离：`tests/TestUserDataIsolation.cs`（ModuleInitializer 重定向用户数据目录，链接进两个项目）；共享测试替身 `tests/TestDoubles/`（含 `StubFatalCrashService`、`StubGameOperationExecutor`，链接进两个项目）
- 质量门禁：
  - `coverage.ps1`：手写 C# 行/分支覆盖率合并 unit+headless；阈值 50%，棘轮基线行 84.30% / 分支 88.99%，禁止下探（2026-09-09 full 实跑 行 85.25% / 分支 92.12%）
  - 两个测试程序集均 `DisableTestParallelization = true`（全局串行，静态状态隔离的基石）
  - 不使用 mocking 框架，全部手写 stub/fake（PROJECT_CONVENTIONS.md 规定）
- 脚本：`build.ps1` / `test.ps1` / `coverage.ps1` / `verify.ps1` / `dev.ps1 ui`
- Golden 截图：`HeadlessTests/Baselines/` 提交 6 张基线 PNG，容差每通道 8 / 失配比 1%，`CAFE_GOLDEN_UPDATE=1` 手工再生成；非 Windows 自动跳过

## Packaging / CI

- CI：`.github/workflows/build.yml` 仅 `windows-latest`（timeout 40 分钟），运行 `Test-LocalizationContract.ps1` + `test.ps1` + `coverage.ps1` 后发布多 RID 归档；顶层 `permissions: contents: read`，17 处 `uses:` 全部 40 位 SHA 固定
- 发布：`scripts/Build-Distribution.ps1`（自包含归档，多 RID）；`.github/workflows/release.yml` 在 ubuntu-24.04 构建 Linux/macOS 包并 smoke test AppImage，Windows job 构建 Inno 安装器（issrc 固定版 + `gh release verify-asset`）
- 安装器：`scripts/New-WindowsInstaller.ps1`（Inno Setup 7.0+，脚本硬性校验；README/AGENTS/CLAUDE/PROJECT_CONVENTIONS 由 `InstallerContractTests` 守卫）
- 图标资产：`scripts/New-AppIconAssets.ps1`

## Repository Rules（规则优先级从高到低）

1. `PROJECT_CONVENTIONS.md` — 强制性 AI 编码规范（测试保护、零警告、向后兼容 settings.json、XAML 设计 token、本地化契约、无远程遥测、不使用 mocking 框架）
2. `AGENTS.md` — 代理工作流权威来源（构建/测试命令、目录约定、本地化流程、Conventional Commits）
3. `CLAUDE.md`、`CONTEXT.md`（领域统一语言 + 设计系统决策）、`UBIQUITOUS_LANGUAGE.md`、`docs/`（含 design/adr，ADR-019/020 为崩溃窗口决策）

## Risk Profile

- 项目类型：跨平台桌面启动器 / 更新器（win-x64、osx-arm64、linux-x64，自包含发布）
- 权重（desktop-launcher 基线）：download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical；cross_platform_behavior / testing_determinism / ui_thread_performance / persistence_and_migration / architecture_boundaries = high；localization_contracts = medium；generic_code_smells = low
- 持久用户数据：`%LOCALAPPDATA%\Cafe Launcher\`（settings.json、download_state.json、unified.log、CrashReports/）；游戏目录内 manifest.json + game-launcher-config.json + clickCode
- 信任边界：Yostar 官方 API/CDN（https）、Cafe CDN/资源面板 API、GitHub 更新源、崩溃快照文件、CI/发布凭据

## Previous Audit State

- Last full audit commit: f6d44a7（2026-09-09，同日修复 d9f2184 / 78c7d67）
- Last audit commit: cffbd4d（本轮基线；上轮报告未覆盖其后的修复提交）
- Open High/Critical findings: 0
