---
last_verified: 2026-08-26
scope: "Common Material Design mistakes and anti-patterns to avoid"
requires_current_verification: true
---

# Material Design Anti-Patterns

## Color anti-patterns

### Primary color saturation

**Problem**: Primary color applied to every button, header, icon, and accent element.

**Why it's wrong**: If everything is primary, nothing is. Users can't identify the
single most important action on the page.

**Fix**: Build a clear emphasis hierarchy. Often that means one dominant action and
lower-emphasis alternatives, but do not turn “one Filled Button per view” into a fake
Material requirement.

### Hardcoded hex values

**Problem**: `background: #6750A4; color: #FFFFFF;` in component CSS.

**Why it's wrong**: Blocks theme switching, dark mode, dynamic color, and
high-contrast mode. Every hardcoded color is technical debt.

**Fix**: Use semantic tokens: `background: var(--md-sys-color-primary); color: var(--md-sys-color-on-primary);`

### Error expressed through color alone

**Problem**: Just turning a text field border red when validation fails.

**Why it's wrong**: Color alone can be missed by users with color-vision differences and
does not provide a programmatic error description to assistive technology.

**Fix**: Red border + error icon + descriptive error text ("Email must contain @")
+ link the error to the field programmatically.

### Pure black in dark theme

**Problem**: `background: #000000` for dark theme surfaces.

**Why it's wrong**: Using the same pure-black value for every surface discards the
semantic surface hierarchy used by baseline M3 and can make boundaries harder to read.
Pure black itself is not universally forbidden; it can be an intentional product/device
choice.

**Fix**: Prefer M3 surface roles and container hierarchy by default. If the product uses
pure black (for example an AMOLED-oriented theme), preserve hierarchy and contrast with
appropriate containers, outlines, and content roles.

---

## Component anti-patterns

### Placeholder as label

**Problem**: Using `placeholder="Email"` with no visible label on a text field.

**Why it's wrong**: Placeholder disappears when the user types. Users forget what
the field is for. Screen readers may not announce the placeholder as a label.

**Fix**: Always include a persistent label. Placeholder is optional supplementary
hint text, never a label replacement.

### Icon-only buttons without accessible names

**Problem**: `<button><span class="icon-close"></span></button>` with no label.

**Why it's wrong**: Screen readers announce "button" with no context. Users don't
know what the button does.

**Fix**: Add `aria-label="Close"` or `contentDescription="Close"` to every icon button.

### Disabled buttons with no explanation

**Problem**: A grayed-out "Submit" button with no tooltip or helper text.

**Why it's wrong**: Users don't know WHY the button is disabled or what they need
to do to enable it. This creates frustration and abandonment.

**Fix**: Make the prerequisite/status understandable near the action or relevant input.
Keeping submit enabled and validating on activation is often more discoverable. If the
action must be disabled, use visible helper/status text where needed; do not rely on a
tooltip-only explanation, especially on touch devices.

### Multiple primary buttons

**Problem**: "Save" (Filled) + "Submit" (Filled) + "Continue" (Filled) on the same form.

**Why it's wrong**: Users can't distinguish the primary path from secondary options.

**Fix**: Reduce competing high-emphasis actions and make the intended path visually
clear. Variant choice should follow action hierarchy and component context, not a global
exactly-one rule.

### FAB overload

**Problem**: Multiple FABs on one screen, or a FAB that opens a menu of 8 unrelated actions.

**Why it's wrong**: FAB is for the single most important create/compose action.
Overloading it defeats its purpose and creates confusion.

**Fix**: Keep the FAB focused on a clear primary action. If several peer actions compete,
use an appropriate toolbar, button group, menu, or another component supported by the
current Material generation. Treat item counts as a scannability decision, not a law.

### Dialog for complex forms

**Problem**: A Dialog containing a multi-field form with dropdowns, validation, and
conditional sections.

**Why it's wrong**: Dialogs are for short, focused interactions. Complex forms in
dialogs are cramped, hard to navigate, and inaccessible.

**Fix**: Use a larger page, adaptive pane, or other surface when the task needs substantial
space, navigation, or validation. A bottom sheet can be appropriate for some bounded
mobile tasks, but it is not the universal replacement for every complex form.

---

## Theme anti-patterns

### Designing light theme only

**Problem**: Completing the entire light theme design, then trying to "invert colors"
for dark theme at the end.

**Why it's wrong**: Dark theme uses different role values and perceptual relationships.
M3 still can use elevation/shadow, but surface roles and contrast must be designed rather
than produced by naïvely inverting the light palette.

**Fix**: Design light and dark themes simultaneously. Use the M3 color role system
so theme switching is a token swap, not a redesign.

### Skipping component states

**Problem**: Designing only the default/enabled state for each component.

**Why it's wrong**: Interactive components need the states that apply to their behavior
and platform. Missing focus/pressed/selected/error behavior can create ambiguity or
accessibility failures.

**Fix**: Define and test **applicable** states. Do not force hover onto touch-only
components or error/loading states onto controls that never have them. Use current
component state tokens instead of one global opacity recipe.

### Brand customization that breaks semantics

**Problem**: Making error states brand-green because "green is our brand color."

**Why it's wrong**: Color roles have semantic meaning. `error` means error regardless
of brand. Overriding semantics with brand destroys user understanding.

**Fix**: Customize brand expression within the allowed scope (primary color, font,
corner style, density). Preserve semantic color roles and component structure.

---

## Platform anti-patterns

### Assuming MUI = M3

**Problem**: "We use MUI, so we're following Material Design 3."

**Why it's wrong**: A library name or Material-inspired component set does not guarantee
that its current defaults, theming vocabulary, and component catalog match the latest M3
specification.

**Fix**: Verify the current MUI release's declared Material generation and map any M3
semantic roles/component differences explicitly when strict conformance matters.

### Assuming Material Web is always the right web choice

**Problem**: Recommending Material Web without checking its current maintenance status.

**Why it's wrong**: Material Web is an official Material project, but its repository is
in maintenance mode at the verification date and its current component/Expressive
coverage must be checked before adoption.

**Fix**: Check the repository's recent commits, open issues, and release frequency
before recommending for production.

### Hardcoding platform library versions in guidance

**Problem**: "Compose Material3 1.4.0 supports spring motion."

**Why it's wrong**: Version numbers drift. 1.4.0 may be outdated or superseded by
the time someone reads the guidance. The feature support claim may also change.

**Fix**: Reference features by capability ("spring motion is available in recent
Compose Material3 releases"), not by version number. Always tell users to verify
against current release notes.

---

## M3 Expressive anti-patterns

### Expressive everywhere

**Problem**: Applying spring animations, emphasized typography, bold shapes, and
hero color moments to every screen in a banking app.

**Why it's wrong**: Expression should serve hierarchy, usability, brand, and task context.
A blanket industry ban is as unhelpful as making every control animated and oversized.

**Fix**: Apply Expressive where stronger shape, type, color, or motion improves the flow,
and use the standard motion/component treatment for utilitarian or repeated interactions
when restraint improves clarity. Evaluate by flow rather than industry label.

### Spring motion without reduced-motion fallback

**Problem**: Bouncy spring animations with no `prefers-reduced-motion` alternative.

**Why it's wrong**: Users with vestibular disorders can experience nausea from
excessive motion. It's also an accessibility requirement.

**Fix**: Respect the platform reduced-motion preference. Reduce/remove unnecessary
travel, scale, parallax, bounce, and morphing while preserving state change and causality.
Zero duration is one option for some effects, not a universal requirement for all motion.

---

## Process anti-patterns

### Designing without tokens

**Problem**: Designing pages by picking hex colors and pixel font sizes directly in
Figma without defining tokens first.

**Why it's wrong**: Without tokens, every style decision is an ad-hoc choice.
Consistency drifts. Theme switching becomes impossible. Handoff to development
requires manual translation of every value.

**Fix**: Define tokens first (color, type, shape, spacing). Build components from
tokens. Design pages from components.

### Skipping accessibility in design

**Problem**: Treating accessibility as a developer responsibility or a pre-launch
checklist.

**Why it's wrong**: Many accessibility issues are design decisions (contrast, touch
target size, focus order, label placement, error communication). They can't be fixed
in code alone.

**Fix**: Include accessibility in design reviews. Run the A11y checklist on every
component and page before handoff.
