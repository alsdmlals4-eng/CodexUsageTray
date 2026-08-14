# Desktop Launcher and Restart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a desktop launch shortcut and a one-click tray restart action without changing browser/mobile/recovery protocols.

**Architecture:** Add one PowerShell shortcut helper consumed by the release installer, plus small C# restart-launch/startup helpers consumed by `TrayApplicationContext` and `Program`. Keep shortcut creation user-scoped and canonical-install-only. Restart launches the same executable with `--restart-after <old PID>`; the replacement waits before acquiring the existing single-instance mutex so the handoff cannot collapse into both instances exiting.

**Tech Stack:** PowerShell 5.1-compatible installer scripts, .NET 8 WinForms, GitHub Actions `windows-latest`.

## Global Constraints

- Do not modify browser-extension behavior, EventBridge protocol, RecoveryRunner, ActivityStatus, ntfy settings, or Codex hook semantics.
- Automatic desktop shortcut creation applies only to `%LOCALAPPDATA%\CodexUsageTray`.
- A shortcut creation failure must not roll back an otherwise valid install/update.
- Restart must dispose existing backends before the old instance exits and must preserve the existing `Local\CodexUsageTray` single-instance guarantee.
- The replacement wait is bounded to 15 seconds and malformed restart PIDs fail closed.
- Use TDD: prove each new contract fails on Windows CI before adding production code.

---

### Task 1: Desktop shortcut contract

**Files:**
- Create: `tests/DesktopShortcut.Tests.ps1`
- Modify: `.github/workflows/ci.yml`
- Create after RED: `scripts/shortcut-registration.ps1`

**Interfaces:**
- Produces: `New-CodexUsageTrayShortcut -ExecutablePath <path> -ShortcutDirectory <dir>` returning the `.lnk` path.

- [x] Write the failing Windows shortcut test.
- [x] Wire the test into PR CI and record RED for the missing helper.
- [x] Add the minimal PowerShell 5.1 helper with `-LibraryOnly`.
- [x] Verify target, working directory, and icon on Windows CI.

### Task 2: Installer integration

**Files:**
- Modify: `scripts/install-release.ps1`
- Modify: `.github/workflows/release.yml`

- [x] Add a failing installer contract for canonical, best-effort shortcut wiring.
- [x] Preserve older/manual package compatibility by treating the helper as optional to the generic installer while requiring it in the new release package.
- [x] Create/refresh the desktop shortcut only after a successful canonical install transaction.
- [x] Package and smoke-check `shortcut-registration.ps1` in release wiring.

### Task 3: Restart launcher contract

**Files:**
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`
- Create: `src/CodexUsageTray/ApplicationRestartLauncher.cs`
- Create after adversarial RED: `src/CodexUsageTray/ApplicationRestartStartup.cs`
- Modify: `src/CodexUsageTray/Program.cs`

**Interfaces:**
- `ApplicationRestartLauncher.TryStart(string executablePath, int currentProcessId)`
- `ApplicationRestartStartup.WaitForPreviousInstance(string[] args)`

- [x] Add RED proving the launcher must use the exact executable path and safely contain process-start failures.
- [x] Add the minimal launcher.
- [x] During adversarial review, identify that launching before release of `Local\CodexUsageTray` makes the replacement exit immediately.
- [x] Add a second RED requiring the current PID to be handed to the replacement and requiring replacement startup to wait before the mutex.
- [x] Add `--restart-after <PID>` handoff with a bounded 15-second wait and fail-closed malformed PID handling.
- [x] Preserve ordinary `--startup` behavior.

### Task 4: Tray restart action

**Files:**
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

- [x] Add `앱 다시 시작` to the existing tray menu.
- [x] Share the existing orderly shutdown path: stop timers, cancel, await active refresh, dispose backends.
- [x] Launch the replacement with the exact executable path and `Environment.ProcessId`.
- [x] Keep repeated clicks blocked by the existing `_exiting` guard.
- [ ] Require the final exact-head PR CI to pass browser validation, PowerShell installer/shortcut tests, core/recovery/RecoveryRunner, 12 Windows UI regression tests, full solution build, and EventBridge integration.

### Task 5: Release and post-merge verification

**Files:**
- Modify after feature merge: `.release-version`
- Modify after feature merge: `browser-extension/manifest.json` only for release-version consistency; do not change extension behavior.

- [ ] Review the exact PR diff for scope leakage and confirm current `main` has not moved underneath the branch.
- [ ] Require all exact-head PR checks green and no unresolved review threads.
- [ ] Squash merge feature PR.
- [ ] Prepare the next patch release and trigger the existing release workflow.
- [ ] Verify release workflow success, ZIP/checksum publication, and release target commit.
- [ ] User installs once with the existing online installer, then verifies desktop shortcut launch and tray `앱 다시 시작` without PowerShell.
