---
last_verified: 2026-08-26
scope: "Cross-version Material 2 and Material 3 foundations comparison: color, typography, shape, elevation, motion, layout, and component evolution"
requires_current_verification: true
---

# Material 2 ↔ Material 3 Foundations Comparison

Use this reference when the task compares M2 and M3, audits a mixed-version product,
or plans a migration. It focuses on **design-system semantics**, not on any one library.

## Reading rules

Material guidance contains three different kinds of statements. Keep them separate:

- **Specification fact** — defined by Material documentation or canonical token/API
  references. Example: M3 uses Display / Headline / Title / Body / Label type roles.
- **Platform implementation fact** — true only for a library/API. Example: a Compose
  API may be experimental even when the design guidance is stable.
- **Project heuristic** — a useful default, not a Material requirement. Example:
  preferring a single visually dominant action in a dense form.

Do not turn heuristics into fake specification rules.

## High-level comparison

| Foundation | Material 2 | Material 3 |
|---|---|---|
| Color | Primary/secondary palette plus variants | Role-based color schemes generated from tonal palettes |
| Personalization | Brand themes | Brand themes + dynamic color / Material You |
| Typography | h1–h6, subtitle, body, button, caption, overline | Display, Headline, Title, Body, Label × Large/Medium/Small |
| Shape | Component-oriented shape theming, relatively restrained | Named shape scale; larger and more varied radii; Expressive extends the scale |
| Elevation | Physical depth communicated mainly by shadows | Elevation remains, but color roles and surface-container hierarchy carry more hierarchy |
| Dark theme | Near-black surfaces + elevation overlays | Tonal neutral surfaces + semantic surface/container roles |
| Motion | Duration/easing-based transitions | Standard motion plus newer motion-scheme / physics-based Expressive motion where supported |
| Responsive layout | Responsive/adaptive patterns, including navigation rail | Window size classes and adaptive/canonical layouts are first-class guidance |
| Tokens | Theme attributes and component tokens vary by implementation | Reference → system → component token architecture is central |
| Components | M2 component vocabulary | Renamed, restyled, added, and removed patterns; Expressive updates more components |

## Color: mapping without false equivalence

### M2 model

Typical M2 roles:

- `primary`, `primaryVariant`
- `secondary`, `secondaryVariant`
- `background`, `surface`, `error`
- `onPrimary`, `onSecondary`, `onBackground`, `onSurface`, `onError`

### M3 model

M3 uses semantic role pairs such as:

- `primary` / `onPrimary`
- `primaryContainer` / `onPrimaryContainer`
- `secondary` / `onSecondary`
- `secondaryContainer` / `onSecondaryContainer`
- `tertiary` / `onTertiary`
- `tertiaryContainer` / `onTertiaryContainer`
- `error` / `onError`
- `errorContainer` / `onErrorContainer`
- `surface`, `surfaceDim`, `surfaceBright`
- `surfaceContainerLowest`, `surfaceContainerLow`, `surfaceContainer`,
  `surfaceContainerHigh`, `surfaceContainerHighest`
- `onSurface`, `onSurfaceVariant`, `outline`, `outlineVariant`
- inverse and fixed color roles where the current implementation exposes them

### Migration rule

Do **not** map M2 `primaryVariant` directly to M3 `primaryContainer` by name. They
serve different systems. Start from the brand/source color, generate an M3 scheme,
then remap each UI use case by semantic purpose.

Example:

| Existing use | M2 source | M3 migration question |
|---|---|---|
| Primary CTA fill | `primary` | Is this still the highest-emphasis action? Use the component's M3 container token. |
| Status-bar darker brand shade | `primaryVariant` | Does the platform/component still need a separate role? Avoid inventing one if not. |
| Card background | `surface` | Which surface-container level expresses the intended hierarchy? |
| Secondary accent | `secondary` | Should it remain secondary, become tertiary, or be represented by a container role? |

## Typography

### M2 canonical scale

| M2 style | Typical size/weight | Typical purpose |
|---|---:|---|
| h1 | 96sp Light | Very large display text |
| h2 | 60sp Light | Large display text |
| h3 | 48sp Regular | Display/section title |
| h4 | 34sp Regular | Prominent heading |
| h5 | 24sp Regular | Heading/title |
| h6 | 20sp Medium | Emphasized heading |
| subtitle1 | 16sp Regular | Supporting title/list text |
| subtitle2 | 14sp Medium | Supporting title/list text |
| body1 | 16sp Regular | Primary body |
| body2 | 14sp Regular | Secondary body |
| button | 14sp Medium | Button label; M2 defaults often use uppercase |
| caption | 12sp Regular | Supporting/caption text |
| overline | 10sp Regular | Small overline label; often uppercase |

### M3 standard scale

| M3 style | Size / line height / default weight |
|---|---|
| displayLarge | 57sp / 64sp / 400 |
| displayMedium | 45sp / 52sp / 400 |
| displaySmall | 36sp / 44sp / 400 |
| headlineLarge | 32sp / 40sp / 400 |
| headlineMedium | 28sp / 36sp / 400 |
| headlineSmall | 24sp / 32sp / 400 |
| titleLarge | 22sp / 28sp / 400 |
| titleMedium | 16sp / 24sp / 500 |
| titleSmall | 14sp / 20sp / 500 |
| bodyLarge | 16sp / 24sp / 400 |
| bodyMedium | 14sp / 20sp / 400 |
| bodySmall | 12sp / 16sp / 400 |
| labelLarge | 14sp / 20sp / 500 |
| labelMedium | 12sp / 16sp / 500 |
| labelSmall | 11sp / 16sp / 500 |

Letter spacing and font family are also part of the token. Do not migrate type by
size alone if line height, weight, or tracking changes the hierarchy.

### Approximate migration anchors

These are **starting points**, not one-to-one mappings:

| M2 | Possible M3 starting point | Why it is approximate |
|---|---|---|
| h1 | displayLarge | M3 display sizes are smaller and hierarchy is rebalanced |
| h2 | displayMedium | Not size-equivalent |
| h3 | displayMedium/displaySmall | Depends on information hierarchy |
| h4 | headlineLarge | Similar prominence, different metrics |
| h5 | headlineSmall/titleLarge | Depends on screen vs component title |
| h6 | titleLarge/titleMedium | Depends on hierarchy and density |
| subtitle1 | titleMedium/bodyLarge | Semantics differ |
| subtitle2 | titleSmall/labelLarge | Semantics differ |
| body1 | bodyLarge | Closest semantic match |
| body2 | bodyMedium | Closest semantic match |
| button | labelLarge | Common button-label role |
| caption | bodySmall/labelMedium | Depends on prose vs label semantics |
| overline | labelSmall | Reconsider uppercase styling rather than copying it |

M3 generally uses sentence case for button labels. Do not mechanically uppercase
translated labels when migrating from M2.

## Shape

### M2

M2 supports shape theming, but many components use relatively restrained corners and
implementations often expose small/medium/large component categories rather than a
single universal numeric radius.

### M3 standard

The standard M3 shape scale commonly includes:

- none / rectangle
- extra small — 4dp
- small — 8dp
- medium — 12dp
- large — 16dp
- extra large — 28dp
- full / circle where appropriate

Do not assume every component uses the token whose number visually looks closest.
Use component tokens/defaults.

### Expressive extensions

Recent M3 Expressive implementations add additional shape roles such as increased
large/extra-large and extra-extra-large, plus a broader decorative shape library.
Those APIs and exact values are platform/version sensitive; read `expressive.md`.

## Elevation and surfaces

### M2

M2's depth model is strongly tied to elevation in dp and shadows. In dark theme,
white elevation overlays were used to distinguish elevated surfaces from the
`#121212` base surface.

### M3

M3 still has elevation levels and shadows, but surface hierarchy is more semantic:
use surface/container roles to separate regions instead of inventing darker/lighter
card colors. In current M3 color schemes, `surfaceContainerLowest` through
`surfaceContainerHighest`, plus `surfaceDim` and `surfaceBright`, express surface
hierarchy across themes.

Do not reduce this to “M3 has no shadows” or “M3 elevation is only color.” Both are
incorrect simplifications.

## Motion

### M2

M2 guidance is primarily duration/easing based. Motion communicates hierarchy,
continuity, and causality; it should not exist only as decoration.

### M3 standard

Traditional duration/easing tokens remain useful in specifications and web
implementations. Platform libraries may expose different abstractions.

### M3 Expressive

Recent Compose Material3 exposes `MotionScheme.standard()` and
`MotionScheme.expressive()` with spatial/effects specs. Spatial motion may affect
shape/bounds and can overshoot; effects motion is intended for properties such as
color/alpha that should stay within strict bounds.

Never translate a motion-spec concept into “use a bouncy spring everywhere.”

## Adaptive layout

Material 3's current window width classes are:

| Width class | Range |
|---|---:|
| Compact | < 600dp |
| Medium | 600dp–839dp |
| Expanded | 840dp–1199dp |
| Large | 1200dp–1599dp |
| Extra-large | ≥ 1600dp |

Height is classified separately. Treat the **available app window** as dynamic; do
not equate a class permanently with a device model. Split screen, folding, resizing,
and orientation can change the class while the app runs.

For adaptive work, prefer asking:

1. How many panes are useful at this available width?
2. Does navigation need to move from bar → rail → expanded rail/drawer?
3. Which controls or content can gain space rather than merely scaling up?
4. What happens at transitions between classes?

Do not apply web-framework breakpoints (`sm`, `md`, `lg`) as if they were Material
window classes without an explicit mapping.

## Component evolution: M2 → M3

| Area | M2 | M3 / current direction |
|---|---|---|
| Bottom navigation | Bottom navigation | Navigation bar |
| Navigation rail | Exists in later M2 guidance | Continues in M3; Expressive adds expanded rail patterns |
| Drawers | Modal/persistent drawer patterns | Standard/modal drawers in M3; current Expressive Android guidance moves toward expanded navigation rail |
| Buttons | Contained, outlined, text | Filled, filled tonal, elevated, outlined, text; Expressive adds/updates button groups and split buttons |
| Segmented controls | Implementation-dependent | Segmented buttons are part of M3, not an Expressive-only invention |
| FAB | FAB / extended FAB | Small/default/large/extended variants; Expressive updates behavior/shape and adds related patterns |
| Progress | Linear/circular | Linear/circular; Expressive updates visuals and adds loading-indicator patterns |
| Banner | Present in M2 guidance | Catalog/implementation status differs in current M3 work; verify the target platform and choose snackbar/dialog/inline status by semantics |

## Accessibility: what did not change

Moving to M3 does not remove basic accessibility obligations:

- Provide accessible names and roles.
- Keep logical focus order and visible keyboard focus where keyboard input exists.
- Do not use placeholder text as the only field label.
- Do not communicate errors or selection with color alone.
- Use sufficiently large interaction targets; a component may have a smaller visual
  container while the hit target expands to at least the platform-recommended size.
- Support text scaling and reflow rather than fixing text to pixel-perfect boxes.
- Respect reduced-motion preferences and platform accessibility settings.

For WCAG contrast thresholds, use WCAG definitions (including the large-text weight
criterion) rather than inventing an `sp`-only rule.

## Migration workflow

1. Inventory current M2 theme attributes, tokens, component variants, and custom overrides.
2. Classify each value as semantic intent vs implementation detail.
3. Build an M3 theme from brand/source colors rather than converting hex values role-by-role.
4. Remap typography by content hierarchy and usage semantics.
5. Define M3 surface hierarchy before converting card/sheet/dialog visuals.
6. Convert navigation and layout based on window-size behavior, not device names.
7. Migrate component families while keeping interaction flows stable.
8. Test light/dark, accessibility, localization, text scaling, and all interactive states.
9. Remove old M2 tokens only after no component depends on them.

## Source anchors

- M3: https://m3.material.io/
- M2 archive: https://m2.material.io/
- M2 navigation rail: https://m2.material.io/components/navigation-rail
- Android adaptive window size classes: https://developer.android.com/develop/adaptive-apps/guides/use-window-size-classes
- Compose Material3 API/release notes: https://developer.android.com/jetpack/androidx/releases/compose-material3
