# P0～P2 Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 收拢 ViewModel 协调 seam，增加可覆盖 Windows 设置的减少动态效果模式，并展示精确的磁盘空间与安装校验重试状态。

**Architecture:** P0 使用已应用快照和明确事件替代子 ViewModel 的可空读取/回调委托，不新增单 adapter interface。P1 用持久化 `motionMode`、Win32 平台读取和纯解析函数形成小 interface。P2 扩展现有进度值对象和阶段映射，不暴露失败文件名。

**Tech Stack:** .NET 10、C#、Avalonia 12.0.4、CommunityToolkit.Mvvm 8.4.2、xUnit 2.9.3、Avalonia Headless XUnit 3.2.2、PowerShell。

---

## 文件结构

### 新建

- `Services/WindowsAnimationSettingsProvider.cs` — 读取 `SPI_GETCLIENTAREAANIMATION`。
- `Services/MotionSettingsResolver.cs` — 解析 `system`、`full`、`reduced` 的有效状态。

### 修改

- `Models/LauncherStateModels.cs` — `MotionModes`、`MotionMode`、进度与结果字段、刷新模式。
- `Services/LauncherSettingsService.cs` — 规范化 `motionMode`。
- `Services/GameDownloadService.cs` — 磁盘和安装校验进度。
- `Services/DiskSpaceService.cs` — 提供可计数的内部测试读取 seam。
- `Services/ServiceConfiguration.cs` — 注册 Windows 动画设置读取 module。
- `Services/LocalizationService.cs` — 三语言动态效果和下载状态文本。
- `ViewModels/GameOperationsViewModel.cs` — 保存快照、发布刷新和最小化事件、映射新进度。
- `ViewModels/MainWindowViewModel.cs` — 订阅协调事件并解析有效动态效果。
- `ViewModels/ResourcePanelViewModel.cs` — 使用已应用设置。
- `ViewModels/WindowChromeViewModel.cs` — 使用已保存设置并发布窗口事件。
- `ViewModels/RemoteContentViewModel.cs` — 根据有效动态效果控制轮播和过渡时长。
- `ViewModels/SettingsOptionsViewModel.cs` — 动态效果选项。
- `ViewModels/SettingsViewModel.cs` — 保存后应用有效动态效果。
- `Views/MainWindow.axaml` — 绑定轮播过渡时长。
- `Views/MainWindow.axaml.cs` — 订阅原生窗口事件。
- `Views/MainWindow.Styles.axaml` — 只对允许动态效果的通知执行淡入。
- `Views/MainWindowSettingsOverlay.axaml` — 动态效果下拉框。
- `Views/MainWindowToastOverlay.axaml` — 通知动态效果 class。

### 测试

- `tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/ResourcePanelUidServiceTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/WindowChromeViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/SettingsEditorTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/RemoteContentViewModelTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/MotionSettingsResolverTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`
- `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

## P0 — 收拢协调 seam

### Task 1: `GameOperationsViewModel` 使用已应用快照

**Files:**
- Modify: `Models/LauncherStateModels.cs`
- Modify: `ViewModels/GameOperationsViewModel.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`

- [ ] **Step 1: 写入失败测试**

增加测试，先调用 `ApplySnapshot(snapshot)`，不设置任何父级委托，再执行启动、修复和卸载命令：

```csharp
[Fact]
public async Task Commands_UseMostRecentlyAppliedSnapshot()
{
    var context = CreateContext();
    var snapshot = ReadySnapshot("C:\\Game");
    context.ViewModel.ApplySnapshot(snapshot);
    context.Backend.LaunchResult = new GameLaunchResult { Success = true };

    await context.ViewModel.StartGameCommand.ExecuteAsync(null);

    Assert.Same(snapshot, context.Backend.LastLaunchSnapshot);
}
```

增加刷新模式测试：

```csharp
[Fact]
public async Task InstallCompletion_RequestsRefreshWithoutPersistedResume()
{
    var context = CreateContext();
    context.ViewModel.ApplySnapshot(UpdateSnapshot());
    GameOperationsRefreshMode? mode = null;
    context.ViewModel.RefreshRequested += requested =>
    {
        mode = requested;
        return Task.CompletedTask;
    };

    await context.ViewModel.InstallOrUpdateCommand.ExecuteAsync(null);

    Assert.Equal(GameOperationsRefreshMode.SkipPersistedResume, mode);
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameOperationsViewModelTests"
```

Expected: FAIL，`RefreshRequested`、`GameOperationsRefreshMode` 和内部快照行为尚不存在。

- [ ] **Step 3: 增加刷新模式**

在 `Models/LauncherStateModels.cs` 增加：

```csharp
public enum GameOperationsRefreshMode
{
    Normal,
    SkipPersistedResume
}
```

- [ ] **Step 4: 替换可空委托**

在 `GameOperationsViewModel` 中增加：

```csharp
private LauncherStatusSnapshot? currentSnapshot;

public event Func<GameOperationsRefreshMode, Task>? RefreshRequested;
public event Action? MinimizeRequested;

public void ApplySnapshot(LauncherStatusSnapshot snapshot)
{
    currentSnapshot = snapshot;
    // 保留现有面板映射。
}

private async Task RequestRefreshAsync(GameOperationsRefreshMode mode)
{
    if (RefreshRequested is null)
    {
        return;
    }

    foreach (Func<GameOperationsRefreshMode, Task> handler in RefreshRequested.GetInvocationList())
    {
        await handler(mode);
    }
}
```

删除 `GetSnapshot`、`RequestRefreshAsync`、`RequestRefreshAfterPersistedResumeAsync`、
`ApplySnapshotAsync`、`MinimizeWindow`。全部快照读取改为 `currentSnapshot`；成功启动调用
`MinimizeRequested?.Invoke()`；刷新点调用新的 `RequestRefreshAsync()`。

- [ ] **Step 5: 验证转绿**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameOperationsViewModelTests"
```

Expected: PASS。

- [ ] **Step 6: 提交**

```powershell
git add -- Models/LauncherStateModels.cs ViewModels/GameOperationsViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs
git commit -m "refactor(viewmodel): 让游戏操作使用已应用快照"
```

### Task 2: 主窗口集中处理刷新请求

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: 写入失败测试**

```csharp
[Fact]
public async Task OperationRefreshRequest_WhenSkipMode_DoesNotResumePersistedDownload()
{
    using var context = CreateContext();
    await context.ViewModel.InitializeAsync();
    context.OperationsBackend.ResetResumeCount();
    context.ViewModel.Operations.ApplySnapshot(UpdateSnapshot());
    context.OperationsBackend.InstallOrUpdateResult =
        new GameOperationResult { Success = true };

    await context.ViewModel.Operations.InstallOrUpdateCommand.ExecuteAsync(null);

    Assert.Equal(0, context.OperationsBackend.ResumeInvocationCount);
}
```

测试通过执行真实命令触发事件，不增加测试专用生产入口。

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected: FAIL，主窗口尚未订阅新事件。

- [ ] **Step 3: 实现集中处理**

在 `WireChildren()` 中：

```csharp
Operations.RefreshRequested += HandleOperationsRefreshRequestedAsync;
```

增加：

```csharp
private async Task HandleOperationsRefreshRequestedAsync(GameOperationsRefreshMode mode)
{
    if (mode == GameOperationsRefreshMode.SkipPersistedResume)
    {
        skipNextPersistedResume = true;
    }

    await RefreshAsync();
}
```

在 `Dispose()` 中解除订阅。删除旧的四个操作委托接线。

- [ ] **Step 4: 验证转绿**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ServiceConfigurationTests"
```

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add -- ViewModels/MainWindowViewModel.cs tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/ServiceConfigurationTests.cs
git commit -m "refactor(viewmodel): 集中处理游戏操作刷新"
```

### Task 3: 收拢资源面板与窗口动作

**Files:**
- Modify: `ViewModels/ResourcePanelViewModel.cs`
- Modify: `ViewModels/WindowChromeViewModel.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Views/MainWindow.axaml.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/WindowChromeViewModelTests.cs`

- [ ] **Step 1: 写入失败测试**

```csharp
[Fact]
public async Task OpenResourcePanel_UsesMostRecentlyAppliedSettings()
{
    var context = CreateContext();
    context.ViewModel.ApplySettings(new LauncherSettings
    {
        ProxyMode = ProxyModes.System,
        PatchUrlGroup = PatchUrlGroups.Cafe
    });

    await context.ViewModel.OpenResourcePanelCommand.ExecuteAsync(null);

    Assert.Equal(ProxyModes.System, context.ResourcePanelService.LastProxyMode);
}
```

窗口事件测试：

```csharp
[Fact]
public void MinimizeCommand_RaisesMinimizeRequestedOnce()
{
    var context = CreateContext();
    var count = 0;
    context.ViewModel.MinimizeRequested += () => count++;

    context.ViewModel.MinimizeCommand.Execute(null);

    Assert.Equal(1, count);
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~WindowChromeViewModelTests|FullyQualifiedName~MainWindowViewModelTests"
```

Expected: FAIL。

- [ ] **Step 3: 实现资源面板设置应用**

```csharp
private string proxyMode = ProxyModes.Direct;
private string patchUrlGroup = PatchUrlGroups.Official;

public void ApplySettings(LauncherSettings settings)
{
    proxyMode = settings.ProxyMode;
    patchUrlGroup = settings.PatchUrlGroup;
}
```

删除 `GetProxyMode`、`GetPatchUrlGroup`，调用处使用字段。

- [ ] **Step 4: 实现窗口事件**

`WindowChromeViewModel` 增加：

```csharp
public event Action? MinimizeRequested;
public event Action? RestoreRequested;
public event Action? CloseRequested;
```

命令发布事件；打开设置使用：

```csharp
settings.LoadFromSnapshot(settings.Editor.GetSavedSnapshot());
```

`MainWindow.axaml.cs` 订阅窗口事件和 `Operations.MinimizeRequested`，在关闭时解除订阅。

- [ ] **Step 5: 应用资源面板设置**

在 `MainWindowViewModel.ApplySnapshotAsync()` 中调用：

```csharp
ResourcePanel.ApplySettings(snapshot.Settings);
```

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~WindowChromeViewModelTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ServiceConfigurationTests"
git add -- ViewModels/ResourcePanelViewModel.cs ViewModels/WindowChromeViewModel.cs ViewModels/MainWindowViewModel.cs Views/MainWindow.axaml.cs tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/WindowChromeViewModelTests.cs
git commit -m "refactor(viewmodel): 收拢资源设置与窗口动作"
```

Expected: PASS。

## P1 — 减少动态效果

### Task 4: 持久化 `motionMode`

**Files:**
- Modify: `Models/LauncherStateModels.cs`
- Modify: `Services/LauncherSettingsService.cs`
- Modify: `Services/LocalizationService.cs`
- Modify: `ViewModels/SettingsOptionsViewModel.cs`
- Modify: `Views/MainWindowSettingsOverlay.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/SettingsEditorTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`

- [ ] **Step 1: 写入设置红灯测试**

```csharp
[Theory]
[InlineData(MotionModes.System)]
[InlineData(MotionModes.Full)]
[InlineData(MotionModes.Reduced)]
public async Task SaveAndRead_PreservesMotionMode(string motionMode)
{
    var service = CreateService();
    await service.SaveAsync(new LauncherSettings { MotionMode = motionMode });

    var result = await service.ReadAsync();

    Assert.Equal(motionMode, result.MotionMode);
}

[Fact]
public async Task Read_WhenMotionModeIsInvalid_NormalizesToSystem()
{
    await File.WriteAllTextAsync(SettingsPath, """{"motionMode":"invalid"}""");
    Assert.Equal(MotionModes.System, (await CreateService().ReadAsync()).MotionMode);
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~LauncherSettingsServiceTests|FullyQualifiedName~SettingsEditorTests"
```

Expected: FAIL，常量和属性不存在。

- [ ] **Step 3: 实现模型和规范化**

```csharp
public static class MotionModes
{
    public const string System = "system";
    public const string Full = "full";
    public const string Reduced = "reduced";
}
```

`LauncherSettings` 增加：

```csharp
[ObservableProperty]
[property: JsonPropertyName("motionMode")]
private string motionMode = MotionModes.System;
```

规范化只接受三个精确值，其他值设为 `MotionModes.System`。

- [ ] **Step 4: 增加本地化和下拉框**

三语言增加精确键：

```text
motionMode
motionModeDescription
motionModeSystem
motionModeFull
motionModeReduced
```

`SettingsOptionsViewModel` 增加 `ObservableCollection<SettingOption> MotionMode`，
并在语言刷新时重建三个选项。XAML 使用 `SelectedValue="{Binding Settings.Editor.Current.MotionMode}"`。

- [ ] **Step 5: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~LauncherSettingsServiceTests|FullyQualifiedName~SettingsEditorTests|FullyQualifiedName~LocalizationServiceTests|FullyQualifiedName~UiStyleContractTests"
git add -- Models/LauncherStateModels.cs Services/LauncherSettingsService.cs Services/LocalizationService.cs ViewModels/SettingsOptionsViewModel.cs Views/MainWindowSettingsOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/LauncherSettingsServiceTests.cs tests/Cafe.Launcher.Avalonia.Tests/SettingsEditorTests.cs tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs
git commit -m "feat(accessibility): 增加动态效果模式设置"
```

Expected: PASS。

### Task 5: 读取 Windows 动画设置

**Files:**
- Create: `Services/WindowsAnimationSettingsProvider.cs`
- Create: `Services/MotionSettingsResolver.cs`
- Modify: `Services/ServiceConfiguration.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/MotionSettingsResolverTests.cs`

- [ ] **Step 1: 写入解析红灯测试**

```csharp
[Theory]
[InlineData(MotionModes.Full, null, false)]
[InlineData(MotionModes.Reduced, true, true)]
[InlineData(MotionModes.System, true, false)]
[InlineData(MotionModes.System, false, true)]
[InlineData(MotionModes.System, null, true)]
public void ShouldReduceMotion_ReturnsExpected(
    string mode,
    bool? windowsAnimationsEnabled,
    bool expected)
{
    Assert.Equal(expected, MotionSettingsResolver.ShouldReduceMotion(
        mode,
        windowsAnimationsEnabled));
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MotionSettingsResolver"
```

Expected: FAIL，module 不存在。

- [ ] **Step 3: 实现纯解析**

```csharp
public static bool ShouldReduceMotion(string motionMode, bool? windowsAnimationsEnabled) =>
    motionMode switch
    {
        MotionModes.Full => false,
        MotionModes.Reduced => true,
        MotionModes.System => windowsAnimationsEnabled != true,
        _ => true
    };
```

- [ ] **Step 4: 实现 Win32 读取**

```csharp
internal sealed partial class WindowsAnimationSettingsProvider
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public bool? AreAnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return SystemParametersInfoW(
            SpiGetClientAreaAnimation,
            0,
            out var enabled,
            0)
            ? enabled
            : null;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool pvParam,
        uint fWinIni);
}
```

在 `ServiceConfiguration` 注册 singleton。

- [ ] **Step 5: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~MotionSettings"
git add -- Services/WindowsAnimationSettingsProvider.cs Services/MotionSettingsResolver.cs Services/ServiceConfiguration.cs tests/Cafe.Launcher.Avalonia.Tests/MotionSettingsResolverTests.cs
git commit -m "feat(accessibility): 跟随 Windows 动画设置"
```

Expected: PASS。

### Task 6: 将有效动态效果应用到 UI

**Files:**
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `ViewModels/RemoteContentViewModel.cs`
- Modify: `ViewModels/SettingsViewModel.cs`
- Modify: `ViewModels/WindowChromeViewModel.cs`
- Modify: `Views/MainWindow.axaml`
- Modify: `Views/MainWindow.Styles.axaml`
- Modify: `Views/MainWindowToastOverlay.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/RemoteContentViewModelTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

- [ ] **Step 1: 写入轮播红灯测试**

```csharp
[Fact]
public void ApplyMotionPreference_WhenReduced_StopsTimerAndRemovesTransition()
{
    var context = CreateContextWithTwoBanners();
    context.ViewModel.StartCarouselTimer();

    context.ViewModel.ApplyMotionPreference(reduceMotion: true);

    Assert.False(context.ViewModel.IsCarouselTimerRunning);
    Assert.Equal(TimeSpan.Zero, context.ViewModel.CarouselTransitionDuration);
}

[Fact]
public void ManualNavigation_WhenReduced_StillChangesSelection()
{
    var context = CreateContextWithTwoBanners();
    context.ViewModel.ApplyMotionPreference(true);
    context.ViewModel.SelectNextBannerCommand.Execute(null);
    Assert.Equal(1, context.ViewModel.CarouselSelectedIndex);
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteContentViewModelTests"
```

Expected: FAIL。

- [ ] **Step 3: 实现有效状态**

`MainWindowViewModel` 增加可观察属性：

```csharp
[ObservableProperty]
private bool isMotionReduced;

public bool IsMotionEnabled => !IsMotionReduced;
```

在应用设置时调用 provider 和 resolver，并通知 `IsMotionEnabled`。

`RemoteContentViewModel` 增加：

```csharp
private bool isMotionReduced;

[ObservableProperty]
private TimeSpan carouselTransitionDuration = TimeSpan.FromMilliseconds(350);

public void ApplyMotionPreference(bool reduceMotion)
{
    isMotionReduced = reduceMotion;
    CarouselTransitionDuration = reduceMotion
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(350);

    if (reduceMotion)
    {
        StopCarouselTimer();
    }
    else if (HasBannerItems)
    {
        StartCarouselTimer();
    }
}
```

`StartCarouselTimer()` 在 `isMotionReduced` 时直接返回。

- [ ] **Step 4: 绑定 XAML**

轮播：

```xml
<CrossFade Duration="{Binding RemoteContent.CarouselTransitionDuration}"/>
```

通知：

```xml
<Border Classes="toast-card"
        Classes.motion-enabled="{Binding DataContext.IsMotionEnabled, ElementName=ToastOverlayRoot}">
```

将通知动画从 `Border.toast-card` 移到 `Border.toast-card.motion-enabled`。

- [ ] **Step 5: 验证保存后即时应用**

增加测试：保存 `MotionModes.Reduced` 后 `IsMotionReduced` 为 `true`，轮播停止；
保存 `MotionModes.Full` 后恢复当前允许的轮播。

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteContentViewModelTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~UiStyleContractTests"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --no-restore --filter "FullyQualifiedName~MainWindowHeadlessTests"
git add -- ViewModels/MainWindowViewModel.cs ViewModels/RemoteContentViewModel.cs ViewModels/SettingsViewModel.cs ViewModels/WindowChromeViewModel.cs Views/MainWindow.axaml Views/MainWindow.Styles.axaml Views/MainWindowToastOverlay.axaml tests/Cafe.Launcher.Avalonia.Tests/RemoteContentViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs
git commit -m "feat(accessibility): 对全部显式动画应用动态效果设置"
```

Expected: PASS。

## P2 — 磁盘与安装校验状态

### Task 7: 增加精确进度字段和磁盘检查

**Files:**
- Modify: `Models/LauncherStateModels.cs`
- Modify: `Services/DiskSpaceService.cs`
- Modify: `Services/GameDownloadService.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`

- [ ] **Step 1: 写入磁盘红灯测试**

通过 `DiskSpaceService` 的内部 `Func<string, long?>` 构造函数注入可计数读取，
实现以下断言：

```csharp
Assert.Equal(1, diskSpace.ReadCount);
Assert.Contains(progress, item =>
    item.Stage == "disk-check"
    && item.RequiredDiskBytes == requiredBytes
    && item.AvailableDiskBytes == availableBytes);
```

磁盘不足结果断言消息同时包含 `FileSizeFormatter.Format(requiredBytes)` 和
`FileSizeFormatter.Format(availableBytes)`。

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~InstallOrUpdateAsync_WhenDiskSpace"
```

Expected: FAIL，新字段和 `disk-check` 尚不存在。

- [ ] **Step 3: 扩展模型**

```csharp
public long RequiredDiskBytes { get; set; }
public long? AvailableDiskBytes { get; set; }
public int FailedFileCount { get; set; }
public int RetryAttempt { get; set; }
public int RetryLimit { get; set; }
```

`GameOperationResult` 增加：

```csharp
public int FailedFileCount { get; set; }
```

- [ ] **Step 4: 增加内部测试 seam**

`DiskSpaceService` 保持 sealed 和现有公开构造函数，增加：

```csharp
private readonly Func<string, long?>? getAvailableBytesOverride;

public DiskSpaceService()
{
}

internal DiskSpaceService(Func<string, long?> getAvailableBytesOverride)
{
    this.getAvailableBytesOverride = getAvailableBytesOverride;
}
```

`GetAvailableBytes()` 的第一条分支为：

```csharp
if (getAvailableBytesOverride is not null)
{
    return getAvailableBytesOverride(path);
}
```

不新增 `IDiskSpaceService`，因为生产中只有一个 adapter。

- [ ] **Step 5: 单次读取并判断**

用一次读取替代 `HasEnoughSpace()`：

```csharp
var availableBytes = diskSpaceService.GetAvailableBytes(gamePath);
progress(new GameOperationProgress
{
    OperationKind = operationKind,
    Stage = "disk-check",
    RequiredDiskBytes = requiredBytes,
    AvailableDiskBytes = availableBytes,
    IsRunning = true,
    CanStop = true
});

if (!availableBytes.HasValue || availableBytes.Value < requiredBytes)
{
    return Failed(
        localizer.F(
            "diskSpaceInsufficientDetail",
            FileSizeFormatter.Format(requiredBytes),
            availableBytes.HasValue ? FileSizeFormatter.Format(availableBytes.Value) : "--"),
        "game-download-error-no-space",
        affectedCount);
}
```

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameDownloadServiceTests"
git add -- Models/LauncherStateModels.cs Services/DiskSpaceService.cs Services/GameDownloadService.cs tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs
git commit -m "feat(download): 报告精确磁盘空间状态"
```

Expected: PASS。

### Task 8: 报告安装校验重试

**Files:**
- Modify: `Services/GameDownloadService.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`

- [ ] **Step 1: 写入重试红灯测试**

使用现有 `VerificationRetryFileDownloadService`，让每轮分别返回固定失败数量，断言：

```csharp
Assert.Collection(
    progress.Where(item => item.Stage == "verification-retry"),
    item =>
    {
        Assert.Equal(1, item.RetryAttempt);
        Assert.Equal(3, item.RetryLimit);
        Assert.Equal(3, item.FailedFileCount);
    },
    item =>
    {
        Assert.Equal(2, item.RetryAttempt);
        Assert.Equal(3, item.RetryLimit);
        Assert.Equal(2, item.FailedFileCount);
    },
    item =>
    {
        Assert.Equal(3, item.RetryAttempt);
        Assert.Equal(3, item.RetryLimit);
        Assert.Equal(1, item.FailedFileCount);
    });
```

最终失败测试断言 `verification-failed` 和 `result.FailedFileCount`。

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~VerificationRetry"
```

Expected: FAIL。

- [ ] **Step 3: 实现重试状态**

在每次 `failedFiles` 生成后：

```csharp
if (failedFiles.Count > 0)
{
    var hasRetry = retry < MaxInstallVerificationRetry;
    progress(new GameOperationProgress
    {
        OperationKind = operationKind,
        Stage = hasRetry ? "verification-retry" : "verification-failed",
        FailedFileCount = failedFiles.Count,
        RetryAttempt = hasRetry ? retry + 1 : MaxInstallVerificationRetry,
        RetryLimit = MaxInstallVerificationRetry,
        IsRunning = hasRetry,
        CanStop = hasRetry
    });
}
```

最终 `Failed(...)` 接收并设置 `FailedFileCount`。不得传递文件路径。

- [ ] **Step 4: 验证次数语义**

保留并强化断言：

```csharp
Assert.Equal(MaxInstallVerificationRetry + 1, downloader.InvocationCount);
```

- [ ] **Step 5: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameDownloadServiceTests"
git add -- Services/GameDownloadService.cs tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs
git commit -m "feat(download): 报告安装校验重试状态"
```

Expected: PASS。

### Task 9: 映射下载状态到本地化界面

**Files:**
- Modify: `Services/LocalizationService.cs`
- Modify: `ViewModels/GameOperationsViewModel.cs`
- Modify: `Views/MainWindow.axaml`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs`
- Test: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] **Step 1: 写入 ViewModel 红灯测试**

```csharp
[Fact]
public void ApplyProgress_MapsDiskAndVerificationDetails()
{
    var context = CreateContext();
    context.ViewModel.ApplyProgress(new GameOperationProgress
    {
        Stage = "disk-check",
        RequiredDiskBytes = 2048,
        AvailableDiskBytes = 4096
    });
    Assert.Contains(FileSizeFormatter.Format(2048), context.ViewModel.ProgressDetail);
    Assert.Contains(FileSizeFormatter.Format(4096), context.ViewModel.ProgressDetail);

    context.ViewModel.ApplyProgress(new GameOperationProgress
    {
        Stage = "verification-retry",
        FailedFileCount = 2,
        RetryAttempt = 1,
        RetryLimit = 3
    });
    Assert.Contains("2", context.ViewModel.ProgressDetail);
    Assert.Contains("1", context.ViewModel.ProgressDetail);
    Assert.Contains("3", context.ViewModel.ProgressDetail);
}
```

- [ ] **Step 2: 验证红灯**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameOperationsViewModelTests"
```

Expected: FAIL，阶段仍映射为 `working`。

- [ ] **Step 3: 增加三语言键**

```text
diskSpaceCheck
diskSpaceInsufficientDetail
verificationRetry
verificationFailed
```

格式参数固定为：

- `diskSpaceCheck(required, available)`
- `diskSpaceInsufficientDetail(required, available)`
- `verificationRetry(failedCount, retryAttempt, retryLimit)`
- `verificationFailed(failedCount)`

- [ ] **Step 4: 映射阶段**

在 `ApplyProgressCore()` 的 switch 中增加三个精确分支。`disk-check`、
`verification-retry` 和 `verification-failed` 清空速度与 ETA，避免显示上一阶段残留值。

- [ ] **Step 5: 增加 Headless 断言**

加载主窗口，应用三个进度状态，查找进度详情 `TextBlock` 并断言文本包含对应数字；
不增加文件列表控件。

- [ ] **Step 6: 验证并提交**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --no-restore --filter "FullyQualifiedName~GameOperationsViewModelTests|FullyQualifiedName~LocalizationServiceTests|FullyQualifiedName~UiStyleContractTests"
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --no-restore --filter "FullyQualifiedName~MainWindowHeadlessTests"
git add -- Services/LocalizationService.cs ViewModels/GameOperationsViewModel.cs Views/MainWindow.axaml tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/LocalizationServiceTests.cs tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs
git commit -m "test(ui): 覆盖动态效果与下载状态展示"
```

Expected: PASS。

## 完整验证

### Task 10: 合约、构建与全量测试

**Files:**
- Verify: all files above

- [ ] **Step 1: 搜索旧委托**

```powershell
rg -n "GetSnapshot|RequestRefreshAfterPersistedResumeAsync|ApplySnapshotAsync|GetProxyMode|GetPatchUrlGroup|MinimizeWindow|RestoreWindow|CloseWindow" ViewModels Views tests
```

Expected: 只保留与 `SettingsEditor.GetSnapshot()` 等无关且语义明确的现有标识符；P0 删除清单中的公开委托无匹配。

- [ ] **Step 2: 搜索动态效果覆盖**

```powershell
rg -n "motionMode|MotionModes|SPI_GETCLIENTAREAANIMATION|SpiGetClientAreaAnimation|CarouselTransitionDuration|motion-enabled" Models Services ViewModels Views tests
```

Expected: 设置模型、平台读取、解析、轮播和通知全部有匹配。

- [ ] **Step 3: 搜索下载状态覆盖**

```powershell
rg -n "disk-check|verification-retry|verification-failed|RequiredDiskBytes|AvailableDiskBytes|FailedFileCount|RetryAttempt|RetryLimit" Models Services ViewModels tests
```

Expected: 生产数据流和测试均有匹配，不出现失败文件路径集合的 UI 字段。

- [ ] **Step 4: 执行完整验证**

```powershell
.\verify.ps1
```

Expected:

- Debug build：0 warnings、0 errors；
- logic tests：0 failed；
- Headless tests：0 failed；
- Release build：0 warnings、0 errors。

- [ ] **Step 5: 检查差异**

```powershell
git diff --check
git status --short
git log --oneline -12
```

Expected: `git diff --check` 无输出；工作区干净；提交顺序与本计划一致。
