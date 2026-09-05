# Finding Lifecycle and Incremental Audits

## State files

Maintain:

- `.repository-audit/findings.json`
- `.repository-audit/audit-state.json`
- `.repository-audit/history/`

Use the templates under `templates/`.

## Lifecycle states

Use:

- `open`
- `resolved`
- `accepted-risk`
- `deferred`
- `architecture-decision`
- `product-decision`
- `false-positive`
- `superseded`

Do not delete historical findings when resolved.

## Stable IDs

Prefer IDs such as `AUD-DEP-001` or `AUD-PERF-003`. Reuse the same ID when the same root cause persists across audits. Create a new ID for a genuinely different root cause.

## Delta reconciliation

For a delta audit:

1. read the previous audited commit;
2. identify changed files/modules;
3. map changes to risk domains;
4. re-check affected open/deferred/decision findings;
5. mark resolved findings only with evidence;
6. detect recurrence or regression;
7. inspect newly exposed risks;
8. skip unaffected deep passes.

## Current vs historical reports

`CODEBASE_AUDIT.md` is a current-state view. Archive dated reports under `.repository-audit/history/`.

If a High finding was fixed after the last full audit, the current report must show it as resolved rather than continue presenting stale evidence as open.

## Guard conversion

When a recurring finding is fixed, ask whether a test/analyzer/CI contract can prevent recurrence. Record the guard in the finding ledger and reduce future manual audit effort for that pattern.
