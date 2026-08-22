# Repository Map

Generated: 2026-08-21

## Scope and repository state

This map describes the current checkout of `Cafe.Launcher.Avalonia`. The working tree is substantially dirty: `git diff --stat` reported 54 tracked paths with changes, and additional untracked files are present. The audit therefore describes the working tree as observed, not only `HEAD` (`a4d9437`, also the current `origin/main`).

## Technology profile

- Language: C# with nullable reference types enabled.
- Runtime: .NET 10 (`net10.0`), SDK pinned through `global.json`.
- UI: Avalonia 12.1.1, compiled bindings, Fluent theme and Material Icons.
- Application shape: Windows desktop launcher (`WinExe`), with a Release `win-x64` self-contained publish path.
- Dependency injection: `Microsoft.Extensions.DependencyInjection`, composed in `Composition/ServiceConfiguration.cs`.
- Logging: Serilog with asynchronous file sink; local diagnostics and logs are used rather than remote telemetry.
- Testing: xUnit v3 unit tests and Avalonia Headless xUnit UI tests.

## Top-level structure

| Area | Responsibility |
| --- | --- |
| `Program.cs`, `App.axaml`, `App.axaml.cs` | Process lifetime, application startup, Avalonia application setup, and shutdown wiring. |
| `Composition/` | Dependency-injection composition root and service registration. |
| `Features/` | Vertical feature slices: Shell, GameOperations, Settings, SetupWizard, Diagnostics, and ResourcePanel. |
| `Services/` | Core application services: HTTP, downloads, settings, installation state, localization, updates, paths, audio, and diagnostics. |
| `ViewModels/` | Shared or cross-feature view models, including remote content and main-window orchestration. |
| `Views/` | Avalonia views and merged style resources. |
| `Controls/` | Reusable Avalonia controls and control-local styles. |
| `Models/`, `Constants/`, `Helpers/`, `Converters/` | Shared domain/data contracts, constants, utility code, and binding converters. |
| `Resources/`, `Assets/` | Localized `.resx` resources and packaged runtime assets. |
| `tests/Cafe.Launcher.Avalonia.Tests/` | Unit and contract tests. |
| `tests/Cafe.Launcher.Avalonia.HeadlessTests/` | Headless Avalonia UI tests. |
| `scripts/`, `installer/` | Build, localization, coverage, distribution, and NSIS packaging automation. |
| `.github/workflows/` | Debug/Release CI, coverage, packaging, and release publication. |

## Runtime and dependency flow

```text
Program
  -> App / ServiceConfiguration
       -> Shell and feature view models
            -> application services
                 -> HTTP, filesystem, settings, downloads, logging, and update boundaries
```

`Program` owns the process-level single-instance and shutdown boundary. `App` creates the service provider. The Shell coordinates the window-level experience, while feature slices keep user journeys grouped by capability. Services own external effects and persistence. URL and filesystem validation helpers provide explicit security boundaries for remote content, game paths, downloads, and updates.

## Build and validation entry points

- `build.ps1`: Debug restore/build with telemetry disabled.
- `test.ps1`: runs both test projects.
- `coverage.ps1`: runs Coverlet and enforces the repository coverage thresholds.
- `verify.ps1`: Debug build, coverage, and Release build sequence.
- `dev.ps1 ui`: UI style-contract and headless UI validation.
- `scripts/Test-LocalizationContract.ps1`: validates resource keys and composite-format placeholders.
- `scripts/Build-Distribution.ps1` and `installer/`: publish and NSIS packaging.

## Important repository rules

- Treat warnings as errors and preserve compiled bindings and nullable annotations.
- Add UI strings to all four localized resource sets and regenerate `LauncherStrings.Designer.cs`.
- Use semantic `Cafe.*` design-system tokens in XAML; style contracts cover these conventions.
- Keep path operations behind the existing validators and preserve atomic/debounced settings persistence.
- Run focused tests after changes, localization contract checks after resource changes, and the complete verification sequence before release.

