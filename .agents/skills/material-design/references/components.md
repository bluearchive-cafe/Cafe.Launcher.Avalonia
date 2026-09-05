---
last_verified: 2026-08-26
scope: "Material Design 3 component catalog with usage rules, anatomy, states, and platform examples"
requires_current_verification: true
---

# Material Design 3 — Component Reference

> This file is the compact catalog/anatomy reference. For detailed version-aware usage,
> when-to-use, alternatives, accessibility, and anti-patterns, load the matching category
> reference: `components-actions.md`, `components-communication.md`,
> `components-containment.md`, `components-navigation.md`, `components-selection.md`, or
> `components-text-input.md`.

## Table of contents

- [Buttons](#buttons)
- [FAB (Floating Action Button)](#fab)
- [Text Fields](#text-fields)
- [Selection Controls](#selection-controls)
- [Chips](#chips)
- [Cards](#cards)
- [Lists](#lists)
- [Navigation](#navigation)
- [Dialogs](#dialogs)
- [Bottom Sheets](#bottom-sheets)
- [Snackbar](#snackbar)
- [Progress Indicators](#progress-indicators)
- [Tabs](#tabs)
- [Menus](#menus)
- [Tooltips](#tooltips)
- [Banners](#banners)
- [Dividers](#dividers)
- [Badges](#badges)
- [Carousel](#carousel)
- [Date & Time Pickers](#date--time-pickers)
- [Search](#search)
- [M3 Expressive new components](#m3-expressive-new-components)
- [Component state checklist](#component-state-checklist)

---

## Buttons

### Types and emphasis levels

| Type | Emphasis | Use case |
|------|----------|----------|
| **Filled Button** | Highest | Primary page action, form submit, "Save", "Buy", "Next" |
| **Filled Tonal Button** | High | Important but not primary, "Add to cart", "Start trial" |
| **Elevated Button** | Medium-high | Action that needs to rise above surrounding content |
| **Outlined Button** | Medium | Secondary action, "Cancel", "Learn more", "View details" |
| **Text Button** | Low | Tertiary action, inline actions, "See all", "Dismiss" |
| **Icon Button** | Variable | Toolbar actions, close, search, menu toggle |
| **Segmented Button** | Equal | Related single- or multi-select options in Material 3; not Expressive-only |
| **Split Button** | Primary + menu | Primary action with related dropdown (M3 Expressive) |

### Button usage rules

1. **Create a clear action hierarchy.** A single dominant action is often easier to scan,
   but Material does not impose a universal “exactly one Filled Button per view” law.
2. **Use concise action labels** that predict the result. Prefer specific wording when it
   reduces ambiguity; do not lengthen every short, already-clear label mechanically.
3. **Destructive actions are semantic, not a fixed visual variant.** Use explicit wording,
   appropriate error/destructive color semantics, undo when feasible, and confirmation
   when the cost/irreversibility justifies interruption.
4. **Follow platform direction/order conventions** for grouped actions, RTL, keyboard, and
   dialog placement rather than hard-coding one cross-platform order.
5. **Loading behavior must preserve context.** Prevent duplicate activation and keep the
   layout stable; whether the label remains, changes, or pairs with an indicator depends
   on the component and task.
6. **Touch targets and visual bounds are different.** On Android/Material touch UIs, aim
   for at least a 48×48dp interactive target even when the visible control is smaller.

### Anatomy

```
[ Icon (optional) ] [ Label text ]    — Filled / Tonal / Elevated / Outlined
[ Label text ]                         — Text Button
[ Icon ]                               — Icon Button
[ Icon ] [ Label ] [ ▼ ]              — Split Button
```

### State layers

Material components use state layers/tokens for applicable hover, focus, pressed, and
dragged states. **Do not apply one global opacity table to every component.** Exact state
colors/opacities and disabled content/container treatment are component-token-specific and
can change by Material generation or implementation. Use the current component spec or
platform defaults; preserve a visible focus indicator on keyboard-capable platforms.

---

## FAB

### Sizes

| Size | Height | Use |
|------|--------|-----|
| Small | 40dp | Compact surfaces, paired with lists |
| Default | 56dp | Standard page-level action |
| Large | 96dp | High-emphasis FAB size in Material 3; not Expressive-only |

### Variants

| Variant | Description |
|---------|-------------|
| **FAB** | Circular, icon only |
| **Small FAB** | Compact circular |
| **Large FAB** | Oversized, icon or icon+label |
| **Extended FAB** | Icon + label, rectangular with full rounding |

### FAB usage rules

1. Prefer a FAB for a clear, high-frequency primary action. Multiple competing FABs are
   usually a sign to use another action grouping pattern; treat this as a hierarchy
   heuristic rather than a universal numeric prohibition.
2. Use for: compose email, create document, start recording, new task.
3. Destructive, navigation, overflow, and low-priority utility actions are generally poor
   FAB candidates; use a control whose semantics match the job.
4. Place the FAB according to the current component/layout spec and safe-area/inset rules;
   do not obscure critical content or navigation.
5. A FAB may respond to scrolling when that improves content visibility. Use the current
   Material/platform motion behavior rather than prescribing one universal transition.
6. FAB Menu / expressive action groups: keep the revealed action set small and scannable; verify the current component API rather than enforcing a fixed item count.

---

## Text Fields

### Types

| Type | Description |
|------|-------------|
| **Filled Text Field** | Solid background, more visual weight |
| **Outlined Text Field** | Border-based, lighter visual weight |

### Anatomy

```
[Leading icon?] [Label] [Input text] [Trailing icon?] [Counter?]
[Supporting text (helper/error)]                    [Character counter]
```

### Input variants

- **Single-line**: Default. For short inputs (name, email, search).
- **Multi-line**: For descriptions/messages. A resize handle is a web/desktop implementation option, not a universal Material requirement.
- **With prefix/suffix**: Currency symbol, unit, domain suffix.
- **With counter**: Show remaining characters for constrained inputs.
- **Password**: Provide show/hide toggle as trailing icon.

### States

| State | Visual |
|-------|--------|
| Enabled | Default outline/fill, label above or within |
| Focused | Accent-colored outline/label, visible cursor |
| Hovered | Subtle state layer on container |
| Disabled | Reduced opacity, non-interactive |
| Error | Red outline, red label, error icon, error text below |
| Success | Product-specific confirmation if useful; not a mandatory Material text-field state |
| Read-only | No cursor, distinguishable from editable |

### Text field rules

1. **Provide a persistent labeling mechanism** — do not use placeholder alone as the label.
   M2 explicitly allows an adjacent/independent label instead of an integrated floating label.
2. **Error text should identify the problem and, where useful, how to fix it**; color-only
   error treatment is insufficient.
3. **Communicate required/optional status consistently** using visible and programmatic
   semantics appropriate to the form; an asterisk is one option, not a universal requirement.
4. **Pre-format input where helpful** (auto-spacing credit card numbers, phone formatting)
   but never interfere with user typing or paste.
5. **Choose validation timing by task.** Avoid noisy errors before the user can reasonably
   complete input; blur, submit, or carefully designed live validation can all be valid.
6. **On submit**, make errors discoverable and move focus/scroll appropriately for the
   platform and accessibility model.

---

## Selection Controls

| Control | Use | Behavior |
|---------|-----|----------|
| **Checkbox** | Multi-select from list | Checked/unchecked, supports indeterminate parent |
| **Radio Button** | Single select from a reasonably short visible set | One selected at a time |
| **Switch** | Instant on/off toggle | Changes take effect immediately |
| **Slider** | Value in a continuous range | Discrete or continuous, with label |
| **Segmented Button** | Small set of closely related options | Single- or multi-select behavior depending on component mode |

### Selection rules

1. **Switch**: Use for settings that take effect immediately (Wi-Fi, Bluetooth, Dark mode).
   NOT for form fields that require submission.
2. **Checkbox**: Use for multi-select contexts and form submissions. Always pair with a label.
3. **Radio**: Best when showing all options aids comparison. For a long or space-constrained set, consider a menu, dropdown, autocomplete, or search-based picker.
4. **Slider**: Label the range endpoints. Show current value for precise adjustments.
5. **Segmented Button**: Use for view toggles (List / Grid), time ranges (Day / Week / Month).

---

## Chips

| Type | Use | Behavior |
|------|-----|----------|
| **Assist Chip** | Contextual assistance during a task | Tappable, triggers the assistance/action |
| **Filter Chip** | Toggle filter state | Selectable, shows check when active |
| **Input Chip** | Represent entered data (email recipients, tags) | Deletable via trailing icon |
| **Suggestion Chip** | Present dynamic suggestions | Tappable, populates input |

### Chip rules

1. Chips are compact — keep labels short enough to remain scannable; do not enforce a fixed word count when clarity needs more context.
2. Filter chips must clearly indicate selected vs unselected state.
3. Input chips need a clear delete/remove action.
4. Avoid nested chip interaction. Use a layout that preserves clear individual hit targets.
5. Group chips by a coherent semantic job. Mixing types can be valid when the relationship is
   clear, but do not make visually similar chips behave unpredictably.

---

## Cards

### Types

| Type | Description |
|------|-------------|
| **Elevated Card** | Subtle shadow, sits above surface |
| **Filled Card** | Tonal fill, lighter weight |
| **Outlined Card** | Border only, minimal weight |

### Card anatomy

```
┌──────────────────────────────┐
│ [Media / Image (optional)]   │
│                              │
│ [Header / Title]             │
│ [Subtitle / Supporting text] │
│ [Body / Description]         │
│                              │
│ [Buttons / Actions] [Icons]  │
└──────────────────────────────┘
```

### Card rules

1. Cards group related content and actions. One card = one conceptual unit.
2. A card may have a primary tappable region and supplementary actions, but keep their
   hit regions and semantics distinct; avoid overlapping or nested interactive regions that
   produce competing pressed/focus behavior.
3. Keep repeated card structure consistent enough to support scanning and comparison.
4. Don't overload a card with unrelated actions; move complex action sets to a menu, detail surface, or dedicated workflow.
5. If the card itself is the primary action, make supplementary controls clearly secondary
   and independently focusable where applicable.

---

## Lists

### List item types

| Type | Lines | Use |
|------|-------|-----|
| Single-line | 1 | Contacts, settings items |
| Two-line | 2 | Emails, messages with preview |
| Three-line | 3 | Detailed list items (use sparingly) |

### List item anatomy

```
[Leading icon/avatar]  [Primary text]
                       [Secondary text]
                       [Tertiary text / metadata]   [Trailing icon/action]
```

### List rules

1. Maintain consistent structure within a list — all items should have the same anatomy.
2. Primary text is the identifier; secondary text adds context; metadata (time, count)
   goes on the trailing side.
3. For long or hard-to-scan lists, add search, filtering, grouping, indexing, or progressive loading when those aids match the task; item count alone is not the criterion.
4. Use dividers sparingly between items — whitespace is usually sufficient.
5. Provide pressed and focus states for tappable list items.

---

## Navigation

### Components by adaptive width

Navigation choice depends on destination count, window size, posture, and product
structure. A practical Android-adaptive starting point is:

| Window width | Common primary-nav direction |
|--------------|------------------------------|
| Compact `<600dp` | Navigation bar or other compact pattern |
| Medium `600–839dp` | Navigation rail often fits well |
| Expanded `840–1199dp` | Rail / expanded navigation treatment / pane-aware layout |
| Large `1200–1599dp` | Expanded navigation + multi-pane content where useful |
| Extra-large `≥1600dp` | Desktop-scale navigation/content arrangement |

These are layout classes, not mandatory component mappings. Re-evaluate at runtime.

### Navigation Bar (Bottom)

- Keep the destination set focused; the navigation bar is intended for a small set of top-level destinations.
- Each destination: icon + label text.
- Active destination: filled icon variant + highlighted label + indicator pill.
- M3 Expressive: flexible height, horizontal items on medium windows, `secondary` color
  for active label.

### Navigation Rail

- Side navigation for top-level destinations on wider windows.
- Historical M2 guidance explicitly supports **3–7 destinations**, so do not describe Navigation Rail as M3-only or enforce a 3–5 limit across generations.
- M3/M3 Expressive implementations may offer collapsed/expanded rail treatments; verify the current platform component.
- A FAB/header can be integrated where the current component/spec supports it.

### Navigation Drawer

- Modal (overlay) or Standard (persistent aside).
- Useful when the navigation hierarchy or secondary destinations need more room than a bar/rail; do not choose it solely from an item-count threshold.
- Use section headers to group related destinations.
- Active destination must be clearly indicated.

### Top App Bar

- Center-aligned or small (left-aligned title).
- Contains navigation/title/actions according to app-bar variant and available width; move overflow actions when space or priority requires it.
- Scroll behavior: can collapse, pin, or scroll away.
- Medium and Large variants for prominent page headers with hero image potential.

### Tabs

- Primary tabs: top-level content switching (fixed or scrollable).
- Secondary tabs: within a section or view.
- Fixed tabs suit a small, stable sibling set; scrollable tabs suit wider/variable labels or more siblings.
- Tab content should be at the same conceptual level — don't nest hierarchy in tabs.

### Search

- Search Bar: expandable inline bar, transitions to full search view.
- Search View: full-screen search with suggestions, history, and results.

---

## Dialogs

### Types

| Type | Use |
|------|-----|
| **Basic Dialog** | Confirmation, alert, short input |
| **Full-screen Dialog** | Complex form, multi-step creation flow (mobile) |

### Anatomy

```
┌──────────────────────────────────┐
│ [Icon (optional)]                │
│ Title                            │
│ Content / description            │
│                                  │
│        [Cancel]  [Confirm]       │
└──────────────────────────────────┘
```

### Dialog rules

1. Use a specific title that makes the decision understandable without generic
   “Are you sure?” wording when a concrete consequence can be named.
2. Keep dialogs focused on a bounded decision/task. Longer content can scroll when the
   component/platform supports it, but complex workflows usually deserve a larger surface.
3. Keep actions few and clearly prioritized; exact count/order follows the component and
   platform conventions rather than a universal 2–3-button law.
4. For destructive decisions, use explicit consequence-oriented wording and appropriate
   semantic styling; confirmation is warranted by risk/irreversibility, not simply by the
   presence of a Delete button.
5. Provide an understandable dismissal/cancel path unless the workflow is intentionally
   blocking and the platform pattern says otherwise. Scrim-tap and close icons are
   implementation choices, not mandatory in every dialog.
6. Avoid turning a dialog into a page-sized application surface; choose full-screen/page
   layouts for multi-step or information-dense work.

---

## Bottom Sheets

### Types

| Type | Use |
|------|-----|
| **Standard Bottom Sheet** | Persistent, coexists with main content |
| **Modal Bottom Sheet** | Overlays content, requires dismissal |

### Usage rules

1. Use for contextual actions related to current page (share, filter, sort, more options).
2. Include a drag handle when it is part of the platform component and helps communicate
   drag affordance; it is not a cross-platform requirement for every modal sheet.
3. Sheet content may scroll when necessary. Keep the task coherent and ensure dismissal,
   focus, and nested-scroll behavior remain usable.
4. Prefer a larger page/pane for complex or multi-step work unless the sheet pattern is
   explicitly designed to support it.
5. On large screens, consider replacing with a Side Sheet or Detail Panel.

---

## Snackbar

### Usage rules

1. Keep snackbar copy brief; Material snackbars can accommodate short one- or two-line
   messages depending on component/platform, usually with at most one concise action.
2. Duration must account for message length, actionability, accessibility settings, and
   platform APIs. Do not hard-code one universal 4–10 second policy into the design spec.
3. Place it where it does not cover navigation, input, or other critical controls.
4. Avoid stacking competing snackbars; queue/replace behavior is a product implementation
   decision that should prevent missed feedback.
5. Use a more persistent/interruptive pattern when the message requires sustained
   attention or blocks safe continuation.
6. **Version note:** archived M2 guidance generally describes self-dismissing snackbars;
   current M3 Compose guidance says a snackbar with an action should not time out/self-dismiss
   before the user has a fair opportunity to act. Do not apply M2 timing as a universal M3 rule.

### Anatomy

```
┌──────────────────────────────────────────┐
│ [Icon?]  Message text here     [Action]  │
└──────────────────────────────────────────┘
```

---

## Progress Indicators

| Type | Use |
|------|-----|
| **Linear Determinate** | Measurable progress (file upload, processing progress) |
| **Linear Indeterminate** | Ongoing work where completion fraction is unknown |
| **Circular Determinate** | Measurable progress in a compact region |
| **Circular Indeterminate** | Ongoing work with unknown completion in a compact region |
| **Loading Indicator** | Brief inline loading (M3 Expressive) |

### Rules

1. Use determinate whenever you can calculate progress. Users prefer knowing.
2. As waits become noticeable or consequential, add context (what is happening) and, when feasible, progress, cancellation, or recovery. Do not use a fixed time threshold as a specification rule.
3. Skeleton screens are for content loading (articles, feeds, dashboards). Progress
   indicators are for actions/processes.
4. Indeterminate loading needs eventual success, failure, retry, or cancellation behavior appropriate to the operation; do not allow an unrecoverable endless wait.

---

## Tabs

### Types

| Type | Description |
|------|-------------|
| **Primary (fixed)** | Equal/fitted tabs for a small sibling set |
| **Primary (scrollable)** | Variable-width tabs for wider labels or a larger sibling set |
| **Secondary** | Within a content section, lighter weight |

### Rules

1. Keep tab labels concise and clearly descriptive; do not enforce a fixed word count.
2. Selected state must be perceivable using the current component tokens/indicator treatment;
   do not hard-code one indicator shape across Material generations.
3. Tab content should be at the same hierarchy level.
4. Swipe/drag between tabs is appropriate when tabs are paired with a pager pattern that supports it; do not add the gesture to every tab implementation by default.
5. Tabs can coexist with global navigation when they organize a subordinate content region;
   do not use them to duplicate the same top-level destinations.
6. If tabs become difficult to scan, navigate, localize, or fit, reconsider the information architecture or use another navigation pattern; avoid a fixed numeric cutoff.

---

## Menus

### Types

| Type | Use |
|------|-----|
| **Dropdown Menu** | List of actions from a trigger button |
| **Context Menu** | Right-click/long-press actions on a specific item |
| **Exposed Dropdown** | Menu that shows selected item, like a select |

### Rules

1. Menu items: short label + optional shortcut hint + optional leading icon/trailing text.
2. Group related items; use dividers between groups.
3. Group destructive actions so their consequence is clear; exact ordering/dividers follow
   platform and product conventions.
4. Constrain/scroll menus to the available viewport and preserve reachable items; there is
   no universal 60%-of-viewport Material rule.
5. Position the menu relative to its anchor and available space using the platform's
   placement/collision behavior.

---

## Tooltips

### Types

| Type | Use |
|------|-----|
| **Plain Tooltip** | Short text label for unlabeled icon buttons |
| **Rich Tooltip** | Title + description, for more context |

### Rules

1. Trigger according to the platform component (for example hover/focus on pointer/keyboard
   systems and long-press where supported). Do not invent a universal 200ms delay.
2. Let the platform/component position the tooltip to avoid clipping and obscuring the
   anchor or pointer target.
3. Keep plain tooltip text concise; use a richer help surface if substantial explanation is
   necessary.
4. Do not put essential information only in a tooltip — touch, keyboard, and assistive
   technology users must be able to discover the same meaning.

---

## Banners (legacy / implementation-dependent)

Banner is well-defined in older Material guidance and remains available in some
implementations, but do not assume it is part of the current M3 core component catalog on
every platform. For strict M3 work, verify the current catalog and consider an inline
message, snackbar, dialog, or platform-specific banner alternative.

### Use when

- A system-wide or page-level message that needs attention but doesn't block usage.
- Persistent until dismissed or resolved.
- Examples: "You're offline", "New version available", "Storage full".

### Rules

1. Place at top of page, below top app bar, pushing content down.
2. Avoid simultaneous competing banners in the same region.
3. Keep actions few and scannable; follow the platform implementation rather than a fixed maximum.
4. Don't use for: critical blocking errors (Dialog), brief confirmations (Snackbar).

---

## Dividers

### Types

| Type | Use |
|------|-----|
| **Full-bleed** | Separate content sections |
| **Inset** | Separate related items within a section (indented to align with text) |
| **Middle Inset** | Separate items but not spanning icon area |

### Rules

1. Default to whitespace. Add dividers only when grouping needs explicit reinforcement.
2. Use full-bleed between sections, inset between items within a section.
3. Outlined cards and surface-based grouping often eliminate the need for dividers.

---

## Badges

### Types

| Type | Use |
|------|-----|
| **Small Badge** | 6dp dot, notification indicator |
| **Large Badge** | Number or short text, notification count |

### Rules

1. Keep badge content compact; use an abbreviated overflow representation when a large count no longer fits or communicates usefully.
2. Position: top-trailing corner of icon or avatar.
3. Avoid over-badging a view; signal value falls when nearly everything is marked. This is a hierarchy heuristic, not a fixed count rule.

---

## Carousel

### Use for

- Browsing collections of images or cards.
- Hero content showcases.
- Curated visual/content browsing where horizontal exploration is appropriate.

### Rules

1. Make additional content discoverable through the selected Material carousel strategy and
   platform affordances; do not require one universal cue such as dots.
2. Support the platform-appropriate scrolling, focus, and accessibility interaction model.
3. Keep the collection navigable and performant; very large collections may need search, categories, pagination, or a different browsing pattern rather than a fixed item cutoff.
4. Avoid automatic movement for task-critical content. If content moves automatically, meet
   applicable accessibility controls for pausing/stopping and sufficient reading time.

---

## Date & Time Pickers

### Date Picker

- **Docked**: Inline calendar, good for forms.
- **Modal**: Overlay calendar, good for quick date selection.
- Support range selection (date range picker).

### Time Picker

- **Dial**: Clock-face interaction, more visual.
- **Input**: Text field with time format, more precise.

### Rules

1. Show today's date clearly highlighted.
2. On keyboard-capable platforms, provide efficient keyboard entry/navigation where the picker implementation supports it; ensure assistive-technology operation everywhere.
3. Validate dates (no Feb 30, no past dates for bookings, etc.).
4. Localize: date format, first day of week, calendar system.

---

## Search

### Modes

| Mode | Description |
|------|-------------|
| **Search Bar** | Collapsed bar that expands into search view |
| **Search View** | Full-screen search with history, suggestions, results |
| **Persistent Search** | Always-visible search field in toolbar |

### Rules

1. Provide recent searches and suggestions before the user types.
2. If results update while typing, debounce/throttle according to backend cost and perceived latency; do not treat 300ms as a Material specification constant.
3. Empty search state: "No results for [query]. Try [suggestion]."
4. Voice input is optional and platform/product-dependent, not a Material requirement.

---

## M3 Expressive component updates

M3 Expressive is an expansion/evolution of Material 3. The current Material site calls
out **14 new and updated components**; prominent examples include new toolbars, Split
Button, Button Groups, and updated Progress Indicators. Treat the exact set and API names
as time-sensitive and verify current design/platform docs.

| Example | Status / interpretation |
|---------|-------------------------|
| Toolbars | New expressive component family/treatments |
| Split Button | New expressive component; current platform API naming can change |
| Button Groups | New expressive grouping/interaction treatment |
| Progress Indicators | Existing family with expressive updates |
| Loading Indicator | Expressive loading treatment where supported |
| Navigation Rail variants | Updated/expanded treatment on current Android Material |

**Do not misclassify existing M3 components**: Segmented Button and Large FAB are not
Expressive-only inventions, and Navigation Rail already existed in Material 2.

---

## Component state checklist

Design and test **applicable** states rather than forcing every state onto every component:

```text
[ ] Enabled/default
[ ] Hovered        — pointer-capable platforms/components only
[ ] Focused        — keyboard/accessibility focus where applicable
[ ] Pressed        — press/touch feedback
[ ] Selected/checked/activated — for stateful controls
[ ] Disabled       — only when disabling is the right interaction model
[ ] Loading        — only for operations with an in-progress state
[ ] Error/invalid  — validation/destructive semantics where relevant
[ ] Empty/no data  — content containers when relevant
```

For each applicable state:
- preserve semantics and a discoverable interaction affordance;
- test light/dark and contrast variants;
- avoid relying on color alone;
- use the platform/component tokens rather than one global opacity recipe.
