# Source index and freshness notes

Last reviewed: **2026-08-27**.

This skill is a synthesized reference, not a copy of Microsoft documentation. When guidance conflicts with a newer upstream source, prefer the newer official source for the target platform/library.

## Fluent 2 official foundations

- Color: https://fluent2.microsoft.design/color
- Web color tokens: https://fluent2.microsoft.design/color-tokens/
- Typography: https://fluent2.microsoft.design/typography
- Layout: https://fluent2.microsoft.design/layout
- Shapes: https://fluent2.microsoft.design/shapes
- Elevation: https://fluent2.microsoft.design/elevation
- Motion: https://fluent2.microsoft.design/motion
- Iconography: https://fluent2.microsoft.design/iconography
- Accessibility: https://fluent2.microsoft.design/accessibility
- Design tokens: https://fluent2.microsoft.design/design-tokens
- React components overview: https://fluent2.microsoft.design/components/web/react

## Fluent 2 component guidance used here

- Button: https://fluent2.microsoft.design/components/web/react/core/button/usage
- Field: https://fluent2.microsoft.design/components/web/react/core/field/usage
- Dialog: https://fluent2.microsoft.design/components/web/react/core/dialog/usage
- Tooltip: https://fluent2.microsoft.design/components/web/react/core/tooltip/usage
- Popover: https://fluent2.microsoft.design/components/web/react/core/popover/usage
- Menu: https://fluent2.microsoft.design/components/web/react/core/menu/usage
- Tablist: https://fluent2.microsoft.design/components/web/react/core/tablist/usage
- Toast: https://fluent2.microsoft.design/components/web/react/core/toast/usage
- Toolbar: https://fluent2.microsoft.design/components/web/react/core/toolbar/usage
- List: https://fluent2.microsoft.design/components/web/react/core/list/usage/
- Tree: https://fluent2.microsoft.design/components/web/react/core/tree/usage
- Card: https://fluent2.microsoft.design/components/web/react/core/card/usage

## Windows official guidance

- Windows design overview: https://learn.microsoft.com/en-us/windows/apps/design/
- Windows design guidelines: https://learn.microsoft.com/en-us/windows/apps/design/guidelines-overview
- Windows color: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color
- Windows geometry: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry
- Windows elevation/layering: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/layering
- Windows materials overview: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials
- Mica: https://learn.microsoft.com/en-us/windows/apps/design/style/mica
- Acrylic: https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic
- Windows motion: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/motion
- Timing and easing: https://learn.microsoft.com/en-us/windows/apps/design/motion/timing-and-easing
- Windows typography: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography
- Layout overview: https://learn.microsoft.com/en-us/windows/apps/design/layout/
- Responsive design: https://learn.microsoft.com/en-us/windows/apps/design/layout/responsive-design
- Screen sizes and breakpoints: https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design
- Navigation basics: https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics
- NavigationView: https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview
- Controls and patterns: https://learn.microsoft.com/en-us/windows/apps/design/controls/
- Windows design resources / UI kit: https://learn.microsoft.com/en-us/windows/apps/design/downloads/

## Fluent UI implementation sources

- Fluent UI repository: https://github.com/microsoft/fluentui
- Design-token architecture guidance: https://github.com/microsoft/fluentui/blob/master/docs/architecture/design-tokens.md
- Token types: https://github.com/microsoft/fluentui/blob/master/packages/tokens/src/types.ts
- Component architecture: https://github.com/microsoft/fluentui/blob/master/docs/architecture/component-patterns.md
- React motion package: https://www.npmjs.com/package/@fluentui/react-motion

## Agent Skill format

- Agent Skills specification: https://agentskills.io/specification

## Freshness policy for agents using this skill

Re-check upstream documentation when any of these are true:

- the task asks for the latest/current Fluent behavior,
- a package/framework version materially affects implementation,
- a token/resource/component name cannot be found in the current codebase,
- Microsoft has introduced a new Windows design language/version,
- a target is not covered by these references,
- accessibility requirements depend on a newer WCAG/platform rule.

Prefer, in order:

1. Current Microsoft Fluent 2 / Microsoft Learn documentation.
2. Current official Fluent UI/WinUI source and component docs.
3. Platform accessibility standards (W3C/WAI, Windows accessibility docs).
4. High-quality third-party guidance only when official docs leave a real gap.
