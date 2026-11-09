# 开发指南

本指南说明如何准备环境、运行验证并交付 Cafe Launcher。实现约束见 [PROJECT_CONVENTIONS.md](../PROJECT_CONVENTIONS.md)，产品行为见 [CONTEXT.md](../CONTEXT.md)，全部文档入口见[文档导航](README.md)。

## 环境与启动

Windows 是支持平台，macOS 和 Linux 构建为实验性。先安装 [global.json](../global.json) 指定的 .NET SDK 和 PowerShell 7；SDK 的 `latestFeature` 策略允许在同一 major/minor 内使用较新的 feature band。工具链版本见[规范 §12](../PROJECT_CONVENTIONS.md#12-工具链与依赖)。

所有仓库脚本都使用 `pwsh`，从 Bash 调用时也一样。Windows PowerShell 5.1 无法解析脚本中的 `??`，也可能错误解码中文输出。脚本关闭 .NET、MTP 与 Avalonia 遥测；构建启用可空引用、编译绑定和警告视为错误。

在仓库根目录运行：

```powershell
pwsh ./build.ps1
dotnet run --project ./src/Cafe.Launcher/Cafe.Launcher.csproj
```

`build.ps1` 先还原依赖，再以 Debug 构建宿主引用图。它不构建独立更新助手；修改助手时补充：

```powershell
dotnet build ./src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj
```

Windows x64 首次构建还需要 curl 与 7-Zip，见下方[视频背景运行库与专项验证](#视频背景运行库与专项验证)。

## 命令与验证

先运行受影响的检查，再运行完整验证。请求评审前执行：

```powershell
pwsh ./verify.ps1
```

验证顺序为：本地化契约 → Debug 构建 → 两套测试与覆盖率 → 当前平台 RID 还原及 Release 构建 → Release 资源契约。成功构建必须零警告；交付时记录通过项、失败项和未覆盖的平台或场景。

### 按改动类型选择检查

| 改动 | 先运行 |
| --- | --- |
| XAML、样式或控件模板 | `UiStyleContractTests`；较大 UI 改动运行 `pwsh ./dev.ps1 ui` |
| 有意改变视觉 | `pwsh ./test.ps1 -UpdateGolden`，审阅并提交 `tests/Cafe.Launcher.HeadlessTests/Baselines/` 中的 PNG |
| 资源键、占位符或翻译 | `pwsh ./scripts/Test-LocalizationContract.ps1`，并按[规范 §6](../PROJECT_CONVENTIONS.md#6-本地化)运行统一资源目录生成器 |
| 依赖版本或锁文件 | 按[依赖与 CI](#依赖与-ci)执行，并运行 `InstallerContractTests` |
| 独立更新助手 | `dotnet build ./src/Cafe.Launcher.Updater/Cafe.Launcher.Updater.csproj` |
| 播放器生命周期 | 按[视频背景运行库与专项验证](#视频背景运行库与专项验证)执行生产场景复测 |
| 其他行为变更 | `pwsh ./test.ps1 -Suite Unit -FilterClass '*<Target>Tests'` 等聚焦检查 |
| 交付前 | `pwsh ./verify.ps1` |

### 常用命令

| 目的 | 命令 |
| --- | --- |
| 运行 Unit 与 Headless 两套测试 | `pwsh ./test.ps1` |
| 只运行一个套件 | `pwsh ./test.ps1 -Suite Unit` 或 `-Suite Headless` |
| 按类或方法筛选 | `pwsh ./test.ps1 -Suite Unit -FilterClass '*VersionComparerTests'`；方法用 `-FilterMethod` |
| 使用发布 CI 配置 | `pwsh ./test.ps1 -Configuration Release` |
| 检查 UI 样式并运行 Headless | `pwsh ./dev.ps1 ui` |
| 检查四语言键与占位符 | `pwsh ./scripts/Test-LocalizationContract.ps1` |
| 运行覆盖率门禁 | `pwsh ./coverage.ps1` |
| 再生 Windows 黄金截图 | `pwsh ./test.ps1 -UpdateGolden` |
| 打包发行物 | `pwsh ./scripts/Build-Distribution.ps1 -Rids win-x64,osx-arm64,linux-x64` |
| 用 Windows 发布目录制作安装器 | `pwsh ./scripts/New-WindowsInstaller.ps1` |
| 从宿主图标源再生平台图标 | `pwsh ./scripts/New-AppIconAssets.ps1` |

`test.ps1` 使用原生 Microsoft.Testing.Platform，保留五分钟无活动挂起保护，空筛选返回失败。类名和方法名筛选支持首尾 `*`。直接调用时使用 `dotnet test --project <csproj>` 与 `--filter-class`，并保留挂起保护参数；不用 VSTest 筛选参数。运行带 `--no-restore` 的命令前先还原依赖。

覆盖率同时守住 line/branch 至少 50% 的底线与 `coverage.ps1` 中的较高基线；具体阈值以脚本为准。测试设施见[规范 §8](../PROJECT_CONVENTIONS.md#8-测试)。

XAML 或样式变更先运行 `UiStyleContractTests`，较大 UI 改动运行 `dev.ps1 ui`。只有有意改变视觉时才使用 `-UpdateGolden`；该参数不能与筛选或 `-Suite Unit` 混用。Headless 和黄金截图在 Windows 验证，Linux CI 只运行 Unit。`dotnet format` 按需用于本次触及的文件，不是 CI 门禁。

## 视频背景运行库与专项验证

Windows x64 构建自动调用 [Restore-VideoBackgroundRuntime.ps1](../scripts/Restore-VideoBackgroundRuntime.ps1)。脚本需要 curl 与 `7z` 或 `7zz`，首次从 GitHub 获取固定 LGPL 构建，缓存在 `.artifacts/native/libmpv`，分别校验压缩包和 DLL 的 SHA-256。需要代理时可先手动调用恢复脚本并传入 `-Proxy`。应用运行时不下载播放器。

升级运行库时，同步 [manifest.json](../native/libmpv/manifest.json)、许可证、来源说明并完成原生冒烟验证。发布输出应包含 `libmpv/win-x64/libmpv-2.dll`、清单、来源和许可证；产品播放与缩略图规则见[领域契约](../CONTEXT.md#设置与操作)，其他平台使用静态回退。

[Build-Distribution.ps1](../scripts/Build-Distribution.ps1) 按目标 RID 显式传递 `BackgroundVideoRid`，避免引用工程清除 `RuntimeIdentifier` 后误选构建机资源。Windows ZIP 和安装器打包前都运行：

```powershell
pwsh ./scripts/Test-WindowsPublishOutput.ps1 -PublishDir <发布目录>
```

它检查主程序、更新助手、播放器、清单和许可证，缺失或哈希不符即失败。`native/libmpv/COPYING*` 固定使用 LF，保证跨平台校验同一字节序列。Debug 通常使用机器上已安装的 .NET，Release 自包含输出还带有运行时 DLL，不能用目录文件数量直接比较两者。

涉及播放器生命周期时执行生产场景复测：

```powershell
pwsh ./scripts/Test-VideoBackgroundPrototypeRetention.ps1 -ProbeMode Production -ExerciseSettings -Cycles 8 -SteadySeconds 60
```

要求与实验边界见[视频背景验证记录](research/video-background-prototype.md#复测要求)，当前尚未关闭的风险以[审计台账](../CODEBASE_AUDIT.md)为准。

## 依赖与 CI

NuGet 版本集中维护在 [Directory.Packages.props](../Directory.Packages.props)，工程文件不单独写 `Version`；Headless xUnit 的 `VersionOverride` 是已记录例外。CI 使用 `RestoreLockedMode`，升级时按顺序完成：

1. 修改中央版本，运行 `dotnet restore`，检查并提交相应 `packages.lock.json`；`NU1004` 表示锁文件未同步。
2. 同步[规范 §12 工具链表](../PROJECT_CONVENTIONS.md#12-工具链与依赖)，运行 `InstallerContractTests` 验证版本一致性。
3. 运行 `pwsh ./scripts/New-ThirdPartyNotices.ps1`，再生全部生产工程的 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)。
4. 完成验证后检查锁文件差异。`verify.ps1` 和各 RID 打包的 restore 会改写 RID 段，只还原确认由本次验证产生的副作用，保留真实升级和原有工作区改动。

CI 使用 `actions/cache/restore` 与 `actions/cache/save`，缓存键取中央包版本文件，保存使用 `if: always()`；关闭 `actions/setup-dotnet` 的重复缓存。测试与覆盖率报告保留 7 天，发布中转产物保留 1 天；实际作业与权限以 [.github/workflows/](../.github/workflows/) 为准。

## 提交与评审

提交使用 Conventional Commits，描述和正文可以是中文：

```text
fix(settings): 修复切换语言后法律信息不显示
```

保持提交聚焦。Dependabot 合并使用 `chore(deps): ...`，便于发布说明分组。功能分支通过 PR 评审，说明实际问题、最终行为、关联 issue 与验证结果；可见 UI 变更附截图。CI 通过与评审批准是团队约定，远端分支保护以仓库设置为准。

提交问题前先搜索 [Issues](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia/issues)，提供版本、平台和复现步骤；用户排障步骤由[反馈指南](https://docs.bluearchive.cafe/cafe-launcher/feedback)维护。

## 发布

只有明确发布时运行 `release.ps1 <semver>`。该脚本会修改版本、生成发布说明、提交、打标签并推送。预发布修复递增预发布号，例如 `1.1.0-beta.7` → `1.1.0-beta.8`。

[CHANGELOG_RELEASE.md](../CHANGELOG_RELEASE.md) 只维护本次版本。正文面向安装使用者，使用已出现在界面上的术语描述可见变化；审计、测试、CI 和架构细节放入提交或末尾唯一的 `<details>` 技术附录。保留 `> [!NOTE]` 与 `> [!WARNING]`。应用内查看器忽略原始 HTML 块，因此附录外仍须有普通说明句。

发布横幅以成品 PNG 交付，放在 `docs/assets/release-banners/<tag>.png`；历史横幅保留原名。画布为 16:9，宽 1920–2400 像素；在发布说明中引用对应标签下的原始图片地址。提交前检查文字、清晰度和裁切，并运行 `ReleaseChangelogContractTests` 与 `ReleaseBannerContractTests`。

## 文档职责

[文档导航](README.md)负责查找入口。每条规则在所属文档维护，其他文档引用该来源。

| 文档 | 维护内容 |
| --- | --- |
| [中文 README](../README.md) / [English README](../README.en.md) | 项目介绍、平台、下载与使用/开发入口 |
| [AGENTS.md](../AGENTS.md) | 代理任务路由与工作循环 |
| 本指南 | 环境、命令、验证、依赖、CI、提交与发布流程 |
| [PROJECT_CONVENTIONS.md](../PROJECT_CONVENTIONS.md) | 实现边界与编码、本地化、DI、持久化及测试规范 |
| [CONTEXT.md](../CONTEXT.md) | 领域概念和必须保持的行为契约 |
| [UBIQUITOUS_LANGUAGE.md](../UBIQUITOUS_LANGUAGE.md) | 界面术语、四语言译名和翻译规则 |
| [设计规范](design/design-system-spec.md) | 现行设计规格、实施现状及决策记录 |
| [设计资料索引](design/README.md) | 现行规范、待执行计划与归档记录的入口和状态 |
| [ADR 索引](design/adr/README.md) | 决策与替代关系；正文保留当时背景 |
| [CODEBASE_AUDIT.md](../CODEBASE_AUDIT.md) | 当前审计发现、已接受风险及其维护规则 |
| [研究记录索引](research/README.md) | 调查、选型与比对记录的主题和结论时点 |
| [原型索引](../prototypes/README.md) | 保留的视觉与交互实验及对应决策 |
| [隐私政策](../PRIVACY.md) | 信息处理、网络访问、本地保留和用户选择 |

修改架构或开发流程时，同步权威文档与 `AGENTS.md` 的路由。相对链接须通过 `DocumentationLinkContractTests`；它不检查标题锚点，重命名标题还要检查入链。历史 ADR 与研究记录保留事实时间，路径变化时修复引用。第三方声明由脚本生成，发布说明随发布维护，审计台账随实际审计维护。

### 配套用户文档站

用户文档源码位于 [bluearchive-cafe/docs](https://github.com/bluearchive-cafe/docs)，本地配套工作区为相邻的 `cafe-docs` 仓库。用户可见行为变更时，同步该仓库的 `docs/cafe-launcher/` 与 `docs/en/cafe-launcher/`，包括旧入口 `user-guide.md`。

以当前 XAML 和 ViewModel 核对入口、分组与条件显示，以设置模型核对默认值，以发布工作流核对发行包。中英文保持同一信息范围，保留对应章节锚点与旧入链；截图与当前版本不一致时明确标注。开发实现细节留在本仓库。

在文档站仓库运行：

```powershell
npm ci
npm run docs:build
```

构建后检查中英文内部链接和锚点、语言切换与相关页面。修改内容或同步工作区不等于发布；推送后才由该仓库部署工作流上线。
