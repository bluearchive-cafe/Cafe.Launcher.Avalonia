---
name: material-design
description: >-
  Use when a task explicitly involves Material Design, Material 2, Material 3,
  Material You, M3 Expressive, Material components, Material theming/tokens, or
  evaluating a library against the Material specification.
---

# Material Design Skill

Guide for designing, auditing, and implementing Material Design interfaces.
Default target: **Material Design 3**. Explicitly supports M2 legacy mode and
M3 Expressive enhancements.

## Do not use this skill when

- The user asks for Ant Design, Fluent, Carbon, Polaris, shadcn/ui, iOS HIG, or
  another named design system without requesting Material alignment.
- The task is pure branding, illustration, marketing visual design, or graphic design
  unrelated to UI component specification.
- The user wants generic frontend implementation advice without Material Design
  constraints.
- The user says "design system" in a general sense without mentioning Material Design —
  ask which system they mean before applying Material rules.

## Source of truth

When factual accuracy matters, prioritize sources in this order:

1. Google official Material Design documentation (m3.material.io).
2. Android Developers / Flutter official documentation.
3. Official component library documentation and repositories.
4. Release notes and issue trackers.
5. Community articles only as secondary references.

**Critical rules:**
- Verify current platform and library status before giving version-specific advice.
- Do not claim a library "fully supports M3" or "M3 Expressive" unless official docs
  confirm it at the time of the request.
- If sources disagree, explain the difference and scope the recommendation.
- A component library is an **implementation**, not the specification. Always
  distinguish: Material Design guideline → Library implementation → Project-specific
  design system → Custom overrides.

## Version gate

Before giving guidance, determine the target Material version:

| User says | Mode | Rules |
|-----------|------|-------|
| "Material 2", "M2", "MD2", or maintaining existing M2 app | **M2 mode** | Use M2 concepts only: primary/secondary palette, type scale (h1–h6, subtitle, body, caption), elevation shadows, M2 component patterns. Do NOT use tonal palettes, surfaceContainer roles, M3 type scale tokens, or M3 Expressive motion. |
| "Migrate from M2 to M3" | **Migration mode** | Compare both versions. Provide a phased migration plan preserving existing UX where possible. |
| "Material 3 Expressive", "M3 Expressive" | **M3 + Expressive mode** | Layer Expressive on M3. Assess whether the product context supports expressive design (see Expressive guidance). |
| Unspecified, new project | **Default to M3** | Use M3 as baseline. Mention M2 only if the user's constraints (e.g., MUI v5, legacy codebase) suggest it. |

In M2 mode, do not use M3-only concepts unless explicitly comparing against M3.
For full M2 reference, read `references/material-2.md`.

## When to use M3 Expressive

M3 Expressive is the current evolution of M3 and adds stronger shape, typography,
motion, adaptive component, and color-expression options. Use it selectively when:

- Building consumer apps, media, social, content, or lifestyle products.
- Brand expression and emotional engagement are product goals.
- You need to guide attention toward key actions or hero content.

**Use with restraint when:**
- The interface is data-dense, repetitive, or optimized for rapid scanning.
- Safety, precision, or regulatory review favors stable and predictable hierarchy.
- Strong motion or shape changes would compete with the user's primary task.

Do not ban Expressive by industry category. Evaluate the actual flow and use restrained
Expressive moments where they improve hierarchy or continuity.

M3 Expressive introduces motion schemes and physics-based motion on supported platforms.
Distinguish spatial motion from effects motion where the platform does. Do not translate
this into “use a bouncy spring everywhere,” and do not assume every platform exposes the
same APIs. Read `references/expressive.md` for current caveats.

## Default assumptions

When the user does not specify, assume:

- Target version: Material Design 3.
- Platform: ask if it affects component or token choices.
- Theme: provide both light and dark guidance.
- Accessibility: WCAG AA minimum; include keyboard, screen reader, and touch target guidance.
- Token usage: semantic tokens, not raw values.

## Reference loading rules

Load reference files on demand based on the task. Do not load all references preemptively.

| When the task involves... | Read |
|---------------------------|------|
| Color roles, dynamic color, dark theme, contrast, brand color mapping | `references/color-system.md` |
| Component catalog, anatomy, state overview | `references/components.md` |
| Buttons, icon buttons, FAB, segmented/split buttons | `references/components-actions.md` |
| Badges, progress, snackbars, M2 banners | `references/components-communication.md` |
| Cards, dialogs, sheets, carousel, tooltips, lists/dividers | `references/components-containment.md` |
| Navigation bar/rail/drawer, app bars, tabs, adaptive navigation | `references/components-navigation.md` |
| Checkbox, radio, switch, chips, sliders, menus, date/time pickers | `references/components-selection.md` |
| Text fields and search | `references/components-text-input.md` |
| Token naming, token layers, platform mappings, design-to-code handoff | `references/design-tokens.md` |
| Platform-specific implementation after verifying current library version | `references/platform-guides.md` |
| User explicitly requests Material 2, M2 theming, or legacy M2 maintenance | `references/material-2.md` |
| Comparing M2↔M3 foundations or planning migration | `references/foundations-comparison.md` |
| M3 Expressive, motion schemes, new/updated expressive components | `references/expressive.md` |
| Common design mistakes and how to avoid them | `references/anti-patterns.md` |


## Evidence discipline

Before stating a design rule, classify it mentally as one of:

1. **Specification fact** — backed by Material guidance/tokens.
2. **Platform fact** — backed by the target library/API and may be version-specific.
3. **Project heuristic** — a reasonable default, not a Material requirement.

Use words such as “must” only for true requirements (including accessibility or API
constraints). Use “prefer”, “typically”, or “consider” for heuristics. Do not invent hard
cutoffs such as “more than N items is forbidden” unless the component specification
actually defines that range.

### Component-guidance discipline

For component-selection questions, load the relevant category reference and answer in this
order when useful:

1. **Purpose** — what job the component performs.
2. **Use when** — conditions supported by the Material guidance.
3. **Avoid / use an alternative when** — semantic mismatch, not stylistic preference.
4. **Best practices** — content, hierarchy, behavior, layout, and applicable states.
5. **Anti-patterns** — concrete misuse examples and the better alternative.
6. **M2 ↔ M3 note** — only when behavior/name/availability materially differs.
7. **Accessibility** — accessible name/state, focus/keyboard, touch target, error/status.

Do not merge M2 and M3 rules into a fake cross-version rule. If M2 and current M3 differ
(for example Snackbar timing/action behavior), label the version difference explicitly.

## Design workflow

### New project (M3 default)

1. Clarify: What platforms and Material version?
2. Define seed color → generate tonal palette → assign color roles.
3. Establish typography scale (family, sizes, line heights, weights).
4. Define shape scale (corner radius tokens).
5. Set spacing scale (base increment, common values).
6. Define motion tokens (scheme, speed tiers — spring if platform supports it).
7. Build core components (button, input, card, navigation, dialog).
8. Define all **applicable** component states (for example enabled, focus, pressed, selected, disabled, loading, or error as relevant).
9. Create page templates (login, list, detail, form, settings, empty state).
10. Validate: contrast, touch targets, keyboard flow, screen reader output.

### Designing a single page

1. Identify the user's primary task and information priority.
2. Select navigation pattern (bottom bar, rail, drawer, tabs).
3. Choose layout for the target breakpoint(s).
4. Pick components from the approved set — don't invent new ones.
5. Apply tokens — never hardcode values.
6. Design the states and edge cases that actually apply: loading/empty/error/success only where the component or content flow can enter them.
7. Run the accessibility checklist (see below).
8. Document interaction behavior and component → code mapping.

## Output templates

### Component specification template

When specifying a Material component, use this structure:

```markdown
## Component: [Name]

**Purpose**: [What it does]
**When to use**: [Context]
**When not to use**: [Alternatives]

### Anatomy
[Structure description]

### Applicable states
| State | Visual | Behavior |
|-------|--------|----------|
| Enabled | ... | ... |
| Hover | ... | ... |
| Focus | ... | ... |
| Pressed | ... | ... |
| Disabled | ... | ... |
| Loading | ... | ... |
| Error | ... | ... |

### Accessibility
- Accessible name:
- Keyboard interaction:
- Focus behavior:
- Screen reader state:
- Error announcement:
- Touch target:
- Contrast considerations:
- Reduced motion behavior:

### Tokens used
| Token | Value |
|-------|-------|

### Platform notes
[Platform-specific implementation notes — verify current library docs]
```

### Design audit template

When auditing an interface against Material Design:

```markdown
## Audit: [Page/Feature]

### Specification alignment
| Check | Status | Notes |
|-------|--------|-------|

### Accessibility defects
| Issue | Impact | Fix | Priority |
|-------|--------|-----|----------|

### Token compliance
[Check for hardcoded values]

### Component misuse
[Components used against their intended purpose]

### State coverage gaps
[Missing hover, focus, pressed, disabled, loading, error states]
```

### Material 2 output mode

When producing M2 guidance, structure the answer as:

```markdown
1. M2 scope and assumptions.
2. Color palette: primary, primaryVariant, secondary, secondaryVariant,
   background, surface, error, onPrimary, onSecondary, onSurface, onError.
3. Typography: h1–h6, subtitle1/2, body1/2, button, caption, overline.
4. Shape and elevation (shadow-based).
5. Component choices (M2 variants).
6. States and accessibility.
7. Platform-specific implementation notes.
8. Migration risks if moving to M3 later.
```

## Quick reference: design defaults (heuristics, not universal rules)

| Situation | Prefer | Reconsider when |
|-----------|--------|-----------------|
| Highest-emphasis action | Filled Button or the component's highest-emphasis variant | Multiple actions truly have equal priority |
| Destructive action | Clear destructive label + error semantics where supported | Reversible/low-risk deletion can use undo instead of confirmation |
| Create/compose action on mobile | FAB when it is a persistent primary action | The action is not persistent, not primary, or conflicts with navigation |
| Irreversible destructive operation | Confirmation or another deliberate friction step | The action is easily reversible and undo is safer/faster |
| Small, visible set of exclusive options | Radio buttons or segmented buttons | Space, localization, or option count makes scanning poor |
| Contextual mobile actions | Menu or bottom sheet depending content/space | A sheet adds unnecessary modality |
| Complex multi-step task | Full page / adaptive pane | The task is genuinely short enough for a dialog/sheet |
| Loading known progress | Determinate progress | Progress cannot be measured reliably |
| Form error | Error text + semantic association + visual cue | Never communicate error by color alone |
| Disabled action | Make prerequisite/status understandable nearby | Tooltip-only explanations are often undiscoverable, especially on touch |

## Accessibility checklist

Run this checklist on every component and page delivered:

### Visual
- [ ] Text contrast follows WCAG: 4.5:1 for normal text; 3:1 for large text (18pt regular or 14pt bold and equivalent)
- [ ] Icon and UI component contrast ≥ 3:1
- [ ] Error states use icon + text, not just color
- [ ] Focus indicators are visible on all interactive elements
- [ ] Disabled state is distinguishable but does not disappear

### Interaction
- [ ] Touch interfaces provide at least the platform-recommended target size (Material/Android commonly 48dp × 48dp), even if the visual container is smaller
- [ ] On keyboard-capable platforms, every interactive element is reachable by keyboard
- [ ] Keyboard behavior follows platform/ARIA conventions (for example Enter/Space activation, Escape dismissal, arrow-key menu navigation on web)
- [ ] Focus order follows visual order
- [ ] Popups, dialogs, and sheets are dismissible without gesture-only paths

### Semantics
- [ ] Icon-only buttons have accessible names (aria-label / contentDescription)
- [ ] Images have alt text or are marked decorative
- [ ] Form inputs have associated labels (not placeholder-only)
- [ ] Error messages are linked to their fields
- [ ] Current page/selection state is announced to screen readers
- [ ] Dynamic content changes use live regions where appropriate

### Motion
- [ ] Reduced-motion/system motion preferences are respected on the target platform
- [ ] No auto-playing video or animation without pause/stop
- [ ] No flashing content above 3 flashes/second
- [ ] Animation does not block task completion

## Common mistakes

1. **Treating Material Design as a visual skin** — it's a structural, semantic, and
   behavioral system. Just styling buttons to "look Material" misses the point.
2. **Saturating the UI with primary color** — overusing high-emphasis color weakens
   hierarchy. Use component color roles according to semantic purpose.
3. **Designing only the default state** — interactive components are incomplete when
   they omit states that apply to their platform/behavior (for example focus, pressed,
   selected, disabled, loading, or error). Do not invent irrelevant states just to fill a checklist.
4. **Skipping dark theme until the end** — dark theme changes color-role assignments,
   surface hierarchy, contrast, and how visible shadows/borders are. Design both together.
5. **Confusing the component library with the specification** — verify what each
   library actually implements. MUI, Angular Material, and Vuetify each have their
   own Material Design support scope. Check official docs, not assumptions.
6. **Over-using M3 Expressive** — expressive shape, type, and motion should reinforce
   hierarchy and continuity, not become decoration on every interaction.
7. **Hardcoding values** — a hex color or pixel font-size in a component is technical
   debt that blocks theme switching, dark mode, and accessibility modes.
8. **Applying M3 tokens to strict M2 projects** — don't use tonal palettes,
   surfaceContainer roles, or M3 type scales in M2 contexts unless migration is
   the explicit goal.

## Platform implementation

### Platform support rule

When platform or library support affects the answer, verify the current official docs,
release notes, or repository state before giving version-specific guidance.
Do not hard-code "current" versions; always note that library support should be verified.

### Android (Jetpack Compose)

Prefer Compose Material3 for new M3 projects. Verify the current stable artifact version
and whether experimental APIs are acceptable for the project. For M3 Expressive features
(motion schemes, new/updated components), check current Compose Material3 API docs and
release notes. Current Expressive APIs are in the regular Material3 package; do not
invent a separate artifact from stale examples. Use `MaterialTheme.colorScheme.*`
inside components rather than raw theme colors.

### Flutter

Flutter enables Material 3 by default in modern releases (since Flutter 3.16).
Use `useMaterial3: true` only for clarity or older codebases; use `false` only
as a temporary M2 fallback — M2 support follows Flutter's deprecation policy.
Verify current M3 Expressive feature availability in the Flutter SDK release notes.

### Web — CSS Custom Properties

Use `--md-sys-*` CSS custom properties. Framework-agnostic. Supports light/dark via
`prefers-color-scheme` or `[data-theme]` attribute.

### React

MUI is a mature Material Design React library. MUI currently documents Material
Design 2 support and does not implement every Material Design component or feature.
Treat M3 support as requiring explicit verification against current MUI docs,
release notes, and design-related issues. For strict M3, map M3 tokens into
MUI's `createTheme()` or evaluate community M3 libraries.

### Angular

Angular Material and CDK are the primary Angular options. Verify the exact version's
M2/M3 theming APIs. CDK primitives (Overlay, A11y, Layout) are valuable regardless of
Material version.

### Vue

Vuetify provides Material-style components. Verify the installed version's Material
Design specification support.

### Material Web

Official M3 Web Components from Google. The repository currently states that MWC is
in maintenance mode pending new maintainers. Treat Expressive coverage as unsupported
unless current official docs say otherwise, and verify component coverage before adoption.

For detailed platform setup, token mapping, and library selection guidance,
read `references/platform-guides.md`.

## Resources

- Official M3 docs: https://m3.material.io
- Material Theme Builder (Figma + web): https://m3.material.io/theme-builder
- Material Symbols (icons): https://fonts.google.com/icons
- Material Web (web components): https://github.com/material-components/material-web
- M3 Compose (Android): https://developer.android.com/jetpack/compose/themes/material3
- M3 Flutter: https://docs.flutter.dev/ui/design/material
- M3 Design Kit (Figma): https://www.figma.com/community/file/1035203688168086460
- Archived M2 docs: https://m2.material.io/
- Adaptive window size classes: https://developer.android.com/develop/adaptive-apps/guides/use-window-size-classes
