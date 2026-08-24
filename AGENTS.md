# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 Avalonia desktop launcher. The application project, including entry points (`Program.cs`, `App.axaml`, and `App.axaml.cs`), lives in `src/Cafe.Launcher.Avalonia/`. `Composition/ServiceConfiguration.cs` is the DI composition root. Major behaviour is organised vertically in `Features/` (`Shell`, `GameOperations`, `Settings`, `SetupWizard`, `Diagnostics`, and `ResourcePanel`); shared infrastructure remains in `Services/`, `Helpers/`, `Models/`, `Constants/`, `Controls/`, and `Converters/`. Views and their styles live in `Views/`. Static runtime assets are under `Assets/`; embedded UI resources are in `Resources/`. Unit tests live in `tests/Cafe.Launcher.Avalonia.Tests`; headless UI tests live in `tests/Cafe.Launcher.Avalonia.HeadlessTests`; packaging scripts are in `scripts/` and `installer/`.

## Build, Test, and Development Commands

- `.\build.ps1` — restore and build the Debug configuration with telemetry disabled.
- `dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj` — run the launcher locally.
- `.\test.ps1` — run both xUnit test projects.
- `.\coverage.ps1` — run tests with Coverlet and enforce coverage thresholds.
- `.\verify.ps1` — perform the complete Debug build, coverage, and Release build sequence.
- `.\dev.ps1 ui` — run UI style-contract and headless UI tests after localized UI changes.
- `.\scripts\Test-LocalizationContract.ps1` — verify resource keys and composite-format placeholders across all localized `.resx` files.
- `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"` — run one test class.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# uses UTF-8, CRLF, four-space indentation, file-scoped namespaces, braces, and explicit types unless the type is apparent. Other repository text files use LF. Nullable reference types, compiled bindings, code-style enforcement, and warnings-as-errors are enabled. Use PascalCase for types and public members, camelCase for locals and parameters, and the existing `IService`/`Service` pairing for abstractions. Keep XAML values on the design tokens defined in `App.axaml`; do not introduce raw colors or spacing values in views.

## Localization & Configuration

Add every UI string to the neutral `Resources/LauncherStrings.resx` file and its `zh-Hans`, `zh-Hant`, and `ja` counterparts. Bind UI text through `Shell.I18n[resourceKey]` and use `LocalizationService.T()` / `F()` in C#. Regenerate `Resources/LauncherStrings.Designer.cs` with `scripts/Generate-LauncherStringsDesigner.ps1` after adding or renaming a key, then run `scripts/Test-LocalizationContract.ps1`. Preserve resource-key spelling, casing, and composite-format placeholders. Never infer identifier spelling, casing, paths, or payload structure; inspect the defining code, tests, logs, or captured data first.

## Testing Guidelines

Tests use xUnit v3; UI tests use `Avalonia.Headless.XUnit`. Name tests `Method_State_ExpectedResult`. Add focused regression tests for behavior changes and run `UiStyleContractTests` after XAML/style edits. Run `.\scripts\Test-LocalizationContract.ps1` after modifying any `Resources/LauncherStrings*.resx`; run `.\dev.ps1 ui` after XAML or style changes. Before merging or releasing, still run `.\verify.ps1`. `coverage.ps1` enforces the 50% minimum for line and branch coverage and rejects regressions below the repository baseline.

## Commit & Pull Request Guidelines

Use Conventional Commits, matching history: `feat(setup): ...`, `fix: ...`, `refactor: ...`, `perf: ...`, or `docs: ...`. Keep each commit focused. Pull requests must explain the change and motivation, link related issues, list verification commands, and include screenshots for visible UI changes. Confirm `verify.ps1` succeeds before requesting review.
