# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
.\build.ps1                              # Debug build (expect 0 warnings, 0 errors)
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore   # Release build
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish   # Self-contained publish (win-x64)
```

**Tests** (xUnit 2.9.3, under `tests/Cafe.Launcher.Avalonia.Tests/`, with coverlet 10.0.1):
```powershell
dotnet test                                                    # Run all tests
dotnet test --filter "FullyQualifiedName~VersionComparerTests" # Run a single test class
```

Available test classes: `VersionComparerTests`, `LauncherApiClientTests`, `LauncherConstantsTests`, `LauncherSettingsServiceTests`, `LocalizationServiceTests`, `MainWindowViewModelTests`, `GameDownloadServiceTests`, `PatchUrlGroupServiceTests`, `BestHttpCookieLibraryServiceTests`, `ResourcePanelUidServiceTests`, `ExternalLinkServiceTests`, `ResourcePanelApiClientTests`, `LocalGameStateServiceTests`.

CI is GitHub Actions on `windows-latest`, .NET 10.0.x:
- **build.yml** (push/PR to `main`): restore, Debug build, Release build, self-contained publish, upload artifact.
- **release.yml** (push of `v*` tag): restore, Release build, publish, ZIP archive, auto-generated changelog from git log, GitHub Release via `softprops/action-gh-release@v2`. Pre-release if tag contains `-`.

**Telemetry must be off during local builds** (already set in `build.ps1`):
- `DOTNET_CLI_TELEMETRY_OPTOUT=1`
- `AVALONIA_TELEMETRY_OPTOUT=1`

## Release workflow

```powershell
.\release.ps1 patch                  # Bump patch version, generate changelog, commit, tag, push
.\release.ps1 minor -DryRun          # Preview minor bump without modifying files
.\release.ps1 2.0.0-beta.1          # Explicit version (prerelease if tag contains -)
.\release.ps1 patch -SkipPush        # Commit + tag locally, don't push to origin
```

`release.ps1` reads `<VersionPrefix>` from the `.csproj`, bumps it, generates `CHANGELOG_RELEASE.md` from git log since last tag (grouped by conventional commit prefix: feat/fix/refactor/perf), updates `AssemblyVersion`/`FileVersion`, commits, creates an annotated tag, and pushes. The actual build + GitHub Release is handled by `release.yml` CI on tag push.

## Architecture

**Tech stack**: .NET 10.0, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.2 (source generators), Material.Icons.Avalonia, Fluent Theme. Compiled bindings enabled by default.

**MVVM pattern** with `ViewLocator` convention: `FooViewModel` → `FooView` by string replacement. ViewModelBase extends `ObservableObject`.

### Single-window desktop app

One `MainWindow` (1300×754, non-resizable with MinWidth 1024/MinHeight 640, borderless with custom chrome). The ViewModel is split into composed sub-ViewModels, each owning a distinct concern:

| Sub-ViewModel | Concern |
|---|---|
| `ShellViewModel` | Product name, version, runtime info, status text, game path display |
| `BackgroundViewModel` | Wallpaper (bundled / remote / custom), theme-color extraction |
| `RemoteContentViewModel` | Announcements, banners, news, social media from API |
| `DialogsViewModel` | Notice popup, repair/uninstall confirmation dialogs |
| `GameOperationsViewModel` | Install / update / repair / launch / uninstall commands and progress |
| `ToastHostViewModel` | Transient toast notification queue |
| `WindowChromeViewModel` | Title bar, minimize/close buttons, window drag state |
| `SettingsViewModel` | Settings panel: language, theme, download source, launch check, speed limit, close behavior, proxy, background, game path |
| `ResourcePanelViewModel` | Resource panel (UID-based game resource display) |

**View files** (XAML split by concern, all under `Views/`):
- `MainWindow.axaml` — window shell, title bar, remote content panel, bottom install/progress/control panels
- `MainWindow.Styles.axaml` — all `Window.Styles` extracted via `<StyleInclude Source="avares://..."/>`
- `MainWindowSettingsOverlay.axaml` — settings dialog overlay
- `MainWindowDialogsOverlay.axaml` — notice popup, repair/uninstall confirmation dialogs
- `MainWindowToastOverlay.axaml` — toast notification overlay

**Entries:**
1. **Program.cs** — Process mutex (`Global\Cafe_Launcher_SI`), single-instance enforcement via `EventWaitHandle` signal, global crash logging to `%LOCALAPPDATA%\Cafe Launcher\crash.log`.
2. **App.axaml.cs** — On framework init: builds DI container via `ServiceConfiguration.AddLauncherServices()`, resolves `MainWindowViewModel`, creates `MainWindow`, wires `ClickCodeService`, `SystemTrayService`. Starts a background thread listening for `EventWaitHandle` signals to restore window from tray.
3. **App.axaml** — Light/Dark `ThemeDictionaries` with custom `Launcher*` brushes, `ViewLocator` data template, FluentTheme + MaterialIconStyles.

**Composition root**: `ServiceConfiguration.AddLauncherServices()` is the DI configuration — it registers all services with `Microsoft.Extensions.DependencyInjection`. The container is built in `App.axaml.cs` via `ServiceCollection.BuildServiceProvider()`. Every service is registered as `AddSingleton`; every ViewModel is registered as `AddTransient` (fresh instance per resolution). Thread-safe disposal order for IDisposable services is defined by reverse registration order (see disposal order section below).

**ViewModel coordination**: Sub-ViewModels communicate with `MainWindowViewModel` through two mechanisms:
- **Delegates** — `MainWindowViewModel.ConfigureViewModel()` sets `Func<>` / `Func<Task>` delegates on children (e.g. `SettingsViewModel.PickGameFolderAsync`, `SettingsViewModel.GetSnapshot`). These let children call back into parent capabilities (folder pickers, state queries).
- **Events** — Children expose `event Func<Task>?` / `event Action?` that the parent subscribes to (e.g. `SettingsViewModel.SettingsSaved`, `SettingsViewModel.CloseRequested`). This decouples child-triggered actions from parent handling.

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
| `LauncherApiClient` | HTTP to `api-launcher-jp.yo-star.com`, MD5-signed `Authorization` header, envelope unwrapping. Implements `IDisposable`. |
| `LauncherCoreService` | Orchestrates API + local state into `LauncherStatusSnapshot`. Exposed as `ILauncherCoreService` in the DI container. |
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
| `HttpClientFactory` | Centralized factory for pre-configured `HttpClient` instances with shared `SocketsHttpHandler` pooling (15-min connection lifetime). Proxy-aware lease creation via `CreateLeaseAsync()`. Registered as singleton; implements `IDisposable`. |
| `ProxySettingsService` | Creates proxy-aware `SocketsHttpHandler` instances for `HttpClientFactory` |
| `ResourcePanelApiClient` | HTTP client for resource panel data. Implements `IDisposable`. |
| `ResourcePanelUidService` | Manages resource panel UID state |
| `BestHttpCookieLibraryService` | Cookie handling for HTTP requests |
| `ThemeColorExtractionService` | Extracts dominant colors from wallpaper images for UI theming |
| `ImageCacheService` | Caches downloaded images (banners, avatars). Implements `IDisposable`. |
| `ManifestValidationService` | Validates local game files against manifest |
| `LauncherUpdateService` | Checks for and downloads launcher updates |
| `ExternalLinkService` | Opens external URLs in the default browser |
| `DownloadStateService` | Serializes/resumes download state to `download_state.json` |
| `OriginalLauncherMigrationService` | Migrates game installation path from the original Electron launcher on first run |
| `Crc64Service`, `OfficialHashService`, `DiskSpaceService`, `ProcessService`, `VersionComparer`, `ClickCodeService` | Supporting services |

**HttpClient lifecycle**: `HttpClientFactory` owns a single shared `SocketsHttpHandler` (pooled, 15-min connection lifetime). Callers get `HttpClient` instances that share this handler and must NOT dispose them. For proxy-aware requests, `CreateLeaseAsync()` returns an `HttpClientLease` that conditionally owns its handler — callers dispose the lease, not the client. `HttpClientLease.Dispose()` only disposes the handler when it was created per-request (proxy mode); for direct connections, disposal is a no-op since the handler is shared.

**IDisposable service disposal order** (reverse registration = forward dispose):
1. `LauncherApiClient` — disposed first
2. `ResourcePanelApiClient`
3. `ImageCacheService`
4. `GameDownloadService` — disposed last

The DI container calls `Dispose()` on these in reverse registration order when the service provider is disposed.

### Local files (`%LOCALAPPDATA%\Cafe Launcher\`)

| File | Purpose |
|---|---|
| `settings.json` | Launcher settings (see Settings reference below) |
| `diagnostics.log` | Runtime diagnostics appended by `LocalDiagnostics` |
| `crash.log` | Global unhandled exception log (written by `Program.cs`) |
| `download_state.json` | Serializable download resume state (`DownloadStateService`) |
| `shown_notices.json` | Tracked shown notice IDs (`NoticeStateService`) |
| `clickCode` | Install attribution code (`ClickCodeService`) |

### Settings reference

Persisted fields in `settings.json` and their valid values:

| Setting | JSON key | Valid codes |
|---|---|---|
| Language | `language` | `auto`, `en`, `zh-Hans`, `ja` |
| Theme | `themeMode` | `system`, `light`, `dark` |
| Patch URL group | `patchUrlGroup` | `official`, `cafe` |
| Launch check | `launchCheckMode` | `LocalManifest`, `RemoteManifest`, `None` |
| Download speed limit | `downloadSpeedLimit` | `unlimited`, `1MB/s`, `5MB/s`, `10MB/s`, `25MB/s`, `50MB/s` |
| Close behavior | `closeBehavior` | `minimize`, `exit` |
| Proxy | `proxyMode` | `direct`, `system` |
| Background | `backgroundSource` | `bundled`, `remote`, `custom` |
| Game path | `gamePath` | Absolute directory path |
| Custom background | `customBackgroundPath` | Absolute file path |
| Toast notifications | `toastNotificationsEnabled` | `true`/`false` |
| Remote content card | `showRemoteContentCard` | `true`/`false` |

### Key models (`Models/`)

- `LauncherApiContracts.cs` — All API response DTOs
- `LauncherStateModels.cs` — String constants for modes/behaviors (`LaunchCheckModes`, `ProxyModes`, `CloseBehaviors`, `LauncherLanguages`, `ThemeModes`, `DownloadSpeedLimits`, `PatchUrlGroups`), plus runtime state objects (`LauncherStatusSnapshot`, `LauncherRemoteState`, `LocalGameState`, `LauncherSettings`, `GameOperationProgress`, `GameOperationResult`), and option types (`SettingOption`, `LanguageOption`, `ThemeOption`) for localized dropdown binding
- `LocalGameContracts.cs` — `LocalManifest`, `RemoteManifest`, `ManifestFile`, `GameLauncherConfig`
- `PatchUrlGroupDefinition.cs` — Code + host-from/to tuples for CDN URL rewriting
- `DownloadTaskState.cs` — Serializable download resume state
- `BannerDot.cs` — Observable carousel dot indicator
- `ThemeColorPaletteItem.cs` — Extracted color data from wallpaper images
- `BestHttpCookieModels.cs` — Cookie-related models for HTTP
- `ResourcePanelModels.cs` — Resource panel data models

### Constants

`LauncherConstants` holds: `ProductName`, `LauncherVersion` (reads from `AssemblyInformationalVersionAttribute`, currently `"1.0.0"`), `YostarAuthorizationVersion` (`"1.7.2"` — the version sent in API auth headers to match the official launcher), `ApiBaseUrl`, `AuthorizationSalt`, `OfficialWebsiteUrl`, `UpdatePackageUrl`, path/filename conventions (`RootFolderName = "YostarGames"`, `GameFolderName = "BlueArchive_JP"`), and `AvaloniaVersion` (must be kept in sync with the `.csproj` `PackageReference` for Avalonia).

### Patch URL groups

Users can switch between `Official` (yo-star.com) and `Cafe` (bluearchive.cafe) CDN hosts for downloading game files. The `PatchUrlGroupService` defines host-rewrite rules, and `LauncherApiClient.RewriteManifestUrl()` / `GameDownloadService.BuildDownloadUrl()` apply them when constructing download URLs. The setting is persisted as `patchUrlGroup` in `settings.json`. A sentinel test ensures URL rewriting scope is strictly limited to package download hosts — no status/list, serverinfo, or SDK netloc endpoints are touched.

### Converters

`UrlToBitmapConverter` (`Converters/`) — converts image URLs to `Bitmap?` for XAML binding, used for remote banner/avatar images.

### Other directories

- `Constants/` — `LauncherConstants` (see above)
- `Helpers/` — `FileSizeFormatter`, `GamePathValidator`, `HttpClientLease`
- `Services/Auth/` — `AuthorizationHeaderFactory` (MD5-signed API auth header)
- `Services/Diagnostics/` — `LocalDiagnostics` (appends to `diagnostics.log`)

### Localization

All UI strings go through `LocalizationService.T(key)` and `LocalizationService.F(key, args)` for formatted strings. String data is loaded from embedded JSON resource files at `Assets/Locales/{locale}.json` (en, zh-Hans, ja) at first access via `AssetLoader`. `LocalizedStrings` (generated by CommunityToolkit source generators) exposes individual `[ObservableProperty]` properties for XAML binding: `{Binding I18n.Settings}`, etc.

**Adding localized strings:**
1. Add the key and value to all 3 JSON files at `Assets/Locales/` (en.json, zh-Hans.json, ja.json)
2. Add an `[ObservableProperty]` field to `LocalizedStrings`
3. Wire it in `LocalizedStrings.Apply()`
4. Build — JSON files are automatically embedded via `<AvaloniaResource Include="Assets\**"/>`

**Testing note:** Unit tests that exercise `LocalizationService.T()` must call `LocalizationService.InitializeForTesting(...)` with test dictionaries in a static constructor before creating service instances, because `AssetLoader` is not available in the test runner context.

**Localized dropdown values** follow the same pattern as `ThemeOption`: create `SettingOption` instances with `Code` (the persisted value) and `DisplayName` (set from `localizer.T()` in a `Refresh*Options()` method called from `ApplyLanguage()`). Bind the ComboBox with `SelectedValue="{Binding SelectedX}"` + `SelectedValueBinding="{Binding Code}"` + an `ItemTemplate` showing `{Binding DisplayName}`.

### Theme

Light/Dark themes defined as `ThemeDictionaries` in `App.axaml` with custom `Launcher*` brush keys. `ThemeModes.System` → `ThemeVariant.Default` (follows OS), `Light`/`Dark` → explicit. Applied via `Application.Current.RequestedThemeVariant`.

Wallpaper color extraction (`ThemeColorExtractionService`) analyzes the current background image and produces a palette that tints UI elements to match.

### Single-instance pattern

`Program.cs` uses a named global `Mutex`. Second instances signal the first via `EventWaitHandle`, which triggers `Dispatcher.UIThread.InvokeAsync` to restore the window from tray/minimized state. Windows-only (`EventWaitHandle` is not supported on Linux — see commit `19db5a3`).

### API auth

`AuthorizationHeaderFactory` builds a JSON header with `{head: {game_tag, time, version}, sign: MD5(headJson + data + salt)}`. Salt is in `LauncherConstants.AuthorizationSalt`.

## Important patterns

- **No remote telemetry**: The original Electron launcher sent logs to Aliyun SLS. This rewrite explicitly excludes those paths (`/api/launcher/advanced/config`, `/api/open/api/config`). Always keep diagnostics local.
- **Path safety**: `GamePathValidator.GetSafePath()` (static helper in `Helpers/GamePathValidator.cs`, used by `GameDownloadService`, `GameUninstallService`, and `ManifestValidationService`) validates that all file operations stay within the game directory — path traversal is rejected.
- **Download resilience**: CRC64 verification after download, rename `.tmp` → final only on success, up to 3 install-verification retries, CDN failover (primary → backup with retry order).
- **Async pause**: `GameDownloadService` uses `TaskCompletionSource`-based pause (never blocks threads). `Pause()` creates a pending `TaskCompletionSource`, download loops `await` it, `Resume()` completes it. `Stop()` also completes the TCS to unblock paused awaits before cancellation.
- **Spacing**: UI spacing follows a 4px grid (0, 4, 8, 12, 16, 20, 24, …). Left panel margin and bottom panel horizontal padding are both 40px for visual symmetry.
- **Version comparison**: `VersionComparer.Compare()` returns -1/0/1 for old/equal/new.
- **XAML extraction**: Large XAML blocks (styles, overlays) are extracted into separate `.axaml` files under `Views/` and referenced via `<StyleInclude>` or `Classes` attributes. The main `MainWindow.axaml` keeps only the window shell and content grid.
- **Conventional commits**: Release changelog generation groups commits by `feat:`/`fix:`/`refactor:`/`perf:` prefixes. Use these prefixes for commit messages to get clean changelogs.
