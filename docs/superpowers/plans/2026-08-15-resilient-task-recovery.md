# Resilient Task Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded ChatGPT web recovery, tray recovery-state visibility, and a checkpointed OpenAI Responses API recovery runner without changing the default tray credential boundary.

**Architecture:** Reuse the existing browser extension/native messaging/activity pipeline for UI recovery states. Keep API execution in a new console project that depends only on Core recovery primitives; the tray executable never reads `OPENAI_API_KEY`.

**Tech Stack:** Manifest V3 JavaScript, Node test runner scripts, .NET 8 C#, WinForms, HttpClient/OpenAI Responses REST contract, GitHub Actions Windows CI.

## Global Constraints

- Auto-retry only explicit transient ChatGPT errors with a retry control in the same error container.
- Retry delays are exactly 3s, 10s, 30s with maximum 3 attempts.
- A 180s no-mutation generation stall never auto-clicks; it reports recovery required.
- Never auto-approve ChatGPT/Codex permission UI.
- Existing tray process must not read or require an OpenAI API key.
- RecoveryRunner reads API credentials only from `OPENAI_API_KEY`.
- Completed checkpoints must never generate a duplicate API request.

---

### Task 1: Browser watchdog contract

**Files:**
- Create: `browser-extension/recovery-watchdog.js`
- Create: `tests/browser-extension/recovery-watchdog.test.js`
- Modify: `browser-extension/manifest.json`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: `CodexUsageTrayRecoveryWatchdog.classifyTransientError(text)`, `getRetryDelay(attempt)`, `RecoveryWatchdog.observe(input)`.

- [ ] Write tests proving supported timeout/network/generation errors are transient while arbitrary text is not.
- [ ] Run the focused Node test and observe RED because `recovery-watchdog.js` does not exist.
- [ ] Implement only text classification and exact delay sequence `[3000, 10000, 30000]`.
- [ ] Add tests for max-three attempt state and 180000ms stall classification without auto retry.
- [ ] Run focused tests GREEN.
- [ ] Wire the new script into manifest before `content.js`, add syntax/test invocation to CI, and run CI later at exact PR head.

### Task 2: Browser content recovery behavior

**Files:**
- Create: `tests/browser-extension/recovery-content.test.js`
- Modify: `browser-extension/content.js`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `CodexUsageTrayRecoveryWatchdog`.
- Produces browser activity statuses `retrying`, `recovery_required`, `recovered`.

- [ ] Write a DOM-light Node contract test for error-container selection: retry button outside the matched error container must not be selected.
- [ ] Verify RED against current content script/helper surface.
- [ ] Extract pure helpers for normalized visible text and safe retry control selection.
- [ ] Add bounded retry scheduling that emits `retrying` before click and `recovery_required` after ceiling or missing safe control.
- [ ] Track last assistant mutation and emit `recovery_required` after a 180s active-generation stall; do not click.
- [ ] Emit `recovered` once a retried route resumes generation or reaches completion.
- [ ] Run browser tests GREEN.

### Task 3: Tray recovery states

**Files:**
- Modify: `src/CodexUsageTray.Core/ActivityModels.cs`
- Modify: `src/CodexUsageTray.Core/BrowserActivityEventParser.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`
- Modify: `src/CodexUsageTray/ActivityPopupQueue.cs`
- Modify: `src/CodexUsageTray/ActivityPopupForm.cs`
- Modify: `src/CodexUsageTray/ActivityHistoryForm.cs`
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Produces `ActivityStatus.Retrying`, `ActivityStatus.RecoveryRequired`, `ActivityStatus.Recovered`.

- [ ] Add Core tests for parser mappings and summaries; verify RED.
- [ ] Add enum values and parser mappings minimally; run Core tests GREEN.
- [ ] Add Windows tests for popup-eligible statuses and status-label presentation; verify RED.
- [ ] Update popup/history/tray counters: recovery-required and recovered are popup-worthy, retrying is history-only.
- [ ] Run Core and Windows tests GREEN.

### Task 4: Recovery checkpoint core

**Files:**
- Create: `src/CodexUsageTray.Core/RecoveryJob.cs`
- Create: `src/CodexUsageTray.Core/RecoveryStateStore.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- `RecoveryJob.Load(string json)` validates `jobId`, `model`, `prompt`, `maxAttempts 1..5`, `timeoutSeconds 10..3600`.
- `RecoveryStateStore.Load(path)` and `SaveAtomic(path, state)`.
- `RecoveryExecutionState` holds status/attempt/response/output/error/update fields.

- [ ] Add validation and completed-state short-circuit tests; verify RED.
- [ ] Implement job/state records and JSON validation.
- [ ] Add atomic save/read round-trip test using a temporary directory.
- [ ] Implement temp-file write plus atomic replace/move and run Core tests GREEN.

### Task 5: RecoveryRunner Responses client

**Files:**
- Create: `src/CodexUsageTray.RecoveryRunner/CodexUsageTray.RecoveryRunner.csproj`
- Create: `src/CodexUsageTray.RecoveryRunner/Program.cs`
- Create: `src/CodexUsageTray.RecoveryRunner/ResponsesRecoveryClient.cs`
- Create: `tests/CodexUsageTray.RecoveryRunner.Tests/CodexUsageTray.RecoveryRunner.Tests.csproj`
- Create: `tests/CodexUsageTray.RecoveryRunner.Tests/Program.cs`
- Modify: `CodexUsageTray.sln`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- `ResponsesRecoveryClient.ExecuteAsync(RecoveryJob, RecoveryExecutionState, CancellationToken)`.
- Retry classifier: transient for `HttpRequestException`, timeout, HTTP 408/409/429/5xx; terminal otherwise.
- Commands: `run --job <path> [--state <path>]`, `resume --state <path>`.

- [ ] Add runner tests using a custom `HttpMessageHandler` proving transient status classification, exact three-attempt ceiling, terminal 400 behavior, and completed-state no-request behavior; verify RED before project implementation.
- [ ] Implement REST call to `POST https://api.openai.com/v1/responses` with bearer credential from `OPENAI_API_KEY`, JSON `{model,input}` and response `id`/`output_text` extraction.
- [ ] Persist `running` before each request and `failed_transient`/`failed_terminal`/`completed` after response.
- [ ] Implement resume by loading the state and the job snapshot stored beside it; reject missing/invalid credentials without sending a request.
- [ ] Add projects to solution and CI, then run all tests/build GREEN.

### Task 6: Documentation and release wiring

**Files:**
- Modify: `README.md`
- Modify: `scripts/build-windows.ps1`
- Modify: release/install scripts only if artifact packaging currently enumerates executables explicitly.

**Interfaces:**
- Installer continues to install tray + EventBridge + browser extension; RecoveryRunner is included as an optional executable but is never auto-launched.

- [ ] Document automatic retry boundary, recovery-required notification, and the exact difference between Retry and Resume.
- [ ] Document RecoveryRunner setup with `OPENAI_API_KEY`, job JSON example, run/resume commands, and credential isolation.
- [ ] Ensure build/release artifact includes `CodexUsageTray.RecoveryRunner.exe` without startup registration.
- [ ] Run full PR CI and inspect exact-head checks.

### Task 7: Adversarial regression review

**Files:** no new scope unless a validated finding requires a minimal fix.

- [ ] Attack: attempt to trigger retry from unrelated text/buttons, stale route state, approval UI, missing native host, and completed checkpoint.
- [ ] Validate critique: only reproducible safety/correctness findings are accepted.
- [ ] Apply minimal fixes for accepted findings.
- [ ] Regression-recheck full CI on exact head.
- [ ] Confirm unresolved review threads = 0 and merge only if all required checks pass.
