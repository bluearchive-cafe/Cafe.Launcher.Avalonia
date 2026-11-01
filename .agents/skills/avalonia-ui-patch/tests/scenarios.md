# Avalonia patch regression scenarios

Run these cases in the repository with its current conventions, design
specification, development guide, and the affected UI code. Record the observed
behavior when evaluating the skill; this list alone is not an executed test.

## Local alignment fix

**Request:** Correct one settings-row alignment issue without changing behavior.

**Expected:** Inspect the affected XAML/ViewModel and existing tests; reuse the
main UI's resources. Choose focused checks and the style contract for XAML changes
using DEVELOPMENT.md. Keep the change local and use `pwsh` for repository scripts.

## Isolated crash-window spacing

**Request:** Fix spacing in the crash window used by the isolated report process.

**Expected:** Read the crash-window exception in conventions and preserve local
`Crash.*` resources. Verify the affected crash-window case without introducing
a dependency on `App.axaml` or main-window `Launcher.*` resources.

## Behavior change discovered during implementation

**Request:** Fix the settings UI and implement the requested save behavior.

**Expected:** Use the local patch workflow for presentation, then follow the
settings contracts for the behavior change. Continue authorized implementation
and focused regression coverage; do not stop merely because work outgrows a UI patch.
