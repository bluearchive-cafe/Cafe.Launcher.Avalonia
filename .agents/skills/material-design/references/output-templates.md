# Output templates

## Component specification template

For a component specification, select the fields needed for the requested component. Omit inapplicable states and platform details:

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

## Design audit template

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

## Material 2 output mode

For a complete M2 theme or system specification, the following outline is useful. A focused M2 question needs only the relevant sections:

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
