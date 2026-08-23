# Design System Final Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement every final acceptance item in `docs/design-system-redesign.md`, using the approved HTML prototypes as the visual baseline.

**Architecture:** Replace the existing `Launcher*` resource and monolithic-style system with `Cafe*` Foundation, Theme, Controls, and feature-private styles. Keep behavior in the existing ViewModel/services boundaries; add only the shared UI components required by the approved design. Settings persistence becomes a versioned, debounced state machine that does not lose edits.

**Tech Stack:** .NET 10, Avalonia 12.1, CommunityToolkit.Mvvm, Material.Icons.Avalonia, xUnit v3, Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-design-system-final-acceptance-design.md`

## Global Constraints

- Treat the existing dirty worktree as user-owned baseline. Inspect and extend it; never reset, checkout, or stage unrelated files.
- The worktree is currently on `main`; do not commit there. Make focused commits only after the user supplies or creates an appropriate feature branch.
- Retain FluentTheme, Material Icons, atomic settings writes, all current `settings.json` fields/normalization, and four-language localization.
- Do not introduce a `Launcher*`-to-`Cafe*` compatibility alias layer. The final resource/style API is `Cafe*` only.
- Run the targeted test command specified by each task before beginning the next task. New behavior gets a regression test before implementation.

## Tasks

- [ ] 1. Make settings persistence lossless, debounced, and recoverable

  **Files:**
  - Modify: `Services/SettingsEditor.cs`
  - Modify: `Features/Settings/SettingsViewModel.cs`
  - Modify: `ViewModels/WindowChromeViewModel.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/SettingsEditorTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/WindowChromeViewModelTests.cs`

  **Step 1: Write failing regression tests.** Cover a second edit made while a save is in flight, a sequence of quick edits yielding one save after 400ms, a failed save retaining unsynced/retry state, retry saving the latest snapshot, and closing settings flushing a pending edit. Use injected delay/save fakes rather than real waiting or disk I/O.

  **Step 2: Implement the versioned save coordinator.** Have every `CurrentPropertyChanged` mutation schedule a cancellable 400ms debounce. Capture a snapshot and revision for each write; only call `MarkSaved` when the completed revision is still current, otherwise queue the newest snapshot. Keep the existing save semaphore and `LauncherSettingsService` atomic write path.

  **Step 3: Apply runtime state at mutation time.** Preserve appearance preview behavior and apply language/theme/log-level effects without waiting for disk persistence. Expose localized pending/failure state and a retry command, and make the global restore-defaults command use the same edit/apply/save path.

  **Step 4: Flush safely on close/disposal.** Replace the Boolean autosave toggle with an async close/flush path so `WindowChromeViewModel` cannot reload the saved snapshot over a pending edit. A failed flush keeps the panel/session open with its retry state; application shutdown is canceled until the pending snapshot is persisted. Cancellation only stops the debounce; it must not discard the latest snapshot.

  **Step 5: Run focused tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~SettingsEditorTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~WindowChromeViewModelTests"
  ```

- [ ] 2. Replace accent normalization with contrast-safe palette generation

  **Files:**
  - Modify: `Helpers/ColorUtils.cs`
  - Modify: `Features/Settings/SettingsAppearanceViewModel.cs`
  - Modify: `Constants/LauncherConstants.cs` only if the default accent constant is moved to the new theme resource
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/MainWindowViewModelTests.cs`
  - Add: `tests/Cafe.Launcher.Avalonia.Tests/ColorUtilsTests.cs`

  **Step 1: Write failing tests.** Assert a contrast-ratio helper and palette output meet the documented threshold for default, light, dark, low-saturation, wallpaper, and custom accents, including foreground and focus indicator selection.

  **Step 2: Implement color utilities.** Add WCAG contrast-ratio calculation and derive default/hover/pressed/soft/on-accent values from the selected source. Pick a dark or white on-accent foreground by measured contrast; adjust the usable accent only when neither foreground meets the required ratio.

  **Step 3: Apply palette as resources.** Change `SettingsAppearanceViewModel.ApplyAccentBrushes` to update the new `CafeAccent*` resources only, keeping theme structural resources intact.

  **Step 4: Run focused tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~ColorUtilsTests|FullyQualifiedName~MainWindowViewModelTests"
  ```

- [ ] 3. Complete the banner's six-second, pause, and keyboard contract

  **Files:**
  - Modify: `ViewModels/RemoteContentViewModel.cs`
  - Modify: `Views/MainWindow.axaml`
  - Modify: `Views/MainWindow.axaml.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/RemoteContentViewModelTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

  **Step 1: Write failing tests.** Cover a fixed six-second loop interval, pointer enter/leave pause, window deactivate/reactivate pause, reduced-motion pause, Space toggle, scoped Left/Right navigation, and pointer activation of previous/next/dot controls.

  **Step 2: Make pause causes composable.** Model user pause, pointer hover, window activation, and reduced motion independently in `RemoteContentViewModel`; derive whether the timer may run instead of letting one cause overwrite another. Remove the current five-second manual-navigation resume behavior if it conflicts with this contract.

  **Step 3: Wire Avalonia events and accessibility.** Add banner pointer/focus/key handlers in `MainWindow.axaml(.cs)`, subscribe to `Deactivated`, and ensure all controls use localized automation names, selected-state semantics, and keyboard focusability.

  **Step 4: Run focused tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~RemoteContentViewModelTests"
  dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
  ```

- [ ] 4. Remove auto-dismiss toast progress while retaining real action progress

  **Files:**
  - Modify: `Services/ToastNotification.cs`
  - Modify: `ViewModels/ToastHostViewModel.cs`
  - Modify: `Views/MainWindowToastOverlay.axaml`
  - Modify: `Views/Styles/Toast.axaml`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/ToastHostViewModelTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

  **Step 1: Write failing tests.** Assert ordinary auto-dismiss toasts wait then exit without an `AutoDismissProgress` public contract, while a running toast action still displays its indeterminate work progress.

  **Step 2: Simplify the notification lifecycle.** Remove countdown state and its 50ms loop from `ToastHostViewModel` and `ToastNotification`; keep duration-based dismissal, cancellation, exit animation, and action execution intact.

  **Step 3: Remove only the countdown ProgressBar.** Keep the action-executing progress bar in the toast template and revise styles/contracts accordingly.

  **Step 4: Run focused tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~ToastHostViewModelTests|FullyQualifiedName~UiStyleContractTests"
  ```

- [ ] 5. Install the final `Cafe*` resource and control layers

  **Files:**
  - Add: `Views/Styles/Foundation.axaml`
  - Add: `Views/Styles/Theme.axaml`
  - Add: `Views/Styles/Controls.axaml`
  - Modify: `App.axaml`
  - Replace/remove: `Views/MainWindow.Styles.axaml`
  - Modify: `Views/Styles/RemoteContent.axaml`, `Views/Styles/Toast.axaml`, `Views/Styles/Diagnostics.axaml`, `Views/Styles/SetupWizard.axaml`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

  **Step 1: Write failing semantic resource tests.** Check only ownership and invariants: the App merge order, documented scales, theme-specific semantic resources, shared control states, and absence of retired `Launcher*` resource names. Do not assert selector counts or exact implementation layout.

  **Step 2: Add Foundation and Theme dictionaries.** Define the approved spacing/radius/icon/type/motion scale and dark/light semantic brushes, scrim, elevation, and accent placeholders.

  **Step 3: Add Controls dictionary.** Move shared Button, ComboBox, TextBox, ToggleSwitch, segmented control, surface, toast, and dialog state styles here. Include normal, hover, pressed, disabled, and focus-visible behavior.

  **Step 4: Migrate feature styles and remove old system.** Change all XAML resource references to `Cafe*`; place feature-private layout rules in their feature styles; delete old global feature-specific tokens and the monolithic old style rules in the same change.

  **Step 5: Run style contract tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~UiStyleContractTests"
  ```

- [ ] 6. Rebuild shared settings and dialog primitives, then migrate settings screens

  **Files:**
  - Modify: `Controls/SettingRow.axaml`, `Controls/SettingRow.axaml.cs`
  - Modify: `Controls/SettingComboRow.axaml`, `Controls/SettingComboRow.axaml.cs`
  - Add: `Controls/DialogFrame.axaml`, `Controls/DialogFrame.axaml.cs`
  - Modify: `Controls/ConfirmDialog.axaml`, `Controls/ConfirmDialog.axaml.cs`
  - Modify: `Controls/LoadingOverlay.axaml`, `Controls/LoadingOverlay.axaml.cs`
  - Modify: `Views/MainWindowSettingsOverlay.axaml`
  - Modify: `Views/SettingsGeneralSection.axaml`, `Views/SettingsGameSection.axaml`, `Views/SettingsDownloadNetworkSection.axaml`, `Views/SettingsAppearanceSection.axaml`, `Views/SettingsAdvancedSection.axaml`, `Views/SettingsAboutSection.axaml`
  - Modify: `Resources/LauncherStrings*.resx`, `Resources/LauncherStrings.Designer.cs`
  - Modify: UI/headless contract tests listed in Tasks 1 and 5

  **Step 1: Write failing UI contracts.** Assert setting rows have no icon slot, wrap control below text in compact width, settings header exposes only title/unsynced state/close, and restore-defaults/retry are accessible.

  **Step 2: Simplify setting controls.** Remove `IconKind` APIs and decorative XAML, preserve control bindings, and apply two-column/stacked layouts through semantic classes.

  **Step 3: Introduce `DialogFrame`.** Encode small/large sizing, scrim, header/content/action regions, focus handling, and close safety; route ConfirmDialog and overlay shells through it. Re-style existing `LoadingOverlay` as a shared primitive.

  **Step 4: Migrate settings content.** Remove the prohibited settings status/branding footer, show save failure/retry state in content, add localized restore-defaults, and update every settings section to the new row primitives.

  **Step 5: Regenerate and validate localization.**

  ```powershell
  .\scripts\Generate-LauncherStringsDesigner.ps1
  .\scripts\Test-LocalizationContract.ps1
  ```

- [ ] 7. Rebuild the home shell against all three approved home prototypes

  **Files:**
  - Modify: `Views/MainWindow.axaml`
  - Modify: `Views/MainWindow.axaml.cs`
  - Add or modify: `Views/Styles/Home.axaml`
  - Modify: `ViewModels/MainWindowViewModel.cs` and/or `ViewModels/WindowChromeViewModel.cs` for compact-layout state
  - Modify: `Views/Styles/RemoteContent.axaml`
  - Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`

  **Step 1: Write failing layout tests.** At 1200×720, assert the always-visible 424px content rail, independent social buttons with the right-edge gradient, visible operation dock, and exactly one `primary` operation. Assert the same dock layouts for ready and progress states.

  **Step 2: Restructure the shell.** Keep the transparent title bar and current commands, but replace duplicate operation layouts with one dock composition that switches presentation by runtime state. Render announcement/banner/news in the approved content rail and move social entries to a vertical right rail.

  **Step 3: Remove compact behavior.** Raise the minimum window size to 1200×720, remove the drawer state and toggle, and keep social actions visible without changing their command or automation coverage.

  **Step 4: Apply home-private styles.** Match approved token spacing, radius, hierarchy, progress, and elevation using `Home.axaml` plus shared controls; no raw visual values in the view.

  **Step 5: Run UI tests.**

  ```powershell
  .\dev.ps1 ui
  ```

- [ ] 8. Migrate all remaining overlays and tools to the shared dialog/toast system

  **Files:**
  - Modify: `Views/MainWindowDialogsOverlay.axaml`
  - Modify: `Views/MainWindowDebugOverlay.axaml`
  - Modify: `Views/MainWindowLogViewerOverlay.axaml`
  - Modify: `Views/SetupWizardOverlay.axaml`
  - Modify: `Views/MainWindowToastOverlay.axaml`
  - Modify: `Views/Styles/Diagnostics.axaml`, `Views/Styles/SetupWizard.axaml`, `Views/Styles/Toast.axaml`
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/DialogActionButtonContractTests.cs`
  - Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

  **Step 1: Write failing contracts.** Verify small/large dialog variants, tool overlay consistency, confirmation cancel-first/default-focus behavior, and Toast-over-social/modal layer ordering.

  **Step 2: Apply `DialogFrame` variants.** Preserve each feature's ViewModel and command surface while migrating overlay chrome, padding, and actions to the common frame.

  **Step 3: Validate focus and motion.** Retain `OverlayFocusBehavior`, Escape semantics, and reduced-motion behavior while eliminating duplicate global size/visual tokens.

  **Step 4: Run focused tests.**

  ```powershell
  dotnet test .\tests\Cafe.Launcher.Avalonia.Tests\Cafe.Launcher.Avalonia.Tests.csproj --filter "FullyQualifiedName~DialogActionButtonContractTests|FullyQualifiedName~UiStyleContractTests"
  dotnet test .\tests\Cafe.Launcher.Avalonia.HeadlessTests\Cafe.Launcher.Avalonia.HeadlessTests.csproj --filter "FullyQualifiedName~MainWindowHeadlessTests"
  ```

- [ ] 9. Finish semantic contracts, update repository guidance, and perform final acceptance verification

  **Files:**
  - Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
  - Modify: `AGENTS.md`, `CLAUDE.md`, `PROJECT_CONVENTIONS.md`
  - Modify: `docs/design-system-redesign.md` only to record completed final acceptance if its status section calls for it
  - Review: `docs/design-system-prototype/*.html`, `docs/design-system-prototype/styles.css`

  **Step 1: Replace obsolete snapshots.** Remove tests that freeze former selector names, old resource counts, old fixed dimensions, and the removed toast countdown. Retain behavior and accessibility assertions only.

  **Step 2: Update guidance.** Document the `Cafe*` layered resource convention, feature-private styling rule, responsive settings/home contracts, and required UI validation commands.

  **Step 3: Run the acceptance matrix.** Compare the app's dark home, progress home, compact home, light settings, and component states with the approved prototype pages. Record any intentional framework-level rendering differences in the final handoff.

  **Step 4: Run complete verification.** First close any running Launcher process holding Debug binaries; then run:

  ```powershell
  .\scripts\Test-LocalizationContract.ps1
  .\dev.ps1 ui
  .\verify.ps1
  ```

  **Step 5: Inspect final scope.** Run `git diff --check` and `git status --short`; confirm no unrelated user-baseline change was overwritten.

## Final Acceptance Checklist

- [ ] All five approved prototype states are represented by working Avalonia UI.
- [ ] The `Cafe*` Foundation/Theme/Controls/feature style boundary exists and `Launcher*` compatibility resources are gone.
- [ ] Normal and compact home layouts preserve the unique primary action and accessible secondary routes.
- [ ] Banner, settings persistence, color contrast, confirmation safety, and toast behavior satisfy the documented interaction contracts.
- [ ] New UI strings have four translations and the generated designer is current.
- [ ] Focused tests, localization contract, UI tests, and `verify.ps1` pass with zero warnings/errors.
