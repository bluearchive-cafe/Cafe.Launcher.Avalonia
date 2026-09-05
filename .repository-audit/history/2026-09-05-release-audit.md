# 2026-09-05 Release 审计报告（v1.1.0-beta.6 发布就绪性）

## Audit Metadata

- 审计日期：2026-09-05
- Commit：27a62f2（工作树干净）
- 模式：release（desktop-launcher 风险档案）
- 前基线：259e217（2026-09-05 delta 复审；其报告已随 27a62f2 归档）
- 范围：`v1.1.0-beta.5..27a62f2`（57 提交，240 文件，+25262/−11832），聚焦构建可复现性、依赖锁定、发布供应链、打包与产物完整性、关键测试、版本/发布说明一致性
- 项目档案：跨平台桌面启动器（Windows 正式支持；macOS/Linux 实验性）；分发模型为 GitHub Releases 双仓库（源仓库 + bluearchive-cafe/Cafe.Launcher.Avalonia_Release），tag 推送触发 release.yml

## Executive Summary

**结论：尚不适合打 tag 发布。存在 3 项发布前必须处理的阻塞项（两项资产/文档类小改动 + 一项 CI 稳定性问题），全部处理完成后即可发布。代码本体质量与发布基础设施均处于可发布状态。**

Open findings：
- Critical：0
- High：2（AUD-REL-001 发布横幅缺失；AUD-REL-003 main CI 红灯 flake）
- Medium：1（AUD-REL-002 发布说明缺漏）

Resolved since previous audit：
- AUD-UI-001（自定义壁纸同路径换内容误跳）→ `ff59fd9`，含 2 个行为测试
- AUD-UI-002（横幅解码无驻留钳制）→ `6e54a9a`，含 3 个无头测试

Most important risks/actions：
1. 补齐 `docs/assets/release-banners/cafe-launcher-v1.1.0-beta.6-release-banner.png`（release.yml 在 tag 推送时硬性要求，缺失则发布流程在该步失败）。
2. 处理 main CI 红灯：向导步进动画无头测试在 CI 环境「未观察到淡入中间帧」。本地 3/3 通过（含 coverage 插桩高负载场景），测试所涉应用代码自上个绿色 CI 以来未变更，判定为 CI 2 核慢机的时序 flake（e1b0929 修复后的残留同类）。建议加固采样断言（预算内重采样/重试）后确认 CI 绿灯，再打 tag。
3. 更新 CHANGELOG_RELEASE.md，补入发布说明准备（9676005）之后落地的用户可见变更（见 AUD-REL-002）。

## 本机发布门禁执行记录（verify.ps1 等价序列，全部通过）

后台任务环境中 powershell.exe 无法执行脚本（执行环境问题，非仓库问题），改为前台逐步执行 verify.ps1 的全部组成部分，证据如下：

| 步骤 | 结果 | 证据 |
|---|---|---|
| `Test-LocalizationContract.ps1` | 通过（exit 0；该脚本成功时静默） | 裸 key 守卫 + 资源键/占位符合约 |
| `build.ps1`（Debug） | 通过 | 0 错误；仅既有 AVLN3001 警告（beta.5 前 CI 已存在，见下） |
| `coverage.ps1` | 通过 | 单元 1429/1429 + Headless **164/164**（含 CI 失败的那个动画测试）；手写代码行 85.83% / 分支 92.41%，均高于棘轮基线（84.30%/88.99%） |
| `dotnet restore -r win-x64` | 通过 | lock 文件按文档预期写入 RID 段，已按 AGENTS.md `git restore` 还原 |
| `dotnet build -c Release` | 通过 | 0 错误 |
| Release ResxResourceContractTests | 通过 | 18/18 |

CI 交叉验证（远端，HEAD 27a62f2）：restore/构建/本地化合约/单元 1429/1429 全部通过；唯一红灯为 headless 动画测试 1 例（AUD-REL-003）。

## 发布要素逐项核验（release 模式）

### 通过（正面，逐项实证）

- **版本一致性防呆**：csproj `VersionPrefix` = `1.1.0-beta.6` = CHANGELOG 标题；`Read-LauncherVersion.ps1` 强制 tag 与 VersionPrefix **精确字符串一致**（`-cne`），错 tag 直接 throw——版本漂移在打包第一步即失败。
- **CHANGELOG_RELEASE.md 单一版本节**：`rg "^## v"` 恰好 1 个标题，符合仓库规则。
- **打包脚本零漂移**：`scripts/Build-Distribution.ps1`、`New-WindowsInstaller.ps1`、图标资产自 beta.5 以来零改动——本次打包路径与已成功产出 beta.1–beta.5 的代码完全一致。仅 `Test-LocalizationContract.ps1` 有改动（守卫脚本，不在打包路径）。
- **发布供应链**（在 delta 复审基础上复核 release.yml 全文）：全部 uses 为 40 位 commit SHA；build/installer job 显式 `ContinuousIntegrationBuild` + `RestoreLockedMode`（RID 发布步骤显式豁免并注明理由）；AppImage 工具 SHA256 双重固定；Inno Setup 7 下载经 `gh release verify-asset` 证明校验；workflow 级 `contents: read`、仅 release job 提升 `contents: write`；`ref_name` 全部经 env 传参；`prerelease: contains(ref_name, '-')` 对 beta 自动判定正确；`fail_on_unmatched_files: true` 防产物缺失静默；安装包经 deb 解包 + dpkg 安装 + AppImage xvfb 冒烟测试后才允许发布。
- **发布说明管线**：tag 推送优先使用仓库维护的 CHANGELOG_RELEASE.md（无需手工贴附）；分发仓库版本追加分仓库下载链接（与 `LauncherConstants.GitHubReleaseRepositorySlug` 注明同步）。
- **工作树状态**：干净；无未提交变更；HEAD 即 delta 复审整改后的最新提交。
- **既有台账无发布阻塞项**：open/deferred/accepted-risk 发现全部为 Low 或有意暂缓项（DispatcherTimer 注入、IModalPresenter 查表、THIRD-PARTY-NOTICES 两行、Shirasagi 年度重审、T3 限速断言），均不影响发布。
- **AVLN3001（MainWindow 无真无参公共构造，运行时 XAML 加载器不可达）**：在 beta.5 时点附近最后一次绿色 CI（7e288be，run 33765512046）日志中已出现 5 次——**既有警告，非本周期引入**，不影响构建与运行。**当日已修复（工作树待提交）**：MainWindow 增加显式无参公共构造（委托注入构造），注入构造参数改为必选消除可选参数隐式注入；并在 csproj 增加 `MSBuildWarningsAsErrors=AVLN3001` 守卫（经实验证实：修复前该设置使构建失败、修复后 0 警告 0 错误）。台账 AUD-BLD-002。

### 发现（发布阻塞项）

### AUD-REL-001 — v1.1.0-beta.6 发布横幅未提交，tag 推送后 release.yml 将在该步失败

- Category：release/packaging
- Severity：High（就绪性）；Confidence：100
- Status：open；Disposition：Fix
- **Evidence**：`release.yml`「Verify release banner」步骤硬性要求 `docs/assets/release-banners/cafe-launcher-<tag>-release-banner.png` 存在于源仓库，缺失即 throw；目录现状仅含 beta.1–beta.5 五张横幅，beta.6 缺失（ls 实证）。
- **Impact**：横幅不补，打 tag 后 build job 在该步失败，发布流程整体中断。
- **Recommendation**：按前五个版本的规格制作并提交 beta.6 横幅后再打 tag。
- **Recommendation validation**：Verified（workflow 步骤为直接读码实证）。
- **Suggested guard**：workflow 硬门禁本身即守卫（fail-fast 于 build job），无需再加。

### AUD-REL-002 — CHANGELOG_RELEASE.md 缺少发布说明准备之后落地的用户可见变更

- Category：release/notes
- Severity：Medium；Confidence：95
- Status：open；Disposition：Fix
- **Evidence**：CHANGELOG 于 9676005 准备；其后 40+ 提交落地，其中用户可见且未入说明的有：`e9819ce`（下载安装验证消除重复 CRC64 整读 + slicing-by-8，20GB 级更新的可感知等待时间缩短）、`b76cc9d`（日志查看器加载失败不再伪装成空日志，toast + 诊断日志）、`ff59fd9`（自定义壁纸同路径覆盖内容后不再被「来源未变」守卫误跳）、`85d2266`+`6e54a9a`（横幅解码移出 UI 线程 + 驻留尺寸钳制）。
- **Impact**：发布说明低估本版实际内容；其中下载验证性能是本周期最大的用户可感知改进之一，不写属明显遗漏。
- **Recommendation**：在既有三个小节基础上补一条「下载与安装验证」性能说明，并在日志查看器/背景图小节补上述两项修复；纯文档改动。
- **Recommendation validation**：Verified。
- **Suggested guard**：发版检查单增加一步——打 tag 前用 `git log <上个tag>..HEAD --oneline` 复核 fix/perf/feat 提交是否全部体现在发布说明。

### AUD-REL-003 — main CI 红灯：向导步进动画无头测试在 CI 环境未观察到淡入中间帧

> **【2026-09-05 当日整改】已修复（工作树待提交）**：中间帧观察由轮询采样改为**属性变更推送**（动画逐帧写值同步触发 `PropertyChanged`，不存在采样窗口；实现要点：X 的变更在 `TranslateTransform` 实例上而非面板上，需两个对象都订阅），并对「帧泵极端停顿导致一帧未落入入场窗口」加 3 次重试（锚点步退场后重入场；重试路径经首次实现的失败运行实证）。验证：单测过滤 5/5、全量 Headless 164/164、coverage 插桩高负载 164/164（该 flake 类的历史触发场景）。**CI 绿灯确认留待推送后**——推送后 main Build 必须绿方可打 tag。

- Category：testing/ci-stability
- Severity：High（打 tag 前必须回到绿色）；Confidence：85（flake 归因）
- Status：resolved（整改待提交 + CI 确认）；Disposition：Fix
- **Evidence**：HEAD（27a62f2）CI run 33941222769 失败于 `MainWindowHeadlessTests.SetupWizard_StepSwitchWithMotion_SequentialSwapSettlesOnFinalStep`：「未观察到淡入中间帧，入场透明度疑似瞬变」（`MainWindowHeadlessTests.SetupWizard.cs:357`）。同日本地三次通过（coverage 插桩套件 1 次 + 单测过滤复跑 2 次，各 3s）。该测试所涉向导/动效应用代码自上个绿色全量 CI 以来无变更（beta.5..HEAD 中 SetupWizard 相关仅测试文件 1a7b63a）。症状与 e1b0929 声称修复的「coverage 高负载下漏采中间帧」同类——该修复消除了插桩场景的漏采，未覆盖 CI 2 核慢机的普通 test.ps1 场景。**根因**：入场为前载 SplineEasing(0,0,0,1)，透明度中间窗口（0.05–0.95）仅占 166.5ms 时长的前 ~63%（约 100ms），轮询采样间隔在 CI 负载下可停顿超过该窗口。
- **Impact**：红 main 上打 tag 违反仓库发布纪律（CI 绿灯是 verify.ps1 之外的事后兜底）；且无法区分「环境 flake」与「尚未理解的真回归」就发布，风险不可接受。
- **Recommendation（已落地）**：属性推送观察 + 3 次有界重试；真回归（瞬变）会连续 3 次失败被确定性拦截，环境抖动则重试通过。
- **Recommendation validation**：Verified（重试与失败聚合路径经失败运行实证；推送观察经 5/5 稳定通过实证）。
- **Suggested guard**：加固后的测试本身即守卫；另在发版检查单中固定「main CI 绿灯」为打 tag 前置条件。

## Changes Since Previous Audit

基线 259e217 → 27a62f2 仅 2 提交：c69a439（verify.ps1 守卫脚本路径修复，已由本次本地化守卫步骤实际通过验证）、27a62f2（审计文档）。**另核销 delta 复审同日整改中的两个 open 发现**：AUD-UI-001 → ff59fd9（来源 key 并入内容指纹 + 2 个行为测试）、AUD-UI-002 → 6e54a9a（新增 BannerImageDecoder 驻留钳制 + 3 个无头测试）。二者均已在本次 verify 全量套件中随 164/164 无头与 1429/1429 单元通过回归验证。

## Resolved Findings

| ID | 修复提交 | 验证方式 |
|---|---|---|
| AUD-UI-001 | ff59fd9 | 本次 verify 全量套件 + 行为测试入账 |
| AUD-UI-002 | 6e54a9a | 本次 verify 全量套件 + 无头测试入账 |

## Verified Strengths（发布视角）

- 版本/产物/发布说明三者的强制一致性设计（VersionPrefix 精确匹配、fail_on_unmatched_files、CHANGELOG 单节规则）在同规模开源项目中少见地完整。
- 发布 job 的供应链姿态：SHA 固定 + 资产证明校验（verify-asset）+ SHA256 固定工具 + 最小权限提升，与 2026-09-04 审计后的整改完全一致并经本次复核维持。
- 打包路径连续五个 beta 零漂移，本次发布使用的是被反复验证过的同一条路径。

## Recommended Priorities

1. **发版当天依次**：补 beta.6 横幅 → 更新 CHANGELOG_RELEASE.md → 处理动画测试 flake 并确认 CI 绿 → 打 tag `v1.1.0-beta.6`。
2. **发布后**：按 CODEBASE_AUDIT.md 既有路线图继续 deferred 项（不阻塞发布）。

## Audit Method and Limitations

- release 模式聚焦发布链路；架构/性能/可维护性维度沿用 2026-09-04 全维审计与 2026-09-05 delta 复审结论，未重复深查。
- verify.ps1 因本会话后台任务环境无法执行 PowerShell 而改为前台逐步执行全部组件（步骤与脚本一致，证据见上表）；未直接单次运行 verify.ps1 本体。
- CI 结论取自远端 run 日志（`gh run view`）；未在 CI 环境直接复现动画 flake，flake 归因基于本地复跑 + 代码未变证据。
- 未核验 `RELEASE_REPOSITORY_TOKEN` 的实际权限形态（fine-grained 与否）——属分发仓库配置，仓库内无证据，维持 2026-09-04 审计建议（评估 fine-grained PAT）。
