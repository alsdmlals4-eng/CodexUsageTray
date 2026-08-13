# Existing Chat Focus and Hook Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 ChatGPT 탭을 재사용해 즉시 복귀하고 Codex Hook 알림 실패가 턴 실패로 표시되지 않게 한다.

**Architecture:** 확장과 EventBridge 사이에 지속형 Native Messaging 포트를 유지하고, 연결별 named pipe로 트레이 클릭 명령을 원래 확장에 되돌려 보낸다. Hook은 15초 제한과 항상 성공 종료하는 ASCII 래퍼를 거쳐 실행한다.

**Tech Stack:** .NET 8, C# named pipes, WinForms, Chrome Manifest V3 JavaScript, Windows PowerShell 5.1, GitHub Actions

## Global Constraints

- 기존 ChatGPT 탭이 있으면 새 탭을 만들거나 페이지를 새로고침하지 않는다.
- 기존 탭이 닫힌 경우에만 저장된 안전한 ChatGPT URL을 새 탭으로 연다.
- 대화 본문과 쿠키를 읽거나 저장하거나 전송하지 않는다.
- Codex Hook 알림 실패는 Codex 작업 결과와 승인 결정을 변경하지 않는다.
- 기존 사용량 표시, 상단 팝업 큐, 사용자 Hook을 보존한다.

---

### Task 1: Stable web completion contract

**Files:**
- Create: `browser-extension/completion-state.js`
- Create: `tests/browser-extension/completion-state.test.js`
- Modify: `browser-extension/content.js`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: `CompletionState.observe({ now, generating, assistantMutated, routeKey })`
- Produces: `{ completed: boolean }` only after 2,000ms of stable completion evidence

- [ ] **Step 1: Write failing Node tests** for transient Stop disappearance, candidate cancellation, one-time stable completion, and route reset.
- [ ] **Step 2: Run `node tests/browser-extension/completion-state.test.js`** and verify failure because the state module is missing.
- [ ] **Step 3: Implement the minimal deterministic state machine** with injected timestamps and no timers or DOM dependency.
- [ ] **Step 4: Integrate DOM observations in `content.js`** and send only when the state machine returns completed.
- [ ] **Step 5: Run Node tests and syntax checks**, then add both to Windows CI and release validation.

### Task 2: Browser tab selection contract

**Files:**
- Create: `browser-extension/tab-focus.js`
- Create: `tests/browser-extension/tab-focus.test.js`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: `CodexUsageTrayTabFocus.createActivationPlan(tabs, targetUrl, preferredTabId)`

- [ ] **Step 1: Write failing Node tests** for preferred exact tab, fallback exact URL tab, and create-only-when-absent behavior with literal expected plans.
- [ ] **Step 2: Run `node tests/browser-extension/tab-focus.test.js`** and verify failure because `tab-focus.js` is missing.
- [ ] **Step 3: Implement URL normalization and activation-plan selection** without Chrome API dependencies.
- [ ] **Step 4: Run the Node test and syntax checks** and verify all cases pass.
- [ ] **Step 5: Add the test to Windows CI and release validation.**

### Task 3: Trusted browser activity identity

**Files:**
- Modify: `src/CodexUsageTray.Core/ActivityModels.cs`
- Modify: `src/CodexUsageTray.Core/BrowserActivityEventParser.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: `ActivityEvent.WithBrowserConnection(string connectionId)`
- Produces: `BrowserConnectionId`, `BrowserTabId`, `BrowserWindowId` activity properties

- [ ] **Step 1: Add failing Core tests** for valid tab/window metadata, invalid numeric metadata, GUID connection validation, and JSON IPC round trip.
- [ ] **Step 2: Push the test-only commit and verify Windows CI fails** because the new activity members do not exist.
- [ ] **Step 3: Implement minimal model and parser support** with positive integer and GUID validation.
- [ ] **Step 4: Run Core tests in Windows CI** and verify green.

### Task 4: Persistent Native Messaging and activation pipe

**Files:**
- Create: `src/CodexUsageTray.Core/BrowserActivationCommand.cs`
- Modify: `src/CodexUsageTray.EventBridge/Program.cs`
- Modify: `browser-extension/background.js`
- Modify: `browser-extension/manifest.json`
- Test: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: `BrowserConnectionId`, `BrowserTabId`, `BrowserWindowId`
- Produces: pipe message `{ "action": "activate", "url": string, "tabId": int, "windowId": int }`

- [ ] **Step 1: Add failing command validation and serialization tests.**
- [ ] **Step 2: Verify RED in Windows CI.**
- [ ] **Step 3: Implement connection-specific pipe naming and command validation.**
- [ ] **Step 4: Replace `sendNativeMessage` with reconnecting `connectNative`.** Attach trusted sender tab/window IDs before posting activity.
- [ ] **Step 5: Handle native activation messages** using `createActivationPlan`; focus existing tab/window or create only if absent.
- [ ] **Step 6: Run Node checks and Windows CI until green.**

### Task 5: Tray click routing

**Files:**
- Create: `src/CodexUsageTray/BrowserActivityActivator.cs`
- Modify: `src/CodexUsageTray/ActivitySourceLauncher.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Produces: `BrowserActivityActivator.TryActivate(ActivityEvent activity)`
- Consumes: connection pipe identity and safe source URL

- [ ] **Step 1: Add a failing Windows regression test** proving successful browser command delivery avoids Windows URL launch and keeps the clicked event identity.
- [ ] **Step 2: Verify RED in Windows CI.**
- [ ] **Step 3: Implement bounded named-pipe activation with URL fallback only when delivery fails.**
- [ ] **Step 4: Verify UI tests and full solution build in Windows CI.**

### Task 6: Codex Hook reliability boundary

**Files:**
- Modify: `scripts/setup-integration.ps1`
- Modify: `scripts/remove-integration.ps1`
- Modify: `tests/PowerShellInstaller.Tests.ps1`
- Modify: `README.md`

**Interfaces:**
- Produces: `%LOCALAPPDATA%\CodexUsageTray\invoke-codex-hook.cmd`
- Produces: Hook timeout `15` seconds and wrapper-based Hook command

- [ ] **Step 1: Add failing PowerShell integration assertions** for wrapper creation, wrapper Hook command, timeout 15, existing Hook preservation, and ASCII wrapper bytes.
- [ ] **Step 2: Verify RED in Windows CI.**
- [ ] **Step 3: Generate the wrapper atomically in setup**, point all three Hook events to it, and remove it during integration removal.
- [ ] **Step 4: Update troubleshooting and reinstall instructions.**
- [ ] **Step 5: Verify PowerShell 5.1 tests and full Windows CI.**

### Task 7: Release and user verification

**Files:**
- Modify: `.release-version`

**Interfaces:**
- Produces: GitHub release `v1.2.1` with ZIP and SHA-256 asset

- [ ] **Step 1: Review the complete diff and run `git diff --check`, Node tests, manifest validation, Windows CI, and release workflow smoke install.**
- [ ] **Step 2: Open a draft PR, make it ready after green checks, and squash merge.**
- [ ] **Step 3: Set `.release-version` to `v1.2.1` and verify release assets are published.**
- [ ] **Step 4: Give the one-line reinstall command and require extension reload plus Codex `/hooks` re-trust.**
- [ ] **Step 5: Manually verify on the user's PC that ChatGPT tab count stays unchanged and the original Codex terminal is activated.**
