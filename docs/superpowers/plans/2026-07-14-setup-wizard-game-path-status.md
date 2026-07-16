# 设置向导游戏路径状态实施计划

> **执行要求：** 使用 `superpowers:executing-plans` 逐项实施，并在每个任务完成后运行其中列出的验证命令。

**目标：** 在设置向导第二步首次自动填入原版启动器一致的默认游戏路径，异步显示该路径的安装状态，并只允许有效安装路径或可安装的空目标目录继续。

**架构：** `SetupWizardViewModel` 持有向导专用的路径检测状态，并复用现有 `GameInstallationPath` 与 `LocalInstallationStateStore`。每一次进入第二步或路径变更都取消上一轮读取，并以递增版本拒绝过期结果。视图仅绑定 ViewModel 暴露的状态文案和样式类；状态文件始终只读。

**技术栈：** .NET 10、Avalonia、CommunityToolkit.Mvvm、xUnit v3、Avalonia.Headless.XUnit。

## 全局约束

- 维持 `GameInstallationPath.GetDefaultGamePath()` 的既有实现，不重新实现原版的 `YostarGames\\BlueArchive_JP` 路径规则。
- 只调用 `LocalInstallationStateStore.ReadAsync(...)`；不得创建目录、写入 `manifest.json`、`game-launcher-config.json`，也不得读取或写入原版启动器的 `downloadPath`。
- 不新增设置字段、公共服务或外部契约。ViewModel 新增的成员只服务于既有向导视图。
- 每个新增 UI 文案必须同步写入 `Assets/Locales/en.json`、`zh-Hans.json`、`zh-Hant.json`、`ja.json`，并经由 `LocalizedStrings` 暴露。
- XAML 仅使用 `App.axaml` 已有的设计令牌和间距资源；状态颜色使用现有 `LauncherSuccessBrush`、`LauncherDangerBrush` 与 `LauncherAccentBrush`。
- 保持现有 920×560 向导尺寸、步骤锁定规则和保存行为不变。

## 任务 1：定义可绑定的路径状态和本地化文案

**文件：**
- 新建：`Features/SetupWizard/SetupWizardGamePathStatus.cs`
- 修改：`ViewModels/LocalizedStrings.cs`
- 修改：`Assets/Locales/en.json`
- 修改：`Assets/Locales/zh-Hans.json`
- 修改：`Assets/Locales/zh-Hant.json`
- 修改：`Assets/Locales/ja.json`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`

1. 先在 `LocalizationServiceTests.cs` 添加失败测试：加载四种语言并断言新键可由 `LocalizedStrings` 读取；再断言切换语言后新属性随 `LocalizationService.LanguageChanged` 更新。
2. 新建内部向导状态枚举 `SetupWizardGamePathStatus`，枚举值依次为 `NotSelected`、`Checking`、`AvailableForInstallation`、`ValidInstallation`、`CorruptedInstallation`、`Inaccessible`。该模型不改变持久化设置或文件格式。
3. 在四份语言文件中按现有 JSON 键的序数排序插入以下键，并提供对应语言的短状态文案：
   - `setupWizardGamePathAvailable`
   - `setupWizardGamePathChecking`
   - `setupWizardGamePathCorrupted`
   - `setupWizardGamePathInaccessible`
   - `setupWizardGamePathInstalled`
4. 在 `LocalizedStrings.cs` 增加同名 PascalCase 可观察属性，并在 `Apply()` 中从 `GetString(...)` 赋值，保持与既有 `SetupWizardGamePath*` 属性相同的生成和更新模式。
5. 运行新增的本地化测试；确认现有键一致性测试仍覆盖四份资源。

**验证：**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"
```

**提交：**

```text
feat(setup): 添加游戏路径状态本地化
```

## 任务 2：在向导 ViewModel 中执行默认路径填充和只读状态检测

**文件：**
- 修改：`ViewModels/SetupWizardViewModel.cs`
- 修改：`Services/ServiceConfiguration.cs`（仅确认现有 `LocalInstallationStateStore` 注册可被构造函数解析；若无需代码改动，不修改该文件）
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/SetupWizardStepItemTests.cs`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/DialogsViewModelTests.cs`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

1. 先在 `SetupWizardViewModelTests.cs` 添加失败测试，覆盖：
   - 首次由第一步进入第二步、且 `GamePath` 为空时，填入 `new GameInstallationPath().GetDefaultGamePath()` 的精确结果。
   - 已有路径与用户清空后的路径都不会在再次进入第二步时被覆盖。
   - 不存在的目标映射为 `AvailableForInstallation` 且第二步可继续。
   - 由 `LocalInstallationStateStore.CommitAsync(...)` 建立的完整状态映射为 `ValidInstallation` 且可继续。
   - 不完整状态文件映射为 `CorruptedInstallation` 且不可继续。
   - 以 `FileShare.None` 保持状态文件打开造成的 `LocalInstallationStateKind.IoFailure`，以及无法规范化的输入，均映射为 `Inaccessible` 且不可继续。
   - 检测进行时不可继续；最终状态到达后更新 `CanGoNext`。
   - 使用 `LocalInstallationStateStore` 现有的内部测试钩子阻塞旧路径的 `CommitAsync(...)` 并占用同一路径锁；随后修改为另一条路径，验证被取消的旧读取不会覆盖新路径的终态。
   使用临时目录创建测试路径；通过订阅 `PropertyChanged` 并等待 `GamePathStatus` 到达预期终态，而非使用固定延迟。
2. 将 `LocalInstallationStateStore` 注入 `SetupWizardViewModel` 构造函数。现有 `ServiceConfiguration.cs` 已注册该单例，因此无需新增服务注册；更新上述所有直接构造 `SetupWizardViewModel` 的测试调用以传入真实的 `LocalInstallationStateStore`。
3. 增加 `GamePathStatus`、`GamePathStatusText`、`IsGamePathChecking`、`IsGamePathReady` 和供 XAML 使用的状态样式布尔属性。状态文本在本地化语言变化后必须触发属性变更通知。
4. 为检测保留一个仅首次进入第二步使用的初始化标志。第一次 `Step == 1` 时，如果 `GamePath` 为空则设置 `GameInstallationPath.GetDefaultGamePath()`；之后绝不自动覆盖该属性。
5. 实现单一异步刷新入口：取消并释放前一轮 `CancellationTokenSource`，增加检测版本，空路径直接设为 `NotSelected`，其余输入先经 `GameInstallationPath.NormalizeGamePath(...)` 规范化，再设为 `Checking` 并调用 `LocalInstallationStateStore.ReadAsync(...)`。
6. 仅当取消令牌和版本仍匹配当前输入时提交结果。将 `NotInstalled` 映射为 `AvailableForInstallation`，`Valid` 映射为 `ValidInstallation`，`Corrupted` 映射为 `CorruptedInstallation`，`IoFailure` 与路径规范化异常映射为 `Inaccessible`。取消的任务不更新 UI 状态。
7. 让 `OnStepChanged` 在第二步触发初始化和检测；让 `OnGamePathChanged` 与目录选择成功后的赋值触发同一检测入口。更新 `CanGoNext`：仅第二步要求 `IsGamePathReady`，其余步骤保持现有规则。
8. 保持 `BuildSettings()` 中的规范化与已有保存逻辑不变，且不把状态字段写入 `LauncherSettings`。

**验证：**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardViewModelTests|FullyQualifiedName~SetupWizardStepItemTests|FullyQualifiedName~GameOperationsViewModelTests|FullyQualifiedName~DialogsViewModelTests|FullyQualifiedName~MainWindowViewModelTests"
```

**提交：**

```text
feat(setup): 检测向导游戏路径状态
```

## 任务 3：在第二步渲染状态行并建立样式契约

**文件：**
- 修改：`Views/SetupWizardOverlay.axaml`
- 修改：`Views/Styles/SetupWizard.axaml`
- 修改：`tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

1. 先在 `UiStyleContractTests.cs` 添加失败契约测试，断言第二步路径输入行后存在绑定 `Dialogs.SetupWizard.GamePathStatusText` 的状态控件、`AutomationProperties.Name` 使用该同一文案、以及状态样式仅引用三种既有画刷令牌。
2. 在 `SetupWizardOverlay.axaml` 的 `GamePath` 输入 Grid 后、既有空路径提示位置处添加单行状态区域。状态区域显示检查中和所有非空终态；未选择路径继续使用既有 `SetupWizardGamePathEmpty` 错误提示。
3. 为状态文本绑定 `GamePathStatusText`，并以绑定到 ViewModel 状态布尔属性的类或可见性区分：检查中使用 `LauncherAccentBrush`，可继续状态使用 `LauncherSuccessBrush`，损坏和不可访问使用 `LauncherDangerBrush`。不写入原始颜色值或间距值。
4. 在 `Views/Styles/SetupWizard.axaml` 添加向导专属状态类选择器，只复用现有 `caption` 排版与设计令牌，避免影响设置页和其他弹层。
5. 保持输入框、浏览按钮、底部按钮顺序、焦点循环和 920×560 布局不变；状态文本需允许四种语言的单行截断或换行，但不得挤压导航栏。

**验证：**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
```

**提交：**

```text
feat(setup): 显示向导路径检测结果
```

## 任务 4：更新无头 UI 回归测试

**文件：**
- 修改：`tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

1. 将现有第一步进入第二步后“路径为空且不能继续”的断言改为：默认路径已填入、状态行存在，并在检测完成后依据可继续状态启用下一步。
2. 使用测试临时目录构造一个不存在的目标，验证状态行显示可安装结果且“下一步”可用。
3. 在临时游戏目录创建不完整状态文件，验证状态行显示损坏结果且“下一步”不可用。
4. 将语言切换到四种已有语言，验证状态行使用 `LocalizedStrings` 提供的文本，导航和自动化名称仍可访问。
5. 保持所有文件操作位于测试临时目录；不得读写实际安装目录或原版启动器目录。

**验证：**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
```

**提交：**

```text
test(setup): 覆盖向导路径状态界面
```

## 任务 5：完整回归与兼容性复核

**文件：** 无新增代码文件；仅在前述实现发现缺口时做最小修正。

1. 复核 `GameInstallationPath.GetDefaultGamePath()` 输出仍等价于原版 `request-default-download-path` 经 `checkPath` 后的目录规则。
2. 复核新代码中只有 `LocalInstallationStateStore.ReadAsync(...)` 调用，没有 `CommitAsync(...)`、状态文件写入或原版 `downloadPath` 访问。
3. 执行向导单元测试、样式契约、无头 UI、全量测试和 Debug 构建。
4. 只有所有命令成功后，检查工作树并提交遗留的最小修正；提交信息仍使用 Conventional Commits。

**验证：**

```powershell
.\test.ps1
.\build.ps1
```

**提交：**

```text
test(setup): 验证向导路径状态回归
```
