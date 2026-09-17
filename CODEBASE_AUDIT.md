# 仓库审计报告（当前状态）

> 本报告为 **full 全量重审**（用户指令）：六域审计通道全部执行，对上一基线以来的 16 个提交逐域重验，全部开放发现按现行源码逐项复核。上一报告（2026-09-13 full+delta）已归档至 `.repository-audit/history/2026-09-13-full-audit.md`。
>
> **同日修复轮**：报告定稿后按优先级将可执行发现全部落地（9 个提交，`521f4c1..95b9f8b`），修复后单元 1710 + Headless 177 全绿。详见 Resolved Findings。
>
> **同日复审（2026-09-14 下午，用户指令「全量审查 + 检查报告问题修复情况」）**：①上轮修复轮 9 项提交逐项在工作树读码核实为真实落地（代码 + 守卫测试 + 文档/注释均在，非纸面关闭）；②6 项开放发现证据锚点复核仍准确；③四域只读子代理独立重扫全树 + 主审计逐项核实候选，**新立案 7 项**（1 Medium + 6 Low，见对应节）；④本地实测单元 1708 通过/0 失败/2 可见跳过 + Headless 177 全绿，`dotnet list package --vulnerable` 三项目零漏洞。上午版报告已归档至 `.repository-audit/history/2026-09-14-full-audit.md`。
>
> **第二修复轮（2026-09-14 晚，用户指令「按优先级修复并逐阶段提交」）**：复审新立案 7 项全部落地（6 个提交 + 1 项转接受），每阶段聚焦测试后逐项提交：AUD-TEST-005（`67229b5`）、AUD-SEC-006（`521f4c1`，复核发现其补救已被 `5a38be9` 刻意否决——fake-ip 代理/CDN 依赖这些段，按 SEC-004/ARCH-007 先例转书面化接受 + 守卫）、AUD-TEST-006（`3ea7baa`）、AUD-TEST-007（`b04cb83`）、AUD-MAINT-003（`de2a1ee`）、AUD-PERF-007（`608d888`）、AUD-MAINT-004（`d4ca700`）。开放发现收敛至 6 项 Low（全部决策/设计轮门控），另 4 项 Low 为书面化 accepted-risk。
>
> **同日后续两轮（用户指令「核查更改是否正确」与「检查远端 CI 状态」）**：①核查轮确认第二修复轮 7 项全部真实落地（逐 diff 对账 + 守卫变异验证 + 全量套件实测一致），顺带收口 TEST-006 元契约对 `MainWindowDebugOverlay.axaml` 的匿名放行（`3ea7baa`）并更正 TEST-005 用例计数归属（`c8698ab`）；②CI 对账轮——AUD-CI-001 守卫的两类 Linux 首跑（weekly schedule 与 build.yml push job）均红，红出三例平台假设而非回归（与推送无涉：schedule 跑的是推送前旧 HEAD），立案 AUD-CI-002/003/004 并同日解决（`08c53f8`），守卫本职生效。
>
> **架构评审复核轮（2026-09-15，用户指令「审查分析报告，确认已修复内容可维护、未引入新问题」）**：对象为架构深化评审（第二轮）已落地的 7 个候选（提交 `65bc9c2..484e17b` ＋ 工作树中的候选 13）。逐项读码复核 + 门禁本地复现：Debug 0 警告 0 错误 · 单元 1749（0 失败，3 可见平台跳过）· Headless 178（0 失败）· 覆盖率棘轮通过（行 86.90% / 分支 93.26%，slack +1.05pp / +0.56pp）。结论：无改动引入的功能性回归；候选 13 的拉取链路经线程安全（快照只做引用替换）与启动顺序（`ShellLifecycle.cs:230` 播种早于 `:242`）两处专门核实成立。复核收口三处落地残留（候选 03 的 `UnifiedLogPath` 无生产消费者、`LogExportService` 自行拼根内路径、`GameCompatibilityPaths` 被误列为 pre-DI；候选 05 的 `WindowChromeViewModel` 未用 using），并为数据根守卫补反空转基线（原先「一个文件都没扫到」与「干净」不可区分）。另立案一条 ADR-027 同类缺口并落地 <a href="design/adr/ADR-029-卸载预检失败不再静默.md">ADR-029</a>（卸载预检失败此前折叠为 `null` 后静默返回）。本窗口的新发现不以 AUD-xxx 立案：它们全部来自评审候选的落地形态而非六域审计通道，记录见对应 ADR 与评审报告的复核轮说明。
>
> **功能轮复核（2026-09-15，用户指令「对照 codebase 分析报告检查最近更改」）**：对象为报告定稿后的 5 个提交（`a243820..6408c58`，65 文件，+2549/−127），三条主线：<a href="design/adr/ADR-030-卸载可彻底清除.md">ADR-030</a> 卸载可彻底清除（含实机回归后的两处修订）、<a href="design/adr/ADR-031-启动后行为可配置且退出不走关闭路径.md">ADR-031</a> 启动后行为可配置、<a href="design/adr/ADR-032-游戏进程按名字家族识别.md">ADR-032</a> 游戏进程按名字家族识别。逐提交读码 + 门禁本地复现：Debug 与 Release 构建均 0 警告 0 错误 · 单元 1819 通过 / 0 失败 / 1 可见跳过（总 1820）· Headless 184 通过 / 0 失败 · 覆盖率棘轮通过（行 86.96%、分支 93.44%，slack +1.11pp / +0.74pp，较上窗口微升）。结论：三条设计与各自 ADR 一致（守卫先行再删、终态落地、启动退出绕开关闭路径、闸门换名字家族），无回归，报告既有开放项的锚点未失效。**新立案 1 项 Low：AUD-ARCH-008**（卸载的执行边界不复查「游戏在跑」闸门——确认框打开期间的跨越窗口无人拦）。另实测否掉一条候选：反作弊残留的 `Xigncode:{GUID}` 条目在 .NET 上抛的是 `FileNotFoundException`（IOException），`DirectoryTreeDeleter` 与 `DirectorySizeProbe` 都接得住，删者额外兜 `ArgumentException` 属冗余防御而非缺口。
>
> **同日功能轮跟进（用户指令「可以」——按该条建议 (a) 修复）**：`GameUninstallService.UninstallAsync` 在读到安装状态之后、删任何东西之前复查同一道进程家族闸门，判据与拒绝文案收进单一私有实现 `FindRunningGameFailureAsync`（`ValidateAsync` 也改走它，两道闸门不会各说各话）；新增 `GameUninstallServiceTests.UninstallAsync_WhenTheGameStartedAfterThePrecheck_RefusesAndDeletesNothing` 并做**变异验证**（把复查改成空判据后该用例与预检用例同时变红，再还原）；ADR-032 增第 7 条决策与守卫说明、AGENTS.md 游戏操作段同步。聚焦实测 140 用例（卸载/旅程/安装状态/VM 四类）全绿，随后的全量门禁复跑：Debug/Release 各 0 警告 0 错误 · 单元 1820 通过 / 0 失败 / 1 可见跳过（总 1821）· Headless 184 通过 / 0 失败 · 覆盖率行 87.00% / 分支 93.45%（slack +1.15pp / +0.75pp）。该修复即 `6408c58`。
>
> **同日下载侧跟进（用户追问「下载/修复/更新是否没有『游戏在跑』的闸门」，核实后按三条建议落地）**：闸门本就存在（`DownloadSession.PrepareDownloadPlanAsync`，安装/更新/修复/续传都过它），但有三处真缺口——本地配置不存在时判据为空而放行、只在计划阶段查一次、报法不点名。**立案 AUD-ARCH-009 并同日解决**：①没有本地配置时退回远端配置声明的启动程序名；②进入写入之前（`RemoveFiles` / `InstallDownloadedFilesAsync` 之前）复查同一道闸门，失败即返回、`.tmp` 留在盘上可重试；③判法与报法各收一处（`FindRunningGameFailureAsync`、`GameProcessNames.DescribeForDisplay`），下载与卸载两个入口报出的进程名从此一致，下载文案改为点名。全量门禁复跑：Debug/Release 各 0 警告 0 错误 · 单元 1823 通过 / 0 失败（总 1824）· Headless 184 通过 / 0 失败 · 覆盖率行 87.01% / 分支 93.46%（slack +1.16pp / +0.76pp）；新增两条下载侧守卫均做变异验证（拆掉即红）。该修复即 `6408c58`。
>
> **同日 CI 对账复核（用户指令「Push」后顺带核对远端）**：推送 `6408c58..5dd6938` 成功（`build` 作业绿），但 `linux-unit-tests` 作业红——且 `gh run list` 显示它自落地起对五次 push 全红（`e49b1d4`/`2d169bf`/`a243820`/`098166a`/`6408c58`），四条失败与本窗口代码无关：两例是测试绑定了 Windows 的精确异常类型（`ShellLifecycleTests`，Linux 抛子类 `DirectoryNotFoundException`），两例是靠文件共享制造「删不掉的条目」而 POSIX 允许 unlink 已打开文件（`DirectoryTreeDeleterTests` 与 `GameUninstallServiceTests` 的 ADR-030 用例）。本轮新增的守卫用例在 Linux 上通过（1801 通过 / 4 失败 / 16 跳过）。结论：AUD-CI-001 的建议已落地但**那个作业一直红、且非 required**，保护没真的生效——立案 AUD-CI-005 并结案 CI-001；本轮开放计数改为 7 项 Low。
>
> **同日 CI 修复（用户指令「提交之后修复 ci 问题」）**：四处测试侧平台假设按 AUD-CI-005 的建议修掉（两例放宽为接受派生类型与 IO 家族类型名、两例按同族先例加 Windows 可见跳过），`08c53f8` 推送后 **`linux-unit-tests` 首次绿**（run 34967403211：1806 通过 / 0 失败 / 18 可见跳过，新增的 2 条门控与设计一致；`build` 侧单元 1823 / 跳过 1、Headless 184、覆盖率棘轮全绿）。CI-005 结案，开放计数回到 6 项 Low；残留一半（作业升为 required check）在规则集侧，只有仓库管理员能做，已记进 Recommended Priorities。
>
> **架构评审复核轮（2026-09-15 晚，用户指令「根据架构评审报告审查项目更改」→「按顺序处理问题，每阶段之后提交」）**：先对第二轮架构评审（`architecture-review-2026-09-14.html`）已落地的 7 个候选做独立复核（**结论：全部真实落地，无纸面关闭**；未落地的 7 项锚点也逐条重验仍成立），复核期间发现的问题按用户给定的顺序逐项修复，每个阶段单独提交（`098166a..608d888`，13 个提交）。修复清单：①Unix 测试隔离缺口——受管兼容子树从真实 XDG 路径派生，跑单元套件会写好再删掉开发机真实的 `~/.local/share/cafe-launcher/compatibility/<gameId>`（CI 容器是新的所以一直绿）；②彻底清除的成功提示对「Prefix 已保留」说了假话（Prefix 落在安装目录内时整棵被删，提示却说保留）；③有残留时「Prefix 已保留」整条消失（新增第四态文案，键数 564 → 565）；④卸载侧「家族来自配置」的接线守卫形同虚设（替身忽略入参、夹具用合成名），ADR-032 与审计的过度声明一并更正；⑤`params` 带引号或尾随空白时游戏可执行文件被静默移出家族（判据与提取的归一口径不一致）；⑥设置写入方所有权守卫的两处拼写依赖（按声明认人，补上 `DownloadSessionContext` 与组合根两个真实持有者）；⑦XAML 键契约失败信息行号被多行注释带偏（实测 4 文件 160 处、最多偏 5 行）；⑧三处守卫强度（内容扫描无基线且扫 bin/obj、形状断言只看参数、策略表 28 行 `InlineData` 可漏行）；⑨尺寸展示与实际删除分叉（链接根按目标体积报数，而删除会拒绝）；⑩零差异的安装状态提交漏了写入边界复查（唯一绕过闸门的安装目录写入）；⑪全新安装的远端兜底漏掉 `params`（ADR-032 原文把漏掉的对象写成了反作弊宿主，方向相反）；⑫路径守卫的英文内部文案直接进本地化提示（新增 `uninstallRefusedByPathGuard`，键数 565 → 566）。另**新立案并同日修复 AUD-PERF-008**：并行校验的进度门控判据是「值变化」，落后的回调会把走过的桶再报一遍（coverage 门禁因此偶发红；归因核实与本轮提交无关——`git diff --stat` 对本轮未触碰的三个文件为空）。全部修复均做变异验证。文档同步：ADR-026/027/030/032、AGENTS.md、CONTEXT 相关段落按更正后的口径重写，并补齐此前未记的已知限制（Unix 下闸门因 `comm` 15 字符截断而结构上失效、进程枚举失败按「没在跑」放行的后果、托盘退出与关窗手势在检查点去留上的不对称）。门禁实测：Debug/Release 构建各 0 警告 0 错误 · 单元 1821 通过 / 0 失败 / 2 可见跳过（总 1823）· Headless 184 通过 / 0 失败 · 覆盖率行 87.05% / 分支 93.48%（slack +1.20pp / +0.78pp）。开放计数保持 6 项 Low + 4 项 accepted-risk。

> **同日深夜 CI 复查（用户指令「修复问题」）**：`linux-unit-tests` 在 AUD-CI-005 结案后再度转红，且**这次不是测试侧**——`6db3cdd`（随 `6408c58` 折叠，即上一轮功能复核第 ⑤ 项）新增的 `GameProcessNamesTests.FromLaunchConfiguration_NormalizesQuotedAndPaddedParameters` 断言了 Windows 路径语义，而生产侧 `GameProcessNames.WithoutExtension` 用 `Path.GetFileName` 取文件名：Unix 上 `\` 不是路径分隔符，`"C:\dir\BlueArchive.exe"` 整条被当成文件名（得到 `C:\dir\BlueArchive`），`params` 声明的那个可执行文件（`BlueArchive`，实机上正是游戏本体进程）因此静默移出家族——判据少一半，正是 ADR-032 要避免的那件事；而 Windows 的 `Path` 语义恰好与配置形状一致，本机与 `build` 作业上结构性看不见。**立案 AUD-CI-006 并同日解决**（`8461044`）：路径切分改为把 `\` 与 `/` 一起当分隔符（`GameProcessNames` 随之不再触碰任何宿主平台 API），补 `WithoutExtension_SplitsPath_IndependentlyOfHostPlatform` 钉住同一批输入在两平台结论相同；推送后 run 34993079616 **两作业全绿**（`linux-unit-tests` 1m31s、`build` 6m15s），本窗口首次整跑全绿。开放计数不变（同日立案即结案）。另记：本窗口未发布增量经一次折叠重排（自 `v1.1.0-beta.9` 起 117 笔 → 76 笔），本报告与 `.repository-audit` 下的归档报告、`findings.json`、`audit-state.json` 里的提交号引用已随之重映射（`dc5b14b`），引用保持可解析。

> **同日深夜 CI 续查（用户指令「修复问题」的后续）**：`linux-unit-tests` 转绿后，`build` 作业又在**与改动无关的提交**上红了一次（`857900f` 仅文档改动，红在 `DownloadExecutorTests` 的 400 文件去重用例：`Assert.Equal(0, delivered[0])` 实到 1），随后同一文件的 12 文件并行用例又在 Linux 作业上红（`Assert.Equal(fileCount, progressCount)` 实到 11）。两次根因都在测试侧：进度回调由并行 worker 调用（校验 ≤8、下载 ≤10 个并发传输），而用例把回调收进未加锁的 `List<T>`、或做非原子自增；`delivered[0] == 0` 还断言了单调门控并不承诺的到达顺序。**立案 AUD-TEST-008 并同日解决**（`e83334b` 收口该用例，`0060855` 一次收完同类站点：新增共享替身 `CallbackRecorder<T>`，校验与下载两阶段喂给测试的回调全部换到它上面，共 13 处；断言改为与到达顺序无关）。本机复现不出（同一用例连跑 12 次全绿），结论来自 CI 日志 + 读码。发布提交 `e1045b6` 的 `Build` 作业 success（两套件 + 覆盖率棘轮），`linux-unit-tests` 在 `0060855` 上绿。开放计数不变（同日立案即结案）。

> **测试设施轮（2026-09-16，用户指令「测试设施复用与可靠性改进」）**：以测试体系为对象做了一轮「先维护成本与隔离、再执行速度」的重构，分三项交付：①**共享设施收敛**——新增 `tests/Support/{TestDirectory,TestRepository,TestWait}.cs`（两工程 Compile-Link 共用），43 个测试文件的临时目录惯例、5 处各自的异步等待实现、仓库/资源定位与 `.resx` 解析全部换到它们上面；②**上下文所有权明确**——主窗口装配迁入 `MainWindowTestContext`（先前装配方法里的局部 `using` 日志在返回时即被释放，而 `SettingsViewModel`/`LogViewerDialogViewModel` 长期持有它们），`AddLauncherServices` 增可选 `launcherDataRoot`，无头上下文改为每用例一个独立数据根；③**反馈效率**——资源面板代理用例拆为呈现层与传输层回环两条（原单条 10.26s 靠「接受连接即断开、等真实传输退避走完」结束），`test.ps1` 增 `-Suite`/`-Filter`。同配置实测（Debug）：单元 1831 → 1853 条、测试用时 31.8s → 23.9s（最慢用例 10.26s → 2.07s），无头 185 条同量级；两套件 0 失败、可见跳过数不变、golden 基线未更新且全绿；受影响异步用例 19 条单元 + 13 条无头各重复 10 次全绿；`verify.ps1` 全绿（覆盖率行 87.17% / 分支 93.62%，余量 +1.32pp / +0.92pp）。立案 AUD-TEST-009 并同日结案（见 Low 节）；串行执行、Windows CI 双跑、`-Filter` 不进 CI 记为**后续性能机会**，本轮明确不碰。

> **简并扫描轮 + 计划落地（2026-09-16，用户指令「全量扫描项目中可简化，可重用的逻辑 并编写修改计划」→「先实现一阶段」→「提交」→「落到 main，之后做 a3」→「进入 b」→「收尾 b17」→「c」→「d1?」→「聚焦 d14」→「可以」→「那现在怎么办」→「可以」）**：本窗口先以 8 路只读子代理对全树做「可简化／可重用逻辑」扫描（覆盖 `Services/`、`GameOperations/`、其余 `Features/`、`ViewModels/`+`Views/`+`Controls/`+`Converters/`、`Helpers/`+`Models/`+`Constants/`+`Composition/`+`scripts/`、单测 A–M、单测 N–Z、无头+`TestDoubles`+`Support`+XAML），产出报告与计划 `docs/design/simplification-reuse-plan-2026-09-16.md`（91 个候选去重为阶段 A 测试设施复用 13／B 生产侧等价收敛 22／C 死代码删除 8／D 结构收敛 17／E 登记不排期 9，另 22 项复核后明确不做）。扫描副产物另被证据钉住 6 项正确性问题与 2 项守卫缺口，本窗口立案 8 项：`AUD-ARCH-010`（**卸载删除清单文件不清只读属性、路径守卫宽于下载侧——已随 beta.9／beta.10 出货，用户可见**；`d2d4bc6` 抽出 `ManifestFileRemover` 由下载与卸载共用，变异验证拆掉只读清除即 3 条用例变红）、`AUD-ARCH-011`（`GameShortcutService` 公开创建路径绕过平台接缝；`56e0509`）、`AUD-MAINT-005`（日志级别词表两份独立声明且无守卫；`cfe9648` 补六级别往返守卫）、`AUD-TEST-011`（破坏性路径用例默认绑定真实进程扫描器；`7161f4c` 17 处默认改 `TestGameProcessTracker.None`，变异验证改成「在跑」即 36 条变红）**四项同日解决**；`AUD-ARCH-012`（journey 的 `Ready` 分支不报告拒绝）、`AUD-TEST-010`（**动效叠层守卫清单已实际漂移**：`DesignGalleryOverlay.axaml` 带 `motion-overlay` 却不在扫描集内，而 `Assert.Equal(9, …)` 仍通过——本审计实测该套件 153 条全绿）、`AUD-TEST-012`（提示条拆除时的倒计时唤醒无覆盖，删掉后 86 条用例全绿）、`AUD-TEST-013`（无头套件泄漏 `Application.RequestedThemeVariant`，两处锚点待复核故置信度只给 60）**四项开放**。
>
> 计划落地进度：**阶段 A 全部（13 项）**——测试设施复用与用例装配去重，净 −814 行测试代码、生产零改动、`%TEMP%` 残留目录由 15 个／轮降到 **0 个／轮**；**阶段 B 18/22**——`B5` 并入评审候选 11 待裁决、`B12`／`B15` 评估后判定收益不抵成本（理由见 `0a92d66`）、`B17` 两侧完成（并记下其暴露的 `AUD-TEST-012`）；**阶段 C 6/8**——`C5`（`ResourcePanelService` 转发成员）与 `C8`（四对字节相同的样式）经核实为深模块边界与承重语义标记（两对分别被穷尽集合守卫与无头定位器钉住），判定不做；**阶段 D 2/17**（`D1` 即 `AUD-ARCH-010`；另 `8aab531` 落地 `D14`——主题引擎从设置外观 VM 搬进 `Services/ThemeApplier`，无接口的具体类 + DI 单例，逐行搬移、行为等价，两条 headless 守卫经重新变异验证；「与候选 11/12 同域」的原议经读码复核推翻，裁定记录见计划文档 §5 决策二）。逐提交前跑 `.\verify.ps1` 退出码 0；收口实测单元 **1857 通过 / 0 失败 / 2 可见跳过**、无头 **185 通过 / 0 失败**、覆盖率行 **87.27%** / 分支 **93.63%**（棘轮余量 +1.42pp / +0.93pp），Release 构建 0 警告 0 错误。
>
> 本窗口另记两条方法论结论：①**涉及「是否存在泄漏」的条目必须以实测为准**——计划原把 22 处 `%TEMP%` 字面量计为泄漏，按计划自带的验证协议（比对 `%TEMP%` 顶层改名集合）实测后证伪一半：基线 15 个／轮 → 那 13 处收敛后 14 个／轮，即只去掉 1 处真实泄漏；真正的泄漏源是 14 处无人释放的 `TestDirectory` 局部变量，静态清点字面量会把「指向不存在目录的路径串」误计为泄漏。②**扫描的计数多次与实情不符，逐项先读码复核是必需的**：`B4` 实为 30 处而非 19 处、`C2` 的 23 处里有 1 处是活的（在 `StackPanel` 上，删掉会丢布局）、`C3` 的 4 个包装里 2 个完全无调用者、`C8` 四对里两对是承重标记。

> **可直接推进项落地（2026-09-17，用户指令「先做可以直接做的」）**：按候选总表 §2 的「不需裁、可直接推进」清单逐项落地，一条工作线内逐项独立提交、每项跑聚焦门禁（收口 `verify.ps1`）：**评审候选 `R2-c14`**（`a97c6b3`，出站通道两出口契约写进 `RemoteHttpTransport` 类文档 ＋ `ADR-022` 交叉引用，纯文档不动接缝）、**阶段 D 的 A 组 10 项**（零外溢：`D2` `bf06ec0`、`D4` `9bf1333`、`D5`、`D6`、`D8`、`D9`、`D10` `68e6efa`、`D11` `ead1ed3`、`D12`、`D16` `5253311`）、**`AUD-TEST-012`**（`4b7b987`）。`AUD-PERF-004` 按本报告既有口径（专属设计轮，扫描明确「不宜顺手改」）**不在本批**。要点：
>
> - `AUD-TEST-012` 结案：`ToastLifecycle` 补 `CountdownTask` 测试缝（`PendingCountdownTask`／`IsCountdownAwaitingResume`），两条守卫分别钉住 `EndLifecycle` 唤醒段的两个半边——挂起等待必须被唤醒、在途显示等待必须被打断；**变异验证**：把唤醒段换成对新记录的 no-op 后两条用例同时变红。
> - 阶段 D 的 A 组特征：`D2` 让一次卸载只读一次安装状态、只枚举一次进程（删除前的边界闸门保留，`AUD-ARCH-008` 的两相守卫不受影响）；`D4` 把七处 `repair` 布尔分叉收进 `DownloadOperationProfile`（检查点字段 `IsRepair` 与两个 `BuildXPlanAsync` 签名不动）；`D5`／`D6` 把无状态工厂（进度快照/失败结果/目录名守卫）与逐文件进度门控搬出 759 行的有状态会话类型；`D8` 五处「最新胜出」刷新槽收进 `Helpers/LatestRefresh`（删掉代数计数与版本号防抖两套自持机制）；`D9` 两处「等到稳定」收进 `Helpers/TaskSettler`；`D10` 语言刷新收敛为根 `ViewModels/ILanguageAwarePresentation`（七处扇出 ＋ 向导的自订阅 → 一个契约，名单装配后注入 Shell，向导经宿主 `DialogsViewModel` 刷新）；`D11` 资源面板装载结果改按位有序列表（五处按 code 查找消失）；`D12` 位图所有权收进 `Helpers/BitmapLifetime`；`D16` `CrashReportWindow` 补齐 `Crash.Layout.*`／`Crash.Typography.*` 并删掉两个从未被消费的孤儿令牌。
> - **守卫净增**：`LatestRefreshTests`（被取代的那次绝不应用其结果／防抖窗内被取代的等待绝不启动）、`TaskSettlerTests`（完成即返回／无在飞即返回／超时放行／等待中被换新落到最新）、`UiStyleContractTests.CrashReport`（族内不留孤儿令牌，带反空转基线／消费点不用裸字面量）、`ToastHostViewModelTests` 两条唤醒守卫、`DialogsViewModelTests` 宿主传播守卫、资源面板与 `DownloadSessionTests` 的按位断言改写。**全部新守卫与关键改写逐条做了变异验证**。
> - **先补测试再改的一处**：`D12` 落实了扫描指出的缺口——横幅流水线此前没有端到端用例（`BannerBitmap` 从无断言）。新用例驱动 `Apply` 断言位图落绑定、内容被替换后已释放；缓存由桩传输喂真实 PNG，走真实 `ImageCacheService` 与解码器。
> - **门禁**：Debug/Release 各 0 警告 0 错误 · 单元 1870 通过 / 0 失败 / 2 可见跳过（总 1872）· 无头 188 通过 / 0 失败（含 `crash-report-window` golden **未重生即通过**，证明 `D16` 像素未动）· 覆盖率行 **87.63%** / 分支 **93.85%**（棘轮余量 +1.78pp / +1.15pp）。开放计数 **7 → 6 项 Low**（余 `PERF-001`／`PERF-004`／`PERF-005`／`SEC-001`／`SEC-002`／`ARCH-005`，全部决策/设计轮门控）；accepted-risk 保持 4 项。
> - **本窗口未立案任何新发现**：这批全部是既有候选的落地，无新缺陷；`D8` 的卡片列了 5 个域内站点 ＋ 1 个「域外同形」站点（`BackgroundViewModel` 的尺寸防抖），本轮只迁域内那 5 处、`BackgroundViewModel` 的版本号防抖按卡片的域外口径留在原地；`D10` 的卡片要求「Shell 遍历呈现族」，实测需再补一步（名单装配后注入 Shell）才能与「测试直调 `Shell.ApplyLanguage`」的既有调用点共用同一条路径。两处偏离均已写进计划文档 §阶段 D 的落地注。

> **重置前台账 20 条逐条复核（2026-09-17，用户指令「先复核」）**：对象是 `.repository-audit/history/2026-09-12-findings-ledger.json` 里 20 条非 resolved 残留——它们是 2026-09-13 台账清零重置时**没有带过来**的唯一记录，总表 §3.5 明写「须先按现行源码逐条复核，才知道哪些还算候选」。本轮逐条读码核验（不转抄旧结论），结论已进候选总表 §3.5 与 `findings.json`：
>
> - **重新立案 5 条**（进现行台账、进 `open-findings` 标记）：`AUD-CI-007`（Release 配置的测试只在 tag 触发的 `release.yml` 里跑：`build.yml:59-60` 是 Debug、`linux-tests.yml:49` 是裸 `dotnet test`、`release.yml:263` 才是 Release，而 build.yml 的 Release `dotnet publish` 只编译不跑测试 → 对构建配置敏感的断言仍只在发版时首次执行。**由原 medium 降档 low**，理由：`release.yml` 发布作业的 `needs: [build, installer]` 使这类失败挡在发布的最后一关、无坏产物外流，代价是一次补提交加重打 tag——属发布流程摩擦。**这是本轮唯一的降档判断，若判不成立改回 medium 即可。**）；`AUD-MAINT-006`（发布横幅版本契约仍无机械守卫：`ReleaseBannerContractTests` 只读模板 JSON、全文件无 `File.Exists`/解码/尺寸断言，`release.yml:152-155` 只 `Test-Path`，指南 `:3`/`:229` 声明 2000×1125 而实测 beta.1–5 为 2400×1350）；`AUD-MAINT-007`（`LocalInstallationStateStore.cs:17` 类注释仍写 `game_config.json`，实际是 `GamePaths.cs:30` 的 `game-launcher-config.json`）；`AUD-TEST-014`（**实测仍有 14 处裸 `await` 门控等待**无上限：`LogExportDialogViewModelTests` 6 处、`LogViewerDialogViewModelTests` 3 处、`MainWindowViewModelTests.Appearance` 2 处、`MainWindowHeadlessTests.Golden`/`ThemeColorExtractionAsyncHeadlessTests`/`MotionVisibilityTests` 各 1 处——末者与同文件四处已加 `WaitAsync(5s)` 的做法自相矛盾；`Tests.csproj:20-26` 的抑制理由仍称「门控等待统一用 WaitAsync/预算轮询加超时上限」。**并更正本报告 Testing 节「正面等待全部有截止/迭代上限（复审全树检索无悬挂面）」——该表述对这 14 处不成立**）；`AUD-ARCH-013`（「游戏在跑时拒绝执行」与官方「安装/更新时强杀游戏目录内进程」的相对分歧仍无成文记录：拒绝语义本身已在 `ADR-032` 决策 5–8 与 `CONTEXT.md` 词条成文，但受版本管理文档里 `强杀` 只命中 ADR-032:20 一处且讲的是别的事。证据限制已写明：「官方强杀」一侧的描述来自按 `AUD-MAINT-002` 决定不入库的对比文档，需重新取证或按外部契约表述）。
> - **已失效或已收口 6 条**：`MTN-020`（`CLAUDE.md` 已不在仓库）、`PERF-012`（即已解决的 `AUD-PERF-007`）、`DEP-010`（`linux-tests.yml:9-10` 的每周 cron 会做还原+构建，`NuGetAudit` 因此每周至少执行一次）、`DEP-011`（`AGENTS.md` 依赖升级段现有「手工更新 §12 工具链表」第 3 步）、`ARCH-003`（轮播计时器已抽出为 `Helpers/CarouselTimer`，`RemoteContentViewModel` 内 `DispatcherTimer` 零命中）、`DOC-003`（分析文档连工作树也已移除，由现行 `AUD-MAINT-002` 继承）。
> - **已裁定不行动 1 条**：`ARCH-006`（`ModalEntry.Content` 只写不读）——评审第二轮显式列入「不重开／不触碰／不行动」，代码形态未变。
> - **处置维持 No Action／Accept Risk 3 条**（原台账即如此判定，不进开放集，仅在总表 §3.5 留档）：`MTN-018`（`BannerImageDecoder.cs:30-43` ≡ `BackgroundImageDecoder.cs:99-115` 策略体仍逐行相同，阈值常量已共享）、`ARCH-007`（`LocalDiagnostics.cs:24` 静态字段 + `:50-51` 注册 + `:128-144` 静态入口都在，静态调用点 26 → 18；**与现行 `AUD-ARCH-007` 同号不同题**）、`SEC-008`（`LocalInstallationStateStore.cs:73-74` 仍固定 `{path}.tmp`，而 `AtomicJsonFileStore.cs:39` 已随机化）。
> - **维持暂缓或接受 4 条**：`MTN-001`（`RemoteContentViewModel` 现 722 行未拆；`IModalPresenter` 半项已被根 `ViewModels/` 契约取代）、`TST-001`（`GameDownloadServiceTests.cs:1240-1268` 真实限速 + `Stopwatch` 下限断言仍在）、`DEP-002`（依赖仍在 `Directory.Packages.props:24`）、`DEP-012`（`AUD-SEC-005` 的 attestation 是「来源可验证」而非「发布者身份可验证」）。
> - **并入既有待裁项 1 条**：`MTN-017`（新增主叠层仍有未守卫编辑点）——其「语言刷新清单漏改静默失败」半项已由 `D10`（`68e6efa`）收口（手工逐处调用 → 装配后注入的单一名单 + 一个契约），剩余即 `R2-c08` 提议的那条补守卫，故不重复立案。
> - **编号提醒**：重新立案一律用**新编号**（`AUD-CI-007`、`AUD-MAINT-006/007`、`AUD-TEST-014`、`AUD-ARCH-013`）。重置前的旧编号已被 2026-09-13 的清零重置回收再利用（旧 `ARCH-007` 是同步 logger 静态字段，现行 `ARCH-007` 是代理指纹变化处置在途 handler），复用会造成同号两题。
> - **文档后果**：候选总表 §4 的「删归档目录前先搬 §3.5 那 20 条」这一前置条件**就此解除**（只剩四份 release 放行证据需先抽成 release-gate 台账）。开放计数 **6 → 11 项 Low**（新立案 5 条），informational 0、accepted-risk 4。

## Audit Metadata

- 日期：2026-09-14（上午 full 六域重审 + 下午修复核实轮/独立重扫 + 晚间第二修复轮）
- Commit：审计基线 `67229b5`（`main`；最新已发布 tag `v1.1.0-beta.9`）；第二修复轮后 HEAD `d4ca700`
- 最近复核窗口（2026-09-15 功能轮）：`a243820..6408c58` 5 提交，HEAD `6408c58`
- 最近复核窗口（2026-09-15 架构评审复核轮）：`3401794..608d888` 13 提交，HEAD `608d888`（对象为评审报告已落地候选的独立复核 + 复核发现的问题按序修复；新立案 1 项 AUD-PERF-008 并同日解决）
- 模式：**full**（上午：`1619d35..67229b5` 16 提交六域重验；下午：HEAD `dc1a6ef` 全树独立重扫 + 修复核实）
- 范围：生产源码 251 个 `.cs`（≈35.1k 行）+ 29 个 `.axaml`、224 个测试文件（≈46.3k 行）、CI 三工作流、打包/安装器脚本、文档契约
- 项目画像：desktop-launcher（`.agents/skills/repository-audit/profiles/desktop-launcher.md` 按仓库证据调整）

## Executive Summary

仓库健康状况：**良好，且较上一审计实质性改善**。desktop-launcher 四个关键风险面（下载完整性、文件系统边界、进程启动、外部链接）防御纵深不变且全部有测试；上一轮全部 5 项 Medium 级结构/测试发现中 4 项已随 `1f09bc8`/`a6d3794`/`08c53f8`/`cf353cd`/`67229b5` 真实解决（不是纸面解决——本审计逐项读码 + 本地实测全绿确认），其余 2 项（AUD-PERF-001、AUD-CI-001）部分解决后降档。同日修复轮 9 项提交经复审逐项读码核实为真实落地。**复审新立案 1 项 Medium（测试覆盖缺口），其余 6 项新发现为 Low。**

开放发现（2026-09-17 可直接推进项落地 + 历史遗留复核后）：

- Critical：0
- High：0
- Medium：0
- Low：11 open（决策/设计轮门控：AUD-PERF-001、AUD-PERF-004、AUD-PERF-005 残留、AUD-SEC-001、AUD-SEC-002、AUD-ARCH-005 ＋ 2026-09-17 从重置前台账重新立案的 5 条：AUD-CI-007、AUD-MAINT-006、AUD-MAINT-007、AUD-TEST-014、AUD-ARCH-013）+ 4 accepted-risk（MAINT-002、SEC-004、SEC-006、ARCH-007）
- 本窗口解决：14 项（8 项随上轮修复落地：ARCH-002/003、TEST-002/003/004、PERF-002/003、MAINT-002；6 项随同日修复轮：ARCH-004/006、CI-001、PERF-006、SEC-003/005）；第二修复轮再解决 6 项（TEST-005/006/007、MAINT-003/004、PERF-007）并将 SEC-006 结案为 accepted-risk；CI 对账轮新立案 3 项（AUD-CI-002/003/004）并同日全部解决；功能轮（2026-09-15）新立案 1 项（ARCH-008）并于同日跟进按建议 (a) 解决（`6408c58`）；随后按用户追问再立案 AUD-ARCH-009（下载/安装/修复闸门的三处缺口）并同日解决（`6408c58`）；CI 对账复核结案 AUD-CI-001（其建议已落地为 `build.yml` 的 push/PR linux 作业）、新立案 AUD-CI-005（该作业连续红且非 required）并于同日修复、作业首绿（`08c53f8`）；CI 复查（2026-09-15 深夜）新立案 AUD-CI-006（生产侧平台假设：`Path.GetFileName` 在 Unix 上切不开配置里 Windows 形状的 `params`，游戏可执行文件静默移出家族）并同日解决（`8461044`）；CI 续查（2026-09-15 深夜）新立案 AUD-TEST-008（并行校验／下载路径上测试侧收集未加锁或非原子自增，两次 CI 偶发红）并同日解决（`e83334b` + `0060855`）；计划落地期间 `779664f` 解决 AUD-TEST-013（= `DEF-4`，无头套件主题变体泄漏：golden 侧钉住基线值 + 新增 `ThemeVariantSnapshot` 设施，两条新守卫各做变异验证，两处锚点复核为一真一假）；`59647cc` 解决 AUD-TEST-010（= `DEF-2`，动效叠层扫描改为按目录发现 + 反空转基线）、`588d80c` 解决 AUD-ARCH-012（= `DEF-5`，快照过期的拒绝改为可见，同批 `D3` 把修复的两道闸门收进旅程并钉住「不得停在 Progress」的顺序不变量）；同窗口另有 `B5`（`28b842b`）把 `LocalDiagnostics` 的九处包装收成两个核心，其守卫由加强后的 `LocalDiagnostics_NewFacades_WriteExpectedLevels` 承担

**一处上轮审计证据更正（重要）**：上轮安全节声明「签名 Authorization 头绝不跟随重定向转发」——复核证实该头经 `RemoteRequestOptions.ConfigureRequest` 钩子在**每一重定向跳重发**（含跨主机），已立案为 AUD-SEC-003（Low）。这推翻了上轮对 DNS 重绑定残余风险影响边界的部分论证。

最重要的风险/行动（第二修复轮后剩余，全部为决策/设计轮门控）：

1. **AUD-PERF-001**（Low）— 更新路径未变更文件的全读是文档化的损坏自愈设计（与官方启动器行为一致），已并行化 ≤8 + 下载期哈希跨轮复用；剩余的见证摊销须先基准实测再决策，不得弱化自愈语义。
2. **AUD-PERF-004**（Low）— 每次壳层刷新全量重解码横幅位图；位图备忘的补救验证为 Plausible，需要专属设计轮处理轮播/陈旧释放生命周期，不宜顺手改。
3. **AUD-PERF-005 残留**（Low）— 首帧权衡已文档化（700e674）；二次冗余解码的消除需先确认跳过卫可合法匹配。
4. **AUD-ARCH-005**（Low，接受中）— ShellLifecycle 的 Wire/Unwire 密度：若再因结构原因触碰，按 ADR-023 声明表收敛。

## Changes Since Previous Audit

`1619d35..67229b5`，16 个提交，+2059/−617 行，49 个文件。主线是**上轮审计发现的成批落地**：

- `08c53f8` test(platform)：平台分支跳过可见化（`Assert.Skip*` 16 处）+ 新增 `linux-tests.yml`（ubuntu-24.04 单元套件，dispatch + 每周）。
- `a6d3794` test(update)：新增 `SettingsViewModelTests`（4 用例）+ `ShellLifecycleTests` 扩至 11 用例，补齐更新检查「服务→UI」粘合层。
- `1f09bc8` perf(download)：停止/暂停/恢复点击路径去同步阻塞；下载缓冲 ArrayPool 池化；测试观察窗按生产节奏校准。
- `cf353cd` refactor(views)：操作表面动效套件抽出为 `OperationSurfaceAnimator`（190 行），`MainWindow.axaml.cs` 663→499 行。
- `67229b5` perf(install)：更新校验改有界并行（≤8）；ShellLifecycle 测试缝所有权制度书面化。
- 功能线：资源面板三连（`f62eab5` 版本一致单行结论、`dfbf8d8` 刷新保留旧数据 + 保存按钮脏检查、`f62eab5` 状态条去重），覆盖层拆分 `ResourcePanelOverlay`（`e56d54b`）；`07c4c8d` 系统代理解析对齐 WinINet；`045ec00` 社交芯片悬停态专用 token；`c826f8a` **revert** 恢复内置壁纸构造期同步解码（见 AUD-PERF-005）。
- 审计自身：`7eca8fe` 上轮 delta 复核入库；`54c1871` 架构评审报告入库 `docs/architecture-review-2026-09-13.html`（上轮的未跟踪待决项就此闭合）。
- 依赖：`Directory.Packages.props`、三份 `packages.lock.json`、`global.json` 零变更（git diff 证实），依赖结论无需重扫；`dotnet list package --vulnerable --include-transitive` 本审计实测三项目均无漏洞包。

**功能轮（2026-09-15，报告定稿后的 5 个提交 `a243820..6408c58`，65 文件，+2549/−127）**

- `098166a` ＋ `098166a` feat/fix(game-ops) **ADR-030**：卸载确认框多一个默认不勾的「彻底清除」可选行（`Controls/ConfirmDialog.axaml:28-36` 新增 `OptionText`/`IsOptionChecked` 槽，未设 `OptionText` 时整行折叠，9 处用量的既有解剖契约不变）；标准卸载也删桌面快捷方式；彻底清除删整棵安装目录与受管 `compatibility/<gameId>` 子树，用户自定义在受管根之外的 Prefix 保留并在完成文案里说明。`Helpers/DirectoryTreeDeleter` 自己遍历（不用 `Directory.Delete(path, recursive: true)`：树内一个 junction 就会让后者抛 `UnauthorizedAccessException`），删不掉的条目不中断、按路径交回调用方；`Helpers/DirectorySizeProbe` 为确认框量同一批目标，对话框先弹、尺寸后回填（实测 37k 文件/24 GB 的树要 3.4 秒）。终态交给 `ShowOperationResult`——返回失败与**抛出**两条路径都有可见反馈，此前结果被丢弃。
- `c8a0474` feat(settings) **ADR-031**：新增持久化设置 `afterLaunchBehavior`（`keepOpen`/`minimize`/`exit`，默认 `minimize`，归一化拒绝未知值），常规分区「应用偏好」下一行；规则在旅程里按快照分派，呈现层仍是两个单用途窗口动词（`RequestMinimize`/`RequestExit`）；退出刻意不走 `PerformClose`——那条路会把关闭意图交给 `CloseBehavior` 解释，用户在那里选「最小化到托盘」时本设置会退化成静默隐藏——改走抽出的 `RequestShutdown`（`Views/MainWindow.axaml.cs:333-344`，`PerformClose` 的退出分支共用）。
- `c8a0474` fix(settings)：`SavedSettingsWriter` 的编辑器收口落到 UI 线程（`Dispatcher.UIThread.InvokeAsync`；无 Avalonia 应用或已在 UI 线程则就地收口）。此前落盘续体在线程池线程上就地写绑定可观察状态，保存设置会撞 `VerifyAccess` 报「保存启动器设置失败」。
- `6408c58` fix(game-ops) **ADR-032**：进程识别从「配置里那一个宿主名」换成**名字家族**（`Services/GameRuntime/GameProcessNames`：宿主名 + `params` 里的可执行文件，再按 `_` 为界收同族变体，单段名不认领整族）；`IGameProcessTracker` 返回值由布尔改为实际在跑的进程名列表（报出的名字就是用户看到的，宿主已退出时不再假报宿主）；卸载预检与下载/安装/修复两条闸门同判据；走一次系统快照而非每名字一扫。判据按名字是因为实测三个进程的镜像路径全读不到（反作弊保护进程对象）。顺带修掉 `GameDownloadServiceTests` 用真实进程扫描导致的随环境变色。
- 文档：三份 ADR（030/031/032）、`CONTEXT.md` 三条词条（游戏进程家族、彻底清除、启动后行为）与 ADR 索引、`AGENTS.md` 游戏操作段（进程家族闸门、`UninstallScope`、递归删除与「对话框先弹、尺寸后到」）；`settings-overlay.png` 金标准随新增设置行重生。
- 依赖：`Directory.Packages.props`、三份 `packages.lock.json`、`global.json` 零变更（git diff 证实）。

## Critical Issues

无。

## High Priority Findings

无。

## Medium Priority Findings

### AUD-TEST-005 — 有界并行安装校验重写（67229b5）无多文件清单测试，并发语义全部未钉住【复审新立案；已解决 `67229b5`】

- 类别：测试 / 关键路径保护
- 严重度：Medium｜置信度：85（主审计直接核实）｜状态：**resolved**（`67229b5`）｜处置：Add Guard（已执行）
- **证据**：`DownloadExecutorTests` 全部 8 处清单参数均为单元素 `[manifestFile]`（`tests/Cafe.Launcher.Avalonia.Tests/DownloadExecutorTests.cs:38-39,:65-66,:94-95,:122,:148`）；`GameDownloadServiceTests` 零处引用 `InstallDownloadedFilesAsync`（grep 证实）。提交 `67229b5` 的 stat 只含 `DownloadExecutor.cs` 与 `ShellLifecycle.cs`，无测试文件——并发落地时未伴随测试扩容。
- **影响**：`SemaphoreSlim(≤8)` 门控、按索引 `failedFlags` 重组、乱序进度回调的**产生侧**、`WhenAll` 取消扇出均无用例可检出回归（如边界死锁、失败标志丢失、顺序破坏）。该路径是下载完整性的执行端。上轮「9 用例经并行路径钉住行为」的说法对并发维度 overstated——用例确实走并行代码路径，但每次只有 1 个文件，从不产生竞争。显示侧乱序回退已由 `608d888` 的钳制测试钉住，产生侧未钉。
- **建议**：补一个多文件清单用例（含故意的哈希不匹配文件 + 文件数 > 并行度），断言失败重组、按清单序重试与成功计数。**建议验证**：Verified（机械测试扩容，被测 API 现成）。
- **解决记录（`67229b5`）**：`DownloadExecutorTests` 增至 13 用例（随后的 `608d888` 另补 400 文件接线测试，至 14）——12 文件（>并行度 8）混合布局断言失败列表按清单序重组、失配 .tmp/终路径删除、通过文件搬移、进度每文件一次；缺失 .tmp 只标记该文件失败。

### AUD-ARCH-010 — 卸载删除清单文件不清只读属性、且路径守卫宽于下载侧：清单里一个只读文件就让整次卸载失败（已随 v1.1.0-beta.9／beta.10 出货）【简并扫描轮新立案；同日解决 `d2d4bc6`】

- 类别：架构 / 执行路径一致性（用户可见）
- 严重度：Medium｜置信度：95（主审计逐行读码核实两处分叉 + 变异验证）｜状态：**resolved**（`d2d4bc6`）｜处置：Fix（已执行）
- **证据**：`Features/GameOperations/GameUninstallService.cs:128-139` 用 `GamePathValidator.GetSafePath` + 裸 `File.Delete`，只兜 `FileNotFoundException`；而 `Features/GameOperations/DownloadExecutor.cs:408-417` 的 `RemoveFiles` 与私有 `DeleteExistingFile` 用 `GetSafeFilePath` 且删除前清 `FileAttributes.ReadOnly`，其 doc 注释原文记录了「不清会让安装/更新直接中止」那次事故——**同一问题修在了下载侧，卸载侧没跟上**。第二处分叉：`GetSafePath` 不拒归一到游戏根目录自身的条目（空路径、`"."`、`"sub/.."`），`File.Delete` 会落在游戏根目录上。
- **影响**：清单里任一文件带只读属性（手工拷贝过、或被打过更新包标记，都是常见形态）→ `UnauthorizedAccessException` 不被兜住 → **整次卸载失败**，而同一批文件在更新路径上是能删的。
- **发布归属**：`git grep "Already gone" v1.1.0-beta.9 -- "*GameUninstallService.cs"` 命中，`v1.1.0-beta.10`（2026-09-16 打标签、HEAD 的祖先）同样含该循环 → **已出货**，下一版需一条面向用户的 `fix` 条目。
- **建议**：抽出 feature 内的清单删除器，采用下载侧的 `GetSafeFilePath` + 清只读 + 宽容语义，由两处共用；卸载侧的 `PercentProgressGate` 保持在调用方。
- **解决记录**：新增 `Features/GameOperations/ManifestFileRemover`，三条语义收成一处（`GetSafeFilePath` 解析、删除前清只读、已不在盘上不算错误），`DownloadExecutor` 的 `RemoveFiles` 与 `DeleteExistingFile` 搬过去（后者同时服务校验不匹配与安装覆盖两处），卸载侧改调它。新增 `GameUninstallServiceTests.UninstallAsync_WhenAManifestFileIsReadOnly_RemovesItInsteadOfFailing`；**变异验证**：删掉只读清除后 **3 条用例同时变红**（新增的卸载用例 + 下载侧既有的 `RemoveFiles_WhenFileIsReadOnly_DeletesFile` 与 `InstallDownloadedFilesAsync_WhenTargetFileIsReadOnly_ReplacesInstalledFile`），即三条删除路径都被真实覆盖；该变异只在 Windows 上咬得住（POSIX 允许 unlink 只读文件），已写入用例注释。顺带一处行为变化：`DownloadSession` 现在把 `activeToken` 传给删除循环（原签名没有令牌参数，属签名的偶然；该路径上其它每一步都转发同一令牌，CA2016 亦如此要求）。

## Low Priority Findings

### AUD-SEC-006 — IsPublicAddress 黑名单遗漏 CGNAT 100.64.0.0/10 与基准段 198.18.0.0/15【复审新立案；结案为 accepted-risk `521f4c1`】

- 类别：安全 / 网络（SSRF 分类缺口）
- 严重度：Low｜置信度：85（主审计读码核实）｜状态：**accepted-risk**（`521f4c1`）｜处置：Accept Risk（书面化已执行）
- **证据**：`Services/RemoteHttpUrlValidator.cs:170-179` IPv4 switch 覆盖 `0/10/127/169.254/172.16-31/192.0/192.168/≥224`，`100.64.0.0/10`（CGNAT）与 `198.18.0.0/15`（benchmark）落入 `_ => true` 按公网放行；字面 IP URL 在 `:91-99` 直接判定，主机名解析后在 `:112-124` 同判。
- **影响**：指向这些段的 URL 被直接拨号（无代理）。可达目标包括 Tailscale 节点与 MagicDNS 解析器（100.100.100.100）、CGNAT 网关管理面、WARP/fake-IP 段（198.18/15）。影响有界：端口限 80/443、GET-only、跨主机重定向已剥离凭据（`d7076cb`）、响应不回传攻击方（仅 JSON 解析失败的 16 字节预览入诊断日志，`RemoteHttpTransport.cs:468-476`）。与 AUD-SEC-001 根因不同：那是 validate-then-dial 时序窗口，这是黑名单分类缺口。
- **建议**：switch 增加 `100 when bytes[1] is >= 64 and <= 127 => false` 与 `198 when bytes[1] is >= 18 and <= 19 => false` 两行；配套 `RemoteHttpUrlValidatorTests` 两段字面 IP 拒绝用例即守卫。**建议验证**：Verified。
- **结案记录（`521f4c1`，接受风险）**：修复轮执行时发现该补救已被 `5a38be9` 刻意否决——fake-ip 模式代理软件把 DNS 应答落在 198.18/15、部分 CDN 边缘节点落在 100.64/10，拦截会回归真实用户的横幅/下载失败，且已有 `ValidateAsync_WhenLiteralAddressIsCarrierGradeNat_ReturnsUri` 钉住放行。诊断成立、补救被否决，按 SEC-004/ARCH-007 先例转书面化接受：switch 显式放行臂 + 让步注释（代价有界：GET-only、80/443、跨主机重定向剥离凭据、响应不回传攻击方），新增 198.18/15 放行守卫测试。勿在未重审该让步前重新收紧。

### AUD-PERF-007 — 校验/diff/卸载阶段逐文件派发 UI 线程进度回调，无下载阶段的累加器等价物【复审新立案；已解决 `608d888`】

- 类别：性能 / UI 线程
- 严重度：Low｜置信度：80｜状态：**resolved**（`608d888`）｜处置：Fix（已执行）
- **证据**：逐文件回调源：`DownloadExecutor.cs:327-329`（每清单文件每次校验轮一次 `progress`，无百分比去重）、`ManifestDiffCalculator.cs:74/:107/:125`（UpdateCheck/RepairCheck 逐文件）、`GameUninstallService.cs:74`（逐文件 `new GameOperationProgress`）。消费侧 `GameOperationsViewModel.ApplyProgress`（`:379-394`）每回调一次 `Dispatcher.UIThread.Post` + 闭包分配，`ApplyProgressCore`（`:396-472`）每次写 ~12 个 observable 属性 + 本地化格式化。下载阶段已有 `DownloadProgressAccumulator(100ms)`（`DownloadExecutor.cs:100-103`）刻意解决同类问题，其余阶段无等价门控。
- **影响**：大清单校验/卸载时 UI 线程收到每文件一次回调，与渲染竞争。定性影响（无测量）；代码库自己在下载路径为此建了累加器，说明该类问题在本仓库是已知痛。
- **建议**：把累加器范式（或按 stage+百分比去重）套用到校验/diff/卸载回调。与 AUD-PERF-001 相互作用（未变更文件跳过重读亦减少本项回调量），但根因不同各自立案。**建议验证**：Strongly Supported。
- **解决记录（`608d888`）**：新增 `PercentProgressGate`（Interlocked 值变化门控：首值 0 必达、重复抑制、阶段折返回退放行、线程安全），应用到校验（`DownloadExecutor`）、stat/修复哈希扫描（`ManifestDiffCalculator`）与卸载（`GameUninstallService`）三处产生侧；显式阶段发射（切换/repair-confirm）有意不过门。守卫：门控四态单测 + 执行器 400 文件同桶去重接线测试。与消费侧 AUD-PERF-006 钳制互补。

### AUD-MAINT-003 — unified.log 文件名硬编码两处，违背「文件名声明于 GamePaths.cs」契约【复审新立案；已解决 `de2a1ee`】

- 类别：可维护性 / 文档-代码契约
- 严重度：Low｜置信度：95（主审计直接核实）｜状态：**resolved**（`de2a1ee`）｜处置：Fix（已执行）
- **证据**：AGENTS.md「Persistence and compatibility contracts」声称文件名声明于 `Constants/GamePaths.cs` 并列举 `unified.log`；实际 `GamePaths.cs:29-33` 只有 manifest/config/settings/download-state/notice 五项。`"unified.log"` 字面量在 `Services/Diagnostics/UnifiedLogger.cs:34` 与 `Services/Diagnostics/LogExportService.cs:172` 各一份，轮转名 `unified_{i:D3}.log` 在 `:177`。同函数 `LogExportService.cs:33-35` 的其余导出项已用 `GamePaths` 常量——字面量是离群点。无契约测试把两处生产字面量拴在一起。
- **影响**：重命名日志文件将静默分叉 logger 与导出器；AGENTS.md 把维护者引向错误的常量归属地。
- **建议**：`GamePaths` 补 `UnifiedLogFileName` 常量并三处引用；AGENTS.md 表述即恢复为真。**建议验证**：Verified。
- **解决记录（`de2a1ee`）**：`GamePaths.UnifiedLogFileName` 收拢三处硬编码（`UnifiedLogger` 构造、`LogExportService` 当前项 + 轮转名派生——`unified_NNN.log` 从常量词干派生，改名自动携带方案）；AGENTS.md 表述恢复为真。`LogExportServiceTests` 的字面量保留为导出线格式线钉。

### AUD-TEST-006 — UiStyleContractTests 令牌纪律扫描遗漏 ResourcePanelOverlay.axaml，守卫白名单在创建该文件的提交中漂移【复审新立案；已解决 `3ea7baa`】

- 类别：测试 / 守卫覆盖
- 严重度：Low｜置信度：93（主审计直接核实）｜状态：**resolved**（`3ea7baa`）｜处置：Add Guard（已执行）
- **证据**：`UiStyleContractTests.cs:21-36` `ViewFiles` 14 项无 `Views/ResourcePanelOverlay.axaml`；令牌纪律用例（原色/`Transparent`/图标尺寸 `Tokens.cs:511-514`、Raw 色值 `:570-578`、排版内联 `:600+`）均消费该列表——最新主叠层不受任何 §2.3 令牌检查。拆分提交 `e56d54b` 在 Motion（`:199-207`）/Dialogs（`:32-38`）/Localization 列表都补了该文件，唯独令牌扫描列表漏了——手工白名单模式已实际漂移一次。当前文件实测无违规（潜在而非现行）。
- **影响**：下一个编辑该文件的人没有绊网；同一漂移可在未来任何新文件上复发。
- **建议**：短平快——把文件补进 `ViewFiles`；更强——加「`Views/*.axaml` 全部在扫描集内」的元契约测试，令白名单漂移不可能。**建议验证**：Verified。
- **解决记录（`3ea7baa`）**：文件补入共享 `ViewFiles`（三扫描全部生效，实测无违规）；新增 `ScanTargets_CoverEveryTopLevelViewFile` 元契约——Views/ 顶层每个 `.axaml` 必须被 `ViewFiles`/`StyleFiles` 声明或显式豁免（`CrashReportWindow` 留名豁免，注释载明其独立 `Crash.*` 令牌族）。复核跟进（同日晚）：原实现曾以匿名 `Append` 放行 `MainWindowDebugOverlay.axaml`（不在扫描集亦未留名豁免，其 ：220 内联 `Padding` 违规因此不可见）——已补入 `ViewFiles`（实测 148 令牌契约全绿），内边距以下沉的 `Border.dialog-card.compact` 样式类收口（视觉不变），元契约自此只剩声明集与留名豁免两条路径。

### AUD-TEST-007 — 主题变体订阅拆卸（b04cb83 当日新增）全仓库无回归守卫【复审新立案；已解决 `b04cb83`】

- 类别：测试 / 回归守卫
- 严重度：Low｜置信度：80｜状态：**resolved**（`b04cb83`）｜处置：Add Guard（已执行）
- **证据**：`SettingsAppearanceViewModel.cs:763-767`（`Dispose` 退订 `ActualThemeVariantChanged`）、`:657-670`（`EnsureThemeSubscription` 含换 Application 先退订分支）——全仓库 grep 无测试引用 `EnsureThemeSubscription`/`themeApplication` 或断言退订；headless 侧仅隐式拆卸（`NeutralStrategyHeadlessTests.cs:99`、`MainWindowHeadlessTests` TestContext）。
- **影响**：提交消息声称「headless 共享 Application 不再跨测试累积订阅」，但删除 `:765` 退订行全套件仍绿——被修复的累积 bug 可静默回归。与已接受的 AUD-ARCH-005（ShellLifecycle Wire/Unwire）无涉：不同类、新代码、无接受裁定。
- **建议**：单测钉住 Dispose 后事件已退订（或 headless 双 VM 实例串联断言不互相触发）。**建议验证**：Verified。
- **解决记录（`b04cb83`）**：新增 `ThemeSubscriptionTeardownHeadlessTests`——哨兵法双相守卫：武装 System 模式处理器后置哨兵色，对照相翻转变体证明哨兵能侦测到在位处理器（方案重刷改写哨兵），`provider.Dispose` 后再翻转哨兵必须存活；退订行被删即测试失败。

### AUD-MAINT-004 — MainWindowDialogsOverlay.axaml 承载点残留化石注释【复审新立案；已解决 `d4ca700`】

- 类别：可维护性 / 卫生
- 严重度：Low｜置信度：90（主审计读文件核实）｜状态：**resolved**（`d4ca700`）｜处置：Fix（已执行）
- **证据**：`Views/MainWindowDialogsOverlay.axaml:13` 注释「Chinese localization settings：资源面板覆盖层（已拆分至 ResourcePanelOverlay.axaml）」——前半句与资源面板无关，`git log -S` 追溯至 `f20de8b` 重组的化石文本。`:13-14` 恰是资源面板（主叠层）嵌于对话框容器、以 FirstChild 位于对话框层之下的承载点。
- **影响**：错误标签遮蔽该位置依赖的微妙层序不变量；调整层序或寻找宿主的维护者会被误导。两行修正。
- **建议**：改写为说明「主叠层 FirstChild 位于对话框层之下」的准确注释。**建议验证**：Verified。
- **解决记录（`d4ca700`）**：注释改写为说明 FirstChild 层序不变量并警示勿在其前插入子项；`UiStyleContractTests` 全绿。

### AUD-CI-002 — 测试隔离用户目录过深，Listen/Raise 派生 Unix 套接字路径超 AF_UNIX 108 字节上限【CI 对账轮新立案；已解决 `08c53f8`】

- 类别：CI / 跨平台（平台假设）
- 严重度：Low｜置信度：90（CI 复现 + 机制读码核实，152ms 快速失败签名与 Raise 6×25ms 重试吻合）｜状态：**resolved**（`08c53f8`）｜处置：Fix（已执行）
- **证据**：AUD-CI-001 守卫的两类 Linux 首跑均红出 `CrossProcessLaunchSignalTests.Raise_WhenFirstInstanceListens_ReturnsOnceAndAutoResets`。`TestUserDataIsolation` 模块初始化器把测试覆盖目录设为 `<tmp>/Cafe.Launcher.Avalonia.Tests/UserData/<Assembly>/<guid32>`（≈104 字符）；Linux 上 `Listen`/`Raise` 以当时的 `LauncherUserDataDirectory.Root`（现已由 `LauncherDataRoot` 取代，见 ADR-025）为套接字目录，派生路径 ≈132 字节 > 107——`UnixDomainSocketEndPoint` 构造抛 `ArgumentException`（`TryBind` 只捕获 `SocketException`/`IOException`，直穿 `EnsureBound` 兜底 catch），绑定永不成功、`unixPending` 为 null，`WaitOne` 立即 false。Windows 不触发（`Listen`/`Raise` 走命名事件分支；`ListenAt` 用例的 `tempDir` 更短）。
- **影响**：CI Linux 通道红；生产影响有界——真实 Linux 数据目录对常见用户名 ≈95 字符可绑定，超长主目录按既有设计降级（Warn 日志，转发不可用）。
- **建议**：压短隔离目录并加「派生套接字路径 ≤107」机械守卫。**建议验证**：Verified。
- **解决记录（`08c53f8`）**：隔离目录压短为 `<tmp>/cl-tests/<guid32>`（≈74 字节），注释载明 108 字节约束；新增 `TestUserDataIsolationTests.IsolatedUserDataDirectory_KeepsDerivedUnixSocketPathUnderKernelLimit` 机械守卫。跟进（`08c53f8`）：守卫初版未门控、在 CI Windows runner 上红出自身（runneradmin 临时目录 41 字符 → 派生 109）——上限只在 Unix 消费域构成约束（Windows 走命名事件分支），改为 `Assert.SkipUnless(非 Windows)` 可见跳过，断言保留在 linux job（push/PR 阻塞 + weekly）上执行；守卫首跑即拦下自己的越界断言，反向验证其敏感性。`linux-unit-tests` 已于 `e49b1d4` 首绿（三例修复全部生效）。

### AUD-CI-003 — ApplyCulture 无效文化名用例选名只覆盖 NLS 失败模式，ICU（Linux）宽容创建不抛异常【CI 对账轮新立案；已解决 `08c53f8`】

- 类别：CI / 跨平台（平台假设）
- 严重度：Low｜置信度：85（CI 断言输出 + 机制读码）｜状态：**resolved**（`08c53f8`）｜处置：Fix（已执行）
- **证据**：`CrashReportBootstrapTests.ApplyCulture_WhenCultureNameIsInvalid_KeepsTheActiveCulture` 期望 `CurrentUICulture` 保持空串、实际变 `xx-INVALID`：Windows NLS 对未知名抛 `CultureNotFoundException`（捕获分支生效、用例绿），Linux ICU 对格式合法但未知的名字以默认数据宽容创建（`GetCultureInfo` 成功 → 文化被应用）。产品行为本身正确——契约是「平台不认识就保持现状」，缺陷在用例选名。
- **建议**：改用格式非法名（双连字符），两个全球化栈都抛异常，真正进入捕获分支。**建议验证**：Verified。
- **解决记录（`08c53f8`）**：改用 `xx--INVALID`，Windows 本地实测通过，注释载明 ICU 宽容性；Linux 侧以 CI 首绿为最终确认。

### AUD-CI-004 — SanitizeFileName 用例硬编码 Windows 非法字符集未门控【CI 对账轮新立案；已解决 `08c53f8`】

- 类别：CI / 跨平台（平台假设）
- 严重度：Low｜置信度：95（CI 断言输出 + 调用点读码）｜状态：**resolved**（`08c53f8`）｜处置：Fix（已执行）
- **证据**：`SanitizeFileName_WhenNameContainsInvalidCharacters_ReplacesThem` 期望 `Blue<>:Archive|?` → `Blue   Archive`（Windows 集），Linux 实际原样返回（`Path.GetInvalidFileNameChars()` 在 Linux 仅含 `/` 与 `\0`）。产品行为正确：`ResolveShortcutFileName` 的两个消费点分别在 Windows（`.lnk`，Shell Link COM）与 Linux（`.desktop`，经 `--launch-game` 走启动器）创建各自平台的工件，字符集随运行平台是设计行为。缺陷在用例未声明平台契约。
- **建议**：拆跨平台契约用例 + Windows 集合精确断言（可见跳过）。**建议验证**：Verified。
- **解决记录（`08c53f8`）**：拆为 `SanitizeFileName_WhenNameContainsPathSeparator_ReplacesThem`（`'/'` 全平台替换并修剪）+ `SanitizeFileName_WhenNameContainsWindowsInvalidCharacters_ReplacesThem`（`Assert.SkipUnless(Windows)`）。

### AUD-SEC-003 — 签名 Authorization 头在每一重定向跳重发（含跨主机）【新立案；更正上轮审计论断】

- 类别：安全 / 网络凭据处理
- 严重度：Low｜置信度：85（本审计主代理亲自读码确认）｜状态：**resolved**（`d7076cb`）｜处置：Fix（已执行）
- **证据**：重定向循环逐跳 `createRequest(currentUri)`（`Services/RemoteHttpRequestService.cs:34`）；`BuildRequest` 每跳调用 `policy.ConfigureRequest?.Invoke(request)`（`Services/RemoteHttpTransport.cs:285-294`）；`Services/LauncherApiClient.cs:198-200` 据此每跳重附 `Authorization`（`TryAddWithoutValidation`）。签名与请求路径无关（`Services/Auth/AuthorizationHeaderFactory.cs:44`，`data` 为空串），被截获即可对任意端点重放至服务器容忍的时限。无测试断言跨跳的头行为。
- **影响**：.NET 内建 `HttpClient` 会在跨主机重定向时剥离 `Authorization`，手写循环丢掉了这层保护。影响有界：仅 API 主机可选择重定向目标，而其被攻陷本可直接取得该头——属加固而非独立利用链。
- **建议**：在 `RemoteHttpRequestService.SendAsync` 中当下一跳 `Uri.Host` 与初始主机不同时剥离 `Authorization`（规则收进单一发送例程，优于让 `ConfigureRequest` 感知跳数）。
- **建议验证**：Verified（机制读码确认；修复为机械改动）。
- **建议守卫**：单测钉住「跨主机重定向后请求不携带 Authorization」。

### AUD-SEC-004 — 手写代理对注册表配置的代理发送当前用户默认凭据【新立案；07c4c8d 引入】

- 类别：安全 / 网络
- 严重度：Low｜置信度：80｜状态：**accepted-risk**（`043279e`，让步已书面化）｜处置：Accept Risk
- **证据**：`Services/ProxySettingsService.cs` `BuildConfiguredProxy` 内 `UseDefaultCredentials = true` `new WebProxy(settings.ProxyUrl) { UseDefaultCredentials = true, … }`；`ProxyUrl` 原样取自 `HKCU\...\Internet Settings\ProxyServer`（`WindowsRegistrySystemProxySettingsProvider.cs:25-56`）。提交消息记录意图：镜像 WinINet 的静默 407 应答。
- **影响**：同用户进程可写 `ProxyServer` 指向攻击者收集 NTLM/Kerberos 应答。误报排查：与 `WebRequest.GetSystemWebProxy()` 行为一致，且注册表值本就在同用户写入能力内（届时攻击者已控制用户会话）——真实风险低。
- **建议**：保持行为；在既有注释旁写明「同用户信任边界让步」；仅 System 模式启用默认凭据已是现状，维持。
- **建议验证**：Verified（读码确认；无凭据行为测试，现有 ProxySettingsServiceTests 断言的是 PAC/凭据属性存在性）。

### AUD-SEC-005 — 发布产物无签名、SHA256SUMS 自行发布于同一 Release【新立案】

- 类别：安全 / 供应链
- 严重度：Low｜置信度：85（事实）；可利用性评估 60｜状态：**resolved**（`db941bf`）｜处置：Add Guard（已执行）
- **证据**：`release.yml:358-372` 生成六产物 `SHA256SUMS` 并发布到同一 Release（:374-409）；全仓库无 Authenticode、无 `actions/attest-build-provenance`、无 macOS codesign/notarization（`installer/macos/Info.plist` 无 `com.apple.security.*`，`Build-Distribution.ps1` 无签名步骤）。
- **影响**：终端用户真实性完全依赖 github.com TLS + Release 组织账号控制——社区项目可辩护的模型（且构建输入侧已有 attestation、SHA 钉住、NuGetAudit）；缺口仅在 Release 账号被攻陷时用户无从独立验证。
- **建议**：为发布产物追加 `actions/attest-build-provenance`（GitHub 托管、无需证书）；文档写明 `SHA256SUMS` 仅完整性非来源。证书签名待项目获得证书再议。
- **建议验证**：Strongly Supported（attest action 为标准 GitHub 功能，适配现有双仓库发布流）。

### AUD-PERF-001 — 更新路径仍对全部未变更已安装文件做全文件 CRC64 重读【部分解决，Medium→Low 降档】

- 类别：性能 / 下载完整性
- 严重度：Low（降档理由：串行 foreach 已消除，并行化 ≤8 + 跨轮摊销落地；残留为文档化的自愈设计）｜置信度：95｜状态：open（部分解决）｜处置：Architecture Decision
- **已解决部分（`67229b5`）**：校验改为有界并行 `Math.Clamp(Environment.ProcessorCount, 1, 8)`（`Features/GameOperations/DownloadExecutor.cs:28`，`SemaphoreSlim` :284）；结果按清单索引收集（`failedFlags`，WhenAll 后读取 :330-339）、计数 `Interlocked`；`Crc64Service` 线程安全（静态只读表 + 池化缓冲）；失败语义不变（不匹配即删 :305-315、按清单序重试 `DownloadSession.cs:489-494`）；下载期 `verifiedHashes` 跨重试轮累积（`DownloadSession.cs:413-435`）；修复通道见证跳过（`PlannedFileHash`）正常；建议的 Verbose 跳过计数日志已加（:341-344）。`DownloadExecutorTests` 9 用例经并行路径钉住行为。
- **残留**：更新计划的 `plannedHashes` 刻意为空（`DownloadPlan.cs:21-29`），未变更文件仍各全读一遍——这是代码注释明示的唯一内容损坏自愈通道（`DownloadExecutor.cs:260-264`，启动校验只比 size/existence）。小更新 + 大安装场景仍是一次全树读（现最多 8 路并行）。
- **建议**：不弱化自愈语义。若再优化，复用修复通道见证机制须以「见证仍匹配才信任跳过」为前提，并先基准实测。**建议验证**：Strongly Supported。
- **既有守卫**：Verbose 跳过计数日志（后续审计可直接量化摊销效果）。

### AUD-PERF-005 — revert `c826f8a` 恢复壁纸构造期同步解码：首帧前 UI 线程全分辨率解码 + 首次刷新二次冗余解码【新立案；文档半项已交付】

- 类别：性能 / 启动与首帧
- 严重度：Low｜置信度：80｜状态：open（文档半项已交付 `700e674`）｜处置：~~Document（必须）~~ 已完成 + Investigate（二次解码）
- **证据**：`ViewModels/BackgroundViewModel.cs:122` 构造函数内 `backgroundImageSource = bundledImageLoader()` 同步解码 `Assets/launcher-background.png`（2560×1388，提交消息载明），经 `App.axaml.cs:61` DI 解析链在 `MainWindow` 显示前于 UI 线程执行。`AGENTS.md:75` 原承诺「no blocking work before the first frame」——revert 刻意违反该承诺（提交消息记录产品理由：无它则窗口先开在主题底色上，golden 基线与 UX 回退）。第二处：首次刷新时跳过卫（:153-160）因 `lastBackgroundSourceKey`/`lastDecodeTarget` 未置而不能命中，:232 于线程池再次全量解码同一内置图（loader 不接收目标尺寸，第二次解码产图同尺寸）。
- **已交付（文档半项）**：AGENTS.md:75 改写为「重活不占首帧路径 + 内置壁纸构造期同步解码是唯一刻意例外（使首帧显示壁纸而非主题底色），勿擅自改回异步」；CONTEXT.md 复核无同类声明，无需改动。
- **影响（残留）**：未量化：首帧关键路径一次全 PNG 解码（已文档化为刻意）+ 启动后短时间内一次冗余解码（线程池）。
- **建议（剩余）**：独立评估构造位图复用或跳过态种子化以消除二次解码——需先确认首次刷新的解码目标可合法匹配跳过卫；与同步/异步之争互不绑定，勿在无产品确认下回退 revert。
- **建议验证**：Verified（代码路径读码确认；解码成本未测量）。

### AUD-PERF-006 — 并行安装校验的进度回调可瞬时回退【新立案；67229b5 引入】

- 类别：性能 / UI 正确性
- 严重度：Low｜置信度：85（竞态从代码确定；用户感知未测）｜状态：**resolved**（`608d888`）｜处置：Fix（已执行）
- **证据**：`Features/GameOperations/DownloadExecutor.cs:327` `progress((int)Math.Round(Interlocked.Increment(ref completedCount) * 100d / manifestFiles.Count));` 在线程池并发执行——递增与回调非原子：线程 A 递增至 5 后被抢占，可在 B 回调 6 之后回调 5。消费侧 `Dispatcher.UIThread.Post` 线程安全但不保序。
- **影响**：FileCheck 百分比可瞬时回退；阶段边界进度（`DownloadSession.cs:457-460`）保证终值正确，无卡死。属外观瑕疵。
- **建议**：`ApplyProgressCore` 钳制单调，或在单次 `Interlocked` 操作内取值并回调。**建议验证**：Verified（单行修复，无需测量）。

### AUD-CI-001 — 非 Windows 测试不随变更执行【部分解决，Medium→Low 降档】

- 类别：CI / 跨平台行为
- 严重度：Low｜置信度：95｜状态：**resolved**（`08c53f8`）｜处置：Add Guard（已执行）
- **已解决部分（`08c53f8`）**：新增 `.github/workflows/linux-tests.yml`（ubuntu-24.04，`workflow_dispatch` + 每周一 cron）：单元套件获得真实非 Windows 执行点，平台自适应测试（如 `GamePathValidatorTests.cs:161`）不再无处执行；权限最小（`contents: read`）、动作 SHA 钉住、无新信任面（本审计安全通道专项评估）。locked 还原跨平台成立的推理已写入工作流注释。
- **残留**：触发仅 dispatch + weekly（`linux-tests.yml:7-10`，注释自述「不阻塞 PR」）——平台分支回归不能阻塞引入它的变更，最坏晚一周才暴露且不阻塞合并；Headless/golden 按设计保持 Windows-only（合理）。该 job 是否曾绿跑，本审计无法验证（只读评审，无 GitHub Actions 运行历史）。另按 PROJECT_CONVENTIONS §9，`main` 规则集无 required_status_checks，Windows job 亦只是约定门。
- **建议**：把现有 job 体（无 RID 还原、无渲染依赖）复用为 `build.yml` 中 push/PR 路径的 ubuntu 单元测试 step；可选为规则集加 required status checks。**建议验证**：Verified（job 体已存在，纯编排改动）。
- **建议已落地（2026-09-15 CI 对账复核）**：`build.yml:96-130` 现有 `linux-unit-tests` 作业，由 `push` 与 `pull_request` 触发（`build.yml:3-6`），注释即引 AUD-CI-001 说明「平台分支必须在随变更执行的 CI 上运行」。原「触发仅 dispatch + weekly」的残留自此消除，本项结案为 resolved。**但新作业自落地起一直红**——保护并未真的生效，转为 AUD-CI-005。

### AUD-CI-005 — `linux-unit-tests` 作业在 main 上连续 6 次红、且非 required：平台假设回归既没被门拦住，也没人看见【CI 对账复核新立案；同日修复，作业首绿】

- 类别：CI / 平台假设
- 严重度：Low｜置信度：95（GitHub Actions 运行历史直接核实，失败清单与错误文本逐条读取）｜状态：**resolved**（`08c53f8`；测试侧四条已修、作业首绿。规则集侧「升为 required check」仍为残留）｜处置：Fix（已执行）
- **证据**：`build.yml` 的 `linux-unit-tests` 作业对 `6408c58` / `098166a` / `a243820` / `2d169bf` / `e49b1d4` 五次 push 全部 failure（`gh run list`）；本次推送的 `6408c58`＋`5dd6938` 同样 failure（run 34961576610：`build` 绿、`linux-unit-tests` 红，单元 1801 通过 / 4 失败 / 16 可见跳过）。四条失败与本窗口代码无关，分两类：
  - **两例测试依赖 Windows 的精确异常类型**（`ShellLifecycleTests.cs:190-207`、`:142-158`）：`CreateBlockedSettingsPath()` 用同名**文件**占位使目录创建失败，Windows 上抛的正是 `IOException`，Linux 上抛的是子类 `DirectoryNotFoundException`——`Assert.ThrowsAsync<IOException>` 要求精确类型、`Assert.Contains("IOException", toast.Message)` 断言的是类型名，于是同一场景在两平台结论相反。
  - **两例依赖 Windows 文件共享语义**（`DirectoryTreeDeleterTests.Delete_WhenAnEntryCannotBeDeleted_...`、`GameUninstallServiceTests.UninstallAsync_WhenThoroughCleanupCannotRemoveSomething_...`）：用例靠 `FileShare.Read/None` 打开句柄来制造「删不掉的条目」，而 POSIX 允许 unlink 已打开的文件——Linux 上删除照常成功，残留清单为空，断言「按路径回报」自然失败。前者带来的 ADR-030 语义未被跨平台钉住。
- **影响**：`main` 上的平台假设回归既不能阻塞合并（PROJECT_CONVENTIONS §9：规则集无 required status checks），又在事实上无人查看（连续 5 次红未被任何一轮复核发现——本报告上一轮的「CI 对账」只验证了另一份 `linux-tests.yml` 的首绿）。产品以实验性形态分发 Linux/macOS 包，真正的平台回归会与这些噪声混在一起。四例本身都是测试缺陷（三个平台上的产品行为未见异常：`DirectoryTreeDeleter` 删掉了它该删的、设置保存失败也确实被报出）。
- **建议**：(a) `ShellLifecycleTests` 两例改为接受派生类型（`Assert.ThrowsAnyAsync<IOException>`；toast 断言改为不绑类型名的可辨识内容，例如被挡路径的父目录名）；(b) `DirectoryTreeDeleterTests` 那条按仓库既有先例加 `Assert.SkipUnless(OperatingSystem.IsWindows(), …)`（同一文件族里的 `UninstallAsync_WhenManifestFileIsLocked_...` 已经这么做），或改用跨平台的阻塞手段（父目录只读在 POSIX 上能挡住 unlink，Windows 上不能，故这条仍以门控为宜）；`GameUninstallServiceTests` 那条同理。(c) 可选：把 `linux-unit-tests` 加进 required status checks，让这类回归真的挡住合并。**建议验证**：Verified（失败清单、错误文本、平台语义逐条核实）。
- **注意**：修的是测试而非产品——不要为了让用例在 Linux 上过而弱化 ADR-030 的残留回报语义，也不要改动 `DirectoryTreeDeleter` 的删除行为。
- **解决**（`08c53f8`，2026-09-15 CI 对账轮）：(a) `ShellLifecycleTests` 两例的 `Assert.ThrowsAsync<IOException>` 改为 `ThrowsAnyAsync`（接受子类），toast 断言改为接受 IO 家族的任一类型名（新增 `MentionsIoFailure` 辅助），并在 `CreateBlockedSettingsPath` 的文档注释里写明「抛出的精确类型随平台而变，不要绑死」；(b) 两条删除残留用例按同族先例加 `Assert.SkipUnless(OperatingSystem.IsWindows(), …)`，注释写明为何不能用「跨平台阻塞手段」代替（父目录只读在 POSIX 能挡 unlink、在 Windows 不能）。**未改**其他「锁住以制造失败」的用例（`ResourcePanelServiceTests`、`LogExportServiceTests`、`LocalInstallationStateStoreTests`、`SetupWizardViewModelTests`）：.NET 在 Unix 上用 flock 模拟文件共享，那些断言在两平台都成立（CI 从未红过），改动属无据重构。
- **验证**：推送 `08c53f8` 后 run **34967403211** 两个作业全绿——`linux-unit-tests` **1806 通过 / 0 失败 / 18 可见跳过（总 1824）**（16 条既有 + 新增 2 条门控，与设计一致），`build` 侧单元 1823 / 跳过 1、Headless 184、覆盖率棘轮均绿。这是该作业自落地以来的首次绿跑。
- **残留**：作业虽绿仍非 required check（PROJECT_CONVENTIONS §9：`main` 规则集无 required_status_checks）——平台回归依旧不会阻塞合并，只是现在能被看见。要真正挡住需要仓库规则集侧的改动（只有仓库管理员能做）。
- **后续（2026-09-15 深夜）**：该作业在结案后再度转红——这次是**生产侧**平台假设而非测试侧，见 AUD-CI-006；本行的状态与验证记录保持当日事实，不做追改。

### AUD-CI-006 — `GameProcessNames.WithoutExtension` 用 `Path.GetFileName` 切路径：Unix 上 `\` 不是分隔符，配置里 Windows 形状的启动参数整条被当成文件名，游戏可执行文件静默移出家族【CI 复查新立案；同日修复】

- 类别：CI / 平台假设
- 严重度：Low｜置信度：95（CI 失败日志、源码与两平台语义逐条核实；修复后作业实测转绿）｜状态：**resolved**（`8461044`）｜处置：Fix（已执行）
- **证据**：AUD-CI-005 于当日以「`linux-unit-tests` 首次绿」结案（run 34967403211）；此后 `6db3cdd`（21:35，随 `6408c58` 折叠）新增 `GameProcessNamesTests.FromLaunchConfiguration_NormalizesQuotedAndPaddedParameters`，作业随即对后续 push 重新变红（改写历史期间的数跑均为 `build` 绿 / linux 红，run 34993079616 之前未再绿）。失败文本单一且稳定：`Failed Cafe.Launcher.Avalonia.Tests.GameProcessNamesTests.FromLaunchConfiguration_NormalizesQuotedAndPaddedParameters` → `Assert.Equal() Failure: Collections differ at index 1`（单元 1803 通过 / 1 失败 / 18 可见跳过）。根因读码确认：`GameProcessNames.WithoutExtension` 用 `Path.GetFileName(normalized)` 取文件名，而 Unix 上 `\` 不是路径分隔符——`"C:\dir\BlueArchive.exe"` 整条被当成文件名（得到 `C:\dir\BlueArchive`），不等于 `BlueArchive`。注意判后缀那一步（`LooksLikeExecutable`，纯 `EndsWith(".exe")`）仍然通过：参数没有被丢弃，而是提取出了错误的名字，这正是索引 1 处集合不同的来源。与 AUD-CI-002..005 不同，这条是**生产侧**而非测试侧的平台假设，Windows 的 `Path` 语义恰好与配置形状一致，因此本机与 `build` 作业上结构性看不见——`linux-unit-tests` 作业的守卫本职在此首次抓到一条产品缺陷。
- **影响（按平台分档）**：**Windows**——无影响（`Path` 语义与配置形状一致）。**Linux / macOS**——已知名集合静默丢掉 `params` 声明的那个可执行文件（`BlueArchive`，实机上正是游戏本体进程，见 ADR-032 的实机记录），只剩宿主名与其 `_` 家族变体，判据少一半；与既有的 Unix 限制叠加（`comm` 15 字符截断使长宿主名对不上，已单独书面化）后，Unix 侧闸门被双重削弱。闸门守的是「替换安装目录里的文件」这类操作，判据缺失会让运行中的游戏被静默换掉正在读的文件（与 AUD-ARCH-009 同一影响类）。macOS 不启动游戏，无运行中进程可漏；实际暴露面是 Linux/Proton 构建（实验性）。`GameProcessNames` 的另外两个出口（`BelongsToFamily` 的家族判定、`DescribeForDisplay` 的报法）口径正确，未受影响。
- **建议**：(a) 路径切分与宿主平台解耦——`\` 与 `/` 一起认；(b) 补一条平台无关性用例，钉住同一批输入在两平台结论相同；(c) 不要把用例按平台跳过——被跳过的正是产品缺陷本身（AUD-CI-002..005 是测试缺陷，这条不是）。
- **解决**（`8461044`，2026-09-15 深夜 CI 复查）：新增私有 `FileNameOf` 与 `PathSeparators`，`WithoutExtension` 改为按最后一个分隔符（`\` 或 `/`）取文件名，不再调用 `Path.GetFileName`；`System.IO` using 随之移除，`GameProcessNames` 不再触碰任何宿主平台 API。未改判据口径：归一（`Unquoted`）、家族判定（`BelongsToFamily`）与报法（`DescribeForDisplay`）保持原样，只把「取文件名」这一步与宿主解耦。`WithoutExtension` 的 remarks 写明为何不能按宿主约定切路径（配置里的 `params` 在任何平台上都是 Windows 形状，Linux 与 macOS 下游戏跑在兼容层里也一样）。
- **验证**：新增回归用例 `WithoutExtension_SplitsPath_IndependentlyOfHostPlatform`（`\`、`/` 与无分隔符三个输入，两平台必须给出同一个答案），Windows 侧本机单元 **1821 通过 / 0 失败 / 2 可见跳过（总 1823）**；推送 `8461044` 后 run **34993079616 两作业全绿**（`linux-unit-tests` 1m31s、`build` 6m15s）——该作业自 CI-005 结案后首次重新转绿。Linux 侧无法在本机复现（无 WSL 发行版与容器），以该作业为最终验证。
- **残留**：作业仍非 required check（同 AUD-CI-005 残留）；Unix 上闸门的 `comm` 15 字符截断属另一条已书面化的结构性限制，不在本条范围内。

### AUD-TEST-008 — 并行校验／下载路径上测试把进度回调收进未加锁的 `List` 或做非原子自增（两次 CI 偶发红）【CI 复查新立案；同日解决】

- 类别：测试 / 确定性
- 严重度：Low｜置信度：95（两次 CI 失败日志 + 源码读码 + 两平台语义核实；本机无法复现，见证据）｜状态：**resolved**（`0060855`）｜处置：Fix（已执行）
- **证据**：两次红都落在**与改动无关**的提交上：①run 34994104568（提交 `857900f`，仅文档改动）的 `build` 作业红在 `DownloadExecutorTests.InstallDownloadedFilesAsync_WhenManyFilesSharePercentBuckets_DeduplicatesProgress`——`Assert.Equal(0, delivered[0])` 实到 1；②run 34995139532（提交 `e83334b`）的 `linux-unit-tests` 作业红在 `DownloadExecutorTests.InstallDownloadedFilesAsync_WhenManyFilesVerifyInParallel_ReassemblesFailuresInManifestOrder`——`Assert.Equal(fileCount, progressCount)` 实到 11。根因两处都在用例：回调由并行 worker 抵达（校验阶段 ≤8、下载阶段 ≤10 个并发传输），而用例把回调收进未加锁的 `List<int>`／`List<GameOperationProgress>`（`List<T>.Add` 并发下会丢条目或写重复项），或用 `_ => progressCount++` 非原子自增（丢更新）；`delivered[0] == 0` 另外断言了单调门控并不承诺的到达顺序——completed 1、2 都算 0 桶、3 就算 1，落后于更高桶的 0 会被单调判据压掉（`PercentProgressGateTests` 里 `Assert.Equal(0, ordered[0])` 是同一处过度指定）。
- **影响**：噪声而非产品缺陷——两条用例钉的语义（百分比去重、失败按清单顺序重组）在产品侧都成立，三个平台的产品行为未见异常；但偶发红会与真回归混在一起，且 `linux-unit-tests` 仍非 required check，红只表现为「没人看」。本机复现不出（同一用例连跑 12 次全绿；CI 上 worker 数多于核数，`Math.Round` 与 `semaphore.Release` 之间被抢占的窗口更大），所以这类问题只能靠 CI 的多次运行暴露——这正是把它记进台账的理由。
- **建议**：(a) 测试侧收集一律走线程安全收集器（新增共享替身 `CallbackRecorder<T>`：加锁收集、读走快照）；(b) 断言只取与到达顺序无关的不变量，顺序相关的期望要么改断言、要么由确定性用例在门控层钉住；(c) 不要把断言放宽成「什么都能过」——12 文件用例的投递次数恰好等于文件数（`round(k*100/12)` 两两不同），这条仍是门控语义的正向证据。
- **解决**（`e83334b` + `0060855`）：`e83334b` 修 400 文件用例（收集加锁、断言改为每桶至多一次／值域 0..100／100 必达）并改掉 `PercentProgressGateTests` 的同一处过度指定、补 `ShouldDeliverMonotonic_WhenTheZeroBucketLagsBehind_SuppressesIt`；`0060855` 新增 `tests/TestDoubles/CallbackRecorder.cs`（需在两个测试项目的 csproj 显式 `Compile Include`——`tests/TestDoubles` 不随 SDK 通配）并把校验与下载两阶段的收集一次换完（`DownloadExecutorTests` 三处、`GameDownloadServiceTests` 十处，共 13 处）；`PercentProgressGate` 的类注释更正（原文称新门控总是投递 0，对并行路径不成立），`ShouldDeliverMonotonic` 的 remarks 补记那个 102/101 另有收集侧竞态一份。**产品口径未改**：单调门控是刻意的（落后回调回跳是真实的），阶段开头的 0 由阶段切换的显式投递负责，用例里已写明这一点。
- **验证**：本机单元 **1822 通过 / 0 失败 / 2 可见跳过（总 1824）**、Headless **184 通过 / 0 失败**；`Build` 作业在发布提交 `e1045b6` 上 **success**（两套件 + 覆盖率棘轮），`linux-unit-tests` 在 `0060855` 上绿。
- **残留**：未动的同类收集——`GameDownloadServiceTests` 的 `runningStates`（`IsRunningChanged` 每次操作只触发一次，不与并行 worker 并发）与其余单点触发的事件处理器收集。这类竞态本机复现不出，若 CI 再现，优先检查是否又有新站点绕开了 `CallbackRecorder`。

### AUD-TEST-009 — 测试设施三处各自重写：临时目录、异步等待、主窗口装配（含两个日志句柄在装配返回时已被释放、无头上下文共用程序集级数据根）【2026-09-16 新立案；同日解决】

- 类别：测试 / 维护成本·隔离性·反馈速度
- 置信度：高（全树统计 + 改造前后同配置 TRX 逐条对照）
- **证据**：
  - 64 个测试源文件、115 处 `Path.GetTempPath()` 各自拼唯一目录；清理按类重写（5 处手写 3–5 轮退避），失败有的抛 `IOException`、有的空 `catch` 吞掉。
  - 异步等待三套并行实现：`HeadlessTestHost.WaitUntilAsync`（`DateTime.UtcNow` 墙钟）、`ToastHostViewModelTests`/`ResourcePanelViewModelTests`/`RemoteContentViewModelTests`/`BackgroundViewModelHeadlessTests` 各持一份私有轮询。墙钟计时在宿主时钟跳变时会提前超时。
  - `MainWindowViewModelTests.CreateViewModelAsync` 以局部 `using var settingsLogger/testLogger` 持有两个 `UnifiedLogger`，方法返回即释放；而 `SettingsViewModel` 与 `LogViewerDialogViewModel`（连同它构造的 `LogExportService`）在用例整个活期内继续持有并写入——写入静默落进已释放的管道，且没有任何断言能看见。
  - `HeadlessTestHost.CreateServiceProvider` 只换日志目录，其余持久化仍走程序集级进程根（`TestUserDataIsolation` 的共享目录）：同程序集的两个上下文互相看得见对方的 `settings.json`、下载检查点、公告状态与崩溃快照。
  - 既有 TRX 中 `ResourcePanelApplySettings_UsesCafeSourceAndSystemProxyWhenOpeningPanel` 单条 **10.26s**：监听器接受连接后立刻断开，用例再等真实传输的 3 次尝试退避（两条请求，800ms+1600ms 各一轮）走完。
- **影响**：维护成本（改一处惯例要动 60+ 文件）、隔离性（跨用例状态串味 ＋ 句柄提前释放这类不会被断言发现的缺陷）、反馈速度（单条 10s 的用例占单元套件测试用时约三分之一）。
- **处置（Fix，已执行）**：
  - `tests/Support/TestDirectory.cs` — 短路径独立目录 ＋ 派生 `LauncherDataRoot` ＋ 释放即删除（5 轮递增退避，约 1s）；删除失败默认抛 `IOException`（普通测试可见），`TestDirectoryCleanup.BestEffort` 留下目录并写诊断（无头拆卸）。隐式转换为自身路径，使既有「把临时目录当路径用」的调用点不必逐个改写。
  - `tests/Support/TestRepository.cs` — 仓库/应用目录与 `.resx` 解析结果的唯一缓存点；`InitializeLocalizationResources()` 每次调用都重装（避免先装自定义资源的用例污染其后用例）；`TestLocalizationHelper` 退化为转发面。
  - `tests/Support/TestWait.cs` — 唯一的有截止时间轮询（`Stopwatch` 单调计时、取消、可注入推进动作、带上下文的 `TimeoutException`）；无头侧 `HeadlessTestHost.WaitUntilAsync` 在同一实现上补「先泵一次 UI 线程」与 UI 调度推进。
  - `MainWindowTestContext` — 主窗口对象图装配集中于此并书面化所有权（只释放自建对象；日志器活到用例结束且在 ViewModel 之后释放）；`MainWindowViewModelTests` 退化为创建 ＋ 登记的薄包装。外部夹具（工厂、图片缓存）仍由用例创建与释放。
  - `ServiceConfiguration.AddLauncherServices(launcherDataRoot:)` — 显式数据根一次性覆盖登记项与闭包捕获的构造参数（日志、崩溃快照、设置、下载检查点、公告状态）；缺省仍按进程解析，生产行为不变。
  - 无头：`HeadlessTestHost.CreateServiceProvider(TestDirectory)` 每上下文一个数据根；三个自建 provider 的用例（`NeutralStrategyHeadlessTests`、`ThemeSubscriptionTeardownHeadlessTests`、`SavedSettingsWriterThreadingTests`）同步。
  - 资源面板代理用例拆分：呈现层一条（桩传输，断言以系统代理打开面板不需二次确认、各端点各请求一次）＋ 传输层一条（回环明文代理应答绝对形式请求并读回 JSON，`WaitAsync` 整体超时）。
  - `test.ps1` 增 `-Suite All|Unit|Headless` 与 `-Filter`；`-UpdateGolden` 保持原行为并拒绝与二者混用。
- **实测（同配置 Debug，改造前 → 改造后）**：单元 1831 → 1853 条（新增 22 条守卫），测试用时 31.8s → 23.9s，最慢用例 10.26s → 2.07s（既有用例 `DownloadAsync_WhenBodyStallsAfterHeaders_…`，非本轮引入）；无头 185 → 185 条，56.4s → 57.1s（同量级）；两套件均 0 失败，可见跳过 2 条不变（平台门控），golden 基线未更新且 12 项全绿；受影响异步用例（单元 19 条 ×10、无头 13 条 ×10）全部稳定通过。
- **后续性能机会（本轮明确不做）**：两套件仍是程序集级串行（依赖静态状态，拆分是独立议题）；Windows CI 仍普通测试与覆盖率双跑；`-Filter` 不进 CI。新增的两条清理失败守卫各约 1.5s（有意走完有界退避），若后续要给单元套件再挤时间，先看这两条与串行策略。

### AUD-ARCH-007 — 代理指纹变化可在下载批次进行中 Dispose 其底层 handler【新立案】

- 类别：架构 / 生命周期所有权
- 严重度：Low｜置信度：70（机制结构性证实；未复现运行时行为）｜状态：**accepted-risk**（`043279e`，权衡已书面化）｜处置：Accept Risk
- **证据**：`Services/ProxySettingsService.cs:100-103` 指纹变化即 `stale.Handler.Dispose()`（下次同模式租约创建时触发）；租约以 `disposeHandler: false` 包裹 handler（`Services/HttpClientFactory.cs:92`），仅释放自己的 HttpClient；下载批单租约全程持有（`Services/DownloadTransport.cs:36-72`，租约 10 分钟 `GameDownloadService.cs:39`）。触发链：系统代理/VPN/PAC 变更 → 任一后续远程调用（更新检查、资源面板、横幅）建租约 → 处置进行中批次下的 handler。:87-89 注释只覆盖创建竞态，未覆盖活租约处置。
- **影响**：批次传输快速失败进入验证重试轮，下轮新租约自愈——代价一轮下载而非永久故障、无完整性风险（.tmp + CRC64）。性能通道独立识别同一机制，交叉证实。
- **建议**：先实验确认 disposed `SocketsHttpHandler` 对在途请求的确切语义；若有害，为缓存 handler 加租约引用计数或延迟至租约释放再处置；若可接受，书面记录「下载中途代理变更代价一轮失败重试」。**建议验证**：Needs External Verification。
- **注意**：修复不得改变 `ProxySettingsService.cs:23-38` 文档化的指纹替换不变量。

### AUD-ARCH-008 — 卸载的「游戏在跑」闸门只在预检跑一次，用户确认前的窗口里游戏被外部起来也照删【功能轮新立案；同日按建议 (a) 解决】

- 类别：架构 / 执行边界一致性
- 严重度：Low｜置信度：85（两条路径逐行读码确认；未做运行时复现——需「对话框开着时从外部启动游戏」的时序）｜状态：**resolved**（`6408c58`）｜处置：Fix（已执行，(a) 分支）
- **证据**：闸门只存在于预检（`GameUninstallService.cs:374-388`）；`UninstallAsync` 自身只复查策略（:79-83，`GameOperationPolicy` 判的是安装生命周期状态，与进程无关），随后直接删清单文件并进入彻底清除（:118-169）；journey（`GameOperationJourney.cs:321-330`）与 VM（`GameOperationsViewModel.cs:423-435`）同样只复查策略，都不再问进程。确认框从 `Show` 到用户点击之间可以一直开着（尺寸统计先弹框后回填，实测 37k 文件/24 GB 的树要 3.4 秒；用户也可以放着不管），期间从桌面快捷方式或 Steam 等外部路径启动游戏不触碰任何闸门——而 ADR-032 的原话是「只在整族退出后放行」。
- **影响**：删除落在正在运行的安装上——能删的会被删掉（`BlueArchive_Data` 下正在被游戏读取的文件），删不掉的按 ADR-030 如实回报为残留。危害被 ADR-030 的残留上报兜住（不静默、不误报成功），但仍可能出现「游戏当场失败、需要修复」这类用户可见损害。与路径守卫的处理不对称：同一提交为路径守卫加了执行边界复查（预检 `:97-115` 跑一次，删除时 `:271-289` 经 `DirectoryTreeDeleter.Delete` → `EnsureDeletable` 再跑一次）。
- **解决**（`6408c58`，2026-09-15 功能轮跟进）：`UninstallAsync` 在读到 `localGame` 之后、彻底清除守卫与文件循环之前调用新的私有 `FindRunningGameFailureAsync`（`GameUninstallService.cs:408`），命中即返回既有的 `GameIsRunning` 失败、不做任何删除；`ValidateAsync` 的同一段判据与文案也改为调用它，两道闸门自此共用一处实现，消息不会分叉（`GameProcessNames` 名字集合 + `RunningProcessSeparator` 拼接 + `.exe` 补回 + `GameOperationErrorCode.GameRunning`）。失败经 `ConfirmUninstallAsync` 的 `ShowOperationResult` 落地，合 ADR-027 的「确认后拒绝必须可见」。守卫：`GameUninstallServiceTests.UninstallAsync_WhenTheGameStartedAfterThePrecheck_RefusesAndDeletesNothing`（探测分两相——预检时没在跑、删除时在跑；断言失败码、点名 `BlueArchive.exe`、且清单文件/安装状态/目录/快捷方式全未被动过），**变异验证**：把复查的判据换成空输入后该用例与 `..._WhenOnlyTheAntiCheatHostIsStillRunning_RefusesAndNamesIt` 同时变红，还原后转绿（**2026-09-15 复核轮更正**：后者的变红只证明闸门被调用过，不证明判据来自配置——它当时的替身忽略入参；该用例已按实测启动配置重写，注入 `FromLaunchConfiguration(name, null)` 即红）。文档：ADR-032 第 7 条决策 + 守卫条目、AGENTS.md 游戏操作段。
- **被否的替代方案**：(b) 判定「用户在确认框上点头就是对当时状态的授权」并写进 ADR-032 已知限制——取 (a) 是因为成本几行 + 一例用例，且与路径守卫的执行边界复查对称；UI 禁用态/进程轮询仍被 ADR-032 否决，未采用。

### AUD-ARCH-009 — 下载/安装/修复的「游戏在跑」闸门：全新安装时判据为空而放行、且只在计划阶段查一次【功能轮跟进新立案；同日解决】

- 类别：架构 / 执行边界一致性
- 严重度：Low｜置信度：90（三处逐行读码确认；「全新安装时判据为空」由代码结构直接证实，跨平台后果的那一档基于 POSIX 语义推理）｜状态：**resolved**（`6408c58`）｜处置：Fix（已执行，三条建议全部落地）
- **证据（三处缺口）**：①**判据的取法要求本地配置存在**——`DownloadSession.PrepareDownloadPlanAsync` 的闸门判据取自 `localGame.GameConfig?.Name`，全新安装时该文件还不存在，于是名字集合为空、闸门直接放行，游戏在跑也照样开始安装；②**只在计划阶段查一次**——下载/修复可能持续数分钟，期间从桌面快捷方式或 Steam 把游戏起来不会再被发现，而真正动安装目录的是后面的 `InstallDownloadedFilesAsync`（`:359-370` 的 `DeleteExistingFile` + `File.Move`）；③**报法不统一**——卸载那条点名实际在跑的进程，下载那条只有笼统一句，与 ADR-032 决策 5「报出的名字就是用户看到的」不一致。
- **影响（按平台分档）**：运行中操作 + Windows → 覆盖被占用的文件抛 IO，操作失败并如实报错、`.tmp` 留在盘上可重试，不会静默坏掉；运行中操作 + Linux（POSIX 允许删除/改名已打开的文件，见 AUD-CI-005 同一语义）或全新安装时游戏在跑 → 可能**静默**把运行中游戏正在读的文件换掉，启动器这边一切「成功」，最坏是游戏当场出错。属「值得补但不是高危」：既不损坏安装记录（CRC64 + 暂存 + 提交兜底），也不涉及越权删除。
- **解决**（`6408c58`，2026-09-15 功能轮跟进）：①`ResolveKnownProcessNames` 在没有本地配置时退回远端配置声明的启动程序名 `GameConfigResponse.GameStartExeName`（它与本地 `Name` 本就是同一身份——提交路径已在做相等校验）；②`RunDownloadVerifyLoopAsync` 在每轮下载之后、第一处写入（`RemoveFiles`）之前复查同一道闸门，命中即返回失败**而不是 Stop**，`.tmp` 留在盘上、用户关掉游戏后重试按已有字节继续（检查点按既有终局语义在该出口丢弃）；③判法与报法各收一处——`FindRunningGameFailureAsync` 同时服务计划阶段与写入边界，「运行中的进程怎么写给用户看」收进 `GameProcessNames.DescribeForDisplay`（卸载那条也改走它），下载文案因此从「游戏正在运行，请关闭游戏后再修改文件。」变为点名的「游戏正在运行：BlueArchive.exe。请关闭游戏后再修改文件。」（四语同步改写，键数不变）。守卫：`GameDownloadServiceTests.InstallOrUpdateAsync_WhenTheRemoteDeclaredExecutableIsRunning_RefusesBeforeWritingAnything`（替身只在请求的名字含 `BlueArchive` 时报在跑，等价于证明名字来自远端配置；断言游戏文件/本地清单/检查点一个都没写）、`..._WhenTheGameStartsDuringTheDownload_RefusesBeforeTouchingTheGameDirectory`（第二次探测才报在跑，断言目标文件未落地而 `.tmp` 留在盘上）、`GameProcessNamesTests.DescribeForDisplay_AppendsTheExecutableExtensionAndJoinsWithASeparator`。**变异验证**：把远端兜底改成空、把复查判据喂空后，前两条用例同时变红，还原后转绿。文档：ADR-032 第 8 条决策 + 已知限制第 3 条、AGENTS.md 游戏操作段、CONTEXT.md「游戏进程家族」词条。
- **残留（ADR-032 已知限制第 3 条）**：远端兜底只有宿主一个名字，拿不到本地 `params`；全新安装且只剩反作弊宿主存活时仍可能放行。要彻底解决需要别的信号（例如随包发布的运行器清单），已书面记录。

### AUD-ARCH-005 — `ShellLifecycle` 为 src/ 最高变更热点，Wire/Unwire 16 对订阅镜像靠人工配对【新立案】

- 类别：架构 / 可维护性
- 严重度：Low｜置信度：80｜状态：open｜处置：Accept Risk（下次因结构原因触碰时按 ADR-023 表驱动收敛）
- **证据**：`Features/Shell/ShellLifecycle.cs` 784 行、25 commits/180d（src/ 第一热点；`ServiceConfiguration.cs` 24、`MainWindow.axaml.cs` 22——本审计 git 计数）。持 ~30 协作者（:35-63）、34 行 Wire（:421-454）+ 44 行 Unwire（:562-605）16 对订退对；现有覆盖仅验 Dispose 路径（`ShellLifecycleTests.cs:237-259`），无对称性断言。
- **影响**：每次跨功能事件变更是双点编辑，漏配对称仅能靠人工评审发现。Shell 聚合本身是 sanctioned 例外，疑虑仅在密度。
- **建议**：仓库先例（ADR-021/022）偏好书面接受而非投机抽取——现状接受；若再动 Shell，把订阅收敛为单张声明表由 Wire/Unwire 共同消费，对称性成为数据性质。**建议验证**：Verified（计数）；行动与否 Needs Architecture Decision。
- **与已解决项的关系**：AUD-ARCH-001（模态注册）已由 `ModalRegistrar` 解决；本项是同文件的另一根因（接线密度）。

### AUD-MAINT-001 — `SettingsAppearanceViewModel` 以 5 个静态字段保存主题方案缓存

- 严重度：Low｜置信度：90｜状态：**resolved**（`b04cb83`）｜处置：Refactor（已执行）
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

### AUD-ARCH-011 — `GameShortcutService` 的公开创建路径直接读 `OperatingSystem`，绕过 `ShortcutEnvironment` 注入的平台探针【简并扫描轮新立案；同日解决 `56e0509`】

- 类别：架构 / 测试接缝旁路
- 严重度：Low｜置信度：90｜状态：**resolved**（`56e0509`）｜处置：Fix（已执行）
- **证据**：`Features/GameOperations/GameShortcutService.cs:98-101`（创建）用裸 `OperatingSystem.IsWindows()/IsLinux()`，`:106-109`（删除）用注入的 `isWindowsPlatform()/isLinuxPlatform()`；`:66-71` 的 `ShortcutEnvironment.ForCurrentPlatform` 把探针接到同一对平台检查上，故生产下两者等价。
- **影响**：公开的 `CreateDesktopShortcutAsync`（真正碰桌面的那条）无法被测试接缝切换，平台问题在测试里不可复现——只有删除那条可切换。
- **解决记录**：收成 `ResolveDesktopDirectory()` 由两个入口共用；生产行为不变，变的是公开入口现在尊重接缝。

### AUD-ARCH-012 — `GameOperationJourney` 的 `Ready` 分支是唯一不报告的拒绝【简并扫描轮新立案；同日解决 `588d80c`】

- 类别：架构 / 反馈一致性
- 严重度：Low｜置信度：85｜状态：**resolved**（`588d80c`）｜处置：Fix（已执行）
- **证据**：`GameOperationJourney` 里安装/更新尝试函数的 `if (snapshot.RuntimeState == Ready) return null;`，与同函数另三个分支对照——`Corrupted` 开修复确认框、`IoFailure`/`RemoteUnavailable` 走刷新、结果走 toast。`GameOperationsViewModelTests` 名为 `…_ReturnsUnavailable` 却只断言 `InstallCallCount == 0`，不断言任何反馈。
- **影响**：只在快照过期时可达（`Ready` 下安装按钮所在面板不可见），用户表现为「点了没反应」——与 ADR-027／029 建立的口径（确认后拒绝必须可见、预检失败不静默）不一致。
- **解决记录（`588d80c`）**：改走与策略预检相同的拒绝渲染，未在此处新增策略调用（该分支语义早已判定）；与 `D3`（修复路径闸门归位）同批落地，两者都动拒绝渲染。用例补成它名字承诺的东西（单条警告、Warning 级、文案 `operationUnavailableForCurrentState`）。**变异验证**：拆掉该行报出立刻变红。同批 `D3` 另把修复的两道闸门收进 `GameOperationJourney.RejectRepairIfUnavailable`，并新增「被拒绝的修复不得把面板停在 `Progress`」断言——把闸门挪到 `PrepareOperation` 之后即红（该调用会 latch 面板，只有 `SetIdlePanels` 能复位）。

### AUD-MAINT-005 — 日志级别词表是两份互不引用的独立声明，且任一侧改动都没有守卫【简并扫描轮新立案；同日解决 `cfe9648`】

- 类别：可维护性 / 无守卫的线格式契约
- 严重度：Low｜置信度：90｜状态：**resolved**（`cfe9648`）｜处置：Add Guard（已执行）
- **证据**：生产侧 `Services/Diagnostics/UnifiedLogger.cs:61` 的 `outputTemplate` 用 Serilog 的 `{Level:u3}`；消费侧 `Services/Diagnostics/LogEntryReader.cs:27-29` 的正则硬编码 `(ERR|WRN|INF|VRB|DBG|FTL)`，被日志查看器与导出过滤器消费。两侧互不引用，全仓库没有任何用例断言两者一致。
- **影响**：任一侧改动（换格式、加一级严重度、改拼写）都会让日志查看器与导出过滤器**静默读不到任何条目**（认不出的头行被当成上一条的续行），而两侧各自的既有用例都仍然通过。
- **解决记录**：按计划「先补测试再谈合并」补上往返守卫 `DiagnosticsServicesTests.LogFileHeaderCodes_EverySeverity_AreRecognisedByTheLogEntryReader`——六个严重度各写一条，断言逐条被识别、顺序一致、时间戳可解析、六个代码两两不同。**变异验证**：从正则里删掉 `VRB` 后立刻变红，还原后转绿。词表「合并成一张表」留待后续（先有守卫，再谈是否值得为它建表）。

### AUD-TEST-010 — 动效叠层守卫清单已实际漂移：`DesignGalleryOverlay` 带 `motion-overlay` 却不在扫描集内，`Assert.Equal(9, …)` 仍通过【简并扫描轮新立案；同日解决 `59647cc`】

- 类别：测试 / 守卫覆盖
- 严重度：Low｜置信度：95（本审计实测计数与文件类名）｜状态：**resolved**（`59647cc`）｜处置：Fix（已执行）
- **证据**：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.Motion.cs` 手抄声明 7 个叠层文件、断言元素数 9；而 `src/Cafe.Launcher.Avalonia/Views/DesignGalleryOverlay.axaml:10` 带 `dialog-overlay motion-overlay`。**本审计实测**：该套件 153 条全绿，即画廊的动效契约今天无人守（若纳入清单，计数应为 10）。
- **影响**：与已解决的 AUD-TEST-006 同一根因（手抄白名单在新建文件时漂移）。已有两条元契约（`ScanTargets_CoverEveryTopLevelViewFile`、`StyleFiles_AreExplicitAndParseable`）只管 `ViewFiles`/`StyleFiles` 两个集合，动效清单不在其管辖内。
- **解决记录（`59647cc`）**：清单取消——扫描目标改为按目录发现（`Views` 顶层的 `*.axaml` 里凡带 `motion-overlay` 类的元素都进契约），并补一条刻意过度指定的反空转基线钉住八个文件与十个元素（原先 9），因为发现式扫描一旦扫到 0 个元素，后面的 `Assert.All` 会全绿地什么都不检。不承载叠层表面的文件在前置判断里跳过，因此不再要求它们也声明 `controls` 命名空间。画廊此前未被扫到，但它的动效属性本来就是对的——纳入后暴露的是**覆盖面**而非新缺陷。**变异验证**：拆掉 `DesignGalleryOverlay.axaml` 的 `controls:MotionVisibility.IsMotionEnabled` → 该用例变红；这条变异在改法之前不可能被任何断言抓到，正是本项要堵的洞。

### AUD-TEST-011 — 破坏性路径的用例默认绑定真实进程扫描器：开发机上有游戏进程时整类用例变红，与用例意图无关【简并扫描轮新立案；同日解决 `7161f4c`】

- 类别：测试 / 环境依赖
- 严重度：Low｜置信度：90｜状态：**resolved**（`7161f4c`）｜处置：Fix（已执行）
- **证据**：`GameUninstallServiceTests.CreateService` 默认 `processTracker ?? new GameProcessTracker()`，而 `Services/GameRuntime/GameProcessTracker.cs:25` 的默认构造绑定 `ProcessService.FindRunningExeNamesAsync`（真实系统快照）；`GameUninstallService` 在写入边界复查该闸门。同类站点另有 16 处（`InstallationOperationStateTests`、`MainWindowTestContext`）。本仓库已实际发生过一次同类事故——`GameDownloadServiceTests` 里 `CreateTrackerReportingNoGameRunning` 的注释记录了它（机器上开着 `BlueArchive.exe`，提交路径用例全部撞上「游戏正在运行」闸门）。
- **影响**：开发机/CI 上有游戏进程存活时，卸载与提交相关用例按设计拒绝执行而变红，原因与用例意图无关；一次真回归会混进这片噪声里。
- **解决记录**：新增 `tests/TestDoubles/TestGameProcessTracker`（`None`/`Running`），17 处「与用例断言无关」的默认绑定改为 `None`，删掉两个私有替身与一处工厂；只有「正在运行」本身是断言对象的用例才注入 `Running`。**变异验证**：把 `None()` 改成报「在跑」→ **36 条用例变红**（下载/安装/卸载路径），正是该缺陷的描述形态。

### AUD-TEST-012 — 「拆除提示条时唤醒挂起的倒计时等待」无覆盖：删掉该唤醒后 86 条提示条用例全绿【简并扫描轮新立案】

- 类别：测试 / 守卫覆盖
- 严重度：Low｜置信度：90（变异实测）｜状态：open｜处置：Add Guard
- **证据**：`ViewModels/ToastHostViewModel.cs` 的 `EndLifecycle` 里 `countdown.Interruption?.Cancel(); ResumeCountdown(countdown);`。**变异验证**：删掉这一段后 **86 条提示条用例全绿**。
- **影响**：失败形态是「挂起的倒计时任务一直阻塞到宿主 `Dispose`」——只漏一个 `Task`、不产生错值，因此没有任何断言看得见。属合并前即存在的缺口（原 `StopCountdown` 做同一件事，`B17` 的字典合并原样保留）。
- **建议**：在生命周期记录上挂本次任务引用作为测试缝，补一条「挂起态提示条被拆除后其等待被唤醒」的用例。**建议验证**：Verified（变异实测）。
- **对照（同提交的另一半**有**覆盖）**：退出信号的移交去掉后 `ToastExit_WhenAutomaticAndManualRequestsOverlap_WaitsAndRemovesOnce` 立刻变红。

### AUD-TEST-013 — 无头套件泄漏 `Application.RequestedThemeVariant`，golden 截图依赖用例顺序【简并扫描轮新立案；同日解决 `779664f`】

- 类别：测试 / 隔离性
- 严重度：Low｜置信度：95（复核后由 60 上调：两处锚点已逐行读码，一真一假，见下）｜状态：**resolved**（`779664f`）｜处置：Fix（已执行）
- **证据**：生产写入点唯一，`D14`（`8aab531`）搬移前在 `Features/Settings/SettingsAppearanceViewModel.cs:584`，**现为 `src/Cafe.Launcher.Avalonia/Services/ThemeApplier.cs` 的 `ApplyThemeMode`**（读历史描述时按新位置定位；设置外观 VM 已不再写它）。无头侧 `CrashReportWindowHeadlessTests.cs` 与 `ThemeSubscriptionTeardownHeadlessTests.cs` 当时各自手写快照 + `finally` 复位（证明危险已知）；`MainWindowHeadlessTests.Golden.cs` 的 `PrepareGoldenWindow` 只固定语言、动效与字体，**不固定变体**。
- **两处待复核锚点的复核结果（本条置信度只有 60 的原因）**：
  - `MainWindowHeadlessTests.Dialogs.cs:332`（`LogExport_WhenAContentRowIsChecked_KeepsItsGlyphReadable` 的暗色分支）**为真**：经 `ApplyTheme` 写变体且从不复位，是唯一真实泄漏点。
  - `SystemThemeColorHeadlessTests.cs:26-33` **为假**：该用例打的是 `ApplyPlatformColorValues`，那条路径只按当前草稿算出 isDark 并落方案，从不写 `RequestedThemeVariant`。若照原报告直接改这个文件，就是白改一处。
- **影响**：共享一个 `Application` 的套件里，golden 截到亮色还是暗色取决于同批次哪个用例先跑——属「偶然绿」。
- **解决记录（`779664f`）**：两半缺一不可——①`PrepareGoldenWindow` 每次显式钉住基线变体（默认亮色＝无头平台把 `ThemeVariant.Default` 解析到的值），golden 从此与执行顺序无关，且它是整套 golden 的基线而非某用例的临时覆盖，故不还原；②新增 `tests/Cafe.Launcher.Avalonia.HeadlessTests/ThemeVariantSnapshot.cs`（`HeadlessTestHost` 旁：记下进入前的变体、改为指定值、释放时还原），Dialogs 用例与两个原本手写复位逻辑的用例统一走它。**守卫与变异验证**：新增 `GoldenPrep_WhenTheAmbientVariantWasLeftDark_StillPinsTheBaselineVariant`（哨兵法）与 `ThemeVariantSnapshotHeadlessTests.Capture_WhenDisposed_RestoresTheVariantItFound`；拆掉钉住、拆掉还原各让对应用例变红。**像素影响**：golden 未重生即全绿，证明钉住的亮色正是今天的像素。**一处刻意的覆盖缺口**：Dialogs 用例改用快照这件事本身无法独立观察（钉住会掩盖泄漏的后果），没有测试会因「忘记用快照」而变红——钉住才是确定性来源，属取舍。

## Informational Findings

无（AUD-ARCH-004 已随 `95b9f8b` 归入 `Features/Diagnostics`；AUD-ARCH-006 已随 `700e674` 修正并结案，均转 Resolved Findings）。

## Advisory（不立案汇总）

计划落地期间新增（2026-09-16，置信度低于报告线或属决策项）：

- **`DownloadExecutorTests.InstallDownloadedFilesAsync_WhenManyFilesVerifyInParallel_ReassemblesFailuresInManifestOrder` 一次未复现的失败**（置信度 40）：当日 `verify.ps1` 内带 coverlet 插桩的单元运行里失败过一次（耗时 37 ms，正序第 2 次插桩运行），此后 12 次定向插桩 + 9 次全量插桩 + 2 次普通全量运行全部通过；失败消息因 TRX 被下一次运行覆盖而丢失。该用例正在 AUD-TEST-008 处理过的那个家族里（并行校验路径的测试侧收集），但那次修复已让 `CallbackRecorder` 线程安全，本次也指不出具体竞态点。**不立案**：没有消息就没有可行动的判据。若 CI 再出现同名红，请连消息与 TRX 一起留下再立案——先按「测试侧偶发」怀疑，再按「生产侧并行校验真有竞态」怀疑。

功能轮新增（2026-09-15，置信度低于报告线或属决策项）：

- **彻底清除阶段没有进度反馈**（置信度 70）：`GameUninstallService.cs:118-142` 的百分比闸门只覆盖清单文件；`DeleteThoroughCleanupTargetsAsync`（:263-289，跑在线程池上）整段不发进度，24 GB 安装的删除期间面板停在 100%。ADR-030 第 4 条为 3.4 秒的测量专门做了「先弹框、尺寸后到」，同一条思路没有延伸到更长的删除段——但删除期间有 `SetBusy` 与操作表面在动，与「点了没反应」不同档，故不立案。
- **尺寸统计不可取消**（置信度 60）：`MeasureFootprintAsync` 的 `Task.Run(..., cancellationToken)` 只能拦住启动，`DirectorySizeProbe.Measure` 自身不查 token，用户关掉对话框后遍历仍把整棵树走完（结果被 `IsVisible` 守卫丢弃）。纯浪费 I/O，无正确性影响。
- **`Helpers/ProcessService.cs` 现引用 `Services/GameRuntime`**（置信度 55）：`GameProcessNames.BelongsToFamily` 是 Helpers 唯一的 Services 依赖（grep 证实全仓 Helpers 仅此一处）。两者同属 AGENTS.md 所列「共享基础设施」，没有成文的方向规则，故仅记录：若将来立「Helpers 不得依赖 Services」的分层规则，这一处需要随之下沉或改接缝。

复审新增（2026-09-14 下午，置信度低于报告线或属决策项）：

- **settings.json 每次壳层刷新双读 + 每读双解析**（置信度 75）：`LauncherCoreService.cs:77` 的 `LoadAsync` 在 `ShellLifecycle.cs:223` 已读之后再次 `settingsService.ReadAsync`；`LauncherSettingsService.cs:79` 反序列化后 `ApplyLegacyFields`（`:122-124`）又无条件 `JsonDocument.Parse(json)` 一次。全部在线程池，文件 KB 级——纯冗余工作，改动需先厘清两次读取的快照一致性语义。
- **DesignGalleryViewModel 内联构造于 DialogsViewModel、缺席组合根**（置信度 62）：`DialogsViewModel.cs:173` `new DesignGalleryViewModel(...)`，与既有的确认对话框族内联构造先例（文档化 :28-32）相邻但类型不同、无归属注释——是「对话框族先例的延伸」还是「违反组合根声明」，Needs Architecture Decision，不立案。
- **NeutralStrategyHeadlessTests.cs:100 `finally` 内裸 `Directory.Delete`**（置信度 65）：同文件族既有 deliberate swallow 范式（`MainWindowHeadlessTests` catch IOException/UnauthorizedAccessException 并注释理由），此处缺失——迟释放句柄可把通过测试变失败或掩盖断言异常。
- **GamePathValidator reparse 游走每调用重新 stat 根与全路径段**（置信度 70）：`GamePathValidator.cs:47,:72-98` 无父目录结果缓存，在计划/校验/安装/卸载每文件循环中每文件走一遍；安全意图正当，冗余是重复 stat（线程池，非 UI）。
- **M3 色彩方案在 UI 线程按 swatch 重复全量重建**（置信度 60）：`MaterialSchemeGenerator.cs:113-118` 中性跟随关闭时每次 `BuildRoleBrushes` 构造第二个完整 HCT 方案读 3 个角色；`RefreshThemeColorPaletteBrushes`（`SettingsAppearanceViewModel.cs:484-497`）每 swatch 一方案。仅设置交互时触发，感知度未测。
- **进度钳制状态不在 `PrepareOperation` 重置**（置信度 55，低于报告线）：`GameOperationsViewModel.cs:175-189` 只归零 `ProgressValue` 不重置 `displayedProgressStage/Floor`；现状无害依赖「新操作首阶段 ≠ 上操作末阶段」的未钉住不变量。若未来 `FileCheck` 成为某操作的首阶段会冻结进度条——触碰该文件时顺手重置即可。
- **`ImageCacheService.cacheLocks` 进程期不收缩**（置信度 55，低于报告线）：每去重键一个 `SemaphoreSlim` 仅 Dispose 清空；launcher 规模下有界，纯保留卫生。

既有（上轮遗留，维持）：

- **组合根运行时服务定位**（已处理 `043279e`）：transport 工厂内 `ISettingsEditor` 改为构造时一次解析并闭包引用，代理模式解析不再重复服务定位。
- **DI 工厂内静态注册共享日志器**：`ServiceConfiguration.cs:42-48` 首次解析时 `LocalDiagnostics.RegisterSharedLogger(logger)`（`Volatile.Write`），构建多容器的测试会令首个容器的 logger 成为全局目标。生产路径 `App.axaml.cs:58` 急切解析一次，序确定；影响限于诊断误路由。建议测试 teardown 复位或显式一次性注册。（置信度 72，advisory 档）
- **Wire() 委托缝**：四个可空委托由 Shell 在构造后赋值（`ShellLifecycle.cs:426-428,:448`），已文档化为意图（`SettingsViewModel.cs:49`「Coordination delegates — set by parent after construction」）、空条件消费、测试钉住——有意设计，仅记录依赖图对构造签名不可见这一属性。
- **清单 `.tmp` 暂存名与 `.tmp` 结尾清单条目可互撞**（置信度 45，低于报告线）：敌意清单可声明 `x.tmp` 使其与 `x` 的暂存名重合；清单内容可控本就意味着内容可控，不构成独立完整性绕过。可加廉价断言（清单路径不得以暂存后缀结尾）。
- **等待助手私有变体 5→7 份**、`GameDownloadServiceTests.cs` 2068 行（最大测试文件）：维护成本项，仓库已有按域拆分先例（`UiStyleContractTests` 11 分部），随下次触碰收敛/拆分。
- **`AtomicJsonFileStore` 写失败路径无直接测试**（消费侧已间接覆盖：`LocalInstallationStateStoreTests.cs:174` 中途移动失败终态可读）——仅在改动该类时补一例。
- **卸载走 `GetSafePath` 而非 `GetSafeFilePath`**：根自身规范化条目不逃逸但会使卸载以异常中止——可用性边角，非完整性。

## Architecture

**结论：文档边界与实现一致，模态隔离裁定与 ADR-023 收敛经受住了本窗口两轮 UI 重构。**

- **跨功能边界零违规**（复核）：全量 `using` 扫描，越界仍仅 `ShellLifecycle`/`ShellPresentationFamily`/`ShellStartup` 三文件 = sanctioned 例外；`DebugViewModel` 仍经 `IGameOperationActivity` 窄抽象消费（组合根绑定 `:154-155`）。功能轮（2026-09-15）新增依赖仅一条 `Helpers/ProcessService.cs` → `Services/GameRuntime/GameProcessNames`（族判定），不属功能边界（两者同列 AGENTS.md 的共享基础设施），记录见 advisory。
- **模态注册声明式收敛保持**：19 个 `ModalKind` ↔ 19 条注册（`ShellLifecycle.cs:461-548` ↔ `ModalKind.cs:5-25`），`TryHandleEscape` 2 行委托（:608-612）；`ResourcePanelOverlay` 拆分（`e56d54b`）未破坏裁定——新覆盖层自带 `IsResourcePanelInteractive` 门（`ResourcePanelOverlay.axaml:12-14`）且仍是主叠层FirstChild、位于对话框层之下。
- **新抽取件干净**：`OperationSurfaceAnimator`（190 行）逐字搬移、ADR-016 注释保留、headless 动效套件未弱化；残留（两处未用 using、锚点退役回调跨文件）见 AUD-ARCH-002 解决记录。
- **组合根纪律**：全 Singleton、纯构造注入、释放顺序显式注释且经读码核实（客户端注册于 `HttpClientFactory` 之后 :115-135）；`Program.ServiceProvider` 仅用于会话末释放。两处轻微偏离见 advisory。
- 剩余 Low 发现：AUD-ARCH-005（接受中，若再动 Shell 按声明表收敛）；ARCH-004/006/007 已分别随 `95b9f8b`/`700e674`/`043279e` 结案。

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
- 发布侧完整性缺口已于 `db941bf` 以 `attest-build-provenance`（OIDC 来源证明）补全；证书签名仍为可选后续。

## Testing

**结论：纪律持续兑现。上轮 4 项测试发现全部真实解决；修复轮的守卫测试逐项核实到位（SEC-003 两用例、PERF-006 两用例断言与 `DownloadSession.cs:439` 的显式归零兼容、`IsSameAuthority` 双向钉住）。复审新立案的 3 项测试缺口（TEST-005/006/007）已随第二修复轮全部补齐并配套元契约/哨兵守卫；收口实测单元 1717 通过 / 0 失败 / 2 可见跳过 + Headless 178 通过 / 0 失败。功能轮（2026-09-15）复测：单元 1819 通过 / 0 失败 / 1 可见跳过（总 1820）+ Headless 184 通过 / 0 失败；同日跟进修复后为单元 1820 / 1 跳过（总 1821）· Headless 184；新增守卫集中在破坏性删除路径与进程家族判据（见 Automated Guards Added 11-16），并修掉一处随环境变色的用例——`GameDownloadServiceTests` 原先用真实进程扫描，开发机上开着游戏就会让提交路径用例集体撞上「游戏在跑」闸门。** 测试设施轮（2026-09-16）复测：单元 1853 通过 / 0 失败 / 2 可见跳过（总 1855）· Headless 185 通过 / 0 失败，`verify.ps1` 全绿（覆盖率行 87.17% / 分支 93.62%）；临时目录、异步等待与主窗口装配改由 `tests/Support/` 与 `MainWindowTestContext` 统一提供（AUD-TEST-009），两个测试工程从此共用同一份设施源码。**

- **关键路径保护**（复核保持 + 一处降级）：下载续传/CRC/限速、安装状态损坏矩阵、设置兼容（legacy 字段 + DeepClone 棘轮）、卸载边界、URL 校验、更新流三分支 + 确认接线（AUD-TEST-002 解决）均钉住；**例外**：并行安装校验的并发语义无多文件用例（AUD-TEST-005，Medium）。
- **确定性**：正面等待全部有截止/迭代上限（复审全树检索无悬挂面）；程序集级串行 + 静态清单 + 用户数据隔离保持；`Assert.Skip*` 16 处，平台分支全部可见跳过（复审复核保持，零隐藏跳过）；`ResourcePanelApiClient` 5 个专用用例。复审另发现一处确定性边角：`NeutralStrategyHeadlessTests.cs:100` 裸 `Directory.Delete`（advisory）。
- **CI 门禁**：覆盖率棘轮在 build.yml 强制（85.85%/92.70% 基线 + 余量打印）；golden 失败工件闭环（actual/diff PNG `if: always()` 上传，路径与写盘一致核实）；契约测试全部在可执行路径上、无本机-only 测试；golden 基线契约（`GoldenBaselineContractTests.cs:16-44` 严格 1:1）复审核实无孤儿/缺失。
- **fire-and-forget 复核**：`1f09bc8` 落地的三处 `_ = diagnostics.DebugAsync(...)` 证实安全（`LocalDiagnostics.cs:109-123` 内部吞异常，无 unobserved-task 风险）；ArrayPool rent/return `try/finally` 包裹无 use-after-return。

## Performance

**结论：上轮全部性能发现落地且质量好（修复非纸面）；复审独立重扫确认启动/首帧、定时器、事件泄漏、位图生命周期、ArrayPool 修复均干净，新立案一条 Low（PERF-007 非下载阶段逐文件 UI 回调）与四条 advisory。**

- **复审核实干净**：启动/首帧路径除文档化壁纸解码外仅 `ImageCacheService` 一处 `Directory.CreateDirectory`；全仓库定时器清点（2 个自停一次性动效 timer、1 轮播、2 个 250ms 内核等待轮询 + 生成守卫的 debounce）；`Crc64Service`/`FileDownloadService` 池化租还并发正确；`ShellLifecycle`/`ShellStartup` Wire 有 `isWired` 守卫，overlay 重订阅先退订；壁纸旧位图交叉淡化释放握手与陈旧代次处置正确。
- **AUD-PERF-001 残留**与 **AUD-PERF-005 残留**见 Low 节；PERF-006 已修复（`608d888`）；**AUD-PERF-007** 已随 `608d888` 解决（PercentProgressGate 百分比门控）；settings 双读双解析、reparse 游走重复 stat、M3 方案重建、cacheLocks 保留见 Advisory。

## Maintainability / Technical Debt

- 热点与结构互证：`ShellLifecycle` 25、`ServiceConfiguration` 24、`MainWindow.axaml.cs` 22 commits/180d——接线处变更多的正常形态；动效引擎已出窗（`cf353cd`），`ShellLifecycle` 的 Wire/Unwire 密度立案为 AUD-ARCH-005。
- 文档漂移三处已修复入库（2026-09-14：`700e674`/`521f4c1`）：AUD-ARCH-006（ZIndex 表述，结案）、AGENTS.md:75 首帧承诺（AUD-PERF-005 文档半项）、`RemoteHttpUrlValidator` DNS 缓存反向框定注释（AUD-SEC-001 证据修正）；advisory 的组合根服务定位已随 `043279e` 一次解析化。AUD-MAINT-001 静态缓存与 AUD-ARCH-004 VM 归属亦已分别随 `b04cb83`/`95b9f8b` 落地。
- 复审新立案的两处低级卫生项已随第二修复轮解决：AUD-MAINT-003（`de2a1ee` UnifiedLogFileName 常量收拢）与 AUD-MAINT-004（`d4ca700` 化石注释更正为 FirstChild 层序不变量说明）。另核实：架构子代理全量 `using` 扫描再次零跨功能违规；`MotionTokens` 双梯有 `MotionTokensTests` 钉住不立案；`CrashReportWindow` 自带 token 族属可辩护隔离。
- `docs/architecture-review-2026-09-13.html` 已入库（`54c1871`）；官方协议对比文档按用户裁定 accepted-risk 结案（AUD-MAINT-002），行为不变量由 `OfficialHashServiceTests`/`AuthorizationHeaderFactoryTests`/`LauncherConstantsTests` 钉住。

## Decisions Required

第二修复轮后仅剩三项既有决策（均维持原裁定）：

1. **AUD-PERF-001 残留**：更新路径是否在「自愈契约」前提下引入见证摊销（并行化已交付；需基准实测后再决策，未测量不得轻动）。
2. **AUD-PERF-005 残留**：首次刷新二次解码是否消除（需先确认跳过卫的解码目标可合法匹配；与同步/异步之争互不绑定）。
3. **AUD-ARCH-005**：若再因结构原因触碰 Shell，按 ADR-023 声明表收敛 Wire/Unwire（维持接受）。

功能轮（2026-09-15）新立案的 AUD-ARCH-008 已于同日跟进按建议 (a) 解决（`6408c58`：`UninstallAsync` 删除前复查家族闸门 + 变异验证过的守卫用例），不留待决项。

复审新立案的 7 项已于第二修复轮全部落地（6 项解决 + SEC-006 转书面化接受）。

## Resolved Findings（本窗口，8 项）

- **AUD-ARCH-002**（`cf353cd`）：操作表面动效套件抽出为 `OperationSurfaceAnimator`（190 行），窗口 code-behind 663→499 行；入场锚点与壁纸淡化按提交消息明示刻意留存（次优先），残留 advisory（两处未用 using、锚点回调耦合）随下次触碰清理。
- **AUD-ARCH-003**（`67229b5`）：双构造所有权制度按原建议第二分支书面化（`ShellLifecycle.cs:115-121`，含差异、测试不可复现性、收敛前置与「已裁定可接受」明示）；分叉保留但已裁定接受。可选升格 ADR。
- **AUD-TEST-002**（`a6d3794`）：`SettingsViewModelTests` 4 用例覆盖 `CheckForUpdatesAsync` 三分支 + 失败消息格式化；`ShellLifecycleTests` 11 用例覆盖确认接线端到端（恰一次打开、Dispose 退订、启动失败降级）。残留 advisory：空 `FailureMessage` 子分支无直接断言。
- **AUD-TEST-003**（`08c53f8`）：`Assert.Skip*` 16 处 + 1 attribute Skip；残留早退全部位于 SkipUnless 之后且为 CA1416 守卫；本地实测 2 项可见 SKIP、零隐藏跳过。
- **AUD-TEST-004**（`1f09bc8`）：处置观察窗 80→400ms（>1× 生产 250ms 轮询，附推导注释）；700ms 重文档化为回归绊网而非生产常量复制。残留 advisory：否定窗仍为固定睡眠（标定合理）。
- **AUD-PERF-002**（`1f09bc8`）：Stop/Pause/Resume 点击路径全部 fire-and-forget 并附 §3.2 意图注释；grep 证实 UI 点击路径零残余同步阻塞。
- **AUD-PERF-003**（`1f09bc8`）：下载缓冲 `ArrayPool.Rent` + finally 归还（return 在 try/finally 外，归还有保证）；内容非敏感无需清零。
- **AUD-MAINT-002**（结案为 accepted-risk）：按用户 2026-09-12 裁定不入库；工作树文件已移除；行为不变量由既有测试守卫兜底。

同日修复轮（2026-09-14，按优先级逐项提交，全部已入库）：

- **AUD-SEC-003**（`d7076cb`）：`RemoteHttpRequestService.SendAsync` 对非初始授权方（scheme+host+port）的跳剥离 `Authorization`，与 .NET 内建 HttpClient 的跨主机剥离约定对齐；配套跨主机剥离/同主机保留两个守卫测试（`AuthTrackingRedirectHandler`）。
- **AUD-CI-001**（`08c53f8`）：`build.yml` 新增 `linux-unit-tests` job（ubuntu-24.04，push/PR 触发并阻塞），复用 weekly 作业体；平台分支回归现在阻塞 PR。
- **AUD-PERF-006**（`608d888`）：`ApplyProgressCore` 按阶段键控钳制进度单调；与重试轮经 `VerificationRetry` 折返 `FileCheck` 显式归零（`DownloadSession.cs:439`）的流程兼容；配套乱序回退/阶段重启守卫测试。
- **AUD-SEC-005**（`db941bf`）：release job 追加 `actions/attest-build-provenance` v4.2.2（SHA 钉住）为六个分发包与 SHA256SUMS 签发 OIDC 构建来源证明；补 `id-token`/`attestations` 权限。
- **AUD-SEC-004 + AUD-ARCH-007**（`043279e`，均结案 accepted-risk）：`ProxySettingsService` 注释书面化同用户凭据让步与活租约处置权衡（一轮失败重试自愈、租约引用计数判为更高风险）；顺带将组合根 `ISettingsEditor` 改为一次解析（advisory 项闭合）。
- **AUD-MAINT-001**（`b04cb83`）：主题方案缓存五字段、`ApplyScheme`、`ActualThemeVariantChanged` 订阅全部转实例；`Dispose` 拆卸订阅（headless 共享 Application 不再跨测试累积）；两处 headless 测试改经 DI 构造的 VM 实例调用。
- **AUD-ARCH-004**（`95b9f8b`）：`DesignGalleryViewModel` 移入 `Features/Diagnostics`（其天然宿主），命名空间随目录。
- **AUD-ARCH-006**（`700e674`）与 **AUD-SEC-001 注释更正**（`521f4c1`）：AGENTS.md ZIndex 表述如实化 + 首帧承诺记录壁纸例外 + DNS 缓存反向框定更正。

修复轮验证：每阶段跑聚焦测试；收口时全量套件（单元 1710 通过/0 失败/2 可见跳过 + Headless 177 通过/0 失败）与 Debug 构建零警告；`LineEndingPolicyContractTests` 绿。CI 编排类改动（build.yml/release.yml）经 YAML 解析校验，未实际触发 workflow 运行。**复审逐项核实（2026-09-14 下午）：9 项修复在 `dc1a6ef` 工作树全部为真实落地**——SEC-003 剥离逻辑与两守卫测试（`RemoteHttpTransportTests.cs:305,:329`）、CI-001 `build.yml:96-97` linux job、PERF-006 `ApplyProgressCore` 钳制与两守卫测试（`GameOperationsViewModelTests.cs:729,:752`）、SEC-005 SHA 钉住的 attest 步骤与权限、SEC-004/ARCH-007 书面化注释（`ProxySettingsService.cs:94,:156`）、MAINT-001 静态字段清零（仅余两个无状态辅助方法）、ARCH-004 文件归位、ARCH-006/SEC-001 文档更正均在。

此前已解决（维持）：AUD-ARCH-001（`2fe3565` ModalRegistrar）、AUD-TEST-001（`f123429..f123429` RemoteHttpTransport 接缝）。

第二修复轮（2026-09-14 晚，按杠杆序逐项提交）：

- **AUD-TEST-005**（`67229b5`）：`DownloadExecutorTests` 增至 13 用例（随后的 `608d888` 另补 400 文件接线测试，至 14）——12 文件（>并行度 8）混合布局钉住失败按清单序重组、失配 .tmp/终路径删除、通过文件搬移、进度每文件一次；缺失 .tmp 只标记该文件失败。
- **AUD-SEC-006**（`521f4c1`，结案 accepted-risk）：执行时发现补救已被 `5a38be9` 否决（fake-ip 代理 DNS 应答落 198.18/15、CDN 边缘节点落 100.64/10，拦截即回归用户可见故障）——转书面化接受：switch 显式放行臂 + 让步注释 + 198.18/15 放行守卫测试。
- **AUD-TEST-006**（`3ea7baa`）：`ResourcePanelOverlay` 补入共享 `ViewFiles`；`ScanTargets_CoverEveryTopLevelViewFile` 元契约令 Views/ 顶层白名单漂移不可能（`CrashReportWindow` 显式豁免留名）。复核跟进：`MainWindowDebugOverlay` 原经匿名 `Append` 放行，已补入 `ViewFiles`（内联 `Padding` 以 `dialog-card.compact` 样式类收口，视觉不变）。
- **AUD-TEST-007**（`b04cb83`）：`ThemeSubscriptionTeardownHeadlessTests` 哨兵法双相守卫——对照相自证哨兵能侦测在位处理器，Dispose 后哨兵必须存活；退订行被删即失败。
- **AUD-MAINT-003**（`de2a1ee`）：`GamePaths.UnifiedLogFileName` 收拢三处硬编码，轮转名从常量词干派生；测试字面量保留为线钉。
- **AUD-PERF-007**（`608d888`）：`PercentProgressGate`（Interlocked 值变化门控）应用到校验/stat/修复扫描/卸载四处产生侧；门控四态单测 + 400 文件同桶去重接线测试；显式阶段发射有意不过门。
- **AUD-MAINT-004**（`d4ca700`）：承载点化石注释更正为 FirstChild 层序不变量说明。

第二修复轮验证：每阶段跑聚焦测试后逐项提交；收口全量套件本地实测（Debug，`d4ca700`）：单元 1719 总量 = 1717 通过 / 0 失败 / 2 可见跳过 + Headless 178 通过 / 0 失败（新增守卫 1 例）。

CI 对账轮（2026-09-14 晚，AUD-CI-001 守卫首跑产出）：

- **AUD-CI-002**（`08c53f8`）：测试隔离目录四层 ≈104 字符，`Listen`/`Raise` 派生 Unix 套接字路径 ≈132 字节超 AF_UNIX 108 上限，绑定永不成功——压短为 `<tmp>/cl-tests/<guid>` 并加派生路径 ≤107 机械守卫。
- **AUD-CI-003**（`08c53f8`）：无效文化名用例改用双连字符格式非法名（ICU 对「格式合法但未知」宽容创建，原选名在 Linux 不抛异常）。
- **AUD-CI-004**（`08c53f8`）：非法字符集用例拆为跨平台路径分隔符断言 + Windows 集合精确断言（可见跳过）；产品按平台取字符集的行为本身正确。

CI 对账轮验证：本地（Windows，Debug，`08c53f8`）全量单元 1720 总量 = 1718 通过 / 0 失败 / 2 可见跳过；Linux 侧行为以推送后 CI 首绿为最终确认（守卫首跑与三例失败均与推送无涉——schedule 跑的是推送前旧 HEAD `67229b5`，同样红出相同三例）。

测试设施轮（2026-09-16）结案：

- **AUD-TEST-009**：见 Low 节（共享设施 + 上下文所有权 + 慢用例拆分；同配置实测单元 31.8s → 23.9s 测试用时，最慢 10.26s → 2.07s）。

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

CI 对账轮（2026-09-14 晚）顺带落地的守卫：

6. `TestUserDataIsolationTests.IsolatedUserDataDirectory_KeepsDerivedUnixSocketPathUnderKernelLimit`——派生 Unix 套接字路径 ≤107 字节的跨平台机械守卫（AUD-CI-002，防止隔离目录再度变深）。

架构评审复核轮（2026-09-15）顺带落地的守卫：

7. `GameOperationJourneyTests.ValidateUninstallAsync_WhenValidationFails_ReportsTheExecutorReason` 与 `GameOperationsViewModelTests.RequestUninstallCommand_WhenValidationFails_ReportsReasonAndSkipsConfirmation`——预检失败必须报出执行层原因（ADR-029，回退为静默即红）。
8. `GameOperationsViewModelTests.RequestUninstallCommand_WhenValidationFailsWithoutReason_ReportsGenericWarning`——无原因边界走通用文案（防止弹出空提示）。
9. `GameOperationJourneyTests.ValidateUninstallAsync_WhenValidationSucceeds_ReturnsTheResultWithoutReporting`——反向守卫：成功路径不得多报一条 Toast。
10. `TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites` 增两条反空转基线（扫描面 ≥231 个 `.cs`、解析点 ≥5 个）——原先「扫到零个文件」与「树是干净的」不可区分，基线为 2026-09-15 实测值，删文件时同步下调。

功能轮（2026-09-15）顺带落地的守卫：

11. `DirectoryTreeDeleterTests`（12 Fact + `IsUnder` 2 例 Theory）与 `DirectorySizeProbeTests`（3 Fact + 空路径 3 例 Theory）——递归删除的越界/盘根/reparse 根拒删、树内链接只删链接且目标存活、只读文件也删、删不掉的条目不影响其余删除并按路径交回；测量的跳过链接口径与删除一致（`TestSymlinks` 抽为共享助手，junction 回退 + 可见跳过）。
12. `GameUninstallServiceTests` 新增 8 例（标准卸载删快捷方式、彻底清除删整棵树与受管子树、自定义前缀保留并说明、删不掉仍成功并点名、游戏根是链接时失败而不抛、`MeasureFootprintAsync` 双树尺寸）与 `GameShortcutServiceTests` 删除三态。
13. `GameProcessNamesTests`（4 Fact + 2 Theory 共 13 例：族推导与正反例——`xldr` 单段不认领、`BlueArchiveData` 同前缀不匹配）与 `GameProcessTrackerTests` 更新（句柄优先、扫描回落、句柄活着但扫描看不见时用注册时记下的名字）＋ `GameUninstallServiceTests.UninstallAsync_WhenOnlyTheAntiCheatHostIsStillRunning_RefusesAndNamesIt`（**2026-09-15 复核轮更正**：立项时写作「只认宿主时这条会放行」，实测不成立——该用例当时的替身忽略入参、夹具用的是与判据无关的合成名，注入 `FromLaunchConfiguration(name, null)` 后照样绿。现改为按实测启动配置构造并要求每个探针都带齐 `name` 与 `params` 两个名字，注入同一回归即红）。
14. `SavedSettingsWriterThreadingTests`（4 例，含后台线程调用方的超时上限与「反空转」断言：无 `Current` 通知即失败）与 `MainWindowHeadlessTests.WindowState` 的启动退出用例（`CloseBehavior = Minimize` 时仍真关闭——`Closed` 与 `IsVisible` 分得开这两者）。
15. `UiStyleContractTests.Dialogs` 确认框可选行的本地化名 + `Mode=TwoWay` 断言、`UiStyleContractTests.Settings` 常规分区绑定清单；`ResxResourceContractTests` 键数基线 555→564（含逐步来源注释）。
16. `GameUninstallServiceTests.UninstallAsync_WhenTheGameStartedAfterThePrecheck_RefusesAndDeletesNothing`——执行边界的进程家族复查（AUD-ARCH-008 守卫，变异验证：拆掉复查即红）。
17. `GameDownloadServiceTests.InstallOrUpdateAsync_WhenTheRemoteDeclaredExecutableIsRunning_RefusesBeforeWritingAnything`——远端兜底判据（AUD-ARCH-009 守卫；替身只在请求的名字含 `BlueArchive` 时报在跑，等价于证明名字来自远端配置）。
18. `GameDownloadServiceTests.InstallOrUpdateAsync_WhenTheGameStartsDuringTheDownload_RefusesBeforeTouchingTheGameDirectory`——下载的写入边界复查（AUD-ARCH-009 守卫；断言目标文件未落地而 `.tmp` 留在盘上）。与 17 同批变异验证：拆掉兜底与复查即红。
19. `GameProcessNamesTests.DescribeForDisplay_AppendsTheExecutableExtensionAndJoinsWithASeparator`——用户可见的运行中进程报法（两个入口共用一处的守卫）。

测试设施轮（2026-09-16）顺带落地的守卫：

1. `TestSupportFacilityTests`（14 例）——共享设施自身的行为：目录独立与派生数据根、`Dispose` 幂等且真的删除、删除失败在 `ReportFailure` 下抛出（Windows 句柄占用，其它平台可见跳过）与 `BestEffort` 下留下目录不抛、`TestWait` 的立即返回/轮询/超时消息/取消/推进动作、`TestRepository` 的路径缓存与「每次重装资源快照」（先装自定义资源再调用必须恢复仓库资源）。
2. `MainWindowTestContextTests`（3 例）——上下文契约：装配返回后日志仍可写并落到自己的文件、`Dispose` 后日志文件可删（句柄释放）、两个上下文互不可见对方的 `settings.json`。
3. `ServiceConfigurationTests` 增 2 例——显式数据根贯穿登记项与闭包（设置/崩溃快照/日志/图片缓存/下载检查点/公告状态都落在指定根），缺省仍解析进程根（生产行为不回归）。
4. `RemoteHttpTransportTests.GetJsonAsync_WhenSystemProxyConfigured_DialsTheProxyAndReadsItsAnswer`——回环明文代理实证「系统代理租约真的把请求发到代理」（原来由资源面板用例顺带覆盖，靠失败重试结束）。

## Verified Strengths

最高杠杆的三项（防止不必要的重构/担忧）：

1. **模态隔离裁定经受住了本窗口两轮 UI 重构**——`ResourcePanelOverlay` 拆分与确认对话框收敛后，七个主叠层交互门与对话框层无闸口结构逐字保持；任何「给对话框层加闸口」的提议仍应拒绝。
2. **上轮审计发现被成批真实落地**——5 项 Medium 中 4 项解决 + 2 项部分解决，每项都有配套测试或文档，无一项是纸面关闭；这验证了「find → fix → add guard」循环在本仓库有效。
3. **网络/文件系统防御纵深真实且有测试**——URL 校验每跳复验、Content-Range 三元组、reparse point 全组件拒绝、并行校验基元线程安全均为读码确认 + 测试钉住，非纸面配置。

## Recommended Priorities

1.（决策后执行）AUD-PERF-001 见证摊销：先以新增的 Verbose 跳过计数日志基准实测，再决定是否引入。
2.（专属设计轮）AUD-PERF-004 横幅位图备忘：Plausible 级补救，需先设计轮播位图的生命周期（复用/失效/陈旧释放）再动手。
3.（随下次触碰）AUD-PERF-005 二次解码调查；AUD-ARCH-005 若再动 Shell 按声明表收敛 Wire/Unwire。
4.（用户侧，可选）AUD-CI-005 残留：把 `linux-unit-tests` 加进 required status checks——作业现已首绿，加进去才能真正挡住平台回归，只有仓库管理员能改。
5.（维持接受）AUD-SEC-001/002 与已书面化的 SEC-004/SEC-006/ARCH-007：除非威胁模型变化。

## Audit Method and Limitations

实际执行：

- 仓库发现与规则加载：AGENTS.md、PROJECT_CONVENTIONS.md、CONTEXT.md、ADR-016..023、CI 三工作流、coverage/test 脚本、desktop-launcher 画像（逐文件）。
- 上午轮：四域并行审计通道（架构/安全/测试/性能由只读子代理逐行检索，证据带 file:line）；依赖/供应链、生命周期对账、报告撰写与关键发现复核由主审计执行。
- **晚间第二修复轮（用户指令「按优先级修复并逐阶段提交」）**：7 项复审新立案按杠杆序逐项提交（67229b5/521f4c1/3ea7baa/b04cb83/de2a1ee/608d888/d4ca700），每阶段聚焦测试后提交，收口全量套件本地实测（单元 1717 通过/0 失败/2 可见跳过 + Headless 178 通过/0 失败）；SEC-006 执行中经 git log -S 复核发现补救已被 5a38be9 刻意否决，转书面化接受。
- **同日核查轮 + CI 对账轮（用户指令「核查更改是否正确」「检查远端 CI 状态」）**：核查轮对第二修复轮 7 提交逐 diff 对账 + 守卫变异验证（删除退订行守卫即红，已恢复）+ 全量套件实测；CI 对账轮经 `gh` 读取 GitHub Actions 运行记录（34839782163 / 34821368356），定位三例失败根因（socket 路径长度 / ICU 宽容性 / 平台字符集）后同日修复（08c53f8），本地 Windows 全量单元 1720 通过 / 0 失败 / 2 可见跳过。
- **下午复审轮**：主审计逐项读码核实修复轮 9 提交与 6 项开放发现；四个只读子代理（架构/安全/测试/性能）以「已知台账排除清单」独立重扫全树（累计 ~230 次工具调用），返回 14 项候选；主审计对 7 项立案候选全部亲自复核（读源文件 + grep 守卫），5 项降为 advisory。工具证据：`.\test.ps1` 本地实测（单元 1708 通过/0 失败/2 可见跳过 + Headless 177 通过/0 失败，`dc1a6ef`）；`dotnet list package --vulnerable --include-transitive` 三项目零漏洞；依赖文件 git diff 零变更。
- 关键新发现亲自复核：AUD-TEST-005（`DownloadExecutorTests` 8 处单元素清单 + `GameDownloadServiceTests` 零引用 grep）、AUD-SEC-006（`IsPublicAddress` switch 现场）、AUD-PERF-007（逐文件 progress 现场 + 累加器作用域 grep）、AUD-MAINT-003（GamePaths.cs 全文 + 两处字面量现场）、AUD-TEST-006（`ViewFiles` 14 项现场）、AUD-TEST-007（守卫 grep 空 + 订阅行现场）、AUD-MAINT-004（注释现场）。
- 未执行：`coverage.ps1`、`verify.ps1` 全序列（Debug 全套件绿基础上视为充分；Release 配置与覆盖率棘轮状态以 CI 为准）；GitHub Actions 运行历史（`linux-unit-tests` job 首跑绿灯未知，以 CI 记录为准）。
- 局限：性能发现均为代码路径推理，无运行时测量（报告内无未经测量的倍数/毫秒声明）；子代理候选的低置信项（<80）未立案、列入 Advisory 并标注置信度；上轮「DownloadExecutorTests 9 用例钉住并行行为」的说法经复审修正为「钉住代码路径但未钉并发语义」（见 AUD-TEST-005）。CI 对账轮的三项修复（`08c53f8`）为平台差异，Windows 本地不可复现 Linux 行为——ICU 宽容性与套接字路径上限的 Linux 侧效果以推送后 CI 首绿为最终确认。
