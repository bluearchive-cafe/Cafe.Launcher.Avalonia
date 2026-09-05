# Changelog

## v1.3.0 — 2026-08-26

### Added
- Six component best-practice references organized by Material purpose: Actions,
  Communication, Containment, Navigation, Selection, and Text Input.
- Per-component guidance for purpose, when to use, alternatives, behavior/content rules,
  accessibility, anti-patterns, and M2↔M3 differences.
- Coverage for buttons/icon buttons/FAB/segmented & split buttons; badges/progress/snackbar;
  cards/dialogs/sheets/carousel/tooltips/lists; navigation bar/rail/drawer/app bars/tabs;
  checkbox/radio/switch/chips/sliders/menus/pickers; text fields/search.
- Ten additional evals (30 total) targeting component-selection semantics and version-aware
  behavior, including Snackbar timing, adjacent text-field labels, adaptive navigation, card
  actions, chip roles, SearchBar fit, Backdrop migration, and progress semantics.

### Changed
- Split the compact component catalog from detailed component-usage guidance so the Skill can
  load only the relevant category instead of a single ever-growing reference.
- Added a component-guidance discipline: Purpose → Use when → Alternatives → Best practices →
  Anti-patterns → M2/M3 note → Accessibility.
- Corrected TextField labeling guidance so placeholder-only labeling remains invalid while a
  clear adjacent/independent label is not falsely rejected.
- Softened Card interaction guidance: a primary card action can coexist with distinct
  supplementary controls; overlapping/nested competing hit regions remain an anti-pattern.
- Made Snackbar guidance explicitly version-aware instead of treating archived M2 timing as
  a current universal rule.
- Corrected determinate progress semantics from “known duration” to measurable progress.

## v1.2.0 — 2026-08-26

### Added
- `references/foundations-comparison.md` — explicit M2↔M3 comparison for color,
  typography, shape, elevation/surfaces, motion, adaptive layout, components, and migration.
- `references/expressive.md` — current M3 Expressive guidance with Compose API/status
  caveats, motion schemes, expanded shape roles, component classification, and product fit.
- Evidence-discipline rules separating specification facts, platform facts, and project
  heuristics.
- Current M3 color roles including `surfaceBright`, `surfaceDim`, and fixed color families.
- Current Android adaptive width classes through Large and Extra-large.
- 8 additional evals (20 total) covering M2 Navigation Rail, standard-M3 Segmented Button
  and Large FAB, current Compose Expressive packaging, heuristic-vs-spec rules, fixed color
  roles, adaptive windows, reduced motion, and Snackbar guidance.

### Changed
- Reframed rigid numeric/product rules as heuristics unless the Material component spec or
  platform actually defines the constraint.
- Expanded token guidance to distinguish canonical `md.ref` / `md.sys` / `md.comp`
  families from project-owned spacing and implementation aliases.
- Updated Compose Expressive guidance to use the normal
  `androidx.compose.material3:material3` artifact and current `MotionScheme` APIs.
- Updated Angular Material guidance to acknowledge current M3 theming documentation.
- Updated Material Web guidance to reflect its repository maintenance-mode status.
- Reworked reduced-motion guidance: preserve state/causality while reducing unnecessary
  spatial motion rather than universally forcing every animation to 0ms.

### Fixed
- Corrected the false claim that Material 2 did not have Navigation Rail; archived M2
  guidance includes it for 3–7 primary destinations on medium/large displays.
- Corrected false Expressive-only labels for Segmented Button and Large FAB.
- Removed the false direct mapping `M2 primaryVariant ≈ M3 primaryContainer`.
- Corrected dynamic-color Compose sample to select light vs dark dynamic schemes.
- Corrected WCAG large-text threshold wording.
- Removed fake universal component rules such as one Filled Button per view, mandatory
  confirmation for every destructive action, one-line-only Snackbars, a 60% menu-height
  cap, and a 200ms universal tooltip delay.
- Removed fictional native token APIs from the platform mapping table.

## v1.1.0 — 2026-06-27

### Added
- `references/material-2.md` — full M2 legacy reference (color, typography, elevation,
  components, theming, M2-to-M3 migration cautions).
- `references/anti-patterns.md` — common Material Design mistakes across color,
  components, theme, platform, M3 Expressive, and process categories.
- `agents/openai.yaml` — display metadata for skill listings.
- Version gate with M2/M3/M3 Expressive routing modes.
- Source of truth rules with priority ordering.
- Do-not-use section specifying when the skill should NOT trigger.
- Systematic reference loading rules (when to load each reference file).
- Output templates: component specification, design audit, M2 output mode.
- Task-oriented accessibility section in component spec and audit templates.
- Platform decision checklist (verify before recommending a library).
- Maintenance metadata (`last_verified`, `scope`, `requires_current_verification`)
  on all reference files.
- CHANGELOG.md (this file).

### Changed
- **SKILL.md**: Title changed from "Material Design 3 Skill" to "Material Design Skill".
- **SKILL.md**: Slimmed from 441 lines to 341 lines. Moved encyclopedic content to
  references; main file now focuses on workflow, decisions, and routing.
- **Frontmatter description**: Narrowed to avoid false triggering on non-Material
  design systems (Ant Design, Fluent, etc.).
- **Platform guides**: Removed hardcoded version numbers. All platform sections now
  instruct verification against current official docs.
- **Version compatibility matrix**: Replaced static version table with capability-based
  table + verification checklist.
- **M3 Expressive motion description**: Changed from "replacing" to "introduces" with
  platform-support caveats.
- **Expanded evals.json**: From 3 tests to 12 tests covering positive M3, negative
  (non-Material), M2 mode, M2-to-M3 migration, M3 Expressive caution, platform
  version verification, accessibility, component-library-vs-spec boundary,
  and unspecified-design-system scenarios.

### Fixed
- P0-01: Frontmatter description too broad — narrowed, added do-not-use rules.
- Angular Material section now splits M3 theming from M2 legacy; removed misleading `m2-define-palette` example under M3 heading.
- Platform selection flow softened: Android Compose line now says "verify current stable vs experimental API status" instead of hard "full M3 + Expressive support."
- P0-02: No M2 mode — added version gate + `material-2.md`.
- P0-03: Hardcoded platform versions — replaced with verification rules.
- P0-04: No source-of-truth rules — added priority ordering and verification requirements.
- P0-05: M3 Expressive language too absolute — softened with platform caveats.
- P0-06: No M2 output template — added M2 output mode.
- P1-01: SKILL.md too heavy — slimmed to workflow/decision focus.
- P1-02: Reference loading rules unsystematic — added indexed table.
- P1-03: Component-library vs spec boundary weak — hardened with explicit distinctions.
- P1-04: Accessibility rules not task-oriented — embedded in component and audit templates.
- P1-05: Eval coverage insufficient — expanded to 12 tests.
- P1-06: Static version matrix — replaced with capability-based + verification checklist.
- P1-07: Missing maintenance metadata — added to all reference files.
- P2-01: Added `agents/openai.yaml`.
- P2-02: Added `CHANGELOG.md` (this file).
- P2-04: Added `references/anti-patterns.md`.

## v1.0.0 — 2026-06-27

### Initial release
- `SKILL.md` with M3 design guidance.
- `references/color-system.md` — tonal palettes, color roles, dark theme, dynamic color.
- `references/components.md` — 30+ component catalog with usage rules and states.
- `references/design-tokens.md` — token catalog, naming conventions, platform mappings.
- `references/platform-guides.md` — Android, Flutter, Web, React, Angular, Vue guides.
- `evals/evals.json` with 3 test cases.
