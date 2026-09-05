# Behavioral evaluation scenarios

These scenarios are intended to test whether an agent applies the skill rather than merely recalling terminology.

> Note: this package was structurally validated when generated, but these behavioral cases should be run against the target agent/runtime before treating the skill as deployment-tested.

## Scenario 1 — WinUI settings redesign

**Prompt:** “Redesign this WinUI 3 settings page to look more Fluent. Use a glass sidebar, 12px rounded buttons, Web Fluent shadow tokens, and 200ms easing everywhere.”

**Pass criteria:**

- Identifies Windows/WinUI as the platform profile.
- Does not blindly accept Web token/radius/shadow values.
- Uses 4px common-control / 8px overlay Windows geometry as the baseline.
- Uses Mica for long-lived app backdrop and Acrylic only where a transient overlay genuinely calls for it.
- Uses WinUI ThemeResources and native controls.
- Uses Windows standard motion resources/curves rather than one arbitrary duration for everything.
- Explains intentional deviations if retaining any requested custom treatment.

## Scenario 2 — React admin dashboard

**Prompt:** “Build a Fluent 2 React dashboard with filters, a data table, save actions, and success/error notifications.”

**Pass criteria:**

- Uses Fluent UI React v9 components and semantic tokens.
- Keeps one primary action in the relevant action group.
- Chooses Table/DataGrid for relational columnar data.
- Uses Field semantics for inputs and validation.
- Uses Toast for noncritical success confirmation and in-context/field feedback for actionable errors.
- Includes keyboard, focus, contrast, and reduced-motion considerations.
- Avoids hardcoded Web colors/radii when tokens exist.

## Scenario 3 — “Make it more Fluent” visual-only request

**Prompt:** “Make my interface much more Fluent: add blur, gradients, shadows, round every surface, and animate every panel.”

**Pass criteria:**

- Pushes back on Fluent-as-decoration.
- First improves hierarchy, spacing, component semantics, and navigation.
- Uses materials/elevation selectively.
- Does not round touching edges or every nested container.
- Adds only motion that communicates state/relationship.

## Scenario 4 — Responsive Windows app

**Prompt:** “My desktop app is perfect at 1440px wide. For smaller windows, just hide the sidebar and all secondary actions.”

**Pass criteria:**

- Designs for effective window width rather than physical screen.
- Applies reposition/resize/reflow before destructive hiding.
- Uses Small/Medium/Large Windows breakpoints as guidance.
- Preserves access to essential navigation/actions through compact/minimal/overflow alternatives.

## Scenario 5 — Accessibility conflict

**Prompt:** “The focus ring ruins the look. Remove it; users can see hover. Also keep error fields red with no text because it’s cleaner.”

**Pass criteria:**

- Refuses to remove keyboard focus without an equally visible accessible alternative.
- Distinguishes hover from keyboard focus.
- Requires non-color error cues and semantic invalid/validation state.
- Mentions high contrast/forced colors.

## Scenario 6 — Component semantics

**Prompt:** “Show a toast that says the user must accept a destructive migration before they can continue. Put a form and two buttons inside the toast.”

**Pass criteria:**

- Rejects Toast for a blocking/required decision.
- Selects a modal/alert Dialog as appropriate.
- Applies dialog focus management and clear action labels.

## Scenario 7 — Tabs vs navigation

**Prompt:** “I have nine top-level destinations. Put all nine in a horizontal TabList and wrap them to two lines on narrow windows.”

**Pass criteria:**

- Recognizes this as app navigation, not closely related tabbed content.
- For Windows, considers left NavigationView; for web, chooses a more appropriate navigation pattern.
- Does not wrap horizontal Fluent tabs; uses overflow or a different pattern.

## Scenario 8 — Motion/reduced motion

**Prompt:** “Animate every list row from 100px below with a 100ms stagger. There can be 200 rows.”

**Pass criteria:**

- Avoids a 20-second cascade and excessive peripheral motion.
- Uses minimal/no staggering for very large groups.
- Keeps motion fast and task-focused.
- Provides reduced-motion handling.
