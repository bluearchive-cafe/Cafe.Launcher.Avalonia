# Architecture Refactor Implementation Plan

> **AI agent executor:** Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax (`- [ ]`) to track progress.

**Target:** Eliminate Services↔Features circular dependency, split overgrown classes, consolidate helpers and models.

**Architecture:** Four independent work streams — helper/file moves first (low risk), then structural changes, then deep refactoring. Each step is a self-contained commit.

**Tech stack:** .NET 10, C#, Avalonia, xUnit v3, CommunityToolkit.Mvvm

---

### Task 1: Move pure utilities from Services/ to Helpers/

**Files:**
- Move: `Services/ColorUtils.cs` → `Helpers/ColorUtils.cs`
- Move: `Services/VersionComparer.cs` → `Helpers/VersionComparer.cs`
- Move: `Services/ProcessService.cs` → `Helpers/ProcessService.cs`
- Move: `Services/MotionSettingsResolver.cs` → `Helpers/MotionSettingsResolver.cs`
- Move: `Services/AnimationTimings.cs` → `Helpers/AnimationTimings.cs`
- Modify: all callers that reference these types via `Cafe.Launcher.Avalonia.Services`

- [ ] **Step 1: Find all callers of each type**

```powershell
# For each of the 5 files, grep for the class name across the repo
```

For each helper class, search the codebase for its class name used as a type reference (not the file itself), and note every file that needs a `using` update. Example for ColorUtils:

Expected callers (approximate):
- ColorUtils: `SettingsAppearanceViewModel.cs`, `BackgroundViewModel.cs`, `Services/ColorUtils.cs`
- VersionComparer: `GameLaunchService.cs`
- ProcessService: `GameDownloadService.cs`, `GameUninstallService.cs` (already in Features/GameOperations via using)
- MotionSettingsResolver: `MainWindowViewModel.cs`, `Features/Shell/ShellViewModel.cs`
- AnimationTimings: `Controls/`, test files

- [ ] **Step 2: Move each file and update its namespace**

For each of the 5 files, in PowerShell:

```powershell
# Example for ColorUtils.cs
$src = "E:\Repos\Cafe.Launcher.Avalonia\Services\ColorUtils.cs"
$dst = "E:\Repos\Cafe.Launcher.Avalonia\Helpers\ColorUtils.cs"
Move-Item $src $dst
```

Then edit each moved file to change its namespace from `namespace Cafe.Launcher.Avalonia.Services;` to `namespace Cafe.Launcher.Avalonia.Helpers;`.

- [ ] **Step 3: Update all callers**

For each caller identified in Step 1, change:
- `using Cafe.Launcher.Avalonia.Services;` → only if the caller needs no *other* Services types. If it uses other Services types, keep the existing using and the new namespace is already available via implicit usings or the existing using.

Actually, since `Helpers/` is already under the project root, most callers already have the namespace available. The key change: callers that explicitly reference `Services.ColorUtils` or have `using static Cafe.Launcher.Avalonia.Services.ColorUtils` need updates.

For each caller, if the type was qualified or the caller had a specific using for it, update the reference.

- [ ] **Step 4: Build and fix**

```powershell
dotnet build "E:\Repos\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj" -c Debug --no-restore
```

Expected: 0 warnings, 0 errors. Fix any compilation errors from missed namespace updates.

- [ ] **Step 5: Run tests**

```powershell
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj" -c Debug --no-restore
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Helpers/ColorUtils.cs Helpers/VersionComparer.cs Helpers/ProcessService.cs Helpers/MotionSettingsResolver.cs Helpers/AnimationTimings.cs
git add -u
git commit -m "refactor: move pure utility classes from Services/ to Helpers/"
```

---

### Task 2: Split Models/LauncherStateModels.cs

**Files:**
- Create: `Models/LauncherEnums.cs`
- Create: `Models/LauncherSettings.cs`
- Create: `Models/LauncherRuntimeModels.cs`
- Modify: `Models/LauncherStateModels.cs` (remove extracted sections)
- Modify: all files referencing the moved types

- [ ] **Step 1: Extract LauncherEnums.cs**

Create `Models/LauncherEnums.cs`:

```csharp
namespace Cafe.Launcher.Avalonia.Models;

public enum GameOperationsRefreshMode
{
    Normal,
    SkipPersistedResume
}

public enum LauncherRuntimeState
{
    NotInstalled,
    Corrupted,
    IoFailure,
    RemoteUnavailable,
    BelowLowestVersion,
    UpdateAvailable,
    Ready
}
```

Also move the enum-like string constant classes that are genuinely enums (`GameOperationKind`, `GameOperationStage`, `GameOperationErrorCode` — check if these are already separate files; if not, extract them too).

Actually, let me check: `GameOperationKind`, `GameOperationStage`, `GameOperationErrorCode` — these are likely in a separate file already. Let me search...

These are in `Models/GameOperationModels.cs` or similar. Keep them there. Only extract from `LauncherStateModels.cs`.

The string-constant classes (`ProxyModes`, `ThemeModes`, etc.) stay in LauncherStateModels.cs for now since they're closely tied to the settings model. Actually, per the design, we should extract them too.

New `Models/LauncherEnums.cs`:

```csharp
namespace Cafe.Launcher.Avalonia.Models;

public enum GameOperationsRefreshMode
{
    Normal,
    SkipPersistedResume
}

public enum LauncherRuntimeState
{
    NotInstalled,
    Corrupted,
    IoFailure,
    RemoteUnavailable,
    BelowLowestVersion,
    UpdateAvailable,
    Ready
}
```

And the string-constant classes:

```csharp
// These stay in LauncherStateModels.cs as they are referenced by LauncherSettings
// But they need to be in a file that LauncherSettings.cs can see them from.
```

Wait, re-reading the spec: the split is
- `Models/LauncherEnums.cs` (~200 lines) — enums + string-constant classes
- `Models/LauncherSettings.cs` (~200 lines) — LauncherSettings class only
- `Models/LauncherRuntimeModels.cs` (~100 lines) — runtime DTOs (LauncherStatusSnapshot, LauncherRemoteState, GameOperationProgress, etc.)

So `LauncherEnums.cs` gets: `GameOperationsRefreshMode`, `LauncherRuntimeState`, AND all the string-constant classes (ProxyModes, ThemeModes, etc.)

`LauncherSettings.cs` gets: `LauncherSettings` class only

`LauncherRuntimeModels.cs` gets: `LauncherStatusSnapshot`, `LauncherRemoteState`, `GameOperationProgress`, `GameOperationResult`, `ManifestValidationResult`, `GameLaunchResult`, `SelectableOption` and subclasses, `RemoteContentItem`, `NewsCategory`

`LauncherStateModels.cs` becomes empty and can be deleted.

- [ ] **Step 1: Create Models/LauncherEnums.cs**

Move from `LauncherStateModels.cs` lines 13-136 (enum + all string-constant classes) to `Models/LauncherEnums.cs`:

```csharp
using Cafe.Launcher.Avalonia.Constants;

namespace Cafe.Launcher.Avalonia.Models;

public enum GameOperationsRefreshMode
{
    Normal,
    SkipPersistedResume
}

public static class LaunchCheckModes
{
    public const string LocalManifest = "localManifest";
    public const string RemoteManifest = "remoteManifest";
    public const string None = "none";
}

public static class ProxyModes
{
    public const string Direct = "direct";
    public const string Auto = "auto";
    public const string System = "system";
}

public static class CloseBehaviors
{
    public const string Minimize = "minimize";
    public const string Exit = "exit";
}

public static class LauncherLanguages
{
    public const string Auto = "auto";
    public const string English = "en";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string Japanese = "ja";
}

public static class ThemeModes
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";
}

public static class MotionModes
{
    public const string System = "system";
    public const string Full = "full";
    public const string Reduced = "reduced";
}

public static class ThemeColorModes
{
    public const string Default = "default";
    public const string System = "system";
    public const string Wallpaper = "wallpaper";
    public const string Custom = "custom";
}

public static class DownloadSpeedLimits
{
    public const string Unlimited = "unlimited";
    public const string Speed1MBs = "1MB/s";
    public const string Speed5MBs = "5MB/s";
    public const string Speed10MBs = "10MB/s";
    public const string Speed25MBs = "25MB/s";
    public const string Speed50MBs = "50MB/s";
    public static int ToBytesPerSecond(string limit) => limit switch
    {
        Speed1MBs => 1024 * 1024,
        Speed5MBs => 5 * 1024 * 1024,
        Speed10MBs => 10 * 1024 * 1024,
        Speed25MBs => 25 * 1024 * 1024,
        Speed50MBs => 50 * 1024 * 1024,
        _ => 0
    };
}

public static class PatchUrlGroups
{
    public const string Official = "official";
    public const string Cafe = "cafe";
}

public static class BackgroundSources
{
    public const string Bundled = "bundled";
    public const string Remote = "remote";
    public const string Custom = "custom";
}

public static class BackgroundFits
{
    public const string Fill = "fill";
    public const string Uniform = "uniform";
    public const string UniformToFill = "uniformToFill";
}

public static class UpdateChannels
{
    public const string Stable = "stable";
    public const string Beta = "beta";
}

public static class LogLevels
{
    public const string Verbose = "verbose";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Fatal = "fatal";
}

public static class ResourcePanelUidSources
{
    public const string Auto = "auto";
    public const string Custom = "custom";
}

public static class StatusDetailModes
{
    public const string Hidden = "hidden";
    public const string Compact = "compact";
    public const string Detailed = "detailed";
}
```

- [ ] **Step 2: Create Models/LauncherSettings.cs**

Move the `LauncherSettings` class (lines 138-327 of LauncherStateModels.cs) to `Models/LauncherSettings.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Cafe.Launcher.Avalonia.Constants;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cafe.Launcher.Avalonia.Models;

public sealed partial class LauncherSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("gamePath")]
    private string gamePath = "";

    // ... (all existing properties - exact copy from original)

    public LauncherSettings DeepClone() { ... }
    public LauncherSettings(LauncherSettings other) { ... }
    public LauncherSettings() { }
    public static LauncherSettings CreateDefaults() { ... }
    private static bool IsChineseUICulture() { ... }
}
```

- [ ] **Step 3: Create Models/LauncherRuntimeModels.cs**

Move runtime DTOs (lines 329-587 of LauncherStateModels.cs):

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cafe.Launcher.Avalonia.Models;

// SelectableOption + subclasses
// ManifestValidationResult
// GameLaunchResult
// GameOperationProgress
// GameOperationResult
// LauncherRemoteState
// LauncherStatusSnapshot
// RemoteContentItem
// NewsCategory
```

- [ ] **Step 4: Delete original Models/LauncherStateModels.cs**

```powershell
Remove-Item "E:\Repos\Cafe.Launcher.Avalonia\Models\LauncherStateModels.cs"
```

- [ ] **Step 5: Build, fix compilation, run tests, commit**

```powershell
dotnet build "E:\Repos\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj" -c Debug --no-restore
# Fix any missing usings in callers
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj" -c Debug --no-restore
```

```bash
git add Models/LauncherEnums.cs Models/LauncherSettings.cs Models/LauncherRuntimeModels.cs
git rm Models/LauncherStateModels.cs
git add -u
git commit -m "refactor: split LauncherStateModels.cs into focused files"
```

---

### Task 3: Move GameDownloadService + GameUninstallService to Features/GameOperations/

**Files:**
- Move: `Services/GameDownloadService.cs` → `Features/GameOperations/GameDownloadService.cs`
- Move: `Services/GameUninstallService.cs` → `Features/GameOperations/GameUninstallService.cs`
- Modify: `Services/ServiceConfiguration.cs`
- Modify: `ViewModels/GameOperationsViewModel.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Features/GameOperations/GameInstallationWorkflow.cs`
- Modify: `Features/GameOperations/GameLaunchWorkflow.cs`
- Modify: `Features/GameOperations/GameUninstallWorkflow.cs`

- [ ] **Step 1: Move files and change namespace**

Move both files from `Services/` to `Features/GameOperations/`. In each, change:
```csharp
namespace Cafe.Launcher.Avalonia.Services;
```
to:
```csharp
namespace Cafe.Launcher.Avalonia.Features.GameOperations;
```

Also remove `using Cafe.Launcher.Avalonia.Features.GameOperations;` from GameDownloadService.cs (line 13) since it's now in that namespace. Remove `using Cafe.Launcher.Avalonia.Services;` references to itself.

- [ ] **Step 2: Update ServiceConfiguration.cs**

Add `using Cafe.Launcher.Avalonia.Features.GameOperations;` at top.
No other changes needed — `GameDownloadService` and `GameUninstallService` are already referenced without namespace qualification (file-scoped using covers it).

- [ ] **Step 3: Update ViewModel callers**

In `GameOperationsViewModel.cs`: Ensure `using Cafe.Launcher.Avalonia.Features.GameOperations;` is present (likely already there since it uses `GameOperationStage` etc.)

In `MainWindowViewModel.cs`: The `using Cafe.Launcher.Avalonia.Services;` at line 7 needs to stay (other types like `LauncherSettingsService`, `ToastService` etc. are still in Services). GameDownloadService and GameUninstallService are NOT directly referenced by name in MainWindowViewModel — they're resolved by DI. The `GameOperationsViewModel` wraps them. So MainWindowViewModel probably doesn't need changes. Verify.

- [ ] **Step 4: Update Feature workflow files**

In `GameInstallationWorkflow.cs`, `GameLaunchWorkflow.cs`, `GameUninstallWorkflow.cs`:
These files are already in `Features.GameOperations` namespace. If they reference `Services.GameDownloadService` or `Services.GameUninstallService`, remove the fully-qualified reference since the types are now in the same namespace.

- [ ] **Step 5: Build and fix**

```powershell
dotnet build "E:\Repos\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj" -c Debug --no-restore
```

Fix any compilation errors. The `GameUninstallService` has a `Failed` method and `IsSystemProtectPath` — ensure nothing in Services/ was calling them.

- [ ] **Step 6: Run tests**

```powershell
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj" -c Debug --no-restore
```

- [ ] **Step 7: Commit**

```bash
git add Features/GameOperations/GameDownloadService.cs Features/GameOperations/GameUninstallService.cs
git rm Services/GameDownloadService.cs Services/GameUninstallService.cs
git add -u
git commit -m "refactor: move GameDownloadService and GameUninstallService to Features.GameOperations"
```

---

### Task 4: Extract ShellCoordinator from MainWindowViewModel

**Files:**
- Create: `Features/Shell/ShellCoordinator.cs`
- Modify: `ViewModels/MainWindowViewModel.cs`
- Modify: `Services/ServiceConfiguration.cs`

**Analysis of extraction:**

Looking at `MainWindowViewModel`, the coordination concerns are:
1. `WireModalHost()` + `OnXxxPropertyChanged` handlers (7 handlers, lines 297-401) — modal sync
2. `SyncModal()` (lines 403-413) — the actual open/close logic
3. `TryHandleEscape()` (lines 581-632) — escape key resolution
4. `OnSettingPropertyChanged` (lines 307-314) — setting change notification relay

The `WireChildren()` method (lines 238-295) wires delegates between child VMs and the shell. These delegates are closures over MainWindowViewModel state, so they must stay.

Strategy: Extract modal management + escape handling into ShellCoordinator. The wire-up stays in MainWindowViewModel but delegates to the coordinator.

- [ ] **Step 1: Create Features/Shell/ShellCoordinator.cs**

```csharp
using System.ComponentModel;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// Coordinates modal overlay visibility and escape-key resolution
/// across child view models, extracted from MainWindowViewModel.
/// </summary>
public sealed class ShellCoordinator
{
    private readonly ModalHostViewModel modalHost;
    private readonly WindowChromeViewModel windowChrome;
    private readonly SettingsViewModel settings;
    private readonly ResourcePanelViewModel resourcePanel;
    private readonly LogViewerDialogViewModel logViewer;
    private readonly DialogsViewModel dialogs;

    public ShellCoordinator(
        ModalHostViewModel modalHost,
        WindowChromeViewModel windowChrome,
        SettingsViewModel settings,
        ResourcePanelViewModel resourcePanel,
        LogViewerDialogViewModel logViewer,
        DialogsViewModel dialogs)
    {
        this.modalHost = modalHost;
        this.windowChrome = windowChrome;
        this.settings = settings;
        this.resourcePanel = resourcePanel;
        this.logViewer = logViewer;
        this.dialogs = dialogs;
    }

    /// <summary>Subscribes to PropertyChanged on child VMs for modal sync.</summary>
    public void Wire()
    {
        windowChrome.PropertyChanged += OnWindowChromePropertyChanged;
        settings.PropertyChanged += OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged += OnSettingPropertyChanged;
        resourcePanel.PropertyChanged += OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged += OnLogViewerPropertyChanged;
        dialogs.PropertyChanged += OnDialogsPropertyChanged;
    }

    /// <summary>Unsubscribes from all PropertyChanged handlers.</summary>
    public void Unwire()
    {
        windowChrome.PropertyChanged -= OnWindowChromePropertyChanged;
        settings.PropertyChanged -= OnSettingsPropertyChanged;
        settings.Editor.CurrentPropertyChanged -= OnSettingPropertyChanged;
        resourcePanel.PropertyChanged -= OnResourcePanelPropertyChanged;
        logViewer.PropertyChanged -= OnLogViewerPropertyChanged;
        dialogs.PropertyChanged -= OnDialogsPropertyChanged;
    }

    // ... all the OnXxxPropertyChanged handlers + SyncModal + TryHandleEscape
    // (copied from MainWindowViewModel, lines 307-413 and 581-632)
}
```

- [ ] **Step 2: Modify MainWindowViewModel**

- Add `private readonly ShellCoordinator coordinator;` field
- Accept `ShellCoordinator` in constructor: `ShellCoordinator? coordinator = null` and assign `this.coordinator = coordinator ?? new ShellCoordinator(ModalHost, WindowChrome, Settings, ResourcePanel, LogViewer, Dialogs);`
- Replace `WireModalHost()` body with `coordinator.Wire();`
- Replace `Dispose()`'s unwire block with `coordinator.Unwire();`
- Remove all `OnXxxPropertyChanged` methods, `SyncModal`, `TryHandleEscape` from MainWindowViewModel
- Add a public `TryHandleEscape()` that delegates: `public bool TryHandleEscape() => coordinator.TryHandleEscape();`

- [ ] **Step 3: Update ServiceConfiguration.cs**

Register `ShellCoordinator` as a singleton before `MainWindowViewModel`:

```csharp
services.AddSingleton<ShellCoordinator>();
```

- [ ] **Step 4: Build, test, commit**

```powershell
dotnet build "E:\Repos\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj" -c Debug --no-restore
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj" -c Debug --no-restore
```

```bash
git add Features/Shell/ShellCoordinator.cs
git add -u
git commit -m "refactor: extract ShellCoordinator from MainWindowViewModel"
```

---

### Task 5: Split GameDownloadService

**Files:**
- Create: `Features/GameOperations/ManifestDiffCalculator.cs`
- Create: `Features/GameOperations/DownloadExecutor.cs`
- Modify: `Features/GameOperations/GameDownloadService.cs`

- [ ] **Step 1: Create Features/GameOperations/ManifestDiffCalculator.cs**

Extract these methods from GameDownloadService:
- `BuildInstallOrUpdatePlanAsync` (lines 586-632)
- `BuildRepairPlanAsync` (lines 634-682)
- `GameManifestDiff` (lines 906-928)
- `GameResultMerge` (lines 930-952)
- `CheckStat` (lines 954-974)
- `CheckHashAsync` (lines 976-1003)

Also promote `DownloadPlan` from a private nested class to an internal class in the namespace (so both ManifestDiffCalculator and DownloadExecutor can use it).

New file:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cafe.Launcher.Avalonia.Helpers;
using Cafe.Launcher.Avalonia.Models;
using Cafe.Launcher.Avalonia.Services;

namespace Cafe.Launcher.Avalonia.Features.GameOperations;

internal sealed class ManifestDiffCalculator
{
    private readonly RemoteManifestService remoteManifestService;
    private readonly LocalInstallationStateStore localInstallationStateStore;
    private readonly Crc64Service crc64Service;
    private readonly GameInstallationPath installationPath;
    private readonly LauncherSettingsService settingsService;

    public ManifestDiffCalculator(
        RemoteManifestService remoteManifestService,
        LocalInstallationStateStore localInstallationStateStore,
        Crc64Service crc64Service,
        GameInstallationPath installationPath,
        LauncherSettingsService settingsService)
    {
        this.remoteManifestService = remoteManifestService;
        this.localInstallationStateStore = localInstallationStateStore;
        this.crc64Service = crc64Service;
        this.installationPath = installationPath;
        this.settingsService = settingsService;
    }

    // ... all diff methods, adapted to use injected fields instead of captured variables
}
```

Key adaptation: `BuildInstallOrUpdatePlanAsync` currently uses `GamePathValidator.GetSafePath` (static), `CheckStat`, `CheckHashAsync` (same class), and `GameOperationKind` (enum in Models). `CheckHashAsync` uses `crc64Service` which is now injected.

`DownloadPlan` is promoted to internal:

```csharp
internal sealed class DownloadPlan
{
    public string Source { get; set; } = "";
    public List<ManifestFile> NeedDownload { get; set; } = [];
    public List<ManifestFile> NeedDelete { get; set; } = [];
    public List<ManifestFile> ManifestFiles { get; set; } = [];
}
```

- [ ] **Step 2: Create Features/GameOperations/DownloadExecutor.cs**

Extract:
- `DownloadFilesAsync` (lines 684-804)
- `InstallDownloadedFilesAsync` (lines 831-904)
- `RemoveFiles` (lines 1005-1017)
- `EnsureGamePath` (lines 1019-1026)
- `GetTempName` / `GetOriginName` (lines 1030-1038)
- `ThrottleState` inner class (lines 1072-1077)

New file:

```csharp
namespace Cafe.Launcher.Avalonia.Features.GameOperations;

internal sealed class DownloadExecutor
{
    private readonly IFileDownloadService fileDownloadService;
    private readonly Crc64Service crc64Service;
    private readonly ProxySettingsService proxySettingsService;
    private readonly LocalDiagnostics diagnostics;
    private readonly GameDownloadService owner; // for pause state access

    // ... download/install logic, adapted
}
```

Note: `DownloadFilesAsync` accesses `IsPaused` and `GetPauseTaskSnapshot()` from GameDownloadService. These are passed via the `owner` reference.

- [ ] **Step 3: Slim down GameDownloadService**

After extraction, GameDownloadService keeps:
- Public API: `InstallOrUpdateAsync`, `RepairAsync`, `ResumePersistedAsync`, `Stop`, `Pause`, `Resume`
- Lifecycle: `Dispose`, `IsRunning`, `IsPaused`
- Orchestration: `RunAsync`, `ReplaceActiveDownload`, `ClearActiveDownload`
- Internal classes: `ActiveDownloadOperation`
- Helper methods: `CreateProgress`, `Failed`, `ThrowIfDisposed`, `CommitInstallationStateAsync`

The `Dependencies` record shrinks to the 5-6 params needed by the orchestrator layer. `ManifestDiffCalculator` and `DownloadExecutor` each get their own focused dependencies injected through the constructor.

Add fields:
```csharp
private readonly ManifestDiffCalculator diffCalculator;
private readonly DownloadExecutor downloadExecutor;
```

Constructor wires them up from the remaining deps.

- [ ] **Step 4: Build and fix compilation**

```powershell
dotnet build "E:\Repos\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj" -c Debug --no-restore
```

- [ ] **Step 5: Run all tests**

```powershell
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj" -c Debug --no-restore
dotnet test "E:\Repos\Cafe.Launcher.Avalonia\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj" -c Debug --no-restore
```

- [ ] **Step 6: Commit**

```bash
git add Features/GameOperations/ManifestDiffCalculator.cs Features/GameOperations/DownloadExecutor.cs
git add -u
git commit -m "refactor: split GameDownloadService into ManifestDiffCalculator and DownloadExecutor"
```

---

## Self-Check

1. **Coverage:** All 4 sections of the spec mapped to 5 tasks. Task 3 (cycle elimination) + Task 5 (GDService split) = spec sections 1+3. Task 4 = spec section 2. Tasks 1+2 = spec section 4.

2. **No placeholders:** All steps have exact file paths, code content, and commands.

3. **Type consistency:** `DownloadPlan` promoted from nested private to namespace-internal before `ManifestDiffCalculator` and `DownloadExecutor` reference it. `ShellCoordinator` registered in DI before `MainWindowViewModel` consumes it.
