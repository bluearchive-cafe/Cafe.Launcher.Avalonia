---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: navigation"
requires_current_verification: true
---

# Material Components — Navigation

Navigation components should reflect **destination hierarchy, window size, and task
frequency**, not just available screen width.

## Navigation bar (M3) / bottom navigation (M2)

**Purpose**: Switch among a small set of top-level destinations.

### Use when

- Destinations are top-level and of comparable importance.
- They should remain consistently accessible.
- Current M3 Android guidance: compact layouts with **3–5 destinations**.

### Best practices

- Keep destination labels concise and stable.
- Preserve user state when switching destinations where product behavior expects it.
- Use icons/labels that describe destinations, not actions.
- If the information architecture grows beyond the component's intended destination set,
  restructure or use a drawer/rail rather than squeezing more items in.

### Anti-patterns

- “Back”, “Create”, “Search now”, or other immediate actions as destinations.
- Dynamically changing the set/order of core destinations without strong reason.
- Truncating, shrinking, or wrapping labels until they become hard to scan.
- Treating bottom navigation and bottom app bar as interchangeable; one is navigation,
  the other is an action/navigation container with different purpose.

Sources:
- https://developer.android.com/develop/ui/compose/navigation
- https://m2.material.io/components/bottom-navigation

---

## Navigation rail

**Purpose**: Persistent top-level navigation for wider layouts while preserving content
space.

### Use when

- A tablet/desktop/landscape layout needs persistent top-level destinations.
- Current Android Material guidance commonly uses **3–7 main destinations**.
- A bottom navigation bar would feel stretched or waste horizontal space.

### Best practices

- Keep destinations consistent with other adaptive navigation forms for the same app.
- Use readable labels where the design calls for them; M2 guidance explicitly warns
  against truncating/shrinking labels merely to force a fit.
- Adapt based on current window size, not a hard-coded “tablet device” assumption.

### Anti-patterns

- Claiming Navigation Rail is M3-only: archived M2 Material already includes it.
- A rail full of contextual actions instead of destinations.
- Switching navigation taxonomy when moving between bar/rail/drawer layouts.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/NavigationRail
- https://m2.material.io/components/navigation-rail

---

## Navigation drawer

**Purpose**: Organize a broader set of destinations, account destinations, and discovery
items in a side panel.

### M3 forms

- **Modal drawer** overlays content and is dismissed after navigation/interaction.
- **Standard/permanent drawer** can share space with content on larger layouts.

### Use when

- The app has more destinations or hierarchy than a navigation bar/rail comfortably shows.
- Account switching, product sections, or feature discovery belong in a broader navigation
  surface.
- Larger layouts can benefit from persistent navigation.

### Best practices

- Group related destinations and keep labels explicit.
- Show selected destination state clearly.
- Keep global destinations stable; do not reorder them based on transient content.
- Choose modal vs persistent behavior based on available space and task context.

### Anti-patterns

- Hiding 3–4 high-frequency destinations in a drawer on compact layouts purely for visual
  minimalism.
- Mixing destructive commands and ordinary destinations with identical treatment.
- A drawer that duplicates every action already visible in the app bar.

Sources:
- https://developer.android.com/develop/ui/compose/components/drawer
- https://m2.material.io/components/navigation-drawer

---

## Top app bar

**Purpose**: Provide page identity, navigation affordance, and key page-level actions.

### M3 choices

- Small top app bar: screens with limited navigation/actions.
- Center-aligned: useful when a centered title and limited action set fits the hierarchy;
  current Android guidance illustrates it for screens with a single primary action.
- Medium / Large: create stronger title hierarchy and can participate in scroll/collapse
  behavior.

### Best practices

- Title the current screen/content, not the entire product redundantly on every screen.
- Put only key actions in the app bar; move lower-priority actions to overflow or content.
- Use the navigation icon for a navigation role (back/up/drawer), not a random action.
- Use current platform scroll behaviors instead of manually inventing collapse physics.

### Anti-patterns

- Five or six unlabeled obscure icon actions competing in the app bar.
- A back arrow that performs destructive cancellation without warning or state handling.
- Duplicating the same navigation menu in top and bottom app bars.

### M2 → M3

M3 adds center-aligned/medium/large variants and explicit scroll behavior APIs in Compose.
Do not reduce migration to changing the import; reevaluate hierarchy and scrolling.

Sources:
- https://developer.android.com/develop/ui/compose/components/app-bars
- https://m2.material.io/components/app-bars-top

---

## Bottom app bar

**Purpose**: Provide access to important actions/navigation near the bottom edge; may
coordinate with a FAB depending on the design.

### Use when

- Important actions need ergonomic bottom placement.
- The component fits the app's navigation/action architecture.

### M2-specific guidance

Archived M2 guidance describes bottom app bars for screens with roughly **2–5 actions** and
warns against pairing them with bottom navigation simply because both occupy the bottom
edge. Treat these as M2 component constraints/guidance, not as a universal M3 numeric law.

### Anti-patterns

- A bottom app bar and navigation bar both fighting for the same bottom region without a
  clear architecture.
- Duplicating top app bar navigation/actions in the bottom app bar.
- Covering snackbar/FAB/safe area because insets were ignored.

Sources:
- https://developer.android.com/develop/ui/compose/components/app-bars
- https://m2.material.io/components/app-bars-bottom

---

## Tabs

**Purpose**: Switch among peer sections within the same content context.

### M3 types

- **Primary tabs**: main content destinations in the pane; use when one tab level is needed.
- **Secondary tabs**: establish another level of related content within a content area.

### Best practices

- Tabs should control the region visually associated with them.
- Keep labels concise and parallel.
- Use scrollable tab treatment when the set cannot fit rather than shrinking text beyond
  readability.
- Preserve selected state clearly and ensure keyboard/focus traversal matches visual order.

### Anti-patterns

- Using tabs as a second global navigation system beneath bottom navigation when they
  actually represent the same destinations.
- Stacking independent tab rows directly under each other without clear hierarchy.
- Tabs for commands such as Delete / Export / Save.
- Wrapping or truncating labels until destination meaning is lost.

### M2 note

M2 explicitly warns against attaching tabs to bottom navigation and clarifies that tabs
control the content region beneath/associated with them.

Sources:
- https://developer.android.com/develop/ui/compose/components/tabs
- https://m2.material.io/components/tabs

---

## Adaptive navigation decision

Use destination count + hierarchy + available window space together:

| Situation | Typical Material direction |
|---|---|
| Compact, 3–5 top-level destinations | Navigation bar |
| Medium/wider, 3–7 persistent main destinations | Navigation rail |
| Broader hierarchy / account & feature sections | Navigation drawer |
| Peer views inside a destination | Tabs |

This table is **guidance**, not a requirement to swap components at one magic breakpoint.
Preserve taxonomy and selection state across adaptive transformations.
