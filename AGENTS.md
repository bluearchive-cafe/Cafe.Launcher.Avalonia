# Repository Guidelines

Authoritative workflow for AI coding agents and human contributors working in this repository. `PROJECT_CONVENTIONS.md` holds the detailed coding rules; this file holds the structure, commands, and contracts that keep changes reviewable. When architecture or developer workflow changes, update this file in the same change.

## Project Structure & Module Organization

This is a .NET 10 Avalonia desktop launcher for Blue Archive (JP). The solution (`Cafe.Launcher.Avalonia.slnx`) contains the application project `src/Cafe.Launcher.Avalonia/` (entry points: `Program.cs`, `App.axaml`, `App.axaml.cs`) and two test projects under `tests/`.

- `Composition/ServiceConfiguration.cs` — the DI composition root. Every DI-managed service and view model is registered here, all as singletons (single-window desktop app).
- `Features/` — major behavior, organized vertically: `Shell`, `GameOperations`, `Settings`, `SetupWizard`, `Diagnostics`, `ResourcePanel`.
- `Services/`, `Helpers/`, `Models/`, `Constants/`, `Controls/`, `Converters/` — shared infrastructure.
- `Views/` — Avalonia views; large style/overlay blocks live in separate `.axaml` files (`MainWindow.Styles.axaml` and per-overlay files). `MainWindow.axaml` keeps only the window shell and content grid.
- `Resources/` — localized `.resx` strings. `Assets/` — static runtime assets.
- `tests/Cafe.Launcher.Avalonia.Tests/` — xUnit v3 unit tests. `tests/Cafe.Launcher.Avalonia.HeadlessTests/` — Avalonia Headless UI tests, including golden-screenshot baselines.
- `tests/Support/` — test facilities both suites use (temp directories, repository/resource lookup, bounded async waiting), compiled into each test assembly through the same `Compile-Link` mechanism as `tests/TestDoubles/`. A facility belongs here only when both projects need it and it carries no project-specific knowledge.
- `scripts/` and `installer/` — packaging scripts and the Windows installer.
- `prototypes/` — throwaway design prototypes; not part of the app or release.

### ViewModel placement rule

A ViewModel that belongs to one feature lives beside that feature (`Features/*/`). The root `ViewModels/` folder is reserved for window-level, cross-feature ViewModels (`MainWindowViewModel`, `ShellViewModel`, `DialogsViewModel`, `ModalHostViewModel`, …) and the cross-feature modal contracts it hosts (`IModalContentViewModel`, `ModalKind`, `ModalEntry`). Do not place non-ViewModel helper types under `ViewModels/`.

Features must not reference each other's concrete types. Extract a narrow abstraction into shared `Services/` and bind it in the composition root (existing example: `IGameOperationActivity`).

### Shell ruling (2026-09 audit)

`Features/Shell` is the window shell layer above the other features, not a peer feature. It intentionally aggregates the concrete presentation ViewModels via `ShellPresentationFamily` and orchestrates them in `ShellLifecycle`/`ShellStartup`; this downward aggregation is the sanctioned exception to the no-cross-feature-references rule. Everything below Shell still holds: non-shell features must not reference each other's concrete types, and shared presentation contracts (such as the modal family) belong in the root `ViewModels/` folder, not inside `Features/Shell`.

The native tray consumes `ISystemTrayActions`, implemented by `Features/Shell/SystemTrayActions`: it reuses the shell launch/settings commands and observes operation, session, and modal state. Native tray adapters own menu rendering only; command availability is rechecked at invocation, and settings opens without toggling an existing draft closed.

### Modal isolation ruling (2026-09-12 audit)

Only the top surface of the modal stack may receive input (the principle in `CONTEXT.md` §模态交互权). The mechanism differs by layer and intentionally has two halves:

- The seven primary overlays (settings, resource panel, log viewer, log export, debug panel, design gallery, setup wizard) each cross the shell via a per-kind `ModalHostViewModel.Is*Interactive` binding on their overlay root.
- The dialog layer has **no** such gate on purpose. Its surfaces are driven by visibility flags (rendering) rather than by a `ModalKind` property — the confirmation family via its `ConfirmationDialogViewModel.IsVisible` instances and the remaining dialogs via `DialogsViewModel.Is*Visible` — so a gate would introduce a second source of truth — a dialog shown without being registered in the modal stack would render but be non-interactive, freezing the window instead of degrading. Its input interception is carried by the full-screen scrim plus `ZIndex` ordering (`Grid.dialog-overlay`, `Views/MainWindow.Styles.axaml`).

Do not add a dialog-layer interaction gate without first making modal registration the single source of truth for dialog visibility.

## Build, Test, and Development Commands

Requires the .NET SDK pinned by `global.json` (`10.0.302`, rolling forward within the same feature band). Repository scripts disable .NET CLI and Avalonia telemetry. Builds enforce nullable reference types, compiled bindings, code style, and warnings-as-errors; a successful build has zero warnings.

Repository commands run under either Bash or PowerShell 7 (`pwsh`). Windows PowerShell 5.1 (`powershell.exe`) is not supported: the scripts use `??`, which its parser rejects, and it decodes their Chinese diagnostics with the ANSI code page (`coverage.ps1` carries a UTF-8 BOM carve-out in `.editorconfig` for that reason). CI reflects the same split — `shell: pwsh` and `shell: bash` only.

| Command | Purpose |
| --- | --- |
| `.\build.ps1` | Restore and build the Debug configuration |
| `dotnet run --project .\src\Cafe.Launcher.Avalonia\Cafe.Launcher.Avalonia.csproj` | Run the launcher locally |
| `.\test.ps1` | Run both xUnit test projects |
| `.\test.ps1 -Suite Headless` | Run one suite only (`All`/`Unit`/`Headless`) |
| `.\test.ps1 -Filter "FullyQualifiedName~VersionComparerTests"` | Run a filtered subset (`dotnet test --filter` expression) |
| `.\test.ps1 -Configuration Release` | Run both projects in Release (what the release workflow runs) |
| `.\coverage.ps1` | Run tests with Coverlet; enforces the 50% line/branch minimum and rejects regressions below the repository baseline (baseline values live in `coverage.ps1`, which prints current slack) |
| `.\verify.ps1` | Full sequence: Debug build, coverage, Release build |
| `.\dev.ps1 ui` | Run UI style-contract and headless UI tests after localized UI changes |
| `.\scripts\Test-LocalizationContract.ps1` | Verify resource keys and composite-format placeholders across all localized `.resx` files |
| `.\scripts\Build-Distribution.ps1` | Publish and package self-contained archives; pass `-Rids win-x64,osx-arm64,linux-x64` for the full set |
| `.\scripts\New-WindowsInstaller.ps1` | Build the Inno Setup installer from `artifacts/publish/win-x64` (requires Inno Setup 7.0+) |
| `.\scripts\New-AppIconAssets.ps1` | Regenerate committed macOS `.icns` and Linux `.png` icon assets after changing `Assets/app-icon-source.png` |

Run one test class:

```powershell
dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~VersionComparerTests"
```

Restore before commands that use `--no-restore`. CI restores in locked mode (`RestoreLockedMode`), so dependency work has its own procedure below.

### Dependency upgrades

When changing package versions (including accepting a Dependabot PR):

1. Run `dotnet restore` locally and commit the regenerated `packages.lock.json` files together with the version change. A PR that updates `Directory.Packages.props` without regenerating the locks fails CI with NU1004 — that failure means the locks need regenerating, not that the enforcement is broken.
2. Local RID-specific restores (`verify.ps1`, `Build-Distribution.ps1`) rewrite the RID section of the lock files; restore them with `git restore` before committing.
3. Update the §12 toolchain table in `PROJECT_CONVENTIONS.md` by hand — `InstallerContractTests.ProjectConventionsToolchainTable_MatchesDeclaredPackageVersions` fails while any row disagrees with `Directory.Packages.props`.
4. Regenerate `THIRD-PARTY-NOTICES.md` with `.\scripts\New-ThirdPartyNotices.ps1`; no test guards its versions, so a stale entry drifts silently until the next audit. The script reads the resolved dependency graph, so it needs a prior `dotnet restore` in the same working tree.

## Application Architecture

- `Program.cs` owns process lifetime: the single-instance gate (named mutex on Windows; on Unix a kernel-atomic socket lock in the data root, because .NET's `Local\` namespace is per-POSIX-session there and never dedups across launch contexts — [ADR-034](docs/design/adr/ADR-034-Unix单实例所有权用数据根内锁套接字判定.md)), the cross-platform second-instance forwarding signals (`--launch-game` and show-window; named events on Windows, local Unix-domain sockets elsewhere), the logger created before DI, crash handlers, first-launch detection, and session start/end logging. The pre-DI `UnifiedLogger` is passed into DI so the process has one Serilog pipeline, and is disposed only after session-end logging.
- `App.axaml.cs` builds the `ServiceCollection`, calls `Composition.ServiceConfiguration.AddLauncherServices(existingLogger:)`, constructs the single `MainWindow`, and either shows the first-launch setup wizard or starts normal asynchronous initialization after the window opens. Heavy work stays off the first-frame path, with one deliberate exception: the bundled wallpaper is decoded synchronously in `BackgroundViewModel`'s constructor (before the window shows) so the first frame shows the wallpaper instead of the bare theme base color. Do not "fix" this into an async load without deciding where the first frame's wallpaper comes from.
- All DI services are singletons. Microsoft DI disposes created services in reverse registration order; when adding an `IDisposable` service, register it after the services that must release first. `Program.RunSession` explicitly disposes the shared pre-DI logger last.
- Avalonia uses compiled, explicit bindings; there is no reflection-based view locator. `ViewModelBase` extends CommunityToolkit.Mvvm's `ObservableObject`. Overlay stacking order is base content → settings (100) → dialogs (200) → setup wizard (500) → toast (1000): the toast layer's value is `LauncherConstants.ZIndexToast`; the settings, dialogs, and setup-wizard layers are pinned by style setters in `Views/MainWindow.Styles.axaml`, guarded by `UiStyleContractTests`.
- Stop intent is domain vocabulary: callers say *why* a workflow stops (`GameOperationStopIntent` in `Services/`), and the game-operations feature translates that once into its own checkpoint-retention policy (`DownloadStopReason`, which lives inside `Features/GameOperations/` and is not named elsewhere — `GameOperationStopOwnershipTests` guards both halves; see [ADR-026](docs/design/adr/ADR-026-停止意图由游戏操作域翻译.md)). Closing the window while downloading is a user stop (the checkpoint is discarded, matching the confirmation text the user saw), not a process exit.
- `GameOperationPolicy` answers permission only, and it answers with `GameOperationDecision` rather than a boolean, so every call site must state what a rejection means (message, failure result, or abort); a rejection after the user has already confirmed a destructive action must be reported, never silently skipped (`GameOperationPolicyTests.Decide_IsTheOnlyPublicVerdict_NoBareBoolean` keeps the boolean off the public surface, and the two `…_ReportsInsteadOfSilence` tests pin the post-confirmation paths — see [ADR-027](docs/design/adr/ADR-027-策略判定结果与确认后拒绝反馈.md)). The pre-check is a second gate with the same rule: `GameOperationJourney.ValidateUninstallAsync` reports the execution layer's own reason and returns `null`, which callers read as "already reported", never as "nothing happened" ([ADR-029](docs/design/adr/ADR-029-卸载预检失败不再静默.md)).
- `Features/GameOperations` separates command presentation (`GameOperationsViewModel`) from journey orchestration (`GameOperationJourney`, `GameOperationExecutor`) and the workflows for launch, install, update, repair, and uninstall. Downloads use remote-manifest diffs, up to 10 concurrent transfers, `.tmp` staging, Range resume, CRC64 verification, persisted session state, and pause/resume. Every file-touching workflow is gated on "the game is not running", and that question is answered by process *name family*, not by the configured host alone: `Services/GameRuntime/GameProcessNames` derives the family from the config the game ships (`name` + executable `params`), so the loader's anti-cheat sibling — which outlives the host and is what actually holds the install directory — is recognised too. Names are the only usable signal, because the anti-cheat protects the game processes' image paths ([ADR-032](docs/design/adr/ADR-032-游戏进程按名字家族识别.md)). That gate runs twice on the destructive paths: uninstall re-checks it at its execution boundary, and download/install/repair re-checks it at its own write boundary — just before files in the install directory are replaced or its two state files are rewritten (the download itself only writes `.tmp`, and the zero-diff path that commits state without downloading is re-checked too) — because a confirmation dialog or a multi-minute download can outlive the pre-check. When no local config exists yet (a fresh install), the family falls back to the same two fields the remote config declares (start executable name plus its start params), so both paths derive the same criterion. `GameProcessNames.DescribeForDisplay` is the single place that formats the running processes for the user. Uninstall takes an `UninstallScope`: the default deletes the manifest-listed files, both state files, and the desktop shortcut, while `ThoroughCleanup` also removes the whole install directory and the launcher-managed `compatibility/<gameId>` subtree — a user-configured Prefix is kept and said so when it sits outside both deletion targets, because recursively deleting a user-chosen directory is a different authorization question. Recursive deletion goes through `Helpers/DirectoryTreeDeleter` (it walks the tree itself: `Directory.Delete(recursive: true)` fails on a junction inside the tree, and links must be removed as links), and `Helpers/DirectorySizeProbe` measures the same targets for the dialog — the dialog opens first and the sizes fill in when the walk returns, because walking an install directory takes seconds. That deleter returns the entries it could not remove instead of throwing, because some entries are undeletable from user mode (the game's anti-cheat leaves `Xigncode:{GUID}` items under `StreamingAssets`, which Win32 reads as a data stream): the uninstall still completes, and names them instead of reporting a failure ([ADR-030](docs/design/adr/ADR-030-卸载可彻底清除.md)). A successful launch report only covers the spawn: `GameSessionMonitor` then watches the session with the same family scan — a runner host that dies before the game's process family ever appears restores the window and reports the failure with its exit code, while the control panel shows a session state line (starting / running / exited / failed) that is hidden when no session exists. The name resolution behind the gate (`RunningGameGate.ResolveKnownProcessNames`) is shared by the destructive paths and the launch path, so "is the game running" never forks ([ADR-035](docs/design/adr/ADR-035-游戏会话状态可见-启动后看护与状态行.md)).
- `Services/RemoteHttpTransport` is the single outbound module for remote JSON/stream fetches: proxy-aware leasing, SSRF validation with per-URI egress resolution, manual redirects, status enforcement, buffering, stall budget, and retries live behind its two-method interface. Remote clients (API, update, resource panel, image cache) own only their API vocabulary and receive the transport via DI; the proxy mode resolves from the settings editor's saved snapshot unless a call overrides `RemoteRequestOptions.ProxyMode`. Retry discipline is declared per call via `RemoteRetryScope`.
- `Services/HttpClientFactory` owns shared pooled handlers and hands out proxy-aware leases; do not create ad-hoc `HttpClient` instances or long-lived handlers elsewhere. Client preferences are *pulled*: the composition root injects a `Func<bool>` reading the saved settings snapshot (HTTP/2) and a snapshot-reading closure (proxy mode), so neither the factory nor the remote transport takes preference pushes — a saved setting takes effect on the next lease with no call site having to remember it ([ADR-028](docs/design/adr/ADR-028-客户端偏好按使用时机拉取.md)). The game-file download channel consumes them through its own batch seam (`IDownloadTransportSource` → `IDownloadTransport`: one transport per download batch wrapping one long-timeout lease). `IFileDownloadService` owns the .tmp staging state machine — resume, completeness, and oversize decisions live there, expressed through explicit `DownloadOutcome` results; callers never stat temporary files except via `GetExistingDownloadedSize` for progress seeding.

### Persistence and compatibility contracts

- Launcher data lives in `%LOCALAPPDATA%\Cafe Launcher\` (`settings.json`, `download_state.json`, `unified.log`, log exports). File names are declared in `Constants/GamePaths.cs`; the root and the well-known paths inside it are owned by `Services/LauncherDataRoot.cs`. That root is resolved once — by the composition root, and by the ADR-019 pre-DI paths (`Program.cs`, the isolated crash reporter app) — and injected everywhere else; the one declared exception is `Services/GameRuntime/GameCompatibilityPaths`, a static helper with no DI seam that resolves the Windows root at call time and uses the XDG data home on Unix. Modules that persist user data take a `LauncherDataRoot` instead of resolving a process-level static, so a new module cannot silently land in the real user data directory. `TestUserDataIsolationTests.ProcessRootResolution_IsConfinedToDeclaredPreDiSites` guards which files may resolve it (see [ADR-025](docs/design/adr/ADR-025-数据根由组合根解析并注入.md)); tests swap the root through `TestDataRoot`.
- `settings.json` is written only through `ISavedSettingsWriter` (`Services/SavedSettingsWriter.cs`); every other module reads it through `LauncherSettingsService`. The writer persists and then makes the normalized persisted value both the draft and the snapshot, so the editor cannot disagree with disk. Callers describe the change (`SaveDraftAsync` / `UpdateAsync` / `ReplaceAsync`) instead of reading, patching, saving and re-syncing the draft themselves — a write that skips the writer silently loses that field when the user next saves settings. `SettingsWriteOwnershipTests` guards the boundary; rationale in ADR-024.
- The game directory is normalized to `YostarGames\BlueArchive_JP`. All game file operations must go through `Helpers/GamePathValidator` so they stay inside that directory; the same validator rejects reparse points and dangerous destinations.
- `LocalInstallationStateStore` manages `game-launcher-config.json` and `manifest.json` as one coordinated installation state shared with the official launcher. Preserve the JSON/wire field order used by `OfficialHashService` — changing it makes the launchers reject each other's manifest.
- Launch validation intentionally fails open when a requested remote manifest cannot be retrieved; repair verifies with CRC64 while launch verification checks size/existence. These mirror the official launcher and are covered by contract tests.
- Outbound remote URLs go through `Services/RemoteHttpUrlValidator`. Its private-address DNS rejection is intentionally skipped only for proxy egress; scheme, port, userinfo, localhost, and literal-IP checks always apply. On the direct path the Fake-IP answer bands (IPv4 `198.18.0.0/15`, IPv6 ULA `fc00::/7`) are tolerated — fake-ip-mode proxies (Clash/mihomo) answer every domain from these ranges, so blocking them breaks dual-stack users (the AUD-SEC-006 concession, extended to IPv6). When such a direct request then fails to connect, `RemoteHttpRequestService` marks the exception (`Exception.Data`, same exception type), and the error presentation swaps the generic network attribution for targeted Fake-IP guidance.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# uses UTF-8, CRLF, four-space indentation, file-scoped namespaces, braces, and explicit types unless the type is apparent. Other repository text files use LF. `.editorconfig` states these endings for editors and `.gitattributes` enforces the same ones for git; keep the two in sync (`LineEndingPolicyContractTests` guards this), because a file committed with the wrong ending shows every later edit as a whole-file diff.

Use PascalCase for types and public members, camelCase for locals and parameters, and the existing `IService`/`Service` pairing for abstractions. Keep XAML values on the design tokens defined in `App.axaml`; do not introduce raw colors, `Transparent`, raw icon sizes, or raw 4/6/8 corner radii in views. Detailed rules, anti-patterns, and the pre-PR checklist live in `PROJECT_CONVENTIONS.md`.

## Localization & Configuration

Add every UI string to the neutral `Resources/LauncherStrings.resx` file and its `zh-Hans`, `zh-Hant`, and `ja` counterparts, keeping each file alphabetically ordered. Bind UI text through `Shell.I18n[resourceKey]` and use `LocalizationService.T()` / `F()` in C#. In C# source, never pass raw key string literals to `T()`/`F()`/`I18n[...]` — reference the compile-time constants on `Constants/LocalizationKeys` instead. Both halves of that vocabulary are guarded: the `.cs` side by rejecting raw key literals, and the `.axaml` side — whose binding path is a string and cannot reference a C# constant — by asserting every `Shell.I18n[…]` key exists in the neutral `.resx` (`ResxResourceContractTests.XamlResourceBindings_UseOnlyKeysThatExistInNeutralResources`, with its anti-vacuity baseline in `XamlKeyScan_StillCoversTheKeyBearingFiles`).

Regenerate `Resources/LauncherStrings.Designer.cs` with `scripts/Generate-LauncherStringsDesigner.ps1` and `Constants/LocalizationKeys.cs` with `scripts/Generate-LocalizationKeys.ps1` after adding or renaming a key, then run `scripts/Test-LocalizationContract.ps1`. Preserve resource-key spelling, casing, and composite-format placeholders. Every interactive control needs an `AutomationProperties.Name` bound to a localized string. Never infer identifier spelling, casing, paths, or payload structure; inspect the defining code, tests, logs, or captured data first.

## Testing Guidelines

Tests use xUnit v3; UI tests use `Avalonia.Headless.XUnit`. Name tests `Method_State_ExpectedResult` — behavior tests use the full three-segment form, while source/guard contract tests (asserting files, resources, or member structure rather than behavior) may use the two-segment `Subject_Expectation` form. Do not introduce a mocking framework; prefer handwritten `HttpMessageHandler` subclasses, fakes, and stubs (shared ones live in `tests/TestDoubles/`).

Add focused regression tests for behavior changes. New services should cover the success path, the typical failure path, and key boundary conditions. Platform-gated tests must skip visibly with `Assert.SkipUnless`/`Assert.SkipWhen` rather than early `return`, so the skip shows up in test results. Run `UiStyleContractTests` after XAML/style edits and `.\dev.ps1 ui` for broader UI changes. Run `.\scripts\Test-LocalizationContract.ps1` after modifying any `LauncherStrings*.resx`. Before merging or releasing, run `.\verify.ps1`. Golden-screenshot baselines are regenerated with `.\test.ps1 -UpdateGolden`.

Use the shared facilities instead of re-rolling them (see `PROJECT_CONVENTIONS.md` §6.5): a temp directory is a `TestDirectory` that owns its own cleanup, `LauncherDataRoot` is derived from it, and every wait for asynchronous state goes through `TestWait.UntilAsync` (`HeadlessTestHost.WaitUntilAsync` adds the UI-dispatch half). Assemble a `MainWindowViewModel` through `MainWindowTestContext`, which states who owns what — the loggers it hands to consumers must outlive the assembly call. Test through the real container only for composition, binding, and window integration.

## Release Notes

Treat `CHANGELOG_RELEASE.md` as a single-release document. When preparing notes for a new version, replace its contents with only that version's section and remove every older version section. Before completion, verify that `rg -n "^## v" CHANGELOG_RELEASE.md` returns exactly one heading and that it matches the version being released; `ReleaseChangelogContractTests` guards the heading, the notice blocks, and the vocabulary rule below.

Name the version per SemVer relative to the previous tag. A backward-compatible fix or feature inside an existing prerelease series increments the prerelease number (`1.1.0-beta.7` → `1.1.0-beta.8`), not the major/minor/patch. `release.ps1 <version>` owns the csproj bump, commit, tag, and push — run it only when a release is explicitly intended.

Write the notes for the person installing the launcher, not for a contributor reading the diff:

- Describe only what the user can see. Audit ledgers, test and coverage work, CI changes, refactors, call sites, and internal architecture vocabulary (modal stack, DI construction, `AppDomain`) belong in commit messages and audit records, never in release notes.
- A feature introduced in this release that is later fixed or reworked before the release ships is described once, in its final form. That defect never reached users, so it does not get its own fix entry.
- Use the shipped UI wording: check the user-facing string in `Resources/LauncherStrings.zh-Hans.resx` before naming a button, label, or status.
- Keep the `> [!NOTE]` focus summary and `> [!WARNING]` stability warning blocks. The referenced banner PNG must be committed under `docs/assets/release-banners/` before tagging; `release.yml` fails the tag build when it is missing. Generate it with the spec-driven `promotional-image` pipeline documented in [docs/promo/release-banner-guide.md](docs/promo/release-banner-guide.md) — copy `docs/promo/specs/release-banner.template.json` per release and commit the spec alongside the banner, so the design stays reproducible rather than living in a one-off script.

## Commit & Pull Request Guidelines

Use Conventional Commits, matching history: `feat(setup): ...`, `fix: ...`, `refactor: ...`, `perf: ...`, or `docs: ...`. Keep each commit focused. Pull requests must explain the change and motivation, link related issues, list verification commands, and include screenshots for visible UI changes. Confirm `verify.ps1` succeeds before requesting review.

None of this is mechanically enforced — the `main` ruleset blocks only deletion and force-pushes, so the pull-request and green-CI requirements are conventions rather than gates (the enforced rules are documented in `PROJECT_CONVENTIONS.md` §9). When merging Dependabot PRs, squash with a `chore(deps): ...` prefix instead of merging the bot's non-conventional commit — release notes are grouped by conventional prefix, and a bare "Bump ..." commit risks landing in the wrong changelog bucket.

## Audit State

`CODEBASE_AUDIT.md` is the current-state repository audit; dated audit reports and ledgers are archived under `.repository-audit/history/`. Treat both as engineering records: update the current report when a finding is fixed or accepted, and archive superseded reports rather than deleting them. The single query entry point for candidates, findings and pending decisions is `docs/design/candidates-ledger-2026-09.md` — it is a view whose owners are the documents above, so change the owner first and update its row afterwards; it does not replace the archived reports, which stay as evidence (its §4 records what only exists there).
