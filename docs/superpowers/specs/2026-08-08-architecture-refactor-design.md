# Architecture Refactor Design (2026-08-08)

## 1. Eliminate Services ↔ Features circular dependency

**Root cause:** `Services/GameDownloadService.cs` and `Services/GameUninstallService.cs` reference types from `Features.GameOperations` (`DownloadCheckpointStore`, `GameOperationStage`), while `Features/` files reference `Services/`. Both classes belong in the game-operations feature, not in shared infrastructure.

**Moves:**

- `Services/GameDownloadService.cs` → `Features/GameOperations/GameDownloadService.cs`
- `Services/GameUninstallService.cs` → `Features/GameOperations/GameUninstallService.cs`

**Namespace change:** `Cafe.Launcher.Avalonia.Services` → `Cafe.Launcher.Avalonia.Features.GameOperations`

**Rule for `Services/` residency:** A class stays in `Services/` only if it is used by ≥2 features or is cross-cutting infrastructure (logging, DI, localization, HTTP, persistence). Everything else belongs under its owning feature.

**Kept in `Services/`:** `GamePathValidator`, `Crc64Service` — used by multiple features.

**Affected callers that need `using` updates:**

- `Services/ServiceConfiguration.cs`
- `ViewModels/GameOperationsViewModel.cs`
- `ViewModels/MainWindowViewModel.cs`
- `Features/GameOperations/GameInstallationWorkflow.cs`
- `Features/GameOperations/GameLaunchWorkflow.cs`
- `Features/GameOperations/GameUninstallWorkflow.cs`

---

## 2. Extract ShellCoordinator from MainWindowViewModel

**Root cause:** `MainWindowViewModel` (670 lines, 13 constructor params) mixes three concerns: child-VM properties for XAML binding, window-level coordination (restore, tray, theme), and `Action<>` delegate factories for child-VM upward communication.

**New class `ShellCoordinator` (~120 lines):**

- Window restore / minimize-to-tray / theme-toggle / single-instance signaling
- Produces the `Action<>` delegates that child VMs call to reach parent capabilities
- Injected into `MainWindowViewModel` as a single constructor parameter

**`MainWindowViewModel` after extraction (~400 lines, ~9 constructor params):**

- Holds child VM properties for XAML bindings (unchanged)
- Delegates coordination to `ShellCoordinator`
- `MainWindow.axaml` binding paths unchanged

---

## 3. Split GameDownloadService

**Root cause:** `GameDownloadService` (1109 lines) combines four distinct responsibilities in a single class.

**New structure:**

| Class | Lines | Responsibility |
| --- | --- | --- |
| `GameDownloadService` | ~300 | Orchestration; public API unchanged (`StartAsync`, `PauseAsync`, `ResumeAsync`) |
| `ManifestDiffCalculator` | ~150 | Remote vs. local manifest diff → `FileDownloadRequest` list |
| `DownloadExecutor` | ~350 | 10-thread concurrent download, Range resume, `.tmp` rename, pause/resume |
| `DownloadCheckpointStore` | (exists) | Checkpoint persistence and restore |

`Crc64Service` stays independent (already used by multiple classes).

**Dependencies:** The `Dependencies` record (11 fields) is dissolved. Each sub-class receives only the 3–4 dependencies it needs, passed through `GameDownloadService`'s constructor.

**Caller impact:** None. `GameDownloadService` public method signatures unchanged.

---

## 4. Consolidate Helpers and split models

**Helpers moved from `Services/` to `Helpers/`:**

| From | To |
| --- | --- |
| `Services/ColorUtils.cs` | `Helpers/ColorUtils.cs` |
| `Services/VersionComparer.cs` | `Helpers/VersionComparer.cs` |
| `Services/ProcessService.cs` | `Helpers/ProcessService.cs` |
| `Services/MotionSettingsResolver.cs` | `Helpers/MotionSettingsResolver.cs` |
| `Services/AnimationTimings.cs` | `Helpers/AnimationTimings.cs` |

**Rule:** Pure static methods or single-instance stateless utilities → `Helpers/`. DI-injected, stateful services → `Services/`.

**`Models/LauncherStateModels.cs` (587 lines) split into:**

| File | Lines | Contents |
| --- | --- | --- |
| `Models/LauncherEnums.cs` | ~200 | `ProxyModes`, `ThemeModes`, `GameState`, etc. |
| `Models/LauncherSettings.cs` | ~200 | Persistence model with `[JsonPropertyName]`, `ObservableObject` |
| `Models/LauncherRuntimeState.cs` | ~100 | Runtime derived state (download progress, install status) |
| `Models/LauncherStatusSnapshot.cs` | (exists) | Already a clean separate file |

`LauncherApiContracts.cs` (231 lines) stays as-is — clean DTO collection.

---

## Constraints

- Single-developer project; fan-out abstractions out of your way
- Zero-warning build enforced by the existing `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`
- All existing tests must pass at every commit boundary
- `ServiceConfiguration.cs` disposal order must remain valid after moves
- Existing public method signatures on `GameDownloadService` preserved
