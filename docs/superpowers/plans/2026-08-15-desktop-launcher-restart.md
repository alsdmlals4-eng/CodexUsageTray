# Desktop Launcher and Restart Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a desktop launch shortcut and a one-click tray restart action without changing browser/mobile/recovery protocols.

**Architecture:** Add one PowerShell shortcut helper consumed by the release installer, and one small C# restart launcher consumed by `TrayApplicationContext`. Keep shortcut creation user-scoped and canonical-install-only; keep process restart inside the existing orderly shutdown path.

**Tech Stack:** PowerShell 5.1-compatible installer scripts, .NET 8 WinForms, GitHub Actions `windows-latest`.

## Global Constraints

- Do not modify browser-extension behavior, EventBridge protocol, RecoveryRunner, ActivityStatus, ntfy settings, or Codex hook semantics.
- Automatic desktop shortcut creation applies only to `%LOCALAPPDATA%\CodexUsageTray`.
- A shortcut creation failure must not roll back an otherwise valid install/update.
- Restart must dispose existing backends before launching the replacement executable.
- Use TDD: prove each new contract fails on Windows CI before adding production code.

---

### Task 1: Desktop shortcut contract

**Files:**
- Create: `tests/DesktopShortcut.Tests.ps1`
- Modify: `.github/workflows/ci.yml`
- Create later after RED: `scripts/shortcut-registration.ps1`

**Interfaces:**
- Produces: `New-CodexUsageTrayShortcut -ExecutablePath <path> -ShortcutDirectory <dir>` returning the `.lnk` path.

- [ ] **Step 1: Write the failing Windows test** that dot-sources `scripts/shortcut-registration.ps1 -LibraryOnly`, creates a temporary fake `CodexUsageTray.exe`, calls `New-CodexUsageTrayShortcut`, reopens the `.lnk` with `WScript.Shell`, and asserts `TargetPath`, `WorkingDirectory`, and `IconLocation` reference the requested executable.
- [ ] **Step 2: Wire `tests/DesktopShortcut.Tests.ps1` into PR CI** before production code exists.
- [ ] **Step 3: Run PR CI and record RED** caused by missing `scripts/shortcut-registration.ps1`.
- [ ] **Step 4: Add minimal helper implementation** with PowerShell 5.1 syntax and `-LibraryOnly` so importing the helper has no side effects.
- [ ] **Step 5: Re-run CI and require the desktop-shortcut test to pass.**

### Task 2: Installer integration

**Files:**
- Modify: `scripts/install-release.ps1`
- Modify: `.github/workflows/release.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: `New-CodexUsageTrayShortcut` from Task 1.

- [ ] **Step 1: Extend the shortcut test with a static installer contract** requiring `install-release.ps1` to include `shortcut-registration.ps1` in installed files and invoke the helper only for the canonical `%LOCALAPPDATA%\CodexUsageTray` path.
- [ ] **Step 2: Run CI and record RED** before installer wiring exists.
- [ ] **Step 3: Update installer** to copy the helper, commit installation first, then best-effort create/update `Codex Usage Tray.lnk` only for the canonical install path; emit warning on shortcut failure without rollback.
- [ ] **Step 4: Update release packaging/smoke checks** so `shortcut-registration.ps1` is included in the ZIP and installed directory.
- [ ] **Step 5: Update README** to document the desktop launch shortcut and no-PowerShell normal use after installation.
- [ ] **Step 6: Re-run CI and require installer/release wiring tests to pass.**

### Task 3: Restart launcher contract

**Files:**
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`
- Create later after RED: `src/CodexUsageTray/ApplicationRestartLauncher.cs`

**Interfaces:**
- Produces: `ApplicationRestartLauncher.TryStart(string executablePath)` and an injectable constructor overload for tests.

- [ ] **Step 1: Add Windows regression tests** proving the launcher passes the exact executable path to the process-start delegate and returns `false` when the delegate throws.
- [ ] **Step 2: Run CI and record RED** because `ApplicationRestartLauncher` does not yet exist.
- [ ] **Step 3: Add minimal launcher implementation** using `Process.Start` with `UseShellExecute = true` in production and injected delegate in tests.
- [ ] **Step 4: Re-run CI and require the new Windows regression tests to pass.**

### Task 4: Tray restart action

**Files:**
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `ApplicationRestartLauncher.TryStart` from Task 3.

- [ ] **Step 1: Add `앱 다시 시작` to the existing tray menu** near other lifecycle actions.
- [ ] **Step 2: Refactor shutdown minimally into a shared async exit path** accepting a restart flag: stop timers, cancel, await active refresh, dispose backends, then launch the current executable and exit the old message loop.
- [ ] **Step 3: If replacement launch fails, show an actionable message instead of throwing.**
- [ ] **Step 4: Run the complete PR CI**: browser validation, PowerShell installer/shortcut tests, core/recovery/RecoveryRunner, Windows UI regression tests, full solution build, EventBridge integration.

### Task 5: Release and post-merge verification

**Files:**
- Modify after feature merge: `.release-version`
- Modify after feature merge: `browser-extension/manifest.json` only if release-version consistency requires it; do not change extension behavior.

- [ ] **Step 1: Review exact PR diff for scope leakage** and verify no unrelated open PR is modified.
- [ ] **Step 2: Require all exact-head PR checks green and no unresolved review threads.**
- [ ] **Step 3: Squash merge feature PR.**
- [ ] **Step 4: Prepare the next patch release version and trigger the existing release workflow.**
- [ ] **Step 5: Verify release workflow success, ZIP/checksum publication, and release target commit.**
- [ ] **Step 6: User installs once with the existing online installer, then verifies desktop shortcut launch and tray `앱 다시 시작` without PowerShell.**
