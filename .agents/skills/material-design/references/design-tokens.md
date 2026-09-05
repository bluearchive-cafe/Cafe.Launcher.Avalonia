---
last_verified: 2026-08-26
scope: "Material Design 3 token architecture, canonical role families, and implementation mapping"
requires_current_verification: true
---

# Material Design 3 — Design Token Reference

## Token architecture

Material uses layered tokens so raw visual values are separated from semantic meaning:

```text
Reference tokens (palette/type/shape primitives)
    ↓
System tokens (semantic roles used by a theme)
    ↓
Component tokens (role/state values for one component)
```

Use tokens to preserve semantics across light/dark themes, dynamic color, accessibility
contrast variants, platform implementations, and M2→M3 migration.

## Naming discipline

Material source files and generated artifacts commonly expose names in the `md.ref.*`,
`md.sys.*`, and `md.comp.*` families (or CSS equivalents such as `--md-sys-*`). Exact
component-token names can change by implementation and release.

```text
md.ref.palette.primary40
md.sys.color.primary
md.sys.typescale.title-medium
md.comp.<component>.<element>.<state>
```

Rules:

1. Treat `md.sys.*` names as semantic roles, not aliases for a specific hex/dp value.
2. Do not invent an `md.comp.*` token and call it official. Check the current design kit,
   component spec, or platform library before giving an exact component-token name.
3. Project tokens may wrap Material tokens (`app.color.brandAction`, for example), but
   document the mapping explicitly.
4. A spacing scale is useful, but arbitrary `md.sys.spacing.*` names are not automatically
   canonical Material tokens just because the values are multiples of 4dp.

---

## Color tokens

### Reference palette families

A Material 3 color scheme is derived from tonal palettes with values across tone 0–100.
Conceptually:

```text
md.ref.palette.primary.*
md.ref.palette.secondary.*
md.ref.palette.tertiary.*
md.ref.palette.neutral.*
md.ref.palette.neutral-variant.*
md.ref.palette.error.*
```

Do not use palette tones directly inside normal components when a semantic system color
role exists.

### System color role families

Current M3 implementations include these important role families:

```text
Primary:
  primary, onPrimary, primaryContainer, onPrimaryContainer, inversePrimary
  primaryFixed, primaryFixedDim, onPrimaryFixed, onPrimaryFixedVariant

Secondary:
  secondary, onSecondary, secondaryContainer, onSecondaryContainer
  secondaryFixed, secondaryFixedDim, onSecondaryFixed, onSecondaryFixedVariant

Tertiary:
  tertiary, onTertiary, tertiaryContainer, onTertiaryContainer
  tertiaryFixed, tertiaryFixedDim, onTertiaryFixed, onTertiaryFixedVariant

Error:
  error, onError, errorContainer, onErrorContainer

Surface:
  surface, onSurface, surfaceVariant, onSurfaceVariant
  surfaceDim, surfaceBright
  surfaceContainerLowest, surfaceContainerLow, surfaceContainer,
  surfaceContainerHigh, surfaceContainerHighest
  inverseSurface, inverseOnSurface

Boundary / utility:
  outline, outlineVariant, scrim, shadow
```

**Fixed roles** maintain a comparable tone across light and dark schemes and are useful
when an element should not flip tonal behavior with the rest of the theme. Do not use
fixed roles merely to avoid implementing dark theme correctly.

### CSS role mapping

```css
:root {
  --md-sys-color-primary: #6750a4;
  --md-sys-color-on-primary: #ffffff;
  --md-sys-color-primary-container: #eaddff;
  --md-sys-color-on-primary-container: #21005d;
  --md-sys-color-surface: #fef7ff;
  --md-sys-color-surface-container: #f3edf7;
  --md-sys-color-surface-bright: #fef7ff;
  --md-sys-color-surface-dim: #ded8e1;
  --md-sys-color-outline: #79747e;
}
```

The values above illustrate a baseline purple scheme; they are not universal product
colors. Prefer generated/authored schemes and verify contrast.

---

## Typography tokens

Baseline M3 uses 15 system roles. Each role carries font family, weight, size,
line-height, and tracking rather than size alone.

```text
Display:   displayLarge, displayMedium, displaySmall
Headline:  headlineLarge, headlineMedium, headlineSmall
Title:     titleLarge, titleMedium, titleSmall
Body:      bodyLarge, bodyMedium, bodySmall
Label:     labelLarge, labelMedium, labelSmall
```

Common baseline size/line-height anchors (sp) are:

| Role | Size / line height |
|------|--------------------|
| displayLarge | 57 / 64 |
| displayMedium | 45 / 52 |
| displaySmall | 36 / 44 |
| headlineLarge | 32 / 40 |
| headlineMedium | 28 / 36 |
| headlineSmall | 24 / 32 |
| titleLarge | 22 / 28 |
| titleMedium | 16 / 24 |
| titleSmall | 14 / 20 |
| bodyLarge | 16 / 24 |
| bodyMedium | 14 / 20 |
| bodySmall | 12 / 16 |
| labelLarge | 14 / 20 |
| labelMedium | 12 / 16 |
| labelSmall | 11 / 16 |

M3 Expressive can add emphasized type roles/variants and more flexible typography. Check
current platform APIs/design-kit exports before assuming an exact emphasized token name.

---

## Shape tokens

The baseline M3 shape scale includes:

```text
none
extraSmall
small
medium
large
extraLarge
full
```

Current Compose M3 Expressive extends `Shapes` with roles such as:

```text
largeIncreased
extraLargeIncreased
extraExtraLarge
```

These are **system shape roles**, not a requirement to make every component more rounded.
Component defaults decide which role to consume; product theming may customize values.
Expressive also uses shape contrast and morphing more actively than baseline M3.

---

## Motion tokens and schemes

### Baseline duration/easing vocabulary

Traditional M3 token sets include duration steps (`short*`, `medium*`, `long*`) and
standard/emphasized easing curves. These remain useful for platforms that expose the
classic duration/easing model.

Do not hard-code one duration as a universal rule for every component. Use the component
or motion-system token provided by the current implementation.

### M3 Expressive motion

M3 Expressive introduces physics-oriented motion schemes. Current Jetpack Compose
Material3 exposes both in the **regular** `androidx.compose.material3:material3` artifact:

```kotlin
MotionScheme.standard()
MotionScheme.expressive()
```

A motion scheme supplies animation specs for Material components. Conceptually separate:

- **Spatial motion** — changes bounds, position, or shape; can use more physically
  expressive behavior.
- **Effects motion** — changes properties such as opacity/color without changing bounds.

Use `MaterialTheme.motionScheme` / current theming APIs rather than recreating Material's
component physics from hand-picked springs. Expressive API stability is release-sensitive;
verify the current Compose Material3 release and annotations.

### Reduced motion

Reduced motion does not universally mean “set every duration to 0”. Preserve state change
and causality while reducing or replacing unnecessary travel, scale, parallax, and
spring/shape-morph intensity. Respect the platform's reduced-motion setting.

---

## Spacing and layout tokens

Material layouts commonly use a 4dp-based rhythm, but projects often define their own
semantic spacing scale. If a project needs reusable spacing tokens, prefer project-owned
names rather than presenting an arbitrary scale as canonical Material:

```text
app.spacing.xs = 4dp
app.spacing.sm = 8dp
app.spacing.md = 16dp
app.spacing.lg = 24dp
app.spacing.xl = 32dp
```

For responsive/adaptive layout, use the platform's current window-size/adaptive APIs and
component guidance rather than deriving all layout behavior from spacing tokens alone.

---

## Elevation and surface hierarchy

Common M3 elevation levels are conceptually level 0–5. Elevation may affect shadow,
surface tint/tone, or component-specific tokens depending on implementation. M3 does not
mean “no shadows”; it reduces reliance on shadow alone by giving surfaces semantic roles.

Prefer roles such as `surfaceContainerLow` / `surfaceContainer` /
`surfaceContainerHigh` for hierarchy where the component/spec calls for them.

---

## Component tokens

Component tokens bind system roles to a component element and state. Example concept:

```text
Filled button
  container color        → primary
  label color            → onPrimary
  container shape        → component/system shape role
  hover/focus/pressed     → component-specific state tokens
  disabled container     → component-specific disabled token
  disabled content       → component-specific disabled token
```

Important:

- State opacity is not one universal number across all components.
- Disabled container and disabled content can use different tokens/opacity values.
- Exact `md.comp.*` token names should be copied from current authoritative artifacts,
  not reconstructed from memory.

---

## Platform mapping

Use native semantic APIs where they exist:

| Concept | CSS | Jetpack Compose | Flutter | Other platforms |
|---------|-----|-----------------|---------|-----------------|
| Primary color | `--md-sys-color-primary` | `MaterialTheme.colorScheme.primary` | `Theme.of(context).colorScheme.primary` | Map to a project semantic token |
| Body large | project/M3 CSS typescale variables | `MaterialTheme.typography.bodyLarge` | `Theme.of(context).textTheme.bodyLarge` | Map to project typography style |
| Medium shape | project/M3 shape variable | `MaterialTheme.shapes.medium` | component/theme shape | Map to project semantic shape token |
| Motion scheme | CSS/platform motion tokens | `MaterialTheme.motionScheme` (current M3) | current Flutter APIs | Map to platform reduced-motion-aware specs |

Do **not** write fictional native APIs such as `UIColor.md.primary` unless the project
actually defines that extension. On platforms without an official M3 token API, generate
or maintain a project token layer and document its Material mapping.

---

## Cross-generation note

M2 names such as `primaryVariant`, `subtitle1`, and numeric shadow elevation do not map
one-to-one to M3 system tokens. See `foundations-comparison.md` and `material-2.md` before
creating a migration mapping.

## Verification sources

For time-sensitive API/token details, verify against:

- Material 3: https://m3.material.io/
- Jetpack Compose Material3 API: https://developer.android.com/reference/kotlin/androidx/compose/material3/package-summary
- Material Color Utilities: https://github.com/material-foundation/material-color-utilities
