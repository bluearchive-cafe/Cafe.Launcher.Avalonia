# NSIS Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement and verify this plan task-by-task.

**Goal:** Build and publish a safe, all-users NSIS installer alongside the standalone ZIP for every `v*` Git tag.

**Architecture:** `dotnet publish` is the only source of application files. `scripts/Build-Distribution.ps1` reads exact project versions, publishes once, generates an explicit uninstall-file include, creates the standalone ZIP, and invokes NSIS. The NSIS module handles machine-wide installation, checked upgrades, all-users shortcuts, 64-bit uninstall registration, and optional deletion of only the uninstalling user's application data.

**Tech Stack:** .NET 10, PowerShell 7, NSIS 3, Modern UI 2, nsDialogs, GitHub Actions

---

## Confirmed contract

| Item | Exact value |
|---|---|
| Product | `Cafe Launcher` |
| Executable | `Cafe.Launcher.Avalonia.exe` |
| Publisher | `BlueArchive Cafe` |
| Scope | All users |
| Default directory | `$PROGRAMFILES64\Cafe Launcher` |
| Registry | 64-bit `HKLM` |
| Shortcuts | All users; desktop shortcut optional and unchecked |
| Application data | `%LOCALAPPDATA%\Cafe Launcher` |
| Interactive uninstall | Optional current-user data deletion, unchecked |
| Silent uninstall | Preserve application data |
| Game directory | Never managed or deleted |
| ZIP | `Cafe.Launcher.Avalonia_${tag}_standalone.zip` |
| Setup | `Cafe.Launcher.Avalonia_${tag}_setup.exe` |

## Safety invariants

1. Never recursively delete `$INSTDIR`.
2. Delete only exact publish files and installer-owned files.
3. Validate `$INSTDIR` against 64-bit HKLM `InstallLocation`.
4. Require `.cafe-launcher-install` and the application executable before uninstall.
5. Never reference or delete `YostarGames`.
6. Block install, upgrade, and uninstall while the launcher process is running.
7. Abort upgrade when the previous uninstaller returns a nonzero exit code.
8. Use `ReadRegStr` after `SetRegView 64`; do not use `InstallDirRegKey`.

## Tasks

### 1. Contract tests

- [x] Add `InstallerContractTests`.
- [x] Verify tests fail before implementation files exist.
- [x] Cover identity, scope, component page, explicit uninstall list, UTF-8 source, process blocking, artifact names, and release uploads.

### 2. Distribution builder

- [x] Read exactly one `VersionPrefix` and `FileVersion` XML node.
- [x] Require the exact `v<VersionPrefix>` tag.
- [x] publish `win-x64` into a clean `artifacts/publish`.
- [x] Generate `artifacts/generated/UninstallFiles.nsh`.
- [x] Reject publish paths containing characters not represented safely in NSIS.
- [x] Delete files explicitly and remove directories only when empty.
- [x] Create standalone ZIP and setup EXE with exact confirmed names.
- [x] Return only the final artifact result object to callers.

### 3. NSIS module

- [x] Declare UTF-8 source encoding.
- [x] Require administrator execution and default to Program Files.
- [x] support English, Simplified Chinese, and Japanese.
- [x] Provide a selectable desktop shortcut through the Components page.
- [x] Detect the exact launcher executable with `tasklist.exe`.
- [x] Use Retry/Cancel interactively and fail silently with a nonzero exit code.
- [x] Parse and execute the previous NSIS uninstaller using the documented `_?=` flow.
- [x] Abort upgrade when previous uninstall fails.
- [x] Write machine-wide shortcuts and 64-bit uninstall registration.
- [x] Validate registry path and installation marker before uninstall.
- [x] Offer current-user application-data deletion only during interactive uninstall.
- [x] Preserve application data during silent uninstall.
- [x] Include the generated explicit uninstall list.

### 4. GitHub Release

- [x] Install NSIS on the existing Ubuntu runner.
- [x] Run `Build-Distribution.ps1` for tag releases.
- [x] Upload standalone ZIP and setup EXE to both repositories.
- [x] Keep `fail_on_unmatched_files: true`.

### 5. Documentation

- [x] Document NSIS prerequisites and local build command.
- [x] Document exact artifact names.
- [x] Document all-users installation and elevation.
- [x] Document upgrade ownership and uninstall data behavior.

### 6. Verification

- [x] Baseline `verify.ps1`.
- [x] Contract tests.
- [x] NSIS 3.12 compilation.
- [x] Clean install in a disposable Windows environment.
- [x] Interactive and silent process-blocking checks.
- [ ] Upgrade cleanup and unrelated-file preservation.
- [x] Interactive uninstall with application data preserved.
- [x] Interactive uninstall with current-user application data deleted.
- [ ] Silent uninstall with application data preserved.
- [x] Final `verify.ps1`.

System-level install, upgrade, and uninstall checks require a disposable Windows environment with UAC. They must not be simulated by modifying `Program Files` directly.
