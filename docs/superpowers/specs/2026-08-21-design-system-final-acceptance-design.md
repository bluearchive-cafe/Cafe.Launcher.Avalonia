# Design System Final Acceptance Design

## Status

Approved design for implementing every final-acceptance item in
[`design-system-redesign.md`](../../design-system-redesign.md). The HTML files in
[`docs/design-system-prototype`](../../design-system-prototype) are the approved
visual reference.

## Goals

- Replace the current mixed `Launcher*` design system with the documented
  `Cafe*` system and its resource/style boundaries.
- Faithfully implement the approved dark home, progress home, compact home,
  light settings, and component-reference prototypes in Avalonia.
- Fix behavioral acceptance gaps: lossless debounced settings persistence,
  accessible accent contrast, banner pause/keyboard behavior, responsive home
  layout, dialog safety, and toast behavior.
- Keep FluentTheme, Material icons, localized UI resources, atomic settings
  writes, and backward-compatible `settings.json` handling.

## Non-goals

- Add a new icon library, component framework, telemetry path, or navigation
  framework.
- Preserve a long-lived compatibility alias layer for `Launcher*` resources or
  the old internal style API.
- Change game-operation, remote-content, or settings JSON domain semantics
  beyond the behavior required by this redesign.

## Architecture

`App.axaml` becomes the resource host and merges the four design-system levels:

```text
App.axaml
├─ Views/Styles/Foundation.axaml
│  └─ CafeSpace, CafeRadius, CafeIcon, CafeTypography, CafeMotion
├─ Views/Styles/Theme.axaml
│  └─ light/dark semantic colors, surfaces, text, borders, scrim, accent family
├─ Views/Styles/Controls.axaml
│  └─ buttons, fields, selects, switches, segmented controls, surfaces,
│     toasts, dialogs and every interaction state
└─ Views/Styles/<feature>.axaml
   └─ feature-private layouts only
```

Foundation is the only source of spacing, radius, icon-size, typography, and
motion scales:

- Spacing: 4, 8, 12, 16, 24, 32.
- Radius: 6, 10, 16.
- Icons: 16, 20, 24.
- Type roles: Caption 12, Body/Label 14, Title 18, Display 24.
- Motion: fast 140ms, standard 220ms, surface 320ms, with a reduced-motion
  equivalent that disables nonessential transitions.

Theme owns semantic brushes and accent variations. Dynamic accent selection
changes only the accent family; it cannot inject raw colors into views or
structural tokens. `Controls.axaml` defines normal, hover, pressed, disabled,
and focus-visible states for each shared interactive control. Views use simple
semantic classes such as `primary`, `quiet`, `danger`, `surface`, and
`elevation`; long multi-class combinations are removed.

The old `Launcher*` resources and monolithic `MainWindow.Styles.axaml` rules
are removed as part of the XAML migration, with no compatibility aliases.

## Shared Components

- `SettingRow` and `SettingComboRow` contain title, description, and control
  only. Decorative row icons are removed. At normal width they use a text/control
  grid; at narrow width the control moves beneath its text and stretches.
- `DialogFrame` provides the scrim, focus containment, header, content/action
  regions, close behavior, and only `Small` and `Large` size variants. Settings,
  confirmation, resource, log, debug, and setup content retain their existing
  feature-specific logic while using this common frame where applicable.
- `LoadingOverlay` centralizes the shared loading presentation without changing
  operation state ownership.

Home-specific announcement, banner, news, social rail, and operation dock stay
feature-private but compose the shared resources and controls.

## Home Layout and Responsive Behavior

- The normal content rail follows the approved 424px visual baseline (within
  the documented 400–440px range); it contains announcement, banner, and news
  in that order.
- The social rail is separate and remains on the right in normal layout.
- The bottom operation dock is always visible and contains exactly one visually
  primary action; the ready and in-progress states use the approved layouts.
- At the compact threshold (the prototype's 1080px baseline, covering the
  required 1024px acceptance case), the content rail becomes a toggleable
  390px drawer. Social actions move into More, while the dock and unique primary
  action remain visible.
- Toasts render at the top right above the social rail and below modal/dialog
  layers only where modal semantics require it.

## Banner Interaction Contract

- Advance every six seconds when motion is allowed and the banner is active.
- Pause while pointer-hovered, when the application window is deactivated,
  when the system requests reduced motion, and when the user toggles pause with
  Space while the banner has focus.
- Left/Right move the active banner only while its focus scope is active.
- Previous/next buttons and slide indicators remain pointer accessible and have
  localized accessible names and current-state feedback.

## Settings Data Flow and Failure Recovery

```text
setting mutation
 ├─ apply runtime effect immediately
 ├─ mark version N pending
 └─ reset 400ms debounce
       └─ persist snapshot N atomically
            ├─ newer pending version -> persist newest snapshot next
            ├─ success with no newer version -> clear pending/failed state
            └─ failure -> retain pending state, show localized error and Retry
```

- Every mutation, not merely an `IsDirty` transition, schedules persistence.
- Snapshot versions prevent a save completion from marking later edits as saved.
- Closing the settings overlay cancels only the waiting debounce and flushes the
  newest pending snapshot before the edit session ends; a failed flush keeps the
  overlay/session open until the user retries or discards the edit. Application
  shutdown follows the same rule.
- Failure does not roll back already-applied theme/language/accent/background
  runtime effects. The settings content exposes localized unsynced state and a
  retry command until writing succeeds.
- `LauncherSettingsService` retains its lock, temporary file, and atomic move.
- A clear global restore-defaults action changes the editor through this same
  immediate-apply/save/retry path.

## Color Accessibility

Default, system, wallpaper, and custom accent sources generate default, hover,
pressed, soft, and on-accent values. `on-accent` is selected from actual WCAG
contrast against the resulting background rather than relying on a luminance cap
that makes white text inevitable. The implementation must meet the document's
contrast requirement for regular text and focus indicators in both themes.

## Dialogs, Toasts, and Safety

- Dialogs use `DialogFrame`'s `Small` or `Large` variant; per-feature layout
  remains private rather than becoming global App tokens.
- Destructive confirmation presents Cancel on the left and confirmation on the
  right, with Cancel as the default focus target. Escape and close retain safe
  cancellation behavior.
- Toasts no longer expose auto-dismiss countdown progress. Real work progress
  remains visible for actions that are actually executing.

## Localization and Repository Guidance

Every new user-facing string is added to neutral, zh-Hans, zh-Hant, and Japanese
resources and gains localized automation names. `LauncherStrings.Designer.cs` is
regenerated after key changes. `AGENTS.md`, `CLAUDE.md`, and
`PROJECT_CONVENTIONS.md` are updated to name `Cafe*` resources and the layered
style system rather than the retired `Launcher*` system.

## Verification Strategy

### Unit tests

- Versioned 400ms debounce, mutation-during-save, write failure/retry, and
  close-time flush for settings; failed close/exit flushes keep the edit session
  open and cancel the shutdown path.
- Accent scale and foreground contrast for every color source.
- Banner timer and pause-state transitions.

### Headless UI tests

- Compact 1024px drawer, always-visible dock, and exactly one primary action.
- Responsive settings row wrapping.
- Banner keyboard controls, pointer controls, and reduced-motion behavior.
- Modal, toast, and social-rail layering.
- Confirmation dialog default focus and Escape handling.

### Static UI contracts

Replace old exact selector/resource-count snapshots with tests that assert:

- `Cafe*` layered resource ownership and removal of retired `Launcher*` names.
- No raw colors, radii, or icon sizes in View XAML.
- Shared controls declare every required interaction state.
- Semantic style classes are used rather than long combined class names.

### Required commands

Run the localization contract after resource changes; run `dev.ps1 ui` after
XAML changes; run focused unit/headless tests during implementation; and finish
with `verify.ps1`. Full build verification requires the currently running
Launcher to be closed if it holds the Debug output files.

## Acceptance Mapping

The five approved prototype pages are the visual evidence for dark home,
download-progress home, compact home, light settings, and component inventory.
The behavior/test sections above map every remaining final-acceptance item in
`design-system-redesign.md` to an implementation and automated verification
surface.
