# Repository Map

- 审计日期：2026-09-09（full 全量审计）
- HEAD：f6d44a7 `feat(diagnostics): 不可恢复崩溃窗口与两级兜底`
- 发布状态：v1.1.0-beta.7 仍为最新 tag（2026-09-07 15:02Z，指向 6bd2a8f）；其后 3 提交为审计整改与崩溃诊断特性，无版本变更

## Language / Framework

- 语言：C#（.NET 10，`net10.0`），XAML（Avalonia 12.1.2）
- 框架：Avalonia UI + CommunityToolkit.Mvvm（MVVM）
- 规模：src 210 个 .cs 文件（不含 Designer/obj/bin）/ 约 30.2k 行 + 27 个 .axaml；tests 单元 1476（1474 过 + 2 跳）+ Headless 166（2026-09-09 full 实跑）

## Architecture

- MVVM + 垂直分 Feature 切片：
  - 应用项目：`src/Cafe.Launcher.Avalonia/`
  - 组合根：`Composition/ServiceConfiguration.cs`（Microsoft.Extensions.DependencyInjection）
  - Features：`Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
  - 共享设施：`Services/`（含 `GameRuntime/`、`Diagnostics/`）、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、`ViewModels/`、`Views/`
- 入口：`Program.cs` → `App.axaml(.cs)`；隔离崩溃报告模式另有最小入口 `CrashReportApp.axaml(.cs)`（`--crash-report [path]`，绕过单实例互斥与 DI）
- 崩溃诊断：`Services/Diagnostics/`（`FatalCrashService` / `CrashReportStore` / `CrashReporterLauncher` / `CrashOrigin` / `CrashReport`）+ 根 `ViewModels/CrashReportWindowViewModel.cs` 与 `Views/CrashReportWindow.axaml`（ADR-019/020）

## Tests

- `tests/Cafe.Launcher.Avalonia.Tests`（xUnit v3 单元测试，1476 个用例含 2 跳）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests`（Avalonia.Headless.XUnit UI 测试，166 个用例）
- 共享隔离：`tests/TestUserDataIsolation.cs`（ModuleInitializer 重定向用户数据目录，链接进两个项目）；共享测试替身 `tests/TestDoubles/`（含 `StubFatalCrashService`，链接进两个项目）
- 质量门禁：
  - `coverage.ps1`：手写 C# 行/分支覆盖率合并 unit+headless；阈值 50%，棘轮基线行 84.30% / 分支 88.99%，禁止下探（2026-09-09 full 实跑 行 85.02% / 分支 92.04%）
  - 两个测试程序集均 `DisableTestParallelization = true`（全局串行，静态状态隔离的基石）
  - 不使用 mocking 框架，全部手写 stub/fake（PROJECT_CONVENTIONS.md 规定）
- 脚本：`build.ps1` / `test.ps1` / `coverage.ps1` / `verify.ps1` / `dev.ps1 ui`
- Golden 截图：`HeadlessTests/Baselines/` 提交 6 张基线 PNG（新增 `crash-report-window.png`），容差每通道 8 / 失配比 1%，`CAFE_GOLDEN_UPDATE=1` 手工再生成；非 Windows 自动跳过

## Packaging / CI

- CI：`.github/workflows/build.yml` 仅 `windows-latest`（timeout 40 分钟），运行 `test.ps1` + `coverage.ps1` 后发布多 RID 归档；顶层 `permissions: contents: read`，17 处 `uses:` 全部 40 位 SHA 固定
- 发布：`scripts/Build-Distribution.ps1`（自包含归档，多 RID）
- 安装器：`scripts/New-WindowsInstaller.ps1`（Inno Setup 7.0+，脚本硬性校验；README/AGENTS/CLAUDE/PROJECT_CONVENTIONS 由 `InstallerContractTests` 守卫）
- 图标资产：`scripts/New-AppIconAssets.ps1`

## Repository Rules（规则优先级从高到低）

1. `PROJECT_CONVENTIONS.md` — 强制性 AI 编码规范（测试保护、零警告、向后兼容 settings.json、XAML 设计 token、本地化契约、无远程遥测、不使用 mocking 框架）
2. `AGENTS.md` — 代理工作流权威来源（构建/测试命令、目录约定、本地化流程、Conventional Commits）
3. `CLAUDE.md`、`CONTEXT.md`（领域统一语言 + 设计系统决策）、`UBIQUITOUS_LANGUAGE.md`、`docs/`（含 design/adr，ADR-019/020 为崩溃窗口决策）
