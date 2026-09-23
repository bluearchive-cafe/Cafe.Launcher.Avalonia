# PROJECT_CONVENTIONS.md

[← Back to AGENTS.md](AGENTS.md)

AI 辅助开发规范 —— 本文件为所有 AI 编码助手（Claude Code、Codex 等）在为本仓库编写代码时提供强制性规则与模式参考。仓库结构、命令与发布流程见 [AGENTS.md](AGENTS.md)。

---

## 1. 核心价值观

1. **行为有测试保护** — 新功能和行为变更应有聚焦测试；先用测试固定缺陷或风险边界，并在完成前运行受影响的测试。
2. **按仓库工作流实施** — `AGENTS.md` 是代理工作流的权威来源：清晰且范围有限的修改直接实施；仅在其中列出的条件成立时才升级到设计或计划流程。
3. **验证先于完成** — 声称完成前必须运行 `dotnet test`（至少跑受影响的测试类）。Don't claim "done" on trust.
4. **向后兼容** — 对 `settings.json` 的修改与新增 key 均需保留对旧格式的兼容；修改公共 API 签名时，所有现有调用点不能断编。
5. **零警告** — `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` 已启用，任何 warning = error。本地 build 后必须看到 `0 个警告 0 个错误`。
6. **无远程遥测** — 诊断日志只留本地。不要添加任何向第三方服务器发送日志或遥测的代码。

---

## 2. 代码格式与风格

### 2.1 命名

| 实体 | 规范 | 示例 |
|---|---|---|
| 类/结构体/枚举 | PascalCase | `LauncherApiClient`, `LogEntrySeverity` |
| 方法（异步） | PascalCase + `Async` 后缀 | `LoadAsync()`, `ReadEntries()` |
| 字段（私有/readonly） | camelCase | `private readonly UnifiedLogger logger;` |
| 属性（公共） | PascalCase | `public bool IsVisible { get; set; }` |
| 枚举值 | PascalCase | `Verbose`, `Debug`, `Error` |
| 本地常量 | PascalCase | `const int MaxRetry = 3;` |
| 测试方法 | `Method_State_ExpectedResult` | `SeverityFilter_WhenNoMatch_ReturnsEmpty` |

### 2.2 文件组织

- 默认每个文件一个主要类型；紧密协作的支持类型可以同文件放置，但应保持同一职责边界。
- `Services/` 下的每个 Service 对应一个文件，文件名与类名一致。
- `Models/` 下按领域分组文件，每个文件对应一个 DTO 或一组强相关的 DTO。
- `Constants/` 下的每个文件独立一个常量类别。
- 测试文件命名：`{TargetClassName}Tests.cs`。

### 2.3 XAML 规范

- 所有可视值使用 `StaticResource`/`DynamicResource` 设计 token（`App.axaml` 中的 `Launcher.*` 家族），**禁止**在 View XAML 中写裸色号、`Transparent`、裸图标尺寸、裸 `4`/`6`/`8` 圆角半径。样式 Setter 与视图内联的 `Spacing`/`ColumnSpacing`/`RowSpacing` 同样必须引用 `Launcher.Spacing.*`（含 `Spacing.None`=0）。透明度只允许两种形态：结构性 `0`/`1`（可见/隐藏、动画关键帧）或引用 `Launcher.StateLayer.*`；分数透明度词汇以 `Launcher.StateLayer.*` 五档为唯一来源（契约锁定）。运行中的 token 取值可在 Debug 门的设计画廊叠层里查看。
- 主题无关的渐变和阴影定义仅允许在 `App.axaml` 或 `MainWindow.Styles.axaml` 中。
- `AutomationProperties.Name` 绑定到本地化字符串的**所有**交互控件都必须有。
- 控件使用语义化的 `Classes` 属性而非内联 Style。
- 大块 XAML（样式、覆盖层）提取为独立 `.axaml` 文件，`MainWindow.axaml` 只保留窗口外壳与内容网格。

### 2.4 代码注释

- 新增或修改的公共类型，以及跨功能边界的 public/internal 成员，应有 XML doc comment (`/// <summary>`) 说明其稳定合约。
- 关键合约与不变量（线程安全、disposal order、线序）必须在注释中写明。
- 注释面向**下一个维护者**（可能是 AI），解释 **why** 而不只是 **what**。
- 符合 `CA2016` 等分析器的抑制项须单行注释说明原因。

---

## 3. 日志记录规范

### 3.1 何时记录

| 场景 | 级别 | 示例 |
|---|---|---|
| 应用启动/退出/会话边界 | Info | `"Session started"`, `"Session ended"` |
| 关键流程成功完成 | Info | `"Game uninstall completed."` |
| 关键流程失败（需人工关注） | Error | HTTP 请求失败、文件 I/O 错误、未处理异常 |
| 可恢复的非严重问题 | Warn | 后台图片加载失败、临时数据不完整 |
| 关键流程中间状态 | Debug | API 调用耗时、下载开始/停止、diff 结果 |
| 极细粒度诊断（默认关闭） | Verbose | CRC 校验汇总、逐文件验证结果 |
| 致命错误（进程即将退出） | Fatal | 初始化失败、未恢复的崩溃 |

### 3.2 如何记录

- **使用 `LocalDiagnostics`** 作为唯一入口（不直接调 `UnifiedLogger.LogAsync`，除了 `Program.cs` 和 `UnifiedLogger` 自身）。
- 将 `LocalDiagnostics diagnostics` 通过构造函数注入，存储为 `private readonly` 字段。
- `title` 参数是日志行的 `[LogTitle]` 标签：用简短的 PascalCase 标识调用方模块（如 `"GameDownload"`, `"LauncherCore"`, `"ApiClient"`）。该标签受 `DiagnosticsLogTitleContractTests` 源码契约守护。
- `message`（可选）放上下文细节：文件路径、耗时毫秒、计数值、状态码。不记密钥/盐/Authorization 头。
- 非热路径的同步上下文用 `LocalDiagnostics.LogSync(severity, title, message)`。**例外——UI 点击路径**（如 `GameDownloadService.Stop()`、`DownloadSession.Pause()/Resume()`）：LogSync 的 sync-over-async 会在 Serilog async sink 背压时阻塞 UI 线程，这类方法改用显式弃等 `_ = diagnostics.DebugAsync(...)` 并注释意图（`DebugAsync` 内部吞掉全部异常，弃等的 Task 不会产生未观察异常）。
- 异步上下文用 `await diagnostics.DebugAsync(title, message, CancellationToken.None)`。不传播调用方的 cancellationToken（日志不应被取消）。

### 3.3 不要做

- 不要在每个下载块后记录逐字节的进度日志（进度已有 UI 通道）。
- 不要在纯静态工具方法（`VersionComparer.Compare`）中记录日志。
- 不要重复记录同一事件的不同级别。
- 不要在日志消息中记录敏感信息（Authorization 头、API salt、user cookie）。

---

## 4. 本地化 (i18n) 规范

### 4.1 添加新字符串

1. 在 4 个资源文件（`Resources/LauncherStrings{,.zh-Hans,.zh-Hant,.ja}.resx`）中按字母序添加 key-value。
2. XAML 中绑定：`{Binding Shell.I18n[newKey]}`。绑定路径是字符串、引用不到 C# 常量，故这一面由 `ResxResourceContractTests.XamlResourceBindings_UseOnlyKeysThatExistInNeutralResources` 做存在性守卫——拼错的键以前构建与测试全绿，只在运行期降级为 `"Localization unavailable."`；同一测试类的 `XamlKeyScan_StillCoversTheKeyBearingFiles` 是它的反空转基线，扫描域为应用工程下全部 `.axaml`（无手抄文件清单）。
3. C# 中一律引用 `Constants/LocalizationKeys` 的编译时常量，**禁止**向 `T()`/`F()`/`I18n[...]` 传裸 key 字符串字面量（`ResxResourceContractTests` 守护）。
4. 所有 4 种语言都提供翻译（对专有名词可回退到英文文本，但不得留空）；术语与译名以 `UBIQUITOUS_LANGUAGE.md` 的规范译法为准。
5. 新增或重命名 key 后运行 `scripts/Generate-LauncherStringsDesigner.ps1` 与 `scripts/Generate-LocalizationKeys.ps1`，再运行 `scripts/Test-LocalizationContract.ps1`。

### 4.2 测试中的本地化

- 使用 `LocalizationService.T()` 的单元测试通过 `TestLocalizationHelper.Initialize()` 或 `LocalizationService.InitializeForTesting(...)` 提供测试资源（前者是 `TestRepository.InitializeLocalizationResources()` 的转发面，见 §6.5）。
- 不要在测试中直接写死预期中文字符串（本地化可能变化）；改用 key 查找或只断言非空/非 null。

---

## 5. DI 与 Service 注册

### 5.1 注册规则

- 所有 DI 管理的 Service 和 ViewModel 在 `Composition/ServiceConfiguration.AddLauncherServices()` 中注册。
- 单窗口桌面应用：全部注册为 `AddSingleton`（无 scoped 边界）。
- `UnifiedLogger` 在 `Program.cs` 预创建，通过 `Composition.ServiceConfiguration.AddLauncherServices(existingLogger:)` 传入 DI 容器复用同一实例。

### 5.2 IDisposable 顺序

`ServiceProvider` 按已创建服务的注册逆序调用 `Dispose()`。新增 `IDisposable` 服务时，检查 `Composition/ServiceConfiguration.cs` 中的注册位置，确保它在仍依赖它的服务之后释放。`Program.RunSession` 在会话结束日志写入后显式释放共享的预 DI `UnifiedLogger`。

### 5.3 构造函数注入

- 所有依赖通过构造函数注入，不使用属性注入或 Service Locator。
- 测试用构造函数（接收 `HttpMessageHandler` 或其他测试替身）标记为 `internal`。
- 不使用 Mocking 框架；测试用手写 stub/fake/handler 子类（共享替身放 `tests/TestDoubles/`，经 Compile-Link 编入两个测试程序集）。

---

## 6. 测试规范

### 6.1 测试项目

- 单元测试：`tests/Cafe.Launcher.Avalonia.Tests/`（xUnit v3 + coverlet.msbuild）
- Headless UI 测试：`tests/Cafe.Launcher.Avalonia.HeadlessTests/`（xUnit v3 + Avalonia.Headless.XUnit，含黄金截图基线）

### 6.2 测试结构

- 一个测试类对应一个被测试类，文件名 `{Target}Tests.cs`。
- 使用 `Fact`（同步/异步）和 `Theory`（参数化）。
- 测试方法命名：`Method_State_ExpectedResult`（下划线风格，`CA1707` 已对测试文件关闭）；源码/契约类守卫测试可用两段式 `Subject_Expectation`。
- IDisposable 的测试类可选实现 `IDisposable` 清理临时文件/目录；临时目录用 `TestDirectory` 持有（见 §6.5），不要自己拼 `Path.GetTempPath()` 再手写删除与退避。

### 6.3 测试编写规则

- **每个新功能必有测试。** 没有测试的 PR/分支不应合并。
- 修改框架/基础设施（日志、本地化、DI）时，先跑现有的全套测试 → 再写新的覆盖新增行为。
- `UiStyleContractTests` 在修改任何 XAML 文件后都必须跑一遍。
- 平台门控的测试用 `Assert.SkipUnless`/`Assert.SkipWhen` 显式跳过，**禁止**用早期 `return` 静默跳过（跳过必须出现在测试结果里）。
- 等待异步状态一律用有截止时间的轮询（统一走 `TestWait.UntilAsync`；无头侧用 `HeadlessTestHost.WaitUntilAsync`，它在同一实现上补一次 UI 调度推进），不要用裸 `Task.Delay(N)` 后断言；能直接等任务的地方直接 `await ... .WaitAsync(超时)`。确需固定延时的负向断言，延时从（internal 可见的）生产常量推导，不要手抄魔数。
- 覆盖率最低阈值为 line ≥ 50%、branch ≥ 50%；`coverage.ps1` 还会验证仓库当前覆盖率基线未回退（基线数值以 `coverage.ps1` 为准，每次运行打印余量）。
- 新增服务按适用情况覆盖：正向路径、典型失败路径（exception/validation failure）和关键边界条件（如 null input、empty collection）。

### 6.4 测试替身

- 不用 Moq/NSubstitute。伪造 `HttpMessageHandler` 时手写子类。
- 伪造 DI 依赖时，创建简洁的内部构造函数接受 `Action<>` 或 `Func<>` 委托。
- 伪造本地化时调用 `TestLocalizationHelper.Initialize()`。
- 只合并**契约相同**的重复替身（共享的放 `tests/TestDoubles/`）；场景专属的故障模拟留在使用它的测试旁边，不要为了共用把替身做成瑞士军刀。

### 6.5 共享测试设施（`tests/Support/`）

两个测试工程通过 `Compile-Link` 共用同一份源码（与 `tests/TestDoubles/` 同一机制）。新增设施的门槛是「两套件都需要、且不含任何工程专属知识」：

- **`TestDirectory`** — 临时目录的唯一创建与清理点：短路径独立目录、派生 `LauncherDataRoot`、释放即删除（有界重试）。删除失败默认**可见**（抛 `IOException`）；无头容器拆卸用 `TestDirectoryCleanup.BestEffort`（留下目录并写诊断，不让清理问题掩盖断言结论）。释放顺序是硬约束：先放掉容器/服务，再 `Dispose()` 目录。它隐式转换为自己的路径字符串，因此用例仍可把它当路径用（`Path.Combine(tempDir, ...)`），但**不要**再对它调用 `Directory.Delete`——删除归 `Dispose`。
- **`TestRepository`** — 仓库与应用目录的唯一定位点，并缓存 `.resx` 的解析结果。`InitializeLocalizationResources()` **每次调用都重装**资源快照：`LocalizationService.InitializeForTesting` 是「最后者胜」的进程级状态，缓存住安装会让一个先装自定义资源的用例污染其后所有用例。
- **`TestWait`** — 唯一的有截止时间轮询实现（单调计时、超时、取消、可注入的推进动作）。超时抛带上下文的 `TimeoutException`。
- 主窗口 ViewModel 的装配走 `MainWindowTestContext`：它只释放自己创建的对象，调用方传入的替身与工厂归调用方。它持有的日志器活到用例结束（消费者——设置页与日志查看器——会在其整个活期内继续写入），因此**不要**把日志器放回装配方法里的局部 `using`。
- 无头上下文每个用例一个独立数据根（`HeadlessTestHost.CreateServiceProvider(dataRoot:)`）：进程根是按程序集隔离的共享目录，用它会让同程序集的两个上下文互相看见对方的设置、下载检查点与崩溃快照。

---

## 7. Settings 兼容性规则

持久化落点一律经注入的 `LauncherDataRoot`（见 `CONTEXT.md` 的「数据根」与 [ADR-025](docs/design/adr/ADR-025-数据根由组合根解析并注入.md)），不要在模块内解析进程级数据根：进程根只由组合根与 ADR-019 保护的 pre-DI 路径解析，`TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites` 以声明表守这条边界。新增落盘位置时把路径加进 `LauncherDataRoot`，文件名仍声明在 `Constants/GamePaths.cs`。

停止活动工作流由调用方表达意图（`GameOperationStopIntent`），检查点去留那套策略词表留在 `Features/GameOperations/` 内、由域翻译一次（[ADR-026](docs/design/adr/ADR-026-停止意图由游戏操作域翻译.md)）；域外不要命名 `DownloadStopReason`，`GameOperationStopOwnershipTests` 守卫这条边界。

`settings.json` 的 JSON 字段名必须向后兼容：

- 新增字段：提供合理默认值（在 `LauncherSettings` 模型中），`LauncherSettingsService` 不因缺失字段而抛异常。
- 重命名或删除字段前，明确旧 JSON 的读取策略；必要时在 `LauncherSettingsService` 中解析旧字段。
- `LauncherSettings` 的新增字段需有默认值，并同步更新两张清单：`DeepClone()` 的拷贝构造与 `ComparedProperties`（状态同一性，见 `CONTEXT.md`）。漏前者会静默浅拷贝，漏后者会让设置页保存按钮不再跟踪该字段。`LauncherSettingsService.NormalizeSettings()` 负责将未知或不合法值兜底为有效默认值。`LauncherSettingsTests` 以反射守护两张清单：`DeepClone` 覆盖全部公共可写属性（比较用测试内独立渲染，不复用生产比较器，以免比较器缺陷被报成拷贝缺陷），状态同一性则是两相守卫（两份默认设置必须判为相同；逐属性改动必脏、还原必净），另有守卫断言两张表的名字与全部公共可写属性一一对应。
- 写入已保存设置一律经 `ISavedSettingsWriter`（见 `CONTEXT.md` 的「已保存设置」与 [ADR-024](docs/design/adr/ADR-024-已保存设置唯一写入方.md)），不要直接调用 `LauncherSettingsService.SaveAsync`：绕过去就不会回写编辑器草稿，用户下一次保存设置会把这次写入的字段回滚掉。`SettingsWriteOwnershipTests` 守卫这条边界（生产调用方唯一性 ＋ 持有者声明表）。

---

## 8. Commit 规范

- 遵循 [Conventional Commits](https://www.conventionalcommits.org/) 前缀：`feat:` / `fix:` / `refactor:` / `perf:` / `chore:` / `test:` / `docs:` / `style:`。
- 中文 Commit 消息不使用英文前缀中文正文混排（统一用英文或统一用中文）。
- Release changelog 依赖 Conventional Commits 分组生成，不规范的 commit 前缀导致 changelog 混乱。

---

## 9. 分支与 PR 流程

- `main` 的**实际**保护规则（GitHub ruleset `Protect main branch`，2026-09-11 核对）：

  | 规则 | 状态 |
  |---|---|
  | 禁止删除 `main` | ✅ 强制（`deletion`） |
  | 禁止强制推送 / 非快进更新 | ✅ 强制（`non_fast_forward`） |
  | 必须走 PR 才能合入 | ❌ 未强制 |
  | 必须状态检查通过（CI 绿灯） | ❌ 未强制 |
  | 必须评审人批准 | ❌ 未强制 |

  即：向 `main` 推送快进提交在机制上是允许的，CI 红灯同样能合进 `main`。仓库允许 merge commit / squash / rebase 三种合并方式，关闭了 auto-merge 与「合并后自动删分支」。若要把 CI 变成真正的合并门禁，在 ruleset 上添加 `required_status_checks` 指向 build 的检查名即可。

- 因此以下三条是**团队约定**，靠自觉遵守而非机制强制：
  - 在功能分支上开发并走 PR。
  - 合并前：`dotnet build` 零警告 → `dotnet test` 全部通过（至少受影响的测试 + 合约测试）→ plan mode 下的设计批准（如适用范围 > 2 个文件）。
  - 合并后手动推送（不自动 rebase squash）。

---

## 10. 代码审查检查清单

在提交或 PR 之前，AI 编码助手应逐项确认：

- [ ] `dotnet build -c Debug --no-restore` → 0 warnings, 0 errors
- [ ] `dotnet test`（受影响的测试类）→ 全部通过
- [ ] XAML 改动 → `UiStyleContractTests` 通过
- [ ] 新功能的测试覆盖了预期行为
- [ ] 新增的本地化 key 存在于 4 个 `LauncherStrings*.resx` 文件中，已生成 `LauncherStrings.Designer.cs` 与 `LocalizationKeys.cs`，且资源合约测试通过
- [ ] 未引入裸色号、裸图标尺寸、裸圆角、裸分数透明度（透明度只允许 0/1 或 `Launcher.StateLayer.*` 引用）在 View XAML 中
- [ ] 新增的 public/internal API 有 XML doc comment
- [ ] IDisposable 新增类注册顺序不影响现有 disposal order
- [ ] 日志调用使用 `LocalDiagnostics`（不直接 `UnifiedLogger`），`title` 为 PascalCase 模块标签
- [ ] 未在日志/异常消息中写入敏感信息（密钥、salt、token）
- [ ] AGENTS.md / CONTEXT.md 如有结构性变化一并更新

---

## 11. 常见反模式

| 反模式 | 正确做法 |
|---|---|
| 在 View XAML 中写 `Foreground="#FF0000"` 等 | 使用 `{DynamicResource LauncherAccentBrush}` 等 |
| 在非 App/Styles 文件中定义内联 Style | 提取到 `MainWindow.Styles.axaml` |
| 用 `await task.Result` 代替 `await task` | 直接 `await task` |
| 在 DI 构造中 `new` 一个 Service 而不是注入 | 通过构造函数注入 |
| 用 `Thread.Sleep` 等待异步结果 | 用 `await` 或 `TaskCompletionSource` |
| 在日志里记 `Authorization` 头内容 | 省略所有 secret/salt/token 字段 |
| 对 `CancellationToken` 用 `default` 忽略 | 显式 `CancellationToken.None` 表示有意不传播 |
| 新增 settings 字段不提供默认值导致旧用户启动就崩 | 在 `LauncherSettings` 模型中设合理默认值 |
| 平台分支测试用早期 `return` 跳过 | 用 `Assert.SkipUnless`/`Assert.SkipWhen` 让跳过可见 |

---

## 12. 工具链与依赖

| 工具/库 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 10.0.302 | Runtime / SDK（global.json 钉住，`latestFeature` 滚动） |
| Avalonia / Avalonia.Desktop | 12.1.2 | UI Framework |
| Avalonia.Controls.ColorPicker | 12.1.2 | 自定义主题色取色器 |
| Markdown.Avalonia.Tight | 12.0.0-a3 | 更新说明 Markdown 预览 |
| Avalonia.Themes.Fluent | 12.1.2 | Fluent 主题 |
| Avalonia.Headless.XUnit | 12.1.2 | Headless UI testing |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | 调试期 UI 诊断（Debug 专用，Release 不分发） |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators |
| Material.Icons.Avalonia | 3.0.2 | Icon library |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | DI 容器 |
| Shirasagi0012.MaterialColorUtilities | 0.2.0 | Material 色彩工具 |
| Serilog | 4.4.0 | Logging pipeline |
| Serilog.Sinks.Async | 2.1.0 | 异步日志 sink |
| Serilog.Sinks.File | 7.0.0 | 文件日志 sink |
| xunit.v3 | 3.2.2 | Test framework |
| xunit.runner.visualstudio | 3.1.5 | xUnit VS 适配器 |
| Microsoft.NET.Test.Sdk | 18.10.0 | 测试宿主 |
| coverlet.msbuild | 10.0.1 | Code coverage |
| Inno Setup | 7.0+ | Windows installer（脚本强制最低 7.0，CI 安装 7.1.0） |

> 版本以 `Directory.Packages.props` 中声明的为准；升级依赖时同步更新本表（受 `InstallerContractTests` 守护），并再生 `THIRD-PARTY-NOTICES.md` 与 lock 文件（流程见 AGENTS.md「Dependency upgrades」）。
