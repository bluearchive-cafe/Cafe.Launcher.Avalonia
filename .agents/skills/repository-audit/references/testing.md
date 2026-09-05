# Testing Audit

## Goal

Assess whether tests reliably protect important behavior, not whether a numeric coverage target looks impressive.

## Critical coverage

Prioritize persistence, migrations, filesystem operations, downloads, security-sensitive paths, core algorithms, concurrency, recovery, platform behavior, and other project-specific critical workflows.

## Flakiness and hangs

Investigate:

- unbounded polling/loops;
- `Task.Delay(N)` then assert;
- wall-clock time dependencies;
- uncontrolled randomness;
- real external network dependencies;
- shared mutable static state;
- test-order dependencies;
- leaked temp files/resources;
- async operations without timeout/cancellation budget.

Every polling loop should have a deadline, iteration limit, or cancellation budget.

## Test architecture

Inspect duplicated fakes/hosts, giant test files, fixtures coupled to implementation, global serialization requirements, cleanup, deterministic teardown, and reusable wait helpers.

## CI integration

A test that does not run in CI may not provide real protection. Check platform gating and skipped critical scenarios.

## Failure diagnostics

For golden/snapshot/headless tests, useful failures preserve actual/expected/diff artifacts and contextual logs when practical.

## Avoid arbitrary coverage demands

Coverage is useful as a regression ratchet or discovery signal. Do not recommend tests for trivial adapters merely because a class has zero direct tests; explain the unprotected behavior.
