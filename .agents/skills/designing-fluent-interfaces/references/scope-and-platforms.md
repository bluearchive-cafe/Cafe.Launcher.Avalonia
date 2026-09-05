# Scope and platform distinctions

## Fluent 2 is a family, not one pixel-perfect platform skin

Fluent 2 provides cross-product foundations for color, typography, spacing, shape, elevation, motion, iconography, accessibility, and components. Microsoft then adapts those ideas to platform-native expectations.

Do not merge these layers:

1. **Fluent 2 design system** — cross-platform principles and web-centric design tokens/components.
2. **Fluent UI implementation libraries** — for example Fluent UI React v9 and Web Components.
3. **Windows design guidance** — Windows 11 visual language, system materials, effective pixels, WinUI controls, windowing, and OS behaviors.
4. **Product-specific design systems** — Teams, Outlook, Azure, or another product can define additional brand and density rules.

## Platform decision table

| Question | Web / React | Windows / WinUI | Other native platforms |
| --- | --- | --- | --- |
| Primary component source | Fluent UI React v9 / Web Components | WinUI 3 controls | Fluent/native library where available |
| Styling primitive | Fluent tokens + semantic aliases | XAML ThemeResources / control styles / system backdrops | Native tokens/resources |
| Default text | Segoe UI/system font stack | Segoe UI Variable | Native platform font unless product guidance says otherwise |
| Spacing basis | Fluent 4px-oriented ramp | Effective pixels; use control defaults and Windows layout guidance | Native units and platform scaling |
| State colors | Fluent 2 Web aliases | Windows control/theme resources | Platform-native state conventions |
| Depth | Fluent shadow token ramp | Windows elevation + contour/strokes; system controls already encode this | Platform-native elevation |
| Materials | Usually ordinary surfaces; product-specific effects | Mica, Mica Alt, Acrylic, Smoke where appropriate | Native materials where appropriate |
| Responsive behavior | CSS/container layout + Fluent tokens | Window-size/effective-pixel breakpoints and adaptive WinUI layouts | Platform-native responsive/adaptive system |

## Important known differences

### Interaction-state color direction

Fluent 2 Web generally moves neutral component colors darker from rest to hover/pressed/selected states. Fluent 2 explicitly notes that Windows currently treats interaction state color in the opposite direction, with controls becoming lighter as interaction increases.

**Rule:** use the platform's semantic state tokens/resources. Never simulate Windows states by copying Web hex values.

### Elevation

Fluent 2 Web uses paired ambient/key shadows and a token ramp such as shadow 2, 4, 8, 16, 28, and 64. Fluent notes that Windows uses strokes/contours in place of key shadows to outline objects. Windows design guidance also defines its own elevation values for layers, controls, cards, tooltips, flyouts, dialogs, and windows.

**Rule:** do not reproduce Web CSS shadows in WinUI to “look Fluent.” Let standard WinUI controls and ThemeShadow/system styling carry elevation unless a custom surface genuinely needs it.

### Shape

Fluent 2's cross-platform shape system offers a broader radius scale. Windows uses a simpler visual hierarchy in common UI: 4px for ordinary in-page controls and 8px for transient overlays/top-level containers, with square corners where touching geometry requires it.

### Typography

Fluent 2 Web uses Segoe UI as the default design-system typeface, with platform font fallbacks. Windows guidance recommends Segoe UI Variable and relies on Windows scaling/effective pixels. Do not assume Web type ramp names map one-to-one to WinUI TextBlock styles.

### Motion

Fluent 2 describes general transition patterns and choreography. Windows defines specific timing/easing resources and interaction patterns for its platform. Use WinUI animation resources on Windows and Fluent motion tokens/libraries on the web.

## Windows 11 design principles

For Windows targets, use these as a final qualitative filter:

- **Effortless** — direct, focused, easy to understand.
- **Calm** — soft, decluttered, and visually restrained.
- **Personal** — respects user theme, accent, scale, and preferences.
- **Familiar** — follows established Windows behaviors and placements.
- **Complete + Coherent** — components, surfaces, and states feel like one system.

These are principles, not permission to add decorative effects. A plain, usable screen using native controls is more Fluent than a custom glass-heavy screen that breaks platform behavior.
