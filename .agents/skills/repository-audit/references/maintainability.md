# Maintainability Audit

## Purpose

Find patterns that make future change disproportionately expensive, risky, or difficult to diagnose.

## High-value signals

- duplicated business logic or test infrastructure;
- shotgun surgery/change amplification;
- hidden coupling;
- inconsistent error handling;
- dead/replaced entry points;
- giant coordinators with unrelated responsibilities;
- duplicated constants/contracts;
- misleading comments/configuration drift;
- bypassed repository abstractions;
- fragile repeated patterns;
- unclear ownership.

Textbook smells such as feature envy, data clumps, primitive obsession, or large classes are investigation prompts, not findings by themselves.

## Error handling

A swallowed exception becomes a meaningful finding when it hides user-visible failure, persistence loss, diagnostic information, or a repository-defined logging path.

## Dead code

Use reference/search/tool evidence. Do not label code dead solely because the current inspection did not find a caller.

## Documentation drift

Report documentation drift when maintainers could make a wrong engineering decision because of it—for example, a comment claiming CI automatically enables a build property when workflows never do.
