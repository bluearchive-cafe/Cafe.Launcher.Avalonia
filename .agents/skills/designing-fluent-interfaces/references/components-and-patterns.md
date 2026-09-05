# Components and patterns

Use this reference to select the right control/pattern. Exact APIs vary by platform; preserve the semantics even when component names differ.

## Actions

### Button

Use a button to trigger an action, not to navigate to another location (use a Link for navigation).

- Keep **one primary button per local layout/action group** for the most important action.
- If more than two actions have equal priority, use neutral styling rather than making multiple “primary” buttons.
- Use outline/subtle/transparent appearances for dense sets of minor actions.
- Place the primary action first in reading order (left in LTR, right in RTL) unless the platform pattern dictates otherwise.
- Labels should describe the next action, usually with a verb; sentence case, no terminal punctuation.

Variants:

- **Split button** — one dominant action plus related alternatives; do not duplicate the dominant action in the menu.
- **Menu button** — opens a menu; no immediate primary action.
- **Compound button** — action plus useful explanatory description.
- **Toggle button** — on/off state, most often in toolbars. For settings-style immediate binary choices, prefer a Switch.

### Toolbar

Use for frequent actions tied to the current view/task. Group related commands, move overflow into a menu, and keep destructive actions separated from routine ones.

## Menus and selection

### Menu

Use a menu for hidden actions, commands, or navigation. Do **not** use it as a form input when Select/Dropdown/Combobox better represents choosing a value.

- Put frequent actions early and dangerous actions late/separated.
- Avoid deep submenu nesting.
- Keep labels short; action items use verbs, option/settings items use nouns/short phrases.
- Secondary text in menu items should normally be reserved for keyboard shortcuts.

### Dropdown / Select / Combobox

Use these when the user is choosing a value rather than invoking an immediate command.

- Select/Dropdown: choose from known options.
- Combobox: use when search/filter/freeform behavior is appropriate.
- Use a clear visible label; keep options grammatically parallel.

### Checkbox / Radio / Switch

- **Checkbox** — independent yes/no choices; multiple can be selected.
- **Radio group** — exactly one option among mutually exclusive choices.
- **Switch** — immediate binary setting/state; label the setting, not the action “turn on/off.”

Do not use a Switch when the change should be committed later with Save; a checkbox/radio form control is usually clearer.

## Forms

### Field

Treat label + control + helper/validation as one semantic unit.

- Top-aligned labels are the default and scan well.
- Horizontal labels are acceptable for consistent, space-constrained forms but can reduce readability.
- Never put essential instructions only in placeholder text.
- Use helper text for accepted values/format; use validation text to explain how to fix an invalid entry.
- Required state must be available to assistive technology, not only shown with a red asterisk.

### Input vs Textarea

- Input: short single-line freeform text.
- Textarea: multi-line text.

Do not use placeholder copy as a substitute for a label.

## Feedback and status

### Toast

Use for **useful, relevant, non-critical** feedback that can be temporary.

Good uses: confirmation, progress, communication updates.

Do not use a toast when the user must act before continuing, when an error needs field-level correction, or when the message is critical. Use Dialog, Field validation, or MessageBar instead.

- Non-actionable toasts may dismiss automatically; Fluent guidance uses ~7 seconds for standard timed dismissal.
- Progress toasts should be determinate when trustworthy progress is known; otherwise use an indeterminate indicator. Do not show spinner and progress bar together.
- Keep toaster location predictable and avoid blocking main content.
- Keep at most about four visible toasts in one toaster, with consistent spacing.

### MessageBar

Use persistent/in-context feedback that applies to a page, tab, card, or form.

- Place it near the scope it describes.
- On forms with multiple errors, a summary MessageBar can receive focus after submit, while individual fields retain inline validation.

### ProgressBar / Spinner

- Determinate ProgressBar when completion proportion is meaningful.
- Spinner/indeterminate progress when duration/proportion is unknown.
- Do not use both to communicate the same operation.

## Overlays and contextual surfaces

### Tooltip

Use for short, nonessential **plain text** information attached to a target.

- Trigger on hover and keyboard focus.
- Do not use for system feedback or complex formatted content.
- Do not repeat already visible labels.
- Disabled controls can use a tooltip to explain why the action is unavailable.
- Associate tooltip content with the target via accessibility semantics (`aria-describedby` on web).

### Popover

Use for nonessential contextual information that can contain structured or interactive content without blocking the page. Prefer Tooltip for plain text and Dialog for more complex/blocking tasks.

### Dialog

Use for important focused tasks, confirmation, or decisions that justify interruption.

- **Modal** — blocks underlying page for a focused task.
- **Non-modal** — helper surface while the underlying content remains usable.
- **Alert dialog** — high-priority decision, such as destructive or potential-loss situations; use sparingly.

Rules:

- Do not nest dialogs.
- Keep footer actions limited and focused (Fluent layouts support up to about three actions).
- On open, move focus into the dialog; modal/alert dialogs trap focus.
- On close, restore focus to the invoking control.
- Titles must say what is happening/being asked, not generic labels like “Error.”

### Drawer / side panel

Use when secondary work/content should remain connected to the current context. Keep body content scannable and avoid putting long legal/essay-like content in drawers.

## Navigation and information organization

### Tablist

Use for closely related categories or sibling views, not arbitrary app-level destinations. Keep labels short and parallel; use overflow or another switching pattern when narrow widths make tabs undiscoverable.

### List

Use for a vertical collection of independent, similar items. For column relationships, use Table/DataGrid; for nested hierarchy, use Tree.

### Table / DataGrid

Use when rows/columns communicate relationships and comparison. Choose DataGrid for richer interactive data operations (sorting, selection, resizing, editing) when the platform supports them.

### Tree

Use for hierarchical nested data such as folders or nested categories. If the content is not hierarchical, use List/Table. If the goal is simply show/hide sections, use Accordion.

### Card

Use cards for compact, self-contained, action-oriented information. Do not card-wrap every section of a page. Selectable cards require clear selection/focus semantics; hover alone is not a sufficient signifier.

## Component anti-patterns

- Button used as a link or link styled as a button without correct semantics.
- Multiple primary buttons fighting for attention.
- Tooltip containing forms, critical errors, or long formatted help.
- Toast used for a required decision.
- Dialog opened from another dialog.
- Menu used as a value-entry form with many nested choices.
- Placeholder-only fields.
- Switch used for a setting that does not apply immediately.
- List used for tabular comparison.
- Accordion used to represent a true hierarchy that needs Tree semantics.
