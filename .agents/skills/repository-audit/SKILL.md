---
name: repository-audit
description: Use when assessing repository-wide engineering health, preparing a release or refactor, investigating systemic risks across modules or CI, or re-checking a codebase after a previous audit.
---

# Repository Audit

## Overview

Audit the repository as an engineering system, not as a collection of isolated files. Prefer a small number of verified, decision-useful findings over exhaustive smell lists.

**Core principle:** understand the project first; verify both the problem and the proposed remedy.

## When to Use

Use for whole-repository health reviews, release readiness, refactoring preparation, systemic reliability/security/performance investigation, or follow-up audits.

Do not use as a replacement for compiler diagnostics, static analyzers, CI, dependency scanners, or PR review.

## Required Loading

Always load:

- `references/core-policy.md`
- `references/execution.md`
- `references/findings.md`

If a previous audit exists, also load `references/lifecycle.md`.

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

1. Select an audit mode and scope before deep inspection.
2. Load repository rules and build a risk profile.
3. Prefer repository-native tools and existing analyzers over heuristic reimplementation.
4. Generate candidate findings from relevant audit passes.
5. Verify each reportable finding against source, callers, tests, configuration, and project rules.
6. Validate concrete recommendations separately; downgrade unverified remedies.
7. Reconcile current findings with prior audit state when available.
8. Generate the current report and suggest automated guards for recurring patterns.

Use `templates/report.md` for the report shape and `templates/repository-map.md` for discovery output.

## Output Rule

`CODEBASE_AUDIT.md` represents the **current known state**. Historical reports belong under `.repository-audit/history/`. Do not leave resolved High/Critical findings presented as currently open.

## Verification

Before considering the skill package complete, run:

```bash
python scripts/validate-skill.py
```

Regression scenarios are documented in `tests/scenarios.md`.
