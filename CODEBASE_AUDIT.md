# 仓库审计报告（当前状态）

> 本报告为 **full 全量重审**（用户指令）：六域审计通道全部执行，对上一基线以来的 16 个提交逐域重验，全部开放发现按现行源码逐项复核。上一报告（2026-09-13 full+delta）已归档至 `.repository-audit/history/2026-09-13-full-audit.md`。
>
> **同日修复轮**：报告定稿后按优先级将可执行发现全部落地（9 个提交，`d1693d5..6c80951`），修复后单元 1710 + Headless 177 全绿。详见 Resolved Findings。
>
> **同日复审（2026-09-14 下午，用户指令「全量审查 + 检查报告问题修复情况」）**：①上轮修复轮 9 项提交逐项在工作树读码核实为真实落地（代码 + 守卫测试 + 文档/注释均在，非纸面关闭）；②6 项开放发现证据锚点复核仍准确；③四域只读子代理独立重扫全树 + 主审计逐项核实候选，**新立案 7 项**（1 Medium + 6 Low，见对应节）；④本地实测单元 1708 通过/0 失败/2 可见跳过 + Headless 177 全绿，`dotnet list package --vulnerable` 三项目零漏洞。上午版报告已归档至 `.repository-audit/history/2026-09-14-full-audit.md`。
>
> **第二修复轮（2026-09-14 晚，用户指令「按优先级修复并逐阶段提交」）**：复审新立案 7 项全部落地（6 个提交 + 1 项转接受），每阶段聚焦测试后逐项提交：AUD-TEST-005（`58edcf8`）、AUD-SEC-006（`8b6d3dd`，复核发现其补救已被 `5a38be9` 刻意否决——fake-ip 代理/CDN 依赖这些段，按 SEC-004/ARCH-007 先例转书面化接受 + 守卫）、AUD-TEST-006（`c2701da`）、AUD-TEST-007（`cabaa3c`）、AUD-MAINT-003（`415809d`）、AUD-PERF-007（`5f53a6e`）、AUD-MAINT-004（`93b3335`）。开放发现收敛至 6 项 Low（全部决策/设计轮门控），另 4 项 Low 为书面化 accepted-risk。

## Audit Metadata

- 日期：2026-09-14（上午 full 六域重审 + 下午修复核实轮/独立重扫 + 晚间第二修复轮）
- Commit：审计基线 `715fee5`（`main`；最新已发布 tag `v1.1.0-beta.9`）；第二修复轮后 HEAD `93b3335`
- 模式：**full**（上午：`8449d37..715fee5` 16 提交六域重验；下午：HEAD `6ecd7bf` 全树独立重扫 + 修复核实）
- 范围：生产源码 242 个 `.cs`（≈33.6k 行）+ 29 个 `.axaml`、210 个测试文件（≈43k 行）、CI 三工作流、打包/安装器脚本、文档契约
- 项目画像：desktop-launcher（`.agents/skills/repository-audit/profiles/desktop-launcher.md` 按仓库证据调整）

## Executive Summary

仓库健康状况：**良好，且较上一审计实质性改善**。desktop-launcher 四个关键风险面（下载完整性、文件系统边界、进程启动、外部链接）防御纵深不变且全部有测试；上一轮全部 5 项 Medium 级结构/测试发现中 4 项已随 `878260a`/`b9dc68e`/`5553793`/`5777f2f`/`715fee5` 真实解决（不是纸面解决——本审计逐项读码 + 本地实测全绿确认），其余 2 项（AUD-PERF-001、AUD-CI-001）部分解决后降档。同日修复轮 9 项提交经复审逐项读码核实为真实落地。**复审新立案 1 项 Medium（测试覆盖缺口），其余 6 项新发现为 Low。**

开放发现（2026-09-14 第二修复轮后）：

- Critical：0
- High：0
- Medium：0
- Low：6 open（全部决策/设计轮门控：AUD-PERF-001、AUD-PERF-004、AUD-PERF-005 残留、AUD-SEC-001、AUD-SEC-002、AUD-ARCH-005）+ 4 accepted-risk（MAINT-002、SEC-004、SEC-006、ARCH-007）
- 本窗口解决：14 项（8 项随上轮修复落地：ARCH-002/003、TEST-002/003/004、PERF-002/003、MAINT-002；6 项随同日修复轮：ARCH-004/006、CI-001、PERF-006、SEC-003/005）；第二修复轮再解决 6 项（TEST-005/006/007、MAINT-003/004、PERF-007）并将 SEC-006 结案为 accepted-risk

**一处上轮审计证据更正（重要）**：上轮安全节声明「签名 Authorization 头绝不跟随重定向转发」——复核证实该头经 `RemoteRequestOptions.ConfigureRequest` 钩子在**每一重定向跳重发**（含跨主机），已立案为 AUD-SEC-003（Low）。这推翻了上轮对 DNS 重绑定残余风险影响边界的部分论证。

最重要的风险/行动（第二修复轮后剩余，全部为决策/设计轮门控）：

1. **AUD-PERF-001**（Low）— 更新路径未变更文件的全读是文档化的损坏自愈设计（与官方启动器行为一致），已并行化 ≤8 + 下载期哈希跨轮复用；剩余的见证摊销须先基准实测再决策，不得弱化自愈语义。
2. **AUD-PERF-004**（Low）— 每次壳层刷新全量重解码横幅位图；位图备忘的补救验证为 Plausible，需要专属设计轮处理轮播/陈旧释放生命周期，不宜顺手改。
3. **AUD-PERF-005 残留**（Low）— 首帧权衡已文档化（690993f）；二次冗余解码的消除需先确认跳过卫可合法匹配。
4. **AUD-ARCH-005**（Low，接受中）— ShellLifecycle 的 Wire/Unwire 密度：若再因结构原因触碰，按 ADR-023 声明表收敛。

## Changes Since Previous Audit

`8449d37..715fee5`，16 个提交，+2059/−617 行，49 个文件。主线是**上轮审计发现的成批落地**：

- `5553793` test(platform)：平台分支跳过可见化（`Assert.Skip*` 16 处）+ 新增 `linux-tests.yml`（ubuntu-24.04 单元套件，dispatch + 每周）。
- `b9dc68e` test(update)：新增 `SettingsViewModelTests`（4 用例）+ `ShellLifecycleTests` 扩至 11 用例，补齐更新检查「服务→UI」粘合层。
- `878260a` perf(download)：停止/暂停/恢复点击路径去同步阻塞；下载缓冲 ArrayPool 池化；测试观察窗按生产节奏校准。
- `5777f2f` refactor(views)：操作表面动效套件抽出为 `OperationSurfaceAnimator`（190 行），`MainWindow.axaml.cs` 663→499 行。
- `715fee5` perf(install)：更新校验改有界并行（≤8）；ShellLifecycle 测试缝所有权制度书面化。
- 功能线：资源面板三连（`8526b97` 版本一致单行结论、`8bda5e1` 刷新保留旧数据 + 保存按钮脏检查、`23ff11c` 状态条去重），覆盖层拆分 `ResourcePanelOverlay`（`d479c9c`）；`66c9c01` 系统代理解析对齐 WinINet；`a9e1818` 社交芯片悬停态专用 token；`5b8b344` **revert** 恢复内置壁纸构造期同步解码（见 AUD-PERF-005）。
- 审计自身：`e024aa1` 上轮 delta 复核入库；`afd6dbd` 架构评审报告入库 `docs/architecture-review-2026-09-13.html`（上轮的未跟踪待决项就此闭合）。
- 依赖：`Directory.Packages.props`、三份 `packages.lock.json`、`global.json` 零变更（git diff 证实），依赖结论无需重扫；`dotnet list package --vulnerable --include-transitive` 本审计实测三项目均无漏洞包。

## Critical Issues

无。

## High Priority Findings

无。

## Medium Priority Findings

### AUD-TEST-005 — 有界并行安装校验重写（715fee5）无多文件清单测试，并发语义全部未钉住【复审新立案；已解决 `58edcf8`】

- 类别：测试 / 关键路径保护
- 严重度：Medium｜置信度：85（主审计直接核实）｜状态：**resolved**（`58edcf8`）｜处置：Add Guard（已执行）
- **证据**：`DownloadExecutorTests` 全部 8 处清单参数均为单元素 `[manifestFile]`（`tests/Cafe.Launcher.Avalonia.Tests/DownloadExecutorTests.cs:38-39,:65-66,:94-95,:122,:148`）；`GameDownloadServiceTests` 零处引用 `InstallDownloadedFilesAsync`（grep 证实）。提交 `715fee5` 的 stat 只含 `DownloadExecutor.cs` 与 `ShellLifecycle.cs`，无测试文件——并发落地时未伴随测试扩容。
- **影响**：`SemaphoreSlim(≤8)` 门控、按索引 `failedFlags` 重组、乱序进度回调的**产生侧**、`WhenAll` 取消扇出均无用例可检出回归（如边界死锁、失败标志丢失、顺序破坏）。该路径是下载完整性的执行端。上轮「9 用例经并行路径钉住行为」的说法对并发维度 overstated——用例确实走并行代码路径，但每次只有 1 个文件，从不产生竞争。显示侧乱序回退已由 `4c0b0cd` 的钳制测试钉住，产生侧未钉。
- **建议**：补一个多文件清单用例（含故意的哈希不匹配文件 + 文件数 > 并行度），断言失败重组、按清单序重试与成功计数。**建议验证**：Verified（机械测试扩容，被测 API 现成）。
- **解决记录（`58edcf8`）**：`DownloadExecutorTests` 增至 13 用例（随后的 `5f53a6e` 另补 400 文件接线测试，至 14）——12 文件（>并行度 8）混合布局断言失败列表按清单序重组、失配 .tmp/终路径删除、通过文件搬移、进度每文件一次；缺失 .tmp 只标记该文件失败。

## Low Priority Findings

### AUD-SEC-006 — IsPublicAddress 黑名单遗漏 CGNAT 100.64.0.0/10 与基准段 198.18.0.0/15【复审新立案；结案为 accepted-risk `8b6d3dd`】

- 类别：安全 / 网络（SSRF 分类缺口）
- 严重度：Low｜置信度：85（主审计读码核实）｜状态：**accepted-risk**（`8b6d3dd`）｜处置：Accept Risk（书面化已执行）
- **证据**：`Services/RemoteHttpUrlValidator.cs:170-179` IPv4 switch 覆盖 `0/10/127/169.254/172.16-31/192.0/192.168/≥224`，`100.64.0.0/10`（CGNAT）与 `198.18.0.0/15`（benchmark）落入 `_ => true` 按公网放行；字面 IP URL 在 `:91-99` 直接判定，主机名解析后在 `:112-124` 同判。
- **影响**：指向这些段的 URL 被直接拨号（无代理）。可达目标包括 Tailscale 节点与 MagicDNS 解析器（100.100.100.100）、CGNAT 网关管理面、WARP/fake-IP 段（198.18/15）。影响有界：端口限 80/443、GET-only、跨主机重定向已剥离凭据（`1dcc9df`）、响应不回传攻击方（仅 JSON 解析失败的 16 字节预览入诊断日志，`RemoteHttpTransport.cs:468-476`）。与 AUD-SEC-001 根因不同：那是 validate-then-dial 时序窗口，这是黑名单分类缺口。
- **建议**：switch 增加 `100 when bytes[1] is >= 64 and <= 127 => false` 与 `198 when bytes[1] is >= 18 and <= 19 => false` 两行；配套 `RemoteHttpUrlValidatorTests` 两段字面 IP 拒绝用例即守卫。**建议验证**：Verified。
- **结案记录（`8b6d3dd`，接受风险）**：修复轮执行时发现该补救已被 `5a38be9` 刻意否决——fake-ip 模式代理软件把 DNS 应答落在 198.18/15、部分 CDN 边缘节点落在 100.64/10，拦截会回归真实用户的横幅/下载失败，且已有 `ValidateAsync_WhenLiteralAddressIsCarrierGradeNat_ReturnsUri` 钉住放行。诊断成立、补救被否决，按 SEC-004/ARCH-007 先例转书面化接受：switch 显式放行臂 + 让步注释（代价有界：GET-only、80/443、跨主机重定向剥离凭据、响应不回传攻击方），新增 198.18/15 放行守卫测试。勿在未重审该让步前重新收紧。

### AUD-PERF-007 — 校验/diff/卸载阶段逐文件派发 UI 线程进度回调，无下载阶段的累加器等价物【复审新立案；已解决 `5f53a6e`】

- 类别：性能 / UI 线程
- 严重度：Low｜置信度：80｜状态：**resolved**（`5f53a6e`）｜处置：Fix（已执行）
- **证据**：逐文件回调源：`DownloadExecutor.cs:327-329`（每清单文件每次校验轮一次 `progress`，无百分比去重）、`ManifestDiffCalculator.cs:74/:107/:125`（UpdateCheck/RepairCheck 逐文件）、`GameUninstallService.cs:74`（逐文件 `new GameOperationProgress`）。消费侧 `GameOperationsViewModel.ApplyProgress`（`:379-394`）每回调一次 `Dispatcher.UIThread.Post` + 闭包分配，`ApplyProgressCore`（`:396-472`）每次写 ~12 个 observable 属性 + 本地化格式化。下载阶段已有 `DownloadProgressAccumulator(100ms)`（`DownloadExecutor.cs:100-103`）刻意解决同类问题，其余阶段无等价门控。
- **影响**：大清单校验/卸载时 UI 线程收到每文件一次回调，与渲染竞争。定性影响（无测量）；代码库自己在下载路径为此建了累加器，说明该类问题在本仓库是已知痛。
- **建议**：把累加器范式（或按 stage+百分比去重）套用到校验/diff/卸载回调。与 AUD-PERF-001 相互作用（未变更文件跳过重读亦减少本项回调量），但根因不同各自立案。**建议验证**：Strongly Supported。
- **解决记录（`5f53a6e`）**：新增 `PercentProgressGate`（Interlocked 值变化门控：首值 0 必达、重复抑制、阶段折返回退放行、线程安全），应用到校验（`DownloadExecutor`）、stat/修复哈希扫描（`ManifestDiffCalculator`）与卸载（`GameUninstallService`）三处产生侧；显式阶段发射（切换/repair-confirm）有意不过门。守卫：门控四态单测 + 执行器 400 文件同桶去重接线测试。与消费侧 AUD-PERF-006 钳制互补。

### AUD-MAINT-003 — unified.log 文件名硬编码两处，违背「文件名声明于 GamePaths.cs」契约【复审新立案；已解决 `415809d`】

- 类别：可维护性 / 文档-代码契约
- 严重度：Low｜置信度：95（主审计直接核实）｜状态：**resolved**（`415809d`）｜处置：Fix（已执行）
- **证据**：AGENTS.md「Persistence and compatibility contracts」声称文件名声明于 `Constants/GamePaths.cs` 并列举 `unified.log`；实际 `GamePaths.cs:29-33` 只有 manifest/config/settings/download-state/notice 五项。`"unified.log"` 字面量在 `Services/Diagnostics/UnifiedLogger.cs:34` 与 `Services/Diagnostics/LogExportService.cs:172` 各一份，轮转名 `unified_{i:D3}.log` 在 `:177`。同函数 `LogExportService.cs:33-35` 的其余导出项已用 `GamePaths` 常量——字面量是离群点。无契约测试把两处生产字面量拴在一起。
- **影响**：重命名日志文件将静默分叉 logger 与导出器；AGENTS.md 把维护者引向错误的常量归属地。
- **建议**：`GamePaths` 补 `UnifiedLogFileName` 常量并三处引用；AGENTS.md 表述即恢复为真。**建议验证**：Verified。
- **解决记录（`415809d`）**：`GamePaths.UnifiedLogFileName` 收拢三处硬编码（`UnifiedLogger` 构造、`LogExportService` 当前项 + 轮转名派生——`unified_NNN.log` 从常量词干派生，改名自动携带方案）；AGENTS.md 表述恢复为真。`LogExportServiceTests` 的字面量保留为导出线格式线钉。

### AUD-TEST-006 — UiStyleContractTests 令牌纪律扫描遗漏 ResourcePanelOverlay.axaml，守卫白名单在创建该文件的提交中漂移【复审新立案；已解决 `c2701da`】

- 类别：测试 / 守卫覆盖
- 严重度：Low｜置信度：93（主审计直接核实）｜状态：**resolved**（`c2701da`）｜处置：Add Guard（已执行）
- **证据**：`UiStyleContractTests.cs:21-36` `ViewFiles` 14 项无 `Views/ResourcePanelOverlay.axaml`；令牌纪律用例（原色/`Transparent`/图标尺寸 `Tokens.cs:511-514`、Raw 色值 `:570-578`、排版内联 `:600+`）均消费该列表——最新主叠层不受任何 §2.3 令牌检查。拆分提交 `d479c9c` 在 Motion（`:199-207`）/Dialogs（`:32-38`）/Localization 列表都补了该文件，唯独令牌扫描列表漏了——手工白名单模式已实际漂移一次。当前文件实测无违规（潜在而非现行）。
- **影响**：下一个编辑该文件的人没有绊网；同一漂移可在未来任何新文件上复发。
- **建议**：短平快——把文件补进 `ViewFiles`；更强——加「`Views/*.axaml` 全部在扫描集内」的元契约测试，令白名单漂移不可能。**建议验证**：Verified。
- **解决记录（`c2701da`）**：文件补入共享 `ViewFiles`（三扫描全部生效，实测无违规）；新增 `ScanTargets_CoverEveryTopLevelViewFile` 元契约——Views/ 顶层每个 `.axaml` 必须被 `ViewFiles`/`StyleFiles` 声明或显式豁免（`CrashReportWindow` 留名豁免，注释载明其独立 `Crash.*` 令牌族）。复核跟进（同日晚）：原实现曾以匿名 `Append` 放行 `MainWindowDebugOverlay.axaml`（不在扫描集亦未留名豁免，其 ：220 内联 `Padding` 违规因此不可见）——已补入 `ViewFiles`（实测 148 令牌契约全绿），内边距以下沉的 `Border.dialog-card.compact` 样式类收口（视觉不变），元契约自此只剩声明集与留名豁免两条路径。

### AUD-TEST-007 — 主题变体订阅拆卸（696abdd 当日新增）全仓库无回归守卫【复审新立案；已解决 `cabaa3c`】

- 类别：测试 / 回归守卫
- 严重度：Low｜置信度：80｜状态：**resolved**（`cabaa3c`）｜处置：Add Guard（已执行）
- **证据**：`SettingsAppearanceViewModel.cs:763-767`（`Dispose` 退订 `ActualThemeVariantChanged`）、`:657-670`（`EnsureThemeSubscription` 含换 Application 先退订分支）——全仓库 grep 无测试引用 `EnsureThemeSubscription`/`themeApplication` 或断言退订；headless 侧仅隐式拆卸（`NeutralStrategyHeadlessTests.cs:99`、`MainWindowHeadlessTests` TestContext）。
- **影响**：提交消息声称「headless 共享 Application 不再跨测试累积订阅」，但删除 `:765` 退订行全套件仍绿——被修复的累积 bug 可静默回归。与已接受的 AUD-ARCH-005（ShellLifecycle Wire/Unwire）无涉：不同类、新代码、无接受裁定。
- **建议**：单测钉住 Dispose 后事件已退订（或 headless 双 VM 实例串联断言不互相触发）。**建议验证**：Verified。
- **解决记录（`cabaa3c`）**：新增 `ThemeSubscriptionTeardownHeadlessTests`——哨兵法双相守卫：武装 System 模式处理器后置哨兵色，对照相翻转变体证明哨兵能侦测到在位处理器（方案重刷改写哨兵），`provider.Dispose` 后再翻转哨兵必须存活；退订行被删即测试失败。

### AUD-MAINT-004 — MainWindowDialogsOverlay.axaml 承载点残留化石注释【复审新立案；已解决 `93b3335`】

- 类别：可维护性 / 卫生
- 严重度：Low｜置信度：90（主审计读文件核实）｜状态：**resolved**（`93b3335`）｜处置：Fix（已执行）
- **证据**：`Views/MainWindowDialogsOverlay.axaml:13` 注释「Chinese localization settings：资源面板覆盖层（已拆分至 ResourcePanelOverlay.axaml）」——前半句与资源面板无关，`git log -S` 追溯至 `f20de8b` 重组的化石文本。`:13-14` 恰是资源面板（主叠层）嵌于对话框容器、以 FirstChild 位于对话框层之下的承载点。
- **影响**：错误标签遮蔽该位置依赖的微妙层序不变量；调整层序或寻找宿主的维护者会被误导。两行修正。
- **建议**：改写为说明「主叠层 FirstChild 位于对话框层之下」的准确注释。**建议验证**：Verified。
- **解决记录（`93b3335`）**：注释改写为说明 FirstChild 层序不变量并警示勿在其前插入子项；`UiStyleContractTests` 全绿。

### AUD-SEC-003 — 签名 Authorization 头在每一重定向跳重发（含跨主机）【新立案；更正上轮审计论断】

- 类别：安全 / 网络凭据处理
- 严重度：Low｜置信度：85（本审计主代理亲自读码确认）｜状态：**resolved**（`1dcc9df`）｜处置：Fix（已执行）
- **证据**：重定向循环逐跳 `createRequest(currentUri)`（`Services/RemoteHttpRequestService.cs:34`）；`BuildRequest` 每跳调用 `policy.ConfigureRequest?.Invoke(request)`（`Services/RemoteHttpTransport.cs:285-294`）；`Services/LauncherApiClient.cs:198-200` 据此每跳重附 `Authorization`（`TryAddWithoutValidation`）。签名与请求路径无关（`Services/Auth/AuthorizationHeaderFactory.cs:44`，`data` 为空串），被截获即可对任意端点重放至服务器容忍的时限。无测试断言跨跳的头行为。
- **影响**：.NET 内建 `HttpClient` 会在跨主机重定向时剥离 `Authorization`，手写循环丢掉了这层保护。影响有界：仅 API 主机可选择重定向目标，而其被攻陷本可直接取得该头——属加固而非独立利用链。
- **建议**：在 `RemoteHttpRequestService.SendAsync` 中当下一跳 `Uri.Host` 与初始主机不同时剥离 `Authorization`（规则收进单一发送例程，优于让 `ConfigureRequest` 感知跳数）。
- **建议验证**：Verified（机制读码确认；修复为机械改动）。
- **建议守卫**：单测钉住「跨主机重定向后请求不携带 Authorization」。

### AUD-SEC-004 — 手写代理对注册表配置的代理发送当前用户默认凭据【新立案；66c9c01 引入】

- 类别：安全 / 网络
- 严重度：Low｜置信度：80｜状态：**accepted-risk**（`78e0ef0`，让步已书面化）｜处置：Accept Risk
- **证据**：`Services/ProxySettingsService.cs` `BuildConfiguredProxy` 内 `UseDefaultCredentials = true` `new WebProxy(settings.ProxyUrl) { UseDefaultCredentials = true, … }`；`ProxyUrl` 原样取自 `HKCU\...\Internet Settings\ProxyServer`（`WindowsRegistrySystemProxySettingsProvider.cs:25-56`）。提交消息记录意图：镜像 WinINet 的静默 407 应答。
- **影响**：同用户进程可写 `ProxyServer` 指向攻击者收集 NTLM/Kerberos 应答。误报排查：与 `WebRequest.GetSystemWebProxy()` 行为一致，且注册表值本就在同用户写入能力内（届时攻击者已控制用户会话）——真实风险低。
- **建议**：保持行为；在既有注释旁写明「同用户信任边界让步」；仅 System 模式启用默认凭据已是现状，维持。
- **建议验证**：Verified（读码确认；无凭据行为测试，现有 ProxySettingsServiceTests 断言的是 PAC/凭据属性存在性）。

### AUD-SEC-005 — 发布产物无签名、SHA256SUMS 自行发布于同一 Release【新立案】

- 类别：安全 / 供应链
- 严重度：Low｜置信度：85（事实）；可利用性评估 60｜状态：**resolved**（`457ac50`）｜处置：Add Guard（已执行）
- **证据**：`release.yml:358-372` 生成六产物 `SHA256SUMS` 并发布到同一 Release（:374-409）；全仓库无 Authenticode、无 `actions/attest-build-provenance`、无 macOS codesign/notarization（`installer/macos/Info.plist` 无 `com.apple.security.*`，`Build-Distribution.ps1` 无签名步骤）。
- **影响**：终端用户真实性完全依赖 github.com TLS + Release 组织账号控制——社区项目可辩护的模型（且构建输入侧已有 attestation、SHA 钉住、NuGetAudit）；缺口仅在 Release 账号被攻陷时用户无从独立验证。
- **建议**：为发布产物追加 `actions/attest-build-provenance`（GitHub 托管、无需证书）；文档写明 `SHA256SUMS` 仅完整性非来源。证书签名待项目获得证书再议。
- **建议验证**：Strongly Supported（attest action 为标准 GitHub 功能，适配现有双仓库发布流）。

### AUD-PERF-001 — 更新路径仍对全部未变更已安装文件做全文件 CRC64 重读【部分解决，Medium→Low 降档】

- 类别：性能 / 下载完整性
- 严重度：Low（降档理由：串行 foreach 已消除，并行化 ≤8 + 跨轮摊销落地；残留为文档化的自愈设计）｜置信度：95｜状态：open（部分解决）｜处置：Architecture Decision
- **已解决部分（`715fee5`）**：校验改为有界并行 `Math.Clamp(Environment.ProcessorCount, 1, 8)`（`Features/GameOperations/DownloadExecutor.cs:28`，`SemaphoreSlim` :284）；结果按清单索引收集（`failedFlags`，WhenAll 后读取 :330-339）、计数 `Interlocked`；`Crc64Service` 线程安全（静态只读表 + 池化缓冲）；失败语义不变（不匹配即删 :305-315、按清单序重试 `DownloadSession.cs:489-494`）；下载期 `verifiedHashes` 跨重试轮累积（`DownloadSession.cs:413-435`）；修复通道见证跳过（`PlannedFileHash`）正常；建议的 Verbose 跳过计数日志已加（:341-344）。`DownloadExecutorTests` 9 用例经并行路径钉住行为。
- **残留**：更新计划的 `plannedHashes` 刻意为空（`DownloadPlan.cs:21-29`），未变更文件仍各全读一遍——这是代码注释明示的唯一内容损坏自愈通道（`DownloadExecutor.cs:260-264`，启动校验只比 size/existence）。小更新 + 大安装场景仍是一次全树读（现最多 8 路并行）。
- **建议**：不弱化自愈语义。若再优化，复用修复通道见证机制须以「见证仍匹配才信任跳过」为前提，并先基准实测。**建议验证**：Strongly Supported。
- **既有守卫**：Verbose 跳过计数日志（后续审计可直接量化摊销效果）。

### AUD-PERF-005 — revert `5b8b344` 恢复壁纸构造期同步解码：首帧前 UI 线程全分辨率解码 + 首次刷新二次冗余解码【新立案；文档半项已交付】

- 类别：性能 / 启动与首帧
- 严重度：Low｜置信度：80｜状态：open（文档半项已交付 `690993f`）｜处置：~~Document（必须）~~ 已完成 + Investigate（二次解码）
- **证据**：`ViewModels/BackgroundViewModel.cs:122` 构造函数内 `backgroundImageSource = bundledImageLoader()` 同步解码 `Assets/launcher-background.png`（2560×1388，提交消息载明），经 `App.axaml.cs:61` DI 解析链在 `MainWindow` 显示前于 UI 线程执行。`AGENTS.md:75` 原承诺「no blocking work before the first frame」——revert 刻意违反该承诺（提交消息记录产品理由：无它则窗口先开在主题底色上，golden 基线与 UX 回退）。第二处：首次刷新时跳过卫（:153-160）因 `lastBackgroundSourceKey`/`lastDecodeTarget` 未置而不能命中，:232 于线程池再次全量解码同一内置图（loader 不接收目标尺寸，第二次解码产图同尺寸）。
- **已交付（文档半项）**：AGENTS.md:75 改写为「重活不占首帧路径 + 内置壁纸构造期同步解码是唯一刻意例外（使首帧显示壁纸而非主题底色），勿擅自改回异步」；CONTEXT.md 复核无同类声明，无需改动。
- **影响（残留）**：未量化：首帧关键路径一次全 PNG 解码（已文档化为刻意）+ 启动后短时间内一次冗余解码（线程池）。
- **建议（剩余）**：独立评估构造位图复用或跳过态种子化以消除二次解码——需先确认首次刷新的解码目标可合法匹配跳过卫；与同步/异步之争互不绑定，勿在无产品确认下回退 revert。
- **建议验证**：Verified（代码路径读码确认；解码成本未测量）。

### AUD-PERF-006 — 并行安装校验的进度回调可瞬时回退【新立案；715fee5 引入】

- 类别：性能 / UI 正确性
- 严重度：Low｜置信度：85（竞态从代码确定；用户感知未测）｜状态：**resolved**（`4c0b0cd`）｜处置：Fix（已执行）
- **证据**：`Features/GameOperations/DownloadExecutor.cs:327` `progress((int)Math.Round(Interlocked.Increment(ref completedCount) * 100d / manifestFiles.Count));` 在线程池并发执行——递增与回调非原子：线程 A 递增至 5 后被抢占，可在 B 回调 6 之后回调 5。消费侧 `Dispatcher.UIThread.Post` 线程安全但不保序。
- **影响**：FileCheck 百分比可瞬时回退；阶段边界进度（`DownloadSession.cs:457-460`）保证终值正确，无卡死。属外观瑕疵。
- **建议**：`ApplyProgressCore` 钳制单调，或在单次 `Interlocked` 操作内取值并回调。**建议验证**：Verified（单行修复，无需测量）。

### AUD-CI-001 — 非 Windows 测试不随变更执行【部分解决，Medium→Low 降档】

- 类别：CI / 跨平台行为
- 严重度：Low｜置信度：95｜状态：**resolved**（`c37c44c`）｜处置：Add Guard（已执行）
- **已解决部分（`5553793`）**：新增 `.github/workflows/linux-tests.yml`（ubuntu-24.04，`workflow_dispatch` + 每周一 cron）：单元套件获得真实非 Windows 执行点，平台自适应测试（如 `GamePathValidatorTests.cs:161`）不再无处执行；权限最小（`contents: read`）、动作 SHA 钉住、无新信任面（本审计安全通道专项评估）。locked 还原跨平台成立的推理已写入工作流注释。
- **残留**：触发仅 dispatch + weekly（`linux-tests.yml:7-10`，注释自述「不阻塞 PR」）——平台分支回归不能阻塞引入它的变更，最坏晚一周才暴露且不阻塞合并；Headless/golden 按设计保持 Windows-only（合理）。该 job 是否曾绿跑，本审计无法验证（只读评审，无 GitHub Actions 运行历史）。另按 PROJECT_CONVENTIONS §9，`main` 规则集无 required_status_checks，Windows job 亦只是约定门。
- **建议**：把现有 job 体（无 RID 还原、无渲染依赖）复用为 `build.yml` 中 push/PR 路径的 ubuntu 单元测试 step；可选为规则集加 required status checks。**建议验证**：Verified（job 体已存在，纯编排改动）。

### AUD-ARCH-007 — 代理指纹变化可在下载批次进行中 Dispose 其底层 handler【新立案】

- 类别：架构 / 生命周期所有权
- 严重度：Low｜置信度：70（机制结构性证实；未复现运行时行为）｜状态：**accepted-risk**（`78e0ef0`，权衡已书面化）｜处置：Accept Risk
- **证据**：`Services/ProxySettingsService.cs:100-103` 指纹变化即 `stale.Handler.Dispose()`（下次同模式租约创建时触发）；租约以 `disposeHandler: false` 包裹 handler（`Services/HttpClientFactory.cs:92`），仅释放自己的 HttpClient；下载批单租约全程持有（`Services/DownloadTransport.cs:36-72`，租约 10 分钟 `GameDownloadService.cs:39`）。触发链：系统代理/VPN/PAC 变更 → 任一后续远程调用（更新检查、资源面板、横幅）建租约 → 处置进行中批次下的 handler。:87-89 注释只覆盖创建竞态，未覆盖活租约处置。
- **影响**：批次传输快速失败进入验证重试轮，下轮新租约自愈——代价一轮下载而非永久故障、无完整性风险（.tmp + CRC64）。性能通道独立识别同一机制，交叉证实。
- **建议**：先实验确认 disposed `SocketsHttpHandler` 对在途请求的确切语义；若有害，为缓存 handler 加租约引用计数或延迟至租约释放再处置；若可接受，书面记录「下载中途代理变更代价一轮失败重试」。**建议验证**：Needs External Verification。
- **注意**：修复不得改变 `ProxySettingsService.cs:23-38` 文档化的指纹替换不变量。

### AUD-ARCH-005 — `ShellLifecycle` 为 src/ 最高变更热点，Wire/Unwire 16 对订阅镜像靠人工配对【新立案】

- 类别：架构 / 可维护性
- 严重度：Low｜置信度：80｜状态：open｜处置：Accept Risk（下次因结构原因触碰时按 ADR-023 表驱动收敛）
- **证据**：`Features/Shell/ShellLifecycle.cs` 784 行、25 commits/180d（src/ 第一热点；`ServiceConfiguration.cs` 24、`MainWindow.axaml.cs` 22——本审计 git 计数）。持 ~30 协作者（:35-63）、34 行 Wire（:421-454）+ 44 行 Unwire（:562-605）16 对订退对；现有覆盖仅验 Dispose 路径（`ShellLifecycleTests.cs:237-259`），无对称性断言。
- **影响**：每次跨功能事件变更是双点编辑，漏配对称仅能靠人工评审发现。Shell 聚合本身是 sanctioned 例外，疑虑仅在密度。
- **建议**：仓库先例（ADR-021/022）偏好书面接受而非投机抽取——现状接受；若再动 Shell，把订阅收敛为单张声明表由 Wire/Unwire 共同消费，对称性成为数据性质。**建议验证**：Verified（计数）；行动与否 Needs Architecture Decision。
- **与已解决项的关系**：AUD-ARCH-001（模态注册）已由 `ModalRegistrar` 解决；本项是同文件的另一根因（接线密度）。

### AUD-MAINT-001 — `SettingsAppearanceViewModel` 以 5 个静态字段保存主题方案缓存

- 严重度：Low｜置信度：90｜状态：**resolved**（`696abdd`）｜处置：Refactor（已执行）
- **原证据**：`Features/Settings/SettingsAppearanceViewModel.cs:614-618` 五个 `private static` 缓存字段（本审计 grep 复核）。VM 为 DI 单例，静态存储功能等价实例态；成本是隐藏耦合与测试污染面（测试侧已有先例为静态 `ResizeReloadDebounce` 付费）。
- **建议**：随下次触碰该文件移入实例字段。**建议验证**：Verified。

### AUD-PERF-004 — 每次壳层刷新销毁并重新解码全部横幅位图

- 严重度：Low｜置信度：85｜状态：open（窗口内零提交触及该文件，git log 证实）｜处置：Refactor
- **证据**：`ViewModels/RemoteContentViewModel.cs:152-195` 每次 `Apply`（启动 `ShellLifecycle.cs:240`、每次设置保存/重置 :310/:353、每次游戏操作完成 :372→:686-704）`DisposeBannerBitmaps()`（:696-704）后全量重取缓存重解码（:546-558）。既有缓解充分：24h 磁盘字节缓存（`ImageCacheService.cs:23,:179-184`）、线程池解码（:555-558）、4 路并发上限（:22）、陈旧解码丢弃（:565-571）。
- **建议**：以 ImageCacheService 已有的 URL/CRC 键做每 URL 位图备忘。**建议验证**：Plausible。

### AUD-SEC-001 — URL 校验与拨号间 DNS 重绑定 TOCTOU 窗口

- 严重度：Low｜置信度：80｜状态：open｜处置：Accept Risk
- **证据更新（2026-09-14）**：逐跳校验现居 `Services/RemoteHttpRequestService.cs:28-34`（`RemoteHttpTransport.cs:276-294` 与 `DownloadTransport.cs:40-50` 共用）；全仓库无 `ConnectCallback`/IP 钉扎（grep 证实），`SocketsHttpHandler` 拨号时二次解析。新认识：`RemoteHttpUrlValidator.cs` 的 30s 正 DNS 缓存对重绑定威胁是**加宽**而非收窄窗口。原注释的「deliberately short: it bounds the window」反向框定已于 2026-09-14 更正（现明确「缓存是省 IO 手段而非重绑定防御，TTL 是该窗口的上限而非关闭」，引 AUD-SEC-001）；风险处置不变。
- **影响不变**：GET-only、响应不回传攻击方、跨主机重定向的凭据面另见 AUD-SEC-003。**建议**：接受；未来收紧时连接钉扎到已校验 IP。**建议验证**：Needs External Verification。

### AUD-SEC-002 — 资源面板 UID 以 URL 查询串传输（且变更为 GET，可重放）

- 严重度：Low（隐私）｜置信度：85｜状态：open｜处置：Accept Risk
- **证据更新（2026-09-14）**：UID 仍为两处查询串（`ResourcePanelApiClient.cs:49` config/get、:71-75 config/set——GET 携带完整变更内容，可重放可入日志）。诊断日志剥离查询串的机制完好，引用更新为 `RemoteHttpTransport.cs:431-432`（`DescribeUri` → `GetLeftPart(Path)`；原 `RemoteHttpRequestService.cs:202-209` 引用已失效，该文件现 121 行不持有诊断）；其余日志点 grep 证实零 UID 泄漏。
- **建议**：接受；上游协议约束不变。**建议验证**：Needs Product Decision。

## Informational Findings

无（AUD-ARCH-004 已随 `6c80951` 归入 `Features/Diagnostics`；AUD-ARCH-006 已随 `690993f` 修正并结案，均转 Resolved Findings）。

## Advisory（不立案汇总）

复审新增（2026-09-14 下午，置信度低于报告线或属决策项）：

- **settings.json 每次壳层刷新双读 + 每读双解析**（置信度 75）：`LauncherCoreService.cs:77` 的 `LoadAsync` 在 `ShellLifecycle.cs:223` 已读之后再次 `settingsService.ReadAsync`；`LauncherSettingsService.cs:79` 反序列化后 `ApplyLegacyFields`（`:122-124`）又无条件 `JsonDocument.Parse(json)` 一次。全部在线程池，文件 KB 级——纯冗余工作，改动需先厘清两次读取的快照一致性语义。
- **DesignGalleryViewModel 内联构造于 DialogsViewModel、缺席组合根**（置信度 62）：`DialogsViewModel.cs:173` `new DesignGalleryViewModel(...)`，与既有的确认对话框族内联构造先例（文档化 :28-32）相邻但类型不同、无归属注释——是「对话框族先例的延伸」还是「违反组合根声明」，Needs Architecture Decision，不立案。
- **NeutralStrategyHeadlessTests.cs:100 `finally` 内裸 `Directory.Delete`**（置信度 65）：同文件族既有 deliberate swallow 范式（`MainWindowHeadlessTests` catch IOException/UnauthorizedAccessException 并注释理由），此处缺失——迟释放句柄可把通过测试变失败或掩盖断言异常。
- **GamePathValidator reparse 游走每调用重新 stat 根与全路径段**（置信度 70）：`GamePathValidator.cs:47,:72-98` 无父目录结果缓存，在计划/校验/安装/卸载每文件循环中每文件走一遍；安全意图正当，冗余是重复 stat（线程池，非 UI）。
- **M3 色彩方案在 UI 线程按 swatch 重复全量重建**（置信度 60）：`MaterialSchemeGenerator.cs:113-118` 中性跟随关闭时每次 `BuildRoleBrushes` 构造第二个完整 HCT 方案读 3 个角色；`RefreshThemeColorPaletteBrushes`（`SettingsAppearanceViewModel.cs:484-497`）每 swatch 一方案。仅设置交互时触发，感知度未测。
- **进度钳制状态不在 `PrepareOperation` 重置**（置信度 55，低于报告线）：`GameOperationsViewModel.cs:175-189` 只归零 `ProgressValue` 不重置 `displayedProgressStage/Floor`；现状无害依赖「新操作首阶段 ≠ 上操作末阶段」的未钉住不变量。若未来 `FileCheck` 成为某操作的首阶段会冻结进度条——触碰该文件时顺手重置即可。
- **`ImageCacheService.cacheLocks` 进程期不收缩**（置信度 55，低于报告线）：每去重键一个 `SemaphoreSlim` 仅 Dispose 清空；launcher 规模下有界，纯保留卫生。

既有（上轮遗留，维持）：

- **组合根运行时服务定位**（已处理 `78e0ef0`）：transport 工厂内 `ISettingsEditor` 改为构造时一次解析并闭包引用，代理模式解析不再重复服务定位。
- **DI 工厂内静态注册共享日志器**：`ServiceConfiguration.cs:42-48` 首次解析时 `LocalDiagnostics.RegisterSharedLogger(logger)`（`Volatile.Write`），构建多容器的测试会令首个容器的 logger 成为全局目标。生产路径 `App.axaml.cs:58` 急切解析一次，序确定；影响限于诊断误路由。建议测试 teardown 复位或显式一次性注册。（置信度 72，advisory 档）
- **Wire() 委托缝**：四个可空委托由 Shell 在构造后赋值（`ShellLifecycle.cs:426-428,:448`），已文档化为意图（`SettingsViewModel.cs:49`「Coordination delegates — set by parent after construction」）、空条件消费、测试钉住——有意设计，仅记录依赖图对构造签名不可见这一属性。
- **清单 `.tmp` 暂存名与 `.tmp` 结尾清单条目可互撞**（置信度 45，低于报告线）：敌意清单可声明 `x.tmp` 使其与 `x` 的暂存名重合；清单内容可控本就意味着内容可控，不构成独立完整性绕过。可加廉价断言（清单路径不得以暂存后缀结尾）。
- **等待助手私有变体 5→7 份**、`GameDownloadServiceTests.cs` 2068 行（最大测试文件）：维护成本项，仓库已有按域拆分先例（`UiStyleContractTests` 11 分部），随下次触碰收敛/拆分。
- **`AtomicJsonFileStore` 写失败路径无直接测试**（消费侧已间接覆盖：`LocalInstallationStateStoreTests.cs:174` 中途移动失败终态可读）——仅在改动该类时补一例。
- **卸载走 `GetSafePath` 而非 `GetSafeFilePath`**：根自身规范化条目不逃逸但会使卸载以异常中止——可用性边角，非完整性。

## Architecture

**结论：文档边界与实现一致，模态隔离裁定与 ADR-023 收敛经受住了本窗口两轮 UI 重构。**

- **跨功能边界零违规**（复核）：全量 `using` 扫描，越界仍仅 `ShellLifecycle`/`ShellPresentationFamily`/`ShellStartup` 三文件 = sanctioned 例外；`DebugViewModel` 仍经 `IGameOperationActivity` 窄抽象消费（组合根绑定 `:154-155`）。
- **模态注册声明式收敛保持**：19 个 `ModalKind` ↔ 19 条注册（`ShellLifecycle.cs:461-548` ↔ `ModalKind.cs:5-25`），`TryHandleEscape` 2 行委托（:608-612）；`ResourcePanelOverlay` 拆分（`d479c9c`）未破坏裁定——新覆盖层自带 `IsResourcePanelInteractive` 门（`ResourcePanelOverlay.axaml:12-14`）且仍是主叠层FirstChild、位于对话框层之下。
- **新抽取件干净**：`OperationSurfaceAnimator`（190 行）逐字搬移、ADR-016 注释保留、headless 动效套件未弱化；残留（两处未用 using、锚点退役回调跨文件）见 AUD-ARCH-002 解决记录。
- **组合根纪律**：全 Singleton、纯构造注入、释放顺序显式注释且经读码核实（客户端注册于 `HttpClientFactory` 之后 :115-135）；`Program.ServiceProvider` 仅用于会话末释放。两处轻微偏离见 advisory。
- 剩余 Low 发现：AUD-ARCH-005（接受中，若再动 Shell 按声明表收敛）；ARCH-004/006/007 已分别随 `6c80951`/`690993f`/`78e0ef0` 结案。

## Security

**结论：无 Critical/High。上午四条新 Low 中三条是「可辩护设计的书面化」，一条（SEC-003）是真实的加固缺口（已修复）；复审独立重扫再立案一条 Low（SEC-006 黑名单分类缺口，第二修复轮结案为书面化接受——补救已被 5a38be9 否决），其余九个检查面（文件系统边界、进程执行、远端 JSON、TLS/重定向/代理、敏感日志、CI、本地数据文件）复核零新发现。**

信任边界不变：Yostar API/CDN、Cafe CDN/资源 API、GitHub 更新源、远端清单、同用户进程、CI/发布凭据、用户注册表配置的系统代理（新）。

已验证的优势（本通道逐项复核）：

- **TLS/重定向**：全仓库零证书校验覆盖（grep 证实）；手动重定向 ≤5 跳、每跳 URL 复验、HTTPS→HTTP 降级阻断（`RemoteHttpRequestService.cs:28-70`）。
- **完整性**：续传 Content-Range 三元组校验（`FileDownloadService.cs:127-140`）；哈希不过即删重试（:166-177）；路径全部经 `GamePathValidator`（规范化 + 根前缀 + 全组件 reparse point 拒绝，`GamePathValidator.cs:24-112`）；并行校验基元线程安全（`Crc64Service.cs:21,33`）。
- **进程执行**：四处 `Process.Start` 全部结构化（URL scheme 白名单、固定 explorer、`ArgumentList` 游戏启动、崩溃自重启 `Environment.ProcessPath`）；零 shell 字符串拼接。
- **自更新**：仅 host 钉住的 GitHub 下载页跳转，从不下载执行自身二进制。
- **CI**：三工作流动作全 SHA 钉住；`permissions` 最小化（release 唯一 `contents: write`）；Inno Setup attestation 校验；`linux-tests.yml` 无新信任面（本审计专项评估：无 PR 触发故无缓存投毒面，`RestoreLockedMode` 开启）。
- **安装器**：`PrivilegesRequired=admin`；卸载归属标记 + NSIS 遗留桥 basename/目录双重校验、篡改注册即删不执行（`installer/*.iss:197-334`）。
- **密钥/日志/反序列化**：无真实凭据（`AuthorizationSalt` 为公开协议常量）；日志零 Authorization/salt/cookie/查询串（UID 全日志面 grep 证实）；纯 System.Text.Json 严格默认，零不安全反序列化/反射加载。

## Dependencies / Supply Chain

**结论：依赖图零变更；漏洞扫描实测干净；锁定闭环保持。**

- `dotnet list package --vulnerable --include-transitive`（本审计实际执行）：三个项目均无漏洞包。
- `Directory.Packages.props`、三份 `packages.lock.json`、`global.json` 在窗口内零变更（git diff 证实），锁定/钉住/attestation 结论沿用上轮无需重扫。
- `NuGetAudit` + warnings-as-errors 在构建层兜底漏洞包（`Directory.Build.props:24-27`）。
- 发布侧完整性缺口已于 `457ac50` 以 `attest-build-provenance`（OIDC 来源证明）补全；证书签名仍为可选后续。

## Testing

**结论：纪律持续兑现。上轮 4 项测试发现全部真实解决；修复轮的守卫测试逐项核实到位（SEC-003 两用例、PERF-006 两用例断言与 `DownloadSession.cs:439` 的显式归零兼容、`IsSameAuthority` 双向钉住）。复审新立案的 3 项测试缺口（TEST-005/006/007）已随第二修复轮全部补齐并配套元契约/哨兵守卫；收口实测单元 1717 通过 / 0 失败 / 2 可见跳过 + Headless 178 通过 / 0 失败。**

- **关键路径保护**（复核保持 + 一处降级）：下载续传/CRC/限速、安装状态损坏矩阵、设置兼容（legacy 字段 + DeepClone 棘轮）、卸载边界、URL 校验、更新流三分支 + 确认接线（AUD-TEST-002 解决）均钉住；**例外**：并行安装校验的并发语义无多文件用例（AUD-TEST-005，Medium）。
- **确定性**：正面等待全部有截止/迭代上限（复审全树检索无悬挂面）；程序集级串行 + 静态清单 + 用户数据隔离保持；`Assert.Skip*` 16 处，平台分支全部可见跳过（复审复核保持，零隐藏跳过）；`ResourcePanelApiClient` 5 个专用用例。复审另发现一处确定性边角：`NeutralStrategyHeadlessTests.cs:100` 裸 `Directory.Delete`（advisory）。
- **CI 门禁**：覆盖率棘轮在 build.yml 强制（85.85%/92.70% 基线 + 余量打印）；golden 失败工件闭环（actual/diff PNG `if: always()` 上传，路径与写盘一致核实）；契约测试全部在可执行路径上、无本机-only 测试；golden 基线契约（`GoldenBaselineContractTests.cs:16-44` 严格 1:1）复审核实无孤儿/缺失。
- **fire-and-forget 复核**：`878260a` 落地的三处 `_ = diagnostics.DebugAsync(...)` 证实安全（`LocalDiagnostics.cs:109-123` 内部吞异常，无 unobserved-task 风险）；ArrayPool rent/return `try/finally` 包裹无 use-after-return。

## Performance

**结论：上轮全部性能发现落地且质量好（修复非纸面）；复审独立重扫确认启动/首帧、定时器、事件泄漏、位图生命周期、ArrayPool 修复均干净，新立案一条 Low（PERF-007 非下载阶段逐文件 UI 回调）与四条 advisory。**

- **复审核实干净**：启动/首帧路径除文档化壁纸解码外仅 `ImageCacheService` 一处 `Directory.CreateDirectory`；全仓库定时器清点（2 个自停一次性动效 timer、1 轮播、2 个 250ms 内核等待轮询 + 生成守卫的 debounce）；`Crc64Service`/`FileDownloadService` 池化租还并发正确；`ShellLifecycle`/`ShellStartup` Wire 有 `isWired` 守卫，overlay 重订阅先退订；壁纸旧位图交叉淡化释放握手与陈旧代次处置正确。
- **AUD-PERF-001 残留**与 **AUD-PERF-005 残留**见 Low 节；PERF-006 已修复（`4c0b0cd`）；**AUD-PERF-007** 已随 `5f53a6e` 解决（PercentProgressGate 百分比门控）；settings 双读双解析、reparse 游走重复 stat、M3 方案重建、cacheLocks 保留见 Advisory。

## Maintainability / Technical Debt

- 热点与结构互证：`ShellLifecycle` 25、`ServiceConfiguration` 24、`MainWindow.axaml.cs` 22 commits/180d——接线处变更多的正常形态；动效引擎已出窗（`5777f2f`），`ShellLifecycle` 的 Wire/Unwire 密度立案为 AUD-ARCH-005。
- 文档漂移三处已修复入库（2026-09-14：`690993f`/`d1693d5`）：AUD-ARCH-006（ZIndex 表述，结案）、AGENTS.md:75 首帧承诺（AUD-PERF-005 文档半项）、`RemoteHttpUrlValidator` DNS 缓存反向框定注释（AUD-SEC-001 证据修正）；advisory 的组合根服务定位已随 `78e0ef0` 一次解析化。AUD-MAINT-001 静态缓存与 AUD-ARCH-004 VM 归属亦已分别随 `696abdd`/`6c80951` 落地。
- 复审新立案的两处低级卫生项已随第二修复轮解决：AUD-MAINT-003（`415809d` UnifiedLogFileName 常量收拢）与 AUD-MAINT-004（`93b3335` 化石注释更正为 FirstChild 层序不变量说明）。另核实：架构子代理全量 `using` 扫描再次零跨功能违规；`MotionTokens` 双梯有 `MotionTokensTests` 钉住不立案；`CrashReportWindow` 自带 token 族属可辩护隔离。
- `docs/architecture-review-2026-09-13.html` 已入库（`afd6dbd`）；官方协议对比文档按用户裁定 accepted-risk 结案（AUD-MAINT-002），行为不变量由 `OfficialHashServiceTests`/`AuthorizationHeaderFactoryTests`/`LauncherConstantsTests` 钉住。

## Decisions Required

第二修复轮后仅剩三项既有决策（均维持原裁定）：

1. **AUD-PERF-001 残留**：更新路径是否在「自愈契约」前提下引入见证摊销（并行化已交付；需基准实测后再决策，未测量不得轻动）。
2. **AUD-PERF-005 残留**：首次刷新二次解码是否消除（需先确认跳过卫的解码目标可合法匹配；与同步/异步之争互不绑定）。
3. **AUD-ARCH-005**：若再因结构原因触碰 Shell，按 ADR-023 声明表收敛 Wire/Unwire（维持接受）。

复审新立案的 7 项已于第二修复轮全部落地（6 项解决 + SEC-006 转书面化接受）。

## Resolved Findings（本窗口，8 项）

- **AUD-ARCH-002**（`5777f2f`）：操作表面动效套件抽出为 `OperationSurfaceAnimator`（190 行），窗口 code-behind 663→499 行；入场锚点与壁纸淡化按提交消息明示刻意留存（次优先），残留 advisory（两处未用 using、锚点回调耦合）随下次触碰清理。
- **AUD-ARCH-003**（`715fee5`）：双构造所有权制度按原建议第二分支书面化（`ShellLifecycle.cs:115-121`，含差异、测试不可复现性、收敛前置与「已裁定可接受」明示）；分叉保留但已裁定接受。可选升格 ADR。
- **AUD-TEST-002**（`b9dc68e`）：`SettingsViewModelTests` 4 用例覆盖 `CheckForUpdatesAsync` 三分支 + 失败消息格式化；`ShellLifecycleTests` 11 用例覆盖确认接线端到端（恰一次打开、Dispose 退订、启动失败降级）。残留 advisory：空 `FailureMessage` 子分支无直接断言。
- **AUD-TEST-003**（`5553793`）：`Assert.Skip*` 16 处 + 1 attribute Skip；残留早退全部位于 SkipUnless 之后且为 CA1416 守卫；本地实测 2 项可见 SKIP、零隐藏跳过。
- **AUD-TEST-004**（`878260a`）：处置观察窗 80→400ms（>1× 生产 250ms 轮询，附推导注释）；700ms 重文档化为回归绊网而非生产常量复制。残留 advisory：否定窗仍为固定睡眠（标定合理）。
- **AUD-PERF-002**（`878260a`）：Stop/Pause/Resume 点击路径全部 fire-and-forget 并附 §3.2 意图注释；grep 证实 UI 点击路径零残余同步阻塞。
- **AUD-PERF-003**（`878260a`）：下载缓冲 `ArrayPool.Rent` + finally 归还（return 在 try/finally 外，归还有保证）；内容非敏感无需清零。
- **AUD-MAINT-002**（结案为 accepted-risk）：按用户 2026-09-12 裁定不入库；工作树文件已移除；行为不变量由既有测试守卫兜底。

同日修复轮（2026-09-14，按优先级逐项提交，全部已入库）：

- **AUD-SEC-003**（`1dcc9df`）：`RemoteHttpRequestService.SendAsync` 对非初始授权方（scheme+host+port）的跳剥离 `Authorization`，与 .NET 内建 HttpClient 的跨主机剥离约定对齐；配套跨主机剥离/同主机保留两个守卫测试（`AuthTrackingRedirectHandler`）。
- **AUD-CI-001**（`c37c44c`）：`build.yml` 新增 `linux-unit-tests` job（ubuntu-24.04，push/PR 触发并阻塞），复用 weekly 作业体；平台分支回归现在阻塞 PR。
- **AUD-PERF-006**（`4c0b0cd`）：`ApplyProgressCore` 按阶段键控钳制进度单调；与重试轮经 `VerificationRetry` 折返 `FileCheck` 显式归零（`DownloadSession.cs:439`）的流程兼容；配套乱序回退/阶段重启守卫测试。
- **AUD-SEC-005**（`457ac50`）：release job 追加 `actions/attest-build-provenance` v4.2.2（SHA 钉住）为六个分发包与 SHA256SUMS 签发 OIDC 构建来源证明；补 `id-token`/`attestations` 权限。
- **AUD-SEC-004 + AUD-ARCH-007**（`78e0ef0`，均结案 accepted-risk）：`ProxySettingsService` 注释书面化同用户凭据让步与活租约处置权衡（一轮失败重试自愈、租约引用计数判为更高风险）；顺带将组合根 `ISettingsEditor` 改为一次解析（advisory 项闭合）。
- **AUD-MAINT-001**（`696abdd`）：主题方案缓存五字段、`ApplyScheme`、`ActualThemeVariantChanged` 订阅全部转实例；`Dispose` 拆卸订阅（headless 共享 Application 不再跨测试累积）；两处 headless 测试改经 DI 构造的 VM 实例调用。
- **AUD-ARCH-004**（`6c80951`）：`DesignGalleryViewModel` 移入 `Features/Diagnostics`（其天然宿主），命名空间随目录。
- **AUD-ARCH-006**（`690993f`）与 **AUD-SEC-001 注释更正**（`d1693d5`）：AGENTS.md ZIndex 表述如实化 + 首帧承诺记录壁纸例外 + DNS 缓存反向框定更正。

修复轮验证：每阶段跑聚焦测试；收口时全量套件（单元 1710 通过/0 失败/2 可见跳过 + Headless 177 通过/0 失败）与 Debug 构建零警告；`LineEndingPolicyContractTests` 绿。CI 编排类改动（build.yml/release.yml）经 YAML 解析校验，未实际触发 workflow 运行。**复审逐项核实（2026-09-14 下午）：9 项修复在 `6ecd7bf` 工作树全部为真实落地**——SEC-003 剥离逻辑与两守卫测试（`RemoteHttpTransportTests.cs:305,:329`）、CI-001 `build.yml:96-97` linux job、PERF-006 `ApplyProgressCore` 钳制与两守卫测试（`GameOperationsViewModelTests.cs:729,:752`）、SEC-005 SHA 钉住的 attest 步骤与权限、SEC-004/ARCH-007 书面化注释（`ProxySettingsService.cs:94,:156`）、MAINT-001 静态字段清零（仅余两个无状态辅助方法）、ARCH-004 文件归位、ARCH-006/SEC-001 文档更正均在。

此前已解决（维持）：AUD-ARCH-001（`77547fa` ModalRegistrar）、AUD-TEST-001（`88ebd46..fae538a` RemoteHttpTransport 接缝）。

第二修复轮（2026-09-14 晚，按杠杆序逐项提交）：

- **AUD-TEST-005**（`58edcf8`）：`DownloadExecutorTests` 增至 13 用例（随后的 `5f53a6e` 另补 400 文件接线测试，至 14）——12 文件（>并行度 8）混合布局钉住失败按清单序重组、失配 .tmp/终路径删除、通过文件搬移、进度每文件一次；缺失 .tmp 只标记该文件失败。
- **AUD-SEC-006**（`8b6d3dd`，结案 accepted-risk）：执行时发现补救已被 `5a38be9` 否决（fake-ip 代理 DNS 应答落 198.18/15、CDN 边缘节点落 100.64/10，拦截即回归用户可见故障）——转书面化接受：switch 显式放行臂 + 让步注释 + 198.18/15 放行守卫测试。
- **AUD-TEST-006**（`c2701da`）：`ResourcePanelOverlay` 补入共享 `ViewFiles`；`ScanTargets_CoverEveryTopLevelViewFile` 元契约令 Views/ 顶层白名单漂移不可能（`CrashReportWindow` 显式豁免留名）。复核跟进：`MainWindowDebugOverlay` 原经匿名 `Append` 放行，已补入 `ViewFiles`（内联 `Padding` 以 `dialog-card.compact` 样式类收口，视觉不变）。
- **AUD-TEST-007**（`cabaa3c`）：`ThemeSubscriptionTeardownHeadlessTests` 哨兵法双相守卫——对照相自证哨兵能侦测在位处理器，Dispose 后哨兵必须存活；退订行被删即失败。
- **AUD-MAINT-003**（`415809d`）：`GamePaths.UnifiedLogFileName` 收拢三处硬编码，轮转名从常量词干派生；测试字面量保留为线钉。
- **AUD-PERF-007**（`5f53a6e`）：`PercentProgressGate`（Interlocked 值变化门控）应用到校验/stat/修复扫描/卸载四处产生侧；门控四态单测 + 400 文件同桶去重接线测试；显式阶段发射有意不过门。
- **AUD-MAINT-004**（`93b3335`）：承载点化石注释更正为 FirstChild 层序不变量说明。

第二修复轮验证：每阶段跑聚焦测试后逐项提交；收口全量套件本地实测（Debug，`93b3335`）：单元 1719 总量 = 1717 通过 / 0 失败 / 2 可见跳过 + Headless 178 通过 / 0 失败（新增守卫 1 例）。

## Automated Guards Added

本窗口由修复顺带落地的守卫：

1. `SettingsViewModelTests` + `ShellLifecycleTests` 更新流用例（AUD-TEST-002 守卫）。
2. `Assert.Skip*` 范式（AUD-TEST-003 守卫，新平台门控照此办理）。
3. 校验跳过计数 Verbose 日志（AUD-PERF-001 建议的量化钩子，`DownloadExecutor.cs:341-344`）。
4. `ProxySettingsServiceTests` 16 用例（WinINet 归一化理论用例）与 `MainWindowHeadlessTests.RemoteContent` 5 用例随功能落地。

剩余建议守卫：无新增——SEC-003 头断言、CI-001 ubuntu job、SEC-005 attestation 均已随修复轮落地；MAINT-001/ARCH-004 的守卫即其重构本身与既有测试。

第二修复轮（2026-09-14 晚）顺带落地的守卫：

1. 并行校验多文件清单用例 ×2（AUD-TEST-005 守卫）。
2. `ScanTargets_CoverEveryTopLevelViewFile` 元契约（AUD-TEST-006 守卫，白名单漂移不可能）。
3. `ThemeSubscriptionTeardownHeadlessTests` 哨兵双相守卫（AUD-TEST-007 守卫）。
4. 198.18/15 放行守卫测试（AUD-SEC-006 接受风险的护栏）。
5. `PercentProgressGate` 四态单测 + 400 文件同桶去重接线测试（AUD-PERF-007 守卫）。

## Verified Strengths

最高杠杆的三项（防止不必要的重构/担忧）：

1. **模态隔离裁定经受住了本窗口两轮 UI 重构**——`ResourcePanelOverlay` 拆分与确认对话框收敛后，七个主叠层交互门与对话框层无闸口结构逐字保持；任何「给对话框层加闸口」的提议仍应拒绝。
2. **上轮审计发现被成批真实落地**——5 项 Medium 中 4 项解决 + 2 项部分解决，每项都有配套测试或文档，无一项是纸面关闭；这验证了「find → fix → add guard」循环在本仓库有效。
3. **网络/文件系统防御纵深真实且有测试**——URL 校验每跳复验、Content-Range 三元组、reparse point 全组件拒绝、并行校验基元线程安全均为读码确认 + 测试钉住，非纸面配置。

## Recommended Priorities

1.（决策后执行）AUD-PERF-001 见证摊销：先以新增的 Verbose 跳过计数日志基准实测，再决定是否引入。
2.（专属设计轮）AUD-PERF-004 横幅位图备忘：Plausible 级补救，需先设计轮播位图的生命周期（复用/失效/陈旧释放）再动手。
3.（随下次触碰）AUD-PERF-005 二次解码调查；AUD-ARCH-005 若再动 Shell 按声明表收敛 Wire/Unwire。
4.（维持接受）AUD-SEC-001/002 与已书面化的 SEC-004/SEC-006/ARCH-007：除非威胁模型变化。

## Audit Method and Limitations

实际执行：

- 仓库发现与规则加载：AGENTS.md、PROJECT_CONVENTIONS.md、CONTEXT.md、ADR-016..023、CI 三工作流、coverage/test 脚本、desktop-launcher 画像（逐文件）。
- 上午轮：四域并行审计通道（架构/安全/测试/性能由只读子代理逐行检索，证据带 file:line）；依赖/供应链、生命周期对账、报告撰写与关键发现复核由主审计执行。
- **晚间第二修复轮（用户指令「按优先级修复并逐阶段提交」）**：7 项复审新立案按杠杆序逐项提交（58edcf8/8b6d3dd/c2701da/cabaa3c/415809d/5f53a6e/93b3335），每阶段聚焦测试后提交，收口全量套件本地实测（单元 1717 通过/0 失败/2 可见跳过 + Headless 178 通过/0 失败）；SEC-006 执行中经 git log -S 复核发现补救已被 5a38be9 刻意否决，转书面化接受。
- **下午复审轮**：主审计逐项读码核实修复轮 9 提交与 6 项开放发现；四个只读子代理（架构/安全/测试/性能）以「已知台账排除清单」独立重扫全树（累计 ~230 次工具调用），返回 14 项候选；主审计对 7 项立案候选全部亲自复核（读源文件 + grep 守卫），5 项降为 advisory。工具证据：`.\test.ps1` 本地实测（单元 1708 通过/0 失败/2 可见跳过 + Headless 177 通过/0 失败，`6ecd7bf`）；`dotnet list package --vulnerable --include-transitive` 三项目零漏洞；依赖文件 git diff 零变更。
- 关键新发现亲自复核：AUD-TEST-005（`DownloadExecutorTests` 8 处单元素清单 + `GameDownloadServiceTests` 零引用 grep）、AUD-SEC-006（`IsPublicAddress` switch 现场）、AUD-PERF-007（逐文件 progress 现场 + 累加器作用域 grep）、AUD-MAINT-003（GamePaths.cs 全文 + 两处字面量现场）、AUD-TEST-006（`ViewFiles` 14 项现场）、AUD-TEST-007（守卫 grep 空 + 订阅行现场）、AUD-MAINT-004（注释现场）。
- 未执行：`coverage.ps1`、`verify.ps1` 全序列（Debug 全套件绿基础上视为充分；Release 配置与覆盖率棘轮状态以 CI 为准）；GitHub Actions 运行历史（`linux-unit-tests` job 首跑绿灯未知，以 CI 记录为准）。
- 局限：性能发现均为代码路径推理，无运行时测量（报告内无未经测量的倍数/毫秒声明）；子代理候选的低置信项（<80）未立案、列入 Advisory 并标注置信度；上轮「DownloadExecutorTests 9 用例钉住并行行为」的说法经复审修正为「钉住代码路径但未钉并发语义」（见 AUD-TEST-005）。
