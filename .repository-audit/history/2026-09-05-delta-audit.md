# 2026-09-05 Delta 审计报告（整改落地复审）

- 审计日期：2026-09-05
- 范围：`4ed11c3..259e217`（19 提交，144 文件，+8614/−701）——即 2026-09-04 全维审计 P0–P3 整改的全部落地代码
- 模式：delta（基线 4ed11c3，见 `CODEBASE_AUDIT.md` 主体与 `.repository-audit/history/2026-09-04-testing-audit.md`）
- 方式：三路并行专项（UI 线程化 / CI 供应链 / 可维护性与测试）+ 主审直查下载验证管线与 CRC64 slicing-by-8 算法；高影响发现逐条读源码二次核验
- 工具证据：`dotnet test`（在临时修复 AUD-BLD-001 后）136/136 通过，覆盖 Crc64ServiceTests、DownloadExecutorTests、FileDownloadServiceTests、GameDownloadServiceTests、LocalizationTerminologyTests、SetupWizardViewModelTests；测试后工作区已精确还原 HEAD

---

## AUD-BLD-001 — Directory.Build.props XML 注释含 `--`，HEAD 所有 restore/build 失败（Critical，置信度 100，已复现）

- 类别：构建/发布完整性
- 证据：`Directory.Build.props:9` 注释「提交前应先还原该文件（git checkout --）」——XML 规范禁止注释内出现 `--`。实机复现：任何项目 `dotnet restore`/`build`/`test` 均报 `MSB4024: 未能加载导入的项目文件 … An XML comment cannot contain '--'`。引入提交为 1613bb8（纯文档提交，其后无构建验证）；`build.yml` 触发条件含 push main，远端 CI 将对此红灯。
- 影响：main 分支当前不可构建——开发、测试、发布全部被阻断；「纯文档提交无需跑构建」的流程假设被证伪（.props/.csproj/.axaml 是 XML，注释写坏即断构建）。
- 状态：open；处置：Fix（一行）
- 建议：把 `（git checkout --）` 改写为不含 `--` 的表述（推荐 `git restore <文件>`，语义等价且无连字符风险）。**建议验证：Verified**（改动仅为注释文本；`git restore` 为标准命令）。
- 建议守卫：触碰 `.props`/`.csproj`/`.axaml`/`.resx`（任何 XML）的提交前，跑一次 `build.ps1`（restore 即可暴露本类故障）；或 pre-commit 钩子对仓库内 XML 工程文件做 `[xml]` 良构校验。CI push 触发是既有兜底，但只能事后拦截。

## 整改核验结论：2026-09-04 报告 P0–P3 声称全部属实

| 基线发现 | 结论 | 关键证据 |
|---|---|---|
| 5.1 locked mode 未接线 | **已解决** | 经 MSBuild env→全局属性机制接线：`build.yml:30-31`、`release.yml:32-33,194-195`；追真实命令路径证实 test/coverage/publish restore 全覆盖；`Directory.Build.props:10-13` 注释已修正（dotnet/sdk#23795 属实） |
| 5.2 lock 缺 RID 段 | **已解决（按修正后方案）** | 三 lock 实测唯一顶层段 `net10.0`；RID 步骤显式豁免 `RestoreLockedMode`（`build.yml:65-71`、`release.yml:69-73`），豁免只关 locked mode，`RestorePackagesWithLockFile` 仍全局生效，lock 未失守 |
| 3.1 Actions 浮动 tag | **已解决** | 17 处 uses 全部 40 位 SHA；5 个唯一 SHA 经 `git ls-remote` 上游逐一实证与注释版本对应（含 softprops v3 annotated tag 解引用情形） |
| 3.2 ref_name 插值 | **已解决** | 5 处全部改 env 传参（`release.yml:75,123,147,258,305`）；残留 `${{ github.server_url }}/${{ github.repository }}`（:308,310）为可信常量，无可利用面 |
| 5.3 Dependabot 缺 nuget | **已解决** | `.github/dependabot.yml:7-16` weekly + minor/patch 分组；SHA 固定 + `# vX` 注释写法可被 Dependabot 正常解析 |
| 7.1 裸本地化 key | **已解决** | 10 处替换零语义漂移（抽验 DownloadSession.cs:229-232、GameLaunchService.cs:64、ShellViewModel 等，常量值逐一比对）；`Test-LocalizationContract.ps1:87-105` 加守卫（但见 AUD-CI-001） |
| 7.3/7.4 静默失败 | **已解决** | LogViewer 加载失败 → toast + `diagnostics.ErrorAsync` 双通道（`LogViewerDialogViewModel.cs:170-186,211-224`，含分页回滚）；关窗持久化失败 → `ErrorAsync` 写本地日志（`App.axaml.cs:259-285`）；`logLoadFailed` 四语言齐全 |
| 7.2 Debug.WriteLine | **已解决** | 31 行 → 10 处，外部调用点 6 处全部带豁免注释，其余为收敛目标自身 fallback 与 Serilog SelfLog 惯例 |
| T1 测试裸自旋 | **已解决** | `SetupWizardViewModelTests`（PropertyChanged+TCS+2s WaitAsync、步数 guard<10）与 `LocalizationTerminologyTests:166-174`（5s deadline+Assert.Fail）；e1b0929 headless 中间帧采样先于等待，保留 5s 预算 |
| T2 FindProjectRoot 拷贝 | **已解决** | 共享版拆分语义（`TestLocalizationHelper.cs:63-76` 项目根 / `:83-95` 仓库根），5 份测试私有拷贝全删；HeadlessTests 因跨项目不引用留 1 份 `FindRepositoryRoot`（GoldenScreenshot.cs:217），合理 |
| 4.1/4.2 双重 CRC64 + 逐字节查表 | **已解决** | 见下文算法核验；`DownloadAsync` 仅在 `crc64 == expectedHash` 时返回哈希（`FileDownloadService.cs:141`），「已下满」早退返回 null 且安装期自行校验（`:74-78`）；`InstallDownloadedFilesAsync` 跳过条件为 `verifiedHash == manifest.Hash` 精确等价、任何不匹配回落实际读盘（`DownloadExecutor.cs:266-283`）；未动文件仍哈希（有意保留，注释说明） |
| 4.3/4.4 壁纸/横幅 UI 线程解码 | **已解决** | 三分支全部 `Task.Run`（`BackgroundViewModel.cs:233,395,451`）；世代号 `Interlocked.Increment` + 完成前校验使乱序应用/Dispose 竞态受控（`:141,551-555`，有 pre-existing 无头测试覆盖该交错）；横幅解码移出 UI 线程、stale 产物就地 Dispose（`RemoteContentViewModel.cs:551-565`） |
| 2.1 Shell 反向依赖 | **已解决（架构裁决）** | AGENTS.md 拍板 Shell 为 Feature 之上的壳层，向下聚合属受认可例外；modal 契约迁根 `ViewModels/`（5be9610）消除 5 条 Feature 反向引用 |
| 2ffd4a6 删 SignalShowInstance | **无回归** | 源码零残留引用；第二实例→EventWaitHandle→轮询→ShowWindow 链路完整（`Program.cs:82-88` → `CrossProcessLaunchBridge.cs:43-93` → `App.axaml.cs:148,347-368`） |

### CRC64 slicing-by-8 算法核验（主审逐位推导）

- 表构造 `T(k)[i] = T0[(byte)T(k-1)[i]] ^ (T(k-1)[i] >> 8)`（`Crc64Service.cs:98-114`）与 8 字节步进 `crc ^= LE64; crc = T7[b0] ^ … ^ T0[b7]`（`:59-72`）均为标准 reflected slicing-by-8 形式；`BinaryPrimitives.ReadUInt64LittleEndian` 保证任意平台端序字节落位一致。
- 测试含 CRC-64/XZ **规范**校验值 `check("123456789") = 0x995DC9BBDF1939FA`（主审手算换算十进制与断言一致，向量来自规范目录，真正独立）+ 7/8/9、16、63/65、1048581（跨 1MB 读缓冲）边界向量。
- 结论：**算法正确，测试向量独立**。运行实测 136/136 通过。

---

## 新发现（均为 Low / advisory）

### AUD-UI-001 — 自定义壁纸「同路径换内容」被跳过守卫误跳（Low，置信度 85）

- 证据：`BackgroundViewModel.cs:267-268` 的来源 key 只含路径 `Custom|<path>`（Remote 分支含 crc64 内容标识，Custom 文件分支没有）。用户在原路径覆盖图片后，任何刷新都命中「来源未变」守卫，壁纸停留旧图，直至解码目标变化或重启。
- 建议：key 中并入内容指纹（`FileInfo.Length + LastWriteTimeUtc` 即可，无需哈希）。**建议验证：Strongly Supported**（改动局部、无行为面扩散）。
- 建议守卫：补「custom 同路径换内容」行为测试（当前该组合零覆盖）。

### AUD-UI-002 — 横幅解码无尺寸钳制（Low，置信度 95/影响不确定）

- 证据：`RemoteContentViewModel.cs:549-551` 全尺寸 `new Bitmap(...)`，注释明示 UniformToFill 有意不降采样；壁纸路径有 4096px 上限（`Helpers/BackgroundImageDecoder.cs:26`），横幅无任何驻留上限。一张 8K 运营图解码后约 128MB 峰值。
- 建议：与壁纸对齐加钳制上限（不影响显示策略，只限驻留尺寸）。
- 附带：横幅新异步路径（Task.Run 解码、stale 产物 Dispose）无行为测试。

### AUD-CI-001 — 裸 key 守卫存在但未接入任何自动门禁，且正则不覆盖索引器（Low~Medium，置信度 95）

- 证据：`Test-LocalizationContract.ps1:87-105` 守卫只活在人工流程文档里——`verify.ps1`/`build.ps1`/`coverage.ps1`/`test.ps1`/两个 workflow 零调用（rg 实证）；正则 `\.[TF]\(\s*"[A-Za-z]` 不覆盖 AGENTS.md 同样禁止的 `I18n["..."]` 形态。守卫不自动执行 = 7.1 类事故回归时依旧静默。
- 建议：接入 `verify.ps1`（成本一行）+ 扩正则覆盖索引器。**建议验证：Verified**（纯脚本接线）。高杠杆：一处接线恢复整类守卫效力。

### AUD-CI-002 — release.yml installer job 缓存键未覆盖 lock/props（Low，置信度 95）

- 证据：`release.yml:208` 仍为 `'**/*.csproj'` 单行；两个 build job 均已是三行版（`:46-49`）。6d32c28 漏改该 job。仅影响缓存命中，不影响正确性。

### AUD-CI-003 — Dependabot（CPM+lock 已知缺口）× locked mode：依赖升级 PR 需人工再生成 lock（Low/运维性，置信度 85）

- 证据：dependabot-core#13950/#1303——nuget updater 可能改 `Directory.Packages.props` 而不同步再生 `packages.lock.json`；本仓库 CI 全程 `RestoreLockedMode=true`，此类 PR 将稳定以 NU1004 红叉收场。配置本身正确，属组合产物。
- 建议：在 PR 模板或 AGENTS.md 注明「依赖升级 PR 必须本地 `dotnet restore` 再生 lock 并补提交」，避免误判为整改失败。

### AUD-PROT-001 — 原型项目继承 lock 开关产生未跟踪 lock 文件（Low，置信度 93）

- 证据：`prototypes/FluentMotionLab/FluentMotionLab.csproj` 已显式关 CPM 但未关 `RestorePackagesWithLockFile`（自根 props 继承 true），本地 `dotnet run` 会生成未跟踪的 `packages.lock.json`。建议 csproj 补 `<RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>`。

### Advisory（pre-existing，本次顺带核实，非 delta 引入）

- `BackgroundViewModel.ReplaceBackgroundImageAfterResize` 的 `DispatcherPriority.Background` Post 释放未走淡化所有权协议（`fadingOutWallpaper`），与换源刷新落在同一 dispatcher 排空窗口时有理论 `ObjectDisposedException` 面（置信度 70，未实测复现）。
- 单实例 show 端点由首实例 `OnFrameworkInitializationCompleted` 才创建，晚于 mutex 获胜，毫秒级窗口内第二实例唤起被静默吞（`CrossProcessLaunchBridge.cs:89-92`）；launch-game 转发不受影响。
- `release.yml:308,310` `github.server_url`/`github.repository` 直接插值进 bash，属可信常量，建议统一 env 风格。

---

## 维持开放的基线项（均有意暂缓/已文档化）

- AUD-ARCH-003（基线 2.3）：`RemoteContentViewModel` 仍直接持有 `DispatcherTimer`（`:29,324-337`），未注入抽象——deferred。
- AUD-MTN-001（基线 7.5 部分）：`IModalPresenter` 查表与 `RemoteContentViewModel` 拆分未做——deferred（CODEBASE_AUDIT.md 已登记）。
- AUD-DEP-001（基线 5.6）：THIRD-PARTY-NOTICES 两行占位许可证未补（delta 区间零改动）——deferred。
- AUD-DEP-002（基线 5.4）：Shirasagi0012.MaterialColorUtilities 维持 + 年度重审——accepted-risk。
- AUD-TST-001（基线 T3）：`GameDownloadServiceTests.cs:979-991` 真实限速 + Stopwatch 下限断言仍在——advisory 维持。

## 核实通过、无需行动（正面）

- 4107b48 的 10 处 key 替换零语义漂移；ToastSeverity 已在 `Models/ToastNotification.cs:102`；两处 Async 后缀已加（RelayCommand 生成名剥离，XAML 绑定无需变）；`ILauncherCoreService.LoadAsync` 已补语义 doc（接口级缺失为极小残留）。
- SHA 固定 + Dependabot + locked mode 组合自洽；缓存键两处 build job 均含 csproj/lock/props。
- 壁纸世代号并发模型有 pre-existing 无头测试直接覆盖交错时序；`App.axaml.cs` shutdown 错误通道经 `LocalDiagnostics.ErrorAsync → UnifiedLogger` 确认写盘。

## 建议优先级

1. **立即**：AUD-BLD-001 一行修复 + 提交（当前 main 不可构建，CI 红灯中）。
2. **顺手（同 PR 或随后）**：AUD-CI-001 守卫接线 + 索引器正则；AUD-PROT-001 原型 csproj 一行；AUD-CI-002 缓存键三行。
3. **下次触碰相关文件时**：AUD-UI-001 key 加内容指纹 + 行为测试；AUD-UI-002 横幅钳制；AUD-CI-003 文档注明。

## 审计方法与局限

- 三路并行专项 + 主审直查；高影响发现（AUD-BLD-001、CRC64 正确性、哈希复用契约、locked mode 生效路径）均主审亲读源码/实机复现二次核验。
- 338f798（RunAsync 拆分 543 行 diff）采用「结构复核 + 下载相关 136 测试全绿」验证，未逐行 diff 三百行；风险为低（提交为纯提取且测试面覆盖验证循环）。
- AUD-UI-003 竞态窗口由调度优先级推断，未实测复现（复现需精确控制 dispatcher 排空时序）。
