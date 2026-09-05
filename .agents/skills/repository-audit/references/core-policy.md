# Core Policy

## Objective

Repository Audit should answer whether the repository is structurally healthy, where systemic engineering risks exist, what matters most, and which risks should be fixed, documented, accepted, or decided by humans.

The unit of analysis is the **engineering system**: source, tests, configuration, CI, dependencies, release flow, runtime boundaries, and repository rules.

## Principles

### High confidence over high quantity

Report only issues with clear evidence, material impact, and a useful action or decision. Five meaningful findings are better than fifty speculative observations.

### Understand before judging

Before findings, identify project type, supported platforms, architecture, build and release model, test strategy, trust model, and repository-specific conventions.

### Repository rules are first-class evidence

Prefer the most specific applicable rule:

1. directory/module rule,
2. root repository rule,
3. documented architecture/ADR,
4. ecosystem convention,
5. generic engineering preference.

If implementation and documentation disagree, report the drift rather than silently choosing one.

### Finding verification and recommendation validation are separate

A correct diagnosis can still have a wrong fix. Concrete remedies must be checked against framework/tool behavior, platform constraints, repository rules, and operational feasibility.

### Prefer automation over repeated findings

For recurring patterns, the preferred lifecycle is:

`find → fix → add guard → stop rediscovering manually`.

Useful guards include tests, analyzers, architecture contracts, CI checks, dependency policies, and generation validation.

### Respect the project's risk model

Do not give every audit category equal weight. A desktop updater, backend API, kernel module, and static website have different failure costs.

## Non-goals

Do not:

- replace compiler/static-analysis/CI output;
- perform arbitrary style review;
- demand arbitrary coverage percentages;
- recommend rewrites because a technology is old;
- report a large file, concrete type, HTTP URL, small package, or missing abstraction as a defect by itself;
- repeat issues already reliably prevented by automation;
- manufacture work to make the repository look cleaner.

## Positive verification

Record a healthy area only when doing so closes a meaningful concern, documents an important invariant, or prevents unnecessary refactoring. Do not fill reports with praise.

## Guardrail Against False Positives

Before reporting a suspicious pattern, ask:

- Is there direct evidence of harmful behavior?
- Is this intentional and documented?
- Is there already a guard or test?
- Does it matter under the actual threat/usage model?
- Would a reasonable maintainer make a different decision?

If evidence remains weak, downgrade to advisory or omit it.
