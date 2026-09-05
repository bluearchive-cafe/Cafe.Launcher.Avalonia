---
name: repository-audit
description: 
  Audit the health of the current repository across architecture,
  maintainability, security, dependencies, testing, and technical debt.
  Use when reviewing an unfamiliar codebase, preparing a release,
  planning refactoring, or assessing repository quality.
---

# Repository Audit Skill

> A repository-level engineering health audit workflow for AI coding agents.

## Overview

`repository-audit` is an AI-assisted repository analysis skill designed to evaluate the overall health of a codebase.

Unlike a Pull Request review skill, which focuses on whether a specific change is correct, this skill analyzes the current state of an entire repository.

Its goal is not to discover every possible issue, but to identify **high-confidence engineering risks** across:

- Architecture
- Maintainability
- Security
- Dependencies
- Testing
- Performance
- Technical debt

---

# Design Goals

## Primary Goals

The skill should answer:

1. Is this repository structurally healthy?
2. Are there architectural problems that will increase future maintenance cost?
3. Are there security risks?
4. Are important components sufficiently tested?
5. Are dependencies safe and maintainable?
6. Are there obvious performance risks?
7. What engineering improvements should be prioritized?

---

## Non-goals

This skill should not:

- Replace compiler errors
- Replace static analyzers
- Replace CI checks
- Perform arbitrary style reviews
- Review individual commits

For commit or PR-level review, use a dedicated code review workflow.

---

# Relationship With Code Review Skills

Repository Audit and Code Review solve different problems.

```
                 Software Engineering

                         |
        ------------------------------------
        |                                  |
   Code Review                    Repository Audit

   Change quality                 Codebase health

   Diff based                     Repository based

   "Is this PR correct?"           "Is this project healthy?"
```

---

# Core Principles

## 1. High Confidence Over High Quantity

The skill should avoid producing large numbers of speculative findings.

Only report issues that:

- Have clear evidence
- Affect maintainability, correctness, security, or reliability
- Are actionable

---

## 2. Understand Before Judging

The workflow should first understand:

- Project type
- Framework
- Architecture
- Repository conventions
- Existing documentation

before generating findings.

---

## 3. Separate Findings From Suggestions

Each finding should have:

```
Issue
Evidence
Impact
Confidence
Recommendation
```

Avoid mixing:

- confirmed risks
- possible improvements
- personal preferences

---

# Directory Structure

Recommended layout:

```
.claude/

└── skills/

    └── repository-audit/

        ├── SKILL.md

        ├── agents/

        │   ├── architecture.md
        │   ├── maintainability.md
        │   ├── security.md
        │   ├── dependency.md
        │   ├── testing.md
        │   └── performance.md

        └── templates/

            └── report.md
```

---

# Skill Metadata

Example:

```yaml
---
name: repository-audit

description:
  Audit the current repository across architecture,
  maintainability, security, dependencies,
  testing and performance.
  Use when analyzing an unfamiliar codebase,
  preparing releases, planning refactors,
  or evaluating repository health.
---
```

---

# Workflow

```
Repository Audit

        |
        v

Repository Discovery

        |
        v

Project Context Loading

        |
        v

Parallel Audit Agents

        |
        v

Finding Verification

        |
        v

Report Generation
```

---

# Phase 1: Repository Discovery

## Objective

Build a basic understanding of the repository.

---

## Files To Inspect

Common files:

```
README.md

CLAUDE.md

CONTRIBUTING.md

package.json

pom.xml

build.gradle

*.csproj

Cargo.toml

go.mod

requirements.txt
```

---

## Output

Generate:

```
.repository-audit/

    repository-map.md
```

Example:

```markdown
# Repository Map

Language:

- Kotlin
- C#

Framework:

- Android
- Avalonia

Architecture:

- MVVM

Modules:

- app
- core
- network
```

---

# Phase 2: Project Rules Discovery

The audit must understand repository-specific rules.

Search:

```
CLAUDE.md

CONTRIBUTING.md

ARCHITECTURE.md

docs/

ADR/
```

---

## Rule Priority

When multiple rules exist:

```
Specific directory rule

        ↓

Module rule

        ↓

Root repository rule
```

---

Example:

```
app/

 └── CLAUDE.md
```

has higher priority than:

```
CLAUDE.md
```

---

# Phase 3: Parallel Audit Agents

The audit should use specialized agents.

Recommended:

```
                Repository

                    |

    --------------------------------

    |        |        |        |

Architecture Security Testing Performance

    |

Maintainability

    |

Dependency
```

---

# Agent 1: Architecture Audit

## Purpose

Evaluate:

- Layer separation
- Module boundaries
- Dependency direction
- Architectural consistency


---

## Checks

Examples:

### MVVM

Expected:

```
UI

 ↓

ViewModel

 ↓

Repository

 ↓

DataSource
```

Problem:

```
Activity

 ↓

Database
```

---

## Output Example

```markdown
## Architecture Issue

Title:

UI directly accesses database


Location:

app/MainActivity.kt


Confidence:

88


Impact:

High


Recommendation:

Introduce repository layer.
```

---

# Agent 2: Maintainability Audit

## Purpose

Identify long-term maintenance problems.

---

## Based On Code Smell Concepts

Examples:

## Duplicated Code

Problem:

Same logic appears repeatedly.

Recommendation:

Extract shared abstraction.


---

## Feature Envy

Problem:

A method depends heavily on another object's data.

Recommendation:

Move behavior closer to the data.


---

## Data Clumps

Problem:

Same group of parameters repeatedly appears.

Recommendation:

Create a dedicated type.


---

## Primitive Obsession

Problem:

Primitive values represent domain concepts.

Example:

```csharp
string countryCode;
```

Better:

```csharp
CountryCode;
```

---

## Shotgun Surgery

Problem:

One change requires many unrelated file edits.

Recommendation:

Improve module boundaries.

---

# Agent 3: Security Audit

## Purpose

Identify security risks.

---

## Secret Detection

Search:

```
password

token

apikey

secret

private_key
```

Example:

```javascript
const apiKey="xxxxx";
```

---

## Dangerous APIs

Examples:

Java:

```java
Runtime.exec()
```

Python:

```python
eval()
```

.NET:

```csharp
Process.Start(userInput)
```

---

## Configuration Risks

Check:

- Debug enabled
- Weak TLS
- HTTP communication
- Unsafe storage

---

# Agent 4: Dependency Audit

## Purpose

Evaluate dependency health.

---

## Checks

### Deprecated packages

Example:

```
old framework version
```

---

### Abandoned dependencies

Example:

```
No updates for years
```

---

### Security risks

Example:

```
Known vulnerable version
```

---

# Agent 5: Testing Audit

## Purpose

Evaluate testing quality.

---

## Principles

Do not demand arbitrary coverage numbers.

Focus on:

- Critical business logic
- Security-related code
- Data migration
- Authentication
- Payment
- Core algorithms

---

Example finding:

```
AuthenticationManager has no automated tests.
```

---

# Agent 6: Performance Audit

## Purpose

Find potential performance problems.

---

## Checks

### Algorithm Complexity

Example:

```java
for(user){

    database.query();

}
```

Potential:

```
N+1 query problem
```

---

### Resource Management

Android:

- Context leaks
- Bitmap memory
- Coroutine lifecycle


.NET:

- IDisposable misuse
- Async blocking

---

# Phase 4: Finding Verification

All findings should be verified.

---

## Finding Format

Example:

```json
{
"title":"Possible memory leak",

"category":"performance",

"file":"MainActivity.kt",

"line":120,

"confidence":85,

"reason":
"Coroutine scope survives Activity lifecycle"
}
```

---

# Confidence Rules

```
0-59

Discard


60-79

Advisory only


80-100

Report
```

---

# Phase 5: Report Generation

Generate:

```
CODEBASE_AUDIT.md
```

---

Example:

```markdown
# Repository Audit Report


Date:

2026-08-10


## Summary


Files analyzed:

532


High confidence issues:

7



# Critical Issues


## 1. Hardcoded Credential


Category:

Security


File:

config.json


Confidence:

95


Impact:

Critical


Recommendation:

Move secrets to environment variables.
```

---

# Audit Categories

Final report:

```
1. Critical Issues

2. Architecture

3. Security

4. Performance

5. Dependencies

6. Testing

7. Maintainability

8. Technical Debt
```

---

# Usage

## Analyze Current Repository

```
/repository-audit
```

---

## Release Audit

```
/repository-audit release
```

Focus:

- Security
- Dependencies
- Stability


---

## Refactoring Preparation

```
/repository-audit refactor
```

Focus:

- Architecture
- Technical debt
- Maintainability

---

# Integration With Other Skills

Recommended combination:

```
                    Development Workflow


                         |

        -----------------------------------

        |                                 |

   code-review                    repository-audit


   Review changes                Review repository


   PR / Branch                   Whole codebase


```

---

# Future Extensions

Possible additions:

## Architecture Diagram Generation

Generate:

```
architecture.md
```

with:

- Module graph
- Dependency graph
- Data flow


---

## Migration Planning

Generate:

```
REFACTOR_PLAN.md
```

including:

- Priority
- Risk
- Estimated effort


---

## Continuous Health Tracking

Store:

```
.audit-history/

    2026-08-10.md

    2026-09-01.md
```

Track:

- New risks
- Resolved issues
- Technical debt trend

---

# Final Principle

Repository Audit should behave like a senior engineer joining an unfamiliar project:

1. Understand the system.
2. Learn the rules.
3. Identify meaningful risks.
4. Explain evidence.
5. Recommend practical improvements.

It should not attempt to replace engineers.

It should improve engineering visibility.
