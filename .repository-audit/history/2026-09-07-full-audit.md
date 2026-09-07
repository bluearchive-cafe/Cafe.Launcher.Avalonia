# 仓库审计报告（当前状态）

- 审计日期：2026-09-07（full 全量审计 + 当日整改复核）
- HEAD：`1ce42d1`（整改后）；审计基线 dc0c4ff
- 审计方式：repository-audit 流程（六域并行专项审计 → 高影响项人工核验 → 修复 → 独立复核代理逐提交验证 7/7 通过）
- 历史报告：`.repository-audit/history/`（最近：2026-09-04 全量、2026-09-05 delta/release）

## 当前结论

**无 Critical/High open 项。2026-09-07 审计发现的全部 8 项（Low 及以下）已于当日修复并经复核确认；main CI 绿灯、全量测试通过（单元 1444 过/2 跳 + Headless 164/164 + 本地化合约 18/18），v1.1.0-beta.6 处于可发布状态。**

上一轮发布阻塞项（AUD-REL-001/002/003、AUD-BLD-002）已在此前核销，本轮未回潮。

## 2026-09-07 发现 → 整改对账（全部 resolved）

| 发现 | 严重度 | 修复提交 | 复核要点 |
|---|---|---|---|
| AUD-SEC-004 固定名探针 + FileMode.Create 符号链接截断面 | Low | `97a17a9` | 探针名每次 `Path.GetRandomFileName()`，固定名预置不可行；类注释同步改为启发式表述 |
| AUD-PERF-005 向导每击键全量读取 + UI 线程写探测 | Low | `1f7a8a2` | 300ms 防抖仅作用于击键路径（步进入仍即时）；探测包 `Task.Run` |
| AUD-PERF-006 前任 CTS 飞行中被 Dispose 竞态 | Low | `1f7a8a2` | Token 在首个 await 前捕获，Cancel+Dispose 安全；防抖延续有 version + isDisposed 双守卫 |
| AUD-MTN-006 DownloadSession 写拒绝块两处重复 | Low | `a9d397c` | 统一为 `StopForWriteDeniedAsync`，行为逐字段等价 |
| AUD-TST-003 更新检查闸口缺端到端测试 | Low-Medium | `836d10e` | 两个行为测试：匹配跳过（sentinel 佐证不重写）/ ACL 拒写 → 本地化失败 + 检查点清除 |
| AUD-MTN-005 中性默认表守卫测试自我循环 | Low | `901bb3e` | 改为解析 App.axaml Light/Dark ThemeDictionaries 钉住真值 |
| AUD-DEP-006 THIRD-PARTY-NOTICES 版本过期 | Low | `a85404f` | 再生至 Avalonia 12.1.2/MEDI 10.0.11；顺带核销 AUD-DEP-001 两行占位许可证（经包内 LICENSE 文件与 nuget 核实） |
| AUD-DEP-007 Dependabot 提交无 Conventional 前缀 | Info | `1ce42d1` | AGENTS.md 载明 squash + `chore(deps):` 约定 |

整改中顺带修复：向导防抖引入后 4 处同步推进测试改为有界等待（消除既有热自旋隐患）；审阅步骤编辑按钮改按 `wizard-review-edit` 类定位（await 后测试延续与 UI 线程的瞬态语言分叉，系 `T()` 线程 Culture 解析的测试面耦合，非产品缺陷）。

## 维持的暂缓/接受项（有意、已文档化）

- AUD-ARCH-001（架构裁决：Shell 壳层地位已在 AGENTS.md 拍板，深层窄接口化为后续方向）
- AUD-ARCH-003 DispatcherTimer 注入抽象、AUD-MTN-001 IModalPresenter 查表 / RemoteContentViewModel 拆分
- AUD-DEP-002 Shirasagi 包年度重审；AUD-TST-001 真实限速断言

Advisory（不单列台账）：向导动画测试 settle 失败不在重试覆盖内（62）；`SetupWizardGamePathStatusDebounce` 300ms 对测试时限的余量充足。

## 门禁实证（2026-09-07 实跑）

- `test.ps1`：单元 1444 过/2 跳 + Headless 164/164，退出码 0。
- `Test-LocalizationContract.ps1` 通过。
- 独立复核代理对 dc0c4ff..HEAD 七个提交逐一验证：修复完整、无回归、无遗留 TODO/调试代码（详见本报告审计方法说明）。

## 正面观察（复验维持）

分层纪律、安全五高危类别、性能基础设施、供应链闭环、flake 面治理全部维持；本轮新增的防抖 + 后台探测架构与有界等待测试模式延续了仓库既有护栏风格。

---

*审计方法说明：六域并行专项审计产出，主审计二次核验（含一项沙盒实验否决误报：verify.ps1 守卫失败路径实测正确退出 1）。整改后由独立复核代理对每个提交做 diff 级验证（7/7 通过，含 ACL 测试清理安全性、App.axaml 解析命名空间正确性、notices 版本与 lock 一致性抽验）。*
