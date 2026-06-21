# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
.\build.ps1                              # Debug build (expect 0 warnings, 0 errors)
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore   # Release build
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish   # Self-contained publish (win-x64)
```

**Tests** (xUnit 2.9.3, under `tests/Cafe.Launcher.Avalonia.Tests/`, with coverlet 10.0.1):
```powershell
dotnet test                                                    # Run all tests
dotnet test --filter "FullyQualifiedName~VersionComparerTests" # Run a single test class
```

Available test classes include `VersionComparerTests`, `LauncherApiClientTests`, `LauncherConstantsTests`, `LauncherSettingsServiceTests`, `SettingsNormalizerTests`, `SettingsEditorTests`, `ToastServiceTests`, `GameInstallationPathTests`, `LocalInstallationStateStoreTests`, `LauncherCoreServiceTests`, `InstallationOperationStateTests`, `LocalizationServiceTests`, `MainWindowViewModelTests`, `DialogsViewModelTests`, `GameDownloadServiceTests`, `PatchUrlGroupServiceTests`, `BestHttpCookieLibraryServiceTests`, `ResourcePanelUidServiceTests`, `ExternalLinkServiceTests`, `ResourcePanelApiClientTests`, `MigrationWizardViewModelTests`, `LevelDbReaderTests`, `OldLauncherDetectionServiceTests`, `LauncherUpdateServiceTests`, `HttpClientFactoryTests`, and `UiStyleContractTests`.

`UiStyleContractTests` enforces design token contracts: no raw colors in view XAML, proper use of `LauncherSpacing*` tokens, correct overlay Z-index ordering, toast layer using `LauncherConstants.ZIndexToast`, and dynamic accent brushes not replacing theme-specific brushes. Run this whenever touching XAML styles or overlays.

**Testing infrastructure**: No mocking framework (Moq/NSubstitute) is used. Tests hand-craft `HttpMessageHandler` subclasses (e.g., `GitHubReleaseHandler`) and manual stubs. The source project exposes internals to tests via `[assembly: InternalsVisibleTo("Cafe.Launcher.Avalonia.Tests")]` in `Properties/AssemblyInfo.cs`.

CI is GitHub Actions on `windows-latest`, .NET 10.0.x:
- **build.yml** (push/PR to `main`): restore, Debug build, test, Release build, self-contained publish, upload artifact.
- **release.yml** (push of `v*` tag): test, Release build, publish, ZIP archive, generate the grouped changelog through `scripts/New-ReleaseChangelog.ps1`, then create matching GitHub Releases in both the source repository and the distribution repository (`bluearchive-cafe/Cafe.Launcher.Avalonia_Release`, defined as `GitHubReleaseRepositorySlug` in constants). The local release script uses the same changelog generator. The distribution repository uses the `RELEASE_REPOSITORY_TOKEN` Actions secret. Pre-release if tag contains `-`.

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

`release.ps1` reads `<VersionPrefix>` from the `.csproj`, bumps it, preserves and reuses the maintained `CHANGELOG_RELEASE.md`, updates `AssemblyVersion`/`FileVersion`, commits, creates an annotated tag, and pushes. If the maintained release notes are missing, it falls back to `scripts/New-ReleaseChangelog.ps1`. `release.yml` follows the same maintained-file-first policy for matching GitHub Releases in the source and distribution repositories.

## Architecture

**Tech stack**: .NET 10.0, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.2 (source generators), Material.Icons.Avalonia, Fluent Theme. Compiled bindings enabled by default. Nullable reference types enabled project-wide (`<Nullable>enable</Nullable>` in the `.csproj`).

**Build configuration**: `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` are enabled project-wide (0 warnings is the norm, not aspirational). Analysis level is `latest-recommended` with `Minimum` mode. Release builds are self-contained `win-x64` with aggressive trimming (`DebuggerSupport=false`, `EventSourceSupport=false`, `HttpActivityPropagationSupport=false`, `MetadataUpdaterSupport=false`, `DebugType=none`). An `AddGitCommitMetadata` MSBuild target embeds `git rev-parse --short=7 HEAD` as `[AssemblyMetadata("CommitSha")]`, surfaced via `LauncherConstants.CommitSha`. The `AvaloniaUI.DiagnosticsSupport` package is Debug-only (conditionally excluded in Release). The Windows `app.manifest` declares DPI awareness (`PerMonitorV2`) and Windows 10/11 support.

**`.editorconfig` key diagnostics**:
- `CA5351` (MD5) → `none` — MD5 is required by the official launcher wire protocol and local compatibility hashes
- `CA1822` (can be static) → `suggestion` — existing service APIs intentionally use instance methods for DI consistency
- `CA1001`/`CA1816` (disposable ownership) → `suggestion` — app-lifetime ownership handled by Avalonia and the DI container
- `CA1707` (identifiers contain underscore) → `none` for tests — test names use `Method_State_ExpectedResult` convention
- `CA1826`/`CA1859`/`CA1861` → `suggestion`

**MVVM pattern** with explicit XAML composition. `ViewModelBase` extends `ObservableObject`; the app does not use a reflection-based `ViewLocator`.

### Single-window desktop app

One `MainWindow` (1300×754, non-resizable with MinWidth 1024/MinHeight 640, borderless with custom chrome). The ViewModel is split into composed sub-ViewModels, each owning a distinct concern:

| Sub-ViewModel | Concern |
|---|---|
| `ShellViewModel` | Product name, version, runtime info (`FrameworkVersion` from `RuntimeInformation.FrameworkDescription`, `PlatformName` from OS detection, Avalonia version, build config), status text, game path display |
| `BackgroundViewModel` | Wallpaper (bundled / remote / custom), theme-color extraction |
| `RemoteContentViewModel` | Announcements, banners, news, social media from API |
| `DialogsViewModel` | Notice popup, repair/uninstall confirmation dialogs |
| `GameOperationsViewModel` | Install / update / repair / launch / uninstall commands and progress |
| `ToastHostViewModel` | Transient toast notification queue |
| `WindowChromeViewModel` | Title bar, minimize/close buttons, window drag state |
| `SettingsViewModel` | Settings command coordination, persistence, folder pickers, update checks, save/discard lifecycle |
| `SettingsAppearanceViewModel` | Theme-color and background UI projections, palette extraction, Avalonia theme resources |
| `SettingsOptionsViewModel` | Localized setting option collections and settings-summary display resolvers |
| `ResourcePanelViewModel` | Resource panel (UID-based game resource display) |
| `MigrationWizardViewModel` | First-launch migration wizard; edits migration values through its own `ISettingsEditor` |

**View files** (XAML split by concern, all under `Views/`):
- `MainWindow.axaml` — window shell, title bar, remote content panel, bottom install/progress/control panels
- `MainWindow.Styles.axaml` — all `Window.Styles` extracted via `<StyleInclude Source="avares://..."/>`
- `MainWindowSettingsOverlay.axaml` — settings dialog overlay
- `MainWindowDialogsOverlay.axaml` — notice popup, repair/uninstall confirmation dialogs
- `MainWindowToastOverlay.axaml` — toast notification overlay

**Entries:**
1. **Program.cs** — Process mutex (`Local\Cafe_Launcher_SI`), single-instance enforcement via `EventWaitHandle` signal. Creates `UnifiedLogger` + `CrashRecoveryService` on startup before the DI container is available. Tracks `PreviousSessionCrashed` via a `session.active` marker file. Sets up unhandled-exception handlers (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`).
2. **App.axaml.cs** — On framework init: builds DI container via `ServiceConfiguration.AddLauncherServices()`, resolves `MainWindowViewModel`, creates `MainWindow`, wires `ClickCodeService`, `SystemTrayService`. Starts a background thread listening for `EventWaitHandle` signals to restore window from tray.
3. **App.axaml** — Light/Dark `ThemeDictionaries` with custom `Launcher*` brushes, FluentTheme + MaterialIconStyles.

**Composition root**: `ServiceConfiguration.AddLauncherServices()` is the DI configuration — it registers all services with `Microsoft.Extensions.DependencyInjection`. The container is built in `App.axaml.cs` via `ServiceCollection.BuildServiceProvider()`. Services are registered as `AddSingleton`; ViewModels are a mix: `SettingsViewModel`, `ShellViewModel`, `RemoteContentViewModel`, `DialogsViewModel`, and `GameOperationsViewModel` are `AddSingleton` (shared state), while `ResourcePanelViewModel`, `BackgroundViewModel`, `ToastHostViewModel`, `WindowChromeViewModel`, `MigrationWizardViewModel`, `MainWindowViewModel`, and `LogViewerDialogViewModel` are `AddTransient` (fresh instance per resolution). Thread-safe disposal order for IDisposable services is defined by reverse registration order (see disposal order section below).

**ViewModel coordination**: Sub-ViewModels communicate with `MainWindowViewModel` through two mechanisms:
- **Delegates** — `MainWindowViewModel.ConfigureViewModel()` sets `Func<>` / `Func<Task>` delegates on children (e.g. `SettingsViewModel.PickGameFolderAsync`, `SettingsViewModel.PreviewAppearanceAsync`). These let children call back into parent capabilities such as native pickers and appearance previews.
- **Events** — Children expose `event Func<Task>?` / `event Action?` that the parent subscribes to (e.g. `SettingsViewModel.SettingsSaved`). This decouples child-triggered actions from parent handling.

**View code-behind** (`MainWindow.axaml.cs`): handles native folder-picker dialog (via `StorageProvider`), window drag-to-move (borderless chrome), and close-behavior routing (minimize-to-tray vs exit). The ViewModel receives `PickGameFolderAsync`, `MinimizeWindow`, and `CloseWindow` delegates via `ConfigureViewModel()`.

### Core data flow

`LauncherCoreService.LoadAsync()` is the central orchestrator:
1. Reads local `settings.json` via `LauncherSettingsService`
2. Fires 6 parallel API calls via `LauncherApiClient` (game config, base config, CDN config — plus 3 optional: operations, social media, installation config)
3. Reads local `game-launcher-config.json` + `manifest.json` via `LocalInstallationStateStore`
4. Computes one `LauncherRuntimeState` value from local classification and remote version comparison
5. Returns a single `LauncherStatusSnapshot` consumed by the ViewModel

### Services (all in `Services/`)

| Service | Role |
|---|---|
| `LauncherApiClient` | HTTP to `api-launcher-jp.yo-star.com`, MD5-signed `Authorization` header, envelope unwrapping. Implements `IDisposable`. |
| `LauncherCoreService` | Orchestrates API + local state into `LauncherStatusSnapshot`. Exposed as `ILauncherCoreService` in the DI container. |
| `LauncherSettingsService` | Reads/writes `settings.json` at `%LOCALAPPDATA%\Cafe Launcher\` and handles exact legacy JSON field names |
| `SettingsNormalizer` | Pure settings-value normalization: enum guards, legacy launch-check values, colors, palette, indexes, paths, and UID trimming |
| `SettingsEditor` | Snapshot/dirty/discard editing of `LauncherSettings` via `ISettingsEditor`, with separate current and saved snapshots for transactional settings behavior. Uses JSON round-trip deep cloning. Registered as **Singleton** (single-window app, shared editor state). |
| `GameInstallationPath` | Computes the default game path and normalizes paths to `YostarGames\BlueArchive_JP` |
| `LocalInstallationStateStore` | Strictly reads, validates, commits, and deletes local `game-launcher-config.json` + `manifest.json` as one installation state |
| `GameDownloadService` | Install/update/repair: manifest diff → parallel CDN download (10 concurrent, `.tmp` files, `Range` resume, CRC64 verify, rename on success). Supports download speed throttling, async pause/resume via `TaskCompletionSource`. Implements `IDisposable` — thread-safe CTS management via `activeDownloadLock`. Constructor takes a `GameDownloadService.Dependencies` record grouping all dependencies. |
| `RemoteManifestService` | Retrieves and caches remote manifest data from CDN, used by `GameDownloadService` |
| `IFileDownloadService` / `FileDownloadService` | File download abstraction with retry, range support, and progress reporting; used by `GameDownloadService` |
| `ResourcePanelService` | Service layer for resource panel data operations |
| `GameLaunchService` | Manifest validation + process launch |
| `GameUninstallService` | Guarded uninstall (checks path safety, exe not running, deletes only manifest-listed files) |
| `LocalizationService` | Inline dictionaries for `en`/`zh-Hans`/`ja`; `auto` resolves via `CultureInfo.CurrentUICulture` |
| `SystemTrayService` | Avalonia 12 `TrayIcon` + `NativeMenu` for minimize-to-tray |
| `ToastService` | Event-based transient notifications. `ToastNotification` is pure data (`Id`, `Message`, `Severity`, `DurationMs`, `IconKind`); view brush resolution stays in the toast XAML converter |
| `UnifiedLogger` | Unified logging with severity levels (`LogEntrySeverity`), async file writing, and session start/end markers. Owned by `Program.cs` at startup before DI is available; also registered in DI. |
| `LogRotationManager` | Log file rotation management — archives logs above a size threshold with timestamped filenames |
| `LogExportService` | Exports collected log entries for display/download |
| `CrashRecoveryService` | Session crash detection via a `session.active` marker file in the settings folder. `PreviousSessionCrashed` is exposed via `Program.PreviousSessionCrashed`. |
| `LocalDiagnostics` | Appends diagnostic entries to `diagnostics.log` via `UnifiedLogger` |
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
| `LauncherUpdateService` | Checks for launcher self-updates via the server proxy endpoint (`ApiConfig.LauncherApiBaseUrl`), supporting stable/beta channels and returning every validated release file in API order. Every file must have a non-empty name, an absolute HTTP/HTTPS URL, and a positive size. |
| `ExternalLinkService` | Opens external URLs in the default browser |
| `Crc64Service` | CRC64 hash computation for downloaded file verification |
| `OfficialHashService` | Official launcher hash algorithm for file verification |
| `DiskSpaceService` | Checks available disk space before download/install |
| `ProcessService` | Checks if game processes are running via `Process.GetProcessesByName` |
| `VersionComparer` | Static utility — semantic version comparison: returns -1/0/1 for old/equal/new. Not DI-registered; used inline. |
| `ClickCodeService` | Saves install attribution code (`clickCode`) on first launch |
| `OldLauncherDetectionService` | Detects old Electron launcher install + reads its localStorage (LevelDB) for migration |
| `LevelDbReader` | Best-effort byte-level scanner for Chrome localStorage LevelDB files (.ldb/.log) |
| `GamePathValidator` | Validates file operations stay within the game directory (path traversal rejection) |
| `ServiceConfiguration` | DI container — registers all services and ViewModels via `AddLauncherServices()`. Services mostly singleton; `ISettingsEditor` singleton; ViewModels mostly transient; `DialogsViewModel` singleton. |

**HttpClient lifecycle**: `HttpClientFactory` owns a single shared `SocketsHttpHandler` (pooled, 15-min connection lifetime). Two patterns:

1. **`CreateClient(baseAddress, timeout)` / `CreateClient(timeout)`** — Returns an `HttpClient` sharing the pooled handler (`disposeHandler: false`). Caller MUST dispose the `HttpClient` instance (the handler survives). Used for direct (non-proxy) connections.
2. **`CreateLeaseAsync(proxyMode, ...)`** — Returns an `HttpClientLease` wrapping a proxy-aware client. When `proxyMode` is System, creates a per-request handler; otherwise shares the default handler. Callers dispose the **lease** (which disposes the client and, if proxy-mode, the handler). `HttpClientLease.Dispose()` is a no-op for the shared handler in direct mode.

**IDisposable service disposal order** (reverse registration = forward dispose):
1. `LauncherApiClient` — disposed first
2. `ResourcePanelApiClient`
3. `ImageCacheService`
4. `GameDownloadService` — disposed last (after all HTTP clients)

The DI container calls `Dispose()` on these in reverse registration order when the service provider is disposed. `UnifiedLogger` is created in `Program.cs` before the DI container and disposed independently.

### First-launch migration

On first launch, `MainWindowViewModel` uses `OldLauncherDetectionService` to check for a previous Electron launcher (`BlueArchive_JP_Gamelauncher`). If detected, it reads settings (game path, proxy mode, close behavior, clickCode) from the old launcher's Chromium localStorage via `LevelDbReader`, which performs a byte-level scan of `.ldb` and `.log` files. The `MigrationWizardViewModel` presents a dialog (rendered in `MainWindowDialogsOverlay.axaml`) letting the user review and adjust detected settings before applying them. After completion, `hasCompletedFirstLaunchWizard` is persisted to `true` to prevent re-running.

`OriginalLauncherMigrationService` is a **static helper** (not a DI service) — `TryGetGamePath()` reads the game path from the old Yostar launcher's localStorage for non-interactive first-run migration. Called directly by `MainWindowViewModel.InitializeAsync()`, bypassing the wizard UI.

### Local files (`%LOCALAPPDATA%\Cafe Launcher\`)

| File | Purpose |
|---|---|
| `settings.json` | Launcher settings (see Settings reference below) |
| `session.active` | Active-session marker written by `CrashRecoveryService`; presence on startup indicates previous session crashed |
| `diagnostics.log` | Runtime diagnostics appended by `LocalDiagnostics` via `UnifiedLogger` |
| `crash.log` | Global unhandled exception log (written by `UnifiedLogger` in `Program.cs`) |
| `download_state.json` | Serializable download resume state (managed by `GameDownloadService`) |
| `shown_notices.json` | Tracked shown notice IDs (`NoticeStateService`) |
| `clickCode` | Install attribution code (`ClickCodeService`) |

### Settings reference

Persisted fields in `settings.json` and their valid values:

| Setting | JSON key | Valid codes |
|---|---|---|
| Language | `language` | `auto`, `en`, `zh-Hans`, `ja` |
| Theme | `themeMode` | `system`, `light`, `dark` |
| Patch URL group | `patchUrlGroup` | `official`, `cafe` |
| Launch check | `launchCheckMode` | `localManifest`, `remoteManifest`, `none` |
| Download speed limit | `downloadSpeedLimit` | `unlimited`, `1MB/s`, `5MB/s`, `10MB/s`, `25MB/s`, `50MB/s` |
| Close behavior | `closeBehavior` | `minimize`, `exit` |
| Proxy | `proxyMode` | `direct`, `system` |
| Background | `backgroundSource` | `bundled`, `remote`, `custom` |
| Wallpaper fit | `backgroundFit` | `fill`, `uniform`, `uniformToFill` |
| Wallpaper fill color | `backgroundFillColor` | Hex color string (e.g. `#FF000000`) |
| Game path | `gamePath` | Absolute directory path |
| Custom background | `customBackgroundPath` | Absolute file path |
| Toast notifications | `toastNotificationsEnabled` | `true`/`false` |
| Remote content card | `showRemoteContentCard` | `true`/`false` |
| Theme color mode | `themeColorMode` | `default`, `system`, `wallpaper`, `custom` |
| Custom theme color | `customThemeColor` | Hex color string (e.g. `#FF2E7DF6`) |
| Theme color palette | `themeColorPalette` | JSON array of hex strings (extracted from wallpaper) |
| Selected palette index | `selectedThemeColorPaletteIndex` | Integer index into `themeColorPalette` |
| Resource panel UID | `resourcePanelUid` | Player UID string |
| First launch wizard | `hasCompletedFirstLaunchWizard` | `true`/`false` |
| Update channel | `updateChannel` | `stable`, `beta` |

### Key models (`Models/`)

- `LauncherApiContracts.cs` — All API response DTOs
- `LauncherStateModels.cs` — String constants for modes/behaviors (`LaunchCheckModes`, `ProxyModes`, `CloseBehaviors`, `LauncherLanguages`, `ThemeModes`, `ThemeColorModes`, `DownloadSpeedLimits`, `PatchUrlGroups`, `UpdateChannels`, `BackgroundSources`, `BackgroundFits`, `GameOperationKinds`), plus runtime state objects (`LauncherStatusSnapshot`, `LauncherRemoteState`, `LauncherRuntimeState`, `LauncherSettings`, `GameOperationProgress`, `GameOperationResult`, `ManifestValidationResult`, `GameLaunchResult`), and option types (`SettingOption`, `LanguageOption`, `ThemeOption`) for localized dropdown binding
- `LocalInstallationStateModels.cs` — Local installation classifications, immutable state snapshots, and commit input records
- `LocalGameContracts.cs` — `LocalManifest`, `RemoteManifest`, `ManifestFile`, `GameLauncherConfig`
- `PatchUrlGroupDefinition.cs` — Code + host-from/to tuples for CDN URL rewriting
- `DownloadTaskState.cs` — Serializable download resume state
- `BannerDot.cs` — Observable carousel dot indicator
- `ThemeColorPaletteItem.cs` — Extracted color data from wallpaper images
- `BestHttpCookieModels.cs` — Cookie-related models for HTTP
- `ResourcePanelModels.cs` — Resource panel data models
- `LauncherReleaseResponse.cs` — Release information from the launcher update server proxy. Includes `LauncherReleaseResponse` (version, release date, files list) and `ReleaseFile` (name, URL, SHA-512, size, formatted display size). The update dialog requires explicit user selection and never infers file purpose from name, extension, or list order.

### Constants

Constants are split into 4 focused files under `Constants/`:

- **`LauncherConstants`** — Cross-cutting UI/product constants: `ProductName`, `DefaultThemeColor` (`#FF2E7DF6`), `ZIndexToast` (`1000`), `OfficialWebsiteUrl`, `GitHubReleaseRepositoryUrl`.
- **`ApiConfig`** — API endpoints, authentication, and release repository metadata: `ApiBaseUrl` (`https://api-launcher-jp.yo-star.com`), `ResourcePanelApiBaseUrl` (`https://api.bluearchive.cafe`), `AuthorizationSalt`, `YostarAuthorizationVersion` (`"1.7.2"` — the version sent in API auth headers to match the official launcher), `GitHubReleaseRepositorySlug` (`bluearchive-cafe/Cafe.Launcher.Avalonia_Release`), `GitHubReleaseRepositoryUrl`, `LauncherApiBaseUrl` (server proxy for launcher self-updates: `https://api-cafe-launcher.saibamidori.com/`), `LauncherReleasesPath` (`/api/launcher/releases`).
- **`BuildInfo`** — Build-time metadata: `LauncherVersion` (reads from `AssemblyInformationalVersionAttribute`, matches `<VersionPrefix>` in the `.csproj`), `CommitSha` (reads from `AssemblyMetadataAttribute`, embedded by the `AddGitCommitMetadata` MSBuild target), `BuildConfiguration` (compile-time `#if DEBUG`), `AvaloniaVersion` (must be kept in sync with the `.csproj` `PackageReference` for Avalonia).
- **`GamePaths`** — Path/filename conventions: `GameTag` (`"BlueArchive_JP"`), `RootFolderName` (`"YostarGames"`), `GameFolderName` (`"BlueArchive_JP"`), `ManifestFileName`, `GameConfigFileName`, `LauncherSettingsFileName`, `OldLauncherAppName` (`"BlueArchive_JP_Gamelauncher"`).

### Patch URL groups

Users can switch between `Official` (yo-star.com) and `Cafe` (bluearchive.cafe) CDN hosts for downloading game files. The `PatchUrlGroupService` defines host-rewrite rules, and `LauncherApiClient.RewriteManifestUrl()` / `GameDownloadService.BuildDownloadUrl()` apply them when constructing download URLs. The setting is persisted as `patchUrlGroup` in `settings.json`. A sentinel test ensures URL rewriting scope is strictly limited to package download hosts — no status/list, serverinfo, or SDK netloc endpoints are touched.

### Converters

- `UrlToBitmapConverter` — converts image URLs to `Bitmap?` for XAML binding via `TaskCompletionSourceNotifying<T>` + `INotifyTaskCompletion<T>`. Owns its own **static** `HttpClient` instance (separate from the DI-managed `HttpClientFactory`), used for remote banner/avatar images.
- `ToastSeverityToBrushConverter` — resolves `ToastSeverity` to the exact `LauncherToast{Severity}Brush` view resource; keeps `ToastNotification` independent of Avalonia.

### Other directories

- `Constants/` — `LauncherConstants`, `ApiConfig`, `BuildInfo`, `GamePaths` (see Constants section above)
- `Helpers/` — `FileSizeFormatter`, `GamePathValidator`, `HttpClientLease`
- `Services/Auth/` — `AuthorizationHeaderFactory` (MD5-signed API auth header)
- `Services/Diagnostics/` — `LocalDiagnostics` (appends to `diagnostics.log`), `UnifiedLogger` (unified async logging with severity levels and session tracking), `LogRotationManager` (log file rotation), `LogExportService` (log export), `CrashRecoveryService` (session crash detection), `LogEntry` / `LogEntrySeverity` (log entry model)
- `Services/HttpClientLeaseSource.cs` — Internal `IHttpClientLeaseSource` abstraction over `HttpClientFactory.CreateLeaseAsync()`. Two implementations: `ProxyAwareHttpClientLeaseSource` (production, delegates to `HttpClientFactory`) and `FixedHttpClientLeaseSource` (testing, wraps a fixed `HttpMessageHandler`). Used by services like `LauncherUpdateService` that need proxy-aware HTTP with lease lifetime management.
- `Services/ServiceConfiguration.cs` — DI registration; services as `AddSingleton`, ViewModels as a mix of singleton (shared state: `SettingsViewModel`, `ShellViewModel`, `RemoteContentViewModel`, `DialogsViewModel`, `GameOperationsViewModel`) and transient (fresh per resolution)

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

**Theme color** controls the accent color tinting UI elements (buttons, progress bars, links). `ThemeColorModes` has 4 variants:
- `default` — uses `LauncherConstants.DefaultThemeColor` (`#FF2E7DF6`)
- `system` — follows the OS accent color
- `wallpaper` — extracts a palette from the current wallpaper via `ThemeColorExtractionService`; the user picks one from the extracted `ThemeColorPalette` list
- `custom` — user picks any color via `ColorPicker`

The selected mode, custom color, and palette are persisted in `settings.json` (`themeColorMode`, `customThemeColor`, `themeColorPalette`, `selectedThemeColorPaletteIndex`). Theme color is applied independently of light/dark theme mode.

### Design Tokens

All numeric design values use `StaticResource` keys defined in `App.axaml`:

**Spacing tokens** (4px grid: 0, 4, 8, 12, 16, 20, 24, 40):

| Token | Value | Typical usage |
|---|---|---|
| `LauncherSpacingXs` | 4 | Tight spacing (label stack gaps, dot gaps) |
| `LauncherSpacingSm` | 8 | Standard spacing (button-content, chips, card groups) |
| `LauncherSpacingMd` | 12 | Section spacing (dialog content, settings rows) |
| `LauncherSpacingLg` | 16 | Panel spacing (control groups, info rows) |
| `LauncherSpacingXl` | 20 | Wide spacing (title bar, dialog title, panel margins) |
| `LauncherSpacingXxl` | 24 | Container spacing (dialog margins, control panels) |
| `LauncherSpacingSection` | 40 | Left panel / horizontal section padding |

**Corner radius tokens:**

| Token | Value | Usage |
|---|---|---|
| `LauncherRadiusSm` | 4 | Buttons, cards, badges, icons, fields, color swatches |
| `LauncherRadiusMd` | 6 | Panel surfaces, settings sections, chips, toasts |
| `LauncherRadiusLg` | 8 | Top-level dialog containers |

**Icon size tokens** (Material.Icons.Avalonia `Width`/`Height`):

| Token | Value | Usage |
|---|---|---|
| `LauncherIconSm` | 16 | Small inline icons (button-content, chips, color swatches) |
| `LauncherIconMd` | 18 | Standalone control icons (title bar, dialog close, settings icons) |
| `LauncherIconLg` | 20 | Section heading icons |
| `LauncherIconXl` | 22 | Dialog area / large action icons |
| `LauncherIconXxl` | 24 | Primary action launch icons |

**Control height tokens:**

| Token | Value | Usage |
|---|---:|---|
| `LauncherControlHeightSetting` | 36 | Settings controls and compact actions |
| `LauncherControlHeightDialog` | 42 | Dialog actions and dialog text inputs |
| `LauncherControlHeightBottom` | 48 | Install/update actions |
| `LauncherControlHeightLaunch` | 58 | Primary launcher controls |

**Gradient brushes**: Title bar gradient (`LauncherTitleBarGradient` in `App.axaml`) and control panel gradient (inline in `MainWindow.Styles.axaml`) use fixed black-transparency values that are intentionally theme-invariant — they overlay the wallpaper/background image.

**Style class hierarchy for corners**: `4` for individual controls, `6` for grouped panels/surfaces, `8` for top-level dialogs.

**Layer hierarchy**: base content → settings overlay (`100`) → dialog overlay (`200`) → toast (`LauncherConstants.ZIndexToast`, `1000`). Overlay backgrounds and positioning are defined by semantic classes in `MainWindow.Styles.axaml`.

**Hardcoded visual values**: view XAML must not contain direct hexadecimal colors, `Transparent`, raw icon sizes, or raw `4`/`6`/`8` corner radii. Theme-invariant wallpaper gradients and the three shadow definitions are allowed only in `App.axaml` or `MainWindow.Styles.axaml`. Component-specific dimensions such as window, dialog, banner, and content widths/heights remain local to the owning view or style.

### Single-instance pattern

`Program.cs` uses a named global `Mutex`. Second instances signal the first via `EventWaitHandle`, which triggers `Dispatcher.UIThread.InvokeAsync` to restore the window from tray/minimized state. Windows-only (`EventWaitHandle` is not supported on Linux — see commit `19db5a3`).

### API auth

`AuthorizationHeaderFactory` builds a JSON header with `{head: {game_tag, time, version}, sign: MD5(headJson + data + salt)}`. Salt is in `LauncherConstants.AuthorizationSalt`.

## Important patterns

- **No remote telemetry**: The original Electron launcher sent logs to Aliyun SLS (Simple Log Service). This rewrite explicitly excludes those paths (`/api/launcher/advanced/config`, `/api/open/api/config`). Always keep diagnostics local.
- **Path safety**: `GamePathValidator.GetSafePath()` (static helper in `Helpers/GamePathValidator.cs`, used by `GameDownloadService`, `GameUninstallService`, and `ManifestValidationService`) validates that all file operations stay within the game directory — path traversal is rejected.
- **Download resilience**: CRC64 verification after download, rename `.tmp` → final only on success, up to 3 install-verification retries, CDN failover (primary → backup with retry order).
- **Async pause**: `GameDownloadService` uses `TaskCompletionSource`-based pause (never blocks threads). `Pause()` creates a pending `TaskCompletionSource`, download loops `await` it, `Resume()` completes it. `Stop()` also completes the TCS to unblock paused awaits before cancellation.
- **Spacing**: UI spacing follows a 4px grid (0, 4, 8, 12, 16, 20, 24, …). Repeated scalar spacing uses `LauncherSpacing*` resources; left panel margin and bottom panel horizontal padding are both 40px for visual symmetry.
- **Version comparison**: `VersionComparer.Compare()` returns -1/0/1 for old/equal/new.
- **XAML extraction**: Large XAML blocks (styles, overlays) are extracted into separate `.axaml` files under `Views/` and referenced via `<StyleInclude>` or `Classes` attributes. The main `MainWindow.axaml` keeps only the window shell and content grid.
- **Conventional commits**: Release changelog generation groups commits by `feat:`/`fix:`/`refactor:`/`perf:` prefixes. Use these prefixes for commit messages to get clean changelogs.
- **AGENTS.md**: A parallel instruction file for Codex exists at the repo root. It covers the same architecture and should be kept in sync when significant structural changes are made.
- *****REMOVED***.md**: Analysis report comparing this launcher to the original Electron launcher, covering migration decisions and intentionally excluded features.
- **VSCode**: `.vscode/launch.json` has `build`/`publish`/`watch` tasks and `.NET Core Launch`/`Attach` configurations.

<!-- superpowers-zh:begin (do not edit between these markers) -->
# Superpowers-ZH 中文增强版

本项目已安装 superpowers-zh 技能框架（20 个 skills）。

## 核心规则

1. **收到任务时，先检查是否有匹配的 skill** — 哪怕只有 1% 的可能性也要检查
2. **设计先于编码** — 收到功能需求时，先用 brainstorming skill 做需求分析
3. **测试先于实现** — 写代码前先写测试（TDD）
4. **验证先于完成** — 声称完成前必须运行验证命令

## 可用 Skills

Skills 位于 `.claude/skills/` 目录，每个 skill 有独立的 `SKILL.md` 文件。

- **brainstorming**: 在任何创造性工作之前必须使用此技能——创建功能、构建组件、添加功能或修改行为。在实现之前先探索用户意图、需求和设计。
- **chinese-code-review**: 中文 review 沟通参考——话术模板、分级标注（必须修复/建议修改/仅供参考）、国内团队常见反模式应对。仅在用户显式 /chinese-code-review 时调用，不要根据上下文自动触发。
- **chinese-commit-conventions**: 中文 commit 与 changelog 配置参考——Conventional Commits 中文适配、commitlint/husky/commitizen 中文模板、conventional-changelog 中文配置。仅在用户显式 /chinese-commit-conventions 时调用，不要根据上下文自动触发。
- **chinese-documentation**: 中文文档排版参考——中英文空格、全半角标点、术语保留、链接格式、中文文案排版指北约定。仅在用户显式 /chinese-documentation 时调用，不要根据上下文自动触发。
- **chinese-git-workflow**: 国内 Git 平台配置参考——Gitee、Coding.net、极狐 GitLab、CNB 的 SSH/HTTPS/凭据/CI 接入差异与镜像同步配置。仅在用户显式 /chinese-git-workflow 时调用，不要根据上下文自动触发。
- **dispatching-parallel-agents**: 当面对 2 个以上可以独立进行、无共享状态或顺序依赖的任务时使用
- **executing-plans**: 当你有一份书面实现计划需要在单独的会话中执行，并设有审查检查点时使用
- **finishing-a-development-branch**: 当实现完成、所有测试通过、需要决定如何集成工作时使用——通过提供合并、PR 或清理等结构化选项来引导开发工作的收尾
- **mcp-builder**: MCP 服务器构建方法论 — 系统化构建生产级 MCP 工具，让 AI 助手连接外部能力
- **receiving-code-review**: 收到代码审查反馈后、实施建议之前使用，尤其当反馈不明确或技术上有疑问时——需要技术严谨性和验证，而非敷衍附和或盲目执行
- **requesting-code-review**: 完成任务、实现重要功能或合并前使用，用于验证工作成果是否符合要求
- **subagent-driven-development**: 当在当前会话中执行包含独立任务的实现计划时使用
- **systematic-debugging**: 遇到任何 bug、测试失败或异常行为时使用，在提出修复方案之前执行
- **test-driven-development**: 在实现任何功能或修复 bug 时使用，在编写实现代码之前
- **using-git-worktrees**: 当需要开始与当前工作区隔离的功能开发，或在执行实现计划之前使用——通过原生工具或 git worktree 回退机制确保隔离工作区存在
- **using-superpowers**: 在开始任何对话时使用——确立如何查找和使用技能，要求在任何响应（包括澄清性问题）之前调用 Skill 工具
- **verification-before-completion**: 在宣称工作完成、已修复或测试通过之前使用，在提交或创建 PR 之前——必须运行验证命令并确认输出后才能声称成功；始终用证据支撑断言
- **workflow-runner**: 在 Claude Code / OpenClaw / Cursor 中直接运行 agency-orchestrator YAML 工作流——无需 API key，使用当前会话的 LLM 作为执行引擎。当用户提供 .yaml 工作流文件或要求多角色协作完成任务时触发。
- **writing-plans**: 当你有规格说明或需求用于多步骤任务时使用，在动手写代码之前
- **writing-skills**: 当创建新技能、编辑现有技能或在部署前验证技能是否有效时使用

## 如何使用

当任务匹配某个 skill 时，使用 `Skill` 工具加载对应 skill 并严格遵循其流程。绝不要用 Read 工具读取 SKILL.md 文件。

如果你认为哪怕只有 1% 的可能性某个 skill 适用于你正在做的事情，你必须调用该 skill 检查。
<!-- superpowers-zh:end -->
