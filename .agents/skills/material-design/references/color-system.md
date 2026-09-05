---
last_verified: 2026-08-26
scope: "Material Design 3 color system, tonal palettes, dark theme, dynamic color"
requires_current_verification: false
---

# Material Design 3 — Color System Deep Dive

## Table of contents

- [Tonal palette generation](#tonal-palette-generation)
- [Color role assignment](#color-role-assignment)
- [Light theme mapping](#light-theme-mapping)
- [Dark theme mapping](#dark-theme-mapping)
- [Dynamic color (Material You)](#dynamic-color)
- [Contrast requirements](#contrast-requirements)
- [High-contrast mode](#high-contrast-mode)
- [Brand color strategy](#brand-color-strategy)
- [Common mistakes](#common-mistakes)

---

## Tonal palette generation

M3 generates a tonal palette from a seed color using the HCT (Hue-Chroma-Tone) color
space. The palette spans 13 tones (0–100) for each key color.

### Key colors and their roles

| Key Color | Role |
|-----------|------|
| **Primary** | Main brand color. Highest emphasis. |
| **Secondary** | Complementary to primary. Used less prominently. |
| **Tertiary** | Additional accent. Used sparingly for variety. |
| **Neutral** | Backgrounds, surfaces, text, icons. |
| **Neutral Variant** | Subtle variation for outlines, weaker surfaces. |
| **Error** | Destructive actions, error states, validation failures. |

### Generating a tonal palette

Use the [Material Theme Builder](https://m3.material.io/theme-builder) or the
[`material-color-utilities`](https://github.com/material-foundation/material-color-utilities)
library:

```javascript
import { Hct, TonalPalette } from '@material/material-color-utilities';

const seed = Hct.fromInt(0xFF6750A4); // #6750A4
const primaryPalette = TonalPalette.of(seed.hue, seed.chroma);
primaryPalette.get(40);  // Primary tone 40
primaryPalette.get(90);  // Primary tone 90
```

### Tone usage pattern

The tone numbers below describe the **baseline static Material 3 scheme** and are useful
for understanding role relationships. They are not a promise that every generated,
dynamic, fidelity, monochrome, high-contrast, or future scheme will use the same tone
for a role. Prefer semantic roles over hard-coded tone numbers in application code.

Typical baseline anchors include primary tone 40 in light themes and tone 80 in dark
themes, with container/on-container pairs chosen to preserve hierarchy and contrast.

---

## Color role assignment

### Primary group

```
Light theme:                      Dark theme:
  primary          → tone 40        primary          → tone 80
  onPrimary        → tone 100       onPrimary        → tone 20
  primaryContainer → tone 90        primaryContainer → tone 30
  onPrimaryContainer → tone 10      onPrimaryContainer → tone 90
  inversePrimary   → tone 80        inversePrimary   → tone 40

Fixed roles (when a color should keep a comparable tone across light/dark):
  primaryFixed / primaryFixedDim
  onPrimaryFixed / onPrimaryFixedVariant
```

### Secondary group

```
Light theme:                      Dark theme:
  secondary         → tone 40       secondary         → tone 80
  onSecondary       → tone 100      onSecondary       → tone 20
  secondaryContainer → tone 90      secondaryContainer → tone 30
  onSecondaryContainer → tone 10    onSecondaryContainer → tone 90
```

### Tertiary group

Same baseline role pattern as secondary, using the tertiary palette. Current Material 3
color schemes also expose tertiary fixed roles (`tertiaryFixed`, `tertiaryFixedDim`,
`onTertiaryFixed`, `onTertiaryFixedVariant`). Secondary has the corresponding fixed roles.

### Error group

```
Light theme:                      Dark theme:
  error             → tone 40       error             → tone 80
  onError           → tone 100      onError           → tone 20
  errorContainer    → tone 90       errorContainer    → tone 30
  onErrorContainer  → tone 10       onErrorContainer  → tone 90
```

### Surface group

```
Light theme:                      Dark theme:
  surface                   → neutral 98    surface                   → neutral 6
  onSurface                 → neutral 10    onSurface                 → neutral 90
  surfaceVariant            → neutralV 90   surfaceVariant            → neutralV 30
  onSurfaceVariant          → neutralV 30   onSurfaceVariant          → neutralV 80
  surfaceContainerLowest    → neutral 100   surfaceContainerLowest    → neutral 4
  surfaceContainerLow       → neutral 96    surfaceContainerLow       → neutral 10
  surfaceContainer          → neutral 94    surfaceContainer          → neutral 12
  surfaceContainerHigh      → neutral 92    surfaceContainerHigh      → neutral 17
  surfaceContainerHighest   → neutral 90    surfaceContainerHighest   → neutral 22
  surfaceBright             → bright surface role (scheme-defined)
  surfaceDim                → dim surface role (scheme-defined)
  inverseSurface            → neutral 20    inverseSurface            → neutral 90
  inverseOnSurface          → neutral 95    inverseOnSurface          → neutral 20
```

### Outline group

```
Light theme:                      Dark theme:
  outline        → neutralV 50     outline        → neutralV 60
  outlineVariant → neutralV 80     outlineVariant → neutralV 30
```

### Utility

```
  scrim  → neutral 0 (always)
  shadow → neutral 0 (always)
```

---

## Light theme mapping

Full CSS example for a light theme:

```css
:root {
  /* Primary */
  --md-sys-color-primary: #6750A4;
  --md-sys-color-on-primary: #FFFFFF;
  --md-sys-color-primary-container: #EADDFF;
  --md-sys-color-on-primary-container: #21005D;
  /* Fixed roles are useful where the same family should remain stable across schemes. */
  --md-sys-color-primary-fixed: #EADDFF;
  --md-sys-color-primary-fixed-dim: #D0BCFF;
  --md-sys-color-on-primary-fixed: #21005D;
  --md-sys-color-on-primary-fixed-variant: #4F378B;

  /* Secondary */
  --md-sys-color-secondary: #625B71;
  --md-sys-color-on-secondary: #FFFFFF;
  --md-sys-color-secondary-container: #E8DEF8;
  --md-sys-color-on-secondary-container: #1D192B;

  /* Tertiary */
  --md-sys-color-tertiary: #7D5260;
  --md-sys-color-on-tertiary: #FFFFFF;
  --md-sys-color-tertiary-container: #FFD8E4;
  --md-sys-color-on-tertiary-container: #31111D;

  /* Error */
  --md-sys-color-error: #B3261E;
  --md-sys-color-on-error: #FFFFFF;
  --md-sys-color-error-container: #F9DEDC;
  --md-sys-color-on-error-container: #410E0B;

  /* Surface */
  --md-sys-color-surface: #FEF7FF;
  --md-sys-color-on-surface: #1D1B20;
  --md-sys-color-surface-variant: #E7E0EC;
  --md-sys-color-on-surface-variant: #49454F;
  --md-sys-color-surface-container-lowest: #FFFFFF;
  --md-sys-color-surface-container-low: #F7F2FA;
  --md-sys-color-surface-container: #F3EDF7;
  --md-sys-color-surface-container-high: #ECE6F0;
  --md-sys-color-surface-container-highest: #E6E0E9;
  --md-sys-color-surface-bright: #FEF7FF;
  --md-sys-color-surface-dim: #DED8E1;

  /* Outline */
  --md-sys-color-outline: #79747E;
  --md-sys-color-outline-variant: #CAC4D0;

  /* Utility */
  --md-sys-color-scrim: #000000;
  --md-sys-color-shadow: #000000;
  --md-sys-color-inverse-surface: #322F35;
  --md-sys-color-inverse-on-surface: #F5EFF7;
  --md-sys-color-inverse-primary: #D0BCFF;
}
```

---

## Dark theme mapping

```css
@media (prefers-color-scheme: dark) {
  :root {
    /* Primary */
    --md-sys-color-primary: #D0BCFF;
    --md-sys-color-on-primary: #381E72;
    --md-sys-color-primary-container: #4F378B;
    --md-sys-color-on-primary-container: #EADDFF;

    /* Secondary */
    --md-sys-color-secondary: #CCC2DC;
    --md-sys-color-on-secondary: #332D41;
    --md-sys-color-secondary-container: #4A4458;
    --md-sys-color-on-secondary-container: #E8DEF8;

    /* Tertiary */
    --md-sys-color-tertiary: #EFB8C8;
    --md-sys-color-on-tertiary: #492532;
    --md-sys-color-tertiary-container: #633B48;
    --md-sys-color-on-tertiary-container: #FFD8E4;

    /* Error */
    --md-sys-color-error: #F2B8B5;
    --md-sys-color-on-error: #601410;
    --md-sys-color-error-container: #8C1D18;
    --md-sys-color-on-error-container: #F9DEDC;

    /* Surface */
    --md-sys-color-surface: #141218;
    --md-sys-color-on-surface: #E6E0E9;
    --md-sys-color-surface-variant: #49454F;
    --md-sys-color-on-surface-variant: #CAC4D0;
    --md-sys-color-surface-container-lowest: #0F0D13;
    --md-sys-color-surface-container-low: #1D1B20;
    --md-sys-color-surface-container: #211F26;
    --md-sys-color-surface-container-high: #2B2930;
    --md-sys-color-surface-container-highest: #36343B;
    --md-sys-color-surface-bright: #3B383E;
    --md-sys-color-surface-dim: #141218;

    /* Outline */
    --md-sys-color-outline: #938F99;
    --md-sys-color-outline-variant: #49454F;

    /* Utility */
    --md-sys-color-inverse-surface: #E6E0E9;
    --md-sys-color-inverse-on-surface: #322F35;
    --md-sys-color-inverse-primary: #6750A4;
  }
}
```

---

## Dynamic color (Material You)

Dynamic color extracts a source color from the user's wallpaper and generates a full
light + dark color scheme. Available natively on Android 12+.

### When to use dynamic color

Dynamic color is a product decision, not an industry allow/deny list. Use it when user
personalization fits the product and the resulting roles remain legible and semantically
clear. If brand recognition or cross-platform visual parity is important, constrain or
disable dynamic color for those surfaces and test the fallback brand scheme.

### Implementation (Android Compose)

```kotlin
@Composable
fun AppTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    useDynamicColor: Boolean = true,
    content: @Composable () -> Unit,
) {
    val context = LocalContext.current
    val scheme = when {
        useDynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && darkTheme ->
            dynamicDarkColorScheme(context)
        useDynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S ->
            dynamicLightColorScheme(context)
        darkTheme -> AppDarkColorScheme
        else -> AppLightColorScheme
    }

    MaterialTheme(colorScheme = scheme, content = content)
}
```

Dynamic color is available natively on Android 12+; always provide authored light and
dark fallbacks for earlier versions and for users/products that disable personalization.

---

## Contrast requirements

### WCAG AA minimums

| Element | Ratio | Notes |
|---------|-------|-------|
| Body text (< 18pt / 24px) | ≥ 4.5:1 | Applies to most content |
| Large text (≥ 18pt regular or ≥ 14pt bold, approximately) | ≥ 3:1 | WCAG large-scale text threshold |
| UI components / icons | ≥ 3:1 | Buttons, inputs, icons |
| Disabled text | No requirement | But must be distinguishable |

### Checking contrast

Use the Material Theme Builder's built-in contrast checker, browser DevTools, or:

- [WebAIM Contrast Checker](https://webaim.org/resources/contrastchecker/)
- Figma plugins: Stark, Contrast, Able
- Code: `material-color-utilities` contrast API

---

## High-contrast mode

For users who need enhanced contrast (for example low vision or difficult viewing
conditions), change **role contrast/tone relationships**, not saturation by default.

- Prefer platform/design-system contrast controls or a contrast-aware generated scheme.
- Increase foreground/background tonal separation for text, icons, controls, and outlines.
- Keep semantic role relationships intact; do not arbitrarily intensify brand hues.
- Verify both ordinary and high-contrast schemes with automated and human checks.
- Test the platform's high-contrast/accessibility settings where available.

---

## Brand color strategy

### Strong brand identity

1. Use the exact brand color as the seed.
2. Generate the full tonal palette from it.
3. Map to color roles using the standard algorithm.
4. Verify that the generated palette works in both light and dark themes.
5. If contrast is insufficient, adjust the tonal assignments for specific roles.

### Subtle brand presence

1. Use a neutral/near-brand color as the seed.
2. Let the primary be a softer expression of the brand.
3. Reserve brand-intense color for key moments (logo, splash, hero).

---

## Common mistakes

1. **Using raw hex values instead of tokens** — blocks theme switching.
2. **Forcing the same authored role values into light and dark without review** — baseline schemes commonly shift role tones (for example primary 40 → 80), but generated/contrast schemes may differ; verify semantic contrast rather than enforcing one tone pair.
3. **Not testing surface container contrast** — cards and sheets should be distinguishable
   from the page background.
4. **Defaulting every dark surface to pure black** — baseline M3 uses tonal neutrals and
   surface roles; pure black can still be an intentional product/platform choice.
5. **Using shadow as the only dark-theme separation cue** — dark shadows can have weak
   perceptual contrast. Combine appropriate surface roles, outlines, and elevation cues
   according to the component.
6. **Skipping onPrimary contrast check** — text on primary-colored backgrounds must meet
   minimum contrast; this often fails with lighter primary colors.
7. **Mixing color systems** — using M3 roles in some places and hardcoded colors in
   others creates the worst of both worlds.
