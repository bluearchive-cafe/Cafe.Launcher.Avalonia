# Dependency and Supply-Chain Audit

## Prefer ecosystem evidence

Use package-manager and repository-native tooling where available: vulnerability audit, outdated-package reports, lock files, central package management, dependency update automation, and license tooling.

## Inspect

- known vulnerable versions;
- deprecated/abandoned packages;
- maintenance concentration/bus factor where operationally relevant;
- lock-file existence **and enforcement**;
- inconsistent restore/publish behavior;
- transitive dependency drift;
- dependency update automation;
- unpinned CI tools/actions;
- licenses/notices;
- debug-only dependencies leaking into release artifacts.

## Reproducibility rule

Configuration presence does not prove configuration effectiveness. Trace the actual build/restore/release command path.

`lock file exists` is weaker evidence than `CI fails when lock and dependency graph disagree`.

## Recommendation validation

Package manager semantics are subtle. For important restore/lock/RID recommendations, validate against the actual SDK/toolchain using documentation or a minimal sandbox. A correct finding about missing enforcement does not make every proposed lock strategy correct.

## Small dependencies

Low downloads or single-maintainer ownership are risk signals, not automatic defects. Consider API surface, replaceability, tests/fixtures around the package, license, and fork fallback.
