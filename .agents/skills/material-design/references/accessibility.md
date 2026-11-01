# Accessibility checklist

Check the items applicable to the affected component, platform, and behavior. Follow existing project contracts for implementation and verification; record checks that could not be exercised.

## Visual
- [ ] Text contrast follows WCAG: 4.5:1 for normal text; 3:1 for large text (18pt regular or 14pt bold and equivalent)
- [ ] Icon and UI component contrast ≥ 3:1
- [ ] Error states use icon + text, not just color
- [ ] Focus indicators are visible on all interactive elements
- [ ] Disabled state is distinguishable but does not disappear

## Interaction
- [ ] Touch interfaces provide at least the platform-recommended target size (Material/Android commonly 48dp × 48dp), even if the visual container is smaller
- [ ] On keyboard-capable platforms, every interactive element is reachable by keyboard
- [ ] Keyboard behavior follows platform/ARIA conventions (for example Enter/Space activation, Escape dismissal, arrow-key menu navigation on web)
- [ ] Focus order follows visual order
- [ ] Popups, dialogs, and sheets are dismissible without gesture-only paths

## Semantics
- [ ] Icon-only buttons have accessible names (aria-label / contentDescription)
- [ ] Images have alt text or are marked decorative
- [ ] Form inputs have associated labels (not placeholder-only)
- [ ] Error messages are linked to their fields
- [ ] Current page/selection state is announced to screen readers
- [ ] Dynamic content changes use live regions where appropriate

## Motion
- [ ] Reduced-motion/system motion preferences are respected on the target platform
- [ ] No auto-playing video or animation without pause/stop
- [ ] No flashing content above 3 flashes/second
- [ ] Animation does not block task completion
