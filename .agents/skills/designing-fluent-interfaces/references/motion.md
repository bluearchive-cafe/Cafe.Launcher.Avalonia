# Motion

## Purpose

Motion exists to explain change, maintain context, provide feedback, and direct attention. If removing an animation makes the interface easier to understand with no loss of state/relationship information, the animation is probably decorative.

## Fluent 2 motion principles

- **Functional** — reveals next steps, confirms state changes, or celebrates meaningful completion.
- **Natural** — movement feels physically plausible through inertia, weight, velocity, and easing.
- **Consistent** — similar transitions behave similarly across the product.
- **Appealing** — delight is welcome only after function and consistency are satisfied.

## Core transition patterns

### Enter and exit

Use when UI appears/disappears, including menus, dialogs, drawers, and transient surfaces. Entry and exit should reinforce where the surface came from and how it relates to its trigger.

### Elevation

Use motion/elevation changes to communicate hover, press, drag/drop, or changes in spatial priority. Avoid dramatic Z-axis movement for ordinary state changes.

### Top-level transition

For large top-level pages/destinations, Fluent 2 recommends a quick fade rather than sliding whole screens around. Large movement can imply an unintended spatial hierarchy and increase disorientation.

### Container transform

Use resize/reposition animation when a container changes layout while remaining conceptually the same object. Preserve continuity rather than cross-fading unrelated geometry.

## Choreography

### Staggering

Short staggering can soften the entry of a set and direct gaze. Avoid long cascades that delay usability.

- Keep offsets short.
- Consider the parent's animation duration.
- Large collections may need little or no staggering if the total sequence becomes slow.

### Hierarchy

Give important elements slightly more motion emphasis; synchronize less-important elements so they read as a group. Avoid simultaneous unrelated movement across the whole viewport.

## Windows motion principles

Windows frames motion as:

- **Connected** — position/size changes preserve object continuity.
- **Consistent** — surfaces sharing an entry point invoke/dismiss similarly.
- **Responsive** — motion responds directly to input, posture, orientation, and system behavior.

## Windows standard timing

Prefer WinUI ThemeResources when available:

| Resource | Duration |
| --- | ---: |
| `ControlFasterAnimationDuration` | 83ms |
| `ControlFastAnimationDuration` | 167ms |
| `ControlNormalAnimationDuration` | 250ms |

Additional Windows motion guidance uses 167, 250, and 333ms depending on travel distance and purpose.

### Windows easing

**Entrance / fast-out-slow-in**

`cubic-bezier(0, 0, 0, 1)`

Use for UI entering or quickly settling into place.

**Exit / slow-out-fast-in**

`cubic-bezier(1, 0, 1, 1)`

Use for UI leaving the scene.

Windows also documents a point-to-point curve for existing elements:

`cubic-bezier(0.55, 0.55, 0, 1)`

Direct exits using position/scale/rotation should combine with fade-out rather than disappearing spatially without opacity support.

## Web implementation

For Fluent UI React, prefer motion tokens and the Fluent motion package rather than freezing numeric values in component CSS. The library exposes semantic duration/easing tokens such as:

- `durationUltraFast`, `durationFaster`, `durationFast`, `durationNormal`, `durationGentle`, `durationSlow`, `durationSlower`, `durationUltraSlow`
- `curveAccelerate*`, `curveDecelerate*`, `curveEasyEase*`, `curveLinear`

Use the package's current tokens as source of truth because library values can evolve independently of older design references.

## Accessible motion

Always provide a reduced/no-motion path.

- Respect OS/browser reduced-motion preferences.
- Keep durations short.
- Avoid flashes, large sudden movement, excessive parallax, or unrelated peripheral motion.
- Keep motion constrained to the active task/element.
- Never make animation the sole carrier of state information.
- Dynamic state changes still need semantic announcements where appropriate, such as ARIA live regions on the web.

## Motion anti-patterns

- Animating every layout change because “Fluent has motion.”
- Sliding whole top-level pages when a fade would preserve orientation better.
- Long spring/bounce animations for routine commands.
- Staggering hundreds of rows.
- Different easing/duration for every custom component.
- Blocking input until a purely decorative animation completes.
- Ignoring reduced-motion settings.
