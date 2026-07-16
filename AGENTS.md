# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 desktop launcher built with Avalonia. Application entry points (`Program.cs`, `App.axaml`, and `App.axaml.cs`) live at the repository root. Domain data belongs in `Models/`; shared values in `Constants/`; application logic and integrations in `Services/`; presentation state in `ViewModels/`; and UI markup/code-behind in `Views/` and `Controls/`. Static images, audio, icons, and locale JSON files are under `Assets/`. Unit tests live in `tests/Cafe.Launcher.Avalonia.Tests`; headless UI tests live in `tests/Cafe.Launcher.Avalonia.HeadlessTests`. Packaging scripts are in `scripts/` and `installer/`.

## Build, Test, and Development Commands

- `.\build.ps1` — restore and build the Debug configuration with telemetry disabled.
- `dotnet run --project .\Cafe.Launcher.Avalonia.csproj` — run the launcher locally.
- `.\test.ps1` — run both xUnit test projects.
- `.\coverage.ps1` — run tests with Coverlet and enforce coverage thresholds.
- `.\verify.ps1` — perform the complete Debug build, coverage, and Release build sequence.
- `.\dev.ps1 ui` — run UI style-contract and headless UI tests after localized UI changes.
- `.\scripts\Test-LocalizationContract.ps1` — verify keys and composite-format placeholders across all locale JSON files.
- `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"` — run one test class.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# uses UTF-8, CRLF, four-space indentation, file-scoped namespaces, braces, and explicit types unless the type is apparent. Other repository text files use LF. Nullable reference types, compiled bindings, code-style enforcement, and warnings-as-errors are enabled. Use PascalCase for types and public members, camelCase for locals and parameters, and the existing `IService`/`Service` pairing for abstractions. Keep XAML values on the design tokens defined in `App.axaml`; do not introduce raw colors or spacing values in views.

## Localization & Configuration

Add every UI string to all four files in `Assets/Locales/` and wire the exact key through `LocalizedStrings`. Preserve documented JSON keys and wire-contract property order. Never infer identifier spelling, casing, paths, or payload structure; inspect the defining code, tests, logs, or captured data first.

## Testing Guidelines

Tests use xUnit v3; UI tests use `Avalonia.Headless.XUnit`. Name tests `Method_State_ExpectedResult`. Add focused regression tests for behavior changes and run `UiStyleContractTests` after XAML/style edits. Run `.\scripts\Test-LocalizationContract.ps1` after modifying any `Assets/Locales/*.json`; run `.\dev.ps1 ui` after XAML or style changes. Before merging or releasing, still run `.\verify.ps1`. Line and branch coverage must each remain at or above 50%.

## Commit & Pull Request Guidelines

Use Conventional Commits, matching history: `feat(setup): ...`, `fix: ...`, `refactor: ...`, `perf: ...`, or `docs: ...`. Keep each commit focused. Pull requests must explain the change and motivation, link related issues, list verification commands, and include screenshots for visible UI changes. Confirm `verify.ps1` succeeds before requesting review.
