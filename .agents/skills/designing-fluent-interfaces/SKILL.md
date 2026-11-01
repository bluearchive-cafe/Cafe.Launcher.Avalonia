---
name: designing-fluent-interfaces
description: Design, implement, or review Fluent 2 and Windows/WinUI interfaces. In this repository, use for the independent Fluent crash window or explicit Fluent guidance; FluentTheme alone does not select the main UI's design system.
metadata:
  version: "1.0.0"
  last-reviewed: "2026-08-27"
---

# Designing Fluent Interfaces

## Overview

Use Fluent as a system of relationships, hierarchy, behavior, and platform conventions—not as a collection of blue colors, blur, and rounded corners.

**Core rule:** identify the target platform before applying numeric values or implementation details. Fluent 2 Web and Windows 11/WinUI share principles but intentionally differ in state colors, elevation treatment, geometry, typography, and platform resources.

## Platform gate

For this repository, read the [design specification](../../../docs/design/design-system-spec.md#1-设计原则) and [XAML conventions](../../../PROJECT_CONVENTIONS.md#4-xaml-与视觉规范) first. The main UI uses M3 semantics on Avalonia FluentTheme; use the [Material workflow](../material-design/SKILL.md) for those semantics. The crash window uses an independent Fluent system with local `Crash.*` resources. Preserve that boundary and the repository's geometry, even when generic WinUI defaults differ.

Before designing or changing UI, classify the target:

| Target | Primary guidance |
| --- | --- |
| Web / React / Microsoft 365-style web UI | Fluent 2 foundations + Fluent UI React v9 tokens/components |
| WinUI 3 / Windows App SDK | Windows design guidance + WinUI controls/resources; use Fluent 2 only as the conceptual foundation |
| Avalonia / another desktop framework | Framework-native controls and resources; read the non-Microsoft framework section in implementation guidance |
| iOS / Android / macOS | Fluent 2 cross-platform principles, then preserve native platform typography, input, sizing, and accessibility conventions |
| Unspecified / visual concept only | Use cross-platform Fluent 2 principles; do not invent Windows- or Web-specific numeric rules |

If the platform is ambiguous and the choice would materially change the result, state the assumption before proceeding.

## Workflow

1. **Understand the task and information hierarchy.** Identify the primary user goal, primary action, navigation depth, density, and important states before styling.
2. **Choose the platform profile and relevant references** from the table below.
3. **Establish hierarchy, layout, components, and applicable states.** Add motion after states and layout work; use it to explain change or provide feedback.
4. **Implement with the target framework's controls and semantic resources.** Check accessibility, localization, theme behavior, and small-window behavior for the affected surface. In this repository, select checks from the [development guide](../../../docs/DEVELOPMENT.md#命令与验证).

## Non-negotiable design rules

- Prefer semantic design tokens and theme resources over hardcoded colors, spacing, radius, shadows, typography, and animation values.
- Do not treat Fluent 2 Web token values as WinUI constants.
- Use one visually dominant primary action per local action group. Do not give every action equal emphasis.
- Use spacing and grouping before adding dividers, borders, cards, or extra surfaces.
- Use brand/accent color sparingly for emphasis and interaction; never as the only status signal.
- Keep persistent surfaces visually calm. Reserve stronger elevation and materials for hierarchy or transient UI.
- Mica is a long-lived Windows app backdrop; Acrylic is primarily for transient/contextual surfaces. Do not turn every panel into frosted glass.
- Keep motion fast, purposeful, and interruptible. Respect reduced-motion/no-motion preferences.
- Keyboard focus, semantic structure, readable contrast, and screen-reader labeling are part of the component design, not post-processing.
- Use platform-native controls when they already encode Fluent behavior, accessibility, input, and theming.

## Output expectations

When asked to produce a design, implementation, or review:

- State the target platform/profile when relevant.
- Explain major component and navigation choices by user task, not aesthetics alone.
- Name the semantic token/resource or component when implementation details are requested.
- Separate **official guidance** from **product-specific design judgment**.
- Call out any intentional deviation from Fluent and explain the usability or product reason.

## Reference loading

Load only the references needed for the task:

| Task | Reference |
| --- | --- |
| Platform differences | [Scope and platforms](references/scope-and-platforms.md) |
| Color, type, shape, elevation, icons, materials | [Foundations](references/foundations.md) |
| Layout and navigation | [Layout and navigation](references/layout-and-navigation.md) |
| Animation and transitions | [Motion](references/motion.md) |
| Component selection | [Components and patterns](references/components-and-patterns.md) |
| Accessibility, focus, writing, localization | [Accessibility and content](references/accessibility-and-content.md) |
| React, WinUI, or other framework implementation | [Implementation](references/implementation.md) |
| Source verification | [Sources](references/sources.md) |
| Changing or evaluating this skill | [Evaluation scenarios](references/evaluation-scenarios.md) |
