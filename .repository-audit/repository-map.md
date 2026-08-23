# Repository Map

Generated: 2026-08-23

## Scope and repository state

This map describes branch `codex/design-system-redesign` at `73fe10c`, compared with `origin/main` at `4cfbaba`. The branch contains 6 commits and changes 114 files, with 8,236 additions and 4,618 deletions. The working-tree remediation adds explicit failed-flush handling, focused regression coverage, and synchronized repository guidance.

## Technology profile

- Language: C# with nullable reference types, compiled bindings, and warnings-as-errors.
- Runtime: .NET 10 (`net10.0`), with a self-contained `win-x64` Release path.
- UI: Avalonia 12.1.1, Fluent theme, Material Icons, and a layered `Cafe.*` design system.
- Application shape: single-process Windows desktop launcher (`WinExe`).
- Dependency injection: `Microsoft.Extensions.DependencyInjection`, composed in `Composition/ServiceConfiguration.cs`.
- Logging: Serilog with an asynchronous file sink; diagnostics remain local.
- Testing: xUnit v3 unit/contract tests and Avalonia Headless xUnit UI tests.

## Top-level structure

| Area | Responsibility |
| --- | --- |
| `Program.cs`, `App.axaml`, `App.axaml.cs` | Process lifetime, application startup, Avalonia resources, and shutdown wiring. |
| `Composition/` | Dependency-injection composition root and service registration. |
| `Features/` | Vertical feature slices: Shell, GameOperations, Settings, SetupWizard, Diagnostics, and ResourcePanel. |
| `Services/` | HTTP, downloads, settings, installation state, localization, updates, paths, audio, logging, and diagnostics. |
| `ViewModels/` | Shared window projections and cross-feature view models. |
| `Views/`, `Controls/` | Avalonia views, shared controls, and layered styles. |
| `Models/`, `Constants/`, `Helpers/`, `Converters/` | Domain/data contracts, constants, utilities, and binding converters. |
| `Resources/`, `Assets/` | Localized `.resx` resources and packaged runtime assets. |
| `tests/Cafe.Launcher.Avalonia.Tests/` | Unit, persistence, security, localization, and style-contract tests. |
| `tests/Cafe.Launcher.Avalonia.HeadlessTests/` | Headless Avalonia UI behavior tests. |
| `scripts/`, `installer/`, `.github/workflows/` | Validation, localization, coverage, distribution, packaging, and CI automation. |

## Runtime and dependency flow

```text
Program
  -> App / ServiceConfiguration
       -> MainWindowViewModel / ShellLifecycle
            -> feature view models
                 -> settings, HTTP, filesystem, download, update, and diagnostic services
```

`Program` owns the process-level single-instance and session-log boundary. `App` creates the service provider. The Shell coordinates window-level state and shutdown. Feature slices own user journeys, while services own persistence and external effects. URL and filesystem validation helpers define the main remote-content, game-path, download, and update security boundaries.

## Branch hotspots

- `Features/Settings/SettingsViewModel.cs`, `Services/SettingsEditor.cs`, and `ViewModels/WindowChromeViewModel.cs`: immediate settings application, 400 ms debounced autosave, retry state, and close/shutdown flush that preserves the edit session on failure.
- `Features/Shell/ShellLifecycle.cs`: shell refresh, modal routing, runtime theme/language application, settings-saved reactions, and shutdown coordination that only begins after pending settings persist.
- `Views/Styles/Foundation.axaml`, `Theme.axaml`, and `Controls.axaml`: layered design tokens and shared interaction states.
- `Services/Diagnostics/AsyncLogBufferMonitor.cs` and `Services/Diagnostics/UnifiedLogger.cs`: local asynchronous logging and dropped-buffer diagnostics.
- `Models/LauncherSettingsContract.cs` and `Models/SettingOptionDescriptors.cs`: persisted-settings equality and option metadata.

## Build and validation entry points

- `build.ps1`: Debug restore/build with telemetry settings.
- `test.ps1`: both test projects.
- `coverage.ps1`: Coverlet plus 50% minimum and recorded baseline gates.
- `verify.ps1`: Debug build, coverage, and Release build sequence.
- `dev.ps1 ui`: UI style-contract and headless UI validation.
- `scripts/Test-LocalizationContract.ps1`: localized key and placeholder contract.
- `scripts/Build-Distribution.ps1` and `installer/`: publish and NSIS packaging.

## Important repository rules

- Preserve warnings-as-errors, nullable annotations, compiled bindings, and four-locale resource parity.
- Use semantic `Cafe.*` design-system tokens in view XAML and keep shared styles separated from feature layout.
- Keep settings JSON backward-compatible and persistence atomic; settings changes are intended to apply immediately and autosave with retry on failure. Close and application exit must cancel when the pending snapshot cannot be persisted.
- Keep path operations behind existing validators and diagnostics local; do not add remote telemetry.
- Run focused tests after changes, localization checks after resource changes, and the complete verification sequence before release.
