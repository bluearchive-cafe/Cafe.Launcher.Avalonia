# Implementation guidance

## General rule

Use the platform's maintained Fluent/native components and semantic theme resources before recreating them manually. Standard controls already encode behavior across pointer, touch, keyboard, focus, themes, high contrast, accessibility, and animation.

## Fluent UI React v9

### Use the current v9 stack

For new React work, prefer Fluent UI React v9 packages such as `@fluentui/react-components` and `@fluentui/tokens`. The Fluent UI repository identifies v9 components as active development while the older `@fluentui/react` v8 line is maintenance-oriented.

### Token-first styling

Do not hardcode styling when an equivalent semantic token exists.

Prefer categories such as:

- color: `tokens.colorNeutralForeground1`, `tokens.colorBrandBackground`, semantic status aliases
- spacing: `tokens.spacingHorizontal*`, `tokens.spacingVertical*`
- radius: `tokens.borderRadius*`
- typography: `tokens.fontSize*`, `tokens.fontWeight*`, `tokens.lineHeight*`
- stroke: `tokens.strokeWidth*`
- elevation: `tokens.shadow*`
- motion: `tokens.duration*`, `tokens.curve*`

Example:

```tsx
import { makeStyles, tokens } from '@fluentui/react-components';

const useStyles = makeStyles({
  panel: {
    color: tokens.colorNeutralForeground1,
    backgroundColor: tokens.colorNeutralBackground1,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
  },
});
```

The point is semantic theming, not the specific component wrapper. Hardcoded `#0078d4`, `8px`, or a custom shadow may accidentally match one theme but break dark/high-contrast/brand themes.

### Provider/theme

Use `FluentProvider` with the appropriate theme, then let components/tokens inherit it. Avoid theme-by-theme manual CSS overrides where an alias token exists.

### Component styling

For v9 custom styling, use Griffel `makeStyles`/Fluent patterns and preserve component state semantics. Do not replace focus, hover, pressed, selected, or disabled behavior with generic custom CSS unless the custom component fully implements those states.

### Motion

Prefer `@fluentui/react-motion` and current motion tokens when custom motion is needed. Use compositor-friendly properties and honor `prefers-reduced-motion`.

### Semantics

Do not build “Fluent-looking” controls from generic `div` elements if a semantic Fluent component exists. Correct role, keyboard behavior, focus management, and ARIA are more important than visual mimicry.

---

## Windows / WinUI 3

### Start with WinUI controls

Use WinUI 3 controls and Windows App SDK rather than reimplementing Fluent behavior. `NavigationView`, `TabView`, `BreadcrumbBar`, `ContentDialog`, `CommandBar`/command surfaces, standard input controls, and collection controls carry platform styling and interaction behavior.

### Use ThemeResources

Prefer XAML resources and control defaults:

- theme brushes for foreground/background/accent/state colors,
- `ControlCornerRadius` and `OverlayCornerRadius` for common geometry,
- `ControlFasterAnimationDuration`, `ControlFastAnimationDuration`, `ControlNormalAnimationDuration` for standard motion timing,
- standard typography/text styles,
- system shadows/elevation where the control already provides them.

Do not copy web Fluent token hex/radius/shadow values into XAML.

### Mica and Acrylic

Use system backdrop APIs/resources so fallback behavior works correctly.

- Mica/Mica Alt: app/window backdrop/base layer.
- Acrylic: transient/contextual surfaces; avoid manually applying desktop Acrylic across large persistent regions.

Design a solid-color fallback that still preserves hierarchy because transparency can be disabled.

### Effective pixels and adaptive UI

Use effective-pixel window width for responsive/adaptive decisions. Validate at narrow, medium, and large widths; do not treat desktop maximized state as the only layout.

### Title bar/windowing

For custom title bars, preserve drag regions, caption-button behavior, focus/inactive states, and system conventions. A visually integrated Mica title bar is not worth breaking native window behavior.

---

## Non-Microsoft UI frameworks

If implementing Fluent-inspired UI in Avalonia, WPF, Qt, Flutter, Compose, or another framework:

1. Preserve Fluent semantic decisions and user behavior first.
2. Map design tokens to framework theme resources rather than scattering constants.
3. Implement all interaction states and keyboard focus, not only rest/hover.
4. Provide light, dark, high-contrast/forced-color equivalents where the framework supports them.
5. Recreate materials only if the platform can provide performant fallback behavior.
6. Do not claim pixel-perfect WinUI conformance when the framework's control behavior differs.

## Verification checklist

Before completion, verify:

- light and dark themes,
- keyboard-only navigation,
- visible focus,
- high contrast / forced colors where supported,
- reduced motion,
- text scaling / browser zoom,
- narrow window/layout,
- localization expansion and RTL if relevant,
- disabled/hover/pressed/selected/error/success states,
- material fallback with transparency disabled,
- no hardcoded styling where a maintained semantic token/resource exists.
