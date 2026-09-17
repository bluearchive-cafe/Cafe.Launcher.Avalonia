# 可简化 / 可重用逻辑全量扫描与修改计划

> 扫描基线：`main@8250756` · 2026-09-16
> 范围：生产 251 个 `.cs`（≈35.1k 行）+ 29 个 `.axaml`，测试 224 个文件（≈46.3k 行），`scripts/` 与 `.github/workflows/`
> 方法：8 路独立只读扫描（`Services/` · `Features/GameOperations/` · 其余 `Features/` · `ViewModels/`+`Views/`+`Controls/`+`Converters/` · `Helpers/`+`Models/`+`Constants/`+`Composition/`+`scripts/` · 单元测试 A–M · 单元测试 N–Z · 无头测试+`TestDoubles/`+`Support/`+XAML），随后主代理对全部高杠杆论断逐条读码复核
> 行号为**扫描时工作树**位置；落地任一单项前须重新定位（本计划不主张行号稳定）

---

## 0. 落地状态（2026-09-16）

**阶段 A 全部（13/13）· 阶段 B 19/22 · 阶段 C 6/8 · 阶段 D 2/17；§2 的 6 项正确性问题中 3 项已修、3 项开放。**
每个提交前跑 `.\verify.ps1` 且退出码 0。收口实测：单元 **1857 通过 / 0 失败 / 2 可见跳过**、
无头 **185 通过 / 0 失败**、覆盖率行 **87.27%** / 分支 **93.63%**（棘轮余量 +1.42pp / +0.93pp）、
Debug 与 Release 构建各 0 警告 0 错误。

| 批次 | 状态 | 提交 | 说明 |
| --- | --- | --- | --- |
| §2 正确性问题 | 3 修 / 3 开放 | `0db316d` `d2d4bc6` `7161f4c` `56e0509` | `DEF-1`（唯一用户可见、且已随 beta.9／beta.10 出货）、`DEF-3`、`DEF-6` 已修；`DEF-2`、`DEF-4`、`DEF-5` 开放，已分别立案为 `AUD-TEST-010`、`AUD-TEST-013`、`AUD-ARCH-012` |
| A 测试设施复用 | **13/13** | `fe9a012`..`0db316d` | 净 −814 行测试代码、生产零改动；`%TEMP%` 残留目录由 15 个／轮降到 **0 个／轮**（实测）；两处偏离原议见 A 节注 |
| B 生产侧等价收敛 | **19/22** | `7161f4c`..`9044998` | `B5` 并入评审候选 11 待裁决；`B12`／`B15` 评估后判定收益不抵成本；`B17` 两侧完成并暴露 `AUD-TEST-012` |
| C 死代码删除 | **6/8** | `5ec1999`..`ed7b290` | `C5`／`C8` 经核实为深模块边界与承重语义标记，判定不做 |
| D 结构收敛 | 2/17 | `d2d4bc6` `8aab531` | `D1`（即 `DEF-1`）与 `D14`（主题引擎搬进 `Services/ThemeApplier`，无接口）已落地；`D15` 仍需先裁决，其余 14 项待做 |
| E 登记不排期 | 0/9 | — | 按定义为登记项，不排期 |

新增发现已写入审计台账：`CODEBASE_AUDIT.md` 与 `.repository-audit/findings.json` 共**立案 8 项**
（1 Medium + 7 Low）：`AUD-ARCH-010`（= `DEF-1`，resolved）、`AUD-ARCH-011`（= `DEF-6`，resolved）、
`AUD-MAINT-005`（resolved，`B10` 补的往返守卫）、`AUD-TEST-011`（= `DEF-3`，resolved）、
`AUD-ARCH-012`（= `DEF-5`，open）、`AUD-TEST-010`（= `DEF-2`，open）、`AUD-TEST-012`（`B17` 暴露，open）、
`AUD-TEST-013`（= `DEF-4`，open，两处锚点待复核）。

**发布归属**：`DEF-1` 已随 `v1.1.0-beta.9` 与 `beta.10` 出货，**下一版需要一条面向用户的 `fix`
条目**（「卸载遇到只读文件不再整体失败」）。本计划未改 `CHANGELOG_RELEASE.md`——它当前是 beta.10
的单版本文档且 beta.10 已打标签。

**两条方法论结论**（都来自实测而非推断）：①涉及「是否存在泄漏」的条目必须以实测为准——本计划
原把 22 处 `%TEMP%` 字面量计为泄漏，按计划自带的协议实测后证伪一半（详见 A3 行下的注）；
②扫描的计数多次与实情不符，**逐项先读码复核是必需的**——`B4` 实为 30 处而非 19 处、`C2` 的 23 处里
有 1 处是活的、`C3` 的 4 个包装里 2 个完全无调用者、`C8` 四对里两对是承重标记、`B1` 的副本比
计划描述的更大。

---

## 1. 结论摘要

### 1.1 计数

扫描共产出候选 **91 项**，去重合并后：

| 分类 | 项数 | 说明 |
| --- | --- | --- |
| 阶段 A 守卫与测试设施复用 | 13 | 零产品风险，且保护其后所有批次 |
| 阶段 B 生产侧等价收敛 | 22 | 行为可证等价，改完即绿 |
| 阶段 C 死代码删除 | 8 | 删除即可，无需新测试 |
| 阶段 D 结构收敛（需逐项裁决） | 17 | 跨文件、涉及所有权或接缝 |
| 阶段 E 登记不排期 | 9 | 收益低于成本，只记录 |
| 明确不做（复核后否决） | 22 | 见 §4 |
| 扫描副产物：正确性问题 | 6 | 见 §2，**不是**简化项 |

其中约 **30 项集中在测试体系**——这是本仓库体量最大的一域（46.3k 行，多于生产源码），也是复用收益最高的地方：`tests/Support/` 与 `tests/TestDoubles/` 的设施在 2026-09-16 的测试设施轮刚建立，但**仍被大量调用点绕过**。

### 1.2 最该先做的五件

1. **`DEF-1`：卸载删除清单文件缺少只读属性清除**（§2）——一个只读文件就让整次卸载失败，且卸载侧用了较宽的路径守卫。同仓库的 `DownloadExecutor` 已经为同一问题付过费（代码注释原文记录了这次事故）。这是本轮扫描中唯一有用户可见后果的项。 → **已完成**（`d2d4bc6`）
2. **`A1`：契约测试各自手写仓库定位**（12 文件 13 处定义）——`TestRepository` 的缓存设计被它自己的目标消费者绕过，其中 5 处的向上目录遍历与设施逐行等价。 → **已完成**（`89fba3b`）
3. **`B1`：`LauncherUpdateService` 重写了 `VersionComparer` 的前置版本比较**——32 行逐行等价副本，两侧都有测试钉住同一批向量。 → **已完成**（`7161f4c`）
4. **`A10`：破坏性路径的测试用真实进程扫描器**——`GameUninstallServiceTests` 的默认装配绑定 `ProcessService.FindRunningExeNamesAsync`，开发机上有游戏进程时整类用例变红。同类事故本仓库已发生过一次（`GameDownloadServiceTests` 的注释记录了它）。 → **已完成**（`7161f4c`）
5. **`DEF-2`：`UiStyleContractTests.Motion` 的手抄叠层清单已实际漂移**——`DesignGalleryOverlay.axaml` 带 `motion-overlay` 却不在扫描集内，画廊的动效契约今天无人守。这是 AUD-TEST-006 的同类复发（守卫白名单在新建文件时漂移）。 → **开放**（立案 `AUD-TEST-010`；已实测确认该套件 153 条全绿而画廊的动效契约无人守）

### 1.3 与既有台账的关系（本计划不重复主张）

既有台账已覆盖的内容，本计划只在需要时引用、不重新立案：

- **`CODEBASE_AUDIT.md` 开放项**：`AUD-PERF-001`（自愈语义，明确不弱化）、`AUD-PERF-004`（横幅位图备忘）、`AUD-PERF-005`（壁纸同步解码，已文档化，勿回退）、`AUD-SEC-001`/`AUD-SEC-002`（接受风险）、`AUD-ARCH-005`（`ShellLifecycle` 订阅密度）。
- **架构深化评审第二轮（`docs/architecture-review-2026-09-14.html`）待裁决 7 项**：候选 06（journey 构造点）、07（Wire/Unwire 声明表）、08（模态闸口）、09（下载节奏时钟接缝）、11（降级三个单适配器接缝）、12（诊断注册所有方）、14（传输出口口径）。

  其中两处与本计划**同源**，落地时不要各改一遍：
  - 候选 **11** 已包含「`LocalDiagnostics` 的吞异常策略收进 `UnifiedLogger` 一次」——本计划的 `B5` 是它的一个子集，**并入候选 11 推进**，不单独排期。
  - 候选 **07** 与本计划的 `D15` 同源，但扫描对形态给出了**与候选卡不同的结论**（见 §5 决策一）：单据表可能比它要替换的代码更长。
- 本计划**不含**任何需要重开 ADR-021/022/024–032 的主张；`D1`/`D15`/`D14` 三项在 §5 明确标注为需裁决。

---

## 2. 扫描副产物：正确性问题

以下 6 项不是「可简化/可重用」，而是扫描过程中被证据钉住的**缺陷或守卫缺口**。按仓库既有惯例它们应各自独立成 `fix:` 提交，**不并入**本文的去重批次（混在一起会让 `git log` 与 release notes 失真）。

### DEF-1 卸载删除清单文件不清除只读属性，且路径守卫较宽

- **状态**：**已修**（`d2d4bc6`，立案 `AUD-ARCH-010`）。修法与建议一致（抽出 `ManifestFileRemover` 由下载与卸载共用），另发现计划未提的第二处：路径守卫也从 `GetSafePath` 收口到 `GetSafeFilePath`。变异验证：拆掉只读清除后 3 条用例同时变红（新增的卸载用例 + 下载侧既有两条）。**已随 beta.9／beta.10 出货，下一版需一条用户可见的 fix 条目。**

- **证据**：
  - `Features/GameOperations/GameUninstallService.cs:129-140`：`GamePathValidator.GetSafePath(gamePath, files[i].Path)` + 裸 `File.Delete(filePath)`，只兜 `FileNotFoundException`。
  - `Features/GameOperations/DownloadExecutor.cs:408-438`：同一件事用 `GamePathValidator.GetSafeFilePath` + `DeleteExistingFile`（先清 `FileAttributes.ReadOnly` 再删），其 doc 注释原文：「**previously aborted installs/updates outright**」。
  - `Helpers/GamePathValidator.cs:24-49` vs `:57-70`：`GetSafeFilePath` 存在的原因正是「拒绝根规范化条目（空路径、`.`、`sub/..`）」。
- **影响**：① 清单里任一文件带只读属性 → `File.Delete` 抛 `UnauthorizedAccessException`（不是 `FileNotFoundException`，不被兜住）→ **整次卸载失败**，用户看到「卸载失败」，而同一批文件走更新路径是能删的。② 根规范化条目走 `GetSafePath` 不被拒，`File.Delete(gameRoot)` 会抛 `UnauthorizedAccessException` 而非按设计被守卫拒绝——报错文本因此指向错误的原因。
- **建议**：抽 feature 内 `ManifestFileRemover.DeleteAll(gamePath, files, Action<int>? percent, CancellationToken)`，采用 `DownloadExecutor` 的 `GetSafeFilePath` + 清只读 + 宽容语义，由 `DownloadExecutor.RemoveFiles` 与 `GameUninstallService` 共用；卸载侧的 `PercentProgressGate` 保持在调用方。
- **守卫**：保留 `GameUninstallServiceTests.UninstallAsync_WhenManifestFileIsLocked_FailsAndKeepsRemainingFiles`（断言 `Progress == 33`）不动；新增 `…_WhenAManifestFileIsReadOnly_RemovesItInsteadOfFailing` 并做**变异验证**（还原成裸 `File.Delete` 即红）。
- **注意**：只读样例需按 AUD-CI-005 的先例处理平台差异——`FileAttributes.ReadOnly` 在 POSIX 上不阻止 unlink，用例应 `Assert.SkipUnless(OperatingSystem.IsWindows(), …)` 可见跳过，或在断言里写明该路径在 Unix 上本就通过。
- **发布影响**：若判定为既有缺陷，按 `AGENTS.md` 的 release notes 规则应作为 `fix` 条目面向用户描述（「卸载遇到只读文件不再整体失败」）；若判定为从未发布的同轮特性，则并入特性描述不单列。

### DEF-2 `UiStyleContractTests.Motion` 的叠层清单已漂移

- **状态**：**开放**（立案 `AUD-TEST-010`）。已实测确认：该套件 153 条全绿而 `DesignGalleryOverlay.axaml:10` 的 `motion-overlay` 无人守（纳入清单计数应为 10）。修它需要同时改清单与元契约，未在本轮动。

- **证据**：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.Motion.cs:197-206` 声明 7 个叠层文件，`:220` 断言 `Assert.Equal(9, overlays.Count)`；而 `src/Cafe.Launcher.Avalonia/Views/DesignGalleryOverlay.axaml:10` 带 `dialog-overlay motion-overlay` —— 该文件不在清单内，**其动效契约今天不被任何断言覆盖**。
- **性质**：与已解决的 `AUD-TEST-006`（令牌扫描遗漏 `ResourcePanelOverlay.axaml`）同一根因：手工白名单在创建新文件时漂移。该文件已有两个元契约（`UiStyleContractTests.cs:27-43` 的 `ScanTargets_CoverEveryTopLevelViewFile`、`UiStyleContractTests.Tokens.cs:702-717` 的 `StyleFiles_AreExplicitAndParseable`），但只管 `ViewFiles`/`StyleFiles` 两个集合，动效清单不在其管辖内。
- **建议**：把清单改为按目录发现（`Views/*Overlay.axaml`）+ 一份显式留名豁免集；补一条与 `StyleFiles_AreExplicitAndParseable` 同形的元契约断言「声明的集合 == 发现的集合」。`DesignGalleryOverlay` 补入清单（`9 → 10`）应与元契约同一提交，否则元契约首次运行即红——这正是它该有的行为。

### DEF-3 破坏性路径的测试绑定真实进程扫描器

- **状态**：**已修**（`7161f4c`，立案 `AUD-TEST-011`）。`TestGameProcessTracker.None` 进 `TestDoubles`，17 处默认绑定改为 `None`；变异验证：改成报「在跑」即 36 条用途例变红。

- **证据**：`tests/Cafe.Launcher.Avalonia.Tests/GameUninstallServiceTests.cs:568-577` 的 `CreateService` 默认 `processTracker ?? new GameProcessTracker()`，而 `Services/GameRuntime/GameProcessTracker.cs:25-27` 的默认构造绑定 `ProcessService.FindRunningExeNamesAsync`（真实系统快照）；`GameUninstallService` 在写入边界复查该闸门。同类站点另见 `InstallationOperationStateTests`（13 处装配 + 9 处检查点）与 `MainWindowTestContext.cs:138,158,210`。
- **影响**：开发机/CI 上若有 `BlueArchive.exe` 或其反作弊同族进程存活，卸载相关用例会按设计拒绝执行而**变红**，且原因与用例意图无关。`GameDownloadServiceTests.cs:1808-1814` 的 `CreateTrackerReportingNoGameRunning` 注释记录了本仓库已实际发生过这次事故——那些调用点没跟上。
- **建议**：把 `CreateTrackerReportingNoGameRunning` 提升为 `tests/TestDoubles/StubGameProcessTracker.cs` 的 `TestGameProcessTracker.None` 工厂，作为两个文件的默认；只有「正在运行」是断言对象的用例才注入真实/定制替身。
- **验证**：在跑着 `BlueArchive.exe` 的机器上跑一次目标用例（改前应红、改后应绿）；或做变异检查——把一处改回真实 tracker 并确认同一用例变红。

### DEF-4 无头套件泄漏 `Application.RequestedThemeVariant`，golden 结果依赖用例顺序

- **状态**：**开放**（立案 `AUD-TEST-013`）。两处锚定的是方法调用而非直接赋值，**尚未逐行复核**，故审计置信度只给 60。

- **证据**：生产写入点唯一，`D14` 落地前在 `Features/Settings/SettingsAppearanceViewModel.cs:584`，**现随该次搬移转到 `src/Cafe.Launcher.Avalonia/Services/ThemeApplier.cs` 的 `ApplyThemeMode`**（本条的锚点须按新位置读）。无头侧 `CrashReportWindowHeadlessTests.cs:28-29/56` 与 `ThemeSubscriptionTeardownHeadlessTests.cs:33/53-62`（`D14` 后直接构造 applier，快照与 `finally` 复位照旧）做了快照+`finally` 复位（证明危险已知），而扫描报告称 `MainWindowHeadlessTests.Dialogs.cs:332`（经 `ApplyTheme`）与 `SystemThemeColorHeadlessTests.cs:26-33`（经 `ApplyPlatformColorValues`）**未复位**；`MainWindowHeadlessTests.Golden.cs` 的 `PrepareGoldenWindow` 只固定语言、动效与字体，不固定变体。
- **影响**：共享一个 `Application` 的套件里，golden 截图截到亮色还是暗色取决于同批次哪个用例先跑。这是「偶然绿」的典型形态。
- **落地前须复核**：本条的行号锚定于方法调用而非直接赋值，落地时先读那两处方法确认是否真的漏了复位。
- **建议**：加 `ThemeVariantSnapshot : IDisposable`（或 `ThemeProbe`）设施放在 `HeadlessTestHost` 旁，四处统一走它；并让 `PrepareGoldenWindow` 显式设定变体，使 golden 不再依赖顺序。
- **像素影响**：若今天的环境变体恰好是亮色，改后像素不变；否则**会移动 golden**——先跑一次 `Golden_*` 对比再决定是否 `-UpdateGolden`。

### DEF-5 `GameOperationJourney` 的 `Ready` 分支是唯一不报告的拒绝

- **状态**：**开放**（立案 `AUD-ARCH-012`）。建议与 `D3`（修复路径闸门归位）同批落地，两者都动拒绝渲染。

- **证据**：`Features/GameOperations/GameOperationJourney.cs:457-460`（`if (snapshot.RuntimeState == Ready) return null;`）与同函数其余三个分支对照——`Corrupted` 开修复对话框（`:445-455`）、`IoFailure`/`RemoteUnavailable` 走刷新、结果走 toast（`:487-511`）。`GameOperationsViewModelTests.cs:1107-1116` 名为 `…_ReturnsUnavailable` 却只断言 `InstallCallCount == 0`。
- **影响**：该分支只在快照过期时可达（`Ready` 下安装按钮所在的 `IsInstallPanelVisible` 为 false），用户表现为「点了没反应」。与 `ADR-027`/`ADR-029` 建立的口径（确认后拒绝必须可见、预检失败不静默）不一致。
- **建议**：走与策略预检相同的拒绝渲染（`ShowOperationUnavailable`）；**不要**在此处新增策略调用——该分支的语义已经判定完毕。把该用例的断言补成它名字承诺的「报出警告」。此项应与 `D3`（修复路径闸门归位）同批，因为两者都动拒绝渲染。

### DEF-6 `GameShortcutService` 的公开入口无法被测试接缝切换

- **状态**：**已修**（`56e0509`，立案 `AUD-ARCH-011`）。收成 `ResolveDesktopDirectory()` 由两个入口共用；生产行为不变。

- **证据**：`Features/GameOperations/GameShortcutService.cs:98-101` 与 `:106-109` 各算一遍「本平台是否支持桌面快捷方式 + 桌面目录」的两套表达式；`:121,130,153,160-161,209,233` 混用注入探针与裸 `OperatingSystem.IsWindows()/IsLinux()`。`ShortcutEnvironment.ForCurrentPlatform`（`:66-71`）把注入探针接到真实平台检查上。
- **影响**：两个入口在生产下等价，但**公开的 `CreateDesktopShortcutAsync` 路径（真正碰真实桌面的那条）无法被接缝切换**，测试只能覆盖到另一条。属测试可及性缺陷而非产品缺陷。
- **建议**：收敛为单个 `ResolveDesktopDirectory()`，由两个入口共用；`ShortcutEnvironment` 保持唯一接缝。补一例经 `DeleteDesktopShortcutAsync` 的同等门控断言。

---

## 3. 修改计划

### 阶段 A — 守卫与测试设施复用（13 项）

**为什么先做**：零产品风险；且其中 `A1`/`A13` 会让后续批次的测试改动更快、更可靠。本阶段全部只动 `tests/`。

> **状态：已落地（2026-09-16，`fe9a012`..`0db316d`）。** 净 −814 行，生产代码零改动；`verify.ps1`
> 退出码 0（Debug／Release 各 0 警告 0 错误、单元 1853 通过 / 2 可见跳过、无头 185 通过、覆盖率
> 棘轮行 87.06% / 分支 93.38%）。两处偏离原议：①`A13` 要求删除的三个 `WaitUntil` 包装**保留**
> ——实测 `ToastHostViewModelTests` 一处有 40 个调用点，删掉会让同一句失败文案重复 40 遍；
> ②`A3` 的前提按实测更正，见该行下的注。`A9`／`A10` 的守卫做了变异验证（改坏后分别 157 条与
> 36 条用例变红），非仅以「用例绿」收口。

| 编号 | 项 | 证据锚点 | 落地改动 | 守卫 | 规模 |
| --- | --- | --- | --- | --- | --- |
| `A1` | 契约测试各自手写仓库根定位 | `DesignTokenContrastTests.cs:346`、`GameOperationStopOwnershipTests.cs:166`、`DialogActionButtonContractTests.cs:155`、`InstallDiskSpaceUiContractTests.cs:27`、`InstallerContractTests.cs:696,699`（该文件内 46 处调用）= 5 处；同类另 7 处（`ReleaseBannerContractTests.cs:319`、`ReleaseChangelogContractTests.cs:90`、`ReleaseScriptTests.cs:72`、`ReusableSettingsControlsContractTests.cs:67`、`SettingsWriteOwnershipTests.cs:161`、`ThirdPartyNoticesContractTests.cs:41`、`UiAccessibilityContractTests.cs:114`、`UiStyleContractTests.cs:349`） | 全删，改用 `TestRepository.InApplication(...)` / `InRepository(...)`；单测 A–M 与 N–Z 两路独立扫描均命中，**共 13 处定义 / 12 文件** | 改造后 TRX 通过集必须逐条一致；可做「重命名被扫描文件后确认消费者仍会失败」的非空转检查 | M |
| `A2` | `InstallationOperationStateTests` 同一段 5 行装配重复 13 次 | `InstallationOperationStateTests.cs:81-89,106-114,128-136,150-158,172-180,209-217,252-260,283-291,321-329,359-367,401-409,438-446,494-502`；另 `new DownloadCheckpointStore(…Guid…)` 10 次（`:533,550,580,604,624,648,697,736,772,789`） | 文件内私有 `CreateLaunchService(IGameRuntime?)` / `CreateCheckpointStore(TestDirectory)`（用 `dir.Sub("checkpoint.json")`） | 57 个用例全部保留、`--filter` 通过集一致；约减 85 行 | M |
| `A3` | 一次性 `%TEMP%\<Guid>` 数据根从不删除 | 22 处 / 8 文件：`InstallationOperationStateTests.cs:533,550,580,604,624,648,697,736,772,789`、`GameDownloadServiceTests.cs:1775-1776`、`GameOperationsViewModelTests.cs:1311`、`DialogsViewModelTests.cs:239,362`、`DownloadSessionTests.cs:238`、`LocalizationTerminologyTests.cs:190`、`ServiceConfigurationTests.cs:64`、`SetupWizardViewModelTests.cs:104,131,285,313,487` | 换 `TestDirectory.Create()` / 既有 `tempDir.Sub(...)`；仅用路径字符串的两处用 `BestEffort` 清理 | 全量跑一次 `test.ps1` 前后统计 `%TEMP%` 目录数，增量必须为 0（当前每轮泄漏 40+） | M |

> **A3 已落地（2026-09-16），本行前提按实测更正 —— 见下。** 本行把「22 处 `%TEMP%\<Guid>` 数据根」
> 当成 22 处泄漏，按本行给出的验证协议实测后**证伪了一半**：只有真正被写入的目录才会落地，
> 而根目录会不会落地取决于消费方是否创建它（`NoticeStateService` 只在公告真被记录时才写；
> `DirectoryWriteProbe.CanCreate` 只探测最近的已存在祖先，不创建目标）。每轮全量单元套件、
> 比对 `%TEMP%` 顶层改名集合的实测值：基线 `8250756` 泄漏 **15** 个 guid32 目录／轮 → 阶段 A
> 主体（`7856b63`，即本行那 13 处收敛）后 **14** 个／轮，**即那批收敛只去掉了 1 处真实泄漏**。
> 真正的泄漏源是本计划**没有识别出来**的另一类：14 处 `TestDirectory.Create()` 局部变量无人
> 释放（设施承诺「一处创建、一处删除」，漏掉后半句就是直接漏目录）。收口后实测 **0 个／轮**。
> 结论：涉及「是否存在泄漏」的条目必须以实测为准，静态清点 `%TEMP%` 字面量会把「指向不存在
> 目录的路径串」误计为泄漏。
| `A4` | 已是每用例独立的 `TestDirectory` 内再套 `Guid` | `MainWindowViewModelTests.Settings.cs:73,107,131,160,195,229,263,424,443`；`MainWindowViewModelTests.ResourcePanel.cs:17,45,69,112,144,173,198`；`MainWindowViewModelTests.Motion.cs:141,166,200,232` | 换 `tempDir.Sub(Constants.GamePaths.LauncherSettingsFileName)` | 每个受影响用例都先播种再断言，碰撞会表现为红而非静默绿 | S |
| `A5` | `TestDataRoot.ForDirectory(tempDir)` 冗余 | 22 处（`BackgroundViewModelTests.cs:483`、`DebugViewModelTests.cs:233,238`、`CrashReportTests.cs:15,84`、`LogExportServiceTests.cs:38`、`ShellLifecycleTests.cs:437,445` 等）+ 29 处 `ForDirectory(Path.Combine(tempDir))`（27 处在 `GameDownloadServiceTests.cs`） | 用 `tempDir.DataRoot`；后者整体塌成 `new LauncherSettingsService(tempDir.DataRoot)` | `TestSupportFacilityTests.DataRoot_DerivesWellKnownPathsFromTheDirectory` 已钉住等价 | S |
| `A6` | 35 处用例体内冗余 `tempDir.Dispose()` | `GameDownloadServiceTests.cs:274,319,374,409,448,503,563,625,646,688,726,840,…,1768`（35 处，其中 4 处包在整测 `try/finally` 里） | 全删，只留类级 `Dispose`（`:33-43`）。`TestDirectory.Dispose` 已幂等，且行内 `finally` 会让清理早于同类下一用例的残留观察——与 `ReportFailure` 的设计意图相反 | 前后 `%TEMP%` 计数不变 | S |
| `A7` | BestHttp Cookie 库二进制写入格式手写 4–5 遍 | `ResourcePanelViewModelTests.cs:385-405` ≡ `ResourcePanelServiceTests.cs:271-291`（逐行等价）；参数化超集在 `ResourcePanelUidServiceTests.cs:130-156`；内联第 4 处在 `MainWindowViewModelTests.ResourcePanel.cs:218-239`；另 `BestHttpCookieLibraryServiceTests.cs:11-27,48-64` | `tests/TestDoubles/BestHttpCookieLibraryFixture.cs`：`WriteAsync(path, uid, domain, cookiePath, empty)` + `Stream(Action<BinaryWriter>)`；超集签名使既有调用形状不改即可编译 | `BestHttpCookieLibraryServiceTests` 逐字段断言解析结果，字节序错即红 | M |
| `A8` | 合成 `Stream` 假体 5–9 份 | 预算+尾部族：`FileDownloadServiceTests.cs:418,468`、`GameDownloadServiceTests.cs:2175`；字节合成族：`ImageCacheServiceTests.cs:216`、`LauncherApiClientTests.cs:190`；「永不产出」两份逐行等价：`RemoteHttpTransportTests.cs:1023-1058` ≡ `ResponseBodyReaderTests.cs:56-92`，带读取计数的 `OneShotStream`（`:94-139`）同骨架 | `tests/TestDoubles/StreamFixtures.cs`：`StalledReadStream`（永不产出，可选计数）、`FixedLengthReadStream(bytes, delivered)`、`GatedReadStream`、`SyntheticReadStream(length, fill)` | 每个站点的尾部行为就是该用例的断言对象，迁移错会翻不同的断言；`ResponseBodyReaderTests` 三条正面用例是「桩仍会 stall」的阳性对照 | M |
| `A9` | 同名不同实现的 `FakeErrorHandlingService` ×2 | `SettingsOptionsDiskSpaceTests.cs:10-23`（`internal`，空实现，跨文件被 `SettingsViewModelTests.cs:134`、`GameRuntimeSettingsUiTests.cs:149` 消费）vs `ResourcePanelViewModelTests.cs:509-531`（`private`，记录型超集）；另 `GameOperationJourneyTests.cs:566` 同族 | 一份 `tests/TestDoubles/RecordingErrorHandlingService.cs`（记录 + 可选抛 `CriticalErrorRequested`），删两处本地声明 | 编译期即暴露漏改（原 `internal` 型程序集可见） | S |
| `A10` | 破坏性路径的测试用真实进程扫描器 | 见 `DEF-3` | 同 `DEF-3`：`TestGameProcessTracker.None` 进 `TestDoubles`，作默认 | 「正在运行」是断言对象的用例仍用真实/定制替身 | M |
| `A11` | 无头远内容信封逐份手搭 | `MainWindowHeadlessTests.Banner.cs:19-38,84-103,152-171,195-214,254-273`；`MainWindowHeadlessTests.RemoteContent.cs:28-52,104-128,152-196,212-257,282-295`（10 处，仅内层 payload 不同） | 无头工程内 `RemoteStateFixture.cs`：`WithBanners/WithNews/WithSocial` + `ApplyRemoteState(this TestContext, …)`。**放无头工程**（带工程知识，不属 `tests/Support/`） | 测试专用，无需新守卫；约减 150 行 | M |
| `A12` | 共享替身放在测试工程根目录 | `tests/Cafe.Launcher.Avalonia.Tests/StubFilePickerService.cs`（33 处调用 / 12 文件，含 `MainWindowTestContext.cs:113,188,294`） | 移入 `tests/TestDoubles/`（命名空间已一致，只改两个 csproj 的 `Compile-Link` 项） | `build.ps1` + `test.ps1 -Suite All`；移动时务必检查无头工程的 Include | S |
| `A13` | 等待原语仍有 4 套私有包装 + 27 处魔法超时 + 墙钟等待 | 包装：`RemoteContentViewModelTests.cs:762`、`ToastHostViewModelTests.cs:833`、`ResourcePanelViewModelTests.cs:345`、`MainWindowViewModelTests.ResourcePanel.cs:243`；超时字面量：`ShellLifecycleTests.cs` ×16、`SetupWizardViewModelTests.cs` ×3、`ResourcePanelViewModelTests.cs` ×3、`ToastHostViewModelTests.cs` ×2、`SettingsCategoryTests.cs` ×2、`RemoteHttpTransportTests.cs` ×1（全仓 60 处，仅 3 个不同值：2s×47 / 5s×12 / 10s×1）；墙钟等待：`CrossProcessPollingListenerTests.cs:25,38,56-57,59,65,85,98,124,147-148`（8× `Thread.Sleep` + `SpinUntil` + `DateTime.UtcNow` 截止）、`ToastHostViewModelTests.cs:730-737,785-792`、`SetupWizardViewModelTests.cs:540-550`、`LocalizationTerminologyTests.cs:148-170` | `TestWait` 补 `UntilAsync(cond, string message, TimeSpan? timeout = null)`（默认命名常量）、`Bounded(Task, string?, TimeSpan?)`、`HoldsForAsync(window, cond, what)`（`Stopwatch` 计时）；删 4 个包装，墙钟循环全部改走设施 | `TestSupportFacilityTests` 已有 `UntilAsync_*` 钉住超时/取消/推进；为两个新成员各补一条。`CrossProcessPollingListenerTests` 若确需真实 soak，保留**一个**命名常量并写明原因（对齐 `BackgroundViewModelTests.cs:216-223` 的先例） | L |

**逐项状态（13/13 落地）**：`A3` → `0db316d`；`A11` → `7856b63`；其余 `A1`·`A2`·`A4`–`A10`·`A12`·`A13` → `89fba3b`。其中 `A13` 有两处偏离原议（三个 `WaitUntil` 包装按判断保留、27 处魔法超时字面量与 `CrossProcessPollingListenerTests` 的 `Thread.Sleep` 未动），见本节注。

### 阶段 B — 生产侧等价收敛（22 项）

**为什么第二批**：改动都在 `src/`，但每项都能给出「等价」的读码证明，且多数已有测试钉住同一行为。逐项独立提交，便于二分。

> **状态：已落地 19／22（2026-09-16，`7161f4c`..`9044998` 八个提交）。** 全部 `verify.ps1` 退出码 0。
> 逐项先读码复核扫描结论再改，多处扫描计数与计划不符（`B4` 实为 30 处而非 19 处、`B1` 的副本是
> 整个 §11 比较而非「核心段比较」等），已在各提交信息里更正。
>
> **四项未按计划执行，各有理由：**
> - `B5`：按计划并入架构评审候选 11，需先对其统一口径作出裁决，不单独排期。
> - `B12`（三个错误三元组走 `ErrorHandlingService`）、`B15`（调试面板私有 `Format` 换
>   `LocalizationService.F`）：评估后判定收益不抵成本，理由见 `0a92d66` 的提交信息（前者需给
>   `ErrorHandlingOptions` 增两个字段并向两个诊断 VM 注入新依赖，三处标题/文案本就不同，改造后
>   行数持平；后者需为调试面板新增依赖并改 11 处调用点，且两者的失败语义不同——调试面板在模板
>   畸形时显示原文比报本地化失败更可取，而该情形已被本地化契约脚本挡在提交前）。
>
> **两项补了守卫（计划只提了「先补测试」）**：`B10` 的日志级别词表往返用例，与 `B17` 的严重度
> 完备性/区分度用例；两者都做了变异验证（拆掉即红）。`B18` 的忙碌重置亦做变异验证（删掉
> `finally` 后 4 条用例变红）。
>
> **`B17` 落地形态与计划草稿有两处偏离**：①草的 `ToastLifecycle` 把 `IsExiting` 列为记录字段——
> 它是模型上被 XAML 绑定的属性，照做会破坏绑定，未采纳；②三个槽位的寿命并不相同（倒计时贯穿
> 全程、动作令牌只在动作执行期、退出信号只在退场动画期），因此 `EndLifecycle` 只做拆除方该做的
> 事（释放悬挂的倒计时等待、丢弃记录），在途动作令牌仍归动作自身与 Dismiss 管——统一取消它会
> 是行为变更而非重构。
>
> **`B17` 的变异验证暴露一处既有覆盖缺口（需后续处理）**：把 `EndLifecycle` 里的倒计时唤醒
> 删掉后 **86 条提示条用例全绿**，即「拆除提示条时唤醒挂起的倒计时等待」这条不变量当前没有覆盖。
> 它是合并前就有的缺口（原 `StopCountdown` 做同一件事），失败形态是「挂起的倒计时任务一直阻塞
> 到宿主 Dispose」——只漏一个 `Task`、不产生错值，所以没有被任何断言看见。要补需要一条测试缝
> （在生命周期记录上挂本次任务），本轮未做。相对地，退出信号的移交是**有**覆盖的：去掉它
> `ToastExit_WhenAutomaticAndManualRequestsOverlap_WaitsAndRemovesOnce` 立刻红。

| 编号 | 项 | 证据锚点 | 落地改动 | 守卫 | 收益/风险 |
| --- | --- | --- | --- | --- | --- |
| `B1` | `LauncherUpdateService` 逐行重写 `VersionComparer` 的前置版本比较 | `Services/LauncherUpdateService.cs:322-353`（`ComparePrereleaseLabels`，32 行）与 `Helpers/VersionComparer.cs:55-87`（`ComparePrerelease`）**算法/规则/返回值完全一致，仅变量名不同**（已逐行读码确认）；另 `IsNewerVersion`（`:284-313`）、`TryParseSemanticVersion`（`:355-369`）、`SemanticVersion` record（`:371`）、`IsPrereleaseVersion`（`:235-236`）为同域自备 | 把 `VersionComparer.ComparePrerelease` 提到 `internal`，`ComparePrereleaseLabels` 删除改调它；`SemanticVersionRegex` 保留（发布过滤需要形状保证，`VersionComparer.Compare` 对非数字段宽容按 0 处理，语义不同，**不要**整体替换成 `Compare > 0`） | `LauncherUpdateServiceTests`（beta.2>beta.1、beta.11>beta.2、`1.2.0-1`<`1.2.0-alpha`、`beta.1.fix`>beta.1）与 `VersionComparerTests.Compare_HandlesPreReleaseSuffixes` 是**同一批向量的两份**，等价性主张一旦错误必红 | 高 / 低 |
| `B2` | 「按字节上限缓冲远程响应体」的循环两份 | `Services/RemoteHttpTransport.cs:377-407` ≡ `Services/ImageCacheService.cs:224-247`（声明长度拒绝 + 64 KiB 分块 + 流式上限，默认值相同；差别只在抛出的异常类型与文案） | `Services/RemoteBodyReader.ReadAllAsync(Stream, int maxBytes, CancellationToken)` 抛 `RemoteBodyTooLargeException`；传输侧映射回既有 `BuildResponseTooLargeException`，图片侧映射回 `InvalidDataException("Image response is too large.")`，两侧消息契约不变 | `ImageCacheServiceTests` 的两条超限用例 + 传输侧超限用例钉住两种异常类型 | 中 / 中 |
| `B3` | `ImageCacheService` 的缓存键穿越守卫两份 | `Services/ImageCacheService.cs:71-73`（查询，返 null）与 `:112-114`（写入，抛 `ArgumentException`）——三条件谓词与「纵深防御」注释逐字相同，只有反应不同（该差异是刻意的） | `private static bool IsUnsafeCacheKey(string key)`，两种反应留在调用点 | `ImageCacheServiceTests.CacheImageAsync_WhenHashContainsPathSyntax_Throws` 已覆盖两侧 | 低中 / 低 |
| `B4` | 「哪些文件系统失败可恢复」的谓词写了 19 遍 | 两类型过滤：`ImageCacheService.cs:55,175,188,276,304,311`、`CrashReportStore.cs:84,93,111,166,218`、`LocalInstallationStateStore.cs:149,183,264`、`LogExportService.cs:94,145`；加宽三处：`DiskSpaceService.cs:58,108,122`；前置 `JsonException` 三处：`LauncherSettingsService.cs:59`、`NoticeStateService.cs:40,61` | `Helpers/StorageFailure`：`IsRecoverable(e)`、`IsRecoverableOrInvalidJson(e)`；`DiskSpaceService` 的三处**保持显式并注明理由**（路径探测刻意容忍更多），不要折进去 | 既有各服务用例已覆盖 catch 路径 | 低中 / 低 |
| `B5` | `LocalDiagnostics` 把同一「尽力而为」包装写了 9 遍 | `Services/Diagnostics/LocalDiagnostics.cs:63-78,80-91,93-107,109-123,125-139,141-155`（六个仅严重级不同的实例方法）+ `:164-182,191-210,215-234`（三个静态入口）；`LogSync(title,message)` 是 `LogSync(Info,…)` 的手工展开 | 两个私有核心 `TryLogAsync(severity,…)` / `TryLogSync(severity,…)`，公开方法退化为一行转发 | `DiagnosticsServicesTests.LogSyncSeverityOverload_DoesNotThrow`、`DiagnosticsLogTitleContractTests`；**需在提交信息里说明一处可见变化**：debug 回退行由 `[INFO]` 变 `[Info]` | 中 / 低 |
| **（并入候选 11）** | — | **`B5` 与架构评审候选 11「把『永不抛』策略收进 `UnifiedLogger` 一次」同源** | 按候选 11 的统一口径推进，不单独排期 | 见候选 11 | — |
| `B6` | `LocalInstallationStateStore.ReadCoreAsync` 的失败构造写 6 遍 | `Services/LocalInstallationStateStore.cs:206-212,216-222,228-234,248-253,257-262,266-272`（六个 `CreateFailure(kind, gamePath, configPath, manifestPath, err)`，三个路径参数已是局部变量） | `ReadCoreAsync` 内局部函数 `Failure(kind, error = null)`；静态 `CreateFailure` 保留给 `CommitAsync`/`DeleteAsync` | `LocalInstallationStateStoreTests` 钉住四种 kind | 中 / 低 |
| `B7` | `HttpClientFactory.CreateLeaseAsync` 同四行构建两遍 | `Services/HttpClientFactory.cs:80-84` 与 `:90-94`（`new HttpClient(handler, disposeHandler: false)`、条件 `BaseAddress`、条件 `Timeout`、`ApplyHttpVersion`） | `private HttpClient CreateClient(SocketsHttpHandler, Uri?, TimeSpan?)`；代理分支只留 `ConnectionProxy` 初始化 | 既有租约/工厂用例；语义不变 | 低 / 低 |
| `B8` | 进程句柄失效谓词两份 7 处 | `Services/GameRuntime/ITrackedProcess.cs:46,61,83,95,103`（5 处）+ `RuntimeVersionProbe.cs:153,180`（2 处），同一组 `InvalidOperationException or Win32Exception or ObjectDisposedException`，配 5 种不同回退值 | `internal static bool IsProcessUnavailable(Exception)` 放在 `Helpers/ProcessService.cs`；**该文件 `:47`/`:67` 的两处较窄集合不要动**（刻意容忍不同失败），在注释里写明 | 谓词是纯函数；`GameProcessTrackerTests`/`GameRuntimeTests` 钉住各回退值 | 低 / 低 |
| `B9` | `CrashReport` 的三处必填字段构造 | `Services/Diagnostics/CrashReportStore.cs:66-77`、`FatalCrashService.cs:134-146`、`CrashReportBootstrap.cs:50-61`（同一份「从 `BuildInfo`/当前 UI 区域性/`CrashOrigin.ToSourceLabel()`/异常类型填充」；**已可见漂移**：两处用 `Environment.OSVersion.ToString()`，`CrashReportStore.cs:73` 用 `RuntimeInformation.OSDescription · OSArchitecture`） | `internal static CrashReport Build(id, occurredAt, source, operatingSystem, exceptionType, technicalDetails)` 自行填三个构建身份字段 | `CrashReportTests` + `CrashReportBootstrapTests`；补一条断言三个出口的 `AppVersion == BuildInfo.LauncherVersion` | 中 / 低 |
| `B10` | 日志级别词表在两处独立存在且无守卫 | 生产侧 `Services/Diagnostics/UnifiedLogger.cs:61` 模板 `[{Level:u3}]`；消费侧 `Services/Diagnostics/LogEntryReader.cs:27-29` 正则 `(ERR\|WRN\|INF\|VRB\|DBG\|FTL)`（被 `LogViewerDialogViewModel.cs:285`、`LogExportService.cs:258` 消费）；另有词表的 5 处再声明（`LogViewerDialogViewModel.cs:307-317`、`SettingsViewModel.cs:586-594`、`SettingsOptionsViewModel.cs:303-311`、`DebugViewModel.cs:136-144,155-169,178-190,210-216`、`UnifiedLogger.cs:103-111`） | **先补测试再谈合并**：写一条「每种 `LogEntrySeverity` 经真实 `UnifiedLogger` 落盘一条，`LogEntryReader` 必须全部识别」的往返用例（用 `TestDirectory`）。之后可选：`LogLevelCatalog` 单表 `(Code, LogEntrySeverity, Label, LogEventLevel, SettingCode, LocalizationKey)`，并断言每个 `Code` 被正则匹配 | 该往返用例本身就是守卫；`LogViewerDialogViewModelTests`/`DebugViewModelTests`/`SettingsViewModelTests` 不退化 | 中 / 中 |
| `B11` | 外观（主题模式 + 主题色）从快照应用写了 4 遍 | `Features/Shell/ShellLifecycle.cs:235-238`、`:395-398`、`:415-418`、`:692-699`（每处都是 `ApplyTheme(s.ThemeMode)` + `ApplyThemeColor(s.ThemeColorMode, ParseColorOrDefault(s.CustomThemeColor))` 这对语句；`:412-420` 是同一对前面加一行语言） | `SettingsAppearanceViewModel.ApplyFrom(LauncherSettings)`（模式+色的幂等投影）；四处改调它，`ApplyLanguageAndThemeAsync` 变为 `ApplyLanguage(s.Language); Appearance.ApplyFrom(s);` | `MainWindowViewModelTests.Appearance.cs`、`SystemThemeColorHeadlessTests`、golden；补一例「保存自定义主题色后 `Launcher.Color.*` 资源已更新」 | 中 / 低 |
| `B12` | 三处手写 toast+diagnostics 三元组，绕过为其而生的服务 | `LogViewerDialogViewModel.cs:165-175`、`:206-216`、`LogExportDialogViewModel.cs:247-255` vs `Services/ErrorHandlingService.cs:80-93`（其类注释原文：让调用点「不再手写这个三元组」）。绕过的唯一原因是 `HandleErrorAsync` 写死诊断标题 `"ErrorHandling"`，而这些站点需要自己的模块标签（`"LogViewer"`、`LogExportService.LogTitle`，由 `DiagnosticsLogTitleContractTests` 强制） | 给 `ErrorHandlingService` 加 `DiagnosticsTitle`/`DiagnosticsMessage`（或一个带模块标签的重载），三处改走它，删两处 `?? "Failed to load log entries"` 字面量 | 既有失败路径对 `ToastRaised` 的断言；补断言诊断标题仍为 `LogViewer`/`LogExport` | 中 / 低 |
| `B13` | 「选项 code → 本地化显示名」刷新循环 4 处 | `SettingsOptionsViewModel.cs:314-320` 与 `:322-328`（逐字相同，仅元素类型不同）；另 `:167`、`LogExportDialogViewModel.cs:122-135`、`ResourcePanelViewModel.cs:133-147`。两个重载存在只因参数是 `ObservableCollection<SettingOption>` vs `ObservableCollection<ThemeOption>`，而两者都派生自 `SelectableOption`（`Models/LauncherRuntimeModels.cs:12/46/57`） | `Helpers/SelectableOptionLocalization.Refresh<T>(IEnumerable<T>, Func<string,string>) where T : SelectableOption` | `SettingsOptionsDiskSpaceTests`、`LocalizationTerminologyTests`、`LogExportDialogViewModelTests` 不变；补语言切换后 `Theme`/`ThemeColor` 显示名断言 | 中 / 低 |
| `B14` | 设置向导硬编码语言显示名 | `SetupWizardViewModel.cs:499-506` 手写 `"English"`/`"简体中文"`/`"繁體中文"`/`"日本語"` vs `Services/LocalizationService.cs:183-190`（工厂已在共享层，不违反无跨功能引用） | 用 `LocalizationService.GetLanguageOptions(localizer).First(o => o.Code == Language).DisplayName` | `SetupWizardViewModelTests` 摘要步骤断言；`Test-LocalizationContract.ps1` 保证字面量不回流 | 中 / 低 |
| `B15` | 调试面板私有格式化器 vs `LocalizationService.F` | `DebugViewModel.cs:447-457`（`Format`，11 处调用：`:123,190,218,220,236,255,282,293,314,328,416`）vs `Services/LocalizationService.cs:169-181`。差异：私有版在 `FormatException` 时静默 `return template`，而共享路径会把「模板未格式化」报为本地化失败 | 注入 `LocalizationService`，改用 `localizer.F(key, args)`；`shell.I18n[...]` 保留给 XAML 绑定文本 | `DebugViewModelTests`；`Test-LocalizationContract.ps1` | 低 / 低 |
| `B16` | `ShellViewModel` 加载占位重置 3 处 + 2 个死属性 | 同一组 5 个赋值出现在 `ViewModels/ShellViewModel.cs:151,154-157`（`ApplyLanguage`）、`:173-175`（`SetRefreshError`）、`:164-165`（`SetLoading`，只覆盖 5 个中的 2 个）；`GameFolderPickerTitle`（`:93`，赋值 `:146`）与 `LogExportFolderPickerTitle`（`:95`，赋值 `:147`）全仓无读取者（各调用点自行本地化标题） | `ResetLoadingPlaceholders()` 供前两处消费；`SetLoading` 的刻意子集保持显式；删两个死属性及其赋值 | `MainWindowViewModelTests.Lifecycle.cs:94` 覆盖 `SetLoading`；`ResxResourceContractTests`/`UiStyleContractTests` 覆盖绑定 | 低中 / 低中 |
| `B17` | Toast 严重度的呈现属性分两层 switch + 三个按 id 的平行字典 | `Models/ToastNotification.cs:101-111`（severity→`IconKind`）vs `Converters/ToastSeverityToBrushConverter.cs:25-31`（severity→色 key）；`ViewModels/ToastHostViewModel.cs:25,26,27` 三个字典、8 处触碰点（`:104,155,267-274,322,358,363-370,405-424,470-476`），每条拆除路径都要记得同时改三个 | ① `Models/ToastSeverityProfile(IconKind, BrushResourceKey)`，`IconKind` 委托给它、转换器读它；② `Dictionary<string, ToastLifecycle>` 合并三字典，`StopCountdown`/`CancelActionToken`/`FinishExitOnUiThread` 收成 `EndLifecycle(id)` | ① `ConverterHeadlessTests`、`UiStyleContractTests.ToastLog`；② `ToastHostViewModelTests`（26 条，含重叠退出、减少动效、退出中释放）+ `MainWindowHeadlessTests.Toast.cs:93` | 中 / 中 |
| `B18` | journey 在 5 个入口重复 guard→busy→try/catch/finally 骨架 | `Features/GameOperations/GameOperationJourney.cs:73-125`、`:155-178`、`:181-220`、`:264-291`、`:338-360`、`:435-485`；相同的 `SetBusy(true)`/`finally { SetBusy(false); }` 对在 80/123、162/176、188/218、338/359、390/413、479/480；差异只有日志上下文串与失败资源键 | `private async Task RunGuardedAsync(string errorContext, string failureMessageKey, Func<Task> body)`；三个无返回值站点（启动、检查更新、创建快捷方式）收敛为「守卫 + 一次调用」；`RepairAsync`/`RunInstallOrUpdateAttemptAsync` 传闭包并保留各自的 `refreshHandled`/`ApplySnapshotSafe`；`ResumePersistedAsync`（`:381-415`，`ShowToast = false`）**保持原样** | 既有逐站点用例已钉住 toast 与 busy 迁移（`GameOperationJourneyTests.cs:119,188,228,272`）；若引入 helper，在其中至少一处补 `IsBusy` 迁移断言 | 中 / 低 |
| `B19` | 策略拒绝的渲染写了 6 处 | 4 处逐字相同：`GameOperationsViewModel.cs:288-293,306-311,383-388` + `GameOperationJourney.cs:331-336`；2 处方法体相同：`GameOperationsViewModel.cs:420-421` ≡ `GameOperationJourney.cs:363-364`；3 处相同结果构造：`GameDownloadService.cs:118,132`、`GameUninstallService.cs:77` | **保留** `GameOperationPolicy.Decide` 与每个站点的拒绝语义（ADR-027 要求调用点表态），只共享渲染：feature 内 `GameOperationRejections.WarnUnavailable(...)` / `UnavailableResult(...)`。`D3` 落地后本项自然再减 3 处 | 无需新测试；`GameOperationPolicyTests.Decide_IsTheOnlyPublicVerdict_NoBareBoolean` 继续钉住判定面 | 中 / 低 |
| `B20` | 暂停/恢复呈现由 3 处各算一遍 | `GameOperationsViewModel.cs:155-162`（`ApplyLanguage`）、`:347-373`（命令）、`:534-537`（进度回调）都写 `PauseResumeText`/`PauseResumeIcon`；`:203-204` 在 `PrepareOperation` 复位；`"Pause"`/`"Play"` 字面量 6 处。**副作用不一致**：命令清 `ProgressSpeed`+`ProgressEstimated`，进度路径经 `clearsDownloadMetrics`（`:517-520`）清速度、仅在阶段非 `Downloading` 时清 ETA（`:527-533`） | `private void ApplyPausePresentation(bool paused, bool canPause)`，三处调用；两个图标字面量提为文件内常量 | `GameOperationsViewModelTests.PauseResumeCommand_TogglesBackendAndPresentation`（`:620`）、`ApplyProgress_MapsPreflightAndVerificationStagesAndClearsDownloadMetrics`（`:974`）；补一例「应用 Paused 进度快照产出与命令相同的文本/图标」 | 中 / 低 |
| `B21` | `GameShortcutService` 两套平台解析 | 见 `DEF-6` | 同 `DEF-6` | 同 `DEF-6` | 中 / 低 |
| `B22` | 两个诊断 `Describe()` 构造器共用同一习语 | `Services/GameRuntime/RuntimeProbeResult.cs:44-69` 与 `GameRuntimeDiagnosticSnapshot.cs:26-54`（都是「可选行 `!string.IsNullOrWhiteSpace` 才追加的 `Label: value` 列表，`Environment.NewLine` 连接」，共 6 处受守卫追加） | `Helpers/DiagnosticText` 极小 builder（`Line`/`Optional`/`ToString`）；若判定太小则各文件内一个私有 `AppendOptional` | `GameRuntimeDiagnosticSnapshotTests` 钉住快照文本；`RuntimeProbeResult.Describe` 由 `GameRuntimeTests` 断言 | 低 / 低 |

**逐项状态（19/22 落地）**：`B1`·`B3`·`B4`·`B6`–`B9`·`B22` → `7161f4c`；`B2`·`B10` → `cfe9648`；`B13`·`B14`·`B16` → `0a92d66`；`B19`·`B21` → `56e0509`；`B20` → `f52eea3`；`B18` → `fe59372`；`B11` 与 `B17` → `42f327d`（`B17` 的严重度半边）+ `9044998`（生命周期半边）。**未落地 3 项**：`B5`（并入评审候选 11，待裁决）、`B12`·`B15`（判定不做，理由见 `0a92d66`）。

### 阶段 C — 死代码与无效代码删除（8 项）

**为什么第三批**：纯删除，零行为风险；但需先确认「无引用」判定正确——本仓库有「靠 `InternalsVisibleTo` 被测试直接调用」与「靠字符串键被 XAML 消费」两类陷阱，逐项已核。

> **状态：已落地 6／8（2026-09-16，`5ec1999`..`f42e5a6` 四个提交）。** 全部 `verify.ps1` 退出码 0。
> 落地：`C1`（4 条不可达样式 + 1 个未消费 token）、`C2`（22 处不生效类）、`C3`（4 个转发包装）、
> `C4`（同步加载路径 + 3 个协作者去可空）、`C6`（1 个死访问器 + 1 个接缝由 public 收窄为
> internal）、`C7`（进程接缝迁到消费者旁）。
>
> **两项经核实后不做，理由是扫描的「无引用」判定在这些点上不成立：**
> - **`C8`（四对字节相同的样式合并）**：四对里有两对是**承重的语义标记**。`Border.motion-bottom`
>   与 `Border.motion-surface` 的样式体确实逐字相同，但 `motion-bottom` 是 ADR-016 用来区分
>   「游戏操作表面」与各叠层的标记——`UiStyleContractTests.Motion.cs:184` 把两个选择器都列进
>   穷尽集合，`:271` 另断言全仓只有一个元素带它且必须是 `OperationSurface`。`Border.banner-media`
>   被无头用例当作定位器（`MainWindowHeadlessTests.Banner.cs:213` 用
>   `Classes.Contains("banner-media")` 找元素），改名即找不到。另外两对不破坏守卫，但同样保留：
>   样式体相同是当前设计令牌的巧合，类名各自标记不同角色。带 `C8` 的版本实测确实让那两条 Motion
>   守卫失败——这正是它们存在的意义。
> - **`C5`（`ResourcePanelService` 的 7 个转发成员）**：方法体确实是纯转发，但它是否「多余」不看
>   方法体而看消费者要知道多少——该类的文档注释明确写着它是「拥有资源面板工作流的深模块：
>   **UID 解析**、并行远程读取、版本与模式映射、保存序列化；ViewModel 只留可观察状态、命令与
>   本地化」。实测消费者只有 ViewModel（7 处）与测试（7 处），改成暴露协作者会让 ViewModel 多依赖
>   两个服务（`ResourcePanelUidService`、`LocalDiagnostics`），依赖面变宽即变浅——与阶段 D 的
>   「深模块」方向相反。
>
> **两处计数与计划不符**：`C2` 的 23 处里**有 1 处是活的**（`:246` 在 `StackPanel` 上，两条选择器
> 都命中它），只删了 TextBlock 上的 22 处；`C3` 的 4 个包装里**有 2 个一个调用者都没有**
> （`GetReadableOnAccentColor`、`AdjustColor`），计划写的「唯一使用者在测试里」低估了。
> `C6` 的第二项也未按计划删除（四条测试是解码与来源解析的单元测试，改指 `Apply` 会变成断言
> 另一件事），而是按 §5.3 把仅供测试的接缝由 `public` 收窄为 `internal`。

| 编号 | 项 | 证据锚点 | 落地改动 | 陷阱提示 |
| --- | --- | --- | --- | --- |
| `C1` | 死样式规则与未引用 token | `Views/MainWindow.Styles.axaml:490-496` `Border.surface`——全仓无 `Classes="surface"` 应用（已 grep 确认，只有定义），且 `UiStyleContractTests.MainWindow.cs:525-528` 反而断言它**不得**出现在远内容表面；`:1021-1024,1038-1040,1047-1049` 的 `.warning` 变体——`warning` 类全仓零应用（同族的 `.danger` 有应用，`:256`）；`App.axaml:55` `Launcher.Component.Dialog.HeaderAction.Margin`——视图/样式/C#/测试全无引用 | 删四处规则 + 一个 token；日志导出若仍需警示语气，改用它处已有的 `dialog-alert.warning` 配方（`MainWindowLogExportOverlay.axaml:47,112` 已用 `dialog-card log-export-warning`） | token 集合里 `Launcher.Motion.Offset.*` **看似**未被 XAML 引用，实际由代码后置的字符串键消费（`SetupWizardOverlay.axaml.cs:27-28`、`Helpers/BannerCarouselTransition.cs:27`）——删 token 前必须 grep 字符串键 |
| `C2` | 22–23 处惰性 `Classes="button-content"` | `Views/MainWindowDebugOverlay.axaml:75,81,87,93,99,105,114,120,126,132,137,144,148,157,162,182,187,192,211,226,230,234`（实测 23 处，全仓唯一这样做且唯一的 `TextBlock` 用法） | 该类的选择器是 `StackPanel.button-content`（`MainWindow.Styles.axaml:1159-1162`）与 `StackPanel.button-content > TextBlock`（`:1163-1165`），落在 `TextBlock` 上不匹配任何选择器。删这 23 个属性即可 | 可顺带抽出 `Controls/IconTextContent.axaml`（`IconKind`+`Text`）供全仓 49 处 `<StackPanel Classes="button-content">` 图标+标签复用（先例：`SettingRow.axaml`/`SettingSelect.axaml`）；须保持 `Button` 的 `AutomationProperties.Name` 原样 |
| `C3` | 4 个仅为测试可达而存在的颜色转发包装 | `Features/Settings/SettingsAppearanceViewModel.cs:727-737`（`NormalizeAccentColorForUi`、`GetReadableOnAccentColor`、`AdjustColor`→`Helpers/ColorUtils`；`ToColorHex`→`ThemeColorExtractionService`）。唯一使用者在 `MainWindowViewModelTests.Appearance.cs:203,215`，而 `MaterialSchemeGeneratorTests.cs:414,432` 已直接调用 `internal` 的 `ColorUtils`（`Properties/AssemblyInfo.cs:3-4` 有 `InternalsVisibleTo`） | 全删，测试改调 `ColorUtils`/`ThemeColorExtractionService`（该文件 `:224,444` 已这么做） | 确认 `ToColorHex` 无生产调用者（已核：无） |
| `C4` | 日志查看器的生产不可达同步加载路径与随之而来的可空容忍 | `Features/Diagnostics/LogViewerDialogViewModel.cs:92-111` + `:235-247`（同步 `ReadEntries`/`LoadEntries`，与 `:258-270` 的异步版逐行对应，只差 `ReadLine` vs `await ReadLineAsync`）；生产零调用，仅 `LogViewerDialogViewModelTests.cs:28,44,219-220` 使用——这也是 `toastService`/`localizer`/`diagnostics` 为可空（`:24-25`）与两处 `localizer?.T(key) ?? "…"` 回退（`:165-167,206-208`）的由来 | 删同步路径，三个协作者改非空；两个测试改走 `OpenCommand` 或已注入的 `entryLoader`（`:83,89`）。对齐 `LogExportDialogViewModel`（同协作者非空，其测试自行构造） | 与 `B12` 同文件，建议合并为一个提交 |
| `C5` | `ResourcePanelService` 的 7 个纯转发成员 | `Features/ResourcePanel/ResourcePanelService.cs:31,34-63,108-111`：`CookieLibraryPath`、`ResolveUidAsync`、`ResolveUidWithSourceAsync`、`GetUidSourceAsync`、`SaveUidSourceAsync`、`SaveManualUidAsync`、`LogErrorAsync` 全部一对一转发给 `ResourcePanelUidService`/`LocalDiagnostics`；服务自身的真实工作是 `LoadDataAsync`/`SaveConfigAsync`/`MapItem`（`:69-131`） | 暴露协作者（`public ResourcePanelUidService Uid { get; }`）或把协作者直接注入 VM；注意 `ResourcePanelUidService:70-75` 的写路径经 `ISavedSettingsWriter`，**该所有权不能变** | `ResourcePanelServiceTests.cs:203-239` 需重定向到服务自身表面 |
| `C6` | 两个无读取者的公开成员 | `ViewModels/ConfirmationDialogViewModel.cs:45-46`（私有字段 `operationContext` 仍在 `:81-83` 使用，仅访问器无引用）；`ViewModels/BackgroundViewModel.cs:400-406`（`LoadCustomBackgroundAsync` 生产零调用，仅 `BackgroundViewModelTests.cs:93,107,125,441`；真正的路径是同文件的 `LoadCustomBackgroundImageResultAsync`） | 删访问器；第二个把四个测试改指 `Apply`/`ApplyBackgroundPresentation` 或 `imageLoader` 接缝后删包装 | 删 `BackgroundViewModel` 成员时注意它的公开静态是刻意的测试接缝，别顺手清理 |
| `C7` | 单消费者的 `Helpers/ProcessService` 位置 | `Helpers/ProcessService.cs:11-71`，唯一消费者 `Services/GameRuntime/GameProcessTracker.cs:25,62`；其 `TryReadProcessName` 是 `internal` 正是为了让 tracker 共用 | 移入 `Services/GameRuntime/`（它不是 DI 服务，`PROJECT_CONVENTIONS.md` §5 不适用）。副作用：消掉 `Helpers/` → `Services/` 的唯一内部依赖 | 成员保持 `internal`，测试零改动 |
| `C8` | 字节相同的样式对 | `MainWindow.Styles.axaml:781-784` `Border.banner-media` ≡ `:785-788` `Border.banner-frame`（各 1 处使用）；`:485-489` `TextBlock.panel-title` ≡ `:1139-1143` `TextBlock.section-title`（1 vs 11 处）；`:1159-1162` `StackPanel.button-content` ≡ `:1166-1169` `StackPanel.card-heading`（49 vs 1 处）；`:582-584` `Border.motion-surface` ≡ `:591-593` `Border.motion-bottom` | 保留高使用量的名字，删孪生并重贴单处调用点；把 `MainWindow.axaml:119` 内联的 `CornerRadius`+`ClipToBounds` 收回类 | **必须同一提交**处理 `UiStyleContractTests.Tokens.cs:135-167` 的 `FontWeight_StrongIsLimitedToConfirmedEmphasisScenarios` `SetEquals` 允许表（`:150` 含 `TextBlock.panel-title`），否则该用例失败。像素不变 |

**逐项状态（6/8 落地）**：`C1` → `5ec1999`；`C2` → `8e8b7d7`；`C3`·`C6`·`C7` → `538c48b`；`C4` → `f42e5a6`。**判定不做 2 项**：`C5`·`C8`（理由见本节注）。

### 阶段 D — 结构收敛（17 项，逐项需裁决）

**为什么最后**：这些项跨文件、动所有权或接缝，且部分与待裁决的评审候选重叠。**每项独立裁决、独立提交**，不要打包。

> **状态：已落地 2／17（`D1` → `d2d4bc6`，`D14` → `8aab531`）。** 其余 15 项未动，其中 `D15`
> （Wire/Unwire 形态）按 §5 仍需先裁决，`D15` 与架构评审候选 07 同源。
>
> `D1` 的落地形态与卡片一致（抽出 `ManifestFileRemover` 由下载与卸载共用），但**实测比卡片描述的
> 缺陷更宽**：除了只读属性，两条路径的路径守卫也不同（卸载侧用 `GetSafePath`，下载侧用
> `GetSafeFilePath`，后者才会拒绝归一到游戏根目录自身的条目）。两者随共享一并收口。另有一处
> 计划未提的行为变化：`DownloadSession` 现在把 `activeToken` 传给删除循环（原先签名里没有令牌，
> 是签名的偶然；该路径上其它每一步都转发同一令牌）。变异验证：拆掉只读清除后三条删除路径的
> 用例同时变红。
>
> `D14` 的落地形态按卡片（`Services/ThemeApplier`），但**未按 §5 原议与候选 11/12 合并**——读码
> 复核后认定两者无共同机制，理由与其裁定同址（§5 决策二）。搬移是逐行照抄：写入的键、颜色与
> 顺序不变，订阅仍在首次落模式时懒建，`Dispose` 的退订随订阅一并归 applier；VM 侧无调用者的
> `GetSystemAccentColor`／`IsDarkTheme` 静态包装直接删除，不留在原地。两条被重定向的 headless
> 守卫**重新做了变异验证**（拆退订、拆中性策略传递各让一条用例变红），因为守卫换了宿主以后
> 「原先它咬得住」不再是不需证明的事实。

| 编号 | 项 | 证据锚点 | 落地改动 | 风险 |
| --- | --- | --- | --- | --- |
| `D1` | 清单文件删除逻辑两份，卸载侧无只读清除 | 见 `DEF-1` | 同 `DEF-1`（feature 内 `ManifestFileRemover`） | 中——动卸载的删除路径，须逐条保住 ADR-030 的残留上报语义 |
| `D2` | `UninstallAsync` 一次调用内问了两次「游戏在跑」 | `GameUninstallService.cs:83` → `:373-422`（闸门 `:409-414`），随后 `:97-102` 的边界复查；两者之间只隔一次 `localInstallationStateStore.ReadAsync`（`:89`）。而每次判定都是一次完整进程枚举 | 拆 `ValidateAsync` 为 `ValidateMetadataAsync`（存在/未保护/元数据合法/字段齐）与公开的 `ValidateAsync` = 元数据 + 进程闸门；`UninstallAsync` 只调元数据部分，方法内**保留恰好一次**进程闸门（边界那次）。注意 `ValidateAsync` 的 `EnsureGamePath`（`:389`）在 `UninstallAsync` 语境下可证为空操作（`:80` 已先归一） | 低——但必须保住 `UninstallAsync_WhenCalledTwice…` 依赖的元数据检查 |
| `D3` | 修复路径的「确认后策略闸门」留在 VM，卸载那条在 journey | 闸口在 VM：`GameOperationsViewModel.cs:288-293`（开对话框前）、`:306-311`（`RepairConfirm.Confirmed` 处理器，订阅于 `:150`）；卸载对应闸口在 journey：`GameOperationJourney.cs:327-336`。两处 `ShowOperationUnavailable` 逐字相同；「安装遇损坏」分支由 journey 构造与 VM 相同的一对（对话框, `RepairWarning`） | 把 `Decide(...) == Rejected → ShowOperationUnavailable(); return;` 移到 `GameOperationJourney.RepairAsync` 顶部、**先于** `PrepareOperation`（`:266-268`）——必须早于它，因为该调用会latch `PanelMode = Progress`（VM `:190-205`）且只有 `SetIdlePanels` 能复位。删两处 `ShowOperationUnavailable`，新增 `GameOperationJourney.RequestRepairAsync(snapshot)` 复用 `:447` 已有的 `RepairWarning` 分支 | 低（与 `DEF-5`/`B19` 同批） |
| `D4` | `repair` 布尔在两文件里分叉 7 次 | `DownloadSession.cs:110,250,264-277,288,513,516,523`；流经 `:40,84,243`、`GameDownloadService.cs:121,135,264`、`DownloadSessionFactory.cs:56`。全部派生自 `DownloadSessionFactory.Create` 一处设定的模式位 | feature 内 `DownloadOperationProfile(Kind, CheckStage, CompletedStage, NoChangesKey, CompletedKey, LogCategory, BuildPlan)` + `ForDownload`/`ForRepair` 两个静态；会话持有 profile 而非 `bool repair`。**保持** `state.IsRepair` 检查点字段与两个 `BuildXPlanAsync` 签名不变 | 中——7 个分叉都要逐一对应到 profile 字段 |
| `D5` | 结果/进度工厂挂在 759 行的有状态会话类型上 | `DownloadSession.cs:653-667`（`CreateProgress`，被 `ManifestDiffCalculator.cs:80,121` 使用）、`:670-684`（`Failed`，13 处外部调用点：`RunningGameGate.cs:38`、`GameDownloadService.cs:118,132`、`GameUninstallService.cs:77,121,379,384,393,399,404`）、`:687-694`（`EnsureGamePath`，`GameUninstallService.cs:389`） | 三个 `internal static` 原样搬进 feature 内的 `GameOperationOutcomes` / `GameOperationProgressFactory`，会话改调新类型。纯搬移，无逻辑变化 | 低 |
| `D6` | 百分比门控在每个逐文件阶段各包一遍 | `ManifestDiffCalculator.cs:72-82,112-124`、`DownloadExecutor.cs:283-284,332-336`（单调）、`GameUninstallService.cs:127,142-151`；裸 `(i+1)*100d/count` 另见 `ManifestDiffCalculator.cs:232,272`、`DownloadExecutor.cs:415` | `internal sealed class StageProgressReporter(kind, stage, Action<GameOperationProgress> sink, bool monotonic = false)`，暴露 `Action<int> Report` 与静态 `Percent(completed, total)`。**必须保留** `ShouldDeliver` vs `ShouldDeliverMonotonic` 的可选（`PercentProgressGate.cs:29-41` 写明并行校验阶段的理由）。`D1` 会吸收卸载那处 | 低 |
| `D7` | 卸载文件计数在两个单位间往返，用字面量 `2` 换算 | `GameUninstallService.cs:211`（`AffectedFileCount = files.Count + 2`）、`:419-420`（`ReadyToUninstall` 用原始计数、`AffectedFileCount` 用 +2）、`GameOperationsViewModel.cs:407`（`Math.Max(0, validation.AffectedFileCount - 2)`）；资源键 `readyToUninstall`/`uninstallConfirmText` 的 `{0}`/`{1}` 与两个单位对不上 | 二选一：`AffectedFileCount` 改报清单文件数（与 `ReadyToUninstall` 的 `{0}` 一致），「含两个状态文件」只在完成文案需要时计算；或命名常量 `LocalInstallationFileCount = 2` 两侧共用 | 低——但改计数单位会动用户可见文案，需同步四语 `.resx` 与 golden |
| `D8` | 「最新胜出」的异步刷新槽 5 处，陈旧判定机制各不相同 | `LogViewerDialogViewModel.cs:113-133`（CTS）、`LogExportDialogViewModel.cs:163-211`（代数计数 **+** CTS，且 `CancelRangeProbe:178-184` 一处同时递增代数、取消并释放同一个源——三机制干一件事）、`SetupWizardViewModel.cs:288-317`（裸 `version` + 不可取消的 `Task.Delay(300)`）、`SettingsViewModel.cs:502-514` 与 `:421-433`（CTS 换取）、`SettingsAppearanceViewModel.cs:173-186`；域外同形：`ViewModels/BackgroundViewModel.cs:329-340` | `Helpers/LatestRefresh`：`run(TimeSpan? debounce, Func<CancellationToken,Task> work)` + `Task Pending { get; }`；各站点保留自己的工作 lambda，`Pending` 由 helper 暴露（测试已在 await `PendingFilterTask`/`PendingRangeProbeTask`/`PendingAppearancePreview`） | 中——5 处行为细节不同，需逐处对照；补一例「被取消的那次绝不应用其结果」 |
| `D9` | 有界「等到稳定」循环两份 | `SettingsViewModel.cs:516-537` 与 `SettingsAppearanceViewModel.cs:145-166`（同一算法：快照当前 in-flight 任务 → `await pending.WaitAsync(带超时的 CTS)` → 超时返回 → 引用未变则重循环；只有被观察字段与 `Timeout` 静态不同） | `Helpers/TaskSettler.WaitAsync(Func<Task?> current, TimeSpan budget)`；两个 `TimeSpan` 静态收进调用点 | 低；补一条超时路径用例 |
| `D10` | 语言刷新有 4 种方法名、2 种机制、7 个扇出目标 | `ShellLifecycle.cs:735-743`（7 个显式调用）、`ShellViewModel.cs:118-160`（自身再拉 2 个）、`SettingsViewModel.cs:219-226`、`ResourcePanelViewModel.cs:120-131`、`DebugViewModel.cs:147-153`、`LogExportDialogViewModel.cs:120`；向导另走 `LocalizationService.LanguageChanged` 订阅（`SetupWizardViewModel.cs:58,488-497`）；`LogViewerDialogViewModel` 完全没有语言钩子 | 根 `ViewModels/` 下共享 `ILanguageAwarePresentation { void RefreshLocalizedText(); }`（与 `IModalContentViewModel` 同址）；Shell 遍历 `ShellPresentationFamily` 成员。属 Shell 已获准的向下聚合，不是跨功能引用 | 低；需断言向导的自订阅已移除、且各家庭成员文本确实被刷新 |
| `D11` | 资源面板装载结果与条目集合不位置对齐 | `ResourcePanelViewModel.cs:110-115,285-290,406-410,425-430`、`:34-40`、`ResourcePanelService.cs:140-154`：`ResourcePanelItems` 是固定 3 元有序表，`ResourcePanelLoadResult` 是同三样的 3 属性包，5 处各自按 code 线性查找（`:485-489`，12 次调用） | 装载结果改为有序 `IReadOnlyList<ResourcePanelItemData>`，`ResourcePanelService.MapItem` 按位映射；baseline 塌成一次集合比较。另给 `ApplyItem`/`MarkItemsLoading`/`MarkItemsFailed`（`:432-476`）一个 `SetState(item, status, icon, text)`（三者现在用魔法图标串写同三个字段） | 低 |
| `D12` | 位图所有权（孤儿释放、先换引用后释放、延迟释放）分散在两条流水线 | 先换后释：`RemoteContentViewModel.cs:576-579` vs `BackgroundViewModel.cs:549-553,564,591-594,684-689`；孤儿释放：`RemoteContentViewModel.cs:559-563` vs `BackgroundViewModel.cs:511-520`（同一个契约，后者是具名 helper、前者是内联副本） | `Helpers/BitmapLifetime`：`ThrowIfCancellationRequested(IImage?, CancellationToken)`（从 `BackgroundViewModel:511-520` 提升）与 `ReleaseAfterBindingsSettle(IImage?)`（`Dispatcher.UIThread.Post(..., Background)` 形态，见 `:564,594`）。**不要**合并两条加载流水线（输入、解码策略、陈旧判定三处都不同） | 中——**先补测试再改**：横幅流水线目前**没有**端到端用例（`MarkImageLoaded`/`BannerBitmap` 在 `Apply` 后从未被断言，`MainWindowViewModelTests.RemoteContent.cs:63-89` 只断言模型旗标）。先写「驱动 `Apply` 后 `BannerItems[0].BannerBitmap is not null`、清空后无泄漏」的无头用例 |
| `D13` | 共享的 XAML 重复块 | ① 游戏管理动作块：`Views/MainWindow.axaml:477-516` ≡ `:543-582`（40 行 `MenuFlyout` 逐字相同），`：464-471` ≡ `:529-537`（启动按钮）——紧凑布局与 `IsStatusDetailHidden` 布局是同一组控件的两次渲染；② 向导步骤帧：`Views/SetupWizardOverlay.axaml:62-68,99-105,180-186,217-223,263-269`（6 属性 × 5），评审行 4 处 16 行块（`:281-296,298-313,315-331,333-348`），单选 5 处 9 行块（`:195-203,…`）；③ 关于分区：`Views/SettingsAboutSection.axaml:181-204`（5 个相同 `about-kv-row` + 4 个分隔条）、`:211-239`（3 个相同 3 列链接按钮） | ① `Views/Styles/SetupWizard.axaml` 加 `StackPanel.wizard-step` 样式（四个值都已是 token）；评审行与单选用 `ItemsControl`（先例：`MainWindow.axaml:294-317`、`DesignGalleryOverlay.axaml:169-207`）。② 关于分区两用 `ItemsControl`（VM 已恰好暴露 5 个标量属性 + 3 个命令）。③ 游戏管理块抽 `Controls/GameManagementActions.axaml`；**注意** `MenuFlyout` 不能挂在两个所有者上，不能用共享资源 | 中——①`UiStyleContractTests.MainWindow.cs:225-289` 当前**断言了这个重复**（`Assert.Equal(2, manageButtons.Length)`），必须同提交改写；②向导 `<StackPanel.RenderTransform>`（`:69-71,106-108,187-189,224-226,270-272`）由代码后置动画驱动，必须保留；③**纯重构不得重生 golden** |
| `D14` | 主题引擎长在设置外观 VM 里 | `Features/Settings/SettingsAppearanceViewModel.cs:571-737`（`ApplyTheme`/`ApplyScheme`/`SetBrush`/`EnsureThemeSubscription`/`OnActualThemeVariantChanged`/`lastScheme*`/`GetSystemAccentColor`/`IsDarkTheme`），由 `ShellLifecycle.cs:235-238,395-398,415-418,692-699,412-420` 当作服务消费 | 把「写 `Application.Resources` 的进程级主题应用器」移进 `Services/ThemeApplier`（或 `IThemeService`），VM 只留草稿/调色板状态并转发 | 中——**需裁决**，且与评审候选 11/12 同域（见 §5 决策二） |
| `D15` | `ShellLifecycle` 的 Wire/Unwire 人工配对 | `Features/Shell/ShellLifecycle.cs:423-456`（Wire，31 条语句）与 `:564-607`（Unwire，41 条语句）；实测 **17 对订阅 + 4 个可赋值委托槽 = 21 个挂点，30 个协作者/字段**（`:35-71`）；其中 `getBackgroundBitmap`、`previewAppearanceAsync`、`applyLanguageAndThemeAsync`、`openExternalUrl` 四个 `Func` 字段**只为 Unwire 的身份比较而存在**（`:586-604`）；对称性只有一条用例（`ShellLifecycleTests.cs:263`） | 扫描建议：保持 `Wire` 为可读列表，但每条包进 `Attach(Action attach, Action detach)` 并累积 `List<Action> detachAll`；`Unwire` = 逆序执行 + `Clear()`。这样删掉 4 个身份检查与 4 个 backing 字段，「新增订阅 = 一行 `Attach`」不会漏配。**扫描明确不建议**改成 `record Subscription` 数据表（三处异质的源/事件元数 + 模态注册块，表会比被替换的代码更长） | 中——**与评审候选 07 同源且结论不同**，见 §5 决策一 |
| `D16` | `CrashReportWindow` 用自己的 token 家族却大量写字面量 | `Views/CrashReportWindow.axaml:20-27` 声明 `Crash.Spacing.*`/`Crash.Radius.*`，但 `:10,12`（`Width="700"`/`MaxHeight="720"`）、`:113`、`:115-117`（`44`/`CornerRadius="22"`）、`:147,180,177-179,192` 及 6 处 `FontWeight="SemiBold"`、`:68-81` 的 `MinWidth="108"`/`Padding="16,8"` 等仍是裸数字；`Crash.Spacing.Md`（`:22`）与 `Crash.Spacing.Xxl`（`:25`）声明后从未被消费 | 补齐 `Crash.Layout.*`/`Crash.Typography.*` 条目并消费；两个未用 token 要么用、要么删 | 低——该文件被 `UiStyleContractTests` 显式豁免（`:11-17`），所以今天无守卫；建议顺带为 `Crash.*` 加一条扫描 |
| `D17` | 三个手写 INPC 模型 vs 工具箱基类 | `Models/LauncherRuntimeModels.cs:243-249` ≡ `:267-273`（两个逐字相同的 `SetField<T>`）、`:38-43`（第三个，仅 `string` 变体）；三个类声明在 `:12,188,252`。同目录其余可观察模型**已经**派生自 `ObservableObject`（`GameRuntimeSettings.cs:7`、`LauncherSettings.cs:10`、`ToastNotification.cs:67`、`ResourcePanelItem.cs:67`、`ThemeColorPaletteItem.cs:6`、`BannerDot.cs:9`），基类已是承重结构 | 三个类改派生 `ObservableObject`；`SelectableOption` 的三个属性可用 `[ObservableProperty]`（工具箱产出的 `PropertyChanged` 契约相同） | 中——须保留 `RemoteContentItem.IsImageLoading`/`IsImageLoadFailed` 的私有 setter；补「每类一个属性的 `PropertyChanged` 名称断言」，避免通知被静默丢掉（横幅圆点会不再更新） |

**逐项状态（2/17 落地）**：`D1` → `d2d4bc6`（即 `DEF-1` / `AUD-ARCH-010`）、`D14` → `8aab531`。其余 15 项未动，其中 `D15` 需先裁决（见 §5），`D3` 建议与 `DEF-5` 同批落地。

### 阶段 E — 登记不排期（9 项）

收益低于成本或属无据重构，**只记录**：

| 编号 | 项 | 不排期的理由 |
| --- | --- | --- |
| `E1` | `GameOperationJourney.cs:457-460` 之外，`GameOperationJourneyTests` 的 `RecordingErrorHandlingService` 未共享 | 单点，`A9` 落地时自然一并处理 |
| `E2` | `ShellRefreshCoordinatorTests.cs:44-60,137-150,192-199` 三份双闸门 host 内联 | 仅约 40 行，且三条用例各钉一个分支；合并需给替身加 `params` 表面 |
| `E3` | `ReleaseBannerContractTests` 同一 JSON 模板重读 8 次 | 可读性小改善，无性能问题 |
| `E4` | `SettingsEditorTests` 的 6 组脏标记事实与 `LauncherSettingsTests` 的反射完备性用例重合 | **不完全**重复——6 组还覆盖 `Discard()` 还原，而反射用例不碰。合并有丢失覆盖的真实风险，收益不足 |
| `E5` | `RuntimeVersionProbeTests` 为 3 条事实 spawn 真实 `dotnet`（各 60s 预算） | 需要给 `RuntimeVersionProbe` 加进程工厂接缝（动生产签名）；只留超时/杀进程一条真跑即可。值得做但不急 |
| `E6` | `MainWindow.Styles.axaml` 与 `Views/Styles/*.axaml` 里 17 处相同的焦点环两 setter 配方、5 处「填充态禁用」四 setter 配方 | Avalonia 没有选择器别名；唯一替代是低优先级的 `:is(Control):focus-visible` 兜底规则，那会改级联语义并**移动像素**，且 `UiStyleContractTests.Tokens.cs:665-675` 正钉着 `Button:focus-visible` 的厚度。除非 UI 负责人裁决，不动 |
| `E7` | `LauncherSettings` 三份平行枚举（属性 / 拷贝构造 / `ComparedProperties`） | `Models/LauncherSettings.cs:168-177` 与 `PROJECT_CONVENTIONS.md` §7 已书面化为刻意设计（可 grep、无反射），两张表都有守卫。不重开 |
| `E8` | `LocalInstallationStateStore` 的 `.tmp`+`File.Move` 未走 `Helpers/AtomicJsonFileStore` | 刻意不同：该 store 必须在发布前**回读并校验** temp 文件（`:131-139`），且有 `beforeTempValidation` 交错接缝（`:126-129`）；通用 store 会丢掉「发布前先验证」的保证 |
| `E9` | `ThemeColorExtractionService.ToSv` 与 `Helpers/ColorUtils.ToHsv` | 代数等价但 IEEE 双精度上非逐位相同，且 `ToSv` 按字节运算以避免热循环里每像素构造 `Color`；其结果喂给量化阈值（`MinimumSaturation`），末位变化可能改变调色板。不值得冒险 |

**状态**：按定义为登记项，不排期，全部未动。

---

## 4. 明确不做（本轮复核后否决）

扫描过程中记录、但**不应改动**的项。列出以便后来者不再重复评估：

**测试侧**
- `[Collection(nameof(LocalizationServiceTestIsolation))]` 覆盖 28 个类——`LocalizationService.InitializeForTesting` 是进程级 last-writer-wins 槽位，只读类也会读到被改写的资源集，串行化是承重的。
- `DownloadExecutorTests.cs:427` 与 `GameUninstallServiceTests.cs:58` 的 `List<GameOperationProgress>` 收集——都是单线程顺序路径（1 个文件 / 1 次卸载），`CallbackRecorder` 的作用域明确排除它们。
- `Assert.Equal(572, ResxValues["en"].Count)`——刻意过度指定的反空转锚点，两条相对断言给不出这个保证。
- `WindowsFactAttribute` 与行内 `Assert.SkipUnless` 两种形态并存——前者适合整测门控，后者适合条件可托管体；两种都符合「跳过可见」规则。
- `SettingsWriteOwnershipTests.cs:28-45` 的硬编码持有者清单——`:81-93` 断言语义是与 `src/` 递归扫描**相等**，新持有者会让构建红，这是硬编码清单的正确形态。
- `GamePathValidatorTests.cs:14,27,37,52,63` 使用 `Path.GetTempPath()`——纯路径串断言，不在磁盘上创建任何东西。
- `RemoteHttpTransportTests.cs:719-786` 的 `LoopbackHttpProxy`——唯一真实 socket 事实，`HttpMessageHandler` 无法行使 `HttpClientLease.ConnectionProxy`。保留。
- `GameDownloadServiceTests.cs:1287-1318`（断言 `Elapsed >= 800ms` 的真实节流）与 `BackgroundViewModelTests.cs:221`（`Task.Delay(700ms)` 否定不变量 soak）——这两条**就是**契约本身。

**生产侧**
- `LauncherCoreService.LoadAsync` 的六块扇出——表驱动需要异构元组，或丢掉 `"game-config"` 这类操作名（那些名字进日志）。
- `RemoteHttpTransport` 的整体体量（`Normalize`、`IsRetryable`、重定向/状态/流策略、64 MiB 上限与十六进制/ASCII/压缩预览）——单模块拥有单传输契约，doc 注释写明了全部错误模式。
- `ProxySettingsService` 的指纹/处理器缓存——并发形态，`AUD-ARCH-007` 已书面接受「批次中途代理变更代价一轮重试」，不得改为租约引用计数而不重开该裁定。
- `CrossProcessLaunchSignal` 的 Unix socket 绑定/探测/重试循环——注释把每个分支绑到一个具体竞态，拆开会把不变量藏起来。
- `DirectorySizeProbe` 与 `DirectoryTreeDeleter` 对 reparse point 的处理不同（一个跳过链接、一个按链接删除）——正是 ADR-030 的要点，各有测试。
- 三个功能级的 `catch (OperationCanceledException) when (ct.IsCancellationRequested)` 折叠——该过滤器携带编译器无法推断的 token，全仓一致的习语。
- `ModalHostViewModel` 的七个 `Is*Interactive` 与九个 `OnPropertyChanged`——编译绑定需要具名属性，`UiStyleContractTests` 钉住叠层集合，且给对话框层加闸口被明令禁止（那是评审候选 08 的议题，需先重开 AGENTS.md 的模态隔离裁决）。
- `DialogsViewModel` 的八个确认字段与设置向导的五个单选 helper——XAML 按名寻址。
- `GameOperationPolicy` 的 `Decide` / 停止意图翻译 / 「游戏在跑」双闸门——ADR-026/027/032 的裁定区，本计划一字不动。
- `Views/*.axaml.cs` 里 11 个只调 `InitializeComponent` 的文件——编译 XAML 的 `x:Class` 要求。
- `MainWindow.axaml.cs:394-459` 的窗口 chrome/拖拽/指针管道——全仓只有一份（`CrashReportWindow` 用系统装饰），抽成可复用行为只会有一个调用者。
- 空标记类（`Button.news-row`、`Grid.operation-layout` 等 7 个）——无 setter 是设计，它们是无头契约的查询钩子。
- 设计画廊的状态矩阵（含 5 个相同填充格）——矩阵本身就是产物，且 `UiStyleContractTests.Tokens.cs:470-482` 钉住其格数与各态 `IsEnabled`。
- `Launcher.StateLayer.*`/`Elevation.*`/`Typography.LetterSpacing.*` 等「标记中未引用」的 token——它们是被 `UiStyleContractTests.DesignTokens.cs` 与 `DesignTokenContrastTests` 钉住、并被设计画廊在运行时枚举的**已声明设计系统表面**，不是死代码。
- `Models/ManifestFile` 的可空归一访问器——官方线协议字段顺序契约。

---

## 5. 开放决策

以下三项需要人裁或需先重开既有裁定。**未裁决前不要动**。

### 决策一：`ShellLifecycle` 的订阅收敛形态（`D15` / 评审候选 07）

扫描的独立结论与候选卡**不同**，值得先裁形态再落地：

- 候选 07 提议「一张声明表，Wire 与 Unwire 共同遍历」。
- 扫描认为：源与事件元数异质（`Action`、`Func<Task>`、`Func<T,Task>`），且中间夹着模态注册块，因此 `record Subscription` 表**会比它替换的代码更长**；同样能达成「对称性是数据性质」的更小形态是 `Attach(Action attach, Action detach)` 累积 `detachAll`，`Unwire` 逆序执行——它额外删掉 4 个仅为身份比较而存在的 `Func` 字段。

**建议**：采纳 `Attach` 形态，在 `AUD-ARCH-005` 的条目上把候选 07 的「声明表」改写为「记录式拆卸」。两者都能消除漏配对称，但后者不动 30 个协作者的字段布局。落地时补一条「`Unwire()`/`Dispose()` 之后任何 shell 处理器都不再运行」的用例（把 `ShellLifecycleTests.cs:263` 的单条断言扩到 `dialogs.CloseRequested`、`debug.RefreshRequested`、`operations.OpenLogViewerRequested`）。

### 决策二：主题引擎是否从 `SettingsAppearanceViewModel` 拆出（`D14`） —— **已裁定：独立拆出（`8aab531`）**

**裁定**：`D14` 单独推进，形态为「sealed 具体类 + DI 单例 + **无接口**」，不并入评审候选 11/12。

**不合并的理由（读码复核后推翻了本节原议）**：候选 11 是 `ISettingsEditor`／`IShellRuntime`／
`LocalDiagnostics` 三个「单适配器零替身」接缝的降级，候选 12 是诊断注册的单一所有方——两者都落在
**日志**这条线上；`D14` 落在**外观资源**这条线上，引擎里没有任何失败路径要记日志（`ApplyScheme`
全程无 try/catch，`SetBrush` 不抛），两边唯一的接触点是 `SettingsAppearanceViewModel` 里那一次
`LocalDiagnostics.LogSync("ThemeColor", …)`。因此原议担心的「VM 拆出去了但静态注册还在测试提供者里」
这种半迁移态并不存在——那是另一个全局量的议题，合并只会让 `D14` 无限期等一个与它无共同机制的裁决。

**不加接口的理由**：单适配器、零替身的接口正是候选 11 要拆掉的那种假接缝（ADR-021 的标准），而
仓库既有先例 `WindowsAnimationSettingsProvider` 同样是「DI 登记的 sealed 具体类，消费方直接持有」。

**与 `DEF-4` 的顺序**：`DEF-4` 的唯一生产写入点已随本次搬移移到 `Services/ThemeApplier`，故 `D14`
先落、`DEF-4` 的快照设施随后对着 applier 建；反过来会让 `DEF-4` 的锚点先失效一次。

**与既有裁定的关系**：`AUD-MAINT-001`（方案缓存由静态改实例）不受影响——缓存仍居实例，只换了宿主；
`AUD-TEST-007` 的哨兵守卫仍成立，并已随本次改动重新做变异验证（拆退订即红）。

### 决策三：`D1`（卸载只读文件）的性质与发布口径 —— **已判定：既有缺陷**

已核实：`git grep "Already gone" v1.1.0-beta.9 -- "*GameUninstallService.cs"` 命中，且
`v1.1.0-beta.10`（2026-09-16 打标签，是 HEAD 的祖先）同样含该循环——**该缺陷已随 beta.9 与
beta.10 出货**。故按 `AGENTS.md` 的 release notes 规则，下一版需要一条面向用户的 `fix` 条目
（「卸载遇到只读文件不再整体失败」）。

本轮**不改** `CHANGELOG_RELEASE.md`：它当前是 `## v1.1.0-beta.10` 的单版本文档，而 beta.10 已
打标签，本修复不在其中；该文档在准备下一版时整体替换。

---

## 6. 门禁与验证协议

沿用仓库既有纪律（`AGENTS.md`、`PROJECT_CONVENTIONS.md` §9），额外强调本计划特有的三条：

1. **逐项提交、逐项跑门禁**。每项一个 `refactor:`/`fix:`/`test:` 提交，提交信息写明「改了什么 + 等价性依据」。不要按阶段打包。
2. **守卫先行的项要变异验证**。凡「新增/改写守卫」的项（`DEF-1`、`DEF-2`、`A1`、`A10`、`D3`、`D13`），落地后把守卫拆掉确认对应用例变红，再还原——这是本仓库既有的做法，也是防止「纸面守卫」的唯一手段。
3. **纯重构不重生 golden**。`D13`/`C8`/`D7` 明确标注了像素影响；`D7` 会动用户可见文案（需同步四语 `.resx` 与 golden），其余应为零像素。任何一次重构后若 golden 变红，先怀疑重构本身而不是重生基线。
4. **测试侧改动必须报「改造前后 TRX 逐条对照」**：通过集与可见跳过数必须逐条一致，不能只看总数。
5. **XAML/样式改动后跑 `.\dev.ps1 ui`**；`.resx` 改动后跑 `.\scripts\Test-LocalizationContract.ps1`；依赖无关项无需碰 `packages.lock.json`。
6. 每阶段收口跑 `.\verify.ps1`（Debug 构建 + 覆盖率 + Release 构建），并记录覆盖率行/分支余量。覆盖率不得倒退——删除死代码会小幅降低绝对覆盖，需在提交信息里说明是分母变化而非回归。

---

## 7. 落地顺序与规模

| 批次 | 项数 | 规模 | 前置 | 预期效果 |
| --- | --- | --- | --- | --- |
| §2 缺陷 | 6 | S–M | 无 | 修掉唯一有用户可见后果的项（`DEF-1`）+ 堵一个已实际漂移的守卫（`DEF-2`）+ 消除环境相关的红（`DEF-3`） |
| A | 13 | S–L | `A10` 与 `DEF-3` 同一改动 | 测试侧减少约 400–500 行重复；`%TEMP%` 泄漏归零；等待原语收敛到一处 |
| B | 22 | S–M | 无（与 A 可并行） | 生产侧减少约 250 行；`B1`/`B2`/`B4` 是真正的杠杆项 |
| C | 8 | S | `C1` 需先 grep 字符串键 | 净删约 120 行，其中 `C2` 的 23 处是纯噪声 |
| D | 17 | M–L | 决策一/二/三 | 结构收益最大、风险最高；`D1`/`D3`/`D5` 相对独立可先走 |
| E | 9 | — | — | 只登记 |

**建议的首轮切片**：`DEF-2` + `A1` + `A10` + `B1` + `DEF-1` 五项。理由是它们覆盖了三种不同性质（守卫漂移、设施未复用、测试环境依赖、生产重复、用户可见缺陷），互不冲突，每项都能独立验证等价性，且改完之后本计划的其余部分会更快更安全——尤其是 `A1` 与 `A10`，它们让后续所有测试改动不再各自重造脚手架。
