# 仓库审计报告（当前状态）

> 本报告为 **full 全量重审**（用户指令）：六域审计通道全部执行，对上一基线以来的 16 个提交逐域重验，全部开放发现按现行源码逐项复核。上一报告（2026-09-13 full+delta）已归档至 `.repository-audit/history/2026-09-13-full-audit.md`。
>
> **同日修复轮**：报告定稿后按优先级将可执行发现全部落地（9 个提交，`521f4c1..95b9f8b`），修复后单元 1710 + Headless 177 全绿。开放发现收敛至 6 项 Low，全部为需要基准数据、产品输入或专属设计轮的决策项。详见 Resolved Findings。

## Audit Metadata

- 日期：2026-09-14
- Commit：审计基线 `67229b5`（`main`；最新已发布 tag `v1.1.0-beta.9`）；同日修复轮后 HEAD `95b9f8b`
- 模式：**full**（上一基线 2026-09-13 @ `ab156dd`/`1619d35`；本窗口 `1619d35..67229b5` 共 16 个提交）
- 范围：生产源码 242 个 `.cs`（≈33.6k 行）+ 29 个 `.axaml`、210 个测试文件（≈43k 行）、CI 三工作流、打包/安装器脚本、文档契约
- 项目画像：desktop-launcher（`.agents/skills/repository-audit/profiles/desktop-launcher.md` 按仓库证据调整）

## Executive Summary

仓库健康状况：**良好，且较上一审计实质性改善**。desktop-launcher 四个关键风险面（下载完整性、文件系统边界、进程启动、外部链接）防御纵深不变且全部有测试；上一轮全部 5 项 Medium 级结构/测试发现中 4 项已随 `1f09bc8`/`a6d3794`/`08c53f8`/`cf353cd`/`67229b5` 真实解决（不是纸面解决——本审计逐项读码 + 本地实测 1704 个单元测试全绿确认），其余 2 项（AUD-PERF-001、AUD-CI-001）部分解决后降档。**未发现 Critical / High / Medium 级问题。**

开放发现（2026-09-14 修复轮后）：

- Critical：0
- High：0
- Medium：0
- Low：6（全部为决策/待设计项：AUD-PERF-001、AUD-PERF-004、AUD-PERF-005 残留、AUD-SEC-001、AUD-SEC-002、AUD-ARCH-005）
- 本窗口解决：14 项（8 项随上轮修复落地：ARCH-002/003、TEST-002/003/004、PERF-002/003、MAINT-002；6 项随同日修复轮：ARCH-004/006、CI-001、PERF-006、SEC-003/005）；另 3 项结案为 accepted-risk（MAINT-002、SEC-004、ARCH-007）

**一处上轮审计证据更正（重要）**：上轮安全节声明「签名 Authorization 头绝不跟随重定向转发」——复核证实该头经 `RemoteRequestOptions.ConfigureRequest` 钩子在**每一重定向跳重发**（含跨主机），已立案为 AUD-SEC-003（Low）。这推翻了上轮对 DNS 重绑定残余风险影响边界的部分论证。

最重要的风险/行动（修复轮后剩余）：

1. **AUD-PERF-001**（Low）— 更新路径未变更文件的全读是文档化的损坏自愈设计，已并行化 ≤8 + 下载期哈希跨轮复用；剩余的见证摊销须先基准实测再决策，不得弱化自愈语义。
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

## Critical Issues

无。

## High Priority Findings

无。

## Medium Priority Findings

无（上一轮的 4 项 Medium：2 项已解决——AUD-ARCH-001、AUD-TEST-001，详见 Resolved Findings；AUD-PERF-001 并行化落地后降档 Low；AUD-CI-001 获得 Linux 执行点后降档 Low；AUD-ARCH-002 动效抽出后解决）。

## Low Priority Findings

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

### AUD-ARCH-007 — 代理指纹变化可在下载批次进行中 Dispose 其底层 handler【新立案】

- 类别：架构 / 生命周期所有权
- 严重度：Low｜置信度：70（机制结构性证实；未复现运行时行为）｜状态：**accepted-risk**（`043279e`，权衡已书面化）｜处置：Accept Risk
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

## Informational Findings

无（AUD-ARCH-004 已随 `95b9f8b` 归入 `Features/Diagnostics`；AUD-ARCH-006 已随 `700e674` 修正并结案，均转 Resolved Findings）。

## Advisory（不立案汇总）

- **组合根运行时服务定位**（已处理 `043279e`）：transport 工厂内 `ISettingsEditor` 改为构造时一次解析并闭包引用，代理模式解析不再重复服务定位。
- **DI 工厂内静态注册共享日志器**：`ServiceConfiguration.cs:42-48` 首次解析时 `LocalDiagnostics.RegisterSharedLogger(logger)`（`Volatile.Write`），构建多容器的测试会令首个容器的 logger 成为全局目标。生产路径 `App.axaml.cs:58` 急切解析一次，序确定；影响限于诊断误路由。建议测试 teardown 复位或显式一次性注册。（置信度 72，advisory 档）
- **Wire() 委托缝**：四个可空委托由 Shell 在构造后赋值（`ShellLifecycle.cs:426-428,:448`），已文档化为意图（`SettingsViewModel.cs:49`「Coordination delegates — set by parent after construction」）、空条件消费、测试钉住——有意设计，仅记录依赖图对构造签名不可见这一属性。
- **清单 `.tmp` 暂存名与 `.tmp` 结尾清单条目可互撞**（置信度 45，低于报告线）：敌意清单可声明 `x.tmp` 使其与 `x` 的暂存名重合；清单内容可控本就意味着内容可控，不构成独立完整性绕过。可加廉价断言（清单路径不得以暂存后缀结尾）。
- **等待助手私有变体 5→7 份**、`GameDownloadServiceTests.cs` 2068 行（最大测试文件）：维护成本项，仓库已有按域拆分先例（`UiStyleContractTests` 11 分部），随下次触碰收敛/拆分。
- **`AtomicJsonFileStore` 写失败路径无直接测试**（消费侧已间接覆盖：`LocalInstallationStateStoreTests.cs:174` 中途移动失败终态可读）——仅在改动该类时补一例。
- **卸载走 `GetSafePath` 而非 `GetSafeFilePath`**：根自身规范化条目不逃逸但会使卸载以异常中止——可用性边角，非完整性。

## Architecture

**结论：文档边界与实现一致，模态隔离裁定与 ADR-023 收敛经受住了本窗口两轮 UI 重构。**

- **跨功能边界零违规**（复核）：全量 `using` 扫描，越界仍仅 `ShellLifecycle`/`ShellPresentationFamily`/`ShellStartup` 三文件 = sanctioned 例外；`DebugViewModel` 仍经 `IGameOperationActivity` 窄抽象消费（组合根绑定 `:154-155`）。
- **模态注册声明式收敛保持**：19 个 `ModalKind` ↔ 19 条注册（`ShellLifecycle.cs:461-548` ↔ `ModalKind.cs:5-25`），`TryHandleEscape` 2 行委托（:608-612）；`ResourcePanelOverlay` 拆分（`e56d54b`）未破坏裁定——新覆盖层自带 `IsResourcePanelInteractive` 门（`ResourcePanelOverlay.axaml:12-14`）且仍是主叠层FirstChild、位于对话框层之下。
- **新抽取件干净**：`OperationSurfaceAnimator`（190 行）逐字搬移、ADR-016 注释保留、headless 动效套件未弱化；残留（两处未用 using、锚点退役回调跨文件）见 AUD-ARCH-002 解决记录。
- **组合根纪律**：全 Singleton、纯构造注入、释放顺序显式注释且经读码核实（客户端注册于 `HttpClientFactory` 之后 :115-135）；`Program.ServiceProvider` 仅用于会话末释放。两处轻微偏离见 advisory。
- 剩余 Low 发现：AUD-ARCH-005（接受中，若再动 Shell 按声明表收敛）；ARCH-004/006/007 已分别随 `95b9f8b`/`700e674`/`043279e` 结案。

## Security

**结论：无 Critical/High。四条新 Low 中三条是「可辩护设计的书面化」，一条（SEC-003）是真实的加固缺口。**

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

**结论：纪律持续兑现。上轮 4 项测试发现全部真实解决（本审计本地实测 1704 通过 / 0 失败 / 2 可见跳过佐证）；无未受保护的关键路径；残留为维护级。**

- **关键路径保护**（复核保持）：下载续传/CRC/限速（`GameDownloadServiceTests` 49 用例 + `FileDownloadServiceTests` 13 + `Crc64ServiceTests` 7）、安装状态损坏矩阵、设置兼容（legacy 字段 + DeepClone 棘轮）、卸载边界、URL 校验、更新流三áváginas 分支 + 确认接线（AUD-TEST-002 解决）。
- **确定性**：正面等待全部有截止/迭代上限（本通道全树检索无悬挂面）；程序集级串行 + 静态清单 + 用户数据隔离保持；`Assert.Skip*` 16 处，平台分支全部可见跳过（AUD-TEST-003 解决）；`ResourcePanelApiClient` 有 5 个专用用例（精确查询串、404→空配置、无客户端重试）。
- **CI 门禁**：覆盖率棘轮在 build.yml 强制（85.85%/92.70% 基线 + 余量打印）；golden 失败工件闭环（actual/diff PNG `if: always()` 上传，路径与写盘一致核实）；契约测试全部在可执行路径上、无本机-only 测试。
- **残留**：无（AUD-CI-001 已随 `08c53f8` 在 build.yml 增加 push/PR 触发的 ubuntu 单测 job 后收口）；advisory 见汇总节（等待助手变体、巨型测试文件）。

## Performance

**结论：上轮全部性能发现落地且质量好（修复非纸面）；新窗口 UI 工作零每帧 C# 开销；新 Low 两处中 PERF-006 已随 `608d888` 修复，PERF-005 文档半项已交付、二次解码调查开放。**

- **已核实干净**：资源面板脏检查事件驱动（3 项，`ResourcePanelViewModel.cs:37-40,:387-403`，无按键路径工作）；`ResourcePanelOverlay` 静态编译绑定 + `ItemsControl` over 3 项；`OperationSurfaceAnimator` 由 Avalonia `Animation` 渲染时钟驱动（无 DispatcherTimer、无每帧回调，`UpdateLayout()` 每转换两次且有运动关闭短路）；轮播单 5s 定时器；代理指纹每租约读注册表为文档化意图（3 次 `Registry.GetValue` 对网络操作可忽略）；启动重活仍在窗口打开后。
- **AUD-PERF-001 残留**与 **AUD-PERF-005 残留**见 Low 节；PERF-006 已修复（`608d888`）。

## Maintainability / Technical Debt

- 热点与结构互证：`ShellLifecycle` 25、`ServiceConfiguration` 24、`MainWindow.axaml.cs` 22 commits/180d——接线处变更多的正常形态；动效引擎已出窗（`cf353cd`），`ShellLifecycle` 的 Wire/Unwire 密度立案为 AUD-ARCH-005。
- 文档漂移三处已修复入库（2026-09-14：`700e674`/`521f4c1`）：AUD-ARCH-006（ZIndex 表述，结案）、AGENTS.md:75 首帧承诺（AUD-PERF-005 文档半项）、`RemoteHttpUrlValidator` DNS 缓存反向框定注释（AUD-SEC-001 证据修正）；advisory 的组合根服务定位已随 `043279e` 一次解析化。AUD-MAINT-001 静态缓存与 AUD-ARCH-004 VM 归属亦已分别随 `b04cb83`/`95b9f8b` 落地。
- `docs/architecture-review-2026-09-13.html` 已入库（`54c1871`）；官方协议对比文档按用户裁定 accepted-risk 结案（AUD-MAINT-002），行为不变量由 `OfficialHashServiceTests`/`AuthorizationHeaderFactoryTests`/`LauncherConstantsTests` 钉住。

## Decisions Required

修复轮后仅剩两项决策：

1. **AUD-PERF-001 残留**：更新路径是否在「自愈契约」前提下引入见证摊销（并行化已交付；需基准实测后再决策，未测量不得轻动）。
2. **AUD-PERF-005 残留**：首次刷新二次解码是否消除（需先确认跳过卫的解码目标可合法匹配；与同步/异步之争互不绑定）。

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

修复轮验证：每阶段跑聚焦测试；收口时全量套件（单元 1710 通过/0 失败/2 可见跳过 + Headless 177 通过/0 失败）与 Debug 构建零警告；`LineEndingPolicyContractTests` 绿。CI 编排类改动（build.yml/release.yml）经 YAML 解析校验，未实际触发 workflow 运行。

此前已解决（维持）：AUD-ARCH-001（`2fe3565` ModalRegistrar）、AUD-TEST-001（`f123429..f123429` RemoteHttpTransport 接缝）。

## Automated Guards Added

本窗口由修复顺带落地的守卫：

1. `SettingsViewModelTests` + `ShellLifecycleTests` 更新流用例（AUD-TEST-002 守卫）。
2. `Assert.Skip*` 范式（AUD-TEST-003 守卫，新平台门控照此办理）。
3. 校验跳过计数 Verbose 日志（AUD-PERF-001 建议的量化钩子，`DownloadExecutor.cs:341-344`）。
4. `ProxySettingsServiceTests` 16 用例（WinINet 归一化理论用例）与 `MainWindowHeadlessTests.RemoteContent` 5 用例随功能落地。

剩余建议守卫：无新增——SEC-003 头断言、CI-001 ubuntu job、SEC-005 attestation 均已随修复轮落地；MAINT-001/ARCH-004 的守卫即其重构本身与既有测试。

## Verified Strengths

最高杠杆的三项（防止不必要的重构/担忧）：

1. **模态隔离裁定经受住了本窗口两轮 UI 重构**——`ResourcePanelOverlay` 拆分与确认对话框收敛后，七个主叠层交互门与对话框层无闸口结构逐字保持；任何「给对话框层加闸口」的提议仍应拒绝。
2. **上轮审计发现被成批真实落地**——5 项 Medium 中 4 项解决 + 2 项部分解决，每项都有配套测试或文档，无一项是纸面关闭；这验证了「find → fix → add guard」循环在本仓库有效。
3. **网络/文件系统防御纵深真实且有测试**——URL 校验每跳复验、Content-Range 三元组、reparse point 全组件拒绝、并行校验基元线程安全均为读码确认 + 测试钉住，非纸面配置。

## Recommended Priorities

1.（决策后执行）AUD-PERF-001 见证摊销：先以新增的 Verbose 跳过计数日志基准实测，再决定是否引入。
2.（专属设计轮）AUD-PERF-004 横幅位图备忘：Plausible 级补救，需先设计轮播位图的生命周期（复用/失效/陈旧释放）再动手。
3.（随下次触碰）AUD-PERF-005 二次解码调查；AUD-ARCH-005 若再动 Shell 按声明表收敛 Wire/Unwire。
4.（维持接受）AUD-SEC-001/002 与已书面化的 SEC-004/ARCH-007：除非威胁模型变化。

## Audit Method and Limitations

实际执行：

- 仓库发现与规则加载：AGENTS.md、PROJECT_CONVENTIONS.md、CONTEXT.md、ADR-016..023、CI 三工作流、coverage/test 脚本、desktop-launcher 画像（逐文件）。
- 四域并行审计通道（架构/安全/测试/性能由只读子代理逐行检索，证据带 file:line）；依赖/供应链、生命周期对账、报告撰写与关键发现复核由主审计执行。
- 工具证据：`dotnet list package --vulnerable --include-transitive`（无漏洞）；**单元测试套件本地实测（Debug，`67229b5`）：1704 通过 / 0 失败 / 2 可见跳过**；`git diff`/`git log --name-only` 热点与依赖零变更核对；`wc -l` 规模核对；多处 grep（证书覆盖、ConnectCallback、UID 日志面、静态字段、Assert.Skip 计数）。
- 关键新发现亲自复核：AUD-SEC-003（重定向循环 + ConfigureRequest 钩子逐行）、AUD-SEC-004（`UseDefaultCredentials` 现场）、AUD-ARCH-007（handler 处置与租约构造现场）、AUD-ARCH-006（AGENTS.md vs `LauncherConstants`）、AUD-PERF-005（构造解码与 AGENTS.md:75）、AUD-MAINT-001（静态字段 :614-618）、AUD-ARCH-003（所有权注释 :115-121）。
- 未执行：Headless 套件、`coverage.ps1`、`verify.ps1` 全序列（本审计在单测全绿基础上视为充分；Release 配置与渲染套件状态以 CI 为准）；GitHub Actions 运行历史（linux-tests.yml 是否曾绿跑未知）。
- 局限：性能发现均为代码路径推理，无运行时测量（报告内无未经测量的倍数/毫秒声明）；AUD-ARCH-007 与 AUD-SEC-003 的运行时语义分别标注 Needs External Verification / 建议配套实验；子代理行号在关键项经主审计抽查核实，未抽查项置信度已相应降档。
