# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Build & Run

```powershell
.\verify.ps1                           # Complete verification: Debug build, both test projects, Release build
.\build.ps1                              # Debug build (expect 0 warnings, 0 errors)
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release --no-restore   # Release build
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -o publish   # Self-contained publish (win-x64)
```

**Tests** (xUnit 2.9.3, under `tests/Cafe.Launcher.Avalonia.Tests/`, with coverlet 10.0.1):
```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore                 # Run unit tests
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore # Run headless tests
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests" # Run a single test class
```

Available test classes include `VersionComparerTests`, `LauncherApiClientTests`, `LauncherConstantsTests`, `LauncherSettingsServiceTests`, `SettingsNormalizerTests`, `SettingsEditorTests`, `LocalizationServiceTests`, `MainWindowViewModelTests`, `DialogsViewModelTests`, `GameDownloadServiceTests`, `GameInstallationPathTests`, `LocalInstallationStateStoreTests`, `LauncherCoreServiceTests`, `InstallationOperationStateTests`, `PatchUrlGroupServiceTests`, `BestHttpCookieLibraryServiceTests`, `ResourcePanelUidServiceTests`, `ExternalLinkServiceTests`, `ResourcePanelApiClientTests`, `LauncherUpdateServiceTests`, `HttpClientFactoryTests`, `ToastServiceTests`, `OfficialHashServiceTests`, and `UiStyleContractTests`.

CI is GitHub Actions on `windows-latest`, .NET 10.0.x:
- **build.yml** (push/PR to `main`): restore, Debug build, test, Release build, self-contained publish, upload artifact.
- **release.yml** (push of `v*` tag): test, Release build, publish, ZIP archive, generate the grouped changelog through `scripts/New-ReleaseChangelog.ps1`, then create matching GitHub Releases in both the source repository and the distribution repository (`bluearchive-cafe/Cafe.Launcher.Avalonia_Release`, defined as `GitHubReleaseRepositorySlug` in constants). The local release script uses the same changelog generator. The distribution repository uses the `RELEASE_REPOSITORY_TOKEN` Actions secret. Pre-release if tag contains `-`.

**Telemetry must be off during local builds** (already set in `build.ps1`):
- `DOTNET_CLI_TELEMETRY_OPTOUT=1`
- `AVALONIA_TELEMETRY_OPTOUT=1`

## Architecture

**Tech stack**: .NET 10.0, Avalonia 12.0.4, CommunityToolkit.Mvvm 8.4.2 (source generators), Material.Icons.Avalonia, Fluent Theme. Compiled bindings enabled by default.

**MVVM pattern** with explicit XAML composition. `ViewModelBase` extends `ObservableObject`; the app does not use a reflection-based `ViewLocator`.

### Single-window desktop app

One `MainWindow` (1300×754 initial size, resizable with MinWidth 1024/MinHeight 640, borderless with custom chrome). Window size/position is not persisted across sessions. The ViewModel drives panel visibility through boolean flags (`IsInstallPanelVisible`, `IsControlPanelVisible`, `IsProgressPanelVisible`, `IsSettingsVisible`).

**View files** (XAML split by concern, all under `Views/` or `Controls/`):
- `MainWindow.axaml` — window shell, title bar, remote content panel, bottom install/progress/control panels
- `MainWindow.Styles.axaml` — all `Window.Styles` extracted via `<StyleInclude Source="avares://..."/>`
- `MainWindowSettingsOverlay.axaml` — settings dialog overlay shell: category navigation, runtime status summary, section host, and transactional footer
- `SettingsGeneralSection.axaml`, `SettingsGameSection.axaml`, `SettingsDownloadNetworkSection.axaml`, `SettingsAppearanceSection.axaml`, `SettingsNotificationsContentSection.axaml`, `SettingsAdvancedSection.axaml`, `SettingsAboutSection.axaml` — the seven settings sections; all share the owning `MainWindowViewModel` data context and use the shared `SettingRow` control for consistent row layout
- `MainWindowDialogsOverlay.axaml` — notice popup, resource panel, update dialog, crash recovery; the six pure confirm dialogs (repair, RP-source, stop, close-while-downloading, uninstall, unsaved) use the shared `ConfirmDialog` control
- `MainWindowToastOverlay.axaml` — toast notification overlay
- `Controls/SettingRow.axaml` — reusable settings row (icon + title + description + action slot) used by all settings sections
- `Controls/ConfirmDialog.axaml` — reusable confirmation dialog (title, description, icon, message, cancel/confirm actions) used by the six pure confirm dialogs
- `Controls/LoadingOverlay.axaml` — reusable loading overlay (indeterminate progress bar + label) used by banner and remote content loading states

**Entries:**
1. **Program.cs** — Process mutex (`Local\Cafe_Launcher_SI`), single-instance enforcement via `EventWaitHandle` signal. Creates `UnifiedLogger` + `CrashRecoveryService` before DI is available; exposes the logger via `PreDiLogger` so the DI container reuses the same instance. Tracks `PreviousSessionCrashed` via a `session.active` marker file. `RunSession` orchestrates session lifecycle with proper crash-marker preservation.
2. **App.axaml.cs** — On framework init: builds DI container via `ServiceConfiguration.AddLauncherServices()`, resolves `MainWindowViewModel`, creates `MainWindow`, wires `ClickCodeService`, `SystemTrayService`. Starts a background thread listening for `EventWaitHandle` signals to restore window from tray.
3. **App.axaml** — Light/Dark `ThemeDictionaries` with custom `Launcher*` brushes, FluentTheme + MaterialIconStyles.

**Composition root**: `ServiceConfiguration.AddLauncherServices()` is the DI configuration — it registers all services with `Microsoft.Extensions.DependencyInjection`. The container is built in `App.axaml.cs` via `ServiceCollection.BuildServiceProvider()`. All services and ViewModels are registered as `AddSingleton` (single-window desktop app, no scoped boundaries). Thread-safe disposal order for IDisposable services is defined by reverse registration order.

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
| `LauncherApiClient` | HTTP to `api-launcher-jp.yo-star.com`, MD5-signed `Authorization` header, envelope unwrapping |
| `LauncherCoreService` | Orchestrates API + local state into `LauncherStatusSnapshot`. Exposed as `ILauncherCoreService` in the DI container. |
| `LauncherSettingsService` | Reads/writes `settings.json` at `%LOCALAPPDATA%\Cafe Launcher\`, normalizes enum values, handles legacy camelCase fields |
| `GameInstallationPath` | Computes the default game path and normalizes paths to `YostarGames\BlueArchive_JP`. Default path is the launcher's own directory (`AppContext.BaseDirectory`) + `YostarGames\BlueArchive_JP`, matching the official launcher's `dirname(exe)` default |
| `LocalInstallationStateStore` | Strictly reads, validates, commits, and deletes local `game-launcher-config.json` + `manifest.json` as one installation state |
| `GameDownloadService` | Install/update/repair: manifest diff → parallel CDN download (10 concurrent, `.tmp` files, `Range` resume, CRC64 verify, rename on success). Supports download speed throttling, async pause/resume via `TaskCompletionSource`. Implements `IDisposable` — thread-safe CTS management via `activeDownloadLock`. |
| `GameLaunchService` | Manifest validation + process launch |
| `GameUninstallService` | Guarded uninstall (checks path safety, exe not running, deletes only manifest-listed files) |
| `LocalizationService` | Inline dictionaries for `en`/`zh-Hans`/`ja`; `auto` resolves via `CultureInfo.CurrentUICulture` |
| `SystemTrayService` | Avalonia 12 `TrayIcon` + `NativeMenu` for minimize-to-tray |
| `ToastService` | Event-based transient notifications (info/success/warning/error) |
| `LocalDiagnostics` | Public facade over `UnifiedLogger`; wraps `LogAsync` as `ErrorAsync`/`MessageAsync`/`LogSync`. All logs go to `unified.log` via the shared `UnifiedLogger` instance. |
| `PatchUrlGroupService` | URL rewriting between Official and Cafe CDN hosts for manifest + CDN config URLs |
| `NoticeStateService` | Tracks which notice IDs have been shown (persisted to `shown_notices.json`) |
| `LauncherUpdateService` | Checks stable and beta launcher releases through the server proxy endpoint and returns every validated release file in API order |
| `Crc64Service` | CRC64 hash computation for downloaded file verification |
| `OfficialHashService` | Official launcher `vc` integrity hash (`MD5(values.join(";"))` → Base64). **Field order must match the official manifest's JSON key order**: manifest file = `path, hash, size`; manifest info = `name, version, basis`; game config = `tag, name, params, version`. Guarded by `OfficialHashServiceTests` |
| `ProxySettingsService` | Creates proxy-aware `SocketsHttpHandler` instances for `HttpClientFactory` |
| `DiskSpaceService` | Checks available disk space before download/install |
| `ProcessService` | Checks if game processes are running via `Process.GetProcessesByName` |
| `VersionComparer` | Static utility — semantic version comparison: returns -1/0/1 for old/equal/new |
| `ClickCodeService` | Saves install attribution code (`clickCode`) on first launch |
| `ImageCacheService` | Caches downloaded images (banners, avatars). Implements `IDisposable`. |
| `ExternalLinkService` | Static utility — opens external URLs in the default browser (http/https/mailto only) |
| `ManifestValidationService` | Validates local game files against manifest by **file size**. `remoteManifest` mode fetches the manifest at the **local** `version`/`basis` and **fails open** (allows launch) when it can't be obtained — matching the official launcher and avoiding a launch-blocked/nothing-to-repair deadlock |
| `ResourcePanelApiClient` | HTTP client for resource panel data. Implements `IDisposable`. |
| `ResourcePanelUidService` | Manages resource panel UID state |
| `BestHttpCookieLibraryService` | Cookie handling for HTTP requests |
| `GamePathValidator` | Static utility — validates file operations stay within the game directory |
| `ThemeColorExtractionService` | Extracts dominant colors from wallpaper images for UI theming |

### Key models (`Models/`)

- `LauncherApiContracts.cs` — All API response DTOs
- `LauncherStateModels.cs` — String constants for modes/behaviors (`LaunchCheckModes`, `ProxyModes`, `CloseBehaviors`, `LauncherLanguages`, `ThemeModes`, `DownloadSpeedLimits`, `PatchUrlGroups`), plus runtime state objects (`LauncherStatusSnapshot`, `LauncherRemoteState`, `LauncherRuntimeState`, `LauncherSettings`, `GameOperationProgress`, `GameOperationResult`), and option types (`SettingOption`, `LanguageOption`, `ThemeOption`) for localized dropdown binding
- `LocalInstallationStateModels.cs` — Local installation classifications, immutable state snapshots, and commit input records
- `LocalGameContracts.cs` — `LocalManifest`, `RemoteManifest`, `ManifestFile`, `GameLauncherConfig`. **`ManifestFile` property order (`path, hash, size, vc`) is a wire contract** — it sets the serialized JSON key order and the `vc` hash order; do not reorder or both launchers reject each other's `manifest.json`
- `PatchUrlGroupDefinition.cs` — Code + host-from/to tuples for CDN URL rewriting
- `DownloadTaskState.cs` — Serializable download resume state
- `BannerDot.cs` — Observable carousel dot indicator
- `LauncherReleaseResponse.cs` — Launcher release data returned by the server proxy: version, release date, and file entries containing name, URL, SHA-512, size, and formatted display size. The update dialog requires explicit file selection.

### Constants

Constants are split across `LauncherConstants`, `ApiConfig`, `BuildInfo`, and `GamePaths`. Launcher self-update requests use the exact `ApiConfig.LauncherApiBaseUrl` and `ApiConfig.LauncherReleasesPath` values. `BuildInfo.LauncherVersion` reads `AssemblyInformationalVersionAttribute` and must match the `.csproj` `VersionPrefix`.

### Patch URL groups

Users can switch between `Official` (yo-star.com) and `Cafe` (bluearchive.cafe) CDN hosts for downloading game files. The `PatchUrlGroupService` defines host-rewrite rules, and `LauncherApiClient.RewriteManifestUrl()` / `GameDownloadService.BuildDownloadUrl()` apply them when constructing download URLs. The setting is persisted as `patchUrlGroup` in `settings.json`. A sentinel test ensures URL rewriting scope is strictly limited to package download hosts — no status/list, serverinfo, or SDK netloc endpoints are touched.

### Converters

`ToastSeverityToBrushConverter` (`Converters/`) — maps toast severity to a brush for toast icons/labels. Banner images bind directly to a pre-decoded `BannerBitmap` property on the model.

### Other directories

- `Constants/` — `LauncherConstants`, `ApiConfig`, `BuildInfo`, and `GamePaths`
- `Helpers/` — `FileSizeFormatter`, `GamePathValidator`
- `Services/Auth/` — `AuthorizationHeaderFactory` (MD5-signed API auth header)
- `Services/Diagnostics/` — `UnifiedLogger` (Serilog-backed engine with async sink, enrichers, LoggingLevelSwitch), `LocalDiagnostics` (facade), `LogExportService`, `CrashRecoveryService`. All diagnostics and crash logs go to a single `unified.log`.

### Localization

All UI strings go through `LocalizationService.T(key)` and `LocalizationService.F(key, args)` for formatted strings. `LocalizedStrings` (generated by CommunityToolkit source generators) exposes individual `[ObservableProperty]` properties for XAML binding: `{Binding I18n.Settings}`, etc.

**Adding localized strings:**
1. Add the key to all 3 language dictionaries (`en`, `zh-Hans`, `ja`)
2. Add an `[ObservableProperty]` field to `LocalizedStrings`
3. Wire it in `LocalizedStrings.Apply()`

**Localized dropdown values** follow the same pattern as `ThemeOption`: create `SettingOption` instances with `Code` (the persisted value) and `DisplayName` (set from `localizer.T()` in a `Refresh*Options()` method called from `ApplyLanguage()`). Bind the ComboBox with `SelectedValue="{Binding SelectedX}"` + `SelectedValueBinding="{Binding Code}"` + an `ItemTemplate` showing `{Binding DisplayName}`.

Settings category selection is session UI state owned by `SettingsViewModel`: closing and reopening settings on the same `MainWindowViewModel` preserves the category, while a new `MainWindowViewModel` starts at `general`. Every new setting belongs to exactly one of the seven category sections; navigation must not save, discard, or recreate the shared settings draft.

### Theme

Light/Dark themes defined as `ThemeDictionaries` in `App.axaml` with custom `Launcher*` brush keys. `ThemeModes.System` → `ThemeVariant.Default` (follows OS), `Light`/`Dark` → explicit. Applied via `Application.Current.RequestedThemeVariant`.

### Design Tokens

Numeric design tokens are defined as `StaticResource` keys in `App.axaml`. See CLAUDE.md § Design Tokens for the full reference table. Key values:

- **Spacing**: 4px grid — `LauncherSpacingXs`(4) through `LauncherSpacingXxl`(24) + `LauncherSpacingSection`(40)
- **Corner radius**: `LauncherRadiusSm`(4) for controls, `LauncherRadiusMd`(6) for panels, `LauncherRadiusLg`(8) for dialogs
- **Icons**: `LauncherIconSm`(16), `LauncherIconMd`(18), `LauncherIconLg`(20), `LauncherIconXl`(22), `LauncherIconXxl`(24)
- **Control heights**: `LauncherControlHeightSetting`(36), `LauncherControlHeightDialog`(42), `LauncherControlHeightBottom`(48), `LauncherControlHeightLaunch`(58), `LauncherSwatchSize`(28), `LauncherChipHeight`(32), `LauncherFieldHeight`(40), `LauncherDialogTitleHeight`(56)
- **Z-index**: base content → settings overlay (100) → dialog overlay (200) → toast (`LauncherConstants.ZIndexToast`, 1000)
- **Visual value rules**: view XAML uses semantic brushes and tokenized icon/radius values. Direct colors, `Transparent`, raw icon sizes, and raw 4/6/8 corner radii are forbidden outside `App.axaml` and `MainWindow.Styles.axaml`. Theme-invariant wallpaper gradients and the three shadows remain centralized in those resource/style files.

### Single-instance pattern

`Program.cs` uses a named global `Mutex`. Second instances signal the first via `EventWaitHandle`, which triggers `Dispatcher.UIThread.InvokeAsync` to restore the window from tray/minimized state. Windows-only (`EventWaitHandle` is not supported on Linux — see commit `19db5a3`).

### API auth

`AuthorizationHeaderFactory` builds a JSON header with `{head: {game_tag, time, version}, sign: MD5(headJson + data + salt)}`. Salt is in `LauncherConstants.AuthorizationSalt`.

## Important patterns

- **No remote telemetry**: The original Electron launcher sent logs to Aliyun SLS. This rewrite explicitly excludes those paths (`/api/launcher/advanced/config`, `/api/open/api/config`). Always keep diagnostics local.
- **Official launcher coexistence**: shares the game directory and `manifest.json` / `game-launcher-config.json` with the official launcher, so on-disk formats must stay byte-compatible. Two coupled contracts: (1) the `vc` field order in `OfficialHashService` / `ManifestFile` (manifest file = `path, hash, size`) — mismatch makes each launcher flag the other's manifest corrupted; (2) launch validation **fails open** when the remote manifest can't be fetched (`ManifestValidationService`), avoiding a launch-blocked/nothing-to-repair loop. Guarded by `OfficialHashServiceTests` and `ManifestValidationServiceTests`. Launch validation checks **size**, repair checks **CRC64** — asymmetry inherited from the official launcher.
- **Path safety**: `GameDownloadService.GetSafePath()` validates that all file operations stay within the game directory — path traversal is rejected.
- **SSRF validation is proxy-aware**: `RemoteHttpUrlValidator` guards outbound HTTP (`RemoteHttpRequestService.SendAsync`) with scheme/port/userinfo/localhost-name/literal-private-IP checks plus a local DNS resolution check that rejects non-public addresses. The DNS check runs **only for direct connections**. When the connection egresses through a system proxy, callers pass `connectionUsesProxy: true` to skip it — the proxy resolves DNS and the launcher never dials the resolved IP, so a local check is meaningless and harmful (it causes `Remote URL resolves to a blocked network address.` when local DNS for the target is blocked/poisoned, which is exactly when users enable a proxy). Literal-IP/localhost/scheme/port checks still apply. Guarded by `RemoteHttpUrlValidatorTests`.
- **Download resilience**: CRC64 verification after download, rename `.tmp` → final only on success, up to 3 install-verification retries, CDN failover (primary → backup with retry order).
- **Async pause**: `GameDownloadService` uses `TaskCompletionSource`-based pause (never blocks threads). `Pause()` creates a pending `TaskCompletionSource`, download loops `await` it, `Resume()` completes it. `Stop()` also completes the TCS to unblock paused awaits before cancellation.
- **Spacing**: UI spacing follows a 4px grid (0, 4, 8, 12, 16, 20, 24, …). Left panel margin and bottom panel horizontal padding are both 40px for visual symmetry.
- **Version comparison**: `VersionComparer.Compare()` returns -1/0/1 for old/equal/new.
- **XAML extraction**: Large XAML blocks (styles, overlays) are extracted into separate `.axaml` files under `Views/` and referenced via `<StyleInclude>` or `Classes` attributes. The main `MainWindow.axaml` keeps only the window shell and content grid.
