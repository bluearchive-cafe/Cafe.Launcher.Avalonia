# 仓库审计报告（全维度复审）

- 审计日期：2026-09-04
- HEAD：4ed11c3
- 审计方式：repository-audit 流程（仓库发现 → 规则加载 → 六路并行专项审计：架构 / 可维护性 / 安全 / 依赖 / 测试 / 性能 → 高影响项逐条人工核验源码）
- 范围：全仓库六维；测试维度为 2026-09-04 专项审计整改后的复审
- 历史报告：`.repository-audit/`（2026-08-25 diff 审计、2026-09-02 全维审计、2026-09-04 测试专项审计）

## 与上次审计的衔接

上次测试专项审计（2026-09-04）的 P0–P3 建议已全部整改。本次复审**逐项核实整改声称属实**（有界轮询、共享 TestDoubles、巨石文件拆分、ShellRefreshCoordinator/RetryPolicy/ResourcePanel 补测、golden 流程等），无一虚报，详见第 6 节。两个已上报待产品决策的静默失败项（向导完成持久化、重置确认）维持原状；本次又发现两个同类新位置（见 7.3 / 7.4）。

## 摘要

- 分析规模：src 201 个 .cs（约 29.8k 行）+ 25 个 .axaml；tests 157 个 .cs（约 37.2k 行，1082 个 Fact/Theory）；scripts 10 个、installer、2 个 CI workflow。
- **无 Critical 问题。** 高置信度发现（置信度 ≥80）28 项，其中影响为 High 的 4 项；另有约 19 项 advisory（置信度 60–79）。
- 总体评价：**工程质量显著高于同类桌面开源项目**。分层纪律近乎完美（共享层零反向依赖、无 Service Locator、disposal 有守卫有测试）；安全五大高危类别（路径穿越/进程注入/反序列化/TLS/遥测）均未发现可利用问题；性能基础设施（进度节流、HttpClient 池化、异步日志、原子写盘）到位；测试体系经整改后 flake 面基本清零。
- 4 项 High 影响发现集中在两个主题：**CI 供应链闭环未完成**（lock 文件从未被强制执行 + Actions 浮动 tag 持有发版权限）和**下载验证管线的重复 IO**（每文件双重 CRC64 + 全清单重哈希 + 逐字节查表实现）。
- 一项需要架构裁决的结构性债务：Shell 作为「根 Feature」系统性反向持有全部其他 Feature 的具体 ViewModel，与仓库自身的 Feature 边界规则冲突（见第 2 节）。

---

## 1. Critical Issues

无。

---

## 2. Architecture

### 2.1 Shell 反向持有全部其他 Feature 的具体 ViewModel，绕过既定窄接口模式（置信度 85，影响 High）

`Features/Shell/ShellPresentationFamily.cs:15-27` 用单条 record 聚合 GameOperations/Settings/ResourcePanel/Diagnostics/SetupWizard 五个切片的具体 VM；`ShellLifecycle.cs` 直接调用 `operations.ResumePersistedDownloadAsync(...)`（:261）、`operations.StopDownload(...)`（:277）、跨 Feature 静态调用 `SettingsAppearanceViewModel.ApplyTheme(...)`（:219/:380/:400/:640）。AGENTS.md 明文规定 "Features must not reference each other's concrete types"，组合根已有 `IGameOperationActivity` 正确样板，但 Shell 完全不经抽象；GameOperations 又反向依赖 ShellViewModel/DialogsViewModel（`GameOperationsViewModel.cs:24-25`），形成双向知识。856 行的 ShellLifecycle 因此成为事实上的上帝协调者。

**建议**：二选一并写进 AGENTS.md 消除歧义：(a) 正式升格 Shell 为 Feature 之上的「壳层」，把 ShellLifecycle/ShellPresentationFamily/ShellStartup 迁出 `Features/`，规则改为「Feature 之间不得横向引用，壳层向下聚合」；(b) 维持 Shell 为 Feature，按 `IGameOperationActivity` 模式为其实际操作面提取窄接口。至少先消除 `ApplyTheme` 这类跨 Feature 静态副作用调用。

### 2.2 横切 modal 契约寄居在 Features/Shell，被 5 个 Feature 反向引用（置信度 80，影响 Medium）

`IModalContentViewModel`/`ModalHostViewModel`/`ModalKind` 位于 `Features/Shell/`，而 Settings、SetupWizard、ResourcePanel、Diagnostics(×2)、DesignGalleryViewModel 全部 `using Cafe.Launcher.Avalonia.Features.Shell` 来实现该接口（如 `Features/Settings/SettingsViewModel.cs:11,22`）。与 2.1 同源：Shell 成为依赖汇聚点。

**建议**：将 `IModalContentViewModel`（连同 `ModalKind`）迁至共享层；`ModalHostViewModel` 本就是窗口级 ViewModel，应迁至根 `ViewModels/`。纯移动 + using 调整，有合约测试护航。

### 2.3 ViewModel 直接依赖 Avalonia UI 运行时设施（置信度 85，影响 Low）

`ViewModels/RemoteContentViewModel.cs:27,326-331` 持有并驱动 `DispatcherTimer`；`BackgroundViewModel.cs:482,512`、`SettingsAppearanceViewModel.cs:269-275`、`DesignGalleryViewModel.cs:47` 直接用 `Dispatcher.UIThread`/`Application.Current`。未发现 VM 操作具体控件，层级分离主体健康；DispatcherTimer 是最明确的越界项。

**建议**：轮播定时器换成注入的定时器抽象（`PeriodicTimer` + 单点 UI 编组）；资源读取以委托注入。

### 2.4 组合根与其余边界：核实通过（正面）

全部 AddSingleton 符合单窗口规则；disposal 顺序有注释且经构造顺序复核成立；`GetRequiredService` 全部集中在组合时点，无 Service Locator 泄漏；`Services/`、`Helpers/`、`Models/`、`Constants/`、`Converters/` 对 `Features/` 的引用为**零命中**；`ViewModels/` 目录无 helper 类型混入；事件接线由 `ShellLifecycle.Wire/Unwire` 逐对镜像。注册形状有 `ServiceConfigurationTests` 守护。

Advisory（置信度 60–79）：公共可空委托属性作为第二注入通道（`SettingsAppearanceViewModel.cs:48` + `ShellLifecycle.cs:413-415`，与 §5.3「仅构造函数注入」相悖，75）；MainWindow 构造函数依赖具体 Service 类而非已存在的接口（`MainWindow.axaml.cs:45-54`，75）；`ToastSeverity` 枚举放在 Services/ 而同类在 Models/（75）；`IShellRuntime`↔`ShellLifecycle`、`IProcessLauncher`↔`DefaultProcessLauncher` 两处命名不配对（65）；`LauncherReleaseResponse.DisplaySize` 让 Model 承担展示格式化（65）。

---

## 3. Security

总体结论：**安全姿态显著高于同类开源启动器平均水平**，五大高危类别均无可利用问题（见 3.5 正面清单）。需要处理的真实事项集中在 CI 供应链。

### 3.1 CI Actions 使用浮动 major tag，第三方发布 Action 持有 write 权限与 PAT（置信度 85，影响 Medium）

`build.yml:30,35,49` 与 `release.yml:138,230,262,268,274,319,335,339` 全部以 `@v6/@v7/@v8` 浮动 tag 引用 action；`release.yml` 的 release job 持 `contents: write` 并将 `${{ secrets.RELEASE_REPOSITORY_TOKEN }}` 直接传入第三方 `softprops/action-gh-release@v3`。tag 可被移动，第三方 action 一旦被投毒可窃取能向公开发布渠道发版的 PAT——这是本仓库现实供应链风险最高的一环。

**建议**：全部 action 固定到完整 commit SHA（配合已有 Dependabot 自动更新）；评估改用 fine-grained PAT（仅限分发仓库 contents:write）+ GitHub Environment 保护。

### 3.2 `${{ github.ref_name }}` 直接插值进 run 脚本（置信度 80，影响 Low）

`release.yml:69`（pwsh）、`:113`、`:239`、`:282-285`（bash）。触发条件是 push tag，攻击者需仓库写权限，不构成外部攻击面，属防御纵深。**建议**：改 `env:` 传参，成本极低。

### 3.3 信任模型局限（如实记录，非缺陷；均经代码核实）

- **API 签名 salt 硬编码**（`Constants/ApiConfig.cs:12`，置信度 90）：核实为 Yostar 官方启动器线签协议的全客户端共享常量，Authorization 头不含任何用户身份——公开不泄露任何非公开秘密。维持现状，补注释「公开协议常量，勿复用于用户场景」。
- **下载完整性依赖 CRC64**（非加密哈希，hash 与文件同源于上游 API 信任域；置信度 85）：上游 Electron 协议约束，双阶段校验 + Content-Range 校验实现严谨。建议在文档声明信任模型。
- **允许 http:// 明文 CDN 下载**（`RemoteHttpUrlValidator.cs:47`，置信度 75）：内容本身公开、完整性由 CRC64 兜底，风险低；建议遇 http 域名时记 Warn。顺带：`BuildDownloadUrl` 会丢弃 domain 中的端口与路径前缀（`FileDownloadService.cs:206`），属功能瑕疵。
- **SSRF 校验器盲区**（置信度 80）：IPv4 黑名单缺 `100.64.0.0/10` 与 TEST-NET 段；先校验后请求存在 DNS-rebinding TOCTOU 窗口。桌面应用威胁模型下记录已知局限即可。

### 3.4 CI 其他核实：无 `pull_request_target`；workflow 级 `contents: read` 最小权限，仅 release job 提升；AppImage 工具 SHA256 双重固定；安装包发布经 `gh release verify-asset` 校验。

### 3.5 正面清单（逐项读码核实）

- **Zip Slip 防护完整**：全部 manifest 相对路径经 `GamePathValidator.GetSafePath`（归一化 `..` + 根前缀校验 + 已存在组件的 Reparse Point 检查），12 个调用点全覆盖且有专门测试；本应用不解压远端压缩包。
- **进程启动无注入面**：全部 `Process.Start` 用 `ArgumentList` 零拼接；外链有 scheme 白名单封堵 `file://`。
- **重定向安全**：所有 handler `AllowAutoRedirect=false`；`RemoteHttpRequestService` 显式阻断 HTTPS→HTTP 降级、每跳重过 URL 校验；SSRF 校验覆盖所有「URL 来自远端数据」的请求，仅有的两处绕过均为编译期常量 HTTPS 端点。
- **反序列化安全**：`EnableUnsafeBinaryFormatterSerialization=false`；无 TypeNameHandling；统一严格 `JsonDefaults`。
- **零遥测**：全仓库仅 GET 请求；日志无 Authorization/salt/cookie/token；settings.json 不存明文凭据。
- 安装器提权路径严谨（NSIS 升级桥三重验证、所有权标记阻断卸载器挪用）。

---

## 4. Performance

### 4.1 每个下载文件被完整 CRC64 校验两次，验证轮次重复哈希整个清单（置信度 90，影响 High）

下载完成后 `FileDownloadService.cs:137` 对临时文件哈希一次；随后 `DownloadExecutor.cs:250` 对**同一个**临时文件再哈希一次（两条路径间无任何写入，第二次必然重复）；且 `:237 foreach (var file in manifestFiles)` 遍历完整清单而非仅本次下载的文件，未变更的存量文件也被整读哈希；整个流程位于 `MaxInstallVerificationRetry = 3` 重试循环内（`DownloadSession.cs:254`），失败时每轮重来。20GB 更新在下载完成后额外产生约 20GB+ 的重复读盘哈希，是本仓库最大的可感知性能成本。

**建议**：`DownloadAsync` 返回已计算的 CRC64；安装验证集合收窄为 `downloadedFiles ∪ 上轮失败集`，只对重下文件复验。

### 4.2 CRC64 逐字节查表实现，吞吐量低 5–10 倍（置信度 80，影响 Medium）

`Services/Crc64Service.cs:48-53` 逐字节查表（典型 300–800MB/s），slicing-by-8 可达数 GB/s；缓冲区本身 1MB 复用无问题，纯算法吞吐。与 4.1 叠加直接决定修复/验证等待时间。**建议**：实现 slicing-by-8（纯查表、无平台依赖），易做基准固化。

### 4.3 内置背景图在 UI 线程同步全尺寸解码：DI 构造期 + 每次刷新（置信度 90，影响 Medium）

`BackgroundViewModel.cs:121` 构造函数内同步解码 2560×1388 PNG（约 14MB 解码后），该单例在 `App.axaml.cs:69` 首帧前解析；且 `UpdateBackgroundImageAsync` 的 Bundled 分支未像 Remote/Custom 分支那样包 `Task.Run`（`:209-214` 对照 `:162`），默认壁纸下每次刷新（启动、设置保存后、每个游戏操作完成后）都在 UI 线程重解码整图（约 30–90ms 停顿），无「来源未变则跳过」守卫。**建议**：Bundled 分支同样走 `Task.Run`；按来源+解码目标缓存复用解码结果。

### 4.4 横幅图在 UI 线程全尺寸解码，每次刷新全部重建（置信度 90，影响 Medium）

`RemoteContentViewModel.cs:553` 在 `Dispatcher.UIThread.InvokeAsync` 内 `new Bitmap(stream)`，无 `DecodeToWidth`；`Apply()` 每次刷新先 Dispose 全部横幅再全部重建（`:135/:171`），字节虽有 24h 磁盘缓存，解码总是全尺寸、总在 UI 线程。旧图 Dispose 正确，无内存累积。**建议**：解码移出 UI 线程并按控件宽度 `DecodeToWidth`；URL 集合未变时跳过整轮重建。

### 4.5 其余核实（正面为主）

下载进度 100ms 窗口节流 + UI 版本号去抖到位；HttpClient 纪律优秀（共享 handler、PooledConnectionLifetime、代理按指纹缓存）；日志管线真异步（bufferSize 10000）+ UnobservedTaskException 兜底；壁纸解码按 `DecodeToWidth` + resize 防抖、位图释放顺序讲究；事件订阅无泄漏；settings.json 仅显式保存与关窗时写盘（原子替换）；启动路径除 4.3 外干净（重活全部推迟到 `Opened` 之后）。

Advisory：每 256KB chunk 两次文件系统元数据调用（`DownloadExecutor.cs:157-159/:335-340`，可改 Interlocked 计数，85）；每次刷新重复执行「缓存整文件哈希校验 + 整图重解码 + 交叉淡化重放 + 壁纸取色」（85）；`SettingsAppearanceViewModel.cs:260` async 上下文用同步 `LogSync`（85，违反 §3.2）；日志查看器过滤重建全量集合 + 续行字符串 O(n²)（70）。

---

## 5. Dependencies

### 5.1 Lock 文件从未被强制执行——「CI 自动进入 locked mode」的声明是错的（置信度 92，影响 High）

`Directory.Build.props` 注释声称 "CI 中 ContinuousIntegrationBuild=true 时 restore 自动进入 locked mode"。全仓库搜索确认：**没有任何 workflow、脚本或 props 设置该属性**，CI 也从未传 `--locked-mode`（本次审计 rg 核验零命中）。审计代理另做了 SDK 10.0.400 沙盒实验：对过期 lock 文件执行 `dotnet restore -p:ContinuousIntegrationBuild=true` 会**成功并静默改写 lock**（该属性与 NuGet locked mode 无接线，dotnet/sdk#23795 至今 open）；对照实验 `--locked-mode` 则正确报 NU1004。即 lock 文件目前只是「记录」而非「约束」，无 CVE 的新传递版本可在两次人工更新之间无声进入构建。

### 5.2 三个 lock 文件均缺 RID 段——三平台发布闭包不在锁定范围内（置信度 88，影响 High）

三个 `packages.lock.json` 顶层只有 `net10.0` 段（本次审计亲自核验）；release CI 以 `win-x64/osx-arm64/linux-x64` 发布（publish 隐式 restore），RID 特定原生依赖闭包（SkiaSharp/ANGLE/HarfBuzz natives）从未回提交仓库。发行产物的实际依赖图与提交的 lock 不一致；且一旦按 5.1 启用 locked mode，三个 RID 的发布会立即 NU1004 失败。**建议与 5.1 一次 PR 完成**：本地对三个 RID 逐一 restore 生成含全部 RID 图的 lock 并提交 → CI 显式设 `ContinuousIntegrationBuild=true` + restore/publish 加 `--locked-mode` → 修正 `Directory.Build.props` 错误注释。

### 5.3 Dependabot 完全未覆盖 NuGet 生态（置信度 96，影响 Medium）

`.github/dependabot.yml` 仅配置 `github-actions`。`Directory.Packages.props` 的 17 个直接依赖没有任何自动更新 PR；`NuGetAudit(all)` 只对已知漏洞报警，版本落后本身静默。**建议**：增加 `package-ecosystem: nuget`（weekly + minor/patch 分组合并）。

### 5.4 Shirasagi0012.MaterialColorUtilities：bus factor 1 的高维护风险小众包（置信度 85，影响 Medium）

nuget.org 实测：总下载 458、全历史 3 个版本（约 6 个月历史）、作者自述 "not yet ready for production use"。M3 动态主题核心（HCT/方案生成）由它承载；主流替代品 `MaterialColorUtilities`（albi005）3 年未更新且缺角色，**选型经 `docs/design/color-utilities-spike.md` 量化对照论证、更优**。缓解因素：API 接触面仅 3 个文件、行为被 spike fixture 单测锁定、Apache-2.0 可 fork 内联。**建议**：维持，列入年度重审，保持调用面收窄、备好 fork 预案。

### 5.5 版本健康度：整体贴近最新（置信度 93，影响 Low）

逐包 nuget.org API 在线核实：Avalonia 12.1.1→12.1.2、MEDI 10.0.10→10.0.11、Test.Sdk 18.8.1→18.9.0 三项小步落后；xunit.v3 4.0.0 已发布但被 `Avalonia.Headless.XUnit 12.1.1 → xunit.v3.extensibility.core [3.2.2]` 钉住，属组合升级项（置信度 80）。无弃用/改名包。

### 5.6 许可证合规：闭包覆盖 38/38 完整（程序化 diff 核验），但两行许可证元数据未落实（置信度 85）

`THIRD-PARTY-NOTICES.md` 中 `Avalonia.Angle.Windows.Natives` 显示已弃用的 licenseUrl 占位、`AvaloniaUI.DiagnosticsSupport` 为 "see package"。**建议**：生成脚本加 per-package 许可证覆盖表。另核实：DiagnosticsSupport 经 `#if DEBUG` + `PrivateAssets=All` 双重 gate，Release 产物确定不含 DevTools——教科书式隔离，维持现状。

Advisory：原型项目 `prototypes/FluentMotionLab` 绕过 CPM 双写版本（78）；CI `cache-dependency-path` 未包含 `Directory.Packages.props`/lock 文件（78，仅影响缓存命中）。

---

## 6. Testing（整改复审）

**整改核实结论：P0–P3 全部属实，无一虚报。** 抽查证据：

| 阶段 | 结论 | 关键证据 |
|---|---|---|
| P0 有界轮询/时间竞态/Dispose/串行化护栏/隔离 Collection | 通过 | 向导推进循环现带 5s/10s deadline + `Assert.Fail`（`MainWindowHeadlessTests.SetupWizard.cs:119-131/149-163/311-325`）；`resumeTask.WaitAsync(5s)`；`ControlledDelay` 门控；本地化隔离 Collection 实为 **24 个类**（超声称的 21） |
| P1 共享 TestDoubles/巨石拆分 | 通过 | `tests/TestDoubles/` 双项目链接；三巨石现为「存根 + 分卷」结构，全 tests 最大文件 1929 行，无 >2000 行 |
| P2 补 68 用例 | 通过 | `ShellRefreshCoordinatorTests` 8 + `RetryPolicyTests` 6（断言密度高，精确到事件序列）；ResourcePanel 10+8；`GameUninstallServiceTests` 含文件锁部分失败与重复卸载幂等 |
| P3 卫生项 | 通过 | `GoldenScreenshot.cs:126-190` 失败输出 actual+diff PNG + 非 Windows Skip；`test.ps1 -UpdateGolden`；tempDir 统一 Dispose |

规模对比：Fact/Theory 1015 → **1082**（+67）；tests 总行数 34.1k → **37.2k**（P2 新增测试减去 P1 净删的基础设施，账目与整改记录自洽）。flake 面基本清零：`await Task.Delay(N); Assert` 直接模式 0 命中、`DateTime.Now` 0 命中、`Thread.Sleep` 仅剩合法用法。46 个 service/coordinator 类仅剩 4 个无测试引用，且为平台薄封装或已间接覆盖。

### 新发现（均 advisory）

- **T1. 单元测试仍有 2 处与上次 P0 同类的无预算裸自旋循环**（置信度 70）：`LocalizationTerminologyTests.cs:166-170`、`SetupWizardViewModelTests.cs:188-189` 的 `while (!vm.IsLastStep) { vm.NextCommand.Execute(null); }` 无 await、无 deadline，靠与后台异步校验竞态获胜退出——任何门控收紧都会让单元测试进程永久挂死。**建议**：套用仓库已有有界模式；因与上次 P0 同类、修复成本极低，纳入本轮 P0 路线图。
- T2. `FindProjectRoot` 在契约测试中仍有 3 份私有拷贝（`DialogActionButtonContractTests.cs:134`、`InstallerContractTests.cs:614`、`InstallDiskSpaceUiContractTests.cs:30`，75）——上次整改漏计，随触碰时改调共享版。
- T3. 真实限速 + Stopwatch 下限断言是上次 §2.3 唯一漏网项（`GameDownloadServiceTests.cs:982-995`，60）——flake 风险低，代价是每轮 +800ms 真实等待。

---

## 7. Maintainability

### 7.1 裸本地化 key 直传 T()/F()，共 10 处（置信度 95）

AGENTS.md 明令禁止；rg 全量枚举核实 10 处且**全部 key 已有对应常量**，改名零风险：`ShellViewModel.cs:205,210`、`DownloadSession.cs:232`、`GameLaunchService.cs:64`、`GameOperationsViewModel.cs:346,402,411,417,444`、`ManifestValidationService.cs:108`（本次审计抽验 4 处属实）。**影响**：重命名 key 时合约测试扫不到这些调用点，静默回退——正是该规则要防的事故。**建议**：机械替换一次提交完成；在 `Test-LocalizationContract.ps1` 加「C# 源码禁止裸 key 直传」守卫。

### 7.2 约 24 处 `Debug.WriteLine` 绕过 LocalDiagnostics（置信度 90）

规范 §3.2 要求 LocalDiagnostics 为唯一日志入口。本次审计核验计数：App.axaml.cs ×9、LogViewerDialogViewModel ×3、ImageCacheService ×2、CrossProcessLaunchSignal ×2、SettingsViewModel ×2、RemoteContentViewModel/WindowsRegistrySystemProxySettingsProvider/SystemTrayService/RemoteManifestService/LauncherSettingsService/CrossProcessPollingListener 各 ×1（豁免 LocalDiagnostics/UnifiedLogger 自身）。其中 `LogViewerDialogViewModel` 已注入 `diagnostics` 字段却仍用 Debug.WriteLine，属明确违规；`RemoteContentViewModel` 根本未注入。**建议**：优先改 LogViewer/RemoteContent/App；bootstrap 期几处可加豁免注释。

### 7.3 日志查看器把加载失败伪装成「空日志」（置信度 80，影响 Medium）

`LogViewerDialogViewModel.cs:108-113/:184-192/:193-201` 三处 `catch { Debug.WriteLine; allEntries = []; }`——日志读取失败（正是用户最需要日志的时刻）时用户看到空列表，无任何 UI 反馈，连本地日志都不写。与已知两项静默失败同类但位置不同、且更差一档。**建议**：加 `HasLoadError` 提示 + `diagnostics.ErrorAsync`。

### 7.4 关窗持久化失败仅 Debug 输出（置信度 85，影响 Medium）

`App.axaml.cs:268-270`（本次审计读码核验）：`CompleteShutdownAsync` 中保存窗口位置/尺寸失败只写 `Debug.WriteLine`——Release 下用户与日志文件均不可见，发生在每次正常退出路径；同一方法里 `serviceProvider` 此刻尚未 Dispose，`LocalDiagnostics` 完全可用。用户报告「窗口位置记不住」时将无任何诊断线索。**建议**：改 `await ...ErrorAsync(...)`。

### 7.5 巨型方法/类（置信度 80–90）

- `DownloadSession.RunAsync` **283 行**（`DownloadSession.cs:105-387`，置信度 90）：九个阶段内联 + 6 个连续 catch 分类块。建议按已有阶段切三段，纯提取。
- `RemoteContentViewModel` **696 行多职责**（轮播状态机 + 137 行 Apply 映射 + 图片预加载 + URL 消毒 + 静态解析，置信度 85）：建议提取 `RemoteContentMapper`（纯静态）与 `CarouselController`。
- `ShellLifecycle` 35 字段编排枢纽：新增一个对话框需同步改 4 处（DialogsViewModel、ModalKind、`TryHandleEscape`、`OnDialogsPropertyChanged`），漏改表现为 Escape 静默失效（置信度 80）。建议引入 `IModalPresenter` 查表，4 处降 1 处。

### 7.6 XML doc comment 覆盖（置信度 85）

24 个样本抽查 54% 有文档；无文档重灾区恰是最核心的持久化服务（`LocalInstallationStateStore` 类及 3 个公共方法全无、`LauncherSettingsService`、`ILauncherCoreService`）。**建议**：按「修改即补齐」优先补这三处，不要求一次性补全存量。

Advisory：`DialogsViewModel` 三个确认命令无 catch，新增订阅者异常将落入无人观察的 ExecutionTask（70）；原子写文件 3 处手写旁路可提取共享骨架（75）；卸载确认文案 `-2` 魔法数字（75）；`(PatchUrlGroup, ProxyMode)` 参数团贯穿下载链路 ≥8 层（70）；两个 async 方法缺 `Async` 后缀（`ResourcePanelViewModel.cs:154`、`DebugViewModel.cs:196`，85）；`IGameShortcutService` 与实现同文件（65）；`DebugViewModel:206` 直调 UnifiedLogger 属有意破例但无豁免注释（65）；`GameDownloadServiceTests.cs` 1929 行成新的最大测试文件（65）。

---

## 8. Technical Debt：整改路线图

**P0 — 供应链闭环与零成本修复（约 1 天）**
1. lock 补三平台 RID 段 + CI 显式 `ContinuousIntegrationBuild=true` 与 `--locked-mode` + 修正 `Directory.Build.props` 注释（5.1/5.2）
2. Actions 固定 commit SHA；`ref_name` 改 env 传参（3.1/3.2）
3. Dependabot 加 nuget 生态（5.3）
4. 裸 key 10 处机械替换 + 合约守卫（7.1）
5. 7.3/7.4 两处静默失败补日志与反馈；单元测试 2 处裸自旋循环加预算（T1）
6. 顺手：删除死代码 `Program.SignalShowInstance`（置信度 95）

**P1 — 用户可感知的性能（1–2 天）**

7. 下载验证管线去重：CRC64 结果复用 + 验证集合收窄 + slicing-by-8（4.1/4.2）
8. Bundled 背景与横幅解码移出 UI 线程 + 来源未变跳过（4.3/4.4）

**P2 — 结构性可维护性（2–3 天，部分需架构裁决）**

9. 裁决 Shell 架构地位：升格壳层或提取窄接口；modal 契约迁共享层（2.1/2.2）
10. `RemoteContentViewModel` 拆分、`RunAsync` 提取、`IModalPresenter` 查表（7.5）
11. `Debug.WriteLine` 收敛进 LocalDiagnostics（7.2）；补核心服务 XML doc（7.6）

**P3 — 卫生项（随手改）**

12. 委托属性注入通道收敛、ToastSeverity 迁移、命名配对/Async 后缀、原子写骨架提取、FindProjectRoot 收敛、许可证两行补齐、CI 缓存键、原型项目 CPM 显式化、依赖 patch 三连升级

---

## 9. 正面观察（保持现状）

- **分层纪律近乎完美**：共享层对 Features 零反向依赖、无 Service Locator、disposal 幂等有守卫有测试；`IGameOperationActivity` 是窄接口绑定的优秀样板。
- **安全五项高危类别全绿**（路径穿越/进程注入/反序列化/TLS/遥测），防护带测试与纵深，安装器提权路径严谨。
- **性能基础设施到位**：进度节流、HttpClient 池化、异步日志、事件对称退订、原子写盘、启动路径干净。
- **测试体系经整改后属上乘**：flake 面基本清零、静态状态卫生成为可执行护栏契约、P2 新增测试断言密度高、覆盖率棘轮（行 84.30%/分支 88.99%）+ 双项目合并口径健康。
- **供应链配置方向正确**：CPM + lock + NuGetAudit(all) + 零警告四件套齐备（唯一缺口是 locked mode 未接线）；DevTools 隔离、THIRD-PARTY-NOTICES 38/38 覆盖、小众包选型有 spike 论证。
- 零 TODO/FIXME/HACK 存量；异常→错误通道纪律（OCE `when` 过滤、HResult 细分）与注释质量（面向下一个维护者解释 why）是库内亮点。

---

*审计方法说明：六路专项审计并行产出，全部高影响发现由主审计逐条读源码二次核验（locked-mode 零命中、裸 key 抽验、Debug.WriteLine 全量计数、双重 CRC64、测试裸自旋循环、ShellPresentationFamily、lock 文件 RID 段、关窗静默失败等均为直接读码确认）；依赖版本经 nuget.org API 在线核实；置信度 <60 的观察已丢弃，60–79 的列为 advisory。发现由并行代理的 SDK 沙盒实验（locked mode 行为）与程序化 diff（许可证闭包）佐证。*
