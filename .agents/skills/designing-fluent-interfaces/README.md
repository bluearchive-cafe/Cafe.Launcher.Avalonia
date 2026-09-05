# designing-fluent-interfaces

A portable Agent Skill for designing, implementing, and reviewing Microsoft Fluent 2 and Windows 11/WinUI interfaces.

## Why the Skill is split into references

The core `SKILL.md` stays concise so it can route the agent and enforce the design workflow. Detailed values and component rules live under `references/` and are loaded only when needed, following the Agent Skills progressive-disclosure model.

## Key design decision

The Skill deliberately separates:

- Fluent 2 cross-platform/Web guidance,
- Fluent UI React v9 implementation guidance,
- Windows 11 / WinUI guidance.

This prevents common errors such as copying Fluent Web shadow/color/radius tokens directly into WinUI.

## Install

Copy the whole `designing-fluent-interfaces/` directory into the skills directory supported by your agent runtime. The open Agent Skills format requires `SKILL.md`; the bundled `references/` directory is optional but recommended.

## Freshness

Sources were checked on 2026-08-27. See `references/sources.md` and re-check upstream Microsoft documentation for version-sensitive tasks.
