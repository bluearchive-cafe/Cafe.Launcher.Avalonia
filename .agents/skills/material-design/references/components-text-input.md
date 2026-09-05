---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: text input and search"
requires_current_verification: true
---

# Material Components — Text Input

## Text field

**Purpose**: Let users enter or edit text.

### M3 variants

- **Filled text field**: stronger filled-container treatment.
- **Outlined text field**: explicit outline with lower filled-surface weight.

Both serve the same core text-entry job; choose the variant consistently according to
visual hierarchy, density, background, and product style.

### Labeling

- Every field needs a **persistent, understandable labeling mechanism** that is visible and
  programmatically associated.
- Do not use placeholder text as the only label; placeholders disappear during input and
  are poor substitutes for field identity.
- M2 explicitly allows the integrated floating label to be omitted when a clear adjacent
  or independent label already identifies the field. Therefore “every field must always
  have an internal floating label” is too strong.

### Use when

- Free-form text or a structured string must be entered/edited.
- The domain cannot be represented more efficiently by a selection control/picker.

### Best practices

- Keep labels short enough to remain fully readable; avoid truncating field identity.
- Use helper/supporting text for persistent guidance when needed.
- On error, show specific text describing what is wrong and, where possible, how to fix it;
  color/border alone is insufficient.
- Choose keyboard/input type and autocomplete semantics that match the field.
- Keep filled vs outlined treatment consistent within a form/region; M2 advises against
  casually intermixing them within the same form.
- On wide layouts, do not stretch short-data fields across the entire viewport solely
  because space exists.
- Preserve entered data after validation errors.

### Anti-patterns

- Placeholder-only labels.
- Error communicated only with red outline.
- Clearing all user input after one validation error.
- A text field for a tiny fixed option set better represented by radio/segmented/menu.
- Disabling paste/password managers/autofill without a security requirement.
- A field whose format requirement is only revealed after submission.

### Accessibility

- Associate label, helper/error text, required/invalid state, and any unit/prefix semantics
  programmatically.
- Ensure focus order matches visual order.
- Do not rely on animation/floating-label position alone to communicate state.

Sources:
- https://developer.android.com/develop/ui/compose/text/user-input
- https://developer.android.com/reference/kotlin/androidx/compose/material3/TextField
- https://m2.material.io/components/text-fields

---

## Search bar

**Purpose**: Provide a persistent search field that can expand to suggestions/results.

### Use when

- Search is a primary or high-priority way to navigate/discover content in the app.
- Suggestions or dynamic results improve discovery.

### Best practices

- Use a Search Bar when search is a major focus; otherwise an app-bar search action or a
  contextual text field may be less visually dominant.
- Keep query state stable while suggestions/results update.
- Expose search suggestions/results with logical screen-reader traversal and keyboard focus.
- Offer sort/filter actions only when they are relevant to the results and do not overload
  the field.
- Distinguish placeholder/hint from the actual accessible name of the search control.

### Anti-patterns

- A persistent large search bar in an app where search is a rare tertiary action.
- Treating search suggestions as static navigation without keyboard/focus semantics.
- Clearing the query when the user opens a result and returns, unless that behavior is
  intentionally part of the product flow.

### Platform status note

The current Jetpack Compose `SearchBar` APIs are version-sensitive and have had experimental
/ API-shape changes. Verify the installed Material3 version rather than copying an old
snippet blindly.

Sources:
- https://developer.android.com/develop/ui/compose/components/search-bar
- https://developer.android.com/reference/kotlin/androidx/compose/material3/SearchBar
