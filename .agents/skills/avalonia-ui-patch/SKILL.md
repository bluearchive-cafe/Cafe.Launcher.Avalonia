---
name: avalonia-ui-patch
description: Use for a localized Avalonia UI fix such as alignment, spacing, selected state, localized display, or an existing control binding.
---

# Avalonia UI Patch

Use this workflow only for a small, localized correction to an existing Avalonia screen or control.

1. Read `AGENTS.md`, the affected XAML, its ViewModel, and the existing focused tests.
2. When feedback includes a screenshot, first check the actual runtime bindings and automation properties; do not infer the cause from appearance alone.
3. Reproduce the reported visual or interaction symptom with the narrowest existing headless/style test; add a focused regression test when the current suite cannot detect it.
4. Reuse tokens from `App.axaml`; do not introduce raw colors, spacing, dependencies, or unrelated refactors.
5. Run `./dev.ps1 ui`; run `./scripts/Test-LocalizationContract.ps1` when a locale JSON file changes.

## Escalate instead

Do not use this skill for settings persistence, navigation rules, external integrations, or a multi-step product-flow change. Create a design and implementation plan for those changes.
