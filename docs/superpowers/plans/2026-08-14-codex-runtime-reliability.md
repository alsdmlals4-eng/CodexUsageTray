# Codex Runtime Reliability Implementation Plan

> **Superseded Stop-output decision:** Codex 0.147.0 requires valid JSON on stdout for a
> successful `Stop` Hook. The zero-stdout decision below caused the v1.2.4-v1.2.6
> regression and is replaced by `2026-08-14-stop-hook-json-success.md` and v1.2.7.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the notification-only Codex hooks complete without producing parser-sensitive stdout, and preserve actionable diagnostics when the usage App Server cannot refresh.

**Architecture:** The hook wrapper becomes a one-way notification boundary: it forwards stdin to EventBridge, discards the helper's stdout/stderr, and returns only the helper exit status. EventBridge itself also emits no Codex control JSON because this integration never controls turn flow. The usage client keeps a bounded copy of App Server stderr, while the tray writes exception plus stderr context to a local diagnostic log instead of mislabeling every unknown failure as a network problem.

**Tech Stack:** .NET 8, Windows Forms, PowerShell 5.1, Codex CLI 0.147 hook/App Server protocols, GitHub Actions on `windows-latest`.

## Global Constraints

- Preserve all existing ChatGPT browser notification and exact-tab-focus behavior.
- Hook notification failures must never stop, continue, approve, or deny a Codex operation.
- Hook stdout must be empty for `UserPromptSubmit`, `PermissionRequest`, and `Stop`.
- Diagnostics must not include hook payloads, prompts, assistant messages, tokens, or environment variables.
- Existing dirty checkout files are user-owned; all changes stay in the isolated worktree.

---

### Task 1: Make Hook execution output-free

**Files:**
- Modify: `tests/EventBridgeHook.Tests.ps1`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`
- Modify: `scripts/setup-integration.ps1`
- Modify: `src/CodexUsageTray.EventBridge/Program.cs`
- Delete: `src/CodexUsageTray.Core/HookProtocolOutput.cs`

**Interfaces:**
- Consumes: Codex hook JSON on stdin and the existing named-pipe activity delivery path.
- Produces: exit code `0` and exactly zero stdout/stderr bytes for notification-only hooks.

- [ ] **Step 1: Write the failing integration assertions**

Change the installed-wrapper test so a valid Stop payload and a notification-parse failure both assert an empty stdout byte array. Add a direct EventBridge invocation assertion so removing wrapper redirection cannot hide a future bridge regression.

- [ ] **Step 2: Run Windows CI and verify RED**

Run the PR workflow on the test-only commit. Expected: `EventBridgeHook.Tests.ps1` fails because v1.2.3 emits `{"continue":true}`.

- [ ] **Step 3: Implement the minimal output-free boundary**

Generate the wrapper with the executable line redirected to `>nul 2>nul`, remove the EventBridge `Console.Out.Write(...)` path, and remove the now-unused `HookProtocolOutput` helper and its unit test.

- [ ] **Step 4: Run the focused and full Windows suites**

Expected: EventBridge integration, Core, Windows UI, PowerShell installer, browser tests, and package smoke tests all pass.

- [ ] **Step 5: Commit**

Commit the tested hook boundary independently with message `fix: make Codex notification hooks output-free`.

### Task 2: Preserve actionable usage diagnostics

**Files:**
- Create: `src/CodexUsageTray.Core/BoundedDiagnosticBuffer.cs`
- Create: `src/CodexUsageTray/DiagnosticLog.cs`
- Modify: `src/CodexUsageTray.Core/CodexAppServerClient.cs`
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Produces: `CodexAppServerClient.GetDiagnosticSummary()` returning at most 8 KiB of recent stderr lines; `DiagnosticLog.Append(Exception, string)` writing a bounded local log under the installation-local app-data directory.
- Consumes: App Server stderr only; never JSON-RPC stdin/stdout payloads.

- [ ] **Step 1: Write failing buffer and redaction tests**

Use literal stderr fixtures to verify newest-line retention, the 8 KiB bound, and removal of bearer-token-shaped text. Add a UI-side test that a generic failure points to the diagnostic log instead of claiming the network is definitely at fault.

- [ ] **Step 2: Run Windows CI and verify RED**

Expected: Core and Windows tests fail because the buffer/log APIs do not exist and the current text says to check the network.

- [ ] **Step 3: Implement bounded stderr capture and log access**

Drain StandardError line-by-line into the bounded buffer, expose a snapshot method, write sanitized exception metadata to the log on refresh failure, and add a tray menu command that opens the log directory.

- [ ] **Step 4: Run focused and full suites**

Expected: all test projects and package smoke tests pass without changing successful usage-refresh behavior.

- [ ] **Step 5: Commit**

Commit with message `fix: expose Codex usage refresh diagnostics`.

### Task 3: Adversarial review and release

**Files:**
- Modify: `.release-version`
- Modify: `README.md`

**Interfaces:**
- Produces: signed-by-checksum `v1.2.4` release assets and the existing one-line online installer path.

- [ ] **Step 1: Run mutation checks**

Temporarily remove wrapper redirection and confirm the direct/wrapper Stop tests fail. Temporarily restore Stop JSON output and confirm the tests fail. Temporarily remove diagnostic truncation/redaction and confirm the diagnostic tests fail. Revert each mutation before continuing.

- [ ] **Step 2: Run release workflow on a release branch**

Expected: Windows build, all regression suites, installed-package smoke test, ZIP checksum generation, and release asset upload pass.

- [ ] **Step 3: Verify release assets**

Confirm `CodexUsageTray-win-x64.zip` and `CodexUsageTray-win-x64.zip.sha256` exist on the GitHub release and the published tag matches `.release-version`.

- [ ] **Step 4: Hand off one-line reinstall and real-machine checks**

Use the existing `irm .../install-online.ps1 | iex` command, restart Codex, run a one-second prompt, and verify `UserPromptSubmit (completed)`, a top popup after the final response, and `Stop (completed)` with no invalid-JSON error.
