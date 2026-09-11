# 仓库审计报告（当前状态）

- 审计日期：2026-09-11（full 全量重审 + 同日按优先度修复 + 分支核查复核）
- 审计对象：`66e103a`（当次 `main`）；本轮 13 项修复经 PR #13 **变基合并**（rebase merge）独立落入 `main`，最新 HEAD `43c17ce`
- 模式：**full 全量重审**（架构、安全、依赖与发布、测试、性能、可维护性六个域）
- 上次全量基线：`cffbd4d`（2026-09-09，全量 r2）
- 风险画像：desktop launcher / updater（下载完整性、文件系统安全、发布供应链、更新恢复 = critical）
- 历史报告：`.repository-audit/history/2026-09-10-network-review-audit.md`（上一份当前态报告，本轮归档）

## 当前结论

**仓库整体健康：0 Critical / 0 High。** 本轮全量重审新增 1 项 Medium、13 项 Low、4 项 Informational；分支核查阶段又发现 2 项 Low 漂移（行尾策略、`main` 保护规则文档）。其中 **13 项已修复并经 `verify.ps1` 全量验证**，其余为信息项、需决策项，或有意保留（理由见下表）。此前 66 项 resolved 发现未发现回归。

最终验证证据（本机实跑 `verify.ps1`，分支 HEAD，**exit 0**）：

| 检查 | 结果 |
|---|---|
| `scripts/Test-LocalizationContract.ps1` | 通过（verify 首道门禁，失败即短路） |
| Debug 构建 | **0 警告 / 0 错误** |
| 单元测试 | **1608 通过 / 2 跳过 / 0 失败**（1610；本轮 +13 条守卫） |
| Headless UI 测试 | **173 / 173 通过**（含 7 份黄金基线） |
| 手写代码覆盖率 | 行 **85.98%**（14372/16716）、分支 **92.93%**（2261/2433） |
| 覆盖率棘轮 | 通过，余量 **+0.13pp 行 / +0.23pp 分支**（基线已收紧至 85.85% / 92.70%） |
| Release 构建（win-x64） | **0 警告 / 0 错误** |
| Release 资源合约测试 | 18 通过 |
| CI（PR #13，run 34573490112） | **success**，6m49s |

> 合并方式说明：本轮以 **rebase merge** 合入，因此 `main` 上这 13 个提交的 SHA 与 PR #13 分支上的不同（内容一致，逐个提交保留、可按提交粒度 revert）。PR 描述里的提交列指的是分支提交；在 `main` 上按提交消息检索即可定位。

## 本轮修复（13 项，均带守卫）

按报告优先度落地，每项都有对应的回归/契约守卫，全部包含在 `verify.ps1` 的 1607 条单元测试中。

| ID | 严重度 | 修复内容 | 守卫 |
|---|---|---|---|
| AUD-SEC-006 | Low | 崩溃快照 `SnapshotPath` 改为仅运行时字段（`[JsonIgnore]`），落盘文档不再含任何路径，PRIVACY.md 的脱敏声明成立 | `CrashReportTests` 现同时断言原始与 JSON 转义两种拼写、并断言字段缺席；已用「临时移除 `[JsonIgnore]`」实验确认守卫会失败（修复前该断言恒真：STJ 把 `\` 转义为 `\\`） |
| AUD-PERF-011 | Medium | 修复会话不再对健康文件整读两遍：规划阶段算出的 CRC 连同大小/最后写入时间见证交给安装阶段复用 | `CheckHashAsync_WhenFilesMatchManifest_PlansTheirHashesForTheInstallPass` + 执行器两条正反断言（同一布局，仅见证不同：命中则跳过、失效则重读并失败） |
| AUD-TST-006 | Low | 补 `settings.json` 损坏/不可读的恢复守卫（此前该 catch 体零命中） | `ReadAsync_WhenSettingsFileIsMalformed/…CannotBeOpened_FallsBackToDefaults` |
| AUD-TST-009 | Low | CI 上传 `TestResults/Golden/*.png`，黄金截图失败的 actual/diff 产物可取回 | — |
| AUD-MTN-015 | Low | 18 处散文 `[LogTitle]` 规范为模块标签（LogViewer/GameUninstall/FileDownload/LauncherUpdate/Background/Toast/Settings），描述移入 message | `DiagnosticsLogTitleContractTests` 源码扫描契约 + 一条防止扫描模式漂移后静默通过的自检 |
| AUD-TST-007 | Low | 覆盖率基线 0.8430/0.8899 → 0.8585/0.9270，并在每次运行打印实测与基线差值 | `coverage.ps1` 的 `Baseline slack` 输出 |
| AUD-CI-006 | Low | 三处 `dotnet-version: 10.0.x` 固定为 `10.0.302`（已核对 .NET 10 发布元数据确认存在）；修正 README 的「由 global.json 固定」失实表述 | — |
| AUD-DEP-008 | Low | notices 新增「Self-contained .NET runtime」小节（运行时版本由 `dotnet --list-runtimes` 取最高版本，实测 10.0.11 与发布产物一致）；`Build-Distribution.ps1` 把 `LICENSE` 与 notices 复制进每个 RID 发布目录，五条打包路径均从该目录取件 | `ThirdPartyNoticesContractTests` 三条（notices 声明、生成器仍输出该节、打包脚本仍复制） |
| AUD-SEC-007 | Low | 诊断消息中的 URL 去掉查询串（资源面板 UID 即在此，而该消息写入恒被导出的 `unified.log`） | `DeserializeJsonAsync` 两条守卫覆盖解析失败与超限两个触发路径 |
| AUD-TST-008 | Low | `DialogsViewModelTests` 三处门控等待加 `WaitAsync(GateTimeout)`，回归时失败而非挂住 runner | 同项 |
| AUD-PERF-013 | Low | `Crc64Service.ComputeFileAsync` 的 1 MiB 缓冲改为 `ArrayPool` 租用（原每次调用一次 LOH 分配） | 既有 CRC-64/XZ 规范向量（纯分配改动，未改算法） |
| AUD-MTN-019 | Low | 5 个 `scripts/*.ps1` 以 CRLF 提交（违反 AGENTS.md/CLAUDE.md/`.editorconfig` 的 LF 规则），其中一个整行含损坏的 `\r\r\n`；根因是策略只写在 `.editorconfig`（编辑器配置，git 不读）→ 规范为 LF，并在 `.gitattributes` 为各扩展名补 `eol` 规则，使 git 在 checkout/add 时真正强制 | `LineEndingPolicyContractTests` 断言 `.editorconfig` 声明的每个 `end_of_line` 都被 `.gitattributes` 以相同 eol 强制（缺规则时报出扩展名）；已实测移除 `*.ps1` 规则即失败 |
| AUD-CI-007 | Low | `PROJECT_CONVENTIONS` §9 只写「`main` 受保护」，未说明保护范围——实测 ruleset 仅 `deletion` + `non_fast_forward`，**无** PR / 状态检查 / 评审要求，即 CI 红灯同样能合入 → 改写为「实际规则表 + 约定 vs 强制」两段，并在 AGENTS.md 注明这些要求是约定而非门禁 | — |

## 仍开放项

| ID | 严重度 | 状态 | 摘要与不修的理由 |
|---|---|---|---|
| AUD-ARCH-005 | Low | open / **需要决策** | `ModalHostViewModel.IsDialogLayerInteractive` 有属性、有测试、无绑定：对话框层没有交互闸口。当前无用户可见缺陷（隔离由主叠层禁用 + 先关后开 + 等 ZIndex 次序隐式承担，本轮未能构造可达失败叠栈）。是绑定为闸口，还是删除并在模态 ADR 中记录隔离策略，属架构决策 |
| AUD-DEP-009 | Informational | **product-decision** | 发行产物无代码签名、无随附 `SHA256SUMS`。发布摘要清单成本极低（beta.8 发布审计曾手工算出），代码签名需证书决策 |
| AUD-MTN-017 | Low | open | 新增主叠层模态仍需约 14 个未守卫编辑点，其中 `ShellLifecycle` 语言刷新清单漏改静默失败。需要一次结构性收敛，非本轮范围 |
| AUD-PERF-012 | Informational | open | 校验/安装/卸载阶段每文件一次 UI 线程 `Post` 无合并。机制已核，**队列深度后果未测量**，故按 advisory 保留 |
| AUD-ARCH-006 | Informational | open | `ModalEntry.Content` 只被写入、从不被读取，模态契约文档高估现实 |
| AUD-ARCH-007 | Informational | open | `LocalDiagnostics.syncLogger` 静态可变（26 处调用点用静态重载）。生产端仅一个实例，当前零影响 |
| AUD-MTN-018 | Informational | open | `BannerImageDecoder` 复制了 `BackgroundImageDecoder` 的钳制算法 |
| AUD-SEC-008 | Informational | open | 两处可预测 `*.tmp` 写路径未纳入随机名硬化；利用需先具备游戏目录写权限，无权限提升 |
| AUD-DEP-010 | Informational | open | 漏洞门禁为推送触发（无 `schedule`）；Dependabot 不覆盖 `prototypes/`（该原型有意退出 CPM） |
| AUD-ARCH-003 / AUD-MTN-001 / AUD-TST-001 | Low | deferred | 与上轮一致：`RemoteContentViewModel` 直接持 `DispatcherTimer`（`:27`/`:330`，714 行）与其拆分、以及限速测试的 `Stopwatch` 下限断言。本轮复验该文件未变，deferred 状态仍成立 |
| AUD-DEP-002 | Low | accepted-risk | `Shirasagi0012.MaterialColorUtilities` 单维护者风险，已有年度复审与 fork 预案 |

## 变更集（`cffbd4d..66e103a`，18 提交 / 59 生产文件 / 47 测试文件）

本轮修复之前，变更集内新增功能面仅两处，均已逐行审读：

1. **日志导出时间范围与可选内容**（`281e9bc`、`66e103a`）：`Services/Diagnostics/{LogExportOptions,ExportWindow,LogEntryReader}.cs` 与 425 行重写的 `LogExportService`。设计面健康——读路径流式化（`ReadLinesInWindow` 惰性喂给 `LogEntryReader`，范围探测 `Any()` 首条命中即短路）、`.partial` 临时文件 + `File.Move` 原子落盘、`unified.log` 恒入档而轮转文件无命中即跳过、失败项记 `skipped` 而非静默丢失、取消与重入语义完整。
2. **网络修复与优化批次**（`d342641`…`d6fee4e`）：统一手动重定向、逐 URI 代理判定、DNS 短 TTL 缓存、`ConnectTimeout`/HTTP-2 keep-alive、启动 30s 整体预算、下载进度内存计数、image-cache 清扫。

## 验证与健康面

- **未发现任何 resolved 发现回归**；本轮抽查的近期守卫测试均随 `verify.ps1` 全绿。
- **网络面**：重定向逐跳复验且显式拒绝 HTTPS→HTTP 降级（`RemoteHttpRequestService.cs:24-67`），全仓库无裸 `HttpClient` 调用；DNS 短 TTL 缓存只在全公网解析成功时命中，私网/空/抛错永不缓存；启动 30s 预算与调用方取消语义正确分离。
- **更新通道不可达代码执行**：启动器只校验下载 URL 前缀并交给浏览器白名单（`LauncherUpdateService.cs:295-303` → `ShellLifecycle.cs:356` → `ExternalLinkService.cs:17-50`）。
- **游戏文件写入被根目录约束**：`GamePathValidator.cs:24-112`（含根目录规范化、逐段重解析点拒绝）被下载、安装、差异、卸载四条路径共用。
- **架构边界无回归**：`Features/Shell` 仍是唯一引用其他 Feature 的 Feature（AGENTS.md 明文豁免），模态契约位于根 `ViewModels/`，24 个接口 singleton 注册、无服务定位器、无 transient/singleton 捕获。
- **本地化契约机械成立**：四份 resx 各 553 键，零缺键/零多余/零空值，生成物与中性 resx 完全同步。
- **无仓库抽象旁路**：除 `HttpClientLeaseSource` 外无裸 `HttpClient` 构造，除 `WindowFilePickerService` 外无 `StorageProvider` 使用，`Process.Start` 仅出现在四个受控启动器内。
- **无已提交密钥**；全树扫描无 `ghp_`/`github_pat_`/`AKIA`/私钥/代理凭据。
- **CI 供应链**：两个 workflow 顶层 `permissions: contents: read`，仅发布 job 提权；5 个第三方 action 全部 40 位 SHA 固定；Inno Setup 7.1.0 额外经 `gh release verify-asset`；AppImage 工具链硬编码 SHA-256 校验。
- **锁文件强制执行**（非装饰）：`build.yml` 设 `RestoreLockedMode=true`，两处 RID 还原以行内注释显式豁免；三份 lock 的每个条目均为 `resolved` + `contentHash`。

## 推荐优先级（剩余项）

1. **AUD-ARCH-005 / AUD-DEP-009 / AUD-SEC-006 遗留措辞**：三项都需要你裁定（绑定还是删除对话框层闸口；是否发布摘要清单/引入签名）。前三者之外的修复本轮已完成。
2. **AUD-TST-007 的基线维护**：下次全量 verify 后按注释把基线与实测值一起前移。
3. **AUD-MTN-017**：模态接线收敛为一张表（kind → 内容 VM → 叠层 + 交互属性），可同时消掉语言刷新清单的静默失败。
4. 其余 Informational 按域择机处理。

## 审计方法与局限

- **模式**：`full` 全量重审。六个域各自独立通读，随后逐条复核候选证据（源码、调用方、守卫测试、配置/CI、仓库规则），再分别验证建议本身的可行性；修复阶段按报告优先度实施，每项配回归/契约守卫。
- **实跑命令**：`pwsh -File ./verify.ps1`（分支 HEAD，exit 0）——含本地化契约脚本、Debug 构建、`coverage.ps1`（棘轮）、win-x64 RID 还原、Release 构建、Release 资源合约测试。测试与覆盖率数字取自本轮产出的 `TestResults/Coverage/{unit,headless}/*.trx` 与 `coverage.cobertura.xml`；覆盖率零命中断言由直接解析 cobertura 得出。RID 还原改写的 `packages.lock.json` 已 `git restore` 还原。
- **分支核查轮**（本轮末尾追加）：逐项核对分支内容与合并面——`git merge-base --is-ancestor` 确认 `main` 是分支祖先（纯快进，冲突面为零）、`git merge-tree --write-tree` 无冲突、35 个改动文件全部为预期文件（无 lock/`TestResults/`/`artifacts/`/`obj`）、`git diff --stat` 与 `--ignore-cr-at-eol` 数字一致（无行尾噪声）。核查中发现并修掉两处问题：我自己的脚本在改写 `scripts/Build-Distribution.ps1` 时误加了一个 UTF-8 BOM（与 `.editorconfig` 的 `.ps1` 无 BOM 要求冲突，且该类文件在索引中无 BOM），已用全量 BOM 比对确认仅此一处、以 `--fixup` 折回引入它的提交；以及上述 AUD-MTN-019 的行尾漂移。
- **实验验证**：AUD-SEC-006 的守卫通过「临时移除 `[JsonIgnore]` → 用例失败 → 恢复」确认其不再恒真；AUD-DEP-008 的运行时版本与本机 `dotnet --list-runtimes`、与既有发布产物 `runtimeconfig.json` 双向核对；AUD-CI-006 的 SDK 版本经 .NET 10 发布元数据确认存在。
- **未执行**：真实网络限速/停滞复现；黄金基线重生成；发行打包（`Build-Distribution.ps1` / Inno Setup）与安装器实测；未在 Linux/macOS 上执行任何测试。
- **局限**：AUD-PERF-011 的收益幅度、AUD-PERF-012 的调度队列后果、AUD-PERF-013 的分配收益均**未测量**（AUD-PERF-013 为纯分配改动，正确性由既有 CRC 向量守卫）；AUD-SEC-007 的触发条件（面板端点返回非 JSON 或超大响应体）未复现，仅核实完整路径并加守卫。AUD-ARCH-005 未能构造可达失败叠栈，故按「契约假象 + 脆弱性」而非当前缺陷定级。本轮未做逐行全量阅读。
