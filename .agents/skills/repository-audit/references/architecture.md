# Architecture Audit

## Purpose

Determine whether system structure makes change predictable and whether documented boundaries match reality.

## Inspect

- layer and feature boundaries;
- dependency direction/cycles;
- composition-root discipline;
- service locator or hidden global dependencies;
- framework/domain leakage;
- cross-feature concrete dependencies;
- giant coordinators/application shells;
- duplicate abstractions;
- hidden injection channels (static hooks, mutable delegates, globals);
- ownership of shared contracts;
- documentation vs implementation.

## Evidence patterns

A direct dependency is not automatically wrong. Compare it with repository rules and the component's architectural role.

Example decision point:

`Shell` is stored under `Features/` but aggregates all feature ViewModels. Two valid remedies may exist: promote Shell to an application layer, or keep it as a feature and introduce narrow shared abstractions. Classify this as `Architecture Decision` until the repository's intended model is clear.

## Composition root

Runtime dependency resolution should normally be concentrated at composition boundaries. Investigate repeated manual construction, runtime container lookups, lifetime mismatches, and hidden static dependencies.

## Reporting

Prefer one root-cause finding over many symptom findings. If several violations stem from an unclear application-shell boundary, report the boundary decision rather than every import separately.
