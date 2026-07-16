# Settings Status Summary Height Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the settings status icon and both status detail blocks exactly 32px high.

**Architecture:** Reuse the existing `LauncherChipHeight` token in the shared status-detail style. Preserve the existing 32px settings icon and vertically center status text by removing vertical padding.

**Tech Stack:** .NET 10, Avalonia 12, xUnit v3, Avalonia.Headless.XUnit

---

### Task 1: Lock down the style contract

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.Tests/UiStyleContractTests.cs`
- Modify: `Views/MainWindow.Styles.axaml`

- [ ] Add assertions that `Border.status-detail` has `Height="{StaticResource LauncherChipHeight}"` and `Padding="12,0"`.
- [ ] Run the focused test and confirm it fails because `Height` is absent and `Padding` is `12`.
- [ ] Apply the two exact style values.
- [ ] Run the focused test and confirm it passes.

### Task 2: Verify the rendered layout

**Files:**
- Modify: `tests/Cafe.Launcher.Avalonia.HeadlessTests/MainWindowHeadlessTests.cs`

- [ ] Add a headless test that opens settings, finds the single `settings-status-summary`, then asserts its one `settings-icon` and two `status-detail` descendants each have `Bounds.Height == 32`.
- [ ] Run the focused test and confirm it fails before the style change or passes only after the style change.
- [ ] Run the existing status-summary and settings layout tests.

### Task 3: Review and complete

**Files:**
- Review: all commits from `d8b42d7` through the implementation commit

- [ ] Run `git diff --check`.
- [ ] Run both complete test projects and Debug/Release builds.
- [ ] Inspect the diff for unrelated changes and exact identifier usage.
- [ ] Commit the implementation as `fix(ui): 统一设置状态摘要元素高度`.
