# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Build & Run

```powershell
.\verify.ps1                              # Full verification: Debug build → coverage.ps1 (tests + 50% threshold) → Release build
.\build.ps1                               # Debug build (expect 0 warnings, 0 errors)
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore   # Release build
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish   # Self-contained publish (win-x64)
dotnet run --project .\Cafe.Launcher.Avalonia.csproj                   # Run locally
```

**Tests** — two test projects under `tests/`:

```powershell
# Unit tests (xUnit v3 3.2.2, coverlet.msbuild 10.0.1)
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"

# Headless Avalonia UI tests (xUnit v3 3.2.2, Avalonia.Headless.XUnit 12.0.5, coverlet.msbuild 10.0.1)
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~SystemTrayServiceTests"

# Run all tests in both projects
dotnet test
```

Available unit test classes include `VersionComparerTests`, `LauncherApiClientTests`, `LauncherConstantsTests`, `LauncherSettingsServiceTests`, `SettingsNormalizerTests`, `SettingsEditorTests`, `ToastServiceTests`, `GameInstallationPathTests`, `LocalInstallationStateStoreTests`, `LauncherCoreServiceTests`, `InstallationOperationStateTests`, `LocalizationServiceTests`, `MainWindowViewModelTests`, `DialogsViewModelTests`, `GameDownloadServiceTests`, `PatchUrlGroupServiceTests`, `BestHttpCookieLibraryServiceTests`, `ResourcePanelUidServiceTests`, `ExternalLinkServiceTests`, `ResourcePanelApiClientTests`, `LauncherUpdateServiceTests`, `HttpClientFactoryTests`, `OfficialHashServiceTests`, `UiStyleContractTests`, `DialogActionButtonContractTests`, `InstallerContractTests`, `BackgroundViewModelTests`, `Crc64ServiceTests`, `DiagnosticsServicesTests`, `FileSizeFormatterTests`, `FlexibleBoolConverterTests`, `GameOperationsViewModelTests`, `GamePathValidatorTests`, `ImageCacheServiceTests`, `LogExportServiceTests`, `LogViewerDialogViewModelTests`, `ManifestValidationServiceTests`, `MotionSettingsResolverTests`, `NoticeStateServiceTests`, `ProxySettingsServiceTests`, `ReleaseScriptTests`, `RemoteContentViewModelTests`, `RemoteHttpUrlValidatorTests`, `RemoteManifestServiceTests`, `ServiceConfigurationTests`, `SettingsCategoryTests`, `ToastHostViewModelTests`, `WindowChromeViewModelTests`, `WindowsAnimationSettingsProviderTests`, and `WindowEscapeStrategyTests`.

Headless test classes: `SystemTrayServiceTests`, `MainWindowHeadlessTests`, `HeadlessSmokeTests`, `OverlayFocusBehaviorTests`, and `ConverterHeadlessTests`.

`UiStyleContractTests` enforces design token contracts: no raw colors in view XAML, proper use of `LauncherSpacing*` tokens, correct overlay Z-index ordering, toast layer using `LauncherConstants.ZIndexToast`, and dynamic accent brushes not replacing theme-specific brushes. Run this whenever touching XAML styles or overlays.

**Testing infrastructure**: No mocking framework (Moq/NSubstitute) is used. Tests hand-craft `HttpMessageHandler` subclasses (e.g., `GitHubReleaseHandler`) and manual stubs. The source project exposes internals to tests via `[assembly: InternalsVisibleTo("Cafe.Launcher.Avalonia.Tests")]` in `Properties/AssemblyInfo.cs`. Headless tests use `Avalonia.Headless.XUnit` for UI component testing without a display server. Both test projects use xUnit v3 with `OutputType=Exe` (required by the v3 migration guide); `dotnet test` runs through the VSTest adapter (`xunit.runner.visualstudio`). Coverage is collected via `coverlet.msbuild` (`CollectCoverage=true`) — the `coverlet.collector` VSTest data collector is incompatible with xUnit v3.

**Code coverage**: `coverage.ps1` runs both test projects with `coverlet.msbuild` (Cobertura format, `CollectCoverage=true`), merges the reports, and enforces a **50% threshold** on both line and branch coverage. Excludes `.axaml` files and `obj/` directories. `verify.ps1` calls `coverage.ps1` as its test step — coverage must pass for verification to succeed.

```powershell
.\coverage.ps1    # Run both test projects with coverage, enforce 50% line + branch threshold
```

**Windows installer (NSIS)**: `scripts/Build-Distribution.ps1` builds a standalone ZIP and an NSIS setup EXE (`installer/Cafe.Launcher.Avalonia.nsi`). Requires NSIS 3 with `makensis.exe` on `PATH`. Output goes to `artifacts/distribution/`. The installer installs system-wide to `C:\Program Files\Cafe Launcher`, requires admin rights, and cleans up old installer-managed files on upgrade. Uninstall optionally removes `%LOCALAPPDATA%\Cafe Launcher` (user data).

```powershell
.\scripts\Build-Distribution.ps1           # Build standalone ZIP + NSIS setup EXE
.\scripts\Build-Distribution.ps1 -Tag v1.0 # Specify tag for version stamping
```

CI is GitHub Actions on **Linux** (`ubuntu-latest`), .NET 10.0.x. Release builds cross-compile to `win-x64`:
- **build.yml** (push/PR to `main`): restore, Debug build, test (both projects), Release build (`-r win-x64`), self-contained publish (`-r win-x64`), upload artifact.
- **release.yml** (push of `v*` tag): test, Release build (`-r win-x64`), publish (`-r win-x64`), build standalone ZIP + NSIS setup EXE via `scripts/Build-Distribution.ps1`, generate the grouped changelog through `scripts/New-ReleaseChangelog.ps1`, then create matching GitHub Releases in both the source repository and the distribution repository (`bluearchive-cafe/Cafe.Launcher.Avalonia_Release`, defined as `GitHubReleaseRepositorySlug` in constants). The local release script uses the same changelog generator. The distribution repository uses the `RELEASE_REPOSITORY_TOKEN` Actions secret. Pre-release if tag contains `-`.

**Telemetry must be off during local builds** (already set in `build.ps1`):
- `DOTNET_CLI_TELEMETRY_OPTOUT=1`
- `AVALONIA_TELEMETRY_OPTOUT=1`

## Release workflow

```powershell
.\release.ps1 patch                  # Bump patch version, generate changelog, commit, tag, push
.\release.ps1 minor -DryRun          # Preview minor bump without modifying files
.\release.ps1 2.0.0-beta.1          # Explicit version (prerelease if tag contains -)
.\release.ps1 patch -Force           # Skip safety checks (dirty tree, existing tag)
.\release.ps1 patch -SkipPush        # Commit + tag locally, don't push to origin
```

`release.ps1` reads `<VersionPrefix>` from the `.csproj`, bumps it, preserves and reuses the maintained `CHANGELOG_RELEASE.md`, updates `AssemblyVersion`/`FileVersion`, commits, creates an annotated tag, and pushes. If the maintained release notes are missing, it falls back to `scripts/New-ReleaseChangelog.ps1`.

## Architecture

**Tech stack**: .NET 10.0, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.2 (source generators), Material.Icons.Avalonia, Fluent Theme. Compiled bindings enabled by default. Nullable reference types enabled project-wide (`<Nullable>enable</Nullable>` in the `.csproj`).

**Build configuration**: `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` are enabled project-wide (0 warnings is the norm, not aspirational). Analysis level is `latest-recommended` with `Minimum` mode. Release builds are self-contained `win-x64` with aggressive trimming (`DebuggerSupport=false`, `EventSourceSupport=false`, `HttpActivityPropagationSupport=false`, `MetadataUpdaterSupport=false`, `DebugType=none`). An `AddGitCommitMetadata` MSBuild target embeds `git rev-parse --short=7 HEAD` as `[AssemblyMetadata("CommitSha")]`, surfaced via `LauncherConstants.CommitSha`. The `AvaloniaUI.DiagnosticsSupport` package is Debug-only (conditionally excluded in Release). The Windows `app.manifest` declares DPI awareness (`PerMonitorV2`) and Windows 10/11 support. The app icon is `Assets/app-icon.ico` (`<ApplicationIcon>` in the `.csproj`).

**`.editorconfig` key diagnostics**:
- `CA5351` (MD5) → `none` — MD5 is required by the official launcher wire protocol and local compatibility hashes
- `CA1822` (can be static) → `suggestion` — existing service APIs intentionally use instance methods for DI consistency
- `CA1001`/`CA1816` (disposable ownership) → `suggestion` — app-lifetime ownership handled by Avalonia and the DI container
- `CA1707` (identifiers contain underscore) → `none` for tests — test names use `Method_State_ExpectedResult` convention
- `CA1826`/`CA1859`/`CA1861` → `suggestion`

**MVVM pattern** with explicit XAML composition. `ViewModelBase` extends `ObservableObject`; the app does not use a reflection-based `ViewLocator`.

### Single-window desktop app

One `MainWindow` (1300×754 initial size, resizable with MinWidth 1024/MinHeight 640, borderless with custom chrome). Resize support and the minimum-size constraint are pinned by `UiStyleContractTests.MainWindow_IsResizableWithMinimumViewportConstraints`. Window size/position is not persisted across sessions. The ViewModel is split into composed sub-ViewModels, each owning a distinct concern:

| Sub-ViewModel | Concern |
|---|---|
| `ShellViewModel` | Product name, version, runtime info (`FrameworkVersion` from `RuntimeInformation.FrameworkDescription`, `PlatformName` from OS detection, Avalonia version, build config), status text, game path display |
| `BackgroundViewModel` | Wallpaper (bundled / remote / custom), theme-color extraction |
| `RemoteContentViewModel` | Announcements, banners, news, social media from API |
| `DialogsViewModel` | Notice popup, repair/uninstall confirmation dialogs |
| `GameOperationsViewModel` | Install / update / repair / launch / uninstall commands and progress — delegates to `IGameOperationsBackend` |
| `GameOperationsBackend` | Internal interface + implementation for download/launch/uninstall operations with pause/resume/stop |
| `ToastHostViewModel` | Toast notification queue |
| `WindowChromeViewModel` | Title bar, minimize/close buttons, window drag state |
| `LogViewerDialogViewModel` | In-app log viewer with 7-level severity filter (All / Verbose / Debug / Info / Warn / Error / Fatal), text search, copy, and export |
| `SettingsViewModel` | Settings command coordination, persistence, folder pickers, update checks, save/discard lifecycle |
| `SettingsAppearanceViewModel` | Theme-color and background UI projections, palette extraction, Avalonia theme resources |
| `SettingsOptionsViewModel` | Localized setting option collections and settings-summary display resolvers |
| `ResourcePanelViewModel` | Resource panel (UID-based game resource display) |

**View files** (XAML split by concern, under `Views/` and `Controls/`):
- `MainWindow.axaml` — window shell, title bar, remote content panel, bottom install/progress/control panels
- `MainWindow.Styles.axaml` — all `Window.Styles` extracted via `<StyleInclude Source="avares://..."/>`
- `MainWindowSettingsOverlay.axaml` — settings dialog overlay with category navigation, runtime status, section host, and transactional footer
- `SettingsGeneralSection.axaml` — language, close behavior, motion mode settings
- `SettingsGameSection.axaml` — game path, launch check, repair/uninstall settings
- `SettingsDownloadNetworkSection.axaml` — proxy, download source, speed limit, update channel, log level settings
- `SettingsAppearanceSection.axaml` — theme, theme color, background, toast notifications, remote content card settings
- `SettingsAboutSection.axaml` — version info, action buttons split into two groups (General: check updates / official site / GitHub repo / help docs; Diagnostics: view log / export logs / open data directory), copyright
- `MainWindowDialogsOverlay.axaml` — notice popup, repair/uninstall/stop/close confirmations, update dialog, crash recovery
- `MainWindowLogViewerOverlay.axaml` — log viewer dialog with 7-tab severity filter bar, search, entry list, export/close footer
- `MainWindowToastOverlay.axaml` — toast notification overlay
- `Controls/SettingRow.axaml` — reusable settings row (icon + title + description + action slot)
- `Controls/ConfirmDialog.axaml` — reusable confirmation dialog (StyledProperty-driven)
- `Controls/LoadingOverlay.axaml` — reusable loading overlay (indeterminate progress + label)

All settings sections and Controls share the owning `MainWindowViewModel` data context. Settings are organized by category code (see `SettingsCategoryCodes` model: `general`, `game`, `download-network`, `appearance`, `about`).

**Entries:**
1. **Program.cs** — Process mutex (`Local\Cafe_Launcher_SI`), single-instance enforcement via `EventWaitHandle` signal. Creates `UnifiedLogger` + `CrashRecoveryService` on startup before the DI container is available; exposes the logger via `PreDiLogger` so the DI container reuses the same instance (single Serilog pipeline for the entire process). Tracks `PreviousSessionCrashed` via a `session.active` marker file. Sets up unhandled-exception handlers (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`). `RunSession` orchestrates the session lifecycle: begin → run app → complete/cleanup, with proper crash-marker preservation. The `ServiceProvider` is disposed in `RunSession`'s finally block after the session-end log entry is written.
2. **App.axaml.cs** — On framework init: builds DI container via `ServiceConfiguration.AddLauncherServices()`, resolves `MainWindowViewModel`, creates `MainWindow`, wires `ClickCodeService`, `SystemTrayService`. Starts a background thread listening for `EventWaitHandle` signals to restore window from tray.
3. **App.axaml** — Light/Dark `ThemeDictionaries` with custom `Launcher*` brushes, FluentTheme + MaterialIconStyles.

**Composition root**: `ServiceConfiguration.AddLauncherServices()` is the DI configuration — it registers all services with `Microsoft.Extensions.DependencyInjection`. The container is built in `App.axaml.cs` via `ServiceCollection.BuildServiceProvider()`. All services and ViewModels are registered as `AddSingleton` (single-window desktop app, no scoped request boundaries). Thread-safe disposal order for IDisposable services is defined by reverse registration order (see disposal order section below).

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
| `SettingsEditor` | Snapshot/dirty/discard editing of `LauncherSettings` via `ISettingsEditor`, with separate current and saved snapshots for transactional settings behavior. Uses JSON round-trip deep cloning. Registered as **Singleton**. |
| `GameInstallationPath` | Computes the default game path and normalizes paths to `YostarGames\BlueArchive_JP`. Default path is the launcher's **parent** directory (`Path.Combine(AppContext.BaseDirectory, "..")`) + `YostarGames\BlueArchive_JP`, matching the official launcher's `dirname(exe)/..` default so both launchers resolve the same location |
| `LocalInstallationStateStore` | Strictly reads, validates, commits, and deletes local `game-launcher-config.json` + `manifest.json` as one installation state |
| `GameDownloadService` | Install/update/repair: manifest diff → parallel CDN download (10 concurrent, `.tmp` files, `Range` resume, CRC64 verify, rename on success). Supports download speed throttling, async pause/resume via `TaskCompletionSource`. Implements `IDisposable` — thread-safe CTS management via `activeDownloadLock`. Constructor takes a `GameDownloadService.Dependencies` record grouping all dependencies. |
| `RemoteManifestService` | Retrieves and caches remote manifest data from CDN, used by `GameDownloadService` |
| `IFileDownloadService` / `FileDownloadService` | File download abstraction with retry, range support, and progress reporting |
| `ResourcePanelService` | Service layer for resource panel data operations |
| `GameLaunchService` | Manifest validation + process launch; gated to `Ready` state |
| `GameUninstallService` | Guarded uninstall (checks path safety, exe not running, deletes only manifest-listed files) |
| `LocalizationService` | JSON locale files for `en`/`zh-Hans`/`zh-Hant`/`ja` under `Assets/Locales/`; `auto` resolves via `CultureInfo.CurrentUICulture` (zh-TW/HK/MO/Hant → `zh-Hant`, other zh → `zh-Hans`) |
| `SystemTrayService` | Avalonia 12 `TrayIcon` + `NativeMenu` for minimize-to-tray |
| `ToastService` | Event-based transient notifications. `ToastNotification` is pure data (`Id`, `Message`, `Severity`, `DurationMs`, `IconKind`); view brush resolution stays in the toast XAML converter |
| `UnifiedLogger` | Serilog-backed central logging engine with async sink wrapper (`Serilog.Sinks.Async`, 10k-event buffer). Writes `unified.log` with size-based rolling (5 MB, 4 retained files: current + 3 rotated). Enriches events with `AppVersion`/`CommitSha` globally; uses `LoggingLevelSwitch` (Verbose in Debug, Information in Release, runtime-adjustable via `SetMinimumLevel()`). Output template: `{Timestamp:O} [{Level:u3}] [{LogTitle}] {Message}{NewLine}{Exception}`. `LogAsync` attaches `LogTitle`/`LogMessage` as structured properties. `SelfLog` routes Serilog diagnostics to `Debug.WriteLine`. Created in `Program.cs` and shared with the DI container (single pipeline). Implements `IDisposable`. |
| `LocalDiagnostics` | Public facade over `UnifiedLogger`; exposes `ErrorAsync`/`MessageAsync`/`VerboseAsync`/`DebugAsync`/`WarningAsync`/`FatalAsync`/`LogSync` (2 overloads: a default-Info overload and a severity-parameter overload) |
| `PatchUrlGroupService` | URL rewriting between Official and Cafe CDN hosts for manifest + CDN config URLs |
| `NoticeStateService` | Tracks which notice IDs have been shown (persisted to `shown_notices.json`) |
| `HttpClientFactory` | Centralized factory for pre-configured `HttpClient` instances with shared `SocketsHttpHandler` pooling (15-min connection lifetime). Proxy-aware lease creation via `CreateLeaseAsync()`. Registered as singleton; implements `IDisposable`. |
| `ProxySettingsService` | Creates proxy-aware `SocketsHttpHandler` instances for `HttpClientFactory` |
| `ResourcePanelApiClient` | HTTP client for resource panel data. Implements `IDisposable`. |
| `ResourcePanelUidService` | Manages resource panel UID state |
| `BestHttpCookieLibraryService` | Cookie handling for HTTP requests |
| `ThemeColorExtractionService` | Extracts dominant colors from wallpaper images for UI theming |
| `ImageCacheService` | Caches downloaded images (banners, avatars). Implements `IDisposable`. |
| `ManifestValidationService` | Validates local game files against manifest by **file size**. `remoteManifest` mode fetches the manifest at the **local** `version`/`basis` and **fails open** (allows launch) when that manifest can't be obtained — matching the official launcher and avoiding a launch-blocked/nothing-to-repair deadlock |
| `LauncherUpdateService` | Checks for launcher self-updates via the server proxy endpoint (`ApiConfig.LauncherApiBaseUrl`), supporting stable/beta channels and returning every validated release file in API order |
| `ExternalLinkService` | Static utility — opens external URLs in the default browser (http/https/mailto only) |
| `Crc64Service` | CRC64 hash computation for downloaded file verification |
| `OfficialHashService` | Official launcher `vc` integrity hash (`MD5(values.join(";"))` → Base64). **Field order must match the official manifest's JSON key order**: manifest file = `path, hash, size`; manifest info = `name, version, basis`; game config = `tag, name, params, version`. Guarded by `OfficialHashServiceTests` against real official values |
| `DiskSpaceService` | Checks available disk space before download/install |
| `ProcessService` | Checks if game processes are running via `Process.GetProcessesByName` |
| `VersionComparer` | Static utility — semantic version comparison: returns -1/0/1 for old/equal/new. Not DI-registered; used inline. |
| `ClickCodeService` | Saves install attribution code (`clickCode`) on first launch |
| `GamePathValidator` | Static helper in `Helpers/GamePathValidator.cs` — validates file operations stay within the game directory (path traversal rejection). Used by `GameDownloadService`, `GameUninstallService`, `ManifestValidationService`, `LocalInstallationStateStore`, and `ClickCodeService`. |
| `ServiceConfiguration` | DI container — registers all services and ViewModels via `AddLauncherServices()`. Services mostly singleton; `ISettingsEditor` singleton; ViewModels mostly transient; `DialogsViewModel` singleton. |

**HttpClient lifecycle**: `HttpClientFactory` owns a single shared `SocketsHttpHandler` (pooled, 15-min connection lifetime). Two patterns:
1. **`CreateClient(baseAddress, timeout)`** — Returns an `HttpClient` sharing the pooled handler. Caller disposes the `HttpClient` (handler survives).
2. **`CreateLeaseAsync(proxyMode, ...)`** — Returns an `HttpClientLease`. When `proxyMode` is System, creates a per-request handler; otherwise shares the default handler. Callers dispose the **lease**.

**IDisposable service disposal order** (reverse registration = forward dispose):
1. `LauncherApiClient` → 2. `ResourcePanelApiClient` → 3. `ImageCacheService` → 4. `UnifiedLogger` → 5. `GameDownloadService`

### Local files (`%LOCALAPPDATA%\Cafe Launcher\`)

| File | Purpose |
|---|---|
| `settings.json` | Launcher settings (see Settings reference below) |
| `session.active` | Active-session marker; presence on startup = previous session crashed |
| `unified.log` | All runtime diagnostics, session lifecycle, crash logs (size-based rotation: 5 MB, 4 files) |
| `download_state.json` | Serializable download resume state |
| `shown_notices.json` | Tracked shown notice IDs |
| `clickCode` | Install attribution code |

### Settings reference

| Setting | JSON key | Valid codes |
|---|---|---|
| Language | `language` | `auto`, `en`, `zh-Hans`, `zh-Hant`, `ja` |
| Theme | `themeMode` | `system`, `light`, `dark` |
| Patch URL group | `patchUrlGroup` | `official`, `cafe` |
| Launch check | `launchCheckMode` | `localManifest`, `remoteManifest`, `none` |
| Download speed limit | `downloadSpeedLimit` | `unlimited`, `1MB/s`, `5MB/s`, `10MB/s`, `25MB/s`, `50MB/s` |
| Close behavior | `closeBehavior` | `minimize`, `exit` |
| Proxy | `proxyMode` | `direct`, `system` |
| Background | `backgroundSource` | `bundled`, `remote`, `custom` |
| Wallpaper fit | `backgroundFit` | `fill`, `uniform`, `uniformToFill` |
| Wallpaper fill color | `backgroundFillColor` | Hex color string |
| Game path | `gamePath` | Absolute directory path |
| Custom background | `customBackgroundPath` | Absolute file path |
| Toast notifications | `toastNotificationsEnabled` | `true`/`false` |
| Remote content card | `showRemoteContentCard` | `true`/`false` |
| Theme color mode | `themeColorMode` | `default`, `system`, `wallpaper`, `custom` |
| Custom theme color | `customThemeColor` | Hex color string |
| Theme color palette | `themeColorPalette` | JSON array of hex strings |
| Selected palette index | `selectedThemeColorPaletteIndex` | Integer |
| Resource panel UID | `resourcePanelUid` | Player UID string |
| Update channel | `updateChannel` | `stable`, `beta` |
| Log level | `logLevel` | `verbose`, `debug`, `information`, `warning`, `error`, `fatal` |

### Key models (`Models/`)

- `LauncherApiContracts.cs` — All API response DTOs
- `LauncherStateModels.cs` — String constants for modes/behaviors (`LaunchCheckModes`, `ProxyModes`, `CloseBehaviors`, `LauncherLanguages`, `ThemeModes`, `ThemeColorModes`, `DownloadSpeedLimits`, `PatchUrlGroups`, `UpdateChannels`, `LogLevels`, `BackgroundSources`, `BackgroundFits`, `GameOperationKinds`), runtime state objects, and option types (`SettingOption`, `LanguageOption`, `ThemeOption`)
- `LocalInstallationStateModels.cs` — Local installation classifications, immutable state snapshots, commit input records
- `LocalGameContracts.cs` — `LocalManifest`, `RemoteManifest`, `ManifestFile`, `GameLauncherConfig`. **`ManifestFile` property order (`path, hash, size, vc`) is a wire contract** — do not reorder or both launchers reject each other's `manifest.json`
- `SettingsCategoryCodes.cs` — Settings section category code constants (`general`, `game`, `download-network`, `appearance`, `about`) with `Normalize()` fallback
- `PatchUrlGroupDefinition.cs` — Code + host-from/to tuples for CDN URL rewriting
- `DownloadTaskState.cs` — Serializable download resume state
- `BannerDot.cs` — Observable carousel dot indicator
- `ThemeColorPaletteItem.cs` — Extracted color data from wallpaper images
- `BestHttpCookieModels.cs` — Cookie-related models for HTTP
- `ResourcePanelModels.cs` — Resource panel data models
- `LauncherReleaseResponse.cs` — Release information from the launcher update server proxy

### Constants

Constants are split into 4 focused files under `Constants/`:
- **`LauncherConstants`** — Cross-cutting UI/product constants: `ProductName`, `DefaultThemeColor` (`#FF2E7DF6`), `ZIndexToast` (`1000`), `OfficialGameWebsiteUrl`, `CafeWebsiteUrl`, `HelpDocsUrl` (`https://docs.bluearchive.cafe/`), `GitHubReleaseRepositoryUrl`.
- **`ApiConfig`** — API endpoints, auth, release repository metadata: `ApiBaseUrl` (`https://api-launcher-jp.yo-star.com`), `ResourcePanelApiBaseUrl` (`https://api.bluearchive.cafe`), `AuthorizationSalt`, `YostarAuthorizationVersion` (`"1.7.2"`), `GitHubReleaseRepositorySlug`, `LauncherApiBaseUrl`.
- **`BuildInfo`** — Build-time metadata: `LauncherVersion` (from `AssemblyInformationalVersionAttribute`), `CommitSha` (from `AssemblyMetadataAttribute`), `BuildConfiguration` (`#if DEBUG`), `AvaloniaVersion`.
- **`GamePaths`** — Path/filename conventions: `GameTag` (`"BlueArchive_JP"`), `RootFolderName` (`"YostarGames"`), `GameFolderName` (`"BlueArchive_JP"`), `ManifestFileName`, `GameConfigFileName`, `LauncherSettingsFileName`.

### Converters
- `FlexibleBoolConverter` (in `Helpers/`) — JSON converter: reads both booleans and numbers as `bool`.
- `ToastSeverityToBrushConverter` (in `Converters/`) — resolves `ToastSeverity` to the exact `LauncherToast{Severity}Brush` resource; keeps `ToastNotification` independent of Avalonia.

### Other directories
- `Constants/` — `LauncherConstants`, `ApiConfig`, `BuildInfo`, `GamePaths`
- `Helpers/` — `FileSizeFormatter`, `GamePathValidator`, `HttpClientLease`, `FlexibleBoolConverter`
- `Controls/` — reusable UI controls: `SettingRow`, `ConfirmDialog`, `LoadingOverlay`
- `Services/Auth/` — `AuthorizationHeaderFactory` (MD5-signed API auth header)
- `Services/Diagnostics/` — `UnifiedLogger`, `LocalDiagnostics`, `LogExportService`, `CrashRecoveryService`, `LogEntrySeverity` enum (Verbose/Debug/Info/Warn/Error/Fatal). Log rotation: 5 MB threshold, 4 retained files (current + 3 rotated). Output template: `{Timestamp:O} [{Level:u3}] [{LogTitle}] {Message}{NewLine}{Exception}`.
- `Services/HttpClientLeaseSource.cs` — `IHttpClientLeaseSource` abstraction with two implementations: `ProxyAwareHttpClientLeaseSource` (production) and `FixedHttpClientLeaseSource` (testing).
- `Services/ServiceConfiguration.cs` — DI registration; `AddLauncherServices(existingLogger?)` accepts an optional pre-created `UnifiedLogger`.
- `installer/` — NSIS installer script
- `docs/adr/` — Architecture Decision Records
- `docs/superpowers/plans/` + `docs/superpowers/specs/` — Implementation plans and design specs

### Localization

All UI strings go through `LocalizationService.T(key)` and `LocalizationService.F(key, args)` for formatted strings. String data is loaded from embedded JSON resource files at `Assets/Locales/{locale}.json` (en, zh-Hans, zh-Hant, ja) at first access via `AssetLoader`. `LocalizedStrings` (generated by CommunityToolkit source generators) exposes individual `[ObservableProperty]` properties for XAML binding.

**Adding localized strings:**
1. Add the key and value to all 4 JSON files at `Assets/Locales/` (keep alphabetical order)
2. Add an `[ObservableProperty]` field to `LocalizedStrings`
3. Wire it in `LocalizedStrings.Apply()`
4. Build — JSON files are automatically embedded via `<AvaloniaResource Include="Assets\**"/>`

**Testing note:** Unit tests that exercise `LocalizationService.T()` must call `LocalizationService.InitializeForTesting(...)` with test dictionaries in a static constructor before creating service instances.

**Localized dropdown values** follow the `ThemeOption` pattern: create `SettingOption` instances with `Code` (persisted value) and `DisplayName` (set from `localizer.T()` in a `Refresh*Options()` method called from `ApplyLanguage()`). Bind the ComboBox with `SelectedValue="{Binding SelectedX}"` + `SelectedValueBinding="{Binding Code}"` + an `ItemTemplate` showing `{Binding DisplayName}`.

### Theme

Light/Dark themes defined as `ThemeDictionaries` in `App.axaml` with custom `Launcher*` brush keys. `ThemeModes.System` → `ThemeVariant.Default` (follows OS), `Light`/`Dark` → explicit. Applied via `Application.Current.RequestedThemeVariant`.

**Theme color** controls the accent color (buttons, progress bars, links). 4 modes: `default` (constant), `system` (OS accent), `wallpaper` (extracted palette), `custom` (color picker). Persisted in `settings.json` independently of light/dark mode.

### Design Tokens

Design values use `StaticResource` keys defined in `App.axaml`. See CLAUDE.md § Design Tokens or `PROJECT_CONVENTIONS.md` for the full reference tables. Key classes:

- **Spacing**: 4px grid — `LauncherSpacingXs`(4) through `LauncherSpacingXxl`(24) + `LauncherSpacingSection`(40)
- **Corner radius**: `LauncherRadiusSm`(4), `LauncherRadiusMd`(6), `LauncherRadiusLg`(8)
- **Icons**: `LauncherIconSm`(16), `LauncherIconMd`(18), `LauncherIconLg`(20), `LauncherIconXl`(22), `LauncherIconXxl`(24)
- **Control heights**: `LauncherControlHeightSetting`(36), `LauncherControlHeightDialog`(42), `LauncherControlHeightBottom`(48), `LauncherControlHeightLaunch`(58), etc.
- **Typography**: `LauncherFontSizeXs`(11) through `LauncherFontSizeDisplay`(22), `LauncherFontWeightNormal`, `LauncherFontWeightStrong`, `LauncherFontFamilyMonospace`
- **Layer hierarchy**: base content → settings overlay (100) → dialog overlay (200) → toast (1000)
- **Hardcoded visual values**: view XAML must not contain direct hexadecimal colors, `Transparent`, raw icon sizes, or raw corner radii. Use `StaticResource` tokens. Theme-invariant gradients/shadows allowed only in `App.axaml` or `MainWindow.Styles.axaml`.

### Single-instance pattern

`Program.cs` uses a named global `Mutex`. Second instances signal the first via `EventWaitHandle`, which triggers `Dispatcher.UIThread.InvokeAsync` to restore the window from tray/minimized state. Windows-only.

### API auth

`AuthorizationHeaderFactory` builds a JSON header with `{head: {game_tag, time, version}, sign: MD5(headJson + data + salt)}`. Salt is in `LauncherConstants.AuthorizationSalt`.

## Important patterns

- **No remote telemetry**: The original Electron launcher sent logs to Aliyun SLS. This rewrite explicitly excludes those paths. Always keep diagnostics local.
- **Official launcher coexistence**: This launcher shares the game directory and `manifest.json` / `game-launcher-config.json` with the official launcher — on-disk formats must stay byte-compatible. Two coupled contracts: (1) `vc` integrity hash field order in `OfficialHashService` / `ManifestFile` (manifest file = `path, hash, size`); (2) launch validation **fails open** when the remote manifest can't be fetched (`ManifestValidationService`), matching the official launcher and avoiding a launch-blocked/nothing-to-repair deadlock. Both guarded by tests. Note launch validation checks **size**, repair checks **CRC64** — asymmetry inherited from the official launcher.
- **Path safety**: `GamePathValidator.GetSafePath()` (static helper in `Helpers/GamePathValidator.cs`) validates all file operations stay within the game directory — path traversal is rejected. Used by `GameDownloadService`, `GameUninstallService`, `ManifestValidationService`, `LocalInstallationStateStore`, and `ClickCodeService`.
- **SSRF validation is proxy-aware**: `RemoteHttpUrlValidator` guards every outbound HTTP request against SSRF. DNS resolution check runs **only for direct connections**; for proxy-mode connections, DNS is skipped (the proxy resolves) — otherwise it causes false-positive `Remote URL resolves to a blocked network address` errors when users enable a proxy precisely because local DNS is blocked/poisoned. Literal-IP, localhost, scheme, and port checks still apply under proxy. Guarded by `RemoteHttpUrlValidatorTests`.
- **Download resilience**: CRC64 verification after download, rename `.tmp` → final only on success, up to 3 install-verification retries, CDN failover (primary → backup with retry order).
- **Async pause**: `GameDownloadService` uses `TaskCompletionSource`-based pause (never blocks threads). `Pause()` creates a pending `TaskCompletionSource`, download loops `await` it, `Resume()` completes it. `Stop()` also completes the TCS to unblock paused awaits before cancellation.
- **Spacing**: UI spacing follows a 4px grid (0, 4, 8, 12, 16, 20, 24, …). Repeated scalar spacing uses `LauncherSpacing*` resources; left panel margin and bottom panel horizontal padding are both 40px for visual symmetry.
- **Version comparison**: `VersionComparer.Compare()` returns -1/0/1 for old/equal/new.
- **XAML extraction**: Large XAML blocks (styles, overlays) are extracted into separate `.axaml` files under `Views/` and referenced via `<StyleInclude>` or `Classes` attributes. The main `MainWindow.axaml` keeps only the window shell and content grid.
- **Conventional commits**: Release changelog generation groups commits by `feat:`/`fix:`/`refactor:`/`perf:` prefixes. Use these prefixes for commit messages to get clean changelogs.
- **CLAUDE.md**: A parallel instruction file for Claude Code exists at the repo root. It covers the same architecture and should be kept in sync when significant structural changes are made.
- **PROJECT_CONVENTIONS.md**: A development conventions guide for AI-assisted development exists at the repo root. It defines the patterns and rules for contributing to this codebase.
- *****REMOVED***.md**: Analysis report comparing this launcher to the original Electron launcher, covering implementation differences and intentionally excluded features.
- **VSCode**: `.vscode/launch.json` has `build`/`publish`/`watch` tasks and `.NET Core Launch`/`Attach` configurations.
