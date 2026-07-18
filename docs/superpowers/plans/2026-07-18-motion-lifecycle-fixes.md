# 动效生命周期修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为覆盖层和 Toast 补齐可取消的退出生命周期，阻止自动轮播切换到未加载横幅，并在窗口激活时刷新系统动效偏好。

**Architecture:** 使用一个 Avalonia 附加行为管理“业务已关闭但视觉仍在退出”的短暂状态，避免修改每个对话框命令；ToastHost 使用通知自身的退出状态延迟集合删除。轮播在统一的单步前进方法中检查下一项图片状态，系统偏好由窗口激活事件显式触发 ViewModel 刷新。

**Tech Stack:** .NET 10、C#、Avalonia 12.0.5、CommunityToolkit.Mvvm 8.4.2、xUnit v3、Avalonia.Headless.XUnit。

## Global Constraints

- 不修改 `Controls/LoadingOverlay.axaml`、`Controls/LoadingOverlay.axaml.cs` 或其行为。
- 保留完整动态效果下现有入场持续时间、位移和缓动。
- 减少动态效果下退出必须立即完成，不引入人为等待。
- 不新增依赖、用户配置或 Windows 消息钩子。
- 保留工作区已有未提交改动；重叠文件只追加本计划所需的最小差异。
- 每个行为改动遵循 RED → GREEN；测试项目编译阻塞先做最小修复。

---

### Task 1: 恢复当前测试项目编译

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs:459`

**Interfaces:**
- Consumes: `SetupWizardViewModel(LocalizationService, GameInstallationPath, LocalInstallationStateStore, LocalDiagnostics)`。
- Produces: 可编译的测试工程，不改变设置向导生产行为。

- [ ] **Step 1: 运行窄测试并记录现有编译失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SetupWizardViewModelTests"
```

Expected: 编译失败 `CS7036`，指出第 459 行缺少 `diagnostics`。

- [ ] **Step 2: 为测试辅助构造补齐现有诊断依赖**

在第 459 行的构造调用中使用与该文件其他测试相同的内部测试构造函数：

```csharp
var vm = new SetupWizardViewModel(
    localizer,
    new GameInstallationPath(),
    new LocalInstallationStateStore(),
    new LocalDiagnostics())
```

`LocalDiagnostics()` 在测试程序集可见，并自行使用临时日志目录；本任务不创建第二套日志生命周期。

- [ ] **Step 3: 重新运行窄测试**

Run: 与 Step 1 相同。

Expected: 工程成功编译，`SetupWizardViewModelTests` 全部通过。

- [ ] **Step 4: 检查差异边界**

Run:

```powershell
git diff -- tests/Cafe.Launcher.Avalonia.Tests/SetupWizardViewModelTests.cs
```

Expected: 除用户原有改动外，本任务只增加缺失的 `LocalDiagnostics` 参数。

---

### Task 2: 建立可取消的覆盖层退出行为

**Files:**
- Create: `Controls/MotionVisibility.cs`
- Create: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MotionVisibilityTests.cs`
- Modify: `Views/MainWindow.Styles.axaml:446-528`
- Modify: `Views/MainWindowDialogsOverlay.axaml`
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Modify: `Views/MainWindowLogViewerOverlay.axaml`
- Modify: `Views/SetupWizardOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

**Interfaces:**
- Produces: `MotionVisibility.IsOpen` 与 `MotionVisibility.IsMotionEnabled` 两个附加属性。
- Produces: `motion-enter`、`motion-exit` 样式类和实际 `Control.IsVisible` 生命周期。
- Consumes: `AnimationTimings.ExitAnimationDuration`。

- [ ] **Step 1: 写可见性行为的失败 Headless 测试**

新增测试覆盖三个行为：

```csharp
[AvaloniaFact]
public async Task SetIsOpen_MotionEnabled_KeepsVisibleUntilExitCompletes()
{
    var control = new Border();
    MotionVisibility.SetIsMotionEnabled(control, true);
    MotionVisibility.SetIsOpen(control, true);

    MotionVisibility.SetIsOpen(control, false);

    Assert.True(control.IsVisible);
    Assert.Contains("motion-exit", control.Classes);
    await MotionVisibility.WaitForPendingExitAsync(control);
    Assert.False(control.IsVisible);
}

[AvaloniaFact]
public void SetIsOpen_MotionReduced_HidesImmediately()
{
    var control = new Border();
    MotionVisibility.SetIsMotionEnabled(control, false);
    MotionVisibility.SetIsOpen(control, true);

    MotionVisibility.SetIsOpen(control, false);

    Assert.False(control.IsVisible);
    Assert.DoesNotContain("motion-exit", control.Classes);
}

[AvaloniaFact]
public async Task SetIsOpen_ReopenedDuringExit_RemainsVisible()
{
    var control = new Border();
    MotionVisibility.SetIsMotionEnabled(control, true);
    MotionVisibility.SetIsOpen(control, true);
    MotionVisibility.SetIsOpen(control, false);
    MotionVisibility.SetIsOpen(control, true);

    await MotionVisibility.WaitForPendingExitAsync(control);

    Assert.True(control.IsVisible);
    Assert.Contains("motion-enter", control.Classes);
}
```

`WaitForPendingExitAsync` 为 `internal` 测试缝，返回该控件当前退出任务；生产代码不调用它。

- [ ] **Step 2: 运行测试确认 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MotionVisibilityTests"
```

Expected: 编译失败，因为 `MotionVisibility` 尚不存在。

- [ ] **Step 3: 实现最小附加行为**

`MotionVisibility.cs` 使用 `ConditionalWeakTable<Control, VisibilityState>` 保存每个控件的 `CancellationTokenSource` 与当前任务。核心接口：

```csharp
public sealed class MotionVisibility : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<MotionVisibility, Control, bool>("IsOpen");

    public static readonly AttachedProperty<bool> IsMotionEnabledProperty =
        AvaloniaProperty.RegisterAttached<MotionVisibility, Control, bool>("IsMotionEnabled");

    public static bool GetIsOpen(Control control) => control.GetValue(IsOpenProperty);
    public static void SetIsOpen(Control control, bool value) => control.SetValue(IsOpenProperty, value);
    public static bool GetIsMotionEnabled(Control control) => control.GetValue(IsMotionEnabledProperty);
    public static void SetIsMotionEnabled(Control control, bool value) =>
        control.SetValue(IsMotionEnabledProperty, value);

    internal static Task WaitForPendingExitAsync(Control control) =>
        States.TryGetValue(control, out var state) ? state.PendingTask : Task.CompletedTask;
}
```

状态更新规则：打开时取消旧令牌、`IsVisible=true`、清除 `motion-exit` 并添加 `motion-enter`；关闭且启用动效时添加 `motion-exit`、等待 `ExitAnimationDuration` 后验证未取消再隐藏；关闭且减少动效时立即隐藏。捕获的 `OperationCanceledException` 只在对应令牌取消时吞掉。

- [ ] **Step 4: 运行 Headless 测试确认 GREEN**

Run: 与 Step 2 相同。

Expected: 3 个测试通过。

- [ ] **Step 5: 先写 XAML 契约失败测试**

在 `UiStyleContractTests` 中断言每个 `motion-overlay` 根节点：

```csharp
Assert.Null(element.Attribute("IsVisible"));
Assert.NotNull(element.Attribute(XName.Get("IsOpen", MotionVisibilityNamespace)));
Assert.Equal("{Binding IsMotionEnabled}",
    element.Attribute(XName.Get("IsMotionEnabled", MotionVisibilityNamespace))?.Value);
```

并断言存在：

```text
Grid.motion-overlay.motion-enabled.motion-exit
Grid.motion-overlay.motion-enabled.motion-exit > Border.motion-surface
```

- [ ] **Step 6: 运行 UI 契约测试确认 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
```

Expected: FAIL，现有 XAML 仍直接绑定 `IsVisible` 且没有退出样式。

- [ ] **Step 7: 接入覆盖层并增加退出样式**

将每个覆盖层根节点：

```xml
IsVisible="{Binding Dialogs.IsNoticeDialogVisible}"
Classes.motion-enter="{Binding Dialogs.IsNoticeDialogVisible}"
```

替换为：

```xml
controls:MotionVisibility.IsOpen="{Binding Dialogs.IsNoticeDialogVisible}"
controls:MotionVisibility.IsMotionEnabled="{Binding IsMotionEnabled}"
```

其他可见属性按各自原绑定照搬。没有 `controls` 命名空间的视图补充：

```xml
xmlns:controls="using:Cafe.Launcher.Avalonia.Controls"
```

新增退出样式：

```xml
<Style Selector="Grid.motion-overlay.motion-enabled.motion-exit">
    <Style.Animations>
        <Animation Duration="0:0:0.15" FillMode="Forward" Easing="QuadraticEaseIn">
            <KeyFrame Cue="0%"><Setter Property="Opacity" Value="1"/></KeyFrame>
            <KeyFrame Cue="100%"><Setter Property="Opacity" Value="0"/></KeyFrame>
        </Animation>
    </Style.Animations>
</Style>
<Style Selector="Grid.motion-overlay.motion-enabled.motion-exit > Border.motion-surface">
    <Style.Animations>
        <Animation Duration="0:0:0.15" FillMode="Forward" Easing="QuadraticEaseIn">
            <KeyFrame Cue="0%">
                <Setter Property="Opacity" Value="1"/>
                <Setter Property="TranslateTransform.Y" Value="0"/>
            </KeyFrame>
            <KeyFrame Cue="100%">
                <Setter Property="Opacity" Value="0"/>
                <Setter Property="TranslateTransform.Y" Value="6"/>
            </KeyFrame>
        </Animation>
    </Style.Animations>
</Style>
```

- [ ] **Step 8: 运行 Headless 与 UI 契约测试**

Run: Step 4 与 Step 6 两条命令。

Expected: 全部通过。

---

### Task 3: 统一 Toast 手动与自动退出

**Files:**
- Modify: `Services/ToastService.cs`
- Modify: `ViewModels/ToastHostViewModel.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Views/Styles/Toast.axaml`
- Modify: `Views/MainWindowToastOverlay.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/ToastHostViewModelTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

**Interfaces:**
- Produces: `ToastNotification.IsExiting`。
- Produces: `ToastHostViewModel.ApplyMotionPreference(bool reduceMotion)`。
- Consumes: `AnimationTimings.ExitAnimationDuration` 和已有 `delayAsync`。

- [ ] **Step 1: 写 Toast 退出失败测试**

使用可控 `TaskCompletionSource` 延时函数，新增：

```csharp
[Fact]
public async Task DismissToastCommand_WithMotion_MarksExitingBeforeRemoval()
{
    var exitDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var viewModel = CreateViewModel((_, _) => exitDelay.Task);
    viewModel.ApplyMotionPreference(false);
    var notification = RaiseToast();
    await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

    viewModel.DismissToastCommand.Execute(notification.Id);

    Assert.True(notification.IsExiting);
    Assert.Contains(notification, viewModel.ActiveToasts);
    exitDelay.SetResult();
    await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
}

[Fact]
public async Task DismissToastCommand_WithReducedMotion_RemovesImmediately()
{
    using var viewModel = CreateViewModel();
    viewModel.ApplyMotionPreference(true);
    var notification = RaiseToast();
    await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 1);

    viewModel.DismissToastCommand.Execute(notification.Id);

    await WaitUntilAsync(() => viewModel.ActiveToasts.Count == 0);
    Assert.False(notification.IsExiting);
}
```

再添加自动超时进入同一退出状态及重复关闭幂等测试。

- [ ] **Step 2: 运行测试确认 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~ToastHostViewModelTests"
```

Expected: 编译失败，缺少 `IsExiting` 与 `ApplyMotionPreference`。

- [ ] **Step 3: 实现通知状态与统一退出方法**

将通知改为可观察对象：

```csharp
public sealed partial class ToastNotification : ObservableObject
{
    [ObservableProperty]
    private bool isExiting;
}
```

ToastHost 增加 `isMotionReduced`，并让手动与自动路径调用：

```csharp
private async Task DismissToastAsync(ToastNotification notification, CancellationToken cancellationToken)
{
    if (!ActiveToasts.Contains(notification) || notification.IsExiting)
    {
        return;
    }

    if (!isMotionReduced)
    {
        notification.IsExiting = true;
        await delayAsync(AnimationTimings.ExitAnimationDuration, cancellationToken);
    }

    await invokeOnUiAsync(() => ActiveToasts.Remove(notification));
}
```

所有集合查询、状态更新和删除都通过 `invokeOnUiAsync` 落在 UI 线程；上述片段在实现中拆成一次 UI 调用读取/标记和一次 UI 调用删除。`MainWindowViewModel.ApplyMotionSettings` 同时调用 `Toasts.ApplyMotionPreference(IsMotionReduced)`。

- [ ] **Step 4: 增加并绑定退出样式**

Toast 卡片添加：

```xml
Classes.motion-exit="{Binding IsExiting}"
```

样式：

```xml
<Style Selector="Border.toast-card.motion-enabled.motion-exit">
    <Style.Animations>
        <Animation Duration="0:0:0.15" FillMode="Forward" Easing="QuadraticEaseIn">
            <KeyFrame Cue="0%">
                <Setter Property="Opacity" Value="1"/>
                <Setter Property="TranslateTransform.Y" Value="0"/>
            </KeyFrame>
            <KeyFrame Cue="100%">
                <Setter Property="Opacity" Value="0"/>
                <Setter Property="TranslateTransform.Y" Value="4"/>
            </KeyFrame>
        </Animation>
    </Style.Animations>
</Style>
```

选择器需排除正在退出的 Toast 入场样式：`Border.toast-card.motion-enabled:not(.motion-exit)`。

- [ ] **Step 5: 运行 Toast 与 UI 契约测试确认 GREEN**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~ToastHostViewModelTests|FullyQualifiedName~UiStyleContractTests"
```

Expected: 全部通过。

---

### Task 4: 阻止自动轮播切换到加载中的横幅

**Files:**
- Modify: `ViewModels/RemoteContentViewModel.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/RemoteContentViewModelTests.cs`

**Interfaces:**
- Produces: `internal bool TryAdvanceCarousel()`。
- Consumes: `RemoteContentItem.IsImageLoading`；成功和失败均表现为 `false`。

- [ ] **Step 1: 写轮播门槛失败测试**

```csharp
[Fact]
public void TryAdvanceCarousel_NextImageLoading_KeepsCurrentBanner()
{
    using var context = CreateContext();
    context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);

    var advanced = context.ViewModel.TryAdvanceCarousel();

    Assert.False(advanced);
    Assert.Equal(0, context.ViewModel.CarouselSelectedIndex);
}

[Theory]
[InlineData(false)]
[InlineData(true)]
public void TryAdvanceCarousel_NextImageTerminal_Advances(bool failed)
{
    using var context = CreateContext();
    context.ViewModel.Apply(CreateBannerState(2, loop: true), new LauncherSettings(), CancellationToken.None);
    if (failed) context.ViewModel.BannerItems[1].MarkImageLoadFailed();
    else context.ViewModel.BannerItems[1].MarkImageLoaded();

    var advanced = context.ViewModel.TryAdvanceCarousel();

    Assert.True(advanced);
    Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~RemoteContentViewModelTests"
```

Expected: 编译失败，缺少 `TryAdvanceCarousel`。

- [ ] **Step 3: 提取并接入单步前进方法**

```csharp
internal bool TryAdvanceCarousel()
{
    if (BannerItems.Count == 0)
    {
        return false;
    }

    var next = (CarouselSelectedIndex + 1) % BannerItems.Count;
    if (BannerItems[next].IsImageLoading)
    {
        return false;
    }

    CarouselSelectedIndex = next;
    return true;
}
```

`DispatcherTimer.Tick` 只调用 `TryAdvanceCarousel()`，不改变手动导航命令。

- [ ] **Step 4: 运行测试确认 GREEN**

Run: 与 Step 2 相同。

Expected: 全部通过。

---

### Task 5: 在窗口激活时刷新系统动效偏好

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Views/MainWindow.axaml.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Produces: `MainWindowViewModel.RefreshSystemMotionPreference()`。
- Consumes: `Settings.Editor.GetSavedSnapshot()` 与 `WindowsAnimationSettingsProvider.GetWindowsAnimationsEnabled()`。

- [ ] **Step 1: 写系统偏好刷新失败测试**

让测试 provider 的读取委托返回可变值并统计次数：

```csharp
[Fact]
public async Task RefreshSystemMotionPreference_SystemMode_ReevaluatesEffectiveMotion()
{
    var enabled = true;
    var snapshot = CreateSnapshot();
    snapshot.Settings.MotionMode = MotionModes.System;
    using var viewModel = await CreateViewModelAsync(
        new CountingCoreService(snapshot),
        windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => (true, enabled)));
    await viewModel.InitializeAsync();
    Assert.False(viewModel.IsMotionReduced);
    enabled = false;

    viewModel.RefreshSystemMotionPreference();

    Assert.True(viewModel.IsMotionReduced);
}

[Theory]
[InlineData(MotionModes.Full)]
[InlineData(MotionModes.Reduced)]
public async Task RefreshSystemMotionPreference_ExplicitMode_DoesNotReadWindows(string mode)
{
    var reads = 0;
    var snapshot = CreateSnapshot();
    snapshot.Settings.MotionMode = mode;
    using var viewModel = await CreateViewModelAsync(
        new CountingCoreService(snapshot),
        windowsAnimationSettingsProvider: new WindowsAnimationSettingsProvider(() => { reads++; return (true, true); }));
    await viewModel.InitializeAsync();
    var readsAfterInitialize = reads;

    viewModel.RefreshSystemMotionPreference();

    Assert.Equal(readsAfterInitialize, reads);
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected: 编译失败，缺少 `RefreshSystemMotionPreference`。

- [ ] **Step 3: 实现刷新和无变化短路**

```csharp
public void RefreshSystemMotionPreference()
{
    var settings = Settings.Editor.GetSavedSnapshot();
    if (settings.MotionMode != MotionModes.System)
    {
        return;
    }

    ApplyMotionSettings(settings);
}
```

`ApplyMotionSettings` 使用 `motionSettingsApplied` 区分“首次应用”和“值未变化”，首次总是同步 RemoteContent 与 ToastHost，后续相同值直接返回。

- [ ] **Step 4: 从窗口激活事件调用刷新**

在 `MainWindow` 构造函数注册：

```csharp
Activated += OnActivated;
```

处理器：

```csharp
private void OnActivated(object? sender, EventArgs e)
{
    (DataContext as MainWindowViewModel)?.RefreshSystemMotionPreference();
}
```

- [ ] **Step 5: 运行测试确认 GREEN**

Run: 与 Step 2 相同。

Expected: 全部通过。

---

### Task 6: 完整验证与范围审计

**Files:**
- Verify only: all files above

**Interfaces:**
- Produces: 可复现的验证证据和不包含 `LoadingOverlay` 的最终差异。

- [ ] **Step 1: 确认未修改 LoadingOverlay**

Run:

```powershell
git diff -- Controls/LoadingOverlay.axaml Controls/LoadingOverlay.axaml.cs
```

Expected: 只显示任务开始前已经存在的用户差异；本实现没有新增差异。用任务开始时保存的 diff 对比确认。

- [ ] **Step 2: 运行本次聚焦测试**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~ToastHostViewModelTests|FullyQualifiedName~RemoteContentViewModelTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~UiStyleContractTests"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MotionVisibilityTests|FullyQualifiedName~OverlayFocusBehaviorTests"
```

Expected: 两条命令均为 0 失败。

- [ ] **Step 3: 运行 UI 验证脚本**

Run:

```powershell
.\dev.ps1 ui
```

Expected: UI 样式契约与 Headless UI 测试通过。

- [ ] **Step 4: 运行完整验证**

Run:

```powershell
.\verify.ps1
```

Expected: Debug build、覆盖率和 Release build 全部通过；若用户正在运行的 Debug 实例继续锁定输出文件，使用独立 `--artifacts-path` 重跑并明确报告环境锁，不关闭用户进程。

- [ ] **Step 5: 检查最终差异**

Run:

```powershell
git diff --check
git status --short
git diff --stat
```

Expected: 无空白错误；只包含计划文件、动效实现/测试，以及任务开始前已有的用户改动。
