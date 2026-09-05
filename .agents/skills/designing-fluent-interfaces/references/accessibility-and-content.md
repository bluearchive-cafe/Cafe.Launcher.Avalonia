# Accessibility and content

Accessibility is a system constraint, not a final audit pass. A Fluent design that cannot be used by keyboard, screen reader, high-contrast mode, zoom, touch, or reduced-motion users is incomplete.

## Visual hierarchy and text

- Standard text: target at least **4.5:1** contrast against its background.
- Large text: **3:1** can satisfy WCAG contrast requirements when it meets the relevant size/weight threshold.
- UI icons that communicate meaning should generally achieve at least **3:1** against their background.
- Do not encode meaning only by color; combine color with text, icon, position, or shape.
- Keep heading levels logical and sequential. Visual size does not replace semantic heading structure.
- Avoid all caps for emphasis and avoid justified UI text.

## Focus and keyboard

- Every interactive element needs a visible keyboard focus state.
- Focus order should match visual/task order.
- When opening a modal dialog, move focus inside it and trap it until the dialog closes.
- Return focus to the invoking control when closing a modal/overlay when practical.
- Do not hide focus outlines merely for aesthetics.
- Tooltips/help that appears on hover must also be reachable on keyboard focus.
- Icon-only controls need accessible names.

## Screen readers and semantics

- Use the native semantic element/control whenever possible.
- Labels must be programmatically associated with fields.
- Required and invalid states need semantic equivalents, not just colored borders or asterisks.
- Dynamic feedback may need live-region/status semantics.
- Tabs, trees, menus, toolbars, dialogs, and grids have established accessibility patterns; do not recreate them with generic divs when a platform component exists.

## High contrast / forced colors

Do not assume authored color, gradient, shadow, Acrylic, or Mica will survive high-contrast/forced-colors modes.

- Use system/semantic resources.
- Verify focus and selected states without relying on subtle background shades.
- Ensure boundaries remain understandable when shadows/transparency are removed.
- Avoid hardcoded text colors on system surfaces.

## Reduced motion

- Respect `prefers-reduced-motion` on web and OS animation preferences on native platforms.
- Reduced motion does not necessarily mean “no feedback.” Replace spatial movement with short fades/state changes where appropriate.
- Do not use flashing/jarring movement.
- Keep unrelated background motion out of task-focused UI.

## Touch and pointer

- Visible icons can be smaller than interactive hit targets.
- Ensure touch targets remain comfortably usable; Fluent 2 calls out ~44×44 for iOS/Web and 48×48 for Android.
- Hover-only affordances need touch and keyboard equivalents.
- Do not require precision pointer placement for essential actions.

## Zoom, scaling, and localization

- Let text reflow. Avoid fixed-height text containers that clip at higher text scale/zoom.
- Expect labels to expand significantly after localization.
- Avoid layouts that require English word length to fit.
- Respect RTL reading order; primary action placement and navigation direction can mirror.
- Avoid embedding text in images/icons when it must localize or scale.

## Content design

### Voice

Prefer language that is friendly, helpful, concise, and action-oriented. Lead with the information needed to act.

### Capitalization

Use sentence case for UI labels, menu items, navigation, tabs, headings, tooltips, and buttons unless a product name or language convention requires otherwise.

### Button labels

- Use active verbs and be specific.
- Prefer “Save” to commit changes when autosave is unavailable.
- Use “Cancel” to stop an in-progress task and discard/revert pending changes.
- Use “Close” when closing a surface without affecting task state.
- Avoid “OK” for dismissing errors; “Close” is clearer.
- In multi-step flows use “Next” and “Previous”; use “Done” for final acknowledgement or “Finish” when the final action commits the selected options.

### Error messages

An error message should answer:

1. What happened?
2. What can the user do next?

Avoid generic titles such as “Error” when a more specific statement is possible. Put actionable recovery guidance near the problem.

### Form copy

- Labels name the expected value.
- Placeholder text is optional supplementary hint, never the only instruction.
- Helper text explains format/requirements before failure.
- Validation text explains the current problem and path forward.

### Toast/dialog copy

- Titles carry the main message.
- Body copy adds context; do not restate the title.
- Buttons respond directly to the title/task.
- Keep temporary feedback short enough to scan before it disappears.
