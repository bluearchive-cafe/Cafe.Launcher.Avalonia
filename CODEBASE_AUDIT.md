# 仓库审计报告（当前状态）

- 审计日期：2026-09-09（full 全量审计；同日修复落地）
- 审计对象：`f6d44a7`（本轮修复 `d9f2184` / `78c7d67` 落在其后）；上轮基线 `b1f62fa`（2026-09-08 delta），增量 3 提交 / 45 文件 / +2743−135
- 审计方式：repository-audit 流程（full 模式：6 领域全过 + 增量特性深审 + 门禁实跑 + CI 实证 + 进程级实测复现）
- 历史报告：`.repository-audit/history/`（最近：2026-09-09 full / 2026-09-08 delta）

## 当前结论

**无 Critical/High/Medium open 项，本轮 2 项 Low 已修复。** 上轮 2 项 Low（AUD-MTN-008 / AUD-MTN-009）随 `3dee04a` 核销；本轮新增 2 项 Low（AUD-REL-005 / AUD-MTN-010）已在本轮修复（`d9f2184` / `78c7d67`）并各带一条守卫；维持 4 项有意暂缓/接受项。全量门禁本机实跑通过（Debug 0 警告 0 错误；单元 1483 过/2 跳；Headless 167/167；合并覆盖率 行 85.24% / 分支 92.13%）。

## Open 项（4 项，全部有意暂缓/接受）

| ID | 严重度 | 状态 | 摘要 |
|---|---|---|---|
| AUD-ARCH-003 | Low | deferred | RemoteContentViewModel 直接持有 DispatcherTimer |
| AUD-MTN-001 | Low | deferred | RemoteContentViewModel（714 行）拆分 |
| AUD-DEP-002 | Low | accepted-risk | Shirasagi0012.MaterialColorUtilities bus factor 1；年度重审 |
| AUD-TST-001 | Low | deferred / No Action | GameDownloadServiceTests 真实限速 + `Stopwatch` 下限断言 |

## 本轮修复（2 项 Low，均两向实测）

### AUD-REL-005 — 隔离报告模式对 JSON-null 的 UiCulture 不设防

- **缺陷**：`System.Text.Json` 默认不校验非空注解，快照里 `"UiCulture": null` 会抛未捕获的 `ArgumentNullException`（`ApplyReportCulture` 只捕获 `CultureNotFoundException`），`RunCrashReporter` 吞掉后进程 exit 1 且不显示任何窗口，违反 ADR-019「任何一次不可恢复崩溃都有界面」。
- **修复**：新增 `Services/Diagnostics/CrashReportBootstrap.cs`（`Resolve` / `ApplyCulture` / `CreateUnreadableReport`，不依赖 Avalonia 与 DI）；`ApplyCulture` 形参改 `string?` 并加 `string.IsNullOrWhiteSpace` 早返回（空值守卫因此成为编译期强制），`CrashReportApp` 改为委托该助手——不可测的启动胶水从 38 行降到 13 行。
- **守卫**：`CrashReportBootstrapTests` 9 例（null/空白/非法/合法文化、快照缺失/畸形/持久化、null-UiCulture 回归）。
- **两向实测**：去掉守卫后 3 例失败（`ArgumentNullException: Value cannot be null. (Parameter 'name')`），恢复后 9/9 通过。
- **覆盖**：`CrashReportBootstrap.cs` 28/28 = 100%；合并覆盖率随之回升（85.02% → 85.24% 行、92.04% → 92.13% 分支）。

### AUD-MTN-010 — design-system-spec.md §10 基线数量与清单过期

- **修复**：`docs/design/design-system-spec.md:194` 改为「6 个基线（壳默认/进度面板/设置覆盖层/确认对话框/Toast/崩溃窗口）」。
- **守卫**：`GoldenBaselineContractTests.Baselines_CommittedBaselinesAndGoldenComparisons_MatchOneToOne`（`Baselines/*.png` ↔ `GoldenScreenshot.Compare` 调用 1:1，顺带防孤儿基线）。
- **两向实测**：放入 `orphan-probe.png` 即失败（Collections differ at index 2），移除后通过。

## 增量核验（b1f62fa..f6d44a7，3 提交）

| 提交 | 结论 |
|---|---|
| `3dee04a` fix: 补齐 Inno Setup 7.0 文档漂移并重排 JSON 缓冲常量摘要 | 核销 AUD-MTN-008 / AUD-MTN-009 |
| `42175e9` docs(audit): 台账 | 无代码影响 |
| `f6d44a7` feat(diagnostics): 不可恢复崩溃窗口与两级兜底 | 架构/安全/本地化/测试均核验；新增 2 项 Low（本轮已修） |

## 关键领域核验（本轮实查）

### 崩溃诊断特性（新增，重点）

- **分层/来源**：tier-1（同进程窗口）入口仅 `HandleFatalCrash`，tier-2（隔离报告进程）由 `HandleUnhandledCrash` 承担；来源为封闭枚举 `CrashOrigin`，快照/日志只写固定标签。生产故障全走 tier-2，tier-1 当前仅调试面板行使——ADR-019 已显式决策，**不记发现**。
- **文件系统/隐私（critical）**：快照字段最小化 + 用户目录替换为 `%USERPROFILE%`；主目录不可写降级临时目录，两处皆失败仍以无路径 `--crash-report` 启动（有测试）；保留最近 10 份 / 30 天，主目录与降级目录同清。进程启动用 `ArgumentList` 结构化参数、`UseShellExecute=false`，无 shell 拼接。快照名含时间戳 + 2 字节随机十六进制，非可预置固定名。
- **启动路径变更**：`UnifiedLogger` 创建点前移到单实例互斥之前。核验第二实例不受影响：Serilog 文件 sink `shared: true`（`FileShare.ReadWrite`），且写入仍只在 `RunSession` 内发生。
- **本地化**：18 个新键四档齐备、排序与占位符一致（键数契约 514→532），Designer 与 `LocalizationKeys` 已再生；崩溃窗口 VM 用 `LocalizationKeys` 常量 + `ResourceManager`（隔离进程无 DI，ADR-019 说明），无裸 key。
- **设计系统**：ADR-020 显式豁免；实测 `Crash.*` 仅出现在 `Views/CrashReportWindow.axaml`，无外溢。

### 供应链 / CI（critical）

- 增量零依赖变更；17 处 `uses:` 全 40 位 SHA 固定；`RestoreLockedMode: 'true'` + lock 提交维持；build.yml 顶层 `permissions: contents: read` 维持。
- CI 实证：HEAD `f6d44a7` push run `34249293770` success。PR 期 run `34246110668` 失败于新增黄金截图失配 1.03% > 1%，`7bf3307` 固定本地墙钟时间后连续三次通过——**合并前已解决的时区漂移**，当前基线跨时区/跨提交稳定。

### 架构 / 可维护性

- 新代码落位符合 AGENTS.md：诊断设施在 `Services/Diagnostics/`，窗口级 VM 在根 `ViewModels/`，调试入口在 `Features/Diagnostics/`；跨特性仅依赖 `IFatalCrashService` 抽象，组合根复用 pre-DI 单例。无新增跨 Feature 具体类型引用。
- 本轮修复新增 `Services/Diagnostics/CrashReportBootstrap.cs`，同属共享诊断设施，未引入新的跨 Feature 依赖。

### 测试

- 增量新增 24 单元 + 2 Headless 用例；本轮修复再增 9 单元 + 1 Headless 守卫（1485 / 167）。
- 黄金截图固定墙钟/文化/构建 SHA，确定性已由三次 CI 通过背书。
- 剩余未覆盖：`CrashReporterLauncher.cs` 0/22（进程启动，按设计不测）、`CrashReportApp.axaml.cs` 13 行薄委托（需 Avalonia 生命周期）、`CrashReportWindow.axaml.cs` 24%（点击处理）。

## Verified Strengths

- `verify.ps1` 本机实跑通过（exit 0）：本地化合约通过；Debug 0 警告 0 错误；单元 1483 过 / 2 跳；Headless 167/167；合并覆盖率 行 85.24%（13701/16073）/ 分支 92.13%（2188/2375），高于棘轮 84.30% / 88.99%；Release win-x64 0 警告 0 错误；Release 下 Resx 契约 18/18。
- 崩溃特性的设计记录完整且与实现一致：ADR-019/020 + CONTEXT.md 词条 + 走查清单 §3.7 + 原型保留。
- 本轮 2 条新守卫均两向实测（引入缺陷即失败、修复后通过），同类漂移不再依赖人工复查。

## Decisions Required

- 无阻塞项。

## Resolved / Superseded Since Previous Audit

- AUD-MTN-008 → `3dee04a`；AUD-MTN-009 → `3dee04a`。
- AUD-REL-005 → `d9f2184`；AUD-MTN-010 → `78c7d67`。

## Recommended Priorities

1. 提交本轮修复（`CrashReportBootstrap` + 9 例测试、§10 一行、基线契约测试、审计产物）并推送，确认 CI 绿灯。
2. 维持 4 项有意暂缓/接受项至下一轮或年度审。

---

### 审计方法说明

- 模式：full。风险画像按 `desktop-launcher`（download_integrity / filesystem_safety / release_supply_chain / update_recovery = critical）。
- 门禁实证：`verify.ps1` 本机实跑 exit 0（审计时与修复后各一轮）；覆盖率按文件对 unit+headless 两份 cobertura 去重合并计算（合并值以 `coverage.ps1` 输出为准）。
- 进程级实测：Debug 构建 + `--crash-report <json>` 三向对照（null / 合法 / 非法文化）+ 4 类畸形快照，用于复现并隔离 AUD-REL-005；实测进程已全部终止，临时探针文件已删除。
- 守卫验证：两条新守卫均两向实测（人为移除守卫 / 放入孤儿基线 → 失败；还原 → 通过）。
- 未实跑 `Build-Distribution.ps1` / `New-WindowsInstaller.ps1`（本增量未触碰打包脚本；有效性由 beta.7 六资产与 CI 绿灯背书）；未执行真实外网更新下载全链路；未在非 Windows 平台实跑。
- 审计与修复过程 `verify.ps1` 触发的 `packages.lock.json` RID 段改写已 `git restore` 还原，工作树最终仅含审计产物与修复。
