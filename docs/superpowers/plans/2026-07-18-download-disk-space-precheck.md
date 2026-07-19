# Download Disk Space Precheck Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在全新安装时以完整解压容量进行磁盘空间预检查，空间明确不足时禁用安装按钮，并在下载服务入口再次校验；更新和修复仍只校验本次待下载文件的容量。

**Architecture:** 将“本次操作需要多少空间”的规则集中到 `DiskSpaceService`，由 UI 和下载服务共同调用。UI 使用同一次磁盘查询生成容量文案与按钮状态；服务在创建下载任务后、下载任何文件前重新读取磁盘空间并决定是否继续，以避免界面状态过期。现有运行期 `ERROR_DISK_FULL` 异常兜底保持不变。

**Tech Stack:** .NET 10、C#、Avalonia、CommunityToolkit.Mvvm、xUnit v3、PowerShell。

## Global Constraints

- 直接在 `main` 分支执行，不创建工作树。
- 保留并且不提交用户现有的未提交修改：
  - `Views/SettingsAboutSection.axaml`
  - `Views/SettingsDownloadNetworkSection.axaml`
  - `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- 不编辑 `UiStyleContractTests.cs`；新增独立结构测试文件，避免混入用户现有修改。
- 不新增依赖、配置项或臆测性的安全余量。
- 全新安装所需空间为 `max(DecompressionSize, NeedDownload.Sum(SizeBytes))`。
- 更新或修复所需空间为 `NeedDownload.Sum(SizeBytes)`。
- `DecompressionSize` 缺失或无法解析时回退到待下载总量。
- UI 无法读取可用空间时不预先禁用；下载服务无法读取可用空间时沿用现有 fail-closed 行为。
- 不新增本地化键，复用 `diskSpaceInsufficientDetail` 和现有安装按钮文案。
- 每个提交只暂存任务明确列出的文件；提交前使用 `git diff --cached --name-only` 检查范围。

---

### Task 1: 建立共享的磁盘容量判定模型

**Files:**

- Create: `Services/DiskSpaceCheckResult.cs`
- Modify: `Services/DiskSpaceService.cs`
- Create: `tests/Cafe.Launcher.Avalonia.Tests/DiskSpaceServiceTests.cs`

- [ ] **Step 1: 先写容量规则与查询结果测试**

创建 `tests/Cafe.Launcher.Avalonia.Tests/DiskSpaceServiceTests.cs`：

```csharp
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class DiskSpaceServiceTests
{
    [Theory]
    [InlineData(true, 10L, "20B", 20L)]
    [InlineData(true, 30L, "20B", 30L)]
    [InlineData(false, 10L, "20B", 10L)]
    [InlineData(true, 10L, null, 10L)]
    [InlineData(true, 10L, "invalid", 10L)]
    public void ResolveRequiredBytes_StateAndSizes_ReturnsOperationPeakRequirement(
        bool isFreshInstall,
        long plannedDownloadBytes,
        string? decompressionSize,
        long expected)
    {
        var actual = DiskSpaceService.ResolveRequiredBytes(
            isFreshInstall,
            plannedDownloadBytes,
            decompressionSize);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Check_WhenAvailableEqualsRequired_ReturnsEnough()
    {
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => 20L
        };

        var result = service.Check(@"C:\Games\BlueArchive_JP", 20L);

        Assert.Equal(20L, result.RequiredBytes);
        Assert.Equal(20L, result.AvailableBytes);
        Assert.True(result.IsAvailableKnown);
        Assert.True(result.HasEnoughSpace);
    }

    [Fact]
    public void Check_WhenAvailableIsUnknown_ReturnsUnknownAndNotEnough()
    {
        var service = new DiskSpaceService
        {
            GetAvailableBytesOverride = _ => null
        };

        var result = service.Check(@"C:\Games\BlueArchive_JP", 20L);

        Assert.Equal(20L, result.RequiredBytes);
        Assert.Null(result.AvailableBytes);
        Assert.False(result.IsAvailableKnown);
        Assert.False(result.HasEnoughSpace);
    }
}
```

- [ ] **Step 2: 运行测试并确认因缺少 API 而失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DiskSpaceServiceTests"
```

Expected: 编译失败，提示 `ResolveRequiredBytes`、`Check` 或 `DiskSpaceCheckResult` 尚不存在。

- [ ] **Step 3: 实现最小共享模型与容量规则**

创建 `Services/DiskSpaceCheckResult.cs`：

```csharp
namespace Cafe.Launcher.Avalonia.Services;

public readonly record struct DiskSpaceCheckResult(long RequiredBytes, long? AvailableBytes)
{
    public bool IsAvailableKnown => AvailableBytes.HasValue;

    public bool HasEnoughSpace =>
        AvailableBytes is long availableBytes && availableBytes >= RequiredBytes;
}
```

在 `Services/DiskSpaceService.cs` 添加 `using Cafe.Launcher.Avalonia.Helpers;`，并在类中加入：

```csharp
public DiskSpaceCheckResult Check(string path, long requiredBytes)
{
    var normalizedRequiredBytes = Math.Max(0L, requiredBytes);
    return new DiskSpaceCheckResult(normalizedRequiredBytes, GetAvailableBytes(path));
}

public static long ResolveRequiredBytes(
    bool isFreshInstall,
    long plannedDownloadBytes,
    string? decompressionSize)
{
    var normalizedPlannedBytes = Math.Max(0L, plannedDownloadBytes);
    if (!isFreshInstall
        || string.IsNullOrWhiteSpace(decompressionSize)
        || !FileSizeFormatter.TryParseHumanReadable(decompressionSize, out var decompressionBytes))
    {
        return normalizedPlannedBytes;
    }

    return Math.Max(normalizedPlannedBytes, decompressionBytes);
}
```

保留 `HasEnoughSpace` 的既有 `requiredBytes <= 0` 兼容语义，并让正数路径复用新结果：

```csharp
public bool HasEnoughSpace(string path, long requiredBytes)
{
    if (requiredBytes <= 0)
    {
        return true;
    }

    return Check(path, requiredBytes).HasEnoughSpace;
}
```

- [ ] **Step 4: 运行聚焦测试并确认通过**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DiskSpaceServiceTests"
```

Expected: 全部通过。

- [ ] **Step 5: 提交共享规则**

```powershell
git add Services/DiskSpaceCheckResult.cs Services/DiskSpaceService.cs tests/Cafe.Launcher.Avalonia.Tests/DiskSpaceServiceTests.cs
git diff --cached --name-only
git commit -m "feat(download): 统一磁盘空间容量策略"
```

Expected: 暂存区只有上述 3 个文件。

---

### Task 2: 在下载服务入口按操作类型重新校验

**Files:**

- Modify: `Services/GameDownloadService.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs`

- [ ] **Step 1: 写出全新安装回归测试，复现截图中的容量差异**

在 `GameDownloadServiceTests` 中新增测试。复用现有 `ManifestHandler`、`RecordingFileDownloadService`、`CreateService` 与 `CreateSnapshot`；测试数据使用截图等价的 `18.5GB / 1.09GB / 7.15GB`：

```csharp
[Fact]
public async Task InstallOrUpdateAsync_WhenFreshInstallNeedsDecompressionSpace_BlocksBeforeDownload()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var gamePath = Path.Combine(tempDir, "Game");
    Assert.True(FileSizeFormatter.TryParseHumanReadable("1.09GB", out var plannedBytes));
    Assert.True(FileSizeFormatter.TryParseHumanReadable("7.15GB", out var availableBytes));
    Assert.True(FileSizeFormatter.TryParseHumanReadable("18.5GB", out var decompressionBytes));

    var downloader = new RecordingFileDownloadService();
    var diskSpace = new DiskSpaceService
    {
        GetAvailableBytesOverride = _ => availableBytes
    };
    var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
    await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
    var manifestFile = new ManifestFile
    {
        Path = "data/archive.bin",
        Size = plannedBytes.ToString(CultureInfo.InvariantCulture),
        Hash = "0"
    };
    using var apiClient = CreateManifestApiClient(manifestFile);
    var snapshot = CreateSnapshot(gamePath);
    snapshot.RuntimeState = LauncherRuntimeState.NotInstalled;
    snapshot.Remote.GameConfig!.DecompressionSize = "18.5GB";
    var progress = new List<GameOperationProgress>();
    using var service = CreateService(
        apiClient,
        settingsService,
        Path.Combine(tempDir, "download_state.json"),
        downloader,
        diskSpace);

    var result = await service.InstallOrUpdateAsync(snapshot, progress.Add);

    Assert.False(result.Success);
    Assert.Equal(GameOperationErrorCode.InsufficientDiskSpace, result.ErrorCode);
    Assert.Equal(0, downloader.InvocationCount);
    var diskCheck = Assert.Single(progress, item => item.Stage == GameOperationStage.DiskCheck);
    Assert.Equal(decompressionBytes, diskCheck.RequiredDiskBytes);
    Assert.Equal(availableBytes, diskCheck.AvailableDiskBytes);
    Directory.Delete(tempDir, recursive: true);
}
```

- [ ] **Step 2: 运行该测试并确认它先失败**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~GameDownloadServiceTests.InstallOrUpdateAsync_WhenFreshInstallNeedsDecompressionSpace_BlocksBeforeDownload"
```

Expected: 断言失败；当前 `RequiredDiskBytes` 仍为约 `1.09GB`，并且下载入口未被完整容量阻止。

- [ ] **Step 3: 在下载任务创建后、任何文件下载前计算正确容量**

在 `GameDownloadService.RunAsync` 现有磁盘检查区块中，将待下载总量与判定替换为：

```csharp
var currentDownloadList = downloadPlan.NeedDownload;
var affectedCount = currentDownloadList.Count + downloadPlan.NeedDelete.Count;
var plannedDownloadBytes = currentDownloadList.Sum(item => item.SizeBytes);
var requiredBytes = DiskSpaceService.ResolveRequiredBytes(
    snapshot.RuntimeState == LauncherRuntimeState.NotInstalled,
    plannedDownloadBytes,
    gameConfig.DecompressionSize);
var diskCheck = diskSpaceService.Check(gamePath, requiredBytes);

progress(new GameOperationProgress
{
    OperationKind = operationKind,
    Stage = GameOperationStage.DiskCheck,
    RequiredDiskBytes = diskCheck.RequiredBytes,
    AvailableDiskBytes = diskCheck.AvailableBytes,
    IsRunning = true,
    CanStop = true
});

if (!diskCheck.HasEnoughSpace)
{
    await diagnostics.MessageAsync(
        "Game download blocked by disk space.",
        $"path: {gamePath}{Environment.NewLine}required: {FileSizeFormatter.Format(diskCheck.RequiredBytes)}",
        activeToken);
    checkpointStore.Clear();
    return Failed(
        localizer.F(
            "diskSpaceInsufficientDetail",
            FileSizeFormatter.Format(diskCheck.RequiredBytes),
            diskCheck.AvailableBytes.HasValue
                ? FileSizeFormatter.Format(diskCheck.AvailableBytes.Value)
                : "--"),
        GameOperationErrorCode.InsufficientDiskSpace,
        affectedCount);
}
```

保留原有诊断消息的具体字段与调用签名；本步骤只替换 required/available 的来源和条件，不移动检查位置，不删除 `checkpointStore.Clear()`，也不修改 `ERROR_DISK_FULL` catch。

- [ ] **Step 4: 运行全新安装回归测试与现有磁盘测试**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~GameDownloadServiceTests"
```

Expected: 新测试和现有 `WhenDiskSpaceIsInsufficient`、`ReadsAvailableDiskSpaceOnceAndReportsDiskCheck` 测试全部通过；磁盘查询仍只执行一次。

- [ ] **Step 5: 增加更新场景回归测试，证明不会误用完整解压容量**

新增一个小文件测试，使用 `planned=10B`、`decompression=20B`、`available=15B`。先通过现有测试辅助方法写入有效的旧安装状态，再将快照设为 `UpdateAvailable`，并使用现有会落盘的下载器：

```csharp
[Fact]
public async Task InstallOrUpdateAsync_WhenUpdating_UsesPendingDownloadBytesOnly()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var gamePath = Path.Combine(tempDir, "Game");
    Directory.CreateDirectory(gamePath);
    await WriteLocalGameFilesAsync(gamePath);

    var settingsService = new LauncherSettingsService(Path.Combine(tempDir, "settings.json"));
    await settingsService.SaveAsync(new LauncherSettings { GamePath = gamePath });
    var fileBytes = new byte[10];
    var manifestFile = await CreateManifestFileAsync(tempDir, "data/file.bin", fileBytes);
    using var apiClient = CreateManifestApiClient(manifestFile);
    var diskSpace = new DiskSpaceService
    {
        GetAvailableBytesOverride = _ => 15L
    };
    var snapshot = CreateSnapshot(gamePath);
    snapshot.RuntimeState = LauncherRuntimeState.UpdateAvailable;
    snapshot.Remote.GameConfig!.DecompressionSize = "20B";
    var progress = new List<GameOperationProgress>();
    using var service = CreateService(
        apiClient,
        settingsService,
        Path.Combine(tempDir, "download_state.json"),
        new WritingFileDownloadService(fileBytes),
        diskSpace);

    var result = await service.InstallOrUpdateAsync(snapshot, progress.Add);

    Assert.True(result.Success);
    var diskCheck = Assert.Single(progress, item => item.Stage == GameOperationStage.DiskCheck);
    Assert.Equal(10L, diskCheck.RequiredDiskBytes);
    Assert.Equal(15L, diskCheck.AvailableDiskBytes);
    Directory.Delete(tempDir, recursive: true);
}
```

- [ ] **Step 6: 运行整个下载服务测试类**

Run:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~GameDownloadServiceTests"
```

Expected: 全部通过。

- [ ] **Step 7: 提交服务入口修复**

```powershell
git add Services/GameDownloadService.cs tests/Cafe.Launcher.Avalonia.Tests/GameDownloadServiceTests.cs
git diff --cached --name-only
git commit -m "fix(download): 按完整容量阻止全新安装"
```

Expected: 暂存区只有上述 2 个文件。

---

### Task 3: 让 UI 使用同一次查询禁用按钮并解释原因

**Files:**

- Modify: `ViewModels/SettingsOptionsViewModel.cs`
- Modify: `ViewModels/ShellViewModel.cs`
- Modify: `ViewModels/GameOperationsViewModel.cs`
- Modify: `Views/MainWindow.axaml`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/SettingsOptionsDiskSpaceTests.cs`
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs`
- Create: `tests/Cafe.Launcher.Avalonia.Tests/InstallDiskSpaceUiContractTests.cs`

- [ ] **Step 1: 为 UI 的单次查询结果写测试**

在 `SettingsOptionsDiskSpaceTests` 中新增：

```csharp
[Fact]
public void ResolveDiskSpaceCheck_WhenSpaceIsInsufficient_ReusesValuesForDisplay()
{
    const long availableBytes = 6L * 1024 * 1024 * 1024;
    var diskSpace = new DiskSpaceService
    {
        GetAvailableBytesOverride = _ => availableBytes
    };
    var options = CreateOptions(diskSpace);

    var check = options.ResolveDiskSpaceCheck(@"C:\Games\BlueArchive_JP", "10GB");
    var text = options.ResolveDiskSpaceText("10GB", check);

    Assert.Equal(10L * 1024 * 1024 * 1024, check.RequiredBytes);
    Assert.Equal(availableBytes, check.AvailableBytes);
    Assert.False(check.HasEnoughSpace);
    Assert.Contains(FileSizeFormatter.Format(availableBytes), text, StringComparison.Ordinal);
}
```

复用测试文件现有的 `CreateOptions` 构造方式；如果它当前内联构造，则保持该风格，不额外抽象。

- [ ] **Step 2: 运行测试并确认因新重载缺失而失败**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SettingsOptionsDiskSpaceTests.ResolveDiskSpaceCheck_WhenSpaceIsInsufficient_ReusesValuesForDisplay"
```

Expected: 编译失败，提示 `ResolveDiskSpaceCheck` 或接收 `DiskSpaceCheckResult` 的文案重载不存在。

- [ ] **Step 3: 在 SettingsOptionsViewModel 中拆分查询与格式化**

新增：

```csharp
public DiskSpaceCheckResult ResolveDiskSpaceCheck(string gamePath, string? requiredSize)
{
    var requiredBytes = DiskSpaceService.ResolveRequiredBytes(
        true,
        0L,
        requiredSize);
    return diskSpaceService.Check(gamePath, requiredBytes);
}

public string ResolveDiskSpaceText(string? requiredSize, DiskSpaceCheckResult check)
{
    var requiredDisplay = string.IsNullOrWhiteSpace(requiredSize)
        ? "--"
        : requiredSize.Replace(" ", "", StringComparison.Ordinal);
    var availableDisplay = check.AvailableBytes.HasValue
        ? FileSizeFormatter.Format(check.AvailableBytes.Value)
        : "--";
    var baseText = localizer.F("diskSpace", requiredDisplay, availableDisplay);
    if (check.RequiredBytes <= 0 || !check.IsAvailableKnown)
    {
        return baseText;
    }

    if (check.HasEnoughSpace)
    {
        return baseText + " " + localizer.T("diskSpaceOkSuffix");
    }

    var missingBytes = check.RequiredBytes - check.AvailableBytes!.Value;
    return baseText + " " + localizer.F(
        "diskSpaceShortSuffix",
        FileSizeFormatter.Format(missingBytes));
}
```

将现有入口保留为兼容包装，确保旧调用与旧测试仍成立：

```csharp
public string ResolveDiskSpaceText(string gamePath, string? requiredSize)
{
    var check = ResolveDiskSpaceCheck(gamePath, requiredSize);
    return ResolveDiskSpaceText(requiredSize, check);
}
```

落地时必须复用该方法当前实际使用的本地化键和原文案拼接方式；只拆分磁盘查询，不改变用户可见措辞。

- [ ] **Step 4: 运行 SettingsOptions 磁盘测试类**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SettingsOptionsDiskSpaceTests"
```

Expected: 全部通过。

- [ ] **Step 5: 写按钮 CanExecute 与提示测试**

在 `GameOperationsViewModelTests` 中新增：

```csharp
[Fact]
public void ApplySnapshot_WhenFreshInstallSpaceIsInsufficient_DisablesInstallAndExplainsWhy()
{
    var context = CreateContext();
    context.Shell.IsInstallBlockedByDiskSpace = true;
    context.Shell.InstallDiskSpaceMessage = "需要 18.5GB，可用 7.15GB";
    var snapshot = new LauncherStatusSnapshot
    {
        RuntimeState = LauncherRuntimeState.NotInstalled
    };

    context.ViewModel.ApplySnapshot(snapshot);

    Assert.False(context.ViewModel.InstallOrUpdateCommand.CanExecute(null));
    Assert.Equal(
        context.Shell.InstallDiskSpaceMessage,
        context.ViewModel.InstallButtonToolTip);
}

[Fact]
public void ApplySnapshot_WhenSpaceIsKnownToBeEnough_EnablesInstallAndUsesActionTooltip()
{
    var context = CreateContext();
    context.Shell.IsInstallBlockedByDiskSpace = false;
    var snapshot = new LauncherStatusSnapshot
    {
        RuntimeState = LauncherRuntimeState.NotInstalled
    };

    context.ViewModel.ApplySnapshot(snapshot);

    Assert.True(context.ViewModel.InstallOrUpdateCommand.CanExecute(null));
    Assert.Equal(context.ViewModel.InstallButtonText, context.ViewModel.InstallButtonToolTip);
}
```

复用测试文件现有 `CreateContext`，快照按上述最小对象直接构造。

- [ ] **Step 6: 运行按钮测试并确认先失败**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~GameOperationsViewModelTests.ApplySnapshot_WhenFreshInstallSpaceIsInsufficient|FullyQualifiedName~GameOperationsViewModelTests.ApplySnapshot_WhenSpaceIsKnownToBeEnough"
```

Expected: 编译失败，缺少 Shell 状态、按钮 tooltip 或命令 CanExecute。

- [ ] **Step 7: 在 Shell 中由同一查询结果生成展示与禁用状态**

在 `ShellViewModel` 增加：

```csharp
[ObservableProperty]
private bool isInstallBlockedByDiskSpace;

[ObservableProperty]
private string installDiskSpaceMessage = string.Empty;
```

在 `ApplySnapshot` 中替换当前 `ResolveDiskSpaceText` 单行调用：

```csharp
var diskCheck = settings.Options.ResolveDiskSpaceCheck(
    localGame.GamePath,
    gameConfig?.DecompressionSize);
DiskSpaceText = settings.Options.ResolveDiskSpaceText(
    gameConfig?.DecompressionSize,
    diskCheck);

IsInstallBlockedByDiskSpace =
    snapshot.RuntimeState == LauncherRuntimeState.NotInstalled
    && diskCheck.RequiredBytes > 0
    && diskCheck.IsAvailableKnown
    && !diskCheck.HasEnoughSpace;

InstallDiskSpaceMessage = IsInstallBlockedByDiskSpace
    ? localizer.F(
        "diskSpaceInsufficientDetail",
        FileSizeFormatter.Format(diskCheck.RequiredBytes),
        FileSizeFormatter.Format(diskCheck.AvailableBytes!.Value))
    : string.Empty;
```

这里必须保留 `IsAvailableKnown` 条件：UI 查询失败时允许用户点击，实际入口由服务再次检查并安全失败。

- [ ] **Step 8: 为安装命令添加容量 CanExecute 与动态提示**

在 `GameOperationsViewModel` 增加：

```csharp
[ObservableProperty]
private string installButtonToolTip = string.Empty;
```

在 `ApplySnapshot` 设置完 `InstallButtonText` 后加入：

```csharp
InstallButtonToolTip = shell.IsInstallBlockedByDiskSpace
    ? shell.InstallDiskSpaceMessage
    : InstallButtonText;
InstallOrUpdateCommand.NotifyCanExecuteChanged();
```

将安装命令的特性改为 `[RelayCommand(CanExecute = nameof(CanInstallOrUpdate))]`，方法体保持原样，并新增：

```csharp
private bool CanInstallOrUpdate()
{
    return !shell.IsInstallBlockedByDiskSpace;
}
```

不把 `IsBusy` 重复写入 CanExecute；现有 XAML 已负责忙碌态，`AsyncRelayCommand` 也负责执行中的并发保护。本条件只表达新增的容量业务约束。

- [ ] **Step 9: 运行 ViewModel 聚焦测试**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SettingsOptionsDiskSpaceTests|FullyQualifiedName~GameOperationsViewModelTests"
```

Expected: 全部通过。

- [ ] **Step 10: 写安装按钮 tooltip 与无障碍说明的结构测试**

创建 `InstallDiskSpaceUiContractTests.cs`：

```csharp
using System.Xml.Linq;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class InstallDiskSpaceUiContractTests
{
    [Fact]
    public void MainWindow_InstallAction_UsesDiskSpaceAwareTooltipAndHelpText()
    {
        var document = XDocument.Load(ProjectFile("Views", "MainWindow.axaml"));
        var installButton = Assert.Single(
            document.Descendants().Where(element =>
                element.Name.LocalName == "Button"
                && element.Attribute("Command")?.Value
                    == "{Binding Operations.InstallOrUpdateCommand}"));

        Assert.Equal(
            "{Binding Operations.InstallButtonToolTip}",
            installButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "ToolTip.Tip").Value);
        Assert.Equal(
            "{Binding Operations.InstallButtonToolTip}",
            installButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.HelpText").Value);
    }

    private static string ProjectFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Cafe.Launcher.Avalonia.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. segments]);
    }
}
```

- [ ] **Step 11: 运行结构测试并确认它先失败**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~InstallDiskSpaceUiContractTests"
```

Expected: 失败，因为按钮仍绑定 `InstallButtonText` 且缺少 `AutomationProperties.HelpText`。

- [ ] **Step 12: 更新安装按钮绑定**

在 `Views/MainWindow.axaml` 的安装按钮上：

```xml
<Button Grid.Column="2"
        Classes="primary-action bottom-action path-operation primary-operation"
        Command="{Binding Operations.InstallOrUpdateCommand}"
        ToolTip.Tip="{Binding Operations.InstallButtonToolTip}"
        AutomationProperties.HelpText="{Binding Operations.InstallButtonToolTip}"
        IsEnabled="{Binding Shell.IsBusy, Converter={StaticResource InverseBoolConverter}}">
```

保留按钮当前其他属性、图标和内容不变。Avalonia 会将显式忙碌态绑定与命令 CanExecute 共同反映到最终启用状态。

- [ ] **Step 13: 运行 UI 相关测试**

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~InstallDiskSpaceUiContractTests|FullyQualifiedName~SettingsOptionsDiskSpaceTests|FullyQualifiedName~GameOperationsViewModelTests"
.\dev.ps1 ui
```

Expected: 聚焦测试和 UI 验证全部通过；安装按钮在容量不足时通过命令 CanExecute 禁用，tooltip/HelpText 给出原因。

- [ ] **Step 14: 提交 UI 行为**

```powershell
git add ViewModels/SettingsOptionsViewModel.cs ViewModels/ShellViewModel.cs ViewModels/GameOperationsViewModel.cs Views/MainWindow.axaml tests/Cafe.Launcher.Avalonia.Tests/SettingsOptionsDiskSpaceTests.cs tests/Cafe.Launcher.Avalonia.Tests/GameOperationsViewModelTests.cs tests/Cafe.Launcher.Avalonia.Tests/InstallDiskSpaceUiContractTests.cs
git diff --cached --name-only
git commit -m "feat(ui): 空间不足时禁用全新安装"
```

Expected: 暂存区只有上述 7 个文件；用户原有 3 个未提交文件仍未暂存。

---

### Task 4: 完整验证并确认工作区边界

**Files:**

- Verify only; 若验证发现本功能缺陷，只修改对应任务中的文件，并以相应的 `fix(download)` 或 `test(download)` 类型追加小提交。

- [ ] **Step 1: 运行本地化契约**

```powershell
.\scripts\Test-LocalizationContract.ps1
```

Expected: 通过；本功能未增加任何本地化键。

- [ ] **Step 2: 运行完整发布门禁**

```powershell
.\verify.ps1
```

Expected: Debug build、单元测试、覆盖率和 Release build 全部通过；Windows 无符号链接权限时允许既有 2 个测试按原逻辑跳过。

- [ ] **Step 3: 审核最终差异与用户文件保护情况**

```powershell
git status --short --branch
git diff --check
git log --oneline d36a11a..HEAD
git diff --name-only d36a11a..HEAD
```

Expected:

- 新提交只包含本计划列出的生产代码、测试和计划文件。
- `SettingsAboutSection.axaml`、`SettingsDownloadNetworkSection.axaml`、`UiStyleContractTests.cs` 仍保持用户原有未提交状态，没有被任何功能提交带入。
- 没有空白错误。
- 服务端全新安装使用完整容量，更新/修复使用增量容量；UI 只在空间明确不足时预禁用。

---

## Plan Self-Review

- [ ] 全新安装、更新/修复、解析失败、空间未知、空间刚好足够五类边界均有测试覆盖。
- [ ] UI 文案和按钮状态来自同一 `DiskSpaceCheckResult`，避免一次刷新内显示与状态不一致。
- [ ] 下载服务在每次实际操作入口重新读取磁盘空间，避免依赖可能过期的 UI 快照。
- [ ] 现有 `ERROR_DISK_FULL` 运行期兜底未删除。
- [ ] 未引入安全余量、缓存、配置或新依赖。
- [ ] 未修改或暂存用户现有的 3 个工作区文件。
- [ ] 所有示例类型名在执行时对照现有模型校正，不能通过修改生产模型迁就测试。
- [ ] 每个提交符合 Conventional Commits，并且能独立通过对应聚焦测试。
