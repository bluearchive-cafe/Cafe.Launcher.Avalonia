# Codebase Audit

Date: 2026-08-21  
Repository: `Cafe.Launcher.Avalonia`  
Scope: current working tree, including uncommitted and untracked files

## Executive summary

The repository has a mature baseline for a .NET Avalonia launcher: the feature-oriented layout is understandable, external-input boundaries are comparatively defensive, CI actions are pinned, and the application Release build succeeds. The current checkout is not release-ready, however. The official test entry point cannot compile because an xUnit analyzer warning is promoted to an error, and an analyzer-disabled unit run exposed a process-wide localization culture leak that makes the suite order-dependent.

The working tree is also in the middle of a broad design-system/UI change. That matters when interpreting the results: the blocking style-test issue is in the current uncommitted test changes, and the audit should not be read as a clean-branch assessment.

## Remediation follow-up — 2026-08-22

The two P1 unit-test findings below have been fixed in the working tree:

- The xUnit2031 assertion now uses the predicate overload of `Assert.Single`.
- The unit test assembly runs without parallelization, and localization-aware test classes restore current and default culture state after each test instance through `LocalizationTestBase`.

Validation after the changes: the full unit project passed with 1,108 tests passed, 2 skipped, and 0 failed. The Release `win-x64` build and localization contract also passed. The headless project still has an independent hang in `SetupWizard_WhenLanguageChanges_LocalizesStatusLineAndKeepsNavigationAccessible`; it was isolated with the hang diagnostic runner and remains outside the two fixes above.

## Findings at a glance

| Priority | Area | Finding | Confidence |
| --- | --- | --- | ---: |
| P1 | Testing / build gate | The official test suite does not compile under the repository's warnings-as-errors policy. | 100% |
| P1 | Testing / isolation | Localization tests mutate process-wide culture state and leave the suite order/environment-dependent. | 98% |
| P2 | Architecture / maintainability | Shell lifecycle orchestration remains a broad coordination hotspot with a high collaborator count. | 88% |
| P2 | Dependencies | Several direct and transitive packages have available upgrades; compatibility work is required before applying them. | 95% |

## Findings

### P1 — Test build is blocked by xUnit2031

**Evidence**

- `.	est.ps1` exited with code 1 before executing the complete test suite.
- The compiler reported `xUnit2031` at `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs:1251`.
- The offending assertion filters with LINQ before calling `Assert.Single`, while the project enables `TreatWarningsAsErrors=true` in `Cafe.Launcher.Avalonia.csproj`.

**Impact**

The repository's normal test, coverage, and verification workflows cannot establish a clean test result. CI will fail at the test-project build until the assertion is rewritten or the analyzer finding is otherwise addressed.

**Recommendation**

Rewrite the assertion using the predicate overload of `Assert.Single` (or assert the filtered sequence with an analyzer-compliant pattern), then rerun `.	est.ps1`, `.coverage.ps1`, and the UI validation command. Keep the warnings-as-errors policy; it is correctly exposing a test-quality issue rather than hiding it.

### P1 — Localization state leaks across tests

**Evidence**

- `Services/LocalizationService.cs:116-129` applies explicit language changes, and `Services/LocalizationService.cs:192-198` writes `CurrentCulture`, `CurrentUICulture`, `DefaultThreadCurrentCulture`, and `DefaultThreadCurrentUICulture`.
- The unit test project has no assembly-level xUnit setting that disables parallelization or otherwise provides a culture fixture. The headless test project has a separate `AssemblyInfo.cs`, but that does not isolate the unit project.
- With analyzers disabled and `UiStyleContractTests` excluded so the tests could build, the unit run reported 1,018 tests: 1,015 passed, 2 skipped, and 1 failed.
- The failure was `MainWindowViewModelTests.InitializeAsync_ForEveryRuntimeState_ResolvesCompleteLocalizedStatusPresentation` at `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs:480`: the test expected English `Update required before launch.` but observed Japanese `起動前に更新が必要です。`.

**Impact**

Tests that change language can affect unrelated tests through process-wide and default-thread culture. Outcomes can vary with test order, parallel scheduling, or the culture left by a prior test, reducing confidence in local and CI results. The same design also makes production code harder to reason about when multiple asynchronous operations are active.

**Recommendation**

Make culture changes scoped and restorable in tests, preferably through a shared fixture/helper that snapshots and restores all affected culture values in `finally`. Isolate tests that intentionally exercise global localization state. Disabling test parallelization can be a short-term containment measure, but it should not replace restoration of process-wide state or a longer-term design that makes localization state explicit at the service boundary.

### P2 — Shell lifecycle remains a coordination hotspot

**Evidence**

- `Features/Shell/ShellLifecycle.cs` is approximately 918 lines and owns startup, refresh, modal, settings, update, error, and shutdown transitions.
- `Features/Shell/ShellRuntime.cs:26-46` accepts more than 20 collaborators, including feature view models, settings, localization, update, diagnostics, and window services.
- Other large orchestration surfaces include `Features/Settings/SettingsViewModel.cs` (approximately 659 lines) and `ViewModels/RemoteContentViewModel.cs` (approximately 636 lines).

**Impact**

The code is still organized behind a coherent Shell boundary, but changes to one lifecycle concern carry a larger regression surface and require broad test setup. This increases navigation and maintenance cost as more launcher states are added.

**Recommendation**

Keep the Shell as the public composition boundary, but incrementally extract cohesive internal coordinators for startup/refresh, modal routing, and shutdown/error handling. Move one concern at a time with transition-focused tests; avoid a speculative rewrite or a new cross-feature abstraction layer.

### P2 — Dependency freshness requires scheduled compatibility work

**Evidence**

`dotnet list .\Cafe.Launcher.Avalonia.csproj package --outdated --include-transitive` reported upgrades including:

- `Microsoft.Extensions.DependencyInjection` 10.0.10 → 10.0.11.
- `NAudio` and `NAudio.Vorbis.Latest` 2.3.0 → 3.0.1.
- Transitive `SkiaSharp` 3.119.4 → 4.151.1 and related native assets.
- Transitive HarfBuzzSharp/platform assets 8.3.1.3 → 14.2.1.2.
- `Tmds.DBus.Protocol` 0.94.1 → 0.94.2.

`dotnet list .\Cafe.Launcher.Avalonia.csproj package --vulnerable --include-transitive` reported no known vulnerable packages from the configured feeds.

**Impact**

The project is not immediately exposed by a known vulnerability in the audited dependency graph, but several available upgrades are potentially breaking, especially the audio and graphics stacks. Delaying all updates increases future upgrade size and reduces the time available to validate native runtime and packaging behavior.

**Recommendation**

Schedule upgrades in a controlled maintenance change. Start with patch-level Microsoft.Extensions and Tmds updates, then validate NAudio and SkiaSharp changes independently with Debug tests, Release `win-x64` build/publish, audio playback, and installer smoke tests. Consider introducing a lock file or centrally managed package versions if reproducibility becomes a concern.

## Security review

No high-confidence security defect was identified in the inspected code. Notable controls include:

- Remote HTTP validation restricts schemes, rejects userinfo and local/private destinations, validates redirects, caps redirect depth, and rejects HTTPS-to-HTTP downgrade.
- Downloaded content is written through temporary/resumable paths and validated with the expected CRC64 before replacement.
- Game path validation normalizes paths, prevents escaping the selected root, and checks existing path components for reparse points.
- Update assets are constrained to HTTPS GitHub release paths before being exposed to the user.
- GitHub Actions use full commit-SHA pins, and the vulnerability scan found no vulnerable packages.
- A repository scan found no hardcoded credentials or tokens; workflow secret references are configuration references rather than embedded secrets.

These are audit observations, not a substitute for a threat model or a dependency scanner with a separate vulnerability database.

## Testing and quality assessment

The repository contains two test projects, a substantial unit/contract suite, headless UI coverage, localization contract validation, and a coverage gate. The current working tree produced the following evidence:

| Command | Result |
| --- | --- |
| `.\test.ps1` | Failed during test-project compilation on xUnit2031 at `UiStyleContractTests.cs:1251`. |
| `dotnet build .\Cafe.Launcher.Avalonia.csproj -c Release -r win-x64` | Passed. |
| `.\scripts\Test-LocalizationContract.ps1` | Passed. |
| `dotnet list ... package --vulnerable --include-transitive` | Passed; no vulnerable packages reported. |
| Analyzer-disabled unit run excluding `UiStyleContractTests` | 1,015 passed, 2 skipped, 1 failed out of 1,018; the failure was the culture leak described above. |
| Current headless UI run | Did not produce a result summary after roughly five minutes of spinner-only progress and was terminated as a bounded diagnostic run. It is not counted as pass or fail. |

Historical `TestResults/Coverage` artifacts were present, but they were dated 2026-08-12 and were not treated as current coverage evidence. Current coverage could not be established while the test project fails to compile.

## Performance assessment

No high-confidence production performance defect was confirmed within the audit scope. Positive patterns include concurrent remote snapshot loading in the launcher core, bounded download behavior, and asynchronous file logging. The headless test run's lack of a result within five minutes is worth investigating separately, but the available evidence does not distinguish a slow test suite from an environment or test-runner hang, so it is not scored as a production performance finding.

## Prioritized remediation roadmap

1. Fix the xUnit2031 assertion in the current style-contract test change and restore the normal test build.
2. Make localization culture changes test-scoped and restore all process/default culture state; then rerun the full unit suite in both parallel and repeated-order configurations.
3. Run `.\test.ps1`, `.\coverage.ps1`, and `.\verify.ps1` from a clean or intentionally staged checkout; investigate the headless timeout independently.
4. Schedule dependency upgrades, beginning with patch-level changes and then validating NAudio/SkiaSharp/native packaging upgrades.
5. Extract Shell lifecycle coordinators incrementally if the feature continues to grow, keeping the existing feature-facing boundary and transition tests.

## Audit limitations

- The checkout was not clean, so findings may belong to the in-progress design-system/UI work rather than the last committed revision.
- No source changes were made to fix findings; this audit adds only the audit artifacts.
- Full current coverage and a complete headless result were unavailable because the test build was blocked and the bounded headless run did not terminate with a summary.
