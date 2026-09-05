# Repository Map

- 审计日期：2026-09-04（全维度复审时更新）
- HEAD：4ed11c3 `docs(audit): 记录 P0-P3 整改状态与验收结果`

## Language / Framework

- 语言：C#（.NET 10，`net10.0`），XAML（Avalonia 12.1.1）
- 框架：Avalonia UI + CommunityToolkit.Mvvm（MVVM）
- 规模：src 201 个 .cs 文件（不含 Designer）/ 约 29.8k 行 + 25 个 .axaml；tests 157 个 .cs 文件 / 约 37.2k 行，1082 个 `[Fact]/[Theory]`

## Architecture

- MVVM + 垂直分 Feature 切片：
  - 应用项目：`src/Cafe.Launcher.Avalonia/`
  - 组合根：`Composition/ServiceConfiguration.cs`（Microsoft.Extensions.DependencyInjection）
  - Features：`Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
  - 共享设施：`Services/`（含 `GameRuntime/`、`Diagnostics/`）、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、`ViewModels/`、`Views/`
- 入口：`Program.cs` → `App.axaml(.cs)`

## Tests

- `tests/Cafe.Launcher.Avalonia.Tests`（xUnit v3 单元测试，920 个 Fact/Theory）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests`（Avalonia.Headless.XUnit UI 测试，约 142 个 AvaloniaFact/Theory）
- 共享隔离：`tests/TestUserDataIsolation.cs`（ModuleInitializer 重定向用户数据目录，链接进两个项目）
- 质量门禁：
  - `coverage.ps1`：手写 C# 行/分支覆盖率合并 unit+headless；阈值 50%，棘轮基线行 84.30% / 分支 88.99%，禁止下探
  - 两个测试程序集均 `DisableTestParallelization = true`（全局串行，静态状态隔离的基石）
  - 不使用 mocking 框架，全部手写 stub/fake（PROJECT_CONVENTIONS.md 规定）
- 脚本：`build.ps1` / `test.ps1` / `coverage.ps1` / `verify.ps1` / `dev.ps1 ui`
- Golden 截图：`HeadlessTests/Baselines/` 提交 5 张基线 PNG，容差每通道 8 / 失配比 1%，`CAFE_GOLDEN_UPDATE=1` 手工再生成

## Packaging / CI

- CI：`.github/workflows/build.yml` 仅 `windows-latest`（timeout 40 分钟），运行 `test.ps1` + `coverage.ps1` 后发布多 RID 归档
- 发布：`scripts/Build-Distribution.ps1`（自包含归档，多 RID）
- 安装器：`scripts/New-WindowsInstaller.ps1`（Inno Setup 6.3+）
- 图标资产：`scripts/New-AppIconAssets.ps1`

## Repository Rules（规则优先级从高到低）

1. `PROJECT_CONVENTIONS.md` — 强制性 AI 编码规范（测试保护、零警告、向后兼容 settings.json、XAML 设计 token、本地化契约、无远程遥测、不使用 mocking 框架）
2. `AGENTS.md` — 代理工作流权威来源（构建/测试命令、目录约定、本地化流程、Conventional Commits）
3. `CLAUDE.md`、`CONTEXT.md`（领域统一语言 + 设计系统决策）、`UBIQUITOUS_LANGUAGE.md`、`docs/`（含 design/adr）
