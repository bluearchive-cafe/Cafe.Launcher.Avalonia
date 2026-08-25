# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository rules

Read [PROJECT_CONVENTIONS.md](PROJECT_CONVENTIONS.md) before changing production code. It is the authoritative source for the project's TDD, XAML, localization, diagnostics, DI, settings-compatibility, test, and commit requirements. Keep this file and [AGENTS.md](AGENTS.md) aligned when architecture or developer workflow changes.

The root `Cafe.Launcher.Avalonia.slnx` contains the application and both test projects. The primary application project is `src/Cafe.Launcher.Avalonia/Cafe.Launcher.Avalonia.csproj`.

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
dotnet restore .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -r win-x64

# Debug build; build.ps1 sets both telemetry variables above
.\build.ps1
dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore

# Run the desktop app
dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj

# Release build and publish
# Release is self-contained; supply the target RID explicitly for a distributable build.
dotnet build .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 --no-restore
dotnet publish .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 -o publish --no-restore
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

# Verify keys and composite-format placeholders across all localized .resx files
.\scripts\Test-LocalizationContract.ps1

# Debug build, coverage (both test projects, 50% line/branch threshold), then win-x64 Release build
.\verify.ps1
```

Tests use xUnit v3 and `coverlet.msbuild`; do not introduce a mocking framework. Prefer handwritten `HttpMessageHandler` subclasses, fakes, and stubs. Use `Avalonia.Headless.XUnit` for UI behavior. When changing XAML or styles, run `UiStyleContractTests` in addition to the focused tests. `coverage.ps1` enforces the 50% line/branch minimum and rejects regressions below the repository baseline.

Test project internals are exposed through `Properties/AssemblyInfo.cs`. Keep tests near the class under test and name them `Method_State_ExpectedResult`. New services should cover: success path, typical failure path (exception/validation failure), and boundary conditions (null input, empty collection).

To build the distributable ZIP and Inno Setup installer, install Inno Setup 6.3 or newer (7.x recommended) and make `ISCC.exe` available on `PATH` or in the default install location (`C:\Program Files\Inno Setup 7`), then run:

```powershell
.\scripts\Build-Distribution.ps1
```

GitHub Actions uses .NET `10.0.x`. The build workflow runs on `windows-latest`: `test.ps1` executes both test projects, `coverage.ps1` enforces the merged coverage baseline, then Debug and Release `win-x64` builds and a Release publish run. The tag-triggered release workflow runs on `windows-latest` (Inno Setup via Chocolatey) and builds both distribution formats with `scripts/Build-Distribution.ps1`. `release.ps1` creates commits, tags, and pushes, so run it only when explicitly asked to perform a release.

## Application architecture

### Startup and composition

- `Program.cs` owns process lifetime: Windows single-instance mutex/signaling, the logger created before DI, crash handlers, first-launch detection, and the session start/end logging lifecycle. The pre-DI `UnifiedLogger` is passed into DI so the process has one Serilog pipeline and is disposed only after session completion logging.
- `App.axaml.cs` is the composition root. It builds `ServiceCollection`, calls `ServiceConfiguration.AddLauncherServices(existingLogger:)`, constructs the single `MainWindow`, configures tray/single-instance restoration, and either shows the first-launch setup wizard or starts normal asynchronous initialization.
- `Composition/ServiceConfiguration.cs` is the DI registration point. This is a single-window desktop app: services and view models are singletons. Microsoft DI disposes created services in reverse registration order, so position a new `IDisposable` service after the consumers that must release first. `Program.RunSession` explicitly disposes the shared pre-DI `UnifiedLogger` after all session-end logging has completed.
- `App.axaml` defines Fluent/Material resources, theme dictionaries, and `Launcher*` design tokens.

### MVVM and views

- `Features/` organizes behavior vertically across the layers — shell, game operations, first-launch setup, and diagnostics each own their boundary. When adding a major feature, prefer extending an existing feature boundary or adding a new `Features/` directory rather than scattering changes across Services/ViewModels/Views.
- Avalonia uses compiled, explicit bindings; there is no reflection-based view locator. `ViewModelBase` extends CommunityToolkit.Mvvm's `ObservableObject`.
- `MainWindowViewModel` is the shell that composes focused view models for shell state, background, remote content, dialogs, game operations, toast host, window chrome, settings, resource panel, log viewer, and first-launch setup. Child view models call parent capabilities through injected delegates and expose events for parent-owned coordination.
- `Views/MainWindow.axaml` retains the window shell. Styles, settings, dialog/log-viewer, and toast layers are separate `.axaml` files. State-driven settings navigation selects categories inside the one window rather than using a navigation framework.
- XAML values must use the `Launcher*` resources in `App.axaml`; raw colors, `Transparent`, raw icon sizes, and raw 4/6/8 corner radii are disallowed in view XAML. Overlay order is base content → settings (100) → dialogs (200) → toast (1000).

### Core runtime and game operations

- `LauncherCoreService.LoadAsync()` is the startup/refresh orchestrator. It reads settings, loads local installation state, starts the required and optional remote API requests concurrently, derives `LauncherRuntimeState`, and returns `LauncherStatusSnapshot` for the view model.
- `Features/GameOperations` separates command presentation (`GameOperationsViewModel`) from journey orchestration (`GameOperationJourney`) and workflows for launch, installation, and uninstall. Downloads use remote-manifest diffs, up to 10 concurrent downloads, `.tmp` files, Range resume, CRC64 verification, persisted state, and pause/resume without blocking threads.
- `HttpClientFactory` owns shared pooled handlers and provides proxy-aware leases. Dispose clients/leases according to the factory method used; do not create ad-hoc long-lived handlers.

### Persistence and compatibility contracts

- Launcher data lives in `%LOCALAPPDATA%\Cafe Launcher\`: settings, unified log, persisted download state, shown notices, and click code. `LauncherSettingsService` must preserve compatibility with old or invalid `settings.json` content; `SettingsEditor` gives the UI transactional save/discard behavior.
- The game directory is normalized to `YostarGames\BlueArchive_JP`. All file operations must go through `GamePathValidator` so they remain inside that game directory.
- `LocalInstallationStateStore` manages `game-launcher-config.json` and `manifest.json` as one coordinated installation state shared with the official launcher. Preserve the JSON/wire field order used by `OfficialHashService`—changing it makes launchers reject each other's manifest.
- Launch validation intentionally fails open if a requested remote manifest cannot be retrieved; repair uses CRC64 whereas launch validation checks file size. These mirror the official launcher and are covered by contract tests.
- Outbound remote URLs go through `RemoteHttpUrlValidator`. Its local DNS rejection is intentionally skipped only for proxy egress, while scheme, port, localhost, and literal-IP checks remain active.

### Localization, themes, and diagnostics

- UI strings come from the embedded `Resources/LauncherStrings{,.zh-Hans,.zh-Hant,.ja}.resx` resources through `LocalizationService`. `ShellViewModel` exposes `LocalizedTextCatalog` as `Shell.I18n`; XAML resolves resource keys with `{Binding Shell.I18n[resourceKey]}`. Adding a new string:
  1. Add the key to all four `.resx` files, keeping each file alphabetically ordered.
  2. Use `localizer.T("resourceKey")` or `localizer.F("resourceKey", ...)` in C#, and `Shell.I18n[resourceKey]` in XAML.
  3. Add `AutomationProperties.Name` for interactive controls.
  4. Run `.\scripts\Generate-LauncherStringsDesigner.ps1` after adding or renaming a key, then `.\scripts\Test-LocalizationContract.ps1`.
  Localization unit tests initialize resource data via `TestLocalizationHelper.Initialize()` or `LocalizationService.InitializeForTesting(...)`.
- Theme mode, wallpaper-derived/custom accents, and other UI state are persisted settings. Keep theme brushes in the dictionaries rather than replacing theme-specific brushes with hardcoded values.
- `UnifiedLogger` writes local rolling logs; application code uses `LocalDiagnostics` with a concise PascalCase title and must not log authorization headers, salts, cookies, or tokens.

## High-value test and change contracts

- `UiStyleContractTests` protect XAML tokens and overlay layering.
- `OfficialHashServiceTests`, `ManifestValidationServiceTests`, `GamePathValidatorTests`, and `RemoteHttpUrlValidatorTests` protect interoperability and safety contracts. Run them when touching the corresponding code paths.
- Settings changes require defaults, normalization, and backward-compatible JSON field handling. New user-visible strings require all four locale files and localized automation names for interactive controls.
