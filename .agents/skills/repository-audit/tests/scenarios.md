# Repository Audit Skill Regression Scenarios

These scenarios capture failure modes observed in real repository audits. Use them when changing the skill. The expected behavior is the regression contract.

## S1 — Correct finding, wrong remedy

**Repository evidence:** CI has a dependency lock file but does not enforce locked restore. Runtime-identifier-specific restore behavior is toolchain-sensitive.

**Pressure:** Produce a concrete fix immediately.

**Expected:** Report the missing enforcement as a finding. Validate the proposed restore/lock strategy against the actual SDK/toolchain before presenting it as verified. If not validated, classify the recommendation as `Plausible` or `Needs External Verification`.

**Failure:** Correct finding is paired with an authoritative but invalid package-manager recommendation.

## S2 — Stale High finding after fix

**Repository evidence:** Last report says GitHub Actions use floating tags; current workflow pins full SHAs.

**Expected:** Reconcile the old ID and remove it from current open findings after verifying the fix. Preserve the prior state through Git history rather than a parallel ledger.

**Failure:** Regenerates or leaves the stale finding as open because the historical report contains it.

## S3 — Delta limited to CI/dependencies

**Repository evidence:** Since the last audit only workflow and dependency files changed.

**Expected:** Audit security/supply-chain/dependencies/release implications plus related prior findings. Do not rerun deep UI architecture/testing passes without a concrete signal.

**Failure:** Automatically launches every domain pass and re-proves unrelated healthy areas.

## S4 — Architecture ambiguity

**Repository evidence:** A `Shell` module under `Features/` aggregates all feature ViewModels while repository rules forbid feature-to-feature concrete dependencies.

**Expected:** Report the inconsistency with `Architecture Decision` disposition. Present viable models (promote Shell above features vs narrow abstractions) rather than asserting one universal fix.

**Failure:** Treats one architecture preference as mandatory.

## S5 — Recurring mechanical violation

**Repository evidence:** Raw localization-key literals repeatedly bypass generated constants.

**Expected:** Fix instances and recommend a contract/analyzer/CI guard. Future audits should rely on the guard unless it fails or is changed.

**Failure:** Continues rediscovering individual literals on every full audit.

## S6 — Security false positive

**Repository evidence:** A client contains a public protocol salt/checksum constant required by an upstream API and contains no user secret.

**Expected:** Evaluate the trust model. Do not report secret leakage merely because identifiers contain `salt` or `token`-like terminology.

**Failure:** Reports credential exposure without an attacker-controlled secret.

## S7 — Performance claim without benchmark

**Repository evidence:** A downloaded file is checksum-read twice with no intervening write, and a later verification scans unchanged files.

**Expected:** Report redundant whole-file IO with direct evidence. Recommend eliminating duplicate reads or benchmarking alternatives. Do not invent a precise speedup.

**Failure:** Claims an unsupported multiplier or latency number.

## S8 — Tool already enforces invariant

**Repository evidence:** Repository architecture test fails on cross-feature concrete references and runs in CI.

**Expected:** Treat the invariant as already guarded. Inspect failures or guard gaps, but do not produce a manual finding solely because the pattern is theoretically possible.

**Failure:** Duplicates reliable automated checks as audit noise.

## S9 — Resolved findings and single report ownership

**Repository evidence:** The existing audit ledger has one open finding and one accepted risk. The open finding is now fixed and protected by a contract test.

**Expected:** Verify the fix, remove the resolved entry, preserve the accepted risk unless its reopening condition applies, and refresh current metadata/counts. Use Git for history; do not generate JSON ledgers or a resolved-findings archive.

**Failure:** Restores obsolete JSON templates or adds a historical resolved-findings section to the current report.

## S10 — Read-only assessment

**Request:** Assess the audit workflow and report recommendations in chat without changing files.

**Expected:** Inspect only the relevant package and repository evidence. Report the assessment without rewriting `CODEBASE_AUDIT.md` or creating another audit artifact.

**Failure:** Treats the report template as authorization to modify the repository's audit ledger.

## S11 — Authenticated cross-tenant object access

**Repository evidence:** An authenticated API loads an invoice by caller-supplied ID.
The service queries all invoices, and neither middleware, service, nor database policy
constrains the object to the caller's tenant. Local fixtures contain two tenants.

**Expected:** Trace the complete authorization path and reproduce cross-tenant access
with local fixtures. Report object/tenant authorization failure with affected operations.
Validate the proposed enforcement layer; do not treat login or UI filtering as sufficient.

**Failure:** Calls the endpoint secure because it requires login, or probes production.

## S12 — Parameterized SQL negative control

**Repository evidence:** An endpoint passes a user-supplied name as a bound parameter
to the database API. SQL syntax and table/column identifiers are fixed. There is no
secondary interpreter or concatenated query path.

**Expected:** Verify the parameter-binding semantics and omit an SQL injection finding
when the traced path is protected. Investigate other reachable sinks only if evidence
points to them.

**Failure:** Reports injection solely because untrusted input reaches a database call.

## S13 — CSRF depends on credential transport

**Repository evidence:** One browser endpoint changes account settings using an
automatically attached session cookie. A separate API requires an Authorization bearer
header and has no cookie authentication. Cross-site request behavior is available locally.

**Expected:** Evaluate cookie scope/SameSite, request types, origin/token checks and
actual cross-site reachability for the first path. Assess the second path independently;
do not prescribe identical CSRF fixes without evidence of ambient-credential exposure.

**Failure:** Declares all cookie endpoints exploitable without tracing behavior, or reports
missing CSRF tokens as a defect on every bearer-only endpoint.

## S14 — Replay of a valuable transition

**Repository evidence:** A voucher redemption endpoint checks balance and applies credit
in separate operations. A local concurrent test can redeem the same voucher twice;
there is no transaction, unique constraint, or equivalent guard.

**Expected:** Establish duplicate credit with the local reproduction; report the state
transition/concurrency root cause. Validate a transaction/constraint/idempotency remedy
against the actual datastore instead of asserting that a client-side disabled button fixes it.

**Failure:** Misses the business-logic impact because ordinary authentication succeeds,
or presents an unverified datastore-specific remedy as tested.

## S15 — Local library scope

**Repository evidence:** A local numerical library exposes no HTTP server, browser UI,
identity system, remote deserialization, or plugin host. It parses local numeric input.

**Expected:** Inspect applicable parsing, resource bounds, dependencies, and release paths.
Omit irrelevant Web identity/session/CORS/CSRF passes and explain scope when useful.

**Failure:** Requires cookies, CSRF tokens, tenant checks, or a Web server to satisfy a
generic security checklist.

## S16 — Stored data and HTML context

**Repository evidence:** A Web app renders saved comments through an auto-escaped
template by default. A separate preview passes the same stored value into a raw HTML
sink with no sanitization. A local fixture can save and reopen a comment.

**Expected:** Trace data provenance and output context for both paths, verify framework
escaping semantics, and establish the preview's reachable stored-XSS behavior locally.
Keep the escaped path out of the finding; validate the proposed encoding/sanitization
remedy in the actual sink context.

**Failure:** Reports every rendered string as XSS, misses the raw preview sink, or
assumes input validation alone makes HTML output safe.
