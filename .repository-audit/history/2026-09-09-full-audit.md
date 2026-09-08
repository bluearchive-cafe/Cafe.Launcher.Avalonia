# 仓库审计报告 — 2026-09-09（full 全量）

## Audit Metadata

- 日期：2026-09-09
- Commit：`f6d44a7`（工作树干净；审计结束时 `packages.lock.json` 的 RID 段改写已 `git restore` 还原）
- 模式：full（全量；上一轮 2026-09-08 delta，基线 `b1f62fa`）
- 范围：全部 6 个领域（架构 / 安全 / 依赖 / 测试 / 性能 / 可维护性），深度优先落在增量特性「不可恢复崩溃两级兜底」
- 风险画像：`desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical；cross_platform_behavior / testing_determinism / ui_thread_performance / persistence_and_migration / architecture_boundaries = high）

## Executive Summary

**无 Critical / High / Medium open 项。** 增量 `b1f62fa..f6d44a7`（3 提交）以上一轮整改收尾 + 新增崩溃诊断特性为主；上一轮 2 项 Low（AUD-MTN-008 / AUD-MTN-009）已随 `3dee04a` 核销，本轮新增 2 项 Low（AUD-REL-005 / AUD-MTN-010，均于同日修复并各带守卫）。全量门禁 `verify.ps1` 本机实跑通过（Debug 0 警告 0 错误；单元 1483 过/2 跳；Headless 167/167；合并覆盖率 行 85.24% / 分支 92.13%）。

Open 项（6 项：2 项本轮新增 Low（同日已修复）+ 4 项维持暂缓/接受）：

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-REL-005 | Low | 已修复 | 隔离报告模式对 JSON-null 的 `UiCulture` 不设防 → 未捕获 `ArgumentNullException`，报告进程无界面退出（已三向实测复现） |
| AUD-MTN-010 | Low | 已修复 | `design-system-spec.md` §10 仍写「5 个基线」并漏列崩溃窗口基线（实际 6 个） |
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分 |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` 下限断言 |

Most important actions：

1. `AUD-REL-005`：`ApplyReportCulture` 加 null/空白守卫；把快照解析与文化应用抽成可测静态助手并补测试（该链路合并覆盖率 0%）。
2. `AUD-MTN-010`：把 §10 的基线句改为「6 个」并补「崩溃窗口」。
3. 维持 4 项有意暂缓/接受项至下一轮或年度审。

## Changes Since Previous Audit

`b1f62fa..f6d44a7`，3 提交、45 文件（+2743/−135）：

| 提交 | 内容 | 审计结论 |
|---|---|---|
| `3dee04a` fix: 补齐 Inno Setup 7.0 文档漂移并重排 JSON 缓冲常量摘要 | 上轮工作树待提交的修复 | 核销 AUD-MTN-008（§12 一行）与 AUD-MTN-009（常量/方法摘要重排）；两处均已复核为最终形态 |
| `42175e9` docs(audit): 记录 2026-09-08 delta 复审并更新台账 | 审计台账 | 无代码影响 |
| `f6d44a7` feat(diagnostics): 不可恢复崩溃窗口与两级兜底 | 新特性：`IFatalCrashService` 两级兜底 + 独立 `CrashReportApp` + `CrashReportWindow` + 快照存储 + 调试面板模拟入口 + ADR-019/020 + 原型 | 见下；架构/安全/本地化/测试均核验，新增 2 项 Low |

## 关键领域核验

### 崩溃诊断特性（本轮新增，重点）

**分层与来源映射（架构）**：tier-1（同进程窗口）入口只有 `IFatalCrashService.HandleFatalCrash`，tier-2（隔离报告进程）由 `HandleUnhandledCrash` 承担；来源是封闭枚举 `CrashOrigin`（5 值），快照与日志只写映射后的固定标签，不接受调用方自由文本。生产故障全部走 tier-2（`AppDomain` / `Dispatcher` / 入口逃逸 / 诊断初始化失败），tier-1 当前仅由调试面板行使——ADR-019 已显式记录该决策（Release 下调试入口不可达），**不记发现**。

**文件系统与隐私（critical 域）**：

- 快照只写诊断所需字段（异常、版本、构建 SHA、OS、UI 语言）；`Sanitize` 把用户目录替换为 `%USERPROFILE%`；不采集环境变量或命令行。
- 主目录不可写降级到临时目录；两处都失败时仍以不带路径的 `--crash-report` 启动报告进程（单元测试覆盖）。保留最近 10 份 / 最长 30 天，清理覆盖主目录与降级目录。
- 进程启动用 `ProcessStartInfo.ArgumentList`（结构化参数、`UseShellExecute=false`、`FileName` 取 `Environment.ProcessPath`），无 shell 拼接。
- 未发现新增的符号链接/路径穿越面：快照文件名含时间戳 + 2 字节随机十六进制，非固定名可预置形态。

**启动路径变更（可靠性）**：`UnifiedLogger` 创建点从单实例互斥之后前移到之前（崩溃处理需覆盖整个进程生命周期）。核验第二实例不再受影响的依据：Serilog 文件 sink 配置 `shared: true`（`FileShare.ReadWrite`），第二实例开日志不会失败；日志写入仍只在 `RunSession` 内发生。

**本地化**：18 个新键在 en/zh-Hans/zh-Hant/ja 四档齐备、排序正确、占位符一致（`ResxResourceContractTests` 键数 514→532 同步），`LauncherStrings.Designer.cs` 与 `LocalizationKeys.cs` 已再生；崩溃窗口 VM 直接走 `LauncherStrings.ResourceManager` + `LocalizationKeys` 常量（隔离进程无 DI，ADR-019 说明），未出现裸 key。

**设计系统**：ADR-020 显式豁免崩溃窗口（自带 `Crash.*` 令牌、不引用 `Launcher.*`）；实测 `Crash.*` 仅出现在 `Views/CrashReportWindow.axaml`，无外溢；`UiStyleContractTests` 的显式清单不含该窗口（与 ADR 描述一致）。

### 隔离报告模式（AUD-REL-005，已复现）

三向实测（Debug 构建 + `--crash-report <json>`，每例观察 6 秒）：

| 快照输入 | 结果 |
|---|---|
| `"UiCulture": null` | 进程 6 秒内自行退出（exit 1），**无任何窗口** |
| `"UiCulture": "en"` | 窗口保持存活（对照组，证伪「参数/文件形态本身不work」） |
| `"UiCulture": "xx-INVALID"` | 窗口保持存活（`CultureNotFoundException` 被捕获，隔离出 null 与非法值的差别） |
| `OccurredAt: null` / `Id: null` / 缺 required 成员 / 非法 JSON | 均落到「快照不可读」报告并显示窗口（设计的兜底面有效） |

根因：`System.Text.Json` 默认不校验非空注解，`"UiCulture": null` 被赋给 `string` 属性；`CultureInfo.GetCultureInfo(null)` 抛 `ArgumentNullException`，`ApplyReportCulture` 只捕获 `CultureNotFoundException`，异常逃出 `OnFrameworkInitializationCompleted` 后被 `RunCrashReporter` 吞掉。

### 供应链 / CI（critical 域）

- 增量未触碰 `Directory.Packages.props` / `packages.lock.json` / `.github/`；17 处 `uses:` 仍全部 40 位 SHA 固定；`RestoreLockedMode: 'true'` 与 lock 提交维持；build.yml 顶层 `permissions: contents: read` 维持。
- CI 实证：HEAD `f6d44a7` push run `34249293770` success（6m58s）。PR 期内 run `34246110668` 曾失败：新增崩溃窗口黄金截图失配 1.03% > 1% 容差，随后 `7bf3307`（固定本地墙钟时间）修复并连续三次 CI 通过——**属合并前已解决的时区漂移**，当前基线锚定 runner 本地偏移，不再随机器时区漂移。

### 架构 / 可维护性

- 新代码落位符合 AGENTS.md：共享诊断设施在 `Services/Diagnostics/`，窗口级 VM 在根 `ViewModels/`，调试入口在 `Features/Diagnostics/`，跨特性仅依赖 `IFatalCrashService` 抽象并由组合根绑定（`existingFatalCrashService` 复用 pre-DI 实例，全进程单例）。
- 未发现新增跨 Feature 具体类型引用；`ServiceConfiguration` 仍为唯一组合根。
- 文档漂移 1 处（AUD-MTN-010）。

### 测试

- 新增 24 个单元用例 + 2 个 Headless 用例（1476 / 166），覆盖快照读写与净化、降级目录、去重与旁路日志、退出码、来源标签、窗口布局与黄金截图。
- 黄金截图确定性：崩溃窗口用例固定墙钟时间 + en-US 文化 + 固定构建 SHA，跨时区/跨提交稳定（CI 三次通过为证）。
- 覆盖缺口：`CrashReporterLauncher.cs` 0/22、`CrashReportApp.axaml.cs` 0/38、`Views/CrashReportWindow.axaml.cs` 12/50（后两者合计 88 行是特性中唯一零覆盖链路）——AUD-REL-005 的缺陷正落在其中。

## Critical Issues

无。

## High Priority Findings

无。

## Low Priority Findings

### AUD-REL-005 — 隔离报告模式对 JSON-null 的 UiCulture 不设防

- Category: reliability
- Severity: Low
- Confidence: 95
- Status: open
- Disposition: Fix

**Evidence**：`src/Cafe.Launcher.Avalonia/CrashReportApp.axaml.cs:34-48`；三向实测表见上（复现 = 证据层级 1）；`CrashReportStore.TryRead` 捕获 `JsonException`，因此字段值为 null 时反序列化成功、不落「快照不可读」分支。

**Impact**：崩溃报告进程是 tier-2 唯一的用户可见面。违反 ADR-019「保证任何一次不可恢复崩溃都有界面」的显式承诺；触发条件为快照文件被手工编辑或未来代码/数据变更写入 null（正常写入路径不会产生 null），故影响面窄。

**Recommendation**：`ApplyReportCulture` 增加 `string.IsNullOrWhiteSpace(cultureName)` 早返回；把「快照 → 报告 → 文化应用」抽为不依赖 Avalonia 生命周期的静态助手（如 `CrashReportBootstrap.Resolve(path)`），便于直接测试。

**Recommendation validation**：Verified（守卫点与异常类型均由实测确认；抽取助手为常规重构，无框架语义风险）。

**Suggested guard**：对该助手补 4 例：null 文化、非法文化、缺 required 成员、非法 JSON——同时把该链路从 0% 覆盖拉起来。

### AUD-MTN-010 — design-system-spec.md §10 基线数量与清单过期

- Category: maintainability/doc-drift
- Severity: Low
- Confidence: 95
- Status: open
- Disposition: Fix

**Evidence**：`docs/design/design-system-spec.md:194` 写「5 个基线（壳默认/进度面板/设置覆盖层/确认对话框/Toast）」；`tests/Cafe.Launcher.Avalonia.HeadlessTests/Baselines/` 现有 6 个 PNG（含 `crash-report-window.png`）。`design-walkthrough-checklist.md` §3.7 已正确记录崩溃窗口基线，属单点漂移。

**Impact**：维护者按 §10 核对基线集合时会认为崩溃窗口无黄金截图守护，或误判存在缺失基线。

**Recommendation**：改为「6 个基线（壳默认/进度面板/设置覆盖层/确认对话框/Toast/崩溃窗口）」。

**Recommendation validation**：Verified（文本改动，无歧义）。

**Suggested guard**：可选——断言 `Baselines/*.png` 与测试内 `GoldenScreenshot.Compare` 调用一一对应，顺带防孤儿基线。

## 修复落地（同日，`d9f2184` / `78c7d67`）

| ID | 修复 | 守卫 | 两向实测 |
|---|---|---|---|
| AUD-REL-005 | 新增 `Services/Diagnostics/CrashReportBootstrap.cs`（`Resolve`/`ApplyCulture`/`CreateUnreadableReport`，无 Avalonia/DI 依赖）；`ApplyCulture` 形参 `string?` + `string.IsNullOrWhiteSpace` 早返回（守卫成为编译期强制）；`CrashReportApp` 改为委托，不可测启动胶水 38 → 13 行 | `CrashReportBootstrapTests` 9 例（null/空白/非法/合法文化、快照缺失/畸形/持久化、null-UiCulture 回归） | 去掉守卫 → 3 例失败（`ArgumentNullException: Value cannot be null. (Parameter 'name')`）；恢复 → 9/9 通过 |
| AUD-MTN-010 | `design-system-spec.md:194` 改为「6 个基线（…/崩溃窗口）」 | `GoldenBaselineContractTests.Baselines_CommittedBaselinesAndGoldenComparisons_MatchOneToOne` | 放入 `orphan-probe.png` → 失败；移除 → 通过 |

修复后门禁：`verify.ps1` exit 0（Debug 0 警告 0 错误；单元 1483 过/2 跳；Headless 167/167；行 85.24% / 分支 92.13%；Release win-x64 0 警告 0 错误；Resx 18/18）。`CrashReportBootstrap.cs` 覆盖 28/28 = 100%。

## Architecture

见「关键领域核验」。无新增发现；`AUD-ARCH-001`（Shell 反向聚合）维持 architecture-decision（AGENTS.md 2026-09 裁决已认领），`AUD-ARCH-003` 维持 deferred。

## Security

信任边界：远端元数据、本地用户输入、崩溃快照文件、进程启动、CI/发布凭据。本轮新增面（快照文件读取 + 进程启动）核验通过：结构化参数、无 shell、路径来自自身 `Environment.ProcessPath`、快照字段最小化 + 用户目录净化。`--crash-report` 可读取任意本地 JSON 并渲染，属本地「打开文件」语义，无提权或代码执行面。无新增发现。

## Dependencies / Supply Chain

增量零依赖变更；锁定与最小权限维持（见「关键领域核验」）。`AUD-DEP-002` 维持 accepted-risk。

## Testing

门禁全绿（见 Verified Strengths）；新增用例确定性核验通过。覆盖缺口与建议见 AUD-REL-005。维持 `AUD-TST-001`（deferred / No Action）。

## Performance

崩溃路径为终止路径：快照同步写（必须在进程消亡前落盘，符合设计），保留策略在下次健康启动清理、目录不存在即早返回，未引入 UI 线程热路径。窗口渲染由 Headless 黄金截图守护。无新增发现。

## Maintainability / Technical Debt

新增 1 项 Low（AUD-MTN-010）。`RemoteContentViewModel` 两项维持 deferred。

## Decisions Required

无阻塞项。

## Resolved Findings

- `AUD-MTN-008` → `3dee04a`（PROJECT_CONVENTIONS §12 与 repository-map 两处 6.3+ → 7.0+；守卫 `CurrentStateDocs_NeverDeclareAnInnoSetupVersionBelowTheEnforcedMinimum` 已落地）
- `AUD-MTN-009` → `3dee04a`（常量摘要归位常量、方法摘要归位方法）

## Automated Guards Added

本轮未新增守卫；AUD-REL-005 的建议守卫（报告启动助手 4 例）待落地。维持既有守卫：本地化合约脚本（verify 首步 fail-fast）、`ResxResourceContractTests` 键数契约、lock 锁定模式、CI 最小权限 + SHA 固定、Inno 版本与 §12 工具链两条新契约、黄金截图阈值 diff。

## Verified Strengths

- `verify.ps1` 本机实跑通过（exit 0，审计时）：本地化合约通过；Debug 0 警告 0 错误；单元 1474 过 / 2 跳；Headless 166/166；合并覆盖率 行 85.02%（13662/16070）/ 分支 92.04%（2185/2374），高于棘轮 84.30% / 88.99%；Release win-x64 0 警告 0 错误；Release 下 `ResxResourceContractTests` 18/18。修复后复跑数值见「修复落地」。
- CI：HEAD `f6d44a7` push run success；PR 期唯一失败为黄金截图时区漂移，已在合并前修复并连续三次通过。
- 崩溃特性的设计记录完整：ADR-019/020 + CONTEXT.md 领域词条 + 走查清单 §3.7 + 原型保留，决策与实现一致。
- 新增本地化键四档齐备、生成物同步、契约计数更新。

## Recommended Priorities

1. 修 `AUD-REL-005`（null 守卫 + 抽助手 + 4 例测试），把隔离报告启动链从 0% 覆盖拉起。
2. 修 `AUD-MTN-010`（一行文案）。
3. 维持 4 项有意暂缓/接受项至下一轮或年度审。

## Audit Method and Limitations

- 模式 full；工具优先：`verify.ps1` 实跑、`coverage.ps1` 产物解析（按文件去重合并两份 cobertura）、`gh run` CI 实证、git 历史与差异逐条核验、对隔离报告模式做三向进程级实测。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`（本增量未触碰打包脚本；有效性由 beta.7 六资产与 CI 绿灯背书）；未执行真实外网更新下载全链路；未在非 Windows 平台实跑（平台行为由 CI 矩阵与既有测试背书）。
- 覆盖率数值为单次实跑；上轮记录的同代码重跑抖动（约 0.02pp）仍适用，不单独记发现。
- 审计过程运行 `verify.ps1` 触发的 `packages.lock.json` RID 段改写已 `git restore` 还原，工作树最终干净。
