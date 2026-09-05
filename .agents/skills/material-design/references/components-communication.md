---
last_verified: 2026-08-26
scope: "Material Design 2 and 3 component best practices: communication and status"
requires_current_verification: true
---

# Material Components — Communication

These components communicate **status, progress, lightweight feedback, or attention**
without becoming the primary task themselves.

## Badge

**Purpose**: Add a small status or numeric signal to another component.

### Use when

- Showing unread/new items, pending count, cart quantity, or a compact status cue.
- The parent icon/destination remains the primary interaction target.

### Best practices

- Keep badge content minimal. Prefer a dot for presence/new-state when the exact count is
  not important; use a short number when count is useful.
- Keep the badge semantically associated with its parent control.
- If the information matters to assistive technology, expose it in the accessible name or
  state of the parent rather than relying on the visual badge alone.

### Anti-patterns

- Putting sentences or important instructions in a badge.
- Making the badge itself the only way to perform an action.
- Showing stale counts or decorative badges that imply nonexistent state.

Source: https://developer.android.com/develop/ui/compose/components/badges

---

## Progress indicator

**Purpose**: Communicate that work is happening and, when known, how much is complete.

### Use when

- Loading remote content, uploading/downloading, or processing that is perceptible enough
  to need feedback.
- Use **determinate** progress when reliable progress is known.
- Use **indeterminate** progress when completion percentage cannot be meaningfully known.
- Choose linear vs circular based on layout/context rather than inventing a semantic
  meaning that Material does not define.

### Best practices

- Prefer determinate feedback when true progress can be measured; it communicates more
  information than indefinite animation.
- Keep progress close to the process or region it describes when possible.
- Preserve an accessible status/value for long-running operations where the platform
  supports it.
- If the process fails, replace endless progress with an actionable error/retry state.

### Anti-patterns

- Fake progress percentages unrelated to actual work.
- An indeterminate spinner that can remain forever with no timeout/error path.
- Showing a full-screen blocking indicator for a tiny background refresh that does not
  prevent interaction.
- Animating progress aggressively when reduced-motion settings request less motion.

Sources:
- https://developer.android.com/develop/ui/compose/components/progress
- https://m2.material.io/components/progress-indicators

---

## Snackbar

**Purpose**: Brief, non-blocking feedback about an app process or action.

### Use when

- Confirming a lightweight result: item archived, message sent, preference updated.
- Offering one contextual follow-up action such as Undo.
- The user can continue the current task without resolving a modal interruption.

### Best practices

- Keep the message concise and outcome-focused.
- Show one snackbar at a time; queue/replace messages rather than stacking a column of
  transient alerts.
- Do not cover critical navigation, persistent actions, or a FAB.
- A single action is the Material pattern; Undo is a strong fit for reversible operations.
- Use a dialog or inline error when the user **must** respond before continuing.

### M2 vs current M3 timing

- **M2 archived guidance** describes snackbars as temporary and generally self-dismissing,
  including a 4–10 second range in the old specification.
- **Current M3 Compose guidance/API behavior** treats a snackbar containing an action more
  cautiously: it should not time out/self-dismiss before the user has a fair opportunity
  to act. Accessibility/platform timeout APIs also matter.
- Therefore, do **not** apply the M2 4–10 second rule as a universal M3 requirement.

### Anti-patterns

- Stacking multiple snackbars.
- Using a snackbar for a critical error, legal consent, or irreversible confirmation.
- Adding several action buttons or a complex form.
- Using iconography or a filled button simply to increase visual weight when the Snackbar
  specification does not call for it.
- Repeating persistent information that belongs inline in the page.

Sources:
- https://developer.android.com/reference/kotlin/androidx/compose/material3/Snackbar
- https://m2.material.io/components/snackbars

---

## Banner (M2 legacy pattern)

**Purpose**: In M2, display a prominent message and related actions near the top of a
screen, without using a dialog.

### Use when

- Maintaining an M2 interface that already uses banner semantics.
- The message is more persistent/important than a snackbar but does not require a modal
  decision.

### Migration guidance

Do not automatically introduce an M2 Banner into a new M3 design. Re-evaluate the job:
inline status, snackbar, dialog, card, or platform-specific notification pattern may be a
better current M3 fit. Verify the current target library before claiming a direct M3
replacement.

Source: https://m2.material.io/components/banners
