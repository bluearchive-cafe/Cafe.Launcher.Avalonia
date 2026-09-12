# 2026-09-08 Delta 审计报告（整改落地复审）

- 审计日期：2026-09-08
- 范围：`6bd2a8f..b1f62fa`（4 提交，12 文件，+390/−48）——即 2026-09-08 full 全量审计 4 项发现的整改落地
- 模式：delta（基线 `6bd2a8f`；基线报告 `.repository-audit/history/2026-09-08-full-audit.md`）
- 风险画像：desktop-launcher（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）
- 门禁实证：`verify.ps1` 本机实跑（HEAD 干净树 + 本轮修复后各一轮），两轮均通过
- CI 实证：两个 fix 提交分别落在两次 push 的 head run 内，均 success（`gh run list` / `gh run view`）

## 当日结论

**无 Critical/High/Medium open 项。** 上轮 4 项 Low 中 3 项核销（AUD-CI-005 / AUD-MTN-007 / AUD-PERF-007），1 项修复不完整被重新打开并补齐（AUD-MTN-008）；本轮新增 1 项 Low（AUD-MTN-009，fix 提交引入的 XML 文档注释孤儿化）。两项修复与 2 条守卫已落地（工作树待提交）。维持 4 项有意暂缓/接受项。

## 增量核验（6bd2a8f..b1f62fa，4 提交）

| 提交 | 内容 | 核验结论 |
|---|---|---|
| ce9c98c | `build.yml` 补 `permissions: contents: read`；PROJECT_CONVENTIONS §12 版本钉为 props 实际值 | 正确：权限块位于顶层、与 release.yml 同口径；§12 各行逐一比对 `Directory.Packages.props` 全部一致 |
| f2430b8 | `DeserializeJsonAsync` 加 64MiB 上限；README/AGENTS/CLAUDE 统一 Inno Setup 7.0+ | 主体正确（见下两节），但 Inno 文档修复漏了 2 处（AUD-MTN-008），且常量摘要块挤掉了方法原有 XML 文档（AUD-MTN-009） |
| be90631 / b1f62fa | 审计记录与台账 | 纯文档，无代码影响 |

## AUD-PERF-007 核验（响应体上限）

- 实现：`MaxBufferedJsonBytes = 64MiB`（`RemoteHttpRequestService.cs:75-81`）；`Content-Length` 预检（`:107-110`）+ 无长度时 64KiB 分块流式累计钳制（`:117-135`），超限抛带 url/status/content-type 上下文的 `HttpRequestException`；公共重载委托到 internal 重载（供测试注入小上限）。
- 边界正确性：`buffer.Length + read > maxBytes` 保证缓冲量不超过上限；等值放行；chunked（无 Content-Length）路径由流式分支覆盖，与预检互不重叠。
- 测试：`LauncherApiClientTests` 新增 2 例——声明超长 Content-Length 预检（断言 `exceeds`/`url:`/`status: 200`）、`RepeatingStream` 无长度流式累计（断言 `buffered: 4096`）；既有解析用例继续覆盖正常路径。
- 结论：**核销**。副作用见 AUD-MTN-009。

## AUD-CI-005 / AUD-MTN-007 核验

- `build.yml:9-10` 顶层 `permissions: contents: read`；release.yml 未受影响（发布 job 仍按需 `contents: write`）；HEAD CI run success。
- §12 表与 `Directory.Packages.props` 逐一比对一致（Avalonia 12.1.2 / MEDI 10.0.11 / Serilog 4.4.0·2.1.0·7.0.0 / Material.Icons 3.0.2 / Shirasagi 0.2.0 / xunit.v3 3.2.2 / runner.visualstudio 3.1.5 / Test.Sdk 18.9.0 / coverlet 10.0.1 / CommunityToolkit.Mvvm 8.4.2）。唯一漏网：Inno Setup 行（→ AUD-MTN-008）。
- 结论：**核销**。

## 新发现 / 重新打开

### AUD-MTN-008（重新打开 → 已补齐，Low）— Inno Setup 版本文档修复不完整

- 证据：`PROJECT_CONVENTIONS.md:230` 仍写 `6.3+`、`.repository-audit/repository-map.md:38` 仍写 `6.3+`；而 `scripts/New-WindowsInstaller.ps1:124-126` 强制 `[version]"7.0"`，README/AGENTS/CLAUDE 已统一为 7.0+。修复面 4/6。
- 影响：PROJECT_CONVENTIONS 是仓库规则 #1（证据优先级最高）；§12 恰是上一轮 AUD-MTN-007 刚修过的同一张表，同行漏改——说明「逐处修文案」不是可持续的修复方式。
- 处置：Fix（本轮改 §12:230 与 repository-map:38）+ Add Guard。
- 建议验证：**Experimentally Verified**（守卫在 6.3 下失败、在 7.0 下通过，两向实跑）。

### AUD-MTN-009（新，Low）— f2430b8 使 `DeserializeJsonAsync` 丢失 XML 文档

- 证据：常量摘要块被插在方法原摘要与方法声明之间，形成连续两个 `///` 文档块；C# 把整块文档挂到最近的声明（常量）上，于是公共方法变无文档、常量带两个 `<summary>`。
- 影响：公共 API 文档丢失；仓库未开 `GenerateDocumentationFile`，编译器不告警，CI 无从拦截（属评审/审计面）。
- 处置：Fix（本轮重排：常量摘要随常量、方法摘要随方法，文本不变）。
- 建议验证：Verified（纯注释重排，Debug/Release 构建 0 警告）。

## 新增守卫（本轮）

- `InstallerContractTests.CurrentStateDocs_NeverDeclareAnInnoSetupVersionBelowTheEnforcedMinimum`：从脚本提取 `[version]"X.Y"` 单一真值，断言 README/AGENTS/CLAUDE/PROJECT_CONVENTIONS 中任何「Inno Setup + 版本」声明不低于该值（`.repository-audit/history/` 下的历史报告为快照，有意排除）。两向实测。
- `InstallerContractTests.ProjectConventionsToolchainTable_MatchesDeclaredPackageVersions`：解析 `Directory.Packages.props` 的 `PackageVersion`，逐行比对 §12 表（Avalonia/Avalonia.Desktop 斜杠行拆分为两个包）；`checkedRows >= 15` 防表格删空后的真空通过。两向实测。

## 维持开放的基线项（均有意暂缓/已文档化，本轮复核无变化）

- AUD-ARCH-003：`RemoteContentViewModel.cs:27,330` 仍直接持有 `DispatcherTimer`——deferred。
- AUD-MTN-001：`RemoteContentViewModel`（714 行）拆分——deferred。
- AUD-DEP-002：Shirasagi0012.MaterialColorUtilities bus factor 1——accepted-risk（调用面仍为 3 个主题类：`MaterialColorMapper` / `MaterialSchemeGenerator` / `ThemeColorExtractionService`）。
- AUD-TST-001：`GameDownloadServiceTests.cs:1092-1100` 真实限速 + `Stopwatch` ≥800ms 下限断言——deferred / No Action。

## 门禁与 CI 实证

- `verify.ps1`（HEAD `b1f62fa` 干净树）：Debug 0 警告 0 错误；覆盖率 行 86.12% / 分支 92.61%（高于棘轮 84.30% / 88.99%）；Headless 164/164；Release win-x64 0 警告 0 错误；Resx 契约 18/18。
- `verify.ps1`（本轮修复后）：同口径通过——单元 1450 过/2 跳、Headless 164/164、Resx 18/18、Release win-x64 0/0；合并覆盖率 行 86.05% / 分支 92.61%。另以 `coverage.ps1` 单独复跑一次得 行 86.03% / 分支 92.61%（同代码两次差 3 行、约 0.02pp；该轮触发一次 coverlet 空壳报告并经脚本内建重试恢复）。三点均远高于棘轮基线且无测试失败，记为观测抖动，不单独记发现。
- CI：run `34185246464`（head be90631，含 ce9c98c）success；run `34214171407`（head b1f62fa，含 f2430b8）success——两个 fix 提交均经 CI 验证。

## 建议优先级

1. 提交本轮修复（§12 一行 + 常量摘要重排 + 2 条守卫）并推送，确认 CI 绿灯。
2. 维持 4 项有意暂缓/接受项到下一全量或年度审。

## 审计方法与局限

- delta 模式：只复核增量 4 提交、4 项未决发现与前轮修复面；未重跑未受影响领域的深审（按模式约定跳过）。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`（本增量未触碰这两个脚本；其有效性由 beta.7 六资产与 CI 绿灯背书）。
- 工具写入的工作树文件为 LF（`.editorconfig` 要求 CRLF）；`core.autocrlf=true` 归一化后提交内容零差异，属工具性、无仓库影响。
- advisory（无行动）：§12 未列 `Avalonia.Controls.ColorPicker` 与 `AvaloniaUI.DiagnosticsSupport`——该表为精选工具链表且已注明「版本以 `Directory.Packages.props` 为准」，不单独记发现。
