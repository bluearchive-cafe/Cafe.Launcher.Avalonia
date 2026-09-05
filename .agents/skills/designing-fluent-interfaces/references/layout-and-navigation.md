# Layout and navigation

## Layout starts with relationships

Fluent layout uses space to communicate grouping, hierarchy, and task flow. Before adding containers, ask whether alignment and proximity already explain the relationship.

### Grouping rules

- Keep tightly related controls/content close together.
- Increase spacing between distinct tasks or sections.
- Use consistent alignment lines to create rhythm.
- Avoid dense “card soup”: not every group needs a bordered/elevated card.
- Reserve the largest surrounding whitespace for the most important regions.

## Grid

A Fluent grid is composed of columns, gutters, and margins.

- A **12-column** grid is a common flexible model, not a mandatory rule.
- Gutters and margins should use the spacing system and can change at breakpoints.
- Choose fixed, stretch, or hybrid behavior based on content—not an arbitrary desktop canvas size.
- Preserve meaningful line lengths and reading order as regions resize.

## Responsive vs adaptive

- **Responsive**: one layout fluidly repositions/resizes/reflows as space changes.
- **Adaptive**: switches to a materially different layout or architecture at a breakpoint.

Use these techniques in increasing order of disruption:

1. **Reposition** — move elements while preserving relationships.
2. **Resize** — let flexible regions grow/shrink.
3. **Reflow** — change rows/columns or wrap content.
4. **Show/hide** — remove lower-priority content or move it behind disclosure.
5. **Re-architect** — switch navigation/layout pattern when the existing one stops working.

Do not hide essential functionality merely to make a narrow screenshot look clean.

## Windows effective-pixel breakpoints

Design to the **app window's available effective width**, not physical monitor size.

| Size class | Window width |
| --- | --- |
| Small | ≤ 640 effective px |
| Medium | 641–1007 effective px |
| Large | ≥ 1008 effective px |

These are design categories, not an instruction to put all responsive logic at exactly two media queries. Components can have their own content-driven thresholds.

## Navigation principles

Navigation should be consistent, simple, and clear. Prefer familiar platform structures over novel navigation chrome.

### NavigationView on Windows

Use `NavigationView` for top-level app navigation when appropriate.

- **Top navigation**: best when there are about **5 or fewer** equally important top-level destinations, when all choices should stay visible, or when icons alone would not identify them well.
- **Left navigation**: best when there are roughly **5–10** top-level destinations or when navigation must remain prominent.
- **LeftCompact**: icons remain visible while labels collapse; useful when space tightens.
- **LeftMinimal**: menu button invokes overlay navigation; useful for narrow windows.
- **Auto**: lets NavigationView adapt among minimal, compact, and expanded modes.

Do not treat NavigationView's back button as an automatic back stack; navigation state still has to be implemented correctly.

### Tabs / Tablist

Use tabs for closely related categories or a small set of frequently switched sibling views. Do not use tabs as a general replacement for top-level app navigation.

- Keep labels brief and parallel.
- Horizontal tabs should not wrap. Use overflow or a different pattern when space is insufficient.
- On small layouts, consider Accordion/Dropdown/other switching patterns if most tabs would disappear into overflow.
- Keep one tab selected by default.

### Breadcrumb

Use breadcrumbs when hierarchy/path matters and users may need to jump back to an ancestor. Do not add a breadcrumb to a shallow two-level experience merely because there is room.

### List/details

Use list/details when users frequently switch among child items while maintaining a parent collection context, such as mail, contacts, or data-entry records. Adapt to a single-column drill-in flow on narrow widths if both panes become unusable.

### Toolbar / command surface

Toolbars should contain frequent actions related to the current task or view.

- Group related commands with spacing/dividers.
- Separate destructive/status-changing actions from routine commands.
- Do not wrap a toolbar to multiple lines; move lower-priority commands to overflow.
- In overflow, provide text labels even if the main toolbar uses icons.
- Allow secondary toolbars to hide when they are not part of the main task.

## Common layout anti-patterns

- Designing only for a 1920×1080 screenshot.
- Fixed-width sidebars plus fixed-width content that cannot shrink.
- Hiding primary actions below a breakpoint with no alternate access.
- Using cards, borders, shadows, and Acrylic simultaneously to distinguish the same grouping.
- Treating every region as equally important by giving all regions equal size/contrast.
- Centering long forms or data-heavy UI where left alignment would scan better.
- Using tab overflow for a navigation taxonomy that should have been a side navigation.
