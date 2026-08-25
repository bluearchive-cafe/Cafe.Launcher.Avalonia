# PROJECT_CONVENTIONS.md

[← Back to CLAUDE.md](CLAUDE.md) · [Back to AGENTS.md](AGENTS.md)

AI 辅助开发规范 —— 本文件为所有 AI 编码助手（Claude Code、Codex 等）在为本仓库编写代码时提供强制性规则与模式参考。

---

## 1. 核心价值观

1. **行为有测试保护** — 新功能和行为变更应有聚焦测试；先用测试固定缺陷或风险边界，并在完成前运行受影响的测试。
2. **按仓库工作流实施** — `AGENTS.md` 是代理工作流的权威来源：清晰且范围有限的修改直接实施；仅在其中列出的条件成立时才升级到设计或计划流程。
3. **验证先于完成** — 声称完成前必须运行 `dotnet test`（至少跑受影响的测试类）。Don't claim "done" on trust.
4. **向后兼容** — 对 `settings.json` 的修改与新增 key 均需保留对旧格式的兼容；修改公共 API 签名时，所有现有调用点不能断编。
5. **零警告** — `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` 已启用，任何 warning = error。本地 build 后必须看到 `0 个警告 0 个错误`。
6. **无远程遥测** — 诊断日志只留本地。不要添加任何向 Aliyun SLS 或第三方服务器发送日志的代码。

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

- 所有可视值使用 `StaticResource` 设计 token，**禁止**在 View XAML 中写裸色号、`Transparent`、裸图标尺寸、裸 `4`/`6`/`8` 圆角半径。
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
- `title` 参数是日志行的 `[LogTitle]` 标签：用简短的 PascalCase 标识调用方模块（如 `"GameDownload"`, `"LauncherCore"`, `"ApiClient"`）。
- `message`（可选）放上下文细节：文件路径、耗时毫秒、计数值、状态码。不记密钥/盐/Authorization 头。
- 同步上下文用 `LocalDiagnostics.LogSync(severity, title, message)`（如 `Stop()`、`Pause()` 等 void 方法）。
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
2. XAML 中绑定：`{Binding Shell.I18n[newKey]}`。
3. 所有 4 种语言都提供翻译（对专有名词可回退到英文文本，但不得留空）。
4. 新增或重命名 key 后运行 `scripts/Generate-LauncherStringsDesigner.ps1`，并运行 `scripts/Test-LocalizationContract.ps1`。

### 4.2 测试中的本地化

- 使用 `LocalizationService.T()` 的单元测试通过 `TestLocalizationHelper.Initialize()` 或 `LocalizationService.InitializeForTesting(...)` 提供测试资源。
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
- 不使用 Mocking 框架；测试用手写 stub/fake/handler 子类。

---

## 6. 测试规范

### 6.1 测试项目
- 单元测试：`tests/Cafe.Launcher.Avalonia.Tests/`（xUnit v3 + coverlet.msbuild）
- Headless UI 测试：`tests/Cafe.Launcher.Avalonia.HeadlessTests/`（xUnit v3 + Avalonia.Headless.XUnit）

### 6.2 测试结构
- 一个测试类对应一个被测试类，文件名 `{Target}Tests.cs`。
- 使用 `Fact`（同步/异步）和 `Theory`（参数化），不使用 `TestFixture`/`TestClass` 等 NUnit 属性。
- 测试方法命名：`Method_State_ExpectedResult`（下划线风格，`CA1707` 已对测试文件关闭）。
- IDisposable 的测试类可选实现 `IDisposable` 清理临时文件/目录。

### 6.3 测试编写规则
- **每个新功能必有测试。** 没有测试的 PR/分支不应合并。
- 修改框架/基础设施（日志、本地化、DI）时，先跑现有的全套测试 → 再写新的覆盖新增行为。
- `UiStyleContractTests` 在修改任何 XAML 文件后都必须跑一遍。
- 覆盖率最低阈值为 line ≥ 50%、branch ≥ 50%；`coverage.ps1` 还会验证仓库当前覆盖率基线未回退。
- 新增服务按适用情况覆盖：正向路径、典型失败路径（exception/validation failure）和关键边界条件（如 null input、empty collection）。

### 6.4 测试替身
- 不用 Moq/NSubstitute。伪造 `HttpMessageHandler` 时手写子类。
- 伪造 DI 依赖时，创建简洁的内部构造函数接受 `Action<>` 或 `Func<>` 委托。
- 伪造本地化时调用 `TestLocalizationHelper.Initialize()`。

---

## 7. Settings 兼容性规则

`settings.json` 的 JSON 字段名必须向后兼容：
- 新增字段：提供合理默认值（在 `LauncherSettings` 模型中），`LauncherSettingsService` 不因缺失字段而抛异常。
- 重命名或删除字段前，明确旧 JSON 的读取策略；必要时在 `LauncherSettingsService` 中解析旧字段。
- `LauncherSettings` 的新增字段需有默认值，并同步更新 `DeepClone()`；`LauncherSettingsService.NormalizeSettings()` 负责将未知或不合法值兜底为有效默认值。

---

## 8. Commit 规范

- 遵循 [Conventional Commits](https://www.conventionalcommits.org/) 前缀：`feat:` / `fix:` / `refactor:` / `perf:` / `chore:` / `test:` / `docs:` / `style:`。
- 中文 Commit 消息不使用英文前缀中文正文混排（统一用英文或统一用中文）。
- Release changelog 依赖 Conventional Commits 分组生成，不规范的 commit 前缀导致 changelog 混乱。

---

## 9. 分支与 PR 流程

- `main` 分支受保护，应在功能分支上开发。
- 合并前必须：`dotnet build` 零警告 → `dotnet test` 全部通过（至少受影响的测试 + 合约测试）→ plan mode 下的设计批准（如适用范围 > 2 个文件）。
- 合并后手动推送（不自动 rebase squash）。

---

## 10. 代码审查检查清单

在提交或 PR 之前，AI 编码助手应逐项确认：

- [ ] `dotnet build -c Debug --no-restore` → 0 warnings, 0 errors
- [ ] `dotnet test`（受影响的测试类）→ 全部通过
- [ ] XAML 改动 → `UiStyleContractTests` 通过
- [ ] 新功能的测试覆盖了预期行为
- [ ] 新增的本地化 key 存在于 4 个 `LauncherStrings*.resx` 文件中，已生成 `LauncherStrings.Designer.cs`，且资源合约测试通过
- [ ] 未引入裸色号、裸图标尺寸、裸圆角在 View XAML 中
- [ ] 新增的 public/internal API 有 XML doc comment
- [ ] IDisposable 新增类注册顺序不影响现有 disposal order
- [ ] 日志调用使用 `LocalDiagnostics`（不直接 `UnifiedLogger`），`title` 含义清晰
- [ ] 未在日志/异常消息中写入敏感信息（密钥、salt、token）
- [ ] CLAUDE.md / AGENTS.md 如有结构性变化一并更新

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

---

## 12. 工具链与依赖

| 工具/库 | 版本 | 用途 |
|---|---|---|
| .NET SDK | 10.0.x | Runtime |
| Avalonia | 12.1.1 | UI Framework |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generators |
| Material.Icons.Avalonia | (latest) | Icon library |
| Serilog + Sinks.Async + Sinks.File | (latest) | Logging pipeline |
| xUnit v3 | 3.2.2 | Test framework |
| Avalonia.Headless.XUnit | 12.1.1 | Headless UI testing |
| coverlet.msbuild | 10.0.1 | Code coverage |
| Inno Setup | 6.3+ | Windows installer |
