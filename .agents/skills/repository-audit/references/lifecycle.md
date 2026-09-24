# Finding Lifecycle

## Single state file

Maintain audit state only in the repository-root `CODEBASE_AUDIT.md`. It contains:

- audit date, commit, scope, and verification limits;
- current open findings;
- explicitly accepted risks and their reopening conditions;
- the current priority order;
- concise verified system facts needed to interpret the findings.

Git history is the historical record. Do not create dated reports, JSON ledgers, repository maps, candidate lists, or audit archive directories.

## Lifecycle states

Use these states inside the current report:

- `open`: actionable now;
- `accepted-risk`: intentionally retained, with rationale and a reopening condition;
- `deferred`, `architecture-decision`, or `product-decision`: retain only when a concrete decision is still pending.

When a finding is resolved or proved false, remove it from the current report. The resolving commit and prior report revision remain available through Git.

## Stable IDs

Prefer IDs such as `AUD-DEP-001` or `AUD-PERF-003`. Reuse the ID while the same root cause persists. Assign a new ID to a different root cause. Never renumber current entries merely to make the sequence contiguous.

## Delta reconciliation

For a delta audit:

1. read the previous `CODEBASE_AUDIT.md` from the last audited commit;
2. identify changed files and risk domains;
3. re-check affected open, accepted, deferred, and decision findings;
4. remove entries only with evidence that they are resolved or no longer real;
5. add only newly verified, decision-useful findings;
6. refresh the report metadata, counts, priorities, and verification limits.

## Guard conversion

When a recurring finding is fixed, add a test, analyzer, CI check, or contract when that guard has favorable maintenance cost. The current report should describe the guard only while it remains relevant to an open or accepted item.
