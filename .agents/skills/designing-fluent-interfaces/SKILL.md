---
name: designing-fluent-interfaces
description: Use when designing, implementing, reviewing, or refactoring interfaces that should follow Microsoft Fluent 2 or Windows 11/WinUI visual, layout, component, motion, accessibility, or content conventions.
metadata:
  version: "1.0.0"
  last-reviewed: "2026-08-27"
---

# Designing Fluent Interfaces

## Overview

Use Fluent as a system of relationships, hierarchy, behavior, and platform conventions—not as a collection of blue colors, blur, and rounded corners.

**Core rule:** identify the target platform before applying numeric values or implementation details. Fluent 2 Web and Windows 11/WinUI share principles but intentionally differ in state colors, elevation treatment, geometry, typography, and platform resources.

## Platform gate

Before designing or changing UI, classify the target:

| Target | Primary guidance |
| --- | --- |
| Web / React / Microsoft 365-style web UI | Fluent 2 foundations + Fluent UI React v9 tokens/components |
| Windows desktop / WinUI 3 / Windows App SDK | Windows design guidance + WinUI controls/resources; use Fluent 2 only as the conceptual foundation |
| iOS / Android / macOS | Fluent 2 cross-platform principles, then preserve native platform typography, input, sizing, and accessibility conventions |
| Unspecified / visual concept only | Use cross-platform Fluent 2 principles; do not invent Windows- or Web-specific numeric rules |

If the platform is ambiguous and the choice would materially change the result, state the assumption before proceeding.

## Workflow

1. **Understand the task and information hierarchy.** Identify the primary user goal, primary action, navigation depth, density, and important states before styling.
2. **Choose the platform profile.** Read [scope and platform distinctions](references/scope-and-platforms.md) when platform behavior matters.
3. **Lay out the experience.** Use [layout and navigation](references/layout-and-navigation.md) for spacing, grids, responsive behavior, breakpoints, and navigation patterns.
4. **Apply foundations.** Use [visual foundations](references/foundations.md) for color, typography, shape, iconography, elevation, and materials.
5. **Select components by semantics.** Use [components and patterns](references/components-and-patterns.md). Prefer standard Fluent/WinUI components before inventing custom controls.
6. **Add motion only after states and layout work.** Use [motion](references/motion.md). Motion must explain change, preserve spatial continuity, or provide feedback.
7. **Check accessibility and content.** Use [accessibility and content](references/accessibility-and-content.md) before calling the design complete.
8. **Implement with platform-native tokens/resources.** Use [implementation guidance](references/implementation.md). Avoid hardcoded visual constants when semantic tokens or theme resources exist.
9. **Review against anti-patterns.** Verify hierarchy, focus, contrast, input states, reduced motion, high contrast/forced colors, localization, and small-window behavior.

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

- Platform differences: [scope-and-platforms.md](references/scope-and-platforms.md)
- Color, typography, spacing, shape, elevation, iconography, materials: [foundations.md](references/foundations.md)
- Responsive layout and navigation: [layout-and-navigation.md](references/layout-and-navigation.md)
- Animation and transitions: [motion.md](references/motion.md)
- Component selection and common patterns: [components-and-patterns.md](references/components-and-patterns.md)
- Accessibility, focus, writing, localization: [accessibility-and-content.md](references/accessibility-and-content.md)
- React/WinUI implementation notes: [implementation.md](references/implementation.md)
- Source index and freshness notes: [sources.md](references/sources.md)
- Behavioral evaluation scenarios: [evaluation-scenarios.md](references/evaluation-scenarios.md)
