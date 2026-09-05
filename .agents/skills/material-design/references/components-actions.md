---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: actions"
requires_current_verification: true
---

# Material Components — Actions

Use this reference when choosing or auditing controls that **initiate actions**. For exact
visual tokens and implementation APIs, verify the current Material specification and the
target platform library.

## How to read these rules

- **Spec**: directly supported by Google Material / Android guidance.
- **Version note**: M2 and M3 differ; do not merge the rules blindly.
- **Heuristic**: project-level advice that supports the spec but is not a Material law.

Never turn a heuristic into a universal “must”.

## Button

**Purpose**: Trigger a clearly defined action.

### Use when

- The action should be explicit and discoverable: Save, Submit, Continue, Add to cart.
- The user must understand the result before activating it.
- A text label communicates the action better than an icon alone.

### Choose the M3 variant by emphasis

| Variant | Typical role |
|---|---|
| Filled | High-emphasis primary action, e.g. Save / Submit |
| Filled tonal | Significant action with less visual dominance than filled |
| Elevated | Important action that benefits from stronger surface separation |
| Outlined | Medium-emphasis action; important but not primary |
| Text | Lowest-emphasis action, often in cards/dialogs or compact contexts |

### Best practices

- Use an **action-oriented label** that predicts the result.
- Establish visual hierarchy. M2 explicitly recommends a layout with one **prominent**
  button so other actions read as less important; this does **not** mean every screen must
  contain exactly one Filled Button.
- Keep the label scannable and avoid wrapping a button label when a shorter accurate label
  is possible.
- If an icon is used, it should reinforce the same action rather than add a second meaning.
- Preserve layout stability during loading and prevent duplicate activation.
- Destructive actions should communicate consequence through wording, semantics, color,
  undo/confirmation where appropriate, and surrounding context. Material does not define
  one universal destructive button variant for every flow.

### Anti-patterns

- Two or more visually dominant actions competing for the same decision.
- Generic labels such as “Yes”, “OK”, or “Submit” when an outcome-specific label such as
  “Discard changes” or “Send request” would materially reduce ambiguity.
- Using a button as decoration or navigation when a link/tab/navigation destination is the
  clearer semantic control.
- Stacking multiple icons in a button or using an icon whose meaning conflicts with label.
- Disabling the only path forward without explaining the unmet requirement.

### M2 → M3

- M2 commonly describes contained / outlined / text buttons; M3 exposes a more explicit
  emphasis ladder including Filled tonal and Elevated.
- M2 examples often use uppercase labels in languages where capitalization applies; do
  not carry capitalization forward as a universal M3 content rule across languages.

### Accessibility

- Provide an accessible name equal to or clearer than the visible label.
- Preserve keyboard focus and button semantics on keyboard-capable platforms.
- On Android/touch Material surfaces, keep an effective touch target around 48×48dp even
  when the visible button/icon is smaller.

Sources:
- https://developer.android.com/develop/ui/compose/components/button
- https://m2.material.io/components/buttons

---

## Icon button

**Purpose**: Trigger a familiar action in compact space using a single icon.

### Use when

- The action has a widely understood symbol: close, favorite, overflow, search.
- The surrounding context makes the action unambiguous.
- Toolbars, app bars, image actions, or dense utility areas need compact controls.

### Best practices

- Choose a familiar symbol; if a label would be clearer, use a labeled button instead.
- Plain icon buttons are low-emphasis; filled/tonal/outlined variants can increase
  prominence or separation where the current M3 component set supports them.
- Give icon-only controls a programmatic accessible name.
- A plain tooltip can supplement an unfamiliar icon on hover/long press, but the tooltip
  must not be the control's only accessible name.

### Anti-patterns

- Using several obscure icon-only actions that require memorization.
- Treating a 24dp icon as a 24dp touch target.
- Relying on color alone to distinguish selected/destructive state.
- Using an image/illustration as an icon button without button semantics.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/IconButton
- https://developer.android.com/develop/ui/compose/graphics/images/material

---

## Floating action button (FAB)

**Purpose**: Make a high-priority, frequent action immediately available above content.

### Use when

- There is a clear positive action central to the current screen, such as Compose, Create,
  Add, or Start recording.
- The action benefits from remaining visible while content scrolls.

### Best practices

- Tie the FAB to the screen's content and task; it should not feel globally unrelated.
- Prefer one clearly dominant FAB action. If several actions need equal prominence, use a
  different action grouping pattern.
- Use Extended FAB when the label materially improves clarity or when the UI benefits from
  a more descriptive primary action.
- Account for bottom app bars, navigation, safe areas, keyboards, and scrolling content so
  the FAB does not obscure essential UI.

### Anti-patterns

- Delete, back, settings, filter, overflow, or other destructive/meta actions as the FAB.
- A FAB on every screen simply for visual consistency.
- Multiple competing FABs with no clear hierarchy.
- Hiding content or navigation beneath the FAB without inset/padding handling.

### M2 → M3

- The core purpose remains a prominent primary action.
- M3 expands size/color/shape choices and M3 Expressive may further update FAB behavior;
  Large FAB itself is not Expressive-only.

Sources:
- https://m2.material.io/components/buttons-floating-action-button
- https://developer.android.com/reference/kotlin/androidx/compose/material3/FloatingActionButton

---

## Segmented button

**Purpose**: Present a small group of closely related choices in one visual control.

### Use when

- Switching among related views, sorting modes, or display options.
- A small mutually exclusive choice set should remain visible instead of hiding in a menu.
- The platform/component variant explicitly supports multi-select and that behavior is
  understandable for the use case.

### Best practices

- Keep choices short, parallel, and mutually understandable as a group.
- For single-select variants, exactly one segment should express the selected option when
  the domain requires one active value.
- Use icons only when they improve recognition; do not make every segment icon-only if the
  symbols are ambiguous.

### Anti-patterns

- A long or highly variable set of choices that forces cramped labels.
- Using segmented buttons for unrelated actions such as Save / Delete / Share.
- Treating segmented buttons as M3 Expressive-only. They are part of standard M3.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/SingleChoiceSegmentedButtonRow
- https://developer.android.com/reference/kotlin/androidx/compose/material3/SegmentedButton

---

## Split button and button groups (M3 Expressive)

**Purpose**: Group related actions while preserving a dominant action or coordinated action
set where the current M3 Expressive implementation supports these components.

### Use when

- One action is the default but closely related alternatives need quick access.
- Related actions benefit from a visually cohesive group.

### Best practices

- Keep the primary action predictable; the menu side should expose genuinely related
  alternatives, not unrelated overflow.
- Verify current platform availability and API stability before specifying these as a
  cross-platform requirement.

### Anti-patterns

- Replacing every button + menu combination with a split button.
- Assuming M3 Expressive components exist identically on Android, Flutter, web, and all
  third-party Material libraries.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/package-summary
- https://m3.material.io/
