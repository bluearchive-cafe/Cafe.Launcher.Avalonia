# 仓库审计报告 — v1.0.0 以来的变更（4cfbabae → 5f84c91）

- 审计日期：2026-08-25
- 审计范围：`v1.0.0`（4cfbabae）至 `HEAD`（5f84c91），共 **16 个提交**
- 工作树状态：干净
- 统计：239 个文件变更（+1763 / -2132，含大比例纯重命名），仓库共 311 个受跟踪文件、约 39.8k 行 C#

## 本范围变更概览

| 提交 | 内容 |
|---|---|
| 7fb20ce | 重组为标准 .NET 工作区：应用移入 `src/Cafe.Launcher.Avalonia/`，新增 `.slnx` 解决方案 |
| dfa23e3 | 移除崩溃恢复功能（`CrashRecoveryService`、`session.active` 标记、恢复对话框） |
| 21967ca / b4bbba0 | 移除“详细”底部面板与设置页顶部状态栏 |
| 28753df / c2592e0 / 007ff7b / 10de288 | 远端内容区域重构（横幅悬停/聚焦自动暂停、原生标签页、社交图标组、横幅控件修复） |
| b0c2a81 | 安装程序从 NSIS 迁移到 Inno Setup |
| 7f7ae68 / e3854a7 / ebd1a41 | CI Action 升级、覆盖率脚本路径修复、版本升至 v1.0.1-beta.1 |
| 8ac5a24 / 065c1e2 / 5f84c91 / 8890cb4 | 资源键契约 456→450、发布说明、Toast 动画测试、过时文档清理 |

## 摘要

本次变更的健康度总体良好：项目重组、崩溃恢复移除、Inno Setup 迁移、CI 升级四项机械性改动均完整、自洽，测试套件同步更新且覆盖面扎实（本地最近一次完整运行：单元 1074 通过 / 0 失败，Headless 96/96 通过；`UiStyleContractTests` 519 行改动继续强制 XAML 令牌纪律）。无关键（Critical）问题。

真正的问题集中在**移除功能的残留**——文档、资源键、写死状态未同步清理，以及一处界面交互状态的双通道设计。修复成本均为中低，优先级建议见文末。

---

## 高置信度发现（≥ 80）

### 1. 文档漂移：README / CLAUDE.md 仍描述已删除的崩溃恢复功能
- **类别：** Maintainability / Docs
- **位置：** `README.md` L3、L12、L106、L152、L163、L183-184；`CLAUDE.md` L82、L103；`README.md` L140
- **证据：**
  - README L12「**崩溃恢复** — 进程启动时通过 `session.active` 检测上一会话是否异常结束」；L106 仍列出 `session.active` 标记表；L183-184 仍绘制 `new CrashRecoveryService() → 检测上次崩溃（session.active）` 启动时序图；L152「Program.cs …会话恢复」。
  - CLAUDE.md L82「…and the `session.active` crash-recovery lifecycle」；L103「Launcher data lives in `%LOCALAPPDATA%\Cafe Launcher\`: settings, **crash marker**, …」。
  - HEAD 代码中 `CrashRecoveryService` / `session.active` / `PreviousSessionCrashed` 已全部删除（src 与 tests 中 0 匹配）；`Program.cs` 仅保留崩溃日志与会话起止日志。
  - README L140 将 `statusDetailMode` 描述为 `hidden / compact / detailed`，但枚举与 `NormalizeSettings` 仅接受 Hidden/Compact。
- **影响：** 中。面向用户的项目 README 与面向 AI 的权威架构文档（CLAUDE.md）都与代码矛盾，会误导后续维护者去寻找不存在的 `session.active` 生命周期。
- **置信度：** 95
- **建议：** 移除 README/CLAUDE.md 中 `session.active`、`CrashRecoveryService`、崩溃恢复/会话恢复描述，将 L140 改为 `hidden / compact`，L152/L183-184 改为“崩溃日志与会话起止日志”。

### 2. 三个孤儿本地化键残留（pauseCarousel / resumeCarousel / statusDetailModeDetailed）
- **类别：** Maintainability / i18n
- **位置：** `Resources/LauncherStrings{,.zh-Hans,.zh-Hant,.ja}.resx`（4 个文件）；`Resources/LauncherStrings.Designer.cs` L507、L635、L811
- **证据：** 上述三个键在全部 4 个 resx 与生成的 Designer 访问器中存在，但全仓 `.cs`/`.axaml`/`.ps1`/`.md` 中除生成代码外无任何消费方。8ac5a24 只正确移除了 6 个 `crashRecovery*` 键（456→450），轮播手动暂停/恢复按钮（已改为悬停/聚焦自动暂停）与“详细”模式移除后留下的这三个键被漏掉。
- **影响：** 中。死键膨胀四份语言文件，且被计入 450 键契约，后续移除需同步更新契约测试（450→447）。
- **置信度：** 92
- **建议：** 从 4 个 resx 删除三个键，重新生成 `LauncherStrings.Designer.cs`，更新 `ResxResourceContractTests` 键数契约并运行 `Test-LocalizationContract.ps1`。

### 3. 新闻条目按钮缺少 `AutomationProperties.Name`
- **类别：** Maintainability / 无障碍
- **位置：** `src/Cafe.Launcher.Avalonia/Views/MainWindow.axaml` L297-302
- **证据：**
  ```xml
  <Button Classes="content-link news-row"
          Cursor="Hand"
          ToolTip.Tip="{Binding Title}"
          Command="…OpenExternalUrlCommand" CommandParameter="{Binding Url}">
  ```
  该按钮可交互（打开外部 URL）但无 `AutomationProperties.Name`，违反 PROJECT_CONVENTIONS 2.3「所有交互控件必须有本地化 AutomationProperties.Name」。同区域的重构控件均具备：横幅链接（L119）、社交链接（L364）、轮播圆点（L170）。`UiStyleContractTests` 只覆盖固定命令列表，未覆盖 `news-row`。
- **影响：** 中。读屏/辅助技术用户面对一个无名焦点控件。
- **置信度：** 90
- **建议：** 添加 `AutomationProperties.Name="{Binding Title}"`（与社交行一致，标题为条目数据而非语言键）。

### 4. `ShellViewModel` 写死演示状态（状态栏/底部面板移除后的残留）
- **类别：** Architecture / Dead code
- **位置：** `src/Cafe.Launcher.Avalonia/ViewModels/ShellViewModel.cs` L51-96（声明）、L174-261（赋值）；写入方 `Features/GameOperations/GameOperationsViewModel.cs` L204、L283、L294、`Features/Shell/ShellLifecycle.cs` L785
- **证据：** `StatusIconKind`、`StatusText`、`CurrentViewTitle`、`NetworkStatusValueText`、`OperationNote` 五个成员在 `ApplySnapshot`/`SetLoading`/`SetRefreshError` 与 `shell.OperationNote =` 各写入点赋值，但全部 `*.axaml` 与 `*.cs` 中**无任何读取方**（`MainWindowDialogsOverlay.axaml` L154-155 的 `StatusText`/`StatusIconKind` 绑定的是 `ResourcePanelItem`，与 Shell 无关）。整条 `IGameOperationJourneyHost.SetOperationNote` → `ErrorHandlingService.OperationNoteRequested` 管线输出无处消费。每次快照刷新都会用 localizer 重新计算这些字符串。
- **影响：** 中。死演示状态 + 每轮刷新的无谓本地化开销；易误导维护者认为状态/暂停 UI 仍存在。
- **置信度：** 93
- **建议：** 在确认无外部契约后删除这五个成员及其赋值与 `SetOperationNote` 管线；若有保留意图请注释说明。

### 5. 横幅交互状态双通道（单一事实源被破坏）
- **类别：** Architecture
- **位置：** `src/Cafe.Launcher.Avalonia/Views/MainWindow.axaml.cs` L141-148；`ViewModels/RemoteContentViewModel.cs` L319-339、L586-599；`Views/MainWindow.Styles.axaml` L666-684
- **证据：** 同一次指针/焦点事件同时驱动两条通道——视觉通道 `BannerStage.Classes.Set("active", …)`（控件显隐）与 VM 通道 `SetBannerPointerOver/SetBannerFocusWithin` → `UpdateCarouselPauseState`（轮播暂停）。`forceHideControls`（007ff7b 修复）只作用于视觉 class，但仍将现场读取的 `BannerStage.IsPointerOver` 转发给 VM。007ff7b 的修复事实说明 `IsPointerOver` 在 `PointerExited` 事件时刻可能仍为 true（这正是原 bug 的成因），因此 VM 的 `isBannerPointerOver` 可能被锁为 true——轮播暂停状态与视觉控件状态可能漂移。
- **影响：** 中。当前无 headless 测试覆盖焦点/指针退出后的 VM 暂停状态，漂移不会被发现。
- **置信度：** 82
- **建议：** 单一化状态源——VM 暴露 `IsBannerInteractionActive = pointerOver || focusWithin`，XAML 绑定 `Classes.active`，由 VM 统一决定暂停；至少让 `forceHideControls` 路径同时复位 VM 暂停状态。

### 6. 安装程序：旧版卸载桥接执行未经身份校验的注册表路径（提权上下文）
- **类别：** Security
- **位置：** `installer/Cafe.Launcher.Avalonia.iss` L109-168（L119、L140、L152-158）
- **证据：** `RemoveLegacyInstallation` 从 HKLM `UninstallString` 解析路径（引号/空格感知）后以 `Exec(path, '/S _?=…', SW_HIDE, ewWaitUntilTerminated)` 在安装器提权令牌下执行；唯一校验是 `FileExists`——未校验可执行文件名、未校验位于记录的安装目录、未校验签名，且存在 check→exec TOCTOU 窗口。
- **影响：** 中。非特权攻击者写入 HKLM 需管理员权限，故非标准用户提权路径；但属于「未经校验的提权执行」反模式，防御纵深上应视为不可信任边界。属于一次性升级桥接，风险随 NSIS 存量用户减少而下降。
- **置信度：** 82
- **建议：** 执行前要求解析路径属 `Uninstall*.exe` 命名且位于同键 `InstallLocation` 下，并验证签名/已知 NSIS 布局；失败路径丢弃 `SW_HIDE` 或记录解析路径。

### 7. 安装包/卸载程序未签名，且以管理员权限安装
- **类别：** Security / Supply chain
- **位置：** `.iss` L39（`PrivilegesRequired=admin`）；无 `SignTool`/`SignedUninstaller`；`scripts/Build-Distribution.ps1` 与 `.github/workflows/release.yml` 均无签名步骤
- **证据：** 发布产物是用户获取 UAC 提权的入口，但无 Authenticode 签名，用户无法验证产物真实性。
- **影响：** 中。供应链/加固缺口（非代码缺陷）。
- **置信度：** 90
- **建议：** 使用发布证书对 `setup.exe`/卸载程序签名并在发布工作流中校验；若暂缓签名，至少以 commit SHA 锁定 Actions，并保留 `fail_on_unmatched_files`（已设置）+ 产物哈希。

### 8. 测试：`RunSession` 会话生命周期失去唯一测试覆盖
- **类别：** Testing
- **位置：** `src/Cafe.Launcher.Avalonia/Program.cs` L73-83；`Services/Diagnostics/UnifiedLogger.cs` L136-179；`tests/…/DiagnosticsServicesTests.cs`（dfa23e3 移除）
- **证据：** dfa23e3 将 `RunSession(CrashRecoveryService, …)` 改为 `RunSession(UnifiedLogger, …)` 并直接调用 `WriteSessionStartAsync`（L75）/`WriteSessionEndAsync`（L79）/`LogCrash`（L80）。唯一测试该路径的两个测试随崩溃恢复被移除；全仓测试中对 `WriteSessionStartAsync/WriteSessionEndAsync` 无直接覆盖。`UnifiedLogger` 本身未变（0 行 diff），会话起止日志（7 个 ForContext 字段的多行头）从未被直接测试。
- **影响：** 中。进程生命周期起止日志与异常→崩溃日志路径回归将无测试拦截。
- **置信度：** 88
- **建议：** 新增 `RunSession_WhenActionReturns_WritesSessionStartAndEnd`、`RunSession_WhenActionThrows_LogsCrashAndRethrows`（用临时目录 + `UnifiedLogger`）。

### 9. 测试：安装器契约测试仅做静态字符串断言
- **类别：** Testing
- **位置：** `tests/Cafe.Launcher.Avalonia.Tests/InstallerContractTests.cs`；`installer/Cafe.Launcher.Avalonia.iss` L97-213
- **证据：** 全部断言为 `Assert.Contains/DoesNotContain` 文本片段。`[Code]` 的解析/守卫逻辑（`RemoveLegacyInstallation` 引号解析与 `/S _?=` 参数、`InitializeUninstall` 标记校验、`CurStepChanged` 标记写入）无任何行为级验证，Pascal 逻辑缺陷可通过 CI。
- **影响：** 中。`[Code]` 是风险最高的部分，静态检查无法发现逻辑错误。
- **置信度：** 85
- **建议：** 在 CI 运行 Inno 编译器不现实，可接受现状；至少增加对标记文件名同时出现在 `[UninstallDelete]`（L92）与 `CurStepChanged`（L182）的断言，防止一侧改名。

### 10. 测试：横幅焦点交互未在窗口级覆盖
- **类别：** Testing
- **位置：** `MainWindow.axaml.cs` L242-259；`tests/…/MainWindowHeadlessTests.cs` L182
- **证据：** headless 测试仅模拟鼠标（指针分支）并断言导航按钮透明度；`BannerStage.IsKeyboardFocusWithin`、`SetBannerFocusWithin`（单元测试已覆盖）与 `active` class 的焦点驱动路径未在真实控件上验证；`UnconfigureViewModel` 的横幅状态复位也未断言。
- **影响：** 低/中。暂停取决于焦点的视觉契约缺少直接守卫。
- **置信度：** 80
- **建议：** 增加对 BannerStage 调用 `Focus(NavigationMethod.Tab)` 并断言 `IsCarouselPaused`/控件透明度的 headless 测试。

### 11. 性能：`CancellationTokenSource`/`DispatcherTimer` 未在非 Dispose 路径释放
- **类别：** Performance / Resource hygiene
- **位置：** `RemoteContentViewModel.cs` L280-284（`StopCarouselTimer` 只 `Stop()`+`null`）、L124、L485-486、L592
- **证据：** 每次手动导航分配新 CTS 并弃置旧 CTS；`DispatcherTimer` 丢弃前不 `Dispose()`；仅 `Dispose()`（L572）释放。关闭时挂起的图片预载续体仅以 `BannerItems.Contains` 守卫（L456），未检查 `disposed`，可能在新位图赋值后再 `DisposeBannerBitmaps()` 前赋值导致泄漏。
- **影响：** 低。对象可被 GC 回收（无硬泄漏），但为可释放资源的流失，与 `IDisposable` 契约不一致。
- **置信度：** 90
- **建议：** `StopCarouselTimer` 同时 `Dispose()`；在取消点释放被替换的 CTS；位图赋值续体增加 `disposed` 检查。

### 12. 性能：重复 `Apply` 时不取消旧横幅预载
- **类别：** Performance
- **位置：** `RemoteContentViewModel.cs` L119-125、L413-431、L434-480
- **证据：** `Apply` 只取消 `carouselDelayCts`，预载使用 ShellLifecycle 的长生命周期 token，第二次 `Apply` 替换 `BannerItems` 后旧 `Task.WhenAll` 继续运行；正确性由 `!BannerItems.Contains(item)` 守卫兜底，但旧 URL 仍会并发下载/解码（最多 2×4 并发）。
- **影响：** 低。
- **置信度：** 82
- **建议：** 为横幅预载持有专用 CTS，在 `Apply` 顶部取消。

### 13. 安装器：`UsedUserAreasWarning`（多用户机器上的 `{localappdata}` 解析）
- **类别：** Dependencies / Tooling
- **位置：** `.iss` L39（`PrivilegesRequired=admin`）+ L95（`{localappdata}\Cafe Launcher` 删除）
- **证据：** ISCC 7.1.0 实际编译输出警告：`[Setup] "PrivilegesRequired" is set to "admin" but per-user areas (localappdata) are used…`。管理员安装 + 按用户数据，提权后的卸载程序解析 `{localappdata}` 为运行账户，多用户机器上可能指向错误的配置文件。
- **影响：** 低。属有意设计（机器级安装、用户数据保留/选择删除）。
- **置信度：** 95
- **建议：** 接受并文档化；若多用户正确性重要，显式解析待删除账户或加 `{code:…}` 守卫。

---

## 建议级（60-79，不计入发现）

- **A. 新闻 TabControl 内联 ControlTheme**（MainWindow.axaml L200-277，约 75 行）——符合令牌纪律且被契约测试固定，但与「MainWindow.axaml 只留外壳」的约定冲突；属有意为之，非缺陷。
- **B. `SettingsOptionsViewModel` L147-151 的 `_ => statusDetailModeCompact`**——"详细"移除后 `_` 分支成为事实上的 Compact 分支并吞掉非法值；可显式写出 `StatusDetailModes.Compact =>` 分支保持与其他选项一致。
- **C. BannerDot.AccessibleName 每页重建**（L402-411）——每次轮播推进对所有圆点重设同一字符串、触发 N 次变更通知；圆点名称不随页面变化，仅 `CarouselPageText` 需每页重算。
- **D. 删除 `docs/native-localization-migration-design.md`**——无悬挂引用，但该 ADR 中的文化回退链（`en-US→en→neutral` 等）与 `system culture` 快照语义未在 CLAUDE.md 完整保留，未来新增语言时决策记录缺失。
- **E. `news-tab` 样式命名/位置失真**——`RemoteContent.axaml` L43-62 的 `Button.news-tab` 现仅被日志查看器筛选按钮消费，新闻标签已改用原生 TabControl；类名与文件位置（RemoteContent）与其唯一消费者（Diagnostics）不匹配。
- **F. `MainWindowViewModel.IsBottomPanelVisible => true`**——恒真属性使 MultiBinding 退化；**v1.0.0 即存在**（非本次回归），可顺手折叠为直接绑定。
- **G. 覆盖率基线（coverage.ps1 的 .8443/.8899）**——本次移除大量代码后收敛趋势未在本地验证（本环境无法运行 Avalonia BuildServices 遥测任务导致构建失败）；CI 已在 v1.0.1-beta.1 发布流程通过该门槛，风险低，但建议发版前本地运行 `verify.ps1` 确认。

---

## 验证通过（显著正面项）

- **重组正确性：** `.slnx`、两个测试 csproj、全部脚本（build/release/coverage/verify/dev/test.ps1）、`.vscode/*`、CI 均指向新 `src/` 路径；根目录无遗留文件。
- **崩溃恢复移除完整：** 代码/测试中 `CrashRecoveryService`、`ModalKind.CrashRecovery`、`PreviousSessionCrashed`、`crashRecovery*` 资源键、`ShowCrashRecovery` 全部清除；`Program.RunSession` 的日志→运行→会话结束→释放顺序正确；事件订阅对称无孤儿。
- **设置向后兼容：** 旧 `statusDetailMode: "detailed"` 由 `NormalizeSettings` 归一化为 Compact，且有测试覆盖（`LauncherSettingsServiceTests` L180-202）。
- **XAML 令牌纪律：** 重构后的 MainWindow.axaml/Styles/RemoteContent 无裸色号/`Transparent`/裸图标尺寸/裸圆角；`MainWindow.Styles.axaml` 中的 `#33000000` 阴影与 `#00000000…A8000000` 渐变属约定允许的主题无关定义（v1.0.0 已有，非新增）；`UiStyleContractTests` 持续强制。
- **安装器迁移：** `.iss` 在 ISCC 7.1.0 下实际编译通过（`[Code]` 编译、版本信息正确）；`Build-Distribution.ps1` 版本门控与 define 安全校验合理；`AppMutex`（`Local\Cafe_Launcher_SI`）与 `Program.cs` 一致；NSIS 全部移除；契约测试与脚本/`.iss` 一致。
- **CI：** 升级后的 Action 版本均真实存在（checkout@v7、setup-dotnet@v6、upload-artifact@v7、gh-release@v3）；`choco install innosetup` 在 windows-latest 可行；预发布判定 `contains(ref_name,'-')` 正确匹配 `v1.0.1-beta.1`；`GitHubReleaseRepositorySlug` 与分发仓库一致。
- **版本/依赖：** `VersionPrefix=1.0.1-beta.1`、`AssemblyVersion/FileVersion=1.0.1.0` 与标签一致；本范围未改任何 `PackageReference`，全部与 PROJECT_CONVENTIONS 版本表一致；global.json（10.0.302 + latestFeature rollForward）自洽。
- **安全面：** 无新增密钥/令牌/明文 http 端点/弱化校验/敏感日志；`ApiConfig.AuthorizationSalt` 为 v1.0.0 逐字节相同的既有客户端签名盐；`AttachDeveloperTools()` 严格位于 `#if DEBUG`；`RemoteHttpUrlValidator` 等路径 0 内容变更。
- **测试健康：** 本机最近一次完整运行（2026-08-25 15:21）单元 1074 通过 / 0 失败、Headless 96/96 通过；新行为（横幅悬停暂停、PageSlide、ToastStackMotion 重排+禁用复位、安装器契约）均有命名测试；无新增硬编码 CJK 断言；`Tests`/`HeadlessTests` csproj 与 AssemblyInfo 转发一致。
- **轮播正确性：** `StartCarouselTimer` 先 `Stop` 后守卫，无双重启动/每次移动重建；`IsCarouselPaused` 仅在实际变化时通知。

---

## 优先级建议

1. **文档同步（低成本、高价值）：** 修正 README/CLAUDE.md 的崩溃恢复/`detailed` 残留（发现 1）。
2. **删除 3 个孤儿资源键**并更新 450→447 契约（发现 2）。
3. **补 `AutomationProperties.Name`** 到新闻行按钮（发现 3，无碍成本）。
4. **恢复会话生命周期测试**（发现 8），防止移除崩溃恢复后的路径再回归。
5. **单一化横幅交互状态源**（发现 5），并在窗口级补充焦点分支 headless 测试（发现 10）。
6. **清理 `ShellViewModel` 写死状态与 `SetOperationNote` 管线**（发现 4）。
7. **安装器加固**：旧版卸载路径身份校验（发现 6）；签名可排入后续发布计划（发现 7）。
8. **资源卫生**：释放 CTS/DispatcherTimer（发现 11）、专用预载 CTS（发现 12）。

## 审计方法

五个并行审计代理（架构、安全、测试、可维护性、依赖/性能/工具链）+ 交叉验证：全部发现均经源码级复核（`git diff/show`、直接阅读文件），关键项（孤儿资源键、`ShellViewModel` 写死状态、新闻按钮无 AutomationName、`.iss` 执行路径、裁剪头 007ff7b 机制）由主代理独立验证。未执行完整构建/测试（沙箱环境限制 Avalonia BuildServices 遥测任务写 `AppData\Local`）；引用的测试结果来自本机最近一次 TRX 运行。

---

## 修复记录（2026-08-25）

发现全部处理完毕，28 个文件修改；验证：Debug/Release（win-x64）构建 0 警告 0 错误，单元测试 1070 通过 / 0 失败 / 2 跳过（GamePathValidator 符号链接测试按设计跳过），Headless 99/99 通过，`Test-LocalizationContract.ps1` 通过。

| 发现 | 修复 | 变更文件 |
|---|---|---|
| 1 文档漂移 | README/CLAUDE.md 移除 `session.active`、`CrashRecoveryService`、崩溃恢复/会话恢复/`detailed` 描述，启动时序图改为会话起止日志 | `README.md`、`CLAUDE.md` |
| 2 孤儿资源键 | 从 4 个 resx 删除 `pauseCarousel`/`resumeCarousel`/`statusDetailModeDetailed`，重新生成 Designer（450→447），更新键数契约测试 | 4 个 `.resx`、`LauncherStrings.Designer.cs`、`ResxResourceContractTests.cs` |
| 3 新闻按钮无 AutomationName | 添加 `AutomationProperties.Name="{Binding Title}"` | `MainWindow.axaml` |
| 4 写死 ShellViewModel 状态 + OperationNote 管线 | 删除 5 个写死成员及其赋值/解析方法，整条 `IGameOperationJourneyHost.SetOperationNote` → `ErrorHandlingService.OperationNoteRequested` → `shell.OperationNote` 管线（含 ErrorHandlingOptions.OperationNoteKey）全部移除；受影响的 9 处测试同步更新 | `ShellViewModel.cs`、`ErrorHandlingService.cs`、`IGameOperationJourneyHost.cs`、`GameOperationJourney.cs`、`GameOperationsViewModel.cs`、`ShellLifecycle.cs`、`DebugViewModel.cs`、`ResourcePanelViewModel.cs` 及 6 个测试文件 |
| 5 横幅交互双通道 | 视觉 `active` 类改为绑定 VM 新属性 `IsBannerInteractionActive`；code-behind 只转发事件；指针退出时以 `hideControls` 语义同时复位视觉与暂停状态（保留 007ff7b 的“退出即隐藏”契约，且消除 VM 暂停锁死）；新增 2 个单元测试 + 1 个 headless 焦点测试 | `RemoteContentViewModel.cs`、`MainWindow.axaml`、`MainWindow.axaml.cs`、`RemoteContentViewModelTests.cs`、`MainWindowHeadlessTests.cs` |
| 6 未校验的旧版卸载执行路径 | `.iss` 执行前校验：卸载程序必须命名为 `Uninstall.exe`、必须存在、且与注册表 `InstallLocation` 一致（大小写/尾部斜杠归一化后比较）；任何不一致删除陈旧注册表项而不执行 | `Cafe.Launcher.Avalonia.iss`、`InstallerContractTests.cs`（含标记文件名双重引用 + 校验顺序断言） |
| 8 会话生命周期无测试 | 新增 `RunSession_WhenActionReturns_WritesSessionStartAndEnd` 与 `RunSession_WhenActionThrows_LogsCrashAndRethrows`（断言会话开始/结束/崩溃日志、异常重抛） | `DiagnosticsServicesTests.cs` |
| 10 焦点分支无窗口级测试 | 新增 `MainWindow_BannerControls_ShowWhileBannerStageIsFocused`（Tab 聚焦横幅 → 控件显示 + 轮播暂停） | `MainWindowHeadlessTests.cs` |
| 11 CTS 卫生 | 被替换/取消的 `carouselDelayCts` 与新增 `bannerPreloadCts` 在取消点与 Dispose 中释放；位图赋值续体增加 `disposed` 守卫（Avalonia `DispatcherTimer` 无 IDisposable，故仅停止+置空） | `RemoteContentViewModel.cs` |
| 12 重复 Apply 不取消预载 | `bannerPreloadCts` 在每次 `Apply` 顶部取消重建，预载线程走专用 token；`Apply` 入口保留 `ThrowIfCancellationRequested` 语义 | `RemoteContentViewModel.cs` |
| 安全建议（数据删除守卫） | `ShouldDeleteUserData` 增加 `(not UninstallSilent) and DeleteApplicationData` 双重守卫 | `Cafe.Launcher.Avalonia.iss` |

**未处理（说明）**：发现 7（安装包未签名）——需要发布者 Authenticode 证书，非仓库内代码可解决，建议列入发布流程后续项；发现 13（ISCC `UsedUserAreasWarning`）——管理员安装 + 按用户数据为有意设计，多用户机器上 `{localappdata}` 解析歧义已通过卸载提示与双重守卫缓解。建议级 A-F 均评估（BannerDot 每页重建已随发现 5 一并优化为仅语言/内容变化时重建；其余为风格/命名类，未改动）。
