# Repository Risk Profiles

A risk profile controls audit attention; it is not a scoring formula.

## Profile fields

Capture:

- project type;
- primary languages/frameworks;
- supported platforms;
- distribution/deployment model;
- persistent user data;
- remote input/trust boundaries;
- privileged operations;
- critical workflows;
- high-cost failures;
- domain weights.

Use `templates/profile.yaml` as a starting point.

## Weight vocabulary

- `critical`: always inspect deeply in a full/release audit.
- `high`: inspect unless clearly irrelevant to scope.
- `medium`: inspect when touched or when evidence points there.
- `low`: sample or skip unless a concrete signal exists.

## Adaptation rule

A bundled profile is only a default. Repository-specific documentation and implementation override it.

For desktop launchers/updaters, `profiles/desktop-launcher.md` provides a useful baseline.
