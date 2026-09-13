# 仓库审计报告（当前状态）

- 审计日期：2026-09-12
- 审计对象：`490c2e5`（`main`，亦即已发布 tag `v1.1.0-beta.9` 指向的提交）
- 模式：**full 全量审计**（架构、安全、依赖与发布、测试、性能、可维护性六个域）
- 上次全量基线：`66e103a`（2026-09-11 报告，见 `.repository-audit/history/2026-09-11-full-audit.md`）；上次审计提交 `43c17ce`
- 变更集：`43c17ce..490c2e5`，10 提交 / 46 文件 / +2203 −176
- 风险画像：desktop launcher / updater（下载完整性、文件系统安全、发布供应链、更新恢复 = critical）
- 状态文件：`.repository-audit/findings.json`（99 项）、`.repository-audit/repository-map.md`

## 当前结论

**仓库整体健康：0 Critical / 0 High。** 当前开放 **14 项：1 项 Medium、6 项 Low、7 项 Informational**，另有 3 项 deferred、2 项 accepted-risk、1 项 product-decision。本轮审计新发现 7 项、因修复不完整**重新打开 1 项 Low**（AUD-TST-005）；此前 resolved 的发现未发现回归——全部契约守卫随测试套件在 Debug 与 Release 两种配置下通过。

审计结论出具后，按你的裁定落地了一批修复（见下节「按裁定落地的修复」）：**AUD-DEP-009、AUD-ARCH-008、AUD-ARCH-005 已结案**（其中 ARCH-005 取「删除」方案），AUD-DOC-003 按决定结案为已接受风险，其决策记录诉求拆分为 AUD-ARCH-009。台账现为 101 项：**81 resolved / 14 open / 3 deferred / 2 accepted-risk / 1 product-decision**。

最值得注意的一项不是代码缺陷而是**流程盲区**：Release 配置的测试只在打 tag 时运行，本轮发版已实际因此失败一次并被追平（AUD-CI-008）。

本机验证证据（本轮实跑，两条命令均 **exit 0**）：

| 检查 | 结果 |
|---|---|
| `scripts/Test-LocalizationContract.ps1`（`verify.ps1` 首道门禁） | 通过 |
| Debug 构建 | **0 警告 / 0 错误** |
| 单元测试（Debug） | **1622 通过 / 2 跳过 / 0 失败**（1624） |
| Headless UI 测试（Debug） | **176 / 176 通过**（含 7 份黄金基线） |
| 手写代码覆盖率 | 行 **86.25%**（14376/16668）、分支 **93.05%**（2262/2431） |
| 覆盖率棘轮 | 通过，余量 **+0.40pp 行 / +0.35pp 分支**（基线 85.85% / 92.70%） |
| Release 构建（win-x64） | **0 警告 / 0 错误** |
| Release 资源合约测试 | 18 通过 |
| **单元 + Headless 全量（`-Configuration Release`）** | **1622 通过 / 2 跳过 / 0 失败；176 / 176** ——与 Debug 完全一致 |
| **落地修复后复跑 `verify.ps1`** | **exit 0**：单元 **1628 通过 / 2 跳过 / 0 失败**（1630）、Headless 176/176，行 **86.26%** / 分支 **93.03%**，棘轮余量 **+0.41pp / +0.33pp**（覆盖率较上一行为微降，因删除了 `ModalHostViewModel` 中一条被覆盖的属性，仍高于棘轮） |

CI 状态（`gh` 实查）：`main` 的 Build run `34666006924` **success**；tag `v1.1.0-beta.9` 的 Release run `34666008931` **success**，Release 已于 2026-09-12T01:58:30Z 发布。

## 变更集审读（`43c17ce..490c2e5`）

10 个提交里只有 4 个含生产代码变更，均已逐行审读：

1. **`c5fa53a` 一键修复**：`ManifestValidationResult.HasDamagedFiles`（`DamagedFileCount > 0`）+ `GameOperationJourney.StartGameAsync` 失败分支改为打开既有修复确认。**设计面健康**：复用 `IGameOperationJourneyHost.ShowRepairConfirmation` 与既有 `ModalKind.RepairConfirmation`，未新增模态表面，因此没有踩到 AUD-MTN-017 的「约 14 个未守卫编辑点」；`HasDamagedFiles` 的判别前提成立——`DamagedFileCount` 的唯一产出点是 `ManifestValidationService.cs:102`，而 `GameLaunchService.Failed()`（`:154-167`）与状态/路径类失败路径留下的计数器恒为 0，故「损坏」与「状态失败」不会互相误判。确认后的链路复用既有 `ConfirmRepairRequested → RepairAsync`，且 `GameOperationsViewModel.RepairAsync` 对运行时状态有 `Corrupted or Ready` 闸门。
2. **`3e95793` 删除 click code 归因链路**：删除 `ClickCodeService`（91 行）及 DI 注册、App 启动调用、`GameLaunchService` 构造参数与启动前写入、`GamePaths.ClickCodeFileName`、导出日志用户数据清单项；全仓库 grep 无残留引用。**顺带降低了文件系统面**（原先启动前会往游戏目录写文件）。因生产侧从不投放归因码源文件，该链路在任何真实部署里必然空转，删除属净收益。
3. **`85e27d7` 启动后最小化到托盘**：`MainWindow.MinimizeToTray()` 按 `PerformClose` 既有规则（有托盘则隐藏、无托盘退回任务栏），`Operations.MinimizeRequested` 与标题栏的 `WindowChrome.MinimizeRequested` 分开接线，3 条 headless 用例锁住两条路径不被合并。
4. **`f69654d` 下载停止日志**：日志移入 `if (session is not null)` 守卫内并将静态 `LogSync` 改为注入的 `diagnostics.DebugAsync`。已核实 `UnifiedLogger.LogAsync` 方法体内无 `await`（`serilogLogger.Write` 直接入队），故 `GetAwaiter().GetResult()` 不构成 UI 线程死锁，与仓库既有写法一致。

其余 6 个提交为发布准备（`0de2905`）、横幅管线与分析文档（`31a4662`、`b56c6d2`）、图标统一（`8911e67`）、审计台账（`1c78317`）与测试级别修正（`490c2e5`）。

依赖面本轮**无任何变更**（`Directory.Packages.props` 与三份 `packages.lock.json` 在变更集内零改动）。

## 按裁定落地的修复（审计结论出具后）

| ID | 裁定 | 落地内容 | 验证 |
|---|---|---|---|
| AUD-DEP-009 | 「可发布 sha256sums」 | `release.yml` 的 release job 新增「Generate checksum manifest」步骤：清单由 `artifacts/distribution` 实际文件推导（不另抄一份产物清单），数量 ≠ 6 即 `exit 1`，产出的 `SHA256SUMS` 随两个发布目标（源码仓库 + 独立 Release 仓库）一并发布 | 本地模拟：`sha256sum -c SHA256SUMS` 全绿；多放入一个文件时按设计失败。新增 `InstallerContractTests.ReleaseWorkflow_PublishesChecksumManifestForEveryDistributionPackage` 锁住该步骤与两个发布目标 |
| AUD-ARCH-008 | 「根据官方客户端的逻辑进行测试；测试版本号更改引发的行为变化」 | ① `ApiConfig.YostarAuthorizationVersion` 补文档注释（是签名负载的一部分、须在官方升版时复核、服务端拒绝即为此字段被校验的信号）；② `AuthorizationHeaderFactory` 增加 `TimeProvider` 接缝（保留无参构造供 DI），使签名可复现；③ 新增 `AuthorizationHeaderFactoryTests` 5 例，期望值用 openssl/Python hashlib 在本实现之外独立算出；④ `LauncherConstantsTests` 中误导性的 `MatchesOfficialLauncherVersion` 更名为 `RemainsTheValueTheProtocolWasVerifiedAgainst` 并注明其局限 | **两向实测**：调换 `head` 字段声明序 → 5 例中 4 例失败；把常量改为 `1.7.3` → 8 例中 5 例失败（含「仅版本变化即改变签名」用例） |
| AUD-DOC-003 | 「不纳入版本管理，也不添加 gitignore」 | 按决定结案为已接受风险；其决策记录诉求拆分为 AUD-ARCH-009（该诉求不依赖分析文档是否入库） | — |
| AUD-ARCH-005 | 选 (A)：**删除**属性而非绑定 | ① 删除 `ModalHostViewModel.IsDialogLayerInteractive` 及其在 `NotifyStackChanged` 的通知；② `ModalHostViewModelTests` 移除 4 处断言，并把 `InteractionState_WhenNestedModalOpens_OnlyTopLayerIsInteractive` 更名为 `..._WhenNestedDialogOpens_MarksUnderlyingLayersNonInteractive`——去掉对已删属性的断言后，该用例改为断言「对话框在顶时下层主叠层全部让出交互权 + `Top.Kind` 正确」，避免静默丢失原有覆盖；③ `AGENTS.md` 新增**模态隔离条款**、`CONTEXT.md` §模态交互权 补实现分工，共同写明两层机制与「对话框层有意不设闸口」的理由（第二真相来源会让漏注册的对话框渲染但不可交互 = 窗口硬冻结） | 全仓库已无 `IsDialogLayerInteractive` 引用（仅归档的历史报告保留当时的表述）；定向构建 0 警告/0 错误，`ModalHostViewModelTests` 8/8 通过 |

落地后的全量验证（本机实跑 `verify.ps1`，**exit 0**）：Debug 构建 0 警告/0 错误；单元 **1628 通过 / 2 跳过 / 0 失败**（1630）；Headless **176/176**；手写覆盖率行 **86.26%**（14376/16666）、分支 **93.03%**（2255/2424）；棘轮余量 **+0.41pp / +0.33pp**；Release 构建 0 警告/0 错误；Release 资源合约 18 通过。

**尚未落地、仍待裁定**：AUD-CI-008（Release 测试接入 `build.yml`）、AUD-REL-008（横幅契约测试）、AUD-MTN-020/021（两处文档/注释修正）、AUD-TST-005（残留无超时等待）、AUD-DEP-011（`AGENTS.md` 补 §12 一句），以及需要决策的 AUD-ARCH-005 / AUD-ARCH-009 / AUD-DEP-012。

## Medium

### AUD-CI-008 — Release 配置的测试只在打 tag 时跑，配置相关失败要到发版才暴露

- 类别：ci/testing｜严重度：Medium｜置信度：95｜状态：open｜处置：Add Guard｜建议验证：Verified

**证据**

`build.yml:60` 跑 `test.ps1 -Configuration Debug`，`coverage.ps1:61-67` 同为 Debug-only；整套 Release 测试只存在于 `release.yml` 的 installer job（`:263` `test.ps1 -Configuration Release`），而该 workflow 仅由 tag 推送触发（`:3-7`）。

本轮已实际发生：

- tag `v1.1.0-beta.9` 指向 `0de2905` 时，Release run `34665542855` 的 installer job **失败**——唯二失败用例是 `GameDownloadServiceTests.Stop_WhenNoOperationIsRunning_DoesNotLogDownloadStopped` 与 `Stop_WhenOperationIsRunning_LogsDownloadStopped`；同一提交的 Build run `34665540775`（Debug）**全绿**。
- 发布 job 因 `needs: [build, installer]` 被跳过，无坏产物外流；随后需追加提交 `490c2e5` 并重打 tag，Release run `34666008931` 才成功。

根因是该类断言对构建配置敏感：`UnifiedLogger.cs:39-45` 的级别开关在 `#if DEBUG` 取 Verbose、否则取 Information，Debug 级条目在 Release 被丢弃，`unified.log` 因此从不创建，读日志的断言随即抛 `DirectoryNotFoundException`。

**影响**

发版检查单里「推送后 main Build 必须绿，方可打 tag」这一条对本类失败**无保护作用**（Build 只跑 Debug）。任何 Debug/Release 分歧都只能在打 tag 之后被发现，代价是一次失败的发布流水线 + 改写历史重打 tag。

**建议**

在 `build.yml` 增加一步 Release 配置的测试运行（或给 job 加 `Configuration` matrix 维度），使该分歧在 PR 上即失败。

**建议验证**

Verified，但有两个实现细节必须照做：Release 配置在 csproj 中声明 `<SelfContained>true</SelfContained>`（`:29`），`dotnet test -c Release` 的还原会按当前平台 RID 解析依赖图，与提交的无 RID 段 lock 在 locked mode 下冲突（NU1004）——`release.yml:256-263` 正是为此显式豁免 `RestoreLockedMode: 'false'`，`build.yml` 新增该步骤必须带同样的 env 覆盖。成本已实测：本机 `test.ps1 -Configuration Release` 单元 45s + Headless 43s。

**建议守卫**：`build.yml` 的 Release 测试步骤本身。

## Low

### AUD-REL-008 — 发布横幅的版本契约无机械守卫，指南声明的画布尺寸也与 5 份已提交横幅不符

- 类别：release/packaging｜严重度：Low｜置信度：92｜状态：open｜处置：Add Guard｜建议验证：Strongly Supported

**证据**（三处缺口同一根因：横幅的版本契约其实只是「文件存在」）

1. `tests/.../ReleaseBannerContractTests.cs` 全文只读 `release-banner.template.json`（`:13`、`:319-320`），9 个 `[Fact]` 全为模板内的字符串/结构断言；唯一与 PNG 有关的断言是 `:32` 的 `Assert.EndsWith("-release-banner.png", resolved)`，作用于模板里 `output.image` 字符串，不触碰任何文件。无 `File.Exists`、无解码、无尺寸断言——零字节或错误尺寸的横幅照样全绿。同理，`:137-153` 只比对 asset 的 id，删掉四份 `docs/promo/assets/icons/*.png` 也不会失败。
2. `release.yml:149-153` 的门禁是 `Test-Path $bannerPath -PathType Leaf`，只查存在性。
3. `docs/promo/release-banner-guide.md:3` 声明「本仓库每个发行版本配一张 2000×1125 的发布横幅」，而实测 `docs/assets/release-banners/` 中 beta.1–beta.5 为 2400×1350、beta.6–beta.9 为 2000×1125（本轮用 System.Drawing 逐张读出）。

**影响**：`v1.1.0-beta.9` 的横幅本次确实合规（2000×1125，且 SHA-256 与管线 manifest 一致），门禁通过；但「提交一张内容或尺寸错误的横幅」这条路径上没有任何机械阻挡，且指南的尺寸声明无人可依。

**建议**：按仓库已有的正确范式补齐——`ReleaseChangelogContractTests.cs:81-88` 的 `ReadProjectVersion()` 已从 csproj 读 `<VersionPrefix>` 再断言 CHANGELOG 标题与之匹配；横幅测试应同样断言「与 csproj 版本同名」的横幅存在、可解码且为 2000×1125（PNG 尺寸可直接解析 IHDR，无需引入依赖）。

**建议验证**：Strongly Supported（范式与文件均已核实；未实现）。

### AUD-TST-005（重新打开）— 测试门控等待仍有未加上限的残留

- 类别：testing/determinism｜严重度：Low｜置信度：88｜状态：**open（原为 resolved）**｜处置：Add Guard｜建议验证：Strongly Supported

**证据**：`f8d6329` 的修复不完整。最明确的一处是 `MotionVisibilityTests.cs:147` 的 `await context.WaitForPostAsync()`——该 TCS（`:159-166`）只在被测代码于被包裹的 SynchronizationContext 窗口内同步 `Post` 时才置位，无任何上限；**同一文件另 4 处（`:31`/`:39`/`:87`/`:118`）在同一轮已加 `WaitAsync(TimeSpan.FromSeconds(5))`**，属同一夹具内的遗漏。同类的无界等待还有 `LogExportDialogViewModelTests.cs:85` 的 `await exportTask`，以及 `LogExportDialogViewModelTests.cs:211/227/239/243/255`、`LogViewerDialogViewModelTests.cs:31/69/183`、`MainWindowHeadlessTests.Golden.cs:67` 对 `PendingRangeProbeTask`/`PendingFilterTask`/`openTask` 的直接 await。

因此 `Cafe.Launcher.Avalonia.Tests.csproj:20-26` 的抑制理由（「门控等待统一用 WaitAsync/预算轮询加超时上限」）**再次不成立**。全仓无 `xunit.runner.json`、无 `[Fact(Timeout)]`、无 runsettings 超时，唯一兜底是 `build.yml:20` 的 40 分钟 job 上限——任一处挂起都会整段吃掉 job 预算而非快速失败。

**建议**：给上述等待补 `WaitAsync(5s)`，或为测试工程引入全局超时。

**建议验证**：Strongly Supported。

### AUD-MTN-020 — `CLAUDE.md:103` 仍把 click code 列为启动器数据

- 类别：maintainability/doc-drift｜严重度：Low｜置信度：95｜状态：open｜处置：Fix｜建议验证：Verified

**证据**：`CLAUDE.md:103` 写「Launcher data lives in `%LOCALAPPDATA%\Cafe Launcher\`: settings, unified log, persisted download state, shown notices, and click code.」`3e95793` 删除了该链路并同步了 PRIVACY.md 与四语言 resx，但漏改此行。附带：该链路原本写入的是**游戏目录**而非 `%LOCALAPPDATA%`，故这一行在删除前也不准确。

**建议**：删去 "and click code"。**建议验证**：Verified。

### AUD-MTN-021 — `LocalInstallationStateStore` 类注释写错文件名

- 类别：maintainability｜严重度：Low｜置信度：95｜状态：open｜处置：Fix｜建议验证：Verified

**证据**：`src/Cafe.Launcher.Avalonia/Services/LocalInstallationStateStore.cs:17` 的类摘要写「游戏目录内安装状态（**game_config.json** + manifest 副本）的唯一读写入口」，实际文件名来自 `Constants/GamePaths.cs` 的 `game-launcher-config.json`（`CLAUDE.md` 用的是正确名称）。grep 全仓库 `game_config` 仅命中该注释本身。该文件是与官方启动器互操作的契约文件，读错名字会把排查引向不存在的文件。

**建议**：改注释为 `game-launcher-config.json`。**建议验证**：Verified。

### AUD-ARCH-009 — 「官方强杀进程 vs Cafe 拒绝执行」这条有意分歧无决策记录

- 类别：architecture/decision-record｜严重度：Low｜置信度：90｜状态：open｜处置：Document｜建议验证：Strongly Supported

**证据**：官方启动器在安装/解压阶段强杀游戏目录下的所有 `.exe`，Cafe 改为拒绝执行并报 `GameRunning`（`DownloadSession.cs:233-238`、`GameUninstallService.cs:181-183`、`GameOperationJourney.cs:425`）。这是有意的安全分歧，但受版本管理的文档中无任何记录：`docs/design/adr/` 现有 ADR-001…020 全部为 UI/M3/崩溃域，grep「强杀」在受版本管理的文件中零命中。`CLAUDE.md` 的 Persistence and compatibility contracts 已记录「启动校验失败开放」「修复用 CRC64」等分歧，但不含这一条。

**影响**：后续维护者按官方行为「修回去」会削弱一项安全属性（游戏运行中拒绝覆盖/删除文件）。

**建议**：写成一条 ADR，或在 `CONTEXT.md` 的决策段登记。**注**：本项由原 AUD-DOC-003 拆出——它不依赖那份未跟踪的分析文档是否入库，故不随 AUD-DOC-003 的结案而消失。

**建议验证**：Strongly Supported。

## Informational

### AUD-DEP-011 — Dependabot 的 nuget PR 必然 Build 红灯

- 类别：ci/supply-chain｜严重度：Informational｜置信度：95｜状态：open｜处置：Document｜建议验证：Verified

**证据**：Dependabot PR #15（2026-09-12 开）的 Build run `34665271098` 失败，唯一失败用例是 `InstallerContractTests.ProjectConventionsToolchainTable_MatchesDeclaredPackageVersions`（Failed 1 / Passed 1623）——即 AUD-MTN-007 引入的「§12 表须与 `Directory.Packages.props` 一致」守卫**按设计触发**，而 Dependabot 无法自行更新 Markdown 表。`AGENTS.md` 的依赖升级约定只要求本地 restore 再生 `packages.lock.json`（对应已结案的 AUD-CI-003），未提 §12。

**建议**：`AGENTS.md` 的依赖升级段落补一句「同时更新 `PROJECT_CONVENTIONS.md` §12 工具链表」。该守卫本身有价值（它正是防止工具链表漂移的机制），本条是配套流程文档缺口，**不是**建议放宽门禁。

## 仍开放项（沿用上轮，状态未变）

| ID | 严重度 | 状态 | 摘要与其不修的理由 |
|---|---|---|---|
| AUD-DEP-012 | Informational | **product-decision** | 发行产物无代码签名。摘要清单已随发布交付（见上节 AUD-DEP-009），用户可校验「下载内容与发布者发布的一致」，但无法验证发布者身份——清单与产物同源托管，同时被替换即可同时失效。Windows 需 Authenticode 证书（`New-WindowsInstaller.ps1:139` 未传 SignTool），macOS 产物未签名/未公证，两者都需证书与费用决策 |
| AUD-MTN-017 | Low | open | 新增主叠层模态仍需约 14 个未守卫编辑点，`ShellLifecycle` 语言刷新清单漏改静默失败。需一次结构性收敛。**注**：本轮 `c5fa53a` 复用既有模态，未加剧该问题 |
| AUD-PERF-012 | Informational | open | 校验/安装/卸载阶段每文件一次 UI 线程 `Post` 无合并。机制已核，**队列深度后果未测量**，按 advisory 保留 |
| AUD-ARCH-006 | Informational | open | `ModalEntry.Content`（`ModalEntry.cs:4`）只被写入（`ModalHostViewModel.cs:60`）、从不被读取；消费者只读 `Top.Kind`。AGENTS.md 把它列为共享模态契约，文档高估现实 |
| AUD-ARCH-007 | Informational | open | `LocalDiagnostics.syncLogger` 静态可变（`LocalDiagnostics.cs:23`，构造时 `Volatile.Write` `:41`），仍有 **27 处**生产调用点走静态重载（本轮 `f69654d` 转换了 `GameDownloadService` 一处）。生产端仅一个实例，当前零影响 |
| AUD-MTN-018 | Informational | open | `BannerImageDecoder.cs:31-36` 复制了 `BackgroundImageDecoder.ClampLargestSide`（`:99-107`）的策略体（阈值常量已共享） |
| AUD-SEC-008 | Informational | open | 两处可预测 `*.tmp` 写路径（`LocalInstallationStateStore.cs:73-74`、`DownloadExecutor.GetTempName` `:423-425`）未纳入随机名硬化；利用需先具备游戏目录写权限，无权限提升 |
| AUD-DEP-010 | Informational | open | 两个 workflow 均无 `schedule:`，闲置 HEAD 上的新公告要等下次推送才被检出；Dependabot 不覆盖 `prototypes/`（该原型有意退出 CPM） |
| AUD-ARCH-003 / AUD-MTN-001 / AUD-TST-001 | Low | deferred | 与上轮一致：`RemoteContentViewModel` 直接持 `DispatcherTimer` 与其拆分（714 行）、限速测试的 `Stopwatch` 下限断言。本轮复验该文件未变，deferred 仍成立 |
| AUD-DEP-002 | Low | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |

## 需要你裁定的决策

1. **AUD-DEP-012**：是否引入代码签名（Windows Authenticode / macOS 公证）？需证书决策。
2. **AUD-ARCH-009**：「官方强杀进程 vs Cafe 拒绝执行」这条有意分歧要不要写成 ADR？
3. **AUD-CI-008 / AUD-REL-008 / AUD-TST-005 / AUD-MTN-020 / AUD-MTN-021 / AUD-DEP-011** 六项待办修复是否本轮一并落地？

> AUD-ARCH-005 已按裁定 (A) 落地（删除属性 + 文档记录策略），不再待决。
> AUD-ARCH-008 的「服务端是否校验 `head.version`」仍是未验证前提（需外部证据）；本轮以「官方逻辑等价 + 版本变更行为」的签名层守卫收口，未改动该常量的取值策略。

## 已解决项与台账维护

- 本轮把 14 项此前因 **rebase 合并**（PR #13，分支 SHA 未保留）而 `resolved_commit` 为空的发现补记为 `main` 上的实际提交：AUD-MTN-009→`3dee04a`、AUD-MTN-015→`aa93333`、AUD-MTN-019→`7a09574`、AUD-PERF-011→`d01d3a9`、AUD-PERF-013→`0520bc6`、AUD-SEC-006→`6f07bdc`、AUD-SEC-007→`4ae2ede`、AUD-TST-006→`310052a`、AUD-TST-007→`297479f`、AUD-TST-008→`310052a`、AUD-TST-009→`adb8446`、AUD-DEP-008→`2ecb148`、AUD-CI-006→`adb8446`、AUD-CI-007→`596dc4b`。delta 审计自此可按 `resolved_commit` 直接比对。
- 本轮新增 7 项（AUD-CI-008、AUD-REL-008、AUD-MTN-020/021、AUD-ARCH-008、AUD-DOC-003、AUD-DEP-011），重新打开 1 项（AUD-TST-005）。
- **按裁定落地**：AUD-DEP-009、AUD-ARCH-008、AUD-ARCH-005 已结案（工作树待提交），AUD-DOC-003 结案为已接受风险，其决策记录诉求拆分为 AUD-ARCH-009，签名诉求拆分为 AUD-DEP-012。台账现为 **101 项（81 resolved / 14 open / 3 deferred / 2 accepted-risk / 1 product-decision）**。
- **本轮新增自动守卫 2 处、文档裁定 2 处**：`AuthorizationHeaderFactoryTests`（5 例，签名算法与版本耦合）、`InstallerContractTests.ReleaseWorkflow_PublishesChecksumManifestForEveryDistributionPackage`（摘要清单发布契约）；`AGENTS.md` 模态隔离条款与 `CONTEXT.md` §模态交互权 的实现分工说明（记录「对话框层有意不设闸口」及其理由）。守卫均已做两向实测。
- **台账已补齐**：按裁定落地的三项已记录解决提交——AUD-DEP-009→`81df872`、AUD-ARCH-008→`7b69e80`、AUD-ARCH-005→`acfb3a3`。连同上一段回填的 15 项，台账内已无「resolved 但缺 `resolved_commit`」的条目。

## 推荐优先级

1. **AUD-CI-008**（在 `build.yml` 加 Release 配置测试）——改动小、杠杆最高，直接消掉「打 tag 才发现」这一整类失败；注意带 `RestoreLockedMode: 'false'` 的 env 覆盖。
2. **AUD-REL-008**（横幅契约测试）——照抄仓库已有的 `ReadProjectVersion()` 范式。
3. **AUD-MTN-020 / AUD-MTN-021**——两处一行文档/注释修正。
4. **AUD-ARCH-009**（一条 ADR 记录「拒绝执行 vs 强杀」）。
5. **AUD-TST-005** 残留等待补超时上限；**AUD-DEP-011** 补 `AGENTS.md` 一句。
6. 其余 Informational 与 deferred 按域择机处理。

## 验证过的健康面

只记录能关闭某个具体疑虑的结论：

- **本轮新功能未踩模态接线陷阱**：`c5fa53a` 复用既有 `ModalKind.RepairConfirmation`（`ModalKind.cs:20`、`ShellLifecycle.cs:519/838`、`MainWindowDialogsOverlay.axaml:255`），未新增叠层——AUD-MTN-017 的编辑点问题本轮未被加剧。
- **启动损坏判定的前提成立**：`DamagedFileCount` 唯一产出点是 `ManifestValidationService.cs:102`（`= 缺失 + 尺寸不符`），`GameLaunchService.Failed()` 与状态/路径类失败留下的计数器恒为 0，故 `HasDamagedFiles` 不会把「状态失败」误判为「文件损坏」。
- **两种构建配置下测试全绿**：Debug 与 Release 各跑一遍全量单元 + Headless，结果完全一致（1622/2 跳过/0 失败；176/176）——`490c2e5` 的级别修正生效。落地修复后 Debug 复跑为 1628 通过 / 2 跳过 / 0 失败（1630），Headless 176/176，棘轮余量 +0.43pp / +0.35pp。
- **发布链路完整**：`v1.1.0-beta.9` 的 tag 已推送（`ec92b351` → `490c2e5`），Release workflow success，Release 已于 2026-09-12 发布；横幅门禁通过；`CHANGELOG_RELEASE.md` 单节且覆盖自 beta.8 以来全部用户可见变更。
- **供应链未变动**：变更集内零依赖改动；CI 两 workflow 顶层 `permissions: contents: read`、第三方 action 全部 40 位 SHA 固定、`RestoreLockedMode=true`（RID 还原显式豁免）均未改动。新增的摘要清单步骤不引入任何新 action 或凭据。
- **无密钥泄漏**：变更集与未跟踪文档全树扫描无 `ghp_`/`github_pat`/`AKIA`/私钥/代理凭据；`ApiConfig.AuthorizationSalt` 是既有的公开协议常量，非新增暴露。
- **click code 删除无残留**：`ClickCodeService` 及其引用在全仓库 grep 零命中（仅历史审计报告与未跟踪分析文档提及）。
- **模态隔离机制已从「隐式」转为「已记录」**：原先对话框层的隔离靠 scrim + `ZIndex` + 子元素次序隐式承担，且 `ModalHostViewModel` 里有一个无消费者的「闸口」属性使契约显得比实际更强。本轮删除该属性并把两层机制（主叠层按种类绑定 `Is*Interactive`／对话框层由遮罩承担）写进 `AGENTS.md` 与 `CONTEXT.md`，使后续新增叠层时不必再靠读代码反推隔离从何而来。

## 审计方法与局限

- **模式**：`full` 全量。六个域各自过一遍：变更集逐提交审读（4 个含生产代码的提交逐行）、开放发现逐条回源复核、resolved 发现按台账回归核对（全部契约守卫随套件在两种配置下通过）、以及架构/安全/依赖/测试/性能/可维护性各域的当前态核查。审计结论出具后追加一轮「按裁定落地」，含两处新增守卫与两向实测。
- **实跑命令**：`pwsh -File ./verify.ps1`（exit 0，含本地化契约、Debug 构建、coverage.ps1 棘轮、win-x64 RID 还原、Release 构建、Release 资源合约测试）；`pwsh -File ./test.ps1 -Configuration Release`（exit 0）；落地修复后复跑 `verify.ps1`（exit 0）与定向 `dotnet test --filter`；`gh run view/list`、`gh release list`、`gh pr list`；`git log/diff/status/ls-remote/tag`；横幅尺寸经 PowerShell + System.Drawing 逐张读取；摘要清单步骤在本地沙箱（`/tmp`）用 6 个填充文件实跑，含 `sha256sum -c` 正反两向。
- **工作树处置**：`verify.ps1` 的 RID 还原按 `Directory.Build.props` 的既定行为两次改写了 `src/Cafe.Launcher.Avalonia/packages.lock.json`，均已按 `AGENTS.md` 要求 `git restore` 还原。
- **实验/交叉验证**：以 CI 实跑记录（Release run `34665542855` 的失败用例名与 job 结论）证实 AUD-CI-008，而非仅凭工作流静态阅读；以本机 Release 全量运行证实 HEAD 当前无配置分歧；横幅尺寸逐张实测以坐实指南声明与产物的差异；`UnifiedLogger.LogAsync` 无 `await` 经源码核实，用于排除 `f69654d` 的同步阻塞死锁假设；AUD-ARCH-008 的每一条守卫都做了反向实验（调换字段声明序、改动常量取值），确认守卫会失败而非恒真。
- **未执行**：`Build-Distribution.ps1` / Inno Setup 打包与安装器实测；Linux/macOS 上的任何测试；promotional-image 横幅管线端到端重跑；真实网络停滞/限速复现；AUD-PERF-012 的调度队列深度测量；`release.yml` 的真正端到端触发（只做了 YAML 解析与步骤本地模拟）。
- **局限**：未做逐行全量阅读。AUD-ARCH-008 的「服务端是否校验 `head.version`」这一前提**未取得证据**，故本轮只做签名层守卫，未改动该常量的取值策略。AUD-REL-008 的尺寸结论取自本地逐张读图（beta.1–beta.5 为 2400×1350），未核对历史发版时的实际发布文件。新增摘要清单未经真实 tag 验证（下一次发版才会实跑）。
- **差点误报（记录以免复现）**：`CHANGELOG_RELEASE.md` 中「不再记录并未发生的『下载已停止』」一条，一度看似「Release 构建下该行为无法被用户观察到」（该日志行为 Debug 级）。经核实仓库存在**用户可见的日志级别设置**（`Settings.LogLevel` → `SettingsViewModel.cs:587-600` 的 `ApplyLogLevel`，含 Debug/Verbose 档），把级别调低即可观察到该差异，故该条发布说明成立，未报为发现。
- **一个本地环境陷阱（非缺陷）**：先后以 Debug 与 Release 构建同一解决方案时，`--no-restore` 会复用上一次配置的还原资产而失败（`AvaloniaUI.DiagnosticsSupport` 的 assets 按 `Configuration != Debug` 排除，见 csproj:61-63），表现为 `App.axaml.cs:35` 的 `AttachDeveloperTools` 编译错误。这是 `release.yml` 的 Release 测试步骤必须显式豁免 `RestoreLockedMode` 的同一根因，重新还原即恢复。
