# Codebase Audit

Date: 2026-08-23
Repository: `Cafe.Launcher.Avalonia`
Branch: `codex/design-system-redesign`
Base: `origin/main` (`4cfbaba`)
Head: `73fe10c`
Scope: `origin/main...HEAD` plus the working-tree remediation changes

## Executive summary

The branch review identified three actionable issues. All three have now been addressed: settings close/exit persistence has an explicit success contract, the coverage gate is restored through focused regression tests, and repository guidance no longer describes removed crash-session state. The remaining item is dependency freshness, which is maintenance work rather than a release blocker; the vulnerability scan reported no known vulnerable package.

## Findings and remediation

### P1 — Failed autosave could lose edits when settings were reopened — resolved

`SettingsViewModel.FlushPendingAutoSaveAsync` now returns whether the current editor state is persisted. `WindowChromeViewModel` leaves the settings panel and autosave session open when the flush fails, while `ShellLifecycle` returns `false` before beginning shutdown. `App` cancels the shutdown request in that case, so the user can retry or explicitly discard the edit.

The autosave coordinator also suppresses notifications caused by committing derived palette fields. This prevents a failed save from scheduling an unbounded retry loop.

Regression coverage includes failed settings-panel close and failed application shutdown, as well as successful flush behavior.

### P1 — Coverage baseline regression — resolved

The original branch run was below the recorded 84.43% line baseline. Focused tests now cover the new autosave shutdown paths and additional settings edge paths without lowering the baseline. The latest `coverage.ps1` result is recorded in the validation table below.

### P2 — Repository guidance drift — resolved

`CLAUDE.md`, `README.md`, the design-system specification, and the acceptance plan now describe the current persistence model: settings are applied immediately, saved through debounced atomic autosave, and a failed close/exit flush keeps the edit session available for retry. The dependency version in `README.md` is aligned with the project file.

### P2 — Dependency freshness requires a controlled maintenance pass — retained

`dotnet list .\Cafe.Launcher.Avalonia.csproj package --outdated --include-transitive` still reports available upgrades including NAudio 2.3.0 → 3.0.1, HarfBuzzSharp 8.3.1.3 → 14.2.1.2, SkiaSharp 3.119.4 → 4.151.1, Microsoft.Extensions.Logging.Abstractions 8.0.0 → 10.0.11, and Tmds.DBus.Protocol 0.94.1 → 0.95.0. The vulnerability query reported no vulnerable packages. These upgrades should remain isolated maintenance changes with Windows packaging, audio, and rendering validation.

## Security review

No high-confidence security defect was identified in the branch scope. Existing controls include remote URL/redirect and private-network validation, safe game-path and reparse-point checks, temporary download files with integrity validation, HTTPS update-asset restrictions, pinned GitHub Actions, and local-only diagnostics. No hardcoded credentials were found in the source/configuration scan.

## Validation evidence

| Command | Result |
| --- | --- |
| `dotnet build .\Cafe.Launcher.Avalonia.csproj -c Debug --no-restore` | Passed, 0 warnings, 0 errors. |
| `dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64 --no-restore` | Passed after the target-specific restore, 0 warnings, 0 errors. |
| `.\test.ps1` / coverage test runs | Passed: unit 1,126 passed / 2 skipped; headless 104 passed. |
| `.\coverage.ps1` / `.\verify.ps1` | Passed: handwritten-C# line coverage 84.53% (10,586/12,524); branch coverage 89.62% (1,589/1,773). |
| `.\dev.ps1 ui` | Passed: UI contract 103; headless 104. |
| `.\scripts\Test-LocalizationContract.ps1` | Passed. |
| `.\verify.ps1` | Passed: Debug build, coverage, Release `win-x64` build, and 17 Release tests. |
| `dotnet list ... package --vulnerable --include-transitive` | Passed; no vulnerable packages reported. |
| `git diff --check` | Must remain clean before handoff. |

## Maintainability observations

`Features/Shell/ShellLifecycle.cs` remains a broad coordination boundary. Shutdown, modal routing, settings persistence, and settings-saved refresh behavior should continue to receive focused transition tests rather than being expanded through unrelated orchestration logic.

The audit does not recommend dependency upgrades as part of this remediation. They should be handled in isolated changes with release-targeted validation.
