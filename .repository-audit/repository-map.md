# Repository Map

- 审计日期：2026-09-02
- HEAD：3a10080 `refactor(game-ops): 统一启动与快捷方式的游戏启动目标解析`

## Language / Framework

- 语言：C#（.NET 10，`net10.0`），XAML（Avalonia 12.1.1）
- 框架：Avalonia UI + CommunityToolkit.Mvvm（MVVM）
- 规模：src 约 207 个 .cs 文件 / 约 27.6k 行；tests 约 129 个 .cs 文件

## Architecture

- MVVM + 垂直分 Feature 切片：
  - 应用项目：`src/Cafe.Launcher.Avalonia/`
  - 组合根：`Composition/ServiceConfiguration.cs`（Microsoft.Extensions.DependencyInjection）
  - Features：`Shell`、`GameOperations`、`Settings`、`SetupWizard`、`Diagnostics`、`ResourcePanel`
  - 共享设施：`Services/`、`Helpers/`、`Models/`、`Constants/`、`Controls/`、`Converters/`、`ViewModels/`、`Views/`
- 入口：`Program.cs` → `App.axaml(.cs)`

## Tests

- `tests/Cafe.Launcher.Avalonia.Tests`（xUnit v3 单元测试）
- `tests/Cafe.Launcher.Avalonia.HeadlessTests`（Avalonia.Headless.XUnit UI 测试）
- 脚本：`build.ps1` / `test.ps1` / `coverage.ps1`（50% 行/分支阈值 + 基线回归）/ `verify.ps1` / `dev.ps1 ui`

## Packaging / CI

- 发布：`scripts/Build-Distribution.ps1`（自包含归档，多 RID）
- 安装器：`scripts/New-WindowsInstaller.ps1`（Inno Setup 6.3+）
- 图标资产：`scripts/New-AppIconAssets.ps1`

## Repository Rules（规则优先级从高到低）

1. `PROJECT_CONVENTIONS.md` — 强制性 AI 编码规范（测试保护、零警告、向后兼容 settings.json、XAML 设计 token、本地化契约、无远程遥测）
2. `AGENTS.md` — 代理工作流权威来源（构建/测试命令、目录约定、本地化流程、Conventional Commits）
3. `CLAUDE.md`、`CONTEXT.md`（领域统一语言 + 设计系统决策）、`UBIQUITOUS_LANGUAGE.md`、`docs/`（含 design/adr）
