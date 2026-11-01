# Project Audit State

This file is the single source of truth for current audit findings, accepted risks, and priorities. Git history retains resolved findings.

Use this shape for a new report. Preserve an existing report's local conventions.
Organize findings by priority, then domain; keep advisories compact. Include
verified system facts only when they close a concern or explain current risks.

## Current Assessment

- Date:
- Commit:
- Mode:
- Scope:
- Project profile:
- Health:
- Open counts:
- Accepted-risk count:

Current priority order:

List the most important current actions and pending decisions.

## Open Findings

### AUD-XXX-001 — Title

- Severity:
- Confidence:
- Category:
- Status:
- Disposition:
- Recommendation validation:

Evidence locations and impact.

Recommended action and completion guard.

For architecture/product decisions, describe the decision, evidence, viable
options, and tradeoffs. Present an option as mandatory only when repository
rules or validated constraints require it.

## Accepted Risks

| ID | Current tradeoff | Reopen when |
| --- | --- | --- |

## Verified Engineering State

Include only facts that materially explain the current findings or prevent unnecessary work.

## Verification Limits

List commands actually completed, external verification, environments not
exercised, and areas skipped because of scope. State the actual coverage of
inspection; do not imply a line-by-line review without having performed one.

## Maintenance Rules

- Add only verified, decision-useful findings.
- Reuse stable IDs for the same root cause.
- Remove resolved entries; Git history retains them.
- Refresh metadata, counts, priorities, and verification limits together.
