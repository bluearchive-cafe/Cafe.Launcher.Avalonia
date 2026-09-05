---
last_verified: 2026-08-26
scope: "Material Design 2 legacy guidance and M2-to-M3 migration reference"
requires_current_verification: false
---

# Material Design 2 Reference

## When to use M2

- Maintaining an existing M2 app, website, or design system.
- Using a component library that primarily targets M2 (e.g., MUI v5, earlier Angular
  Material versions).
- The user explicitly says "Material Design 2", "M2", "MD2", or "old Material".
- Migration budget is constrained and full M3 adoption is not feasible short-term.

## M2 source status

Material Design 2 is Google's previous-generation specification. It is no longer the
recommended default for new projects. The M2 specification site (material.io) has been
superseded by m3.material.io. M2 components and patterns remain widely deployed in
production — especially via MUI, Angular Material, and legacy MDC-Android projects.

## M2 vs M3: key differences

| Aspect | M2 | M3 |
|--------|----|----|
| Color system | Primary + PrimaryVariant + Secondary + SecondaryVariant | Tonal palette → color roles (primary, primaryContainer, etc.) |
| Surface | Single `surface` color + shadow elevation | Surface container hierarchy (5 levels) + tonal elevation |
| Typography | h1–h6, subtitle1/2, body1/2, button, caption, overline | Display/Headline/Title/Body/Label × Large/Medium/Small |
| Shape | Simpler, mostly component-level corners | System shape scale; M3 Expressive further expands shape roles and morphing |
| Motion | Duration + easing curves | Standard M3 motion plus physics-based motion schemes in M3 Expressive |
| Dynamic color | Not available | Material You dynamic color (Android 12+) |
| Elevation | Shadow-dominant | Shadow + surface tone + borders |
| Dark theme | Dark surface + elevation overlays (white % opacity) | Tonal neutrals + surface container hierarchy |

## M2 color system

### Primary palette

```
primary       — main brand color, app bar, primary buttons
primaryVariant — darker shade of primary, status bar
secondary     — accent color, FAB, selection controls
secondaryVariant — darker shade of secondary

background    — page background
surface       — card, dialog, menu, sheet backgrounds
error         — error states, destructive actions

onPrimary     — text/icons on primary
onSecondary   — text/icons on secondary
onSurface     — text/icons on surface
onBackground  — text/icons on background
onError       — text/icons on error
```

### M2 color usage

```css
:root {
  --mdc-theme-primary: #6200EE;
  --mdc-theme-primary-variant: #3700B3;
  --mdc-theme-secondary: #03DAC6;
  --mdc-theme-secondary-variant: #018786;
  --mdc-theme-background: #FFFFFF;
  --mdc-theme-surface: #FFFFFF;
  --mdc-theme-error: #B00020;
  --mdc-theme-on-primary: #FFFFFF;
  --mdc-theme-on-secondary: #000000;
  --mdc-theme-on-surface: #000000;
  --mdc-theme-on-error: #FFFFFF;
}
```

### M2 dark theme

M2 dark theme uses:
- `background`: `#121212` (near-black)
- `surface`: `#121212` with semi-transparent white overlay for elevation levels
- Elevation expressed through `rgba(255,255,255, N)` overlays (0%–12% based on dp height)

This is fundamentally different from M3's tonal surface container approach.

## M2 typography system

```
h1        — 96sp  Light     — hero headlines
h2        — 60sp  Light     — major section headers
h3        — 48sp  Regular   — section headers
h4        — 34sp  Regular   — sub-section headers
h5        — 24sp  Regular   — card/dialog titles
h6        — 20sp  Medium    — emphasis in body

subtitle1 — 16sp  Regular   — list item primary text
subtitle2 — 14sp  Medium    — list item secondary emphasis

body1     — 16sp  Regular   — body text, paragraphs
body2     — 14sp  Regular   — secondary body text

button    — 14sp  Medium    — button labels (UPPERCASE by convention)
caption   — 12sp  Regular   — helper text, image captions
overline  — 10sp  Regular   — section headers, small labels (UPPERCASE)
```

### M2 typography rules

- Button text is UPPERCASE by convention (this is an M2 default; M3 uses sentence case).
- `subtitle1` and `subtitle2` are the workhorse styles for list content.
- Body text defaults to `body1` (16sp) for reading comfort.

## M2 shape and elevation

### Shape

M2 uses a simpler shape system:
- Small components (chips, buttons): 4dp radius
- Medium components (cards, dialogs, sheets): 4–8dp radius
- No system-wide shape tokens by default; shape is mostly component-specific

### Elevation (shadow-based)

| Level | dp | Use |
|-------|----|-----|
| 0 | 0dp | Flat content |
| 1 | 1dp | Cards, search bar (resting) |
| 2 | 3dp | FAB (resting), raised button (resting) |
| 3 | 4dp | App bar (resting) |
| 4 | 6dp | App bar (scrolled), menu |
| 6 | 8dp | FAB (pressed), raised button (pressed) |
| 8 | 12dp | Bottom navigation |
| 12 | 16dp | Dialog |
| 16 | 24dp | Navigation drawer, modal bottom sheet |

M2 elevation is primarily shadow-based. In the baseline dark theme, light elevation overlays are used to make raised surfaces perceptible because dark-on-dark shadows alone provide weak separation.

## M2 component patterns

### Buttons

| Type | Use |
|------|-----|
| Contained Button | Primary action (equivalent to M3 Filled Button) |
| Outlined Button | Secondary action |
| Text Button | Low-emphasis action |
| FAB | Page-level primary action |
| Icon Button | Toolbar/dense actions |

Note: M2 does not have Filled Tonal, Elevated, or Segmented buttons.
M2 buttons default to UPPERCASE labels.

### Text fields

| Type | Description |
|------|-------------|
| Filled Text Field | Solid fill, more visual weight |
| Outlined Text Field | Border-based, lighter weight |

M2 text fields use a different label animation (label moves up on focus/input).
M3 refined this behavior but the pattern is similar.

### Navigation

| Component | M2 usage |
|-----------|----------|
| Bottom Navigation | Typically 3–5 top-level destinations |
| Navigation Rail | 3–7 top-level destinations on medium/large displays; optional FAB/header |
| Navigation Drawer | Modal or persistent navigation for broader destination sets |
| Tabs | Fixed or scrollable sibling views |
| Top App Bar | Standard or prominent app-level actions/navigation |

**Important correction:** Navigation Rail is part of the archived Material 2 specification.
Do not treat it as an M3-only component. The M2 specification positions it for tablet and
desktop layouts and explicitly describes three to seven primary destinations.

### Dialogs

M2 dialogs share the familiar title/content/action structure, but action count and dismissal behavior depend on the task and platform. M2 commonly uses sharper corners and more shadow-led elevation than M3.

## M2 theming by platform

### Android (MDC / AppCompat)

```xml
<style name="Theme.MyApp" parent="Theme.MaterialComponents.Light.NoActionBar">
    <item name="colorPrimary">@color/primary</item>
    <item name="colorPrimaryVariant">@color/primary_variant</item>
    <item name="colorSecondary">@color/secondary</item>
    <item name="colorSecondaryVariant">@color/secondary_variant</item>
    <item name="colorSurface">@color/surface</item>
    <item name="colorError">@color/error</item>
</style>
```

### MUI v5 (React)

```jsx
const m2Theme = createTheme({
  palette: {
    primary: { main: '#6200EE', dark: '#3700B3' },
    secondary: { main: '#03DAC6', dark: '#018786' },
    background: { default: '#FFFFFF', paper: '#FFFFFF' },
    error: { main: '#B00020' },
  },
  typography: {
    button: { textTransform: 'uppercase' }, // M2 default
  },
});
```

### Angular Material

```scss
$m2-primary: mat.define-palette(mat.$deep-purple-palette);
$m2-accent: mat.define-palette(mat.$teal-palette);
$m2-theme: mat.define-light-theme((
  color: (primary: $m2-primary, accent: $m2-accent)
));
@include mat.all-component-themes($m2-theme);
```

## M2 accessibility checklist

M2 accessibility requirements are similar to M3, but note:

- M2 elevation cannot be perceived in dark theme without additional overrides.
- M2 outlined text fields have lower contrast borders — verify they meet ≥ 3:1.
- M2 button text is UPPERCASE by default, which can reduce readability for some users.
- M2 does not have built-in dynamic color or high-contrast mode settings.

## M2-to-M3 migration cautions

1. **Color roles do not map 1:1.** In particular, do not rename `primaryVariant` to
   `primaryContainer`: the roles have different semantics and tonal construction. Start
   from brand/source colors, then assign M3 semantic roles and verify contrast.
2. **Typography taxonomies are structurally different.** M2 h1–h6/subtitle/body names
   and M3 Display/Headline/Title/Body/Label names describe different systems. Map by
   content hierarchy and measured appearance, not by token-name similarity.
3. **Elevation and surfaces need redesign, not numeric conversion.** M2 shadow levels and
   dark-theme overlays do not translate directly to M3 surface-container roles.
4. **Content style can change.** M2 button capitalization and legacy component copy may
   need content/i18n review when adopting M3 conventions.
5. **Component catalogs overlap but are not identical.** Navigation Rail exists in M2;
   Filled Tonal Button and the M3 Segmented Button are M3-era patterns. M3 Expressive
   then adds or updates additional component families.
6. **Light and dark themes can be migrated incrementally by bounded feature/screen**, but
   do not mix M2 and M3 token semantics inside one component without an explicit bridge.
   Validate both themes at each migration slice.
7. **Library support is implementation-specific and time-sensitive.** Check the current
   library documentation before stating that MUI, Angular Material, Vuetify, Flutter, or
   Android Views supports a particular generation of Material exactly.

### Recommended migration approach

1. Inventory existing M2 semantic roles, component variants, custom overrides, and states.
2. Define an M3 source/seed strategy and generate or author complete light and dark schemes.
3. Build a deliberate mapping table for typography, shape, elevation/surface hierarchy,
   and component variants; record cases that have no 1:1 equivalent.
4. Migrate one bounded component family or screen at a time behind a theme boundary.
5. Test light/dark, high contrast where supported, text scaling, focus/touch states, and
   responsive layouts for each slice.
6. Use visual regression and interaction tests before deleting M2 compatibility tokens.

See `foundations-comparison.md` for the cross-generation mapping table and migration
principles.
