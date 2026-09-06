# Cafe Launcher Design System — AI Agent Guide v3

> **Audience:** AI coding agents, design agents, maintainers, reviewers  
> **Scope:** Cafe Launcher for Avalonia  
> **Role:** Agent-facing governance layer  
> **Status:** Secondary guidance; does not replace accepted repository specifications or ADRs  
> **Provenance:** Repository-adopted edition of the external "AI Agent Guide v2". v3 adds numeric accessibility baselines, an M3 default-pairing note, and repository anchors, and merges duplicated naming-governance sections. No accepted design decision was changed.

---

# 0. Purpose

This document tells AI Agents **how to reason about Cafe Launcher UI work**.

It does not create a second independent design specification.

The authoritative relationship is:

```text
Accepted ADRs (docs/design/adr/)
      +
docs/design/design-system-spec.md
      +
repository implementation contracts
      ↓
this Agent Guide
      ↓
AI design / implementation decisions
```

If this document conflicts with an accepted ADR, the design-system spec, or an explicit repository contract, the existing repository decision wins.

This guide interprets repository governance. It does not replace it, and it is not a second source of truth.

Agents MUST NOT reinterpret this guide as permission to silently replace accepted design decisions.

---

# 1. Core Design Direction

Cafe Launcher is governed by its own product design system:

## Cafe Design System

Its current design direction is:

```text
Cafe product ownership
        │
        ├── M3-derived color system
        ├── selected M3 visual semantics
        ├── Fluent desktop layout principles
        ├── Fluent spatial/elevation reasoning
        ├── Fluent-derived motion
        └── Avalonia FluentTheme implementation base
```

A concise statement is:

> **Cafe owns the product.  
> M3 primarily supplies color and selected visual semantics.  
> Fluent primarily supplies desktop spatial, interaction, and motion principles.  
> Accepted Cafe Launcher ADRs decide the actual implementation.**

Cafe Launcher MUST NOT be treated as:

- a pure Material Design application;
- a pure Fluent Design application;
- a visual mixture where components independently choose one design system.

Material and Fluent are **reference systems**, not competing product identities.

The repository already embodies a hybrid:

```text
Avalonia FluentTheme
        +
Launcher design tokens
        +
M3-derived dynamic color
        +
Cafe-owned product components
        +
Fluent-derived motion
```

The goal is not to replace this with another visual language, but to formalize its responsibilities:

```text
Before

"M3-oriented application
 using FluentTheme
 plus selected Fluent motion"

        ↓

Governed model

"Cafe-owned design system
 using M3 color semantics,
 Fluent desktop behavior,
 and repository ADRs
 as implementation authority."
```

---

# 2. Decision Authority

When an Agent needs to make a UI decision, use this order:

```text
1. Accessibility and platform safety gates
2. Explicit product requirement
3. Accepted Cafe Launcher ADR
4. docs/design/design-system-spec.md
5. Existing Cafe component / pattern contract
6. Fluent desktop guidance
7. Material 3 guidance
8. Avalonia implementation guidance
```

## 2.1 Accessibility is a gate

Accessibility is not merely another design preference.

A design MUST pass relevant accessibility requirements before visual preference is considered.

This includes, where applicable:

- contrast;
- visible keyboard focus;
- logical keyboard navigation;
- automation/accessibility naming;
- reduced motion;
- non-color state communication;
- target size;
- High Contrast behavior;
- localization resilience.

Numeric baselines (WCAG AA):

| Element | Minimum contrast |
| --- | --- |
| Normal text (below ~18pt / 24px) | 4.5 : 1 |
| Large text (≥ 18pt regular or ≥ 14pt bold) | 3 : 1 |
| UI components and icons (borders, focus indicators, glyphs) | 3 : 1 |
| Disabled text | No fixed ratio, but it MUST remain distinguishable |

Target size: where touch is a supported input scenario, keep the hit target at the platform baseline (48px Material/Android; 44px Fluent web/iOS guidance) even if the visual container is smaller. Desktop pointer-and-keyboard density is governed separately (section 43); do not inflate desktop controls to mobile touch dimensions.

An existing visual convention does not justify violating accessibility.

---

# 3. Implementation Task vs Design-System Decision

An Agent MUST distinguish an implementation task from a design-system decision.

```text
"Make this dialog use the accepted dialog surface token."
→ implementation task
```

```text
"Rename all SurfaceContainer roles because another terminology seems cleaner."
→ design-system decision
→ requires explicit architectural review, normally an ADR
```

An implementation task follows existing contracts. A design-system decision changes them.

Agents MUST NOT silently reinterpret accepted repository decisions merely because an external design specification suggests another valid approach. This guide does not authorize such reinterpretation either.

---

# 4. Current Architectural Model

The intended architecture is:

```text
                     Cafe Design System
                            │
          ┌─────────────────┼─────────────────┐
          │                 │                 │
          ↓                 ↓                 ↓
    Color semantics    Desktop behavior    Product identity
          │                 │                 │
       M3-derived        Fluent-derived        Cafe
          │                 │                 │
          └─────────────────┼─────────────────┘
                            ↓
                       Cafe Components
                            ↓
                       Cafe Patterns
                            ↓
                    Avalonia application
```

The application SHOULD present one coherent Cafe product.

Users should not perceive:

```text
Material component
Fluent component
Avalonia component
```

as separate visual families.

---

# 5. Design-System Layers

Agents SHOULD reason in the following layers:

```text
Foundation
    ↓
System / Semantic
    ↓
Component
    ↓
Pattern
    ↓
Feature
```

These are architectural responsibilities, not necessarily mandatory namespace names.

---

# 6. Foundation Layer

Foundation tokens represent reusable design primitives.

Examples:

```text
Launcher.Spacing.*
Launcher.Radius.*
Launcher.Motion.Duration.*
Launcher.Motion.Easing.*
Launcher.Icon.*
Launcher.Stroke.*
```

Foundation values SHOULD:

- be generic;
- have no page-specific meaning;
- avoid embedding isolated component requirements.

Do not create foundation tokens such as:

```text
Launcher.Spacing.SettingsDialogSpecialOffset
```

unless the value genuinely belongs to a reusable system primitive.

---

# 7. Color System

Cafe Launcher retains its M3-derived color architecture.

Conceptually:

```text
Wallpaper / explicit seed
        ↓
seed extraction
        ↓
Material Color Utilities
        ↓
HCT / tonal palettes
        ↓
M3 system color roles
        ↓
Cafe product / component roles
        ↓
UI
```

Material Color Utilities is used as a **color-system engine**.

Its use does not require every Cafe component to follow Material component anatomy.

---

# 8. M3 System Color Roles Are Valid Semantic Roles

Agents MUST NOT treat M3 system color roles as implementation leakage.

Roles such as:

```text
Primary
OnPrimary

Secondary
OnSecondary

Surface

SurfaceContainerLowest
SurfaceContainerLow
SurfaceContainer
SurfaceContainerHigh
SurfaceContainerHighest
```

are legitimate system-level semantic roles.

If the repository exposes them as `Launcher.Color.SurfaceContainerHigh`, that is valid. Do NOT create a second synonym merely to hide Material terminology (see section 40).

---

# 9. Tonal Hierarchy Is Not Elevation

This distinction is mandatory:

```text
M3 tonal hierarchy
        ≠
Fluent spatial elevation
```

For example:

```text
SurfaceContainerHigh
```

does NOT imply:

```text
Elevation.Raised
```

and:

```text
Elevation.Modal
```

does NOT automatically imply:

```text
SurfaceContainerHighest
```

Treat them as independent dimensions.

Conceptually:

```text
              Spatial elevation
                     ↑

Modal                │
Flyout               │
Raised               │
None                 │
                     └──────────────────→ Tonal hierarchy
                       Surface
                       ContainerLow
                       Container
                       ContainerHigh
                       ContainerHighest
```

A component contract may intentionally combine them.

Example:

```text
Dialog

Background:
    SurfaceContainerHigh

Elevation:
    Modal
```

That mapping belongs to the Cafe component specification. It is not a universal M3 → Fluent conversion rule.

Note the inverse direction: M3's own component specifications do define default pairings between container roles and elevation levels (for example, dialogs default to a high surface container, menus to a mid container). When a Cafe component borrows M3 component anatomy, those default pairings are a legitimate reference point — but they enter the contract through the Cafe component specification, not through an automatic conversion.

---

# 10. Product Color Roles

Cafe-specific semantic color roles SHOULD exist only when M3 roles do not adequately describe the product meaning.

Examples may include:

```text
Launcher.Color.Success
Launcher.Color.Warning
Launcher.Color.Info

Launcher.Color.Chrome.*
Launcher.Color.Overlay.*
```

Component tokens may then consume either:

- M3 system roles;
- Cafe product roles;
- both.

Do not create duplicate aliases where no semantic distinction exists.

---

# 11. Neutral Strategy and Scheme Variants

Agents MUST preserve repository-approved support for:

- selectable neutral strategy;
- supported M3 color-scheme variants;
- seed-derived color generation;
- light and dark schemes;
- switching back to the product/default palette without stale derived colors.

Do not simplify the color pipeline into:

```text
seed → Primary only
```

The tonal and neutral system is part of the product architecture (see ADR-010 for the accepted neutral-strategy interaction).

Changes to neutral strategy, variant support, or scheme generation require corresponding design-system review and tests.

---

# 12. Wallpaper and Background Contract

Wallpaper is a product visual layer, not an unrestricted background decoration.

Agents MUST preserve accepted constraints around:

- wallpaper readability;
- foreground contrast;
- scrim use;
- protected/exception regions;
- wallpaper-derived accent generation;
- wallpaper transition behavior.

Text or controls MUST NOT depend directly on arbitrary wallpaper contrast.

When necessary, use the accepted:

```text
surface
scrim
overlay
```

mechanisms to guarantee readability.

Do not remove a scrim merely because a screenshot looks visually cleaner with one specific wallpaper.

---

# 13. Typography

Typography is Cafe-owned (ADR-005, ADR-011).

Do not mechanically mirror every role from either Material 3 or Fluent.

Typography roles SHOULD describe textual function.

Prefer concepts such as:

```text
PageTitle
SectionTitle
ItemTitle
Body
Label
Caption
```

Avoid typography role names that encode color meaning.

Bad:

```text
BodySecondary
```

if it actually means:

> normal body typography using secondary text color.

Prefer:

```text
Typography = Body
Color      = secondary text role
```

Typography and color are separate concerns.

---

# 14. Shape

Cafe Launcher SHOULD maintain a small controlled shape scale.

Existing accepted radius values and migrations SHOULD remain authoritative (ADR-002).

Agents MUST NOT add arbitrary corner radii to individual views.

Do not assume:

> larger radius = more modern.

Shape should reflect:

- component anatomy;
- visual hierarchy;
- desktop density;
- existing Cafe patterns.

---

# 15. Desktop-First Layout

Cafe Launcher is primarily a desktop launcher.

Agents MUST optimize for:

- pointer interaction;
- keyboard navigation;
- hover;
- desktop information density;
- resizable windows;
- persistent task state;
- single-window overlay workflows.

Do NOT mechanically copy mobile Material spacing or component sizes.

Touch compatibility may be retained, but should not control desktop density without an explicit requirement (see sections 2.1 and 43 for how touch targets and desktop density relate).

---

# 16. Use Spacing Before Containers

Logical grouping SHOULD normally be expressed through:

1. typography;
2. spacing;
3. proximity;
4. subtle separators;
5. surface changes only when useful.

Avoid unnecessary:

```text
Card
  inside Card
    inside Section Card
```

Do not create rounded containers merely to make a screen look "modern".

A container should represent a meaningful surface or object.

---

# 17. Settings Layout

Settings should behave like a desktop configuration surface (ADR-006, ADR-013; the accepted settings-page ADR wins where more specific).

Preferred pattern:

```text
Appearance
────────────────────────

Theme
Description if needed                 [Control]

Accent color
Description if needed                 [Control]

Background
Description if needed                 [Control]


Behavior
────────────────────────

Launch behavior                       [Control]

Close behavior                        [Control]
```

Prefer:

- clear rows;
- stable control alignment;
- restrained separators;
- hierarchy through whitespace;
- direct scanning.

Avoid standalone cards for every setting.

---

# 18. Localization Is a Layout Requirement

Cafe Launcher supports multiple UI languages.

Agents MUST consider localization when designing:

- settings rows;
- buttons;
- dialog actions;
- navigation items;
- tabs;
- toast actions;
- status messages.

Do not design around one language's string length.

Avoid fragile assumptions such as:

```text
label width = 120 px
```

without evidence that all supported locales fit.

When relevant, validate:

```text
longest locale
+
minimum supported window width
+
normal accessibility scaling
```

Layout SHOULD:

- wrap text where appropriate;
- allow flexible columns;
- avoid truncating important actions;
- preserve clear hierarchy in CJK languages.

---

# 19. Interaction States

Interactive components MUST consider relevant states.

At minimum:

```text
Default
Hover
Pressed
Focused
Disabled
```

Where applicable also:

```text
Selected
Loading
Error
Success
Indeterminate
```

Agents MUST NOT implement only the default appearance.

Hover and pressed feedback should be subtle and immediate.

Focus MUST be visible (explicit focus indicator, not a hover color reuse).

Hover MUST NOT be required to discover essential functionality.

---

# 20. Elevation

Elevation follows desktop spatial reasoning.

It represents perceived distance from the surface behind the element.

Recommended conceptual semantic roles:

```text
None
Raised
Flyout
Modal
```

Exact existing repository tokens remain authoritative.

Typical usage:

```text
Normal content
→ None

Persistent panel
→ None or restrained Raised

Menu / temporary floating surface
→ Flyout

Dialog
→ Modal
```

Elevation MUST NOT be used simply to express importance.

---

# 21. Light and Dark Elevation

Do not assume the same shadow alone communicates elevation equally in both themes.

In light theme, a raised surface may use combinations of:

```text
surface tone
+
shadow
+
border where required
```

In dark theme, shadow contrast may become weak.

Use the accepted combination of:

```text
surface tonal distinction
+
shadow
+
contour / border
```

where necessary to preserve spatial separation.

Do not simply increase shadow opacity until it becomes visible.

---

# 22. High Contrast

High Contrast is a distinct platform accessibility state.

It is not equivalent to:

```text
Dark theme with stronger colors
```

When High Contrast is active:

- platform accessibility colors may override Cafe brand colors;
- wallpaper should not remain the only basis of visual distinction;
- subtle tonal hierarchy may be insufficient;
- custom status colors must not undermine system legibility.

Accessibility colors take precedence over brand fidelity.

Agents MUST NOT "fix" platform High Contrast appearance back toward the normal Cafe palette.

---

# 23. Motion Durations

Cafe Launcher retains its accepted Fluent-derived motion system (ADR-016).

Current semantic duration direction:

```text
Immediate / control feedback ≈ 83 ms
Fast transition             ≈ 167 ms
Standard transition         ≈ 250 ms
Large spatial continuity    ≤ 333 ms
```

Existing repository motion tokens and ADR definitions are authoritative (repository anchors in section 50).

Do not invent per-screen durations.

---

# 24. Motion Curves

Use the accepted Fluent-derived model (ADR-016):

```text
Enter
→ decelerate

Exit
→ accelerate

Point-to-point
→ continuous transition curve
```

Do not replace these with generic exponential easing merely because it is convenient.

Use repository motion tokens rather than local easing literals.

---

# 25. Motion Families

Agents SHOULD classify motion into existing semantic families.

Examples:

```text
Immediate feedback

Content transition

Spatial continuity

Transient surface

Important completion
```

Use the family semantics to choose:

- duration;
- direction;
- easing;
- whether spatial movement is justified.

---

# 26. Intentional Non-Adoption of M3 Expressive Motion

Cafe Launcher intentionally does NOT use M3 Expressive spring behavior as its general motion foundation.

This is a product choice.

It does not mean expressive spring motion is invalid in Material Design.

The product prioritizes:

```text
desktop predictability
task continuity
pointer responsiveness
reduced distraction
```

Therefore ordinary controls SHOULD NOT use:

- bounce;
- spring overshoot;
- playful scaling;
- repeated staggered entrances.

An explicitly designed product moment MAY borrow expressive behavior only if:

- it serves a clear product purpose;
- it is approved as a Cafe pattern;
- reduced-motion behavior exists.

Agents MUST NOT "upgrade" ordinary Cafe motion to M3 Expressive springs automatically.

---

# 27. Reduced Motion

Reduced motion is an explicit product state.

Reduced-motion behavior SHOULD:

- remove spatial translation where possible;
- suppress decorative animation;
- disable unnecessary auto-motion;
- retain immediate interaction feedback;
- preserve comprehension.

Very short opacity transitions may remain when required for interaction clarity.

Motion MUST NOT be the sole representation of application state.

---

# 28. Dialog Motion

Dialogs and transient modal surfaces SHOULD animate as one coherent surface.

Prefer:

```text
scrim transition
+
single surface entrance
```

Avoid:

```text
dialog moves
+
title moves
+
body moves
+
buttons move separately
```

unless an explicit pattern requires it.

Internal elements should not repeat the surface entrance.

---

# 29. Automatic vs User-Directed Motion

Automatic motion SHOULD be restrained.

For example:

```text
automatic carousel
→ crossfade preferred
```

User-triggered directional navigation may use short spatial movement if direction communicates relationship.

Example:

```text
wizard next
→ short forward spatial continuity

wizard back
→ short reverse spatial continuity
```

Motion should explain structure, not decorate idle UI.

---

# 30. Component Ownership

Components are Cafe-owned.

A Cafe component may internally borrow:

```text
M3 color semantics
Fluent interaction behavior
Avalonia Fluent control templates
Material icons
```

but the component contract belongs to Cafe.

Feature code should reason about:

```text
Cafe / Launcher component role
```

not:

```text
Material Button vs Fluent Button
```

Avoid parallel component APIs such as:

```text
MaterialPrimaryButton
FluentPrimaryButton
```

---

# 31. Product Patterns

Not every repeated layout should become a generic component.

Important Cafe-specific patterns include:

```text
Launcher Shell
Game Operation Surface
Installation Progress Surface
Settings
Setup Wizard
Remote Content
Diagnostics
Update Flow
```

Patterns may compose many reusable controls.

Agents SHOULD ask:

> Is this a generic reusable component, or a product workflow pattern?

Do not over-generalize product-specific behavior.

---

# 32. Game Operation Surface

The game-operation area is a signature Cafe pattern.

The user should perceive:

```text
Install
    ↓
Download
    ↓
Verify
    ↓
Ready
    ↓
Launch
```

as stages of one task lifecycle.

Prefer:

- stable anchoring;
- continuous container transformation;
- preserved task context.

Avoid turning routine task transitions into unrelated dialogs or disconnected cards.

Errors and interruptions should retain enough task context for recovery.

---

# 33. Dialog Use

Dialogs SHOULD be reserved for:

- focused decisions;
- confirmations;
- modal tasks;
- errors requiring explicit action.

Do not use dialogs as generic information cards.

Routine state changes should remain in their owning task surface whenever possible.

---

# 34. Overlay Architecture

Overlay order is a repository contract.

Agents MUST preserve the accepted Z-order.

Current design logic includes explicit layers such as:

```text
base content
settings overlay
dialog layer
other accepted transient layer(s)
toast layer
```

The concrete contract lives in the overlay layer setters and the toast Z-order constant (repository anchors in section 50).

Do NOT invent a new arbitrary:

```text
Panel.ZIndex="750"
```

inside feature XAML.

If a new overlay category is genuinely required, update the overlay architecture deliberately.

Do not solve stacking bugs with one-off larger integers.

---

# 35. Toasts

Toast represents transient feedback.

Toast SHOULD:

- remain non-blocking;
- communicate concise state;
- include actions only when immediately useful;
- preserve accessibility announcement semantics.

Persistent workflow state belongs in its owning screen or task surface.

Do not use Toast as the only place where critical task state is visible.

---

# 36. Icons

Icon source is independent of design-system identity.

Using Material Icons does not make a component Material Design.

Material Icons MAY continue to be used if:

- semantics are clear;
- visual weight is coherent;
- no product-specific glyph is needed.

Avoid casually mixing multiple incompatible icon families within the same control group. Where a variable icon family is used, keep weight and fill axes consistent within a control group.

---

# 37. Avalonia FluentTheme

Avalonia FluentTheme remains the platform implementation base.

Conceptually:

```text
Feature
    ↓
Cafe pattern / component
    ↓
Cafe + M3 semantic resources
    ↓
Avalonia control implementation
    ↓
FluentTheme
```

FluentTheme is not the product design authority.

Agents MUST NOT assume that an Avalonia Fluent default automatically matches the accepted Cafe component contract.

---

# 38. Framework Constraints Are Part of the Architecture

Design specifications do not override actual Avalonia behavior.

Known framework constraints and project workarounds MUST be respected.

For example, the repository documents that `BoxShadow` cannot be consumed through a normal `StaticResource` conversion path; the accepted helper / project mechanism (see ADR-015 and `Controls/DialogSurface.cs`) MUST be used.

Do not "simplify" a working workaround back into an unsupported form.

Before rewriting framework-specific styling infrastructure:

1. inspect the current implementation;
2. inspect its tests;
3. inspect the relevant ADR/spec note;
4. verify the proposed Avalonia behavior.

---

# 39. Resource Organization

The architectural goal is separation of responsibilities.

A preferred conceptual structure is:

```text
DesignSystem/
│
├── Foundations/
│   ├── Spacing
│   ├── Shape
│   ├── Typography
│   ├── Elevation
│   └── Motion
│
├── Colors/
│   ├── SchemeRoles
│   ├── ProductRoles
│   └── ThemeDictionaries
│
├── Components/
│   ├── Buttons
│   ├── Inputs
│   ├── Navigation
│   ├── Dialog
│   ├── Toast
│   └── Progress
│
└── Patterns/
    ├── Shell
    ├── Settings
    ├── GameOperations
    ├── SetupWizard
    └── Diagnostics
```

The exact file layout MAY differ.

The important rule is:

> foundation, scheme, product semantics, component tokens, and product patterns should not remain conceptually indistinguishable.

---

# 40. Naming Stability

The current `Launcher.*` namespace is valid, and the M3 role names already exposed through it are valid semantic vocabulary.

Do NOT initiate either of the following as routine cleanup:

- a `Launcher.* → Cafe.*` namespace migration;
- long-lived alias tokens that duplicate an existing role under a second name, such as pairing `Launcher.Color.SurfaceContainerHigh` with `Cafe.Color.Surface.Raised` pointing to the same value.

Both would churn XAML, implementation code, tests, design contracts, documentation, and migration maps. A long-lived alias layer leaves two active naming systems for one semantic. The branding or terminology benefit alone does not justify either.

If a deliberate rename is approved, follow the repository's accepted migration policy to completion. Do not leave two semantic naming systems active indefinitely.

A namespace migration requires an explicit architecture decision (section 46).

---

# 41. Component Tokens

Component-specific tokens are valid when they encode component-specific decisions.

Examples:

```text
Launcher.Component.Dialog.*
Launcher.Component.Toast.*
Launcher.Component.Settings.*
```

Component tokens may reference:

```text
foundation values
M3 system color roles
Cafe product roles
motion roles
elevation roles
```

They SHOULD NOT leak back down and become foundations for unrelated components.

Dependency direction should remain:

```text
Foundation / Scheme
        ↓
Semantic / Product
        ↓
Component
        ↓
Pattern
        ↓
Feature
```

---

# 42. Accessibility Requirements

For new or changed interactive UI, check:

```text
contrast (numeric baselines in section 2.1)
keyboard focus
keyboard navigation
automation name
state semantics
reduced motion
High Contrast
target size
disabled readability
```

Color MUST NOT be the only representation of:

- selection;
- warning;
- error;
- success;
- disabled state.

Pair status color with text, iconography, or shape.

If a control is interactive, its accessibility behavior is part of its component contract.

---

# 43. Density

Cafe Launcher uses a desktop-oriented density model.

Do not enlarge controls merely to match mobile Material touch dimensions.

Do not compress them into legacy Win32 density without justification.

The target balance is:

```text
desktop efficiency
+
modern readability
+
Cafe visual identity
```

Existing dimensions should remain stable unless the redesign explicitly targets density.

A system-wide density change requires deliberate review (section 46).

---

# 44. Brand Expression

Cafe identity SHOULD primarily come from:

- Blue Archive / Cafe visual context;
- wallpaper;
- imagery;
- color;
- typography hierarchy;
- selected shapes;
- product-specific task surfaces;
- restrained signature details.

Do not manufacture identity through arbitrary:

- gradients;
- glowing borders;
- excessive shadows;
- oversized pill shapes;
- decorative motion.

Function remains primary.

---

# 45. Agent Workflow

Before editing UI, follow this workflow.

## Step 1 — Read the relevant decisions

Inspect:

```text
docs/design/design-system-spec.md
relevant ADR (docs/design/adr/)
existing component style
relevant tests
```

Do not rely solely on this guide.

## Step 2 — Identify the affected layer

Classify the change:

```text
Foundation
Color scheme
Product semantic
Component
Pattern
Feature
```

Do not solve a system-level problem with a feature-local value.

## Step 3 — Determine the reference system

Ask:

```text
Is the problem primarily dynamic color?
→ consult M3 color guidance.

Is it desktop interaction or layout?
→ consult Fluent guidance.

Is it motion?
→ follow accepted Fluent-derived Cafe motion ADR.

Is it product workflow?
→ follow Cafe pattern decisions.

Is it implementation behavior?
→ inspect Avalonia constraints.
```

## Step 4 — Search before creating

Before introducing a:

- color;
- spacing token;
- radius;
- shadow;
- easing;
- component style;

search the existing design-system resources (repository anchors in section 50).

Reuse existing contracts where appropriate.

## Step 5 — Do not overwrite accepted semantics

Naming and semantic-stability rules live in sections 3, 8, and 40. If a proposed change renames or reinterprets an existing role, stop and treat it as a design-system decision.

## Step 6 — Check complete states

For interactive UI, inspect relevant combinations of:

```text
Light
Dark
High Contrast where applicable

Default
Hover
Pressed
Focus
Disabled

Selected / Error / Loading / etc.
```

## Step 7 — Check localization

For text-sensitive layouts, inspect:

```text
supported locales
minimum window size
wrapping behavior
button/action overflow
```

## Step 8 — Check motion modes

For animated UI, validate:

```text
Full motion
System motion
Reduced motion
```

where relevant.

## Step 9 — Run repository UI contracts

After XAML or style work, run the relevant:

- `.\dev.ps1 ui` — UI style-contract and headless UI tests (including `UiStyleContractTests`);
- `.\scripts\Test-LocalizationContract.ps1` — after any `LauncherStrings*.resx` change;
- the unit suite, including `DesignTokenContrastTests`;
- golden screenshot checks in the headless suite;
- focused component tests.

Do not consider a visual change complete merely because it renders once locally.

---

# 46. Changes That Usually Require an ADR

Agents SHOULD treat the following as architecture changes:

- changing the M3 color-role model;
- introducing a new neutral strategy;
- removing or adding scheme variants;
- changing the overall radius scale;
- changing global control density;
- replacing Fluent-derived motion;
- changing global motion durations or easing families;
- changing overlay Z-order;
- introducing a new global surface hierarchy;
- renaming the main token namespace;
- changing core settings layout architecture;
- replacing a major Cafe product pattern.

Do not hide such changes inside an implementation commit.

---

# 47. MUST / SHOULD / MUST NOT

## MUST

Agents MUST:

- treat accepted Cafe Launcher ADRs as authoritative;
- preserve the M3-derived color architecture unless explicitly redesigning it;
- preserve valid M3 system-role naming;
- preserve Fluent-derived desktop motion direction;
- distinguish tonal hierarchy from elevation;
- use repository tokens rather than raw styling values;
- consider desktop pointer and keyboard behavior;
- preserve accessibility gates (numeric baselines in section 2.1);
- respect High Contrast;
- account for localization;
- preserve overlay ordering;
- respect documented Avalonia constraints;
- keep product patterns Cafe-owned.

## SHOULD

Agents SHOULD:

- use spacing and proximity before adding containers;
- keep elevation restrained;
- prefer semantic motion;
- maintain desktop information density;
- reuse accepted component patterns;
- separate typography semantics from color semantics;
- isolate component-specific tokens from foundations;
- use M3 and Fluent as references rather than absolute authorities;
- explain intentional deviations from external design systems.

## MUST NOT

Agents MUST NOT:

- declare Cafe Launcher purely Material or purely Fluent;
- create a duplicate Cafe synonym for every M3 system role;
- rename `Launcher.*` merely for branding purity;
- mechanically copy mobile Material layouts;
- automatically adopt M3 Expressive springs;
- assume `SurfaceContainerHigh` means elevated;
- invent arbitrary ZIndex values;
- introduce raw colors, radii, shadows, motion durations, or common spacing without justification;
- use animation as the sole state signal;
- communicate critical state only through color;
- create cards merely as decoration;
- bypass an accepted component contract for local convenience;
- silently override an accepted ADR based on external guidelines.

---

# 48. Review Checklist

## Architecture

- [ ] Relevant ADR/spec was checked.
- [ ] Correct design-system layer was identified.
- [ ] No accepted decision was silently replaced.
- [ ] No unnecessary synonym token was introduced.
- [ ] Component-specific values did not leak downward.

## Color

- [ ] M3 role semantics remain correct; product-specific roles are justified.
- [ ] Tonal hierarchy is not confused with elevation.
- [ ] Light and dark themes were checked.
- [ ] Neutral strategy and scheme variant behavior remain valid.
- [ ] Wallpaper/scrim readability remains valid.

## Layout

- [ ] Desktop density remains appropriate.
- [ ] Grouping uses spacing before unnecessary containers.
- [ ] Minimum window size remains usable; localization expansion was considered.

## Interaction

- [ ] Default / Hover / Pressed / Focus / Disabled all checked.
- [ ] Keyboard interaction remains valid.

## Motion

- [ ] Existing motion tokens used; correct semantic motion family chosen.
- [ ] No unnecessary spring/bounce introduced.
- [ ] Reduced motion remains usable.
- [ ] Repeated child entrance was avoided.

## Accessibility

- [ ] Contrast meets the section 2.1 numeric baselines; focus remains visible.
- [ ] Important state is not color-only.
- [ ] Automation semantics remain present.
- [ ] High Contrast was considered where relevant.

## Platform

- [ ] Avalonia implementation limitations were checked; no accepted workaround was removed blindly.
- [ ] Overlay Z-order remains valid.

## Product

- [ ] The result still feels like one Cafe Launcher product.
- [ ] The change improves hierarchy or usability rather than adding decoration.
- [ ] Existing product patterns remain coherent.

---

# 49. Common Anti-Patterns

## 49.1 "M3 says so"

Bad:

> M3 uses a certain component size, therefore Cafe must use it.

Better:

> Check whether the M3 principle applies to this desktop Cafe workflow and whether an accepted Cafe token already exists.

## 49.2 "Windows app = Fluent colors"

Bad:

> This is a Windows application, therefore replace M3 dynamic color with Fluent palette generation.

Color system and desktop interaction system are separate concerns.

## 49.3 Renaming valid M3 semantics

Bad:

> `SurfaceContainerHigh` → `RaisedSurface`, solely to avoid Material terminology.

This reduces semantic precision rather than improving it. See sections 3, 8, and 40.

## 49.4 Tonal hierarchy interpreted as Z-depth

Bad:

> `SurfaceContainerHighest` = top-most visual layer.

M3 container emphasis and spatial elevation are independent.

## 49.5 Cardification

Bad:

```text
Page
└── Card
    └── Card
        └── Setting Card
```

Prefer hierarchy through spacing and typography.

## 49.6 Local magic values

Bad:

```xml
<Button
    Padding="17,9"
    CornerRadius="11"
    Background="#448AFF" />
```

without an explicit component exception.

## 49.7 Arbitrary Z escalation

Bad:

```xml
Panel.ZIndex="9999"
```

to fix an overlay issue. Fix the overlay architecture instead.

## 49.8 Expressive motion as automatic modernization

Bad:

> Add spring overshoot because M3 Expressive is newer.

Cafe Launcher intentionally uses a restrained Fluent-derived global motion language.

## 49.9 Treating High Contrast as a visual bug

Bad:

> High Contrast looks off-brand, so restore Cafe colors.

High Contrast accessibility takes priority.

---

# 50. Repository Anchors

Authoritative locations this guide refers to. Verify current contents before relying on specifics.

| Concern | Location |
| --- | --- |
| Design-system specification | `docs/design/design-system-spec.md` |
| Accepted ADRs | `docs/design/adr/` (ADR-001 … ADR-018) |
| Motion tokens (durations, offsets, easing) | `App.axaml` resource section; `Helpers/MotionTokens.cs` |
| Fluent motion layer decision | ADR-016 |
| Neutral strategy interaction | ADR-010 |
| Settings layout decisions | ADR-006, ADR-013 |
| Button type specifications | ADR-004 |
| Dialog family / `BoxShadow` constraint | ADR-015; `Controls/DialogSurface.cs` |
| Overlay Z-order contract | `Views/MainWindow.Styles.axaml` layer setters; `Constants/LauncherConstants.cs` (`ZIndexToast`) |
| M3 scheme generation | `Services/MaterialSchemeGenerator.cs` |
| UI style-contract tests | `UiStyleContractTests`; `.\dev.ps1 ui` |
| Contrast tests | `DesignTokenContrastTests` |
| Golden screenshot checks | `tests/Cafe.Launcher.Avalonia.HeadlessTests/GoldenScreenshot.cs` |
| Localization contract | `.\scripts\Test-LocalizationContract.ps1` |

---

# 51. Final Principle

The design-system question is not:

> "What would Material do?"

Nor:

> "What would Fluent do?"

The correct question is:

> **"Given Cafe Launcher's accepted design decisions, product requirements, accessibility constraints, and platform behavior, what is the most coherent Cafe solution?"**

External design systems provide evidence.

Repository decisions provide authority.

Cafe Launcher remains the product.
