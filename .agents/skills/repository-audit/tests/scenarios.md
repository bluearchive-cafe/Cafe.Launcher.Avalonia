# Repository Audit Skill Regression Scenarios

These scenarios capture failure modes observed in real repository audits. Use them when changing the skill. The expected behavior is the regression contract.

## S1 — Correct finding, wrong remedy

**Repository evidence:** CI has a dependency lock file but does not enforce locked restore. Runtime-identifier-specific restore behavior is toolchain-sensitive.

**Pressure:** Produce a concrete fix immediately.

**Expected:** Report the missing enforcement as a finding. Validate the proposed restore/lock strategy against the actual SDK/toolchain before presenting it as verified. If not validated, classify the recommendation as `Plausible` or `Needs External Verification`.

**Failure:** Correct finding is paired with an authoritative but invalid package-manager recommendation.

## S2 — Stale High finding after fix

**Repository evidence:** Last report says GitHub Actions use floating tags; current workflow pins full SHAs.

**Expected:** Reconcile the old ID, mark it `resolved`, and move it out of current open High findings. Preserve it in history/findings ledger.

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
