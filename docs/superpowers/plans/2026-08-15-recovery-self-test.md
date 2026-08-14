# Safe Recovery Self-Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tray `복구 기능 테스트` action that validates the existing 3/10/30 recovery coordinator policy and production Windows recovery-status UI without touching a live ChatGPT page or network.

**Architecture:** Add a small deterministic `RecoverySelfTestRunner` to `CodexUsageTray.Core`, backed by the existing `BrowserRecoveryCoordinator`. The WinForms tray invokes it and sends test-only `RecoveryRequired`/`Recovered` activities through the existing `HandleActivity` path using detail `recovery_self_test`, which cannot trigger production browser reload logic.

**Tech Stack:** .NET 8, WinForms, existing Core activity/recovery models, GitHub Actions `windows-latest`.

## Global Constraints

- No ChatGPT DOM mutation, browser reload, navigation, prompt resend, network interruption, OpenAI API request, or approval action.
- No new browser permission or Native Messaging command.
- Self-test uses a fresh `BrowserRecoveryCoordinator`; real conversation recovery state is untouched.
- Synthetic UI events use detail exactly `recovery_self_test`, never `disconnected_waiting`.
- Repeated menu clicks cannot overlap self-tests.
- Existing browser, ntfy, RecoveryRunner, desktop shortcut, restart, and Codex Hook behavior must remain unchanged.
- Use TDD and preserve representative RED evidence before production code.

---

### Task 1: Core self-test policy contract

**Files:**
- Modify: `tests/CodexUsageTray.Recovery.Tests/Program.cs`
- Create after RED: `src/CodexUsageTray.Core/RecoverySelfTestRunner.cs`

**Interfaces:**
- Produces: `RecoverySelfTestRunner.Run(Func<BrowserRecoveryInstruction, bool>? executeAttempt = null)`
- Produces: `RecoverySelfTestResult` with `Passed`, `VerifiedDelays`, `DuplicateSuppressed`, `CeilingEnforced`, `ResetVerified`, and `Failure`.

- [ ] Add a recovery test that calls `RecoverySelfTestRunner.Run`, records instructions passed to the executor, and requires attempts `1,2,3` with delays exactly `3s,10s,30s`, duplicate suppression, three-attempt ceiling, and reset to attempt 1 / 3s.
- [ ] Add a second test whose attempt executor returns `false` and require `Passed == false` with a nonempty failure reason.
- [ ] Run PR CI before creating `RecoverySelfTestRunner.cs`; expected RED is a missing type/build failure in `CodexUsageTray.Recovery.Tests`.
- [ ] Implement the minimal runner with a fresh `BrowserRecoveryCoordinator` and synthetic `ChatGptWeb` activity whose detail is `disconnected_waiting` only inside the isolated runner.
- [ ] Re-run CI and require recovery tests green before touching tray UI.

### Task 2: Tray menu and production UI-path contract

**Files:**
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`
- Modify after RED: `src/CodexUsageTray/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `RecoverySelfTestRunner.Run(...)` from Task 1.
- Produces: tray menu item text exactly `복구 기능 테스트`.
- Produces: synthetic UI event detail exactly `recovery_self_test`.

- [ ] Add a Windows regression test that constructs `TrayApplicationContext`, finds `복구 기능 테스트`, clicks it, and requires a visible test `RecoveryRequired` popup.
- [ ] In that test, inspect `_activityStore` and require the synthetic session/detail to be identifiable as a self-test.
- [ ] Click the first popup and require the queued test `Recovered` popup to become visible.
- [ ] Require the final activity-store entry for the same synthetic activity key to be `Recovered`, leaving no fake unresolved recovery warning.
- [ ] Run PR CI before tray production changes; expected RED is missing menu/self-test integration.
- [ ] Add `_recoverySelfTestRunning` guard and `RunRecoverySelfTest()` to `TrayApplicationContext`.
- [ ] On success, call the existing `HandleActivity` twice with the same synthetic activity key: first `RecoveryRequired`, then `Recovered`, both detail `recovery_self_test`; do not call `BrowserActivityActivator` directly.
- [ ] On runner failure, show a test-failure error dialog and emit no recovered success claim.
- [ ] Re-run Windows UI tests and require the new menu/popup/store contract green.

### Task 3: Adversarial safety verification

**Files:**
- Modify if needed: `tests/CodexUsageTray.Windows.Tests/Program.cs`
- Modify if needed: `tests/CodexUsageTray.Recovery.Tests/Program.cs`

- [ ] Verify the synthetic UI event's detail differs from `disconnected_waiting`, so `TrayApplicationContext`'s production `_browserRecovery.Plan(activity)` returns no instruction.
- [ ] Verify the self-test runner never receives or uses a real BrowserConnectionId, tab ID, window ID, SourceUri, prompt, or API key.
- [ ] Verify mobile notification behavior is unchanged and recovery self-test states remain non-mobile-push events.
- [ ] Re-run the complete PR CI: browser validation, PowerShell installer, desktop shortcut, core, recovery, RecoveryRunner, Windows UI/mobile regressions, full solution build, EventBridge integration.

### Task 4: PR and merge gate

**Files:**
- Review only unless a defect is found.

- [ ] Compare feature head against the exact current `main`; require `behind_by == 0` or resync without touching unrelated work.
- [ ] Review changed files for scope leakage; browser extension behavior, EventBridge protocol, RecoveryRunner, ntfy settings, ActivityStatus, installer semantics, and restart code must remain outside the feature diff unless a verified test requires otherwise.
- [ ] Require exact-head PR CI success and unresolved review threads `0`.
- [ ] Squash merge the feature PR only after those gates pass.

### Task 5: Patch release and user validation

**Files:**
- Modify on a separate release branch after feature merge: `.release-version`
- Modify metadata only: `browser-extension/manifest.json`

- [ ] Prepare `v1.3.3` metadata with no browser behavior change.
- [ ] Run exact-head release PR CI and squash merge when green.
- [ ] Verify the Windows release workflow publishes ZIP/checksum from the release merge commit and `latest` points to `v1.3.3`.
- [ ] User installs once with the existing online installer.
- [ ] User right-clicks tray → `복구 기능 테스트`, confirms `[테스트] 복구 필요`, clicks it, then confirms `[테스트] 자동 복구` and final history state is recovered.
- [ ] Record the remaining production boundary accurately: real ChatGPT DOM error detection is still confirmed only by a naturally occurring timeout/disconnection.
