# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
.\build.ps1                              # Debug build (expect 0 warnings, 0 errors)
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore   # Release build
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish   # Self-contained publish (win-x64)
```

**Tests** (xUnit, under `tests/Cafe.Launcher.Avalonia.Tests/`):
```powershell
dotnet test                                                    # Run all tests
dotnet test --filter "FullyQualifiedName~VersionComparerTests" # Run a single test class
```

CI is GitHub Actions (`.github/workflows/build.yml`) on `windows-latest`, .NET 10.0.x — restore, Debug build, Release build, and self-contained publish.

**Telemetry must be off during local builds** (already set in `build.ps1`):
- `DOTNET_CLI_TELEMETRY_OPTOUT=1`
- `AVALONIA_TELEMETRY_OPTOUT=1`

## Architecture

**Tech stack**: .NET 10.0, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.1 (source generators), Material.Icons.Avalonia, Fluent Theme. Compiled bindings enabled by default.

**MVVM pattern** with `ViewLocator` convention: `FooViewModel` → `FooView` by string replacement. ViewModelBase extends `ObservableObject`.

### Single-window desktop app

One `MainWindow` (1300×754, non-resizable, borderless with custom chrome). The ViewModel drives panel visibility through boolean flags (`IsInstallPanelVisible`, `IsControlPanelVisible`, `IsProgressPanelVisible`, `IsSettingsVisible`).

**View files** (XAML split by concern, all under `Views/`):
- `MainWindow.axaml` — window shell, title bar, remote content panel, bottom install/progress/control panels
- `MainWindow.Styles.axaml` — all `Window.Styles` extracted via `<StyleInclude Source="avares://..."/>`
- `MainWindowSettingsOverlay.axaml` — settings dialog overlay
- `MainWindowDialogsOverlay.axaml` — notice popup, repair/uninstall confirmation dialogs
- `MainWindowToastOverlay.axaml` — toast notification overlay

**Entries:**
1. **Program.cs** — Process mutex (`Global\Cafe_Launcher_SI`), single-instance enforcement via `EventWaitHandle` signal, global crash logging to `%LOCALAPPDATA%\Cafe Launcher\crash.log`.
2. **App.axaml.cs** — On framework init: creates `LauncherApplicationServices` (the composition root), calls `CreateMainWindowViewModel()` to get the ViewModel, creates `MainWindow`, wires `ClickCodeService`, `SystemTrayService`. Starts a background thread listening for `EventWaitHandle` signals to restore window from tray.
3. **App.axaml** — Light/Dark `ThemeDictionaries` with custom `Launcher*` brushes, `ViewLocator` data template, FluentTheme + MaterialIconStyles.

**Composition root**: `LauncherApplicationServices` is the DI container — it instantiates all services and wires their dependencies in the constructor. Used by both `App.axaml.cs` (real app) and tests (via `CreateMainWindowViewModel()`). Order of construction matters: services that depend on others are constructed after their dependencies.

**View code-behind** (`MainWindow.axaml.cs`): handles native folder-picker dialog (via `StorageProvider`), window drag-to-move (borderless chrome), and close-behavior routing (minimize-to-tray vs exit). The ViewModel receives `PickGameFolderAsync`, `MinimizeWindow`, and `CloseWindow` delegates via `ConfigureViewModel()`.

### Core data flow

`LauncherCoreService.LoadAsync()` is the central orchestrator:
1. Reads local `settings.json` via `LauncherSettingsService`
2. Fires 6 parallel API calls via `LauncherApiClient` (game config, base config, CDN config — plus 3 optional: operations, social media, installation config)
3. Reads local `game-launcher-config.json` + `manifest.json` via `LocalGameStateService`
4. Computes `IsInstalled`, `NeedsUpdate`, `BelowLowestVersion` from version comparison
5. Returns a single `LauncherStatusSnapshot` consumed by the ViewModel

### Services (all in `Services/`)

| Service | Role |
|---|---|
| `LauncherApiClient` | HTTP to `api-launcher-jp.yo-star.com`, MD5-signed `Authorization` header, envelope unwrapping |
| `LauncherCoreService` | Orchestrates API + local state into `LauncherStatusSnapshot` |
| `LauncherSettingsService` | Reads/writes `settings.json` at `%LOCALAPPDATA%\Cafe Launcher\`, normalizes enum values, handles legacy camelCase fields |
| `LocalGameStateService` | Reads local `game-launcher-config.json` + `manifest.json`, normalizes paths to `YostarGames\BlueArchive_JP` |
| `GameDownloadService` | Install/update/repair: manifest diff → parallel CDN download (10 concurrent, `.tmp` files, `Range` resume, CRC64 verify, rename on success). Supports download speed throttling, async pause/resume via `TaskCompletionSource`. Implements `IDisposable` — thread-safe CTS management via `activeDownloadLock`. |
| `GameLaunchService` | Manifest validation + process launch |
| `GameUninstallService` | Guarded uninstall (checks path safety, exe not running, deletes only manifest-listed files) |
| `LocalizationService` | Inline dictionaries for `en`/`zh-Hans`/`ja`; `auto` resolves via `CultureInfo.CurrentUICulture` |
| `SystemTrayService` | Avalonia 12 `TrayIcon` + `NativeMenu` for minimize-to-tray |
| `ToastService` | Event-based transient notifications (info/success/warning/error) |
| `LocalDiagnostics` | Appends to `diagnostics.log` in the settings folder |
| `PatchUrlGroupService` | URL rewriting between Official and Cafe CDN hosts for manifest + CDN config URLs |
| `NoticeStateService` | Tracks which notice IDs have been shown (persisted to `shown_notices.json`) |
| `Crc64Service`, `OfficialHashService`, `ProxySettingsService`, `DiskSpaceService`, `ProcessService`, `VersionComparer`, `ClickCodeService`, `DownloadStateService`, `ImageCacheService`, `ExternalLinkService`, `ManifestValidationService`, `LauncherUpdateService` | Supporting services |

### Key models (`Models/`)

- `LauncherApiContracts.cs` — All API response DTOs
- `LauncherStateModels.cs` — String constants for modes/behaviors (`LaunchCheckModes`, `ProxyModes`, `CloseBehaviors`, `LauncherLanguages`, `ThemeModes`, `DownloadSpeedLimits`, `PatchUrlGroups`), plus runtime state objects (`LauncherStatusSnapshot`, `LauncherRemoteState`, `LocalGameState`, `LauncherSettings`, `GameOperationProgress`, `GameOperationResult`), and option types (`SettingOption`, `LanguageOption`, `ThemeOption`) for localized dropdown binding
- `LocalGameContracts.cs` — `LocalManifest`, `RemoteManifest`, `ManifestFile`, `GameLauncherConfig`
- `PatchUrlGroupDefinition.cs` — Code + host-from/to tuples for CDN URL rewriting
- `DownloadTaskState.cs` — Serializable download resume state
- `BannerDot.cs` — Observable carousel dot indicator

### Constants

`LauncherConstants` holds: `ProductName`, `LauncherVersion` ("1.7.2"), `ApiBaseUrl`, `AuthorizationSalt`, `OfficialWebsiteUrl`, `UpdatePackageUrl`, path/filename conventions.

### Patch URL groups

Users can switch between `Official` (yo-star.com) and `Cafe` (bluearchive.cafe) CDN hosts for downloading game files. The `PatchUrlGroupService` defines host-rewrite rules, and `LauncherApiClient.RewriteManifestUrl()` / `GameDownloadService.BuildDownloadUrl()` apply them when constructing download URLs. The setting is persisted as `patchUrlGroup` in `settings.json`. A sentinel test ensures URL rewriting scope is strictly limited to package download hosts — no status/list, serverinfo, or SDK netloc endpoints are touched.

### Converters

`UrlToBitmapConverter` (`Converters/`) — converts image URLs to `Bitmap?` for XAML binding, used for remote banner/avatar images.

### Reference docs

- `docs/architecture.md` — current Avalonia rewrite behavior and implementation notes
- `docs/report.md` — concise original Electron launcher analysis
- `docs/bluearchive_jp_gamelauncher_analysis.md` — detailed decompiled launcher report

### Localization

All UI strings go through `LocalizationService.T(key)` and `LocalizationService.F(key, args)` for formatted strings. `LocalizedStrings` (generated by CommunityToolkit source generators) exposes individual `[ObservableProperty]` properties for XAML binding: `{Binding I18n.Settings}`, etc.

**Adding localized strings:**
1. Add the key to all 3 language dictionaries (`en`, `zh-Hans`, `ja`)
2. Add an `[ObservableProperty]` field to `LocalizedStrings`
3. Wire it in `LocalizedStrings.Apply()`

**Localized dropdown values** follow the same pattern as `ThemeOption`: create `SettingOption` instances with `Code` (the persisted value) and `DisplayName` (set from `localizer.T()` in a `Refresh*Options()` method called from `ApplyLanguage()`). Bind the ComboBox with `SelectedValue="{Binding SelectedX}"` + `SelectedValueBinding="{Binding Code}"` + an `ItemTemplate` showing `{Binding DisplayName}`.

### Theme

Light/Dark themes defined as `ThemeDictionaries` in `App.axaml` with custom `Launcher*` brush keys. `ThemeModes.System` → `ThemeVariant.Default` (follows OS), `Light`/`Dark` → explicit. Applied via `Application.Current.RequestedThemeVariant`.

### Single-instance pattern

`Program.cs` uses a named global `Mutex`. Second instances signal the first via `EventWaitHandle`, which triggers `Dispatcher.UIThread.InvokeAsync` to restore the window from tray/minimized state. Windows-only (`EventWaitHandle` is not supported on Linux — see commit `19db5a3`).

### API auth

`AuthorizationHeaderFactory` builds a JSON header with `{head: {game_tag, time, version}, sign: MD5(headJson + data + salt)}`. Salt is in `LauncherConstants.AuthorizationSalt`.

## Important patterns

- **No remote telemetry**: The original Electron launcher sent logs to Aliyun SLS. This rewrite explicitly excludes those paths (`/api/launcher/advanced/config`, `/api/open/api/config`). Always keep diagnostics local.
- **Path safety**: `GameDownloadService.GetSafePath()` validates that all file operations stay within the game directory — path traversal is rejected.
- **Download resilience**: CRC64 verification after download, rename `.tmp` → final only on success, up to 3 install-verification retries, CDN failover (primary → backup with retry order).
- **Async pause**: `GameDownloadService` uses `TaskCompletionSource`-based pause (never blocks threads). `Pause()` creates a pending `TaskCompletionSource`, download loops `await` it, `Resume()` completes it. `Stop()` also completes the TCS to unblock paused awaits before cancellation.
- **Spacing**: UI spacing follows a 4px grid (0, 4, 8, 12, 16, 20, 24, …). Left panel margin and bottom panel horizontal padding are both 40px for visual symmetry.
- **Version comparison**: `VersionComparer.Compare()` returns -1/0/1 for old/equal/new.
- **XAML extraction**: Large XAML blocks (styles, overlays) are extracted into separate `.axaml` files under `Views/` and referenced via `<StyleInclude>` or `Classes` attributes. The main `MainWindow.axaml` keeps only the window shell and content grid.
