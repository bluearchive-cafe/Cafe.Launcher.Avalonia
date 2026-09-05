# Execution and Scoping

## 1. Repository Discovery

Inspect high-information files first: README, AGENTS/CLAUDE/CONTRIBUTING, architecture docs/ADRs, solution/project manifests, dependency manifests, build props, CI workflows, scripts, installer/packaging, tests, and release configuration.

Create or refresh `.repository-audit/repository-map.md` from `templates/repository-map.md`.

Skip generated output, package caches, vendored dependencies, binaries, build artifacts, and large snapshots unless directly relevant.

## 2. Load Project Rules

Extract explicit engineering contracts: `must`, `must not`, `never`, `only`, architectural boundaries, naming/placement rules that affect behavior, release requirements, test commands, and security constraints.

Do not infer identifier spelling, paths, payload shape, or architectural rules when defining evidence exists in the repository.

## 3. Build a Risk Profile

Load `references/profiles.md`. Determine project type, platforms, data sensitivity, distribution model, critical operations, and failure costs. Apply a profile under `profiles/` when appropriate, then adapt it to repository evidence.

## 4. Select Scope

Load `references/modes.md` and select `full`, `delta`, `release`, `refactor`, `security`, `testing`, or `performance`.

Default to `delta` when a recent compatible baseline exists. Use `full` for first audit, major architecture change, or stale baseline.

## 5. Establish an Execution Budget

A full audit means all important engineering domains were considered, not that every line was read.

For large repositories:

1. map modules;
2. identify risk-heavy subsystems;
3. use tools/search to locate relevant evidence;
4. inspect representative critical paths;
5. deepen only where evidence warrants it.

## 6. Tool Priority

Prefer:

1. repository-native verification scripts and tests;
2. ecosystem-standard analyzers/package-manager commands;
3. targeted source/configuration inspection;
4. AI heuristic reasoning.

Never claim a command/test passed unless it was actually executed successfully.

## 7. Audit Passes

Architecture, security, dependency, testing, performance, and maintainability are logical passes. They may run sequentially, in parallel, or through one agent. Parallel subagents are an implementation detail, not a requirement.

## 8. Candidate → Verified Finding

For each candidate:

1. read the implementation;
2. trace relevant callers/callees;
3. inspect tests;
4. inspect configuration/CI;
5. check repository rules;
6. determine intentional design;
7. reproduce or use tool evidence when practical.

Then validate the recommendation separately.

## 9. Finalization

Reconcile lifecycle state, update the current report, archive historical reports, and propose automated guards where a recurring pattern can be mechanically prevented.
