# Safe Recovery Self-Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a tray `복구 기능 테스트` action that validates the existing 3/10/30 recovery coordinator policy and production Windows recovery-status UI without touching a live ChatGPT page or network.

**Architecture:** Add a small deterministic `RecoverySelfTestRunner` to `CodexUsageTray.Core`, backed by the existing `BrowserRecoveryCoordinator`. The WinForms tray invokes it, emits a test-only `RecoveryRequired` with detail `recovery_self_test`, then transitions the same activity to `Recovered` only after the user acknowledges the first popup. Self-test UI events stop after activity-store/popup processing and cannot reach mobile push or production browser recovery fan-out.

**Tech Stack:** .NET 8, WinForms, existing Core activity/recovery models, GitHub Actions `windows-latest`.

## Global Constraints

- No ChatGPT DOM mutation, browser reload, navigation, prompt resend, network interruption, OpenAI API request, or approval action.
- No new browser permission or Native Messaging command.
- Self-test uses a fresh `BrowserRecoveryCoordinator`; real conversation recovery state is untouched.
- Synthetic UI events use detail exactly `recovery_self_test`, never `disconnected_waiting`.
- Synthetic UI events explicitly bypass `MobileNotificationRuntime` and production browser recovery fan-out.
- Repeated menu clicks cannot overlap an unacknowledged self-test.
- Existing browser, ntfy, RecoveryRunner, desktop shortcut, restart, PopupQueue semantics, and Codex Hook behavior remain unchanged.
- Use TDD and preserve representative RED evidence before production code.

---

### Task 1: Core self-test policy contract

**Files:**
- Modify: `tests/CodexUsageTray.Recovery.Tests/Program.cs`
- Create after RED: `src/CodexUsageTray.Core/RecoverySelfTestRunner.cs`

**Interfaces:**
- Produces: `RecoverySelfTestRunner.Run(Func<BrowserRecoveryInstruction, bool>? executeAttempt = null)`
- Produces: `RecoverySelfTestResult` with `Passed`, `VerifiedDelays`, `DuplicateSuppressed`, `CeilingEnforced`, `ResetVerified`, and `Failure`.

- [x] Add a recovery test that calls `RecoverySelfTestRunner.Run`, records instructions passed to the executor, and requires attempts `1,2,3` with delays exactly `3s,10s,30s`, duplicate suppression, three-attempt ceiling, and reset to attempt 1 / 3s.
- [x] Add a second test whose attempt executor returns `false` and require `Passed == false` with a nonempty failure reason.
- [x] Run PR CI before creating `RecoverySelfTestRunner.cs`; RED run `31851054009` failed because `RecoverySelfTestRunner` did not exist.
- [x] Implement the minimal runner with a fresh `BrowserRecoveryCoordinator` and an isolated synthetic `ChatGptWeb` activity.
- [x] Re-run CI and require recovery tests green; run `31851179815` passed the new core self-test contracts.

### Task 2: Tray menu and production UI-path contract

**Files:**
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`
- Modify after RED: `src/CodexUsageTray/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `RecoverySelfTestRunner.Run(...)` from Task 1.
- Produces: tray menu item text exactly `복구 기능 테스트`.
- Produces: synthetic UI event detail exactly `recovery_self_test`.

- [x] Add a Windows regression test that constructs `TrayApplicationContext`, finds `복구 기능 테스트`, clicks it, and requires a visible test `RecoveryRequired` popup.
- [x] Run PR CI before tray production changes; RED run `31851313889` failed because the menu/self-test integration did not exist.
- [x] Add `_recoverySelfTestRunning` guard and `RunRecoverySelfTest()` to `TrayApplicationContext`.
- [x] During first GREEN attempt, identify the existing `ActivityPopupQueue` same-key coalescing contract: immediate `RecoveryRequired → Recovered` overwrote the visible required popup. Run `31851552058` failed at the expected first-popup assertion.
- [x] Correct the UI contract so the store remains `RecoveryRequired` until acknowledgement; corrected RED run `31851720814` reproduced the first-popup failure against the immediate-transition implementation.
- [x] On success, emit only `RecoveryRequired`; when that popup is acknowledged, `OpenActivity` transitions the same activity key to `Recovered` and displays the recovered popup.
- [x] On runner failure, show a test-failure error dialog and emit no recovered success claim.
- [x] Re-run Windows UI tests; run `31851876323` passed all 13 Windows UI regression tests and the full PR workflow.

### Task 3: Adversarial safety verification

**Files:**
- `tests/CodexUsageTray.Windows.Tests/Program.cs`
- `tests/CodexUsageTray.Recovery.Tests/Program.cs`
- `src/CodexUsageTray/TrayApplicationContext.cs`

- [x] Verify synthetic UI detail differs from `disconnected_waiting`; the Windows test proves a fresh production `BrowserRecoveryCoordinator` returns no instruction for the self-test activity.
- [x] Verify synthetic UI activity carries no BrowserConnectionId, tab ID, window ID, or SourceUri.
- [x] Explicitly stop self-test events in `HandleActivity` before `MobileNotificationRuntime` and production `_browserRecovery` execution, making the safety boundary independent of future policy expansion.
- [x] Keep self-test policy execution isolated in a fresh coordinator with a no-side-effect executor; no prompt, API key, browser identity, or live browser command is used.
- [x] Preserve global `ActivityPopupQueue` semantics and adapt only the self-test lifecycle.
- [x] Run the complete PR CI; `31851876323` passed browser validation, PowerShell installer, desktop shortcut, core, recovery 9/9, RecoveryRunner 6/6, Windows UI 13/13 plus mobile notification regressions, full solution build, and EventBridge integration.

### Task 4: PR and merge gate

**Files:**
- Review only unless a defect is found.

- [ ] Compare feature head against the exact current `main`; require `behind_by == 0` or resync without touching unrelated work.
- [ ] Review changed files for scope leakage; browser extension behavior, EventBridge protocol, RecoveryRunner, ntfy settings, ActivityStatus, installer semantics, restart code, and PopupQueue must remain outside the feature diff unless a verified test requires otherwise.
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
