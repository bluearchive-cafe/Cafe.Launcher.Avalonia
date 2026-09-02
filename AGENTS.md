# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 Avalonia desktop launcher. The application project, including entry points (`Program.cs`, `App.axaml`, and `App.axaml.cs`), lives in `src/Cafe.Launcher.Avalonia/`. `Composition/ServiceConfiguration.cs` is the DI composition root. Major behaviour is organised vertically in `Features/` (`Shell`, `GameOperations`, `Settings`, `SetupWizard`, `Diagnostics`, and `ResourcePanel`); shared infrastructure remains in `Services/`, `Helpers/`, `Models/`, `Constants/`, `Controls/`, and `Converters/`. Views and their styles live in `Views/`. Static runtime assets are under `Assets/`; embedded UI resources are in `Resources/`. Unit tests live in `tests/Cafe.Launcher.Avalonia.Tests`; headless UI tests live in `tests/Cafe.Launcher.Avalonia.HeadlessTests`; packaging scripts are in `scripts/` and `installer/`.

ViewModel placement rule: a ViewModel that belongs to one feature lives beside that feature (`Features/*/`, e.g. `Features/Settings/SettingsViewModel`); the root `ViewModels/` folder is reserved for window-level, cross-feature ViewModels (`MainWindowViewModel`, `ShellViewModel`, `DialogsViewModel`, etc.). Do not place non-ViewModel helper types under `ViewModels/`. Features must not reference each other's concrete types; extract a narrow abstraction into shared `Services/` and have the composition root bind it (see `IGameOperationActivity`).

## Build, Test, and Development Commands

- `.\build.ps1` — restore and build the Debug configuration with telemetry disabled.
- `dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj` — run the launcher locally.
- `.\test.ps1` — run both xUnit test projects.
- `.\coverage.ps1` — run tests with Coverlet and enforce coverage thresholds.
- `.\verify.ps1` — perform the complete Debug build, coverage, and Release build sequence.
- `.\dev.ps1 ui` — run UI style-contract and headless UI tests after localized UI changes.
- `.\scripts\Test-LocalizationContract.ps1` — verify resource keys and composite-format placeholders across all localized `.resx` files.
- `.\scripts\Build-Distribution.ps1` — publish and package self-contained archives; pass `-Rids win-x64,osx-arm64,linux-x64` for the full cross-platform set (the Windows Inno Setup installer is a separate script).
- `.\scripts\New-WindowsInstaller.ps1` — build the Inno Setup installer from `artifacts/publish/win-x64` (requires Inno Setup 6.3+).
- `.\scripts\New-AppIconAssets.ps1` — regenerate the committed macOS `.icns` and Linux `.png` icon assets after changing `Assets/app-icon-source.jpg`; run on Windows and commit the outputs.
- `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"` — run one test class.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# uses UTF-8, CRLF, four-space indentation, file-scoped namespaces, braces, and explicit types unless the type is apparent. Other repository text files use LF. Nullable reference types, compiled bindings, code-style enforcement, and warnings-as-errors are enabled. Use PascalCase for types and public members, camelCase for locals and parameters, and the existing `IService`/`Service` pairing for abstractions. Keep XAML values on the design tokens defined in `App.axaml`; do not introduce raw colors or spacing values in views.

## Localization & Configuration

Add every UI string to the neutral `Resources/LauncherStrings.resx` file and its `zh-Hans`, `zh-Hant`, and `ja` counterparts. Bind UI text through `Shell.I18n[resourceKey]` and use `LocalizationService.T()` / `F()` in C#. Regenerate `Resources/LauncherStrings.Designer.cs` with `scripts/Generate-LauncherStringsDesigner.ps1` after adding or renaming a key, then run `scripts/Test-LocalizationContract.ps1`. Preserve resource-key spelling, casing, and composite-format placeholders. Never infer identifier spelling, casing, paths, or payload structure; inspect the defining code, tests, logs, or captured data first.

## Testing Guidelines

Tests use xUnit v3; UI tests use `Avalonia.Headless.XUnit`. Name tests `Method_State_ExpectedResult`. Add focused regression tests for behavior changes and run `UiStyleContractTests` after XAML/style edits. Run `.\scripts\Test-LocalizationContract.ps1` after modifying any `Resources/LauncherStrings*.resx`; run `.\dev.ps1 ui` after XAML or style changes. Before merging or releasing, still run `.\verify.ps1`. `coverage.ps1` enforces the 50% minimum for line and branch coverage and rejects regressions below the repository baseline.

## Release Notes

Treat `CHANGELOG_RELEASE.md` as a single-release document. When preparing notes for a new version, replace its contents with only that version's section and remove every older version section. Before completion, verify that `rg -n "^## v" CHANGELOG_RELEASE.md` returns exactly one heading and that it matches the version being released.

## Commit & Pull Request Guidelines

Use Conventional Commits, matching history: `feat(setup): ...`, `fix: ...`, `refactor: ...`, `perf: ...`, or `docs: ...`. Keep each commit focused. Pull requests must explain the change and motivation, link related issues, list verification commands, and include screenshots for visible UI changes. Confirm `verify.ps1` succeeds before requesting review.
