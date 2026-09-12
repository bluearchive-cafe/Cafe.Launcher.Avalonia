# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 Avalonia desktop launcher. The application project, including entry points (`Program.cs`, `App.axaml`, and `App.axaml.cs`), lives in `src/Cafe.Launcher.Avalonia/`. `Composition/ServiceConfiguration.cs` is the DI composition root. Major behaviour is organised vertically in `Features/` (`Shell`, `GameOperations`, `Settings`, `SetupWizard`, `Diagnostics`, and `ResourcePanel`); shared infrastructure remains in `Services/`, `Helpers/`, `Models/`, `Constants/`, `Controls/`, and `Converters/`. Views and their styles live in `Views/`. Static runtime assets are under `Assets/`; embedded UI resources are in `Resources/`. Unit tests live in `tests/Cafe.Launcher.Avalonia.Tests`; headless UI tests live in `tests/Cafe.Launcher.Avalonia.HeadlessTests`; packaging scripts are in `scripts/` and `installer/`.

ViewModel placement rule: a ViewModel that belongs to one feature lives beside that feature (`Features/*/`, e.g. `Features/Settings/SettingsViewModel`); the root `ViewModels/` folder is reserved for window-level, cross-feature ViewModels (`MainWindowViewModel`, `ShellViewModel`, `DialogsViewModel`, `ModalHostViewModel`, etc.) and the cross-feature modal contracts it hosts (`IModalContentViewModel`, `ModalKind`, `ModalEntry`). Do not place non-ViewModel helper types under `ViewModels/`. Features must not reference each other's concrete types; extract a narrow abstraction into shared `Services/` and have the composition root bind it (see `IGameOperationActivity`).

Shell ruling (2026-09 audit): `Features/Shell` is the window shell layer above the other features, not a peer feature. It intentionally aggregates the concrete presentation ViewModels via `ShellPresentationFamily` and orchestrates them in `ShellLifecycle`/`ShellStartup`; this downward aggregation is the sanctioned exception to the no-cross-feature-references rule. Everything below Shell still holds: non-shell features must not reference each other's concrete types, and shared presentation contracts (such as the modal family) belong in the root `ViewModels/` folder, not inside `Features/Shell`.

Modal isolation ruling (2026-09-12 audit): only the top surface of the modal stack may receive input (the principle in `CONTEXT.md` §模态交互权). The mechanism differs by layer and intentionally has two halves. The seven primary overlays (settings, resource panel, log viewer, log export, debug panel, design gallery, setup wizard) each cross the shell via a per-kind `ModalHostViewModel.Is*Interactive` binding on their overlay root. The dialog layer has **no** such gate on purpose: its surfaces are driven by `DialogsViewModel.Is*Visible` (rendering) rather than by a `ModalKind` property, so a gate would introduce a second source of truth — a dialog shown without being registered in the modal stack would render but be non-interactive, freezing the window instead of degrading. Its input interception is carried by the full-screen scrim plus `ZIndex` ordering (`Grid.dialog-overlay`, `Views/MainWindow.Styles.axaml`). Do not add a dialog-layer interaction gate without first making modal registration the single source of truth for dialog visibility.

## Build, Test, and Development Commands

- `.\build.ps1` — restore and build the Debug configuration with telemetry disabled.
- `dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj` — run the launcher locally.
- `.\test.ps1` — run both xUnit test projects.
- `.\coverage.ps1` — run tests with Coverlet and enforce coverage thresholds.
- `.\verify.ps1` — perform the complete Debug build, coverage, and Release build sequence.
- `.\dev.ps1 ui` — run UI style-contract and headless UI tests after localized UI changes.
- `.\scripts\Test-LocalizationContract.ps1` — verify resource keys and composite-format placeholders across all localized `.resx` files.
- `.\scripts\Build-Distribution.ps1` — publish and package self-contained archives; pass `-Rids win-x64,osx-arm64,linux-x64` for the full cross-platform set (the Windows Inno Setup installer is a separate script).
- `.\scripts\New-WindowsInstaller.ps1` — build the Inno Setup installer from `artifacts/publish/win-x64` (requires Inno Setup 7.0+).
- `.\scripts\New-AppIconAssets.ps1` — regenerate the committed macOS `.icns` and Linux `.png` icon assets after changing `Assets/app-icon-source.jpg`; run on Windows and commit the outputs.
- `dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"` — run one test class.

When changing package versions (including accepting a Dependabot PR), run `dotnet restore` locally and commit the regenerated `packages.lock.json` files together with the version change. CI restores in locked mode, so a PR that updates `Directory.Packages.props` without regenerating the lock files fails with NU1004 — that failure means the locks need regenerating, not that the enforcement is broken. Local `-r`-specific restores (e.g. `verify.ps1`, `Build-Distribution.ps1`) rewrite the RID section of the lock files; restore them with `git restore` before committing.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# uses UTF-8, CRLF, four-space indentation, file-scoped namespaces, braces, and explicit types unless the type is apparent. Other repository text files use LF. `.editorconfig` states these line endings for editors and `.gitattributes` enforces the same ones for git; keep the two in sync, because a file committed with the wrong ending shows every later edit as a whole-file diff. Nullable reference types, compiled bindings, code-style enforcement, and warnings-as-errors are enabled. Use PascalCase for types and public members, camelCase for locals and parameters, and the existing `IService`/`Service` pairing for abstractions. Keep XAML values on the design tokens defined in `App.axaml`; do not introduce raw colors or spacing values in views.

## Localization & Configuration

Add every UI string to the neutral `Resources/LauncherStrings.resx` file and its `zh-Hans`, `zh-Hant`, and `ja` counterparts. Bind UI text through `Shell.I18n[resourceKey]` and use `LocalizationService.T()` / `F()` in C#. In C# source, never pass raw key string literals to `T()`/`F()`/`I18n[...]` — reference the compile-time constants on `Constants/LocalizationKeys` instead. Regenerate `Resources/LauncherStrings.Designer.cs` with `scripts/Generate-LauncherStringsDesigner.ps1` and `Constants/LocalizationKeys.cs` with `scripts/Generate-LocalizationKeys.ps1` after adding or renaming a key, then run `scripts/Test-LocalizationContract.ps1`. Preserve resource-key spelling, casing, and composite-format placeholders. Never infer identifier spelling, casing, paths, or payload structure; inspect the defining code, tests, logs, or captured data first.

## Testing Guidelines

Tests use xUnit v3; UI tests use `Avalonia.Headless.XUnit`. Name tests `Method_State_ExpectedResult`; behavior tests use the full three-segment form, while source/guard contract tests (asserting files, resources, or member structure rather than behavior) may use the two-segment `Subject_Expectation` form. Add focused regression tests for behavior changes and run `UiStyleContractTests` after XAML/style edits. Run `.\scripts\Test-LocalizationContract.ps1` after modifying any `Resources/LauncherStrings*.resx`; run `.\dev.ps1 ui` after XAML or style changes. Before merging or releasing, still run `.\verify.ps1`. `coverage.ps1` enforces the 50% minimum for line and branch coverage and rejects regressions below the repository baseline.

## Release Notes

Treat `CHANGELOG_RELEASE.md` as a single-release document. When preparing notes for a new version, replace its contents with only that version's section and remove every older version section. Before completion, verify that `rg -n "^## v" CHANGELOG_RELEASE.md` returns exactly one heading and that it matches the version being released; `ReleaseChangelogContractTests` guards the heading, the notice blocks, and the vocabulary rule below.

Name the version per SemVer relative to the previous tag. A backward-compatible fix or feature inside an existing prerelease series increments the prerelease number (`1.1.0-beta.7` → `1.1.0-beta.8`), not the major/minor/patch. `release.ps1 <version>` owns the csproj bump, commit, tag, and push.

Write the notes for the person installing the launcher, not for a contributor reading the diff:

- Describe only what the user can see. Audit ledgers, test and coverage work, CI changes, refactors, call sites, and internal architecture vocabulary (modal stack, DI construction, `AppDomain`) belong in commit messages and audit records, never in release notes.
- A feature introduced in this release that is later fixed or reworked before the release ships is described once, in its final form. That defect never reached users, so it does not get its own fix entry.
- Use the shipped UI wording: check the user-facing string in `Resources/LauncherStrings.zh-Hans.resx` before naming a button, label, or status.
- Keep the `> [!NOTE]` focus summary and `> [!WARNING]` stability warning blocks. The referenced banner PNG must be committed under `docs/assets/release-banners/` before tagging; `release.yml` fails the tag build when it is missing. Generate it with the spec-driven `promotional-image` pipeline documented in [docs/promo/release-banner-guide.md](docs/promo/release-banner-guide.md) — copy `docs/promo/specs/release-banner.template.json` per release and commit the spec alongside the banner, so the design stays reproducible rather than living in a one-off script.

## Commit & Pull Request Guidelines

Use Conventional Commits, matching history: `feat(setup): ...`, `fix: ...`, `refactor: ...`, `perf: ...`, or `docs: ...`. Keep each commit focused. Pull requests must explain the change and motivation, link related issues, list verification commands, and include screenshots for visible UI changes. Confirm `verify.ps1` succeeds before requesting review. None of this is mechanically enforced — the `main` ruleset blocks only deletion and force-pushes, so the pull-request and green-CI requirements are conventions rather than gates; see `PROJECT_CONVENTIONS.md` §9 for the enforced rules. When merging Dependabot PRs, squash with a `chore(deps): ...` prefix instead of merging the bot's non-conventional commit — release notes are grouped by conventional prefix, and a bare "Bump ..." commit risks landing in the wrong changelog bucket.
