# Findings Model

## Required fields

Every reportable finding should capture:

- stable ID;
- title/category;
- evidence/location;
- impact;
- severity;
- confidence;
- lifecycle status;
- disposition;
- recommendation;
- recommendation-validation status;
- suggested guard when applicable.

## Evidence hierarchy

Strongest to weakest:

1. reproduced failure;
2. failing test/tool output;
3. direct source path proving behavior;
4. configuration plus execution path;
5. multiple independent references;
6. strong static inference;
7. heuristic suspicion.

Heuristic-only observations should normally be advisory or omitted.

## Confidence

Confidence measures whether the finding is real, not how severe it is.

Guidance:

- 0–59: discard;
- 60–79: advisory;
- 80–89: report;
- 90–100: strongly verified.

Useful calibration factors: direct evidence, reproduction/tool confirmation, multiple references, explicit rule violation; subtract for assumptions, environment uncertainty, incomplete call-chain visibility, or stylistic judgment.

## Severity

- `Critical`: likely credential compromise, arbitrary code execution, major data loss, release-integrity break, or severe outage.
- `High`: serious reliability/performance/maintenance/supply-chain risk.
- `Medium`: meaningful but moderate engineering/user impact.
- `Low`: real, limited concern.
- `Informational`: useful context that does not justify work now.

Severity must reflect the repository's real threat/usage model.

## Disposition

Use one of:

`Fix`, `Refactor`, `Document`, `Add Guard`, `Accept Risk`, `Architecture Decision`, `Product Decision`, `Investigate`, `No Action`.

## Recommendation validation

Use one of:

`Verified`, `Experimentally Verified`, `Strongly Supported`, `Plausible`, `Needs Architecture Decision`, `Needs Product Decision`, `Needs External Verification`.

Never present `Plausible` as proven.

## Output limits

Default guidance:

- Critical: all;
- High: up to 10;
- Medium: up to 15;
- Low: only when useful;
- Advisory: summarize.

Group symptoms with the same root cause.

## Prioritization

Use judgment roughly equivalent to:

`impact × confidence × project relevance × fix leverage`.

A fix has high leverage when it creates an automated invariant and removes a recurring class of failures.
