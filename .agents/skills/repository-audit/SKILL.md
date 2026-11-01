---
name: repository-audit
description: Use when assessing repository-wide engineering health, preparing a release or refactor, investigating systemic risks across modules or CI, or re-checking a codebase after a previous audit.
---

# Repository Audit

## Overview

Audit the repository as an engineering system, not as a collection of isolated files. Prefer a small number of verified, decision-useful findings over exhaustive smell lists.

**Core principle:** understand the project first; verify both the problem and the proposed remedy.

## Required Loading

Always load:

- `references/core-policy.md`
- `references/execution.md`
- `references/findings.md`
- `references/lifecycle.md` for the single audit-state file and reconciliation rules

Then load only the domain references relevant to the selected scope:

| Domain | Reference |
|---|---|
| Architecture | `references/architecture.md` |
| Maintainability | `references/maintainability.md` |
| Security | `references/security.md` |
| Dependencies / supply chain | `references/dependencies.md` |
| Testing | `references/testing.md` |
| Performance | `references/performance.md` |
| Git history / hotspots | `references/git-history.md` |

For mode routing, load `references/modes.md`. For project-specific weighting, load `references/profiles.md` and an applicable file under `profiles/`.

## Execution Contract

1. Inspect the repository's entry points and rules, then select a mode and scope before deep inspection.
2. Build a risk profile from repository evidence.
3. Prefer repository-native tools and existing analyzers over heuristic reimplementation.
4. Generate candidate findings from relevant audit passes.
5. Verify each reportable finding against source, callers, tests, configuration, and project rules.
6. Validate concrete recommendations separately; downgrade unverified remedies.
7. Reconcile current findings with prior audit state when available.
8. Generate the current report and suggest automated guards for recurring patterns.

Follow `references/lifecycle.md` for `CODEBASE_AUDIT.md` ownership. Use `templates/report.md` for a new report; preserve an existing report's maintenance rules and record only the current known state. Report an assessment without changing the ledger when the user requests a read-only review.

## Verification

When editing this skill package, run from the repository root:

```powershell
python -X utf8 .agents/skills/repository-audit/scripts/validate-skill.py
python -B -X utf8 -m unittest discover -s .agents/skills/repository-audit/tests -p 'test_*.py'
```

The validator's automated checks are in `tests/test_validate_skill.py`; agent-behavior scenarios are in `tests/scenarios.md`.
