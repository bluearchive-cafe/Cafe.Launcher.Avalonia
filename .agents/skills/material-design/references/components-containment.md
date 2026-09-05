---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: containment and grouped content"
requires_current_verification: true
---

# Material Components — Containment

Containment components group related content, actions, or transient surfaces. Material 3
also treats patterns such as tooltips and sheets as containment surfaces.

## Card

**Purpose**: Group content and actions about a single subject.

### Use when

- A unit of content should read as independent, selectable, comparable, or movable.
- A collection benefits from clear grouping and scanning.
- The card provides meaningful structure, not merely decoration.

### M3 variants

- **Filled**: subtle containment and low separation.
- **Elevated**: greater surface separation through elevation.
- **Outlined**: explicit boundary without relying on elevation.

### Best practices

- Keep one coherent subject per card.
- Make hierarchy clear at a glance: primary content first, supplementary metadata/actions
  subordinate.
- Limit actions to those directly related to the card's subject.
- If the card has a large primary tappable region, keep supplementary action regions
  distinct and avoid overlapping/nested interactive hit regions.
- In M2 mobile guidance, cards generally expand and allow the **screen** to scroll rather
  than creating a small independently scrolling region inside each card.
- Keep repeated cards consistent enough to scan and compare.

### Anti-patterns

- Wrapping every page section in a card with no semantic reason.
- Multiple unrelated subjects in one card.
- A clickable whole card containing overlapping clickable descendants with confusing
  focus/pressed behavior.
- Putting global filter/sort controls inside one card in a collection when they apply to
  the whole collection.
- Flipping a card to reveal content when expansion/navigation would be clearer.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/Card
- https://m2.material.io/components/cards

---

## Dialog

**Purpose**: Interrupt the current flow for important information or a decision/action that
needs focused attention.

### Use when

- The user must acknowledge or decide before the task can safely continue.
- A blocking error needs explanation and an actionable next step.
- A short focused task belongs in a temporary modal surface.

### Best practices

- State the issue/task clearly; avoid vague alarmist titles such as “Warning!” when a
  specific title can explain the situation.
- Label actions by outcome: “Discard”, “Delete file”, “Keep editing” is clearer than
  “Yes/No”.
- Keep choices focused. If the task becomes multi-step, information-dense, or requires
  navigation, consider a full screen/sheet instead.
- Use confirmation based on **risk and reversibility**, not for every destructive-looking
  control. An immediate action + Undo can be less disruptive for low-risk reversible work.
- Let the platform/component manage button layout when space is constrained rather than
  forcing one cross-platform action order.

### Anti-patterns

- Showing a dialog for routine success feedback that a snackbar/inline state can handle.
- A confirmation dialog after nearly every button press.
- “Are you sure?” with Yes / No and no clear consequence.
- A dialog containing a long document or complex navigation tree.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/AlertDialog
- https://m2.material.io/components/dialogs

---

## Bottom sheet

**Purpose**: Present supplementary content or actions from the bottom edge while preserving
context from the underlying screen.

### Use when

- A mobile flow needs a list of actions/options that is richer than a small menu.
- Items need icons/descriptions or more room than a compact popup menu provides.
- A modal sheet should temporarily focus the user while keeping source context visible.

### Best practices

- Keep the sheet's relationship to the invoking content clear.
- Modal sheets block interaction with underlying content until dismissed/actioned; do not
  pretend background controls remain available.
- Make drag/dismiss behavior consistent with the platform and protect unsaved work when
  accidental dismissal would be costly.
- For larger windows, consider whether a side sheet, pane, or non-modal layout uses space
  more effectively.

### Anti-patterns

- Using a bottom sheet for a tiny two-item choice that a menu handles more efficiently.
- Hiding a full application settings hierarchy in a transient sheet.
- Allowing a swipe-to-dismiss gesture to silently destroy unsaved high-value input.

### M2 → M3

Compose migration commonly maps M2 `ModalBottomSheetLayout` to M3 `ModalBottomSheet`.
The exact API is an implementation detail; preserve the interaction job rather than doing
only a class-name replacement.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/ModalBottomSheet
- https://m2.material.io/components/sheets-bottom

---

## Side sheet

**Purpose**: Present supporting content/actions along the side of a wider layout.

### Use when

- Medium/expanded layouts have room for contextual details without replacing primary
  content.
- The task benefits from side-by-side context, inspector-like controls, or secondary panes.

### Anti-patterns

- Forcing a side sheet into a compact window where it crowds the primary task.
- Treating side sheets as identical to navigation drawers; navigation and contextual
  supporting content have different semantics.

Sources:
- https://m3.material.io/components/side-sheets/overview
- https://m2.material.io/components/sheets-side

---

## Carousel (M3)

**Purpose**: Let users quickly browse a horizontally arranged set of visual/content items.

### Use when

- Content is naturally browsed horizontally, e.g. media art, photos, featured items.
- A partial next item or size treatment can communicate that more content exists.

### Best practices

- Keep item content scannable and appropriate for horizontal browsing.
- Choose the Material carousel strategy/variant that matches content sizing rather than
  manually imitating arbitrary peeking behavior.
- Ensure keyboard/focus and screen-reader traversal remain logical where applicable.

### Anti-patterns

- A carousel for essential ordered form steps or content users must compare line-by-line.
- Auto-advancing critical content that moves before the user can read/interact.
- Nesting multiple competing horizontal scrolling regions without a clear interaction need.

Source: https://developer.android.com/reference/kotlin/androidx/compose/material3/carousel/package-summary

---

## Tooltip

**Purpose**: Add contextual explanation to an anchored UI element.

### M3 types

- **Plain tooltip**: brief description of an element or icon-button action.
- **Rich tooltip**: more explanation; may include title, link, or action according to the
  current component API/spec.

### Use when

- An icon or compact element needs supplemental explanation on hover/long press.
- Secondary detail is useful but should not occupy persistent page space.

### Best practices

- Keep plain tooltip copy brief.
- Use rich tooltips only when the extra detail is genuinely helpful.
- Tooltips supplement, not replace, accessible names and visible labels for critical tasks.
- Do not invent one universal delay/duration across all platforms; follow the current
  component/platform behavior and accessibility expectations.

### Anti-patterns

- Putting required form instructions only in a hover tooltip.
- Using tooltips for errors users need to revisit after focus moves away.
- A tooltip that contains a miniature complex form.
- Assuming desktop hover behavior exists on touch-only devices.

Sources:
- https://developer.android.com/develop/ui/compose/components/tooltip
- https://m2.material.io/components/tooltips

---

## List item and divider

**Purpose**: Present repeated rows of related content/actions and group them into scannable
sets.

### Use when

- Items share a common structure: settings, messages, contacts, options, metadata rows.
- Users primarily scan vertically and act on individual rows.

### Best practices

- Keep primary text and metadata hierarchy consistent across the list.
- Make the row's interaction model obvious: navigation row, selection row, or inline
  control row should not feel interchangeable.
- Use dividers only where they clarify grouping/containment; whitespace and alignment may
  provide enough structure.
- Entire-row click/tap targets can improve usability when the row represents one action,
  but do not create conflicting row + child control behavior.

### Anti-patterns

- A divider between every row when spacing/grouping already makes relationships obvious.
- Rows with different alignment/anatomy for equivalent content.
- Making the whole row navigate when a switch/checkbox inside it is the actual action,
  unless the entire row intentionally toggles that same state with correct semantics.

Sources:
- https://m2.material.io/components/lists
- https://m2.material.io/components/dividers
- https://developer.android.com/reference/kotlin/androidx/compose/material3/ListItem

---

## M2 legacy containment patterns

### Backdrop

Backdrop is an M2 pattern with a back layer exposing controls/context behind a front layer.
Do not assume a direct M3 equivalent; for migration, reconsider whether a drawer, sheet,
app bar controls, or adaptive pane better serves the task.

Source: https://m2.material.io/components/backdrop

### Image list / data table

M2 contains explicit Image list and Data table guidance. On M3 projects, component
availability and modern guidance vary by platform/library. Preserve the information
architecture and accessibility requirements, then verify the current target component
rather than claiming these are removed or unchanged across every M3 implementation.
