# Report Generation

Use `templates/report.md` as the canonical shape.

## Executive summary

State:

- audit mode and commit;
- overall health in plain language;
- open Critical/High/Medium counts;
- resolved findings since the prior audit;
- decisions required;
- top 3–5 risks/actions.

## Main body

Organize by priority first, then domain. Keep advisories compact. Include positive verification only where it closes a meaningful concern.

## Decision findings

For architecture/product decisions, present the decision, evidence, viable options, and tradeoffs. Do not frame one option as mandatory unless validated by repository rules or hard constraints.

## Resolved findings

Include enough evidence to show that the underlying root cause changed and, when possible, note the guard that prevents recurrence.

## Audit method

Record commands/tools actually executed, important limitations, external verification used, and areas intentionally skipped because of scope.

Never imply exhaustive line-by-line review unless that actually occurred.
