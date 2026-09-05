# Audit Modes

## default

Choose the narrowest mode that answers the user's goal. Prefer `delta` when a recent baseline exists; otherwise use `full`.

## full

Use for first audit, ownership transfer, major architecture change, or stale baseline. Consider all relevant domains and create a fresh repository map/risk profile.

## delta

Compare current state with the last audited commit. Focus on changed subsystems, unresolved findings, invalidated assumptions, regressions around previous fixes, and newly exposed risks. Do not re-prove unaffected healthy areas.

## release

Prioritize build reproducibility, dependency locking, supply-chain permissions, release tokens, packaging, artifact integrity, signing, migrations/state preservation, critical tests, platform-specific behavior, and upgrade/rollback risk.

## refactor

Prioritize architecture boundaries, dependency cycles, unstable interfaces, giant coordinators, change amplification, duplication, hotspots, test protection, and migration risk.

## security

Prioritize trust boundaries, remote input, filesystem paths, process execution, unsafe deserialization, redirects/TLS/SSRF, privileged operations, CI tokens, dependency trust, release artifacts, and sensitive logging.

## testing

Prioritize critical-path protection, flakiness, timing assumptions, bounded waits, isolation, shared mutable state, platform assumptions, failure diagnostics, duplicated test infrastructure, and CI execution.

## performance

Prioritize startup, UI thread work, filesystem/network IO, hashing, image decoding, cache invalidation, retries, serialization, large collections, resource lifetime, and algorithmic hot paths.

## Mode-to-reference routing

Always load core/execution/findings. Load only the domain references needed by the mode. `full` normally loads all six domain references; focused modes do not.
