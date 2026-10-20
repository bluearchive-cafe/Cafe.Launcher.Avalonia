# 开发指南

本文件维护开发命令、验证和交付流程。编码规则见 [PROJECT_CONVENTIONS.md](../PROJECT_CONVENTIONS.md)，代理入口见 [AGENTS.md](../AGENTS.md)。

## 环境与启动

- Windows 为支持平台；macOS、Linux 为实验性构建。
- 安装 [global.json](../global.json) 指定的 .NET SDK；`latestFeature` 允许在同一 major/minor 内向较新 feature band 滚动。工具链版本表见[规范 §12](../PROJECT_CONVENTIONS.md#12-工具链与依赖)。
- 仓库脚本使用 PowerShell 7（`pwsh`）；Windows PowerShell 5.1 无法解析 `??`，且会以 ANSI 解码中文诊断。Bash 中也通过 `pwsh` 调用脚本。
- 仓库脚本关闭 .NET、MTP 和 Avalonia 遥测；构建启用可空引用、编译绑定和警告视为错误。

在仓库根目录执行：

```powershell
pwsh ./build.ps1
dotnet run --project ./src/Cafe.Launcher/Cafe.Launcher.csproj
```

`build.ps1` 先 restore，再构建宿主及其引用图；修改独立更新 helper 时另行运行
`dotnet build ./src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj`。

## 命令与验证

| 命令 | 用途 |
| --- | --- |
| `pwsh ./test.ps1` | Debug 下运行 Unit 和 Headless 两套测试 |
| `pwsh ./test.ps1 -Suite Unit` / `-Suite Headless` | 运行单个套件 |
| `pwsh ./test.ps1 -FilterClass '*VersionComparerTests'` | 按类筛选；`-FilterMethod` 按方法筛选，均支持首尾 `*` |
| `pwsh ./test.ps1 -Configuration Release` | 按发布 CI 的配置运行两套测试 |
| `pwsh ./test.ps1 -UpdateGolden` | 再生 Windows 黄金截图；不能与筛选或 `-Suite Unit` 混用 |
| `pwsh ./dev.ps1 ui` | UI 样式契约 + Headless 套件 |
| `pwsh ./scripts/Test-LocalizationContract.ps1` | 检查四语言资源键与格式占位符 |
| `pwsh ./coverage.ps1` | 覆盖率门禁：line/branch 至少 50%，并守住脚本内的基线 |
| `pwsh ./verify.ps1` | 本地化契约 → Debug 构建 → 覆盖率 → Release 构建 → Release 资源契约 |
| `pwsh ./scripts/Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64` | 发布并打包各平台发行包 |
| `pwsh ./scripts/New-WindowsInstaller.ps1` | 从 `artifacts/publish/win-x64` 构建 Windows 安装器 |
| `pwsh ./scripts/New-AppIconAssets.ps1` | 修改宿主 `Assets/app-icon-source.png` 后再生 macOS/Linux 图标 |

优先通过 `test.ps1` 运行测试，它保留五分钟无活动挂起保护（Golden 再生也使用同一保护）。空筛选返回失败；测试使用原生 Microsoft.Testing.Platform，直接调用时使用 `dotnet test --project <csproj>` 和 `--filter-class`，不用 VSTest 参数。

修改后先运行受影响的测试；XAML/样式变更运行 `UiStyleContractTests`，较大 UI 改动运行 `dev.ps1 ui`。有意改变视觉时，用 `-UpdateGolden` 再生并提交 [Baselines](../tests/Cafe.Launcher.HeadlessTests/Baselines/) 下的 PNG。Headless 和黄金截图在 Windows 上验证；Linux CI 只运行 Unit。测试设施与命名规则见[规范 §8](../PROJECT_CONVENTIONS.md#8-测试)。

请求评审前运行 `verify.ps1` 并记录结果。运行带 `--no-restore` 的命令前先 restore；成功构建须为零警告。`dotnet format` 不是 CI 门禁，仅按需用于本次触及的文件。

## 依赖与 CI

版本集中在 [Directory.Packages.props](../Directory.Packages.props)，csproj 不声明 `Version`；Headless 的 xUnit `VersionOverride` 是已记录的例外。CI 使用 `RestoreLockedMode`，升级依赖（含 Dependabot）时：

1. 修改中央版本后运行 `dotnet restore`，将更新的 `packages.lock.json` 一并提交。`NU1004` 表示锁文件尚未同步。
2. 同步[规范 §12 的工具链表](../PROJECT_CONVENTIONS.md#12-工具链与依赖)，由 `InstallerContractTests` 校验。
3. restore 后运行 `pwsh ./scripts/New-ThirdPartyNotices.ps1`，再生 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)；它汇总 `src/` 下全部生产工程。
4. `verify.ps1` 和发行打包的 RID restore 会改写锁文件 RID 段；提交前检查差异，仅还原这些副作用，保留真实升级及原有工作区改动。

CI 使用 `actions/cache/restore` 与 `actions/cache/save`，键仅取 `Directory.Packages.props`，保存设 `if: always()`。组织的 Actions 配额为 500 MB 且已超额，因此保持 `actions/setup-dotnet` 的自动缓存关闭。测试/覆盖率报告保留 7 天，发布中转产物保留 1 天。

## 提交与评审

提交遵循 Conventional Commits：`<type>[optional scope]: <description>`，描述与正文可以使用中文。例如 `fix(settings): 修复切换语言后法律信息不显示`；Dependabot 合并使用 `chore(deps): ...`，以便发布说明正确分组。

保持提交聚焦，在功能分支开发并通过 PR 评审。PR 说明动机、关联 issue 和验证结果，可见 UI 变更附截图。报告问题前搜索已有 [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues)，提供版本、平台和复现步骤。

PR、CI 通过与评审批准是团队约定；仓库记录的 `main` 规则只禁止删除和强制推送（2026-09-11 核对）。合并后手动推送，不把远端规则视为本地验证的替代。

## 发布

仅在明确发布时运行 `release.ps1 <semver>`；脚本负责 csproj 版本、发布说明生成、提交、标签和推送。预发布修复递增预发布号，例如 `1.1.0-beta.7` → `1.1.0-beta.8`。

[CHANGELOG_RELEASE.md](../CHANGELOG_RELEASE.md) 只保留本次版本。发布说明使用已出现在界面上的术语，面向安装使用者描述可见变化；审计、测试、CI 和架构细节放入提交或末尾唯一的 `<details>` 技术附录。保留 `> [!NOTE]` 与 `> [!WARNING]`；附录标签外须有普通说明句，因为应用内查看器忽略原始 HTML 块。

横幅 PNG 必须提交到 `docs/assets/release-banners/`，并有 `docs/promo/specs/` 中的逐版本规格；制作流程见[横幅指南](promo/release-banner-guide.md)。`ReleaseChangelogContractTests` 守护发布说明格式与资产。

## 文档职责

| 文档 | 唯一维护的内容 |
| --- | --- |
| [README](../README.md) / [English README](../README.en.md) | 产品介绍、安装使用及开发入口 |
| [AGENTS.md](../AGENTS.md) | 代理的任务路由与最小工作循环 |
| 本开发指南 | 环境、命令、验证、依赖、CI、提交与发布流程 |
| [PROJECT_CONVENTIONS.md](../PROJECT_CONVENTIONS.md) | 程序集边界、编码、本地化、DI 与测试规范 |
| [CONTEXT.md](../CONTEXT.md) | 领域概念和必须保持的行为契约 |
| [UBIQUITOUS_LANGUAGE.md](../UBIQUITOUS_LANGUAGE.md) | 界面术语、四语言译名和翻译规则 |
| [设计规范](design/design-system-spec.md) | 设计系统规格；决策变更追溯到 [ADR 索引](design/adr/README.md) |
| [CODEBASE_AUDIT.md](../CODEBASE_AUDIT.md) | 当前审计发现与已接受风险，维护规则在该文件末尾 |
| [design/](design/) 与 [research/](research/) 中的计划和研究 | 当时的方案、证据与验证边界；实施前核对当前代码及后续 ADR |

每条规则在所属文档维护，其他入口通过链接引用。架构或开发流程变更时，同步所属文档及 `AGENTS.md` 的入口；ADR 保留当时决策及替代关系，路径变更时修复引用。Markdown 相对链接必须通过 `DocumentationLinkContractTests`；这项测试不校验标题锚点，改标题时也要检查入链。
