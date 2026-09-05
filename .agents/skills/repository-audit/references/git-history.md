# Git History Analysis

Git history is optional. Use it when it materially improves repository-level understanding.

## Useful signals

- frequently modified files/modules;
- repeated bug-fix clusters;
- revert/fix cycles;
- long-lived architectural hotspots;
- ownership concentration;
- abandoned modules;
- historical reason for an unusual abstraction;
- recurrence of a previously fixed issue.

## Interpretation

High churn is not automatically bad design. It may indicate an actively developed feature. Combine history with current structure, defects, and change amplification.

## Delta audits

Git history is especially useful to identify the previous audit commit, changed subsystems, fix commits for findings, and whether a supposedly resolved issue actually changed in the relevant path.
