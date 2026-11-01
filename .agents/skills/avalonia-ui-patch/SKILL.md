---
name: avalonia-ui-patch
description: Use for a localized Avalonia UI fix such as alignment, spacing, selected state, localized display, or an existing control binding.
---

# Avalonia UI Patch

Use this workflow only for a small, localized correction to an existing Avalonia screen or control.

1. Follow the UI routing in [AGENTS.md](../../../AGENTS.md), then inspect the affected XAML, its ViewModel, and focused tests.
2. When feedback includes a screenshot, first check the actual runtime bindings and automation properties; do not infer the cause from appearance alone.
3. Reproduce the reported visual or interaction symptom with the narrowest existing headless/style test; add a focused regression test when the current suite cannot detect it.
4. Reuse the target surface's resources under [Conventions §4](../../../PROJECT_CONVENTIONS.md#4-xaml-与视觉规范): main UI uses `Launcher.*`; the isolated crash window uses its own `Crash.*` resources. Keep the correction within the affected control or screen.
5. Choose checks from [Development: commands and validation](../../../docs/DEVELOPMENT.md#命令与验证): focused tests and `UiStyleContractTests` for XAML/styles; `pwsh ./dev.ps1 ui` for larger UI changes. For localization resource changes, follow [Conventions §6](../../../PROJECT_CONVENTIONS.md#6-本地化), including generated keys when applicable. Run repository scripts from the repository root with `pwsh`.

## Broader changes

For settings persistence, navigation rules, integrations, or multi-step product flows, switch to the relevant domain contracts and implementation workflow through `AGENTS.md`. Continue the user's authorized work; produce only a plan when that is the requested deliverable or a concrete unresolved decision prevents implementation.

When changing this workflow, check the [regression scenarios](tests/scenarios.md).
