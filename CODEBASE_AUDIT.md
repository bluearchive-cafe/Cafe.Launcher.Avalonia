# 仓库审计报告（当前状态）

- 审计日期：2026-09-08（delta 增量复审；同日早先为 full 全量审计）
- HEAD：`b1f62fa`（工作树含本轮修复，待提交）；增量范围 `6bd2a8f..b1f62fa`（4 提交，12 文件，+390/−48）
- 审计方式：repository-audit 流程（delta 模式：增量提交逐条核验 + 未决发现复核 + 门禁实跑 + CI 实证 + 新守卫两向实测）
- 历史报告：`.repository-audit/history/`（最近：2026-09-08 delta / 2026-09-08 full）

## 当前结论

**无 Critical/High/Medium open 项。** 上一轮（2026-09-08 full）4 项 Low 中 3 项核销（AUD-CI-005 / AUD-MTN-007 / AUD-PERF-007），1 项因修复不完整重新打开并补齐（AUD-MTN-008）；本轮新增 1 项 Low（AUD-MTN-009）。两项修复 + 2 条守卫已落地（工作树待提交）。维持 4 项有意暂缓/接受项。全量门禁本机实跑通过（单元 1450 过/2 跳 + Headless 164）。

## Open 项（4 项，全部 Low / 有意）

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer（`RemoteContentViewModel.cs:27,330`） |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分 |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；调用面收窄至 3 个主题类，年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` ≥800ms 下限断言（墙钟依赖但非 flaky 失败） |

## 本轮增量核验（6bd2a8f..b1f62fa，4 提交）

| 提交 | 结论 |
|---|---|
| ce9c98c fix: build.yml 最小权限 + §12 版本对齐 | 核销 AUD-CI-005（顶层 `permissions: contents: read`，与 release.yml 同口径）与 AUD-MTN-007（§12 与 `Directory.Packages.props` 逐行一致） |
| f2430b8 fix: JSON 上限 + Inno 文档 | 核销 AUD-PERF-007（64MiB 上限 + 2 守卫测试）；Inno 文档只改 4/6 处 → AUD-MTN-008 重新打开；常量摘要挤掉方法文档 → AUD-MTN-009 |
| be90631 / b1f62fa | 审计记录与台账，无代码影响 |

## 本轮新增 / 重新打开并已清理（2 项 Low）

- **AUD-MTN-008**（重新打开 → 已补齐）：`PROJECT_CONVENTIONS.md:230` 与 `.repository-audit/repository-map.md:38` 仍写 Inno Setup `6.3+`，而 `scripts/New-WindowsInstaller.ps1:124-126` 强制 `7.0`、README/AGENTS/CLAUDE 已统一——修复面 4/6。已补齐 2 处并新增守卫。
- **AUD-MTN-009**（新）：f2430b8 把常量摘要块插在方法原摘要与方法声明之间，使公共 `DeserializeJsonAsync` 丢失 XML 文档、常量带两个 `summary`。已重排修复。

## 关键领域核验（本轮实查）

### 下载完整性 / 文件系统安全（critical）

- 本轮增量未触碰下载/校验/路径校验代码；`DownloadExecutor`、`GamePathValidator`、CRC64 复用等维持上一轮核验结论（哈希强制 + 穿越/符号链接防护 + 原子替换）。

### 供应链 / CI（critical）

- `build.yml:9-10` 顶层 `permissions: contents: read`（本轮新增，与 release.yml 同口径）；17 处 `uses:` 仍全部 40 位 commit SHA 固定；依赖锁定（`RestoreLockedMode` + lock 提交）维持。
- CI 实证：run `34185246464`（head be90631，含 ce9c98c）success；run `34214171407`（head b1f62fa，含 f2430b8）success——两个 fix 提交均经 CI 验证。
- 发布状态：`v1.1.0-beta.7` 仍为最新 tag（指向 `6bd2a8f`），六资产齐全、prerelease=true；发布后 4 提交为审计整改与台账，无版本变更。

### 响应体上限（AUD-PERF-007 修复核验）

- `MaxBufferedJsonBytes = 64MiB`（`RemoteHttpRequestService.cs:75-81`）：`Content-Length` 预检（`:107-110`）+ 无长度时 64KiB 分块流式累计钳制（`:117-135`），超限抛带 url/status/content-type 上下文的 `HttpRequestException`；公共重载委托 internal 重载供测试注入小上限。
- 边界：`buffer.Length + read > maxBytes` 保证缓冲不超上限、等值放行、chunked 路径由流式分支覆盖；`LauncherApiClientTests` 新增 2 例（声明超长预检 / `RepeatingStream` 无长度流式累计）覆盖两条路径。

### 架构 / 可维护性

- Feature 边界与根 `ViewModels/` modal 契约维持（增量未触碰）。本轮复核 `RemoteContentViewModel` 位于根 `ViewModels/`：由 `MainWindow.axaml` 直接绑定且为 `MainWindowViewModel` 属性，属窗口级呈现，与 AGENTS.md 约定一致——**不记发现**。
- 组合根纪律、无 service locator 等维持。

### 文档一致性（本轮新增守卫面）

- 版本事实的「脚本 / props / 文档」三处来源已用 2 条契约测试钉住（见下），同类漂移不再依赖人工复查。

## Verified Strengths

- 全量门禁 `verify.ps1` 本机实跑通过：Debug 0 警告 0 错误；合并覆盖率 行 86.05%（三次实跑 86.03%–86.12%）/ 分支 92.61%（高于棘轮 84.30% / 88.99%）；Release win-x64 0 警告 0 错误；Resx 契约 18/18；单元 1450 过/2 跳、Headless 164/164。
- 两个 fix 提交均落在 CI push run 内且 success；`v1.1.0-beta.7` 六资产与 tag 状态复核无变化。
- 新增守卫两向实测（引入漂移即失败、修复后通过），把「逐处改文案」升级为「单一真值 + 自动比对」。

## Decisions Required

- 无阻塞项。

## Resolved / Superseded Since Previous Audit

- AUD-CI-005 → `ce9c98c`；AUD-MTN-007 → `ce9c98c`；AUD-PERF-007 → `f2430b8`。
- AUD-MTN-008 → 上一轮修复不完整，本轮重新打开并补齐（工作树待提交）。
- AUD-MTN-009 → 本轮新增并修复（工作树待提交）。

## Automated Guards Added

- `InstallerContractTests.CurrentStateDocs_NeverDeclareAnInnoSetupVersionBelowTheEnforcedMinimum`：从 `New-WindowsInstaller.ps1` 提取 `[version]"X.Y"` 单一真值，断言 README/AGENTS/CLAUDE/PROJECT_CONVENTIONS 中任何「Inno Setup + 版本」声明不低于它（历史报告目录有意排除）。
- `InstallerContractTests.ProjectConventionsToolchainTable_MatchesDeclaredPackageVersions`：解析 `Directory.Packages.props`，逐行比对 §12 工具链表；`checkedRows >= 15` 防真空通过。
- 维持既有守卫：本地化合约（verify 最前 fail-fast）、lock 锁定模式、CI 最小权限 + SHA 固定、发布横幅硬门禁等。

## Recommended Priorities

1. 提交本轮修复（§12 一行 + 常量摘要重排 + 2 条守卫）并推送，确认 CI 绿灯。
2. 维持 4 项有意暂缓/接受项到下一全量或年度审。

---

### 审计方法说明

- 模式：delta。风险画像按 `desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）；按模式约定跳过未受影响领域的深审，未重证健康区。
- 门禁实证：`verify.ps1` 本机实跑两轮（HEAD 干净树 + 本轮修复后），均 exit 0；同一最终代码两次独立覆盖率实跑为 86.05% / 86.03%（差 3 行、约 0.02pp），连同修复前一轮 86.12% 共三点，均远高于棘轮基线，且未伴随任何测试失败——记录为覆盖数值的观测抖动（异步/计时路径的命中差异），不单独记发现。第三轮 `coverage.ps1` 曾触发一次 coverlet 空壳报告并按其内建重试恢复（脚本已注释该已知竞态），属环境性、非仓库缺陷。
- 守卫验证：两条新守卫均做两向实测（人为引入 6.3 / 4.3.0 漂移 → 失败；还原 → 通过）。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`（本增量未触碰；有效性由 beta.7 六资产与 CI 绿灯背书）；未执行真实外网更新下载全链路。
- 工具写入的工作树文件为 LF（`.editorconfig` 要求 CRLF）；`core.autocrlf=true` 归一化后提交内容零差异，属工具性、无仓库影响。
