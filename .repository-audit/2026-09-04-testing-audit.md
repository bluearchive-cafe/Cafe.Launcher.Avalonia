# 仓库审计报告（测试体系专项）

- 审计日期：2026-09-04
- HEAD：8657fa6
- 审计方式：repository-audit 流程（仓库发现 → 规则加载 → 三路并行审计：单元测试质量 / Headless 测试质量 / 覆盖缺口映射 → 证据人工核验）
- 范围：以 Testing Audit 为重点；其余维度仅在与测试交叉处覆盖
- 历史报告：全仓库六维审计（2026-09-02）已归档至 `.repository-audit/2026-09-02-audit.md`

## 整改状态（2026-09-04 更新）

报告中全部 P0–P3 建议已按阶段实施并分阶段提交，阶段验收均为全量测试通过：

| 阶段 | 提交 | 内容 |
|---|---|---|
| P0 | `4a1ca91` | 无界轮询加预算、时间竞态改确定性门控、Dispose 清理容错、串行化护栏注释、21 个本地化测试类纳入隔离 Collection |
| P1 | `c110e3e` | 共享 TestDoubles（合并 5+5 份重复 fake）、TestAnimationSetup/FindProjectRoot/TestTrayPlatform 去重、三个巨石测试文件按域拆分（净 −10.8k 行重构）、Headless 轮询收敛 |
| P2 | `f862337` | 补 68 个用例：ShellRefreshCoordinator/RetryPolicy（零→14）、FileDownloadService 缺口、GameUninstallService 部分失败、ShellLifecycle 失败路径、跨进程竞态、ManifestDiff 边界、ResourcePanelService/ViewModel（零→18） |
| P3 | `9c0d78b` | Golden 失败输出 actual+diff 图、非 Windows 显式 Skip、`test.ps1 -UpdateGolden`、GameDownloadServiceTests tempDir 统一 Dispose、MotionTokens 强类型断言、AGENTS.md 命名规范拍板 |

与报告建议的偏差（有意决策）：
- **xUnit1051 抑制保留**（原建议移除）：实测该规则对全部带可选 CancellationToken 的调用报警（约 1100+ 处文件 IO），远超"裸延迟"范畴；悬挂防护已由 P0 的有界等待设计保证，抑制理由已写入 csproj 注释。
- P2 期间发现的两个疑似 UX 缺陷已上报未修复（属产品决策）：①向导完成持久化失败无用户可见反馈（`SetupWizardViewModel.CompleteAsync` 经 fire-and-forget 命令吞异常）；②两个"恢复默认设置"入口失败仅写日志（`DialogsViewModel.ConfirmSettingsResetAsync`/`ConfirmDebugResetAsync`）。

整改后规模：单元 1412 通过/2 跳过 + Headless 161 通过；测试基础设施净减约 11k 行重复/巨石代码。


## 摘要

测试体系健康度**高于绝大多数同类仓库**：测试代码 34.1k 行超过生产代码 28.6k 行（1.19:1）；1015 个 `[Fact]/[Theory]`（theory 展开后 **1505 个用例**，本机实测全绿，单元 30s + Headless 52s）；手写行/分支覆盖率棘轮基线 84.30% / 88.99%；无真实网络/注册表/托盘依赖；用户数据目录进程级隔离；契约测试（样式/本地化/安装器脚本）成体系；xUnit 分析器 + `TreatWarningsAsErrors` 全开。

无 Critical 问题。高置信度发现 12 项，集中三类风险：

1. **静态状态隔离完全依赖全局串行化，且无护栏**（最大的隐性耦合）；
2. **少量时间敏感断言与无界轮询循环**（最可能的 CI flake / 挂死来源）；
3. **少数高并发、删除类关键路径覆盖缺口**（`ShellRefreshCoordinator`、`RetryPolicy` 零测试）。

一个值得注意的结构性事实：Headless 测试仅占 11% 用例却消耗 63% 套件时长（161 例 / 52s 对 1344 例 / 30s），印证动画与等待大量依赖真实时间。

## 1. Critical Issues

无。

## 2. 高优先级发现（小改动即可消除）

### 2.1 三处无界轮询循环可挂死整条 CI 流水线（置信度 90，已核验）

- `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs:2701` — `while (!settled)` 无 deadline；
- `:2503-2506` — `while (!wizard.IsLastStep) { wizard.NextCommand.Execute(null); }` 循环体内无 await，命令被门控卡住即真死循环；
- `:2525-2536` — 同类，内层等待仅在 `IsStep1` 分支生效。

影响：调度异常时测试永久挂起直至 CI job 超时（40 分钟）。仓库内已有正确范例：`WaitForGamePathStatusAsync`（`MainWindowHeadlessTests.cs:3099`，事件驱动 + `WaitAsync(2s)`）。建议全部改为带 3–5s deadline 的统一轮询 helper。

### 2.2 全局串行化是所有静态隔离的隐形基石（置信度 85，已核验）

两个测试程序集均 `DisableTestParallelization = true`（`tests/*/AssemblyInfo.cs:3`），约 34k 行、1505 用例全部串行。依赖串行兜底的静态状态：

| 状态 | 位置 | 问题 |
|---|---|---|
| `LocalizationService.testResources` | `TestLocalizationHelper.cs`，21 个类经静态构造函数初始化 | 写入后从不重置，"最后者胜"；仅 1 个类在隔离 Collection 中 |
| `AnimationTimings.ExitAnimationDuration` | `TestAnimationSetup.cs`（ModuleInitializer，两项目各一份拷贝） | 全局清零生产静态；测试永远运行在"零时长动画"这一生产不会出现的配置下 |
| `SettingsAppearanceViewModel.ApplyScheme` | 多处 | 直接改 Application 级资源，靠各测试自行快照/恢复 |
| `SettingsViewModel.AppearancePreviewSettleTimeout` | `MainWindowViewModelTests.cs:845` | mutate-and-restore，进程级共享 |
| `CultureInfo` | `LocalizationServiceTests.cs:43` 等 3 处 | 保存/恢复齐全，但依赖串行 |

影响：套件时长随规模线性恶化；任何人移除串行化配置，上述状态全部变成竞态，且无注释或测试提示这层耦合。建议：`AssemblyInfo.cs` 加注释声明依赖方；21 个本地化测试类统一纳入隔离 Collection（或一次性 collection fixture）；中长期在静态状态按 Collection 隔离后评估类间并行。

### 2.3 时间敏感断言是最可能的 flake 来源（置信度 85–90，已核验）

| 位置 | 模式 | 性质 |
|---|---|---|
| `GameDownloadServiceTests.cs:988-996` | 真实限速 + Stopwatch 断言"至少 800ms" | 必然慢；调度抖动可偶发假失败 |
| `GameDownloadServiceTests.cs:1285-1287` | `Task.WhenAny(resumeTask, Task.Delay(250))` | 要求 250ms 内完成，慢机偶发失败 |
| `ToastHostViewModelTests.cs:112/645/696` | `Task.Delay(20)` 后断言状态翻转 | 定时竞态 |
| `MainWindowViewModelTests.cs:828` | `Task.Delay(50)` 后断言"未完成" | 定时竞态 |
| `BannerCarouselTransitionTests.cs:25,33` | 500ms 动画在 50ms 处单点采样中间帧 | 时间敏感 |
| `BackgroundViewModelHeadlessTests.cs:124` | `Task.Delay(300)` 后断言"未重解码" | **误绿风险**：debounce 晚于 300ms 时负向断言失效 |

修复范式仓库内已具备：`TaskCompletionSource` 门控（`GameOperationsViewModelTests.cs:34`、`BackgroundViewModelHeadlessTests.cs:257`）、事件驱动 `WaitAsync`（4 处范例）、限速逻辑注入 `TimeProvider` 或可配置间隔。

## 3. 覆盖缺口（按 风险 × 缺口 排序）

`ShellRefreshCoordinator` 与 `RetryPolicy` 的零测试引用经 grep 全量核验。

| 目标 | 现状 | 缺口场景 | 风险 |
|---|---|---|---|
| `ShellRefreshCoordinator`（Features/Shell） | **0 测试** | 信号量串行化、activeRefreshCount、关闭排空握手——纯并发逻辑恰是最该单测的 | 高 |
| `RetryPolicy`（Services） | **0 直接测试**（仅经 API 客户端间接） | 取消立即传播、不可重试异常透传、退避时序 | 中-高 |
| `ShellLifecycle`（856 行，壳编排中枢） | 6 例间接 + 文本契约 | 设置保存失败、刷新异常传播、向导完成失败 | 高 |
| `FileDownloadService`（下载完整性核心） | 域重试/续传/哈希已充分 | 取消中断流、HTTP 非 2xx、超时 | 高 |
| `GameUninstallService`（删文件） | 6 例仅守卫 + 正常路径 | 部分删除失败、中断态、重复卸载并发 | 高 |
| `CrossProcessLaunchSignal`/`PollingListener` | 9 例 | 废弃句柄、竞态、多次 set；Windows 实路径无 SkippableFact 兜底 | 高 |
| `ResourcePanelService`/`ResourcePanelViewModel`（446 行） | **0 专项测试**，仅集成引用 | 并行读部分失败、保存序列化错误、UID 回退优先级 | 中 |
| `ManifestDiffCalculator`（251 行，更新决策） | 仅 3 例 | 改名/大小写/仅哈希变化边界 | 中 |

说明：`SettingsAppearanceViewModel`（760 行）无专项测试文件，但属 UI 状态，优先级低于上表。平台相关代码的测试守卫整体做得好（`WindowsFactAttribute`、`Assert.SkipUnless(OperatingSystem.IsWindows(), ...)`、注入 `IsWindowsPlatform` 委托全分支覆盖）；仅注册表代理提供者、user32 动画设置等真实 OS 路径只能在 Windows 人工验证。

## 4. Architecture（测试视角）

**正面**：Feature 垂直切片 + 组合根绑定窄接口（`IGameOperationExecutor`、`IProcessLauncher`、`ISystemTrayPlatform`、`IWindowMetricsService`）使测试可在 DI 层插桩；Headless 测试走与生产相同的 `AddLauncherServices()` 注册表再按需覆盖（`HeadlessTestHost.cs:22-31`）。

**问题：测试基础设施多份拷贝，存在漂移风险**（置信度 90–95）：

| 重复项 | 位置 |
|---|---|
| `IGameOperationExecutor` fake（同名 `TestBackend` ×3） | `DebugViewModelTests.cs:245`、`GameOperationsViewModelTests.cs:1091`、`WindowChromeViewModelTests.cs:324`、`GameOperationJourneyTests.cs:301`、`MainWindowViewModelTests.cs:2346` |
| `IFileDownloadService` fake ×5 | `DownloadExecutorTests.cs:133,177`、`GameDownloadServiceTests.cs:1505,1520,1593` |
| `TestAnimationSetup.cs` | 两个测试项目各一份几乎相同的 ModuleInitializer |
| `FindProjectRoot()` | `TestLocalizationHelper.cs:63` 与 `UiStyleContractTests.cs:5305` |
| `TestTrayPlatform` fake / `CreateContext` 样板 | `SystemTrayServiceTests.cs:86` 与 `MainWindowHeadlessTests.cs:3193/3054`、`HeadlessTestHost.cs:22` |
| Dispose 清理不一致 | `MainWindowHeadlessTests.cs:3183` 裸 `Directory.Delete`，而 `HeadlessTestHost.cs:80` 同场景已有 catch——主测试类反而缺保护 |

不使用 mocking 框架是 `PROJECT_CONVENTIONS.md` 的明文规定，当前手写成本可控，不建议引入 mocking 库；收敛建议是建立共享 `TestDoubles`（Recording/Configurable 双 fake 先合并两个 ×5 的接口）。

## 5. Testing（质量细节）

- **命名规范漂移**（置信度 95）：约 69% 严格符合 `Method_State_ExpectedResult` 三段式，285 个两段式（多为契约守卫测试），1 个无下划线且方法体超 100 行（`RemoteContentViewModelTests.cs:309`）。建议批量规范化，或在 AGENTS.md 明确"契约测试允许两段式"。
- **巨石文件**（行数已核验）：`UiStyleContractTests.cs` 5351 行（占单元测试项目 18%）、`MainWindowHeadlessTests.cs` 3273 行/94 测试/8 种职责、`MainWindowViewModelTests.cs` 2622 行。纯移动即可拆分。
- **低危坏味道**：`<NoWarn>xUnit1051</NoWarn>`（`Tests.csproj:24`）压掉测试挂起警告；`MotionTokensTests.cs:9-27` 反射断言常量值，字段改名后报错误导；`GameDownloadServiceTests.cs:997/1257` 在测试体中段删 tempDir，断言失败时泄漏；`LauncherSettingsServiceTests.cs:148-150` 期望值随机器 UI 文化变化。
- **Golden 截图机制**（置信度 85–95）：5 张基线已提交，容差每通道 8 / 失配比 1%，`CAFE_GOLDEN_UPDATE=1` 手工再生成。缺口：无 `OperatingSystem.IsWindows()` 守卫（非 Windows 本地跑必挂，现仅靠 CI `windows-latest` 事实规避）、失败无 diff 图输出、更新流程未集成进 `test.ps1`。
- **Headless 基础设施**：每程序集一次 App、每测试一个 Window、无 headless 假时钟 API（动画靠真实时间 + `RunJobs()` 泵帧）；无界循环与魔法延迟之外，等待模式整体健康（160 处 `RunJobs()`、4 处事件驱动 `WaitAsync`）。

## 6. 正面发现（保持现状）

- 无真实网络/注册表/托盘依赖；fake `HttpMessageHandler` + `.invalid` 域名；`EasterEggTests` 注入固定 `DateTime`。
- 断言质量高于平均：约 3.8 断言/方法，`Assert.NotNull` 仅占 3%，逐字段 JSON 断言、精确字节数组比较、`TaskCompletionSource` 时序断言均有范例。
- Theory 数据 0 缺失；Skip 仅 1 处且实现为可复用的 `WindowsFactAttribute`。
- 覆盖率棘轮（行 84.30% / 分支 88.99%，禁止下探）+ 空壳报告检测 + 双项目合并统计，工程化程度高。
- 契约测试体系（UI 样式 131 例、本地化契约、安装器/发布脚本契约）以低成本守护了大量易回归面。

## 7. 优化建议路线图

**P0 — 消除 CI 挂死与 flake（约半天）**
1. 三处无界循环加 deadline（`MainWindowHeadlessTests.cs:2701/2503/2525`）
2. 250ms/50ms/20ms 时间断言改确定性门控；`BackgroundViewModelHeadlessTests.cs:124` 负向断言改记录式谓词
3. `MainWindowHeadlessTests.cs:3183` Dispose 清理补 catch（照抄 `HeadlessTestHost.cs:80-88`）
4. 两个 `AssemblyInfo.cs` 注释串行化依赖；本地化初始化类纳入隔离 Collection

**P1 — 结构性可维护性（1–2 天）**

5. 提取共享 TestDoubles（先合并两个 ×5 的 fake），合并重复的 `TestAnimationSetup`/`FindProjectRoot`/`TestTrayPlatform`/`CreateContext`
6. 拆分三个巨石测试文件（纯移动）：`UiStyleContractTests.cs` 按视图域、`MainWindowHeadlessTests.cs` 按职责七分、`MainWindowViewModelTests.cs` 按特性
7. 轮询收敛为单一 `WaitUntilAsync(Func<bool>, TimeSpan)` helper，统一超时预算

**P2 — 补关键路径测试（按第 3 节表格逐个 PR）**

8. `ShellRefreshCoordinator` 并发语义 → `RetryPolicy` 语义 → `ShellLifecycle` 失败路径 → `FileDownloadService` 取消/错误 → `GameUninstallService` 部分失败 → `CrossProcessLaunchSignal` 竞态 → `ResourcePanelService/ViewModel` → `ManifestDiffCalculator` 边界

**P3 — 卫生项（随手改）**

9. Golden：失败输出 diff PNG；加 OS 守卫非 Windows 显式 Skip；`CAFE_GOLDEN_UPDATE` 写进 `test.ps1`
10. 移除 `<NoWarn>xUnit1051</NoWarn>` 并给裸 `Task.Delay` 补超时/取消
11. `MotionTokensTests` 反射断言改强类型引用
12. tempDir 清理统一移入 `Dispose`；命名两段式问题在 AGENTS.md 拍板或批量规范化

---

*审计方法说明：发现由并行探查产出，高影响项均经人工核验源码（并行化配置、无界循环、时间断言、golden 守卫、零测试引用、文件行数）；置信度 <80 的观察已丢弃。全量测试在本机实测通过（1344+161 例，约 82s，不含构建）。*
