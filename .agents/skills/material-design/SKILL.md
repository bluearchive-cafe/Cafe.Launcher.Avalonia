---
name: material-design
description: >-
  Design, implement, or review Material 2, Material 3, and M3 Expressive interfaces,
  including this repository's main Avalonia UI and Material token mappings.
  Use the Fluent workflow for the independent crash window.
---

# Material Design

## Repository context

For this repository, start with the [design specification](../../../docs/design/design-system-spec.md#1-设计原则)
and [XAML conventions](../../../PROJECT_CONVENTIONS.md#4-xaml-与视觉规范).
The main UI uses **M3 semantics on Avalonia FluentTheme**. FluentTheme is the
implementation base, not a reason to replace the product's Material design rules.
Use existing `Launcher.*` resources and components. The independent crash window
belongs to the [Fluent workflow](../designing-fluent-interfaces/SKILL.md) and keeps its local `Crash.*` resources.
Project geometry, density, and documented exceptions take precedence over generic
platform examples; infer the target from the repository before asking the user.

For other projects, apply this skill when Material alignment is requested or
already established. If the design system is unknown, inspect available project
context first and clarify only when the choice affects the requested work.

## Version and platform

Use the existing project's version unless migration is requested. For a new
Material project with no version constraint, default to M3.

| Task | Mode and reference |
| --- | --- |
| Maintain an M2 interface | M2 concepts; [M2 reference](references/material-2.md) |
| Migrate or compare M2 and M3 | Keep each version explicit; [foundations comparison](references/foundations-comparison.md) |
| M3 Expressive | Layer selectively on M3; [Expressive guidance](references/expressive.md) |
| Ordinary M3 work | Existing M3 components and semantic tokens |

Use the target framework's controls, resources, input behavior, and accessibility
APIs. Platform samples are examples, not instructions to introduce another stack.

## Evidence and constraints

- Separate **specification facts**, **platform/library facts**, and **project heuristics**.
  Reserve mandatory language for actual requirements; label product judgments.
- Prefer official Material guidance for specification questions, official library
  documentation for API behavior, and repository contracts for local choices.
  A component library's coverage does not define the Material specification.
- Verify current official documentation before giving version-specific API,
  dependency, stability, or library-support advice. References record historical
  verification dates; do not treat those dates as proof of current support.
- Keep M2 and M3 rules distinct. Do not import M3-only tokens into strict M2 work
  or claim one-to-one migration mappings without evidence.
- Reuse semantic tokens. Preserve applicable interaction states, visible focus,
  non-color status cues, and the target platform's reduced-motion behavior.

## Workflow

1. Identify the requested outcome, target surface, Material version, framework,
   and existing component/token vocabulary.
2. Establish information priority and choose components by purpose. For component
   questions, read the matching category below; explain the choice, relevant
   alternatives, states, and accessibility rather than listing every component.
3. Implement the affected layout and states using existing resources. Define new
   token families only when the task requires them. Consider light and dark themes
   together where supported; use Expressive treatment when it aids hierarchy or continuity.
4. Verify the changed behavior and applicable accessibility checks. In this
   repository, use the [development guide](../../../docs/DEVELOPMENT.md#命令与验证)
   for checks and run repository scripts from its root with `pwsh`.
5. Deliver the requested result with relevant validation and any intentional
   deviation from the project's design rules. Use templates only when the user
   needs a specification or audit document.

## References

Load only the references needed for the task:

| Task | Reference |
| --- | --- |
| Color roles, dynamic color, dark themes, contrast | [Color system](references/color-system.md) |
| Component inventory and anatomy across categories | [Component catalog](references/components.md) |
| Buttons, icon buttons, FAB, segmented/split buttons | [Actions](references/components-actions.md) |
| Badges, progress, snackbars, legacy banners | [Communication](references/components-communication.md) |
| Cards, dialogs, sheets, carousel, tooltips, lists/dividers | [Containment](references/components-containment.md) |
| Navigation bars/rails/drawers, app bars, tabs | [Navigation](references/components-navigation.md) |
| Checkbox, radio, switch, chips, sliders, menus, pickers | [Selection](references/components-selection.md) |
| Text fields and search | [Text input](references/components-text-input.md) |
| Token layers, names, and framework mappings | [Design tokens](references/design-tokens.md) |
| Android, Flutter, or web implementation | [Platform guides](references/platform-guides.md) |
| Accessibility review of the affected surface | [Accessibility](references/accessibility.md) |
| Specification or design-audit deliverables | [Output templates](references/output-templates.md) |
| Diagnosing common design mistakes | [Anti-patterns](references/anti-patterns.md) |
| Changing or evaluating this skill | [Evaluation cases](evals/evals.json) |
