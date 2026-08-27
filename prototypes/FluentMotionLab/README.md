# Fluent Motion Lab — PROTOTYPE

> Three motion variants of the launcher, switchable from one Avalonia window: `Legacy`, `Fluent`, and `Reduced`.

This is throwaway code for answering one question: **does the proposed Fluent motion language feel calmer, more connected, and more responsive than the current launcher motion?** It is not production architecture and must not be referenced by the application.

Run from PowerShell:

```powershell
.\prototypes\FluentMotionLab\run.ps1
```

Use the bottom switcher or the left/right arrow keys to change motion variants. Select a scene, then use **Replay** or **Rapid fire**. Scene-specific buttons exercise direction, completion, modal ownership, Toast stacking, and appearance changes.

The accepted behavior is defined by `docs/design/adr/ADR-016-Fluent动效层.md`; the complete prototype matrix is in `docs/design/fluent-motion-prototype-handoff.md`.
