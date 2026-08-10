# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository rules

Read [PROJECT_CONVENTIONS.md](PROJECT_CONVENTIONS.md) before changing production code. It is the authoritative source for the project's TDD, XAML, localization, diagnostics, DI, settings-compatibility, test, and commit requirements. Keep this file and [AGENTS.md](AGENTS.md) aligned when architecture or developer workflow changes.

This repository contains no solution file. The primary application project is `Cafe.Launcher.Avalonia.csproj`; test projects must be addressed explicitly.

- Requires the .NET 10 SDK. The application is a `net10.0` Avalonia Windows GUI (`WinExe`); Release builds are self-contained for `win-x64`.
- Builds enforce nullable references, code style, and warnings as errors. A successful build has zero warnings.
- C# uses 4-space indentation and CRLF; markup, JSON, Markdown, and PowerShell use LF. File-scoped namespaces are preferred. There is no separate lint or formatting command—the build analyzers are the enforcement point.
- Do not add remote telemetry. Diagnostics remain local and production code logs through `LocalDiagnostics`, except for the pre-DI bootstrap path in `Program.cs` and the logging implementation itself.

## Build, run, and test

Restore before commands that use `--no-restore`:

```powershell
# Restore app dependencies for Windows builds
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet restore .\Cafe.Launcher.Avalonia.csproj -r win-x64

# Debug build; build.ps1 sets both telemetry variables above
.\build.ps1
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore

# Run the desktop app
dotnet run --project .\Cafe.Launcher.Avalonia.csproj

# Release build and publish
# Release is self-contained; supply the target RID explicitly for a distributable build.
dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 --no-restore
dotnet publish .\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 -o publish --no-restore
```

```powershell
# Unit tests
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore
# One unit-test class
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~VersionComparerTests"

# Avalonia headless UI tests
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore
# One headless-test class
dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SystemTrayServiceTests"

# Both test projects with Coverlet; deletes and recreates TestResults\Coverage
.\coverage.ps1

# Both test projects (without coverage)
.\test.ps1

# UI style-contract and headless UI tests (run after XAML or style changes)
.\dev.ps1 ui

# Verify keys and composite-format placeholders across all locale JSON files
.\scripts\Test-LocalizationContract.ps1

# Debug build, coverage (both test projects, 50% line/branch threshold), then win-x64 Release build
.\verify.ps1
```

Tests use xUnit v3 and `coverlet.msbuild`; do not introduce a mocking framework. Prefer handwritten `HttpMessageHandler` subclasses, fakes, and stubs. Use `Avalonia.Headless.XUnit` for UI behavior. When changing XAML or styles, run `UiStyleContractTests` in addition to the focused tests. Coverage thresholds: line ≥ 50%, branch ≥ 50% (enforced by `coverage.ps1`).

Test project internals are exposed through `Properties/AssemblyInfo.cs`. Keep tests near the class under test and name them `Method_State_ExpectedResult`. New services should cover: success path, typical failure path (exception/validation failure), and boundary conditions (null input, empty collection).

To build the distributable ZIP and NSIS installer, install NSIS 3 and make `makensis`/`makensis.exe` available on `PATH`, then run:

```powershell
.\scripts\Build-Distribution.ps1
```

GitHub Actions runs on `ubuntu-latest` with .NET `10.0.x`. The build workflow currently runs the unit-test project, then cross-builds and publishes `win-x64`; local `coverage.ps1` is the command that runs both test projects. `release.ps1` creates commits, tags, and pushes, so run it only when explicitly asked to perform a release.

## Application architecture

### Startup and composition

- `Program.cs` owns process lifetime: Windows single-instance mutex/signaling, the logger created before DI, crash handlers, first-launch detection, and the `session.active` crash-recovery lifecycle. The pre-DI `UnifiedLogger` is passed into DI so the process has one Serilog pipeline and is disposed only after session completion logging.
- `App.axaml.cs` is the composition root. It builds `ServiceCollection`, calls `ServiceConfiguration.AddLauncherServices(existingLogger:)`, constructs the single `MainWindow`, configures tray/single-instance restoration, and either shows the first-launch setup wizard or starts normal asynchronous initialization.
- `Services/ServiceConfiguration.cs` is the only DI registration point. This is a single-window desktop app: services and view models are singletons. Be deliberate when adding `IDisposable` services — Microsoft DI disposes registrations in reverse order. The expected disposal order is `LauncherApiClient` → `ResourcePanelApiClient` → `ImageCacheService` → `UnifiedLogger` → `GameDownloadService`. New `IDisposable` services should be registered before `UnifiedLogger` unless a later disposal slot is required.
- `App.axaml` defines Fluent/Material resources, theme dictionaries, and `Launcher*` design tokens.

### MVVM and views

- `Features/` organizes behavior vertically across the layers — shell, game operations, first-launch setup, and diagnostics each own their boundary. When adding a major feature, prefer extending an existing feature boundary or adding a new `Features/` directory rather than scattering changes across Services/ViewModels/Views.
- Avalonia uses compiled, explicit bindings; there is no reflection-based view locator. `ViewModelBase` extends CommunityToolkit.Mvvm's `ObservableObject`.
- `MainWindowViewModel` is the shell that composes focused view models for shell state, background, remote content, dialogs, game operations, toast host, window chrome, settings, resource panel, log viewer, and first-launch setup. Child view models call parent capabilities through injected delegates and expose events for parent-owned coordination.
- `Views/MainWindow.axaml` retains the window shell. Styles, settings, dialog/log-viewer, and toast layers are separate `.axaml` files. State-driven settings navigation selects categories inside the one window rather than using a navigation framework.
- XAML values must use the `Launcher*` resources in `App.axaml`; raw colors, `Transparent`, raw icon sizes, and raw 4/6/8 corner radii are disallowed in view XAML. Overlay order is base content → settings (100) → dialogs (200) → toast (1000).

### Core runtime and game operations

- `LauncherCoreService.LoadAsync()` is the startup/refresh orchestrator. It reads settings, loads local installation state, starts the required and optional remote API requests concurrently, derives `LauncherRuntimeState`, and returns `LauncherStatusSnapshot` for the view model.
- `GameOperationsBackend` keeps UI commands separate from the implementation services: `GameLaunchService`, `GameDownloadService`, and `GameUninstallService`. Downloads use remote-manifest diffs, up to 10 concurrent downloads, `.tmp` files, Range resume, CRC64 verification, persisted state, and pause/resume without blocking threads.
- `HttpClientFactory` owns shared pooled handlers and provides proxy-aware leases. Dispose clients/leases according to the factory method used; do not create ad-hoc long-lived handlers.

### Persistence and compatibility contracts

- Launcher data lives in `%LOCALAPPDATA%\Cafe Launcher\`: settings, crash marker, unified log, persisted download state, shown notices, and click code. `LauncherSettingsService` must preserve compatibility with old or invalid `settings.json` content; `SettingsEditor` gives the UI transactional save/discard behavior.
- The game directory is normalized to `YostarGames\BlueArchive_JP`. All file operations must go through `GamePathValidator` so they remain inside that game directory.
- `LocalInstallationStateStore` manages `game-launcher-config.json` and `manifest.json` as one coordinated installation state shared with the official launcher. Preserve the JSON/wire field order used by `OfficialHashService`—changing it makes launchers reject each other's manifest.
- Launch validation intentionally fails open if a requested remote manifest cannot be retrieved; repair uses CRC64 whereas launch validation checks file size. These mirror the official launcher and are covered by contract tests.
- Outbound remote URLs go through `RemoteHttpUrlValidator`. Its local DNS rejection is intentionally skipped only for proxy egress, while scheme, port, localhost, and literal-IP checks remain active.

### Localization, themes, and diagnostics

- UI strings come from the embedded `Assets/Locales/{en,zh-Hans,zh-Hant,ja}.json` resources through `LocalizationService`. Adding a new string:
  1. Add the key alphabetically to all four locale files.
  2. Add an `[ObservableProperty] private string newKey = "";` field in `LocalizedStrings`.
  3. Add `NewKey = localizer.T("newKey");` in `LocalizedStrings.Apply()`.
  4. Bind in XAML via `{Binding Shell.I18n.NewKey}` and add `AutomationProperties.Name` for interactive controls.
  Localization unit tests must initialize test dictionaries via `TestLocalizationHelper.Initialize()` before using `LocalizationService.T()`. After modifying any locale file, run `.\scripts\Test-LocalizationContract.ps1`.
- Theme mode, wallpaper-derived/custom accents, and other UI state are persisted settings. Keep theme brushes in the dictionaries rather than replacing theme-specific brushes with hardcoded values.
- `UnifiedLogger` writes local rolling logs; application code uses `LocalDiagnostics` with a concise PascalCase title and must not log authorization headers, salts, cookies, or tokens.

## High-value test and change contracts

- `UiStyleContractTests` protect XAML tokens and overlay layering.
- `OfficialHashServiceTests`, `ManifestValidationServiceTests`, `GamePathValidatorTests`, and `RemoteHttpUrlValidatorTests` protect interoperability and safety contracts. Run them when touching the corresponding code paths.
- Settings changes require defaults, normalization, and backward-compatible JSON field handling. New user-visible strings require all four locale files and localized automation names for interactive controls.
