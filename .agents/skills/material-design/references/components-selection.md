---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: selection and choice"
requires_current_verification: true
---

# Material Components — Selection

Choose controls by **selection semantics**, not visual preference.

## Checkbox

**Purpose**: Select zero, one, or multiple independent options; may also represent an
on/off value in a multi-option context.

### Use when

- Multiple options may be selected independently.
- The user accepts an agreement/condition.
- A parent checkbox needs an indeterminate state to summarize mixed child selection.

### Best practices

- Make the label clickable/tappable with the control where platform semantics allow it.
- Keep each option phrased independently; selecting one should not imply another is
  deselected unless the model explicitly works that way.
- Use the indeterminate state for mixed/partial selection, not as a third arbitrary value.

### Anti-patterns

- Checkboxes for a mutually exclusive one-of-N choice: use radio buttons/segmented choice.
- A checkbox with a label written as an action button (“Save now”).
- Color-only checked state with no shape/check state.

Source: https://developer.android.com/develop/ui/compose/components/checkbox

---

## Radio button

**Purpose**: Select one option from a visible mutually exclusive group.

### Use when

- Exactly one option should be chosen from a set.
- Seeing the alternatives at the same time helps comparison/understanding.

### Best practices

- Group radio buttons semantically and expose one selected value.
- Make the row/label part of the activation target when appropriate.
- If the set is large or the alternatives do not need to remain visible, consider a menu,
  picker, or other compact single-select control.

### Anti-patterns

- Allowing multiple radio buttons in one group to be selected.
- Using radio buttons for independent feature toggles.
- Hiding the selected radio state through custom styling.

Sources:
- https://developer.android.com/develop/ui/compose/components/radio-button
- https://m2.material.io/components/radio-buttons

---

## Switch

**Purpose**: Toggle a binary setting/state, generally with an immediate effect.

### Use when

- Enabling/disabling a feature or preference.
- The state can be understood as on/off, checked/unchecked.
- Changing the value can take effect immediately without a separate Save action.

### Best practices

- Label the setting/state rather than labeling the physical action “switch on”.
- The current state must remain visually and programmatically perceivable.
- A settings row may make the entire row toggle the same switch if focus/semantics remain
  coherent and there is no competing row action.

### Anti-patterns

- A switch followed by a mandatory “Save” button when the toggle is presented as immediate.
- Using a switch to choose one of three languages or themes.
- A switch whose on/off consequence is unclear until after activation.

Sources:
- https://developer.android.com/develop/ui/compose/components/switch
- https://m2.material.io/components/switches

---

## Chips

### M3 chip roles

| Type | Purpose |
|---|---|
| Assist | Guide or assist the user during a task; often contextual |
| Filter | Refine content by selectable attributes |
| Input | Represent user-provided information such as a tag/contact; often removable |
| Suggestion | Offer recommendations based on context or user input |

### Best practices

- Use the chip type matching the semantic job; do not choose only by visual style.
- Keep chip labels concise and individually understandable.
- Filter chips can support multiple selected filters; expose selected state clearly.
- Input chips should make removal/edit affordances understandable when supported.
- Suggestion/assist chips should remain contextual rather than becoming a permanent row of
  unrelated navigation destinations.

### Anti-patterns

- Using chips as tiny buttons for every action in the interface.
- Long paragraphs or multi-clause labels in chips.
- A filter chip group where selecting one silently deselects others without a clear
  single-select model.
- Treating M2 Choice chip / Action chip names as if they map 1:1 to M3 component names.

### M2 → M3

M2 documents Input, Choice, Filter, and Action chips. M3 organizes chips as Assist,
Filter, Input, and Suggestion. Preserve the **interaction purpose** during migration rather
than renaming by visual resemblance alone. M2 Choice chips describe single selection among
at least two visible options; in M3, segmented buttons or another single-select pattern may
sometimes express that job more directly.

Sources:
- https://developer.android.com/develop/ui/compose/components/chip
- https://m2.material.io/components/chips

---

## Slider / range slider

**Purpose**: Select a value or range along a continuum.

### Use when

- Relative/approximate adjustment is meaningful: volume, brightness, filter range.
- Range slider is appropriate for minimum/maximum bounds.

### Best practices

- Provide meaningful minimum/maximum semantics and current value.
- Use discrete steps when the domain is discrete and users benefit from constrained values.
- Provide a more exact input method when exact arbitrary numbers matter more than spatial
  adjustment; this is a product heuristic, not a universal Material prohibition.

### Anti-patterns

- A slider for a binary on/off setting.
- A slider where the user must hit an exact value but cannot see or enter it precisely.
- Unlabeled endpoints/value semantics.

Sources:
- https://developer.android.com/develop/ui/compose/components/slider
- https://m2.material.io/components/sliders

---

## Menu

**Purpose**: Present a temporary list of actions or options anchored to a control/context.

### Use when

- Choices should remain hidden until requested.
- The list is compact, scannable, and less prominent than always-visible selection controls.
- Overflow actions or contextual actions do not deserve permanent toolbar space.

### Best practices

- Position the menu relative to its trigger and keep it on-screen.
- Group related items; use dividers only when they clarify groups.
- Long menus should scroll instead of clipping off-screen.
- Use visible radio/check semantics when a menu represents persistent selection, if the
  target Material component/platform pattern supports it.

### Anti-patterns

- Hiding the primary action in an overflow menu.
- A menu containing a full settings page or deep hierarchy.
- Arbitrary fixed viewport-height percentages invented as Material rules.

Sources:
- https://developer.android.com/develop/ui/compose/components/menu
- https://m2.material.io/components/menus

---

## Date picker

**Purpose**: Select a date or date range using calendar and/or text input.

### Current M3 Android patterns

- **Docked**: inline/anchored presentation, useful when a modal interruption would be
  disproportionate and the layout is compact enough to host it.
- **Modal**: focused selection in an overlay/dialog.
- **Modal input**: supports typed date entry in a modal flow.

### Best practices

- Match presentation to the task: quick nearby selection vs focused date task.
- Constrain unavailable dates programmatically and explain why when needed.
- Respect locale, date ordering, calendar conventions, and accessibility semantics.

### Anti-patterns

- Hard-coding MM/DD/YYYY globally.
- Allowing impossible dates and only rejecting them after form submission.
- Forcing calendar navigation across decades when text/year selection would be more usable.

Sources:
- https://developer.android.com/develop/ui/compose/components/datepickers
- https://m2.material.io/components/date-pickers

---

## Time picker

**Purpose**: Select a time with dial or input-oriented controls.

### Best practices

- Choose dial/input form based on available space, precision, and user task.
- Respect 12/24-hour locale/user preference.
- Keep AM/PM and selected hour/minute state programmatically clear.

### Anti-patterns

- Assuming 12-hour time for all locales.
- A dial when the task repeatedly enters exact times from another source and keyboard input
  would be faster.

Sources:
- https://developer.android.com/develop/ui/compose/components/time-pickers
- https://m2.material.io/components/time-pickers
