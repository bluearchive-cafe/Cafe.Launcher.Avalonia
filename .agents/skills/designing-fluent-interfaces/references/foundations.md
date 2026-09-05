# Visual foundations

## 1. Color

### Fluent 2 palette roles

Fluent 2 organizes color into three broad palettes:

- **Neutral** — surfaces, text, strokes, layout structure, and most states.
- **Shared** — reusable cross-product colors, including semantic status colors.
- **Brand** — product identity and high-value emphasis.

Use color to express hierarchy and meaning. Most of the interface should remain neutral so that brand, semantic, and interactive colors retain signal value.

### Semantic color rules

- Reserve success, warning, danger/error, and informational colors for meaning, not decoration.
- Pair status color with text, iconography, shape, or other non-color cues.
- Use semantic alias tokens instead of fixed palette swatches when a token exists.
- Test every theme/state combination, not only light-mode rest state.
- Avoid large high-saturation brand surfaces unless the product's own brand system requires them.

### Interaction states

Web Fluent components have semantic rest, hover, pressed, selected, disabled, and focus tokens. In general, Web states become darker through interaction, while Fluent explicitly calls out Windows as an exception where controls can become lighter.

For focus, do not fake focus with a hover color. Use an explicit focus indicator/stroke that remains visible against neighboring colors.

### Windows color

Windows supports light and dark themes and user accent colors. Use accent sparingly for important interactive elements and state. Let system theme resources handle contrast and user personalization whenever possible.

Never use color as the only differentiator for state. High contrast / forced colors can replace your authored palette entirely.

---

## 2. Typography

### Typeface

- Fluent Web: use the default Fluent/system font stack; Segoe UI is Microsoft's signature typeface.
- Windows: prefer **Segoe UI Variable** and standard WinUI typography styles.
- macOS/iOS: preserve native San Francisco-family conventions unless product guidance requires otherwise.
- Android: preserve native Roboto/system typography unless product guidance requires otherwise.

Avoid mixing multiple UI typefaces merely to create hierarchy. Hierarchy should normally come from size, weight, spacing, and semantic role.

### Fluent 2 Web type ramp

| Role | Weight | Size / line height |
| --- | --- | --- |
| Caption 2 | Regular | 10 / 14 px |
| Caption 1 | Regular | 12 / 16 px |
| Body 1 | Regular | 14 / 20 px |
| Subtitle 2 | Semibold | 16 / 22 px |
| Subtitle 1 | Semibold | 20 / 26 px |
| Title 3 | Semibold | 24 / 32 px |
| Title 2 | Semibold | 28 / 36 px |
| Title 1 | Semibold | 32 / 40 px |
| Large Title | Semibold | 40 / 52 px |
| Display | Semibold | 68 / 92 px |

Strong/stronger variants exist for several caption/body/subtitle roles. Prefer semantic presets/tokens rather than rebuilding the ramp manually.

### Fluent 2 Windows type ramp

| Role | Weight | Size / line height |
| --- | --- | --- |
| Caption | Regular small | 12 / 16 px |
| Body | Regular | 14 / 20 px |
| Body Strong | Semibold text | 14 / 20 px |
| Body Large | Regular | 18 / 24 px |
| Subtitle | Semibold display | 20 / 28 px |
| Title | Semibold display | 28 / 36 px |
| Large Title | Semibold display | 40 / 52 px |
| Display | Semibold display | 68 / 92 px |

On Windows, prefer platform resources/styles rather than hardcoding this table into XAML.

### Text styling rules

- Use sentence case; avoid all caps as emphasis.
- Align body text to the reading direction. Avoid justified UI text.
- Center only short, intentionally prominent content.
- Use a clear, logical heading hierarchy; do not choose heading sizes only for aesthetics.
- Standard text should normally reach at least 4.5:1 contrast; large text can use 3:1 under WCAG criteria.

---

## 3. Spacing and density

Fluent's global spacing ramp is based primarily on a 4px system, with 2, 6, and 10 values included to accommodate icon alignment and component details.

Common global values:

`0, 2, 4, 6, 8, 10, 12, 16, 20, 24, 28, 32, 36, 40, 48, 52, 56`

Use spacing semantically:

- Small internal gaps imply strong relationship.
- Larger gaps divide groups and sections.
- White space can create hierarchy without adding cards/dividers.
- Do not force every gap to be identical if icon optical alignment or target size requires an adjustment.
- In responsive layouts, spacing can change with available size; consistency means preserving rhythm, not freezing one number.

Touch targets: Fluent 2 layout guidance calls out approximately **44×44** for iOS/Web touch targets and **48×48** for Android. Do not confuse visible icon size with hit-target size.

---

## 4. Shape and stroke

### Fluent 2 general shape tokens

| Radius | Typical role |
| --- | --- |
| 0 | connected/tab/navigation edges |
| 2px | small badges / compact elements |
| 4px | ordinary buttons and dropdown-like controls |
| 8px | larger controls |
| 12px | large floating surfaces such as popovers/sheets |
| 50% | circular person/avatar shapes |

Default rectangular components commonly use 4px. Smaller-than-32px shapes can use 2px; larger surfaces can use 8px or 12px.

### Windows geometry

Windows common guidance is intentionally simpler:

- **4px** — in-page controls, list backplates, bars, and Tooltip.
- **8px** — top-level containers, flyouts, dialogs, TeachingTip, transient overlay UI.
- **0px** — touching straight edges, snapped/maximized window contexts, or geometry that would otherwise create awkward gaps.

WinUI exposes `ControlCornerRadius` and `OverlayCornerRadius`; prefer these resources over repeated custom values.

### Stroke

Fluent general stroke tokens use approximately 1, 2, 3, and 4px on web (mobile scales heavier at larger stroke tokens). Stroke weight should scale with visual size. Use strokes to define boundaries only when fill/spacing alone does not provide enough separation.

---

## 5. Elevation and layering

### Fluent 2 Web elevation

The shadow ramp includes `shadow2`, `shadow4`, `shadow8`, `shadow16`, `shadow28`, and `shadow64`. Larger/softer shadows imply greater distance from the base surface.

Examples from the system:

- Low values: cards, raised items, toolbars/tooltips.
- Mid values: callouts and hover cards.
- High values: side navigation, bottom sheets, panels, dialogs.

Use brand shadow tokens on colored surfaces rather than applying the neutral shadow formula blindly.

### Windows elevation

Windows combines shadow and a 1px contour/stroke. Current guidance uses these conceptual elevation values:

| Surface | Elevation |
| --- | ---: |
| Layer | 1 |
| Control | 2 |
| Card | 8 |
| Tooltip | 16 |
| Flyout | 32 |
| Window / Dialog | 128 |

Pressed controls can drop from elevation 2 to 1. Standard WinUI controls already encode these relationships.

Windows commonly uses a two-layer app model:

1. **Base layer** — app foundation, navigation, menus, commands.
2. **Content layer** — the central task/content, continuous or segmented into cards.

Do not create extra elevation levels merely to make the interface “3D.”

---

## 6. Materials

### Mica / Mica Alt

Use on Windows as a long-lived app backdrop/base layer. Mica is opaque and incorporates theme/wallpaper coloration efficiently. It can communicate active/inactive window state and should be most visible around the title bar/base layer.

Mica Alt has stronger tinting and can support stronger hierarchy, such as tabbed title-bar scenarios.

### Acrylic

Use Acrylic primarily for transient/contextual Windows surfaces such as light-dismiss flyouts, menus, or overlay navigation panes. Avoid:

- large desktop Acrylic backgrounds,
- multiple adjacent Acrylic panes,
- stacking Acrylic surfaces,
- accent-colored body text on Acrylic without verified contrast.

Transparency can be disabled by user settings, battery saver, high-contrast mode, or hardware/platform fallback. The design must remain coherent when Acrylic becomes a solid fallback.

### Smoke

Use transparent/dimming overlays to establish modal focus where the platform/component applies them. Do not manually dim unrelated areas as decoration.

---

## 7. Iconography

Use Fluent system icons for actions, navigation, and status where available.

- **Regular** icons work well for available actions/wayfinding.
- **Filled** icons add weight for selected states or small high-emphasis moments.
- 12px system icons are suitable for information but are generally too small as standalone interactive targets.
- Keep icon and hit-target size separate.
- Use literal, recognizable metaphors. Avoid decorative ambiguity.
- If adding color to a system icon, prefer one solid color and verify contrast.
- Do not recolor Microsoft product-launch icons.
- For icon-only interactive controls, provide accessible names and usually tooltips.
