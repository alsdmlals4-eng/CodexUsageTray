# Codex Usage Tray Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows system-tray app that displays the most constrained Codex usage allowance and shows every quota window on demand.

**Architecture:** A dependency-free .NET 8 core library owns JSON-RPC, Codex App Server process control, response parsing, and presentation calculations. A small WinForms shell owns only tray rendering, the details flyout, startup registration, and refresh scheduling.

**Tech Stack:** C# 12, .NET 8, WinForms, System.Text.Json, System.Drawing, Windows Registry

## Global Constraints

- Target Windows 10/11 x64.
- Use the official `codex app-server` stdio JSONL protocol and `account/rateLimits/read`.
- Never read or store ChatGPT tokens, browser cookies, or an OpenAI API key.
- Refresh every five minutes and allow manual refresh.
- Publish a self-contained single-file executable without third-party NuGet packages.
- Preserve the last successful snapshot during transient failures.

---

## File map

- `src/CodexUsageTray.Core/Models.cs`: immutable quota and snapshot records.
- `src/CodexUsageTray.Core/UsagePresentation.cs`: remaining-percent, label, and severity calculations.
- `src/CodexUsageTray.Core/RateLimitParser.cs`: official response compatibility parser.
- `src/CodexUsageTray.Core/JsonRpcConnection.cs`: newline JSON request correlation.
- `src/CodexUsageTray.Core/CodexAppServerClient.cs`: process and handshake lifecycle.
- `src/CodexUsageTray/TrayIconRenderer.cs`: native icon creation and cleanup.
- `src/CodexUsageTray/UsageFlyoutForm.cs`: details UI.
- `src/CodexUsageTray/TrayApplicationContext.cs`: app orchestration.
- `src/CodexUsageTray/StartupRegistration.cs`: per-user startup toggle.
- `tests/CodexUsageTray.Core.Tests/Program.cs`: dependency-free test runner.
- `scripts/build-windows.ps1`: test and publish gate.

### Task 1: Core quota model and presentation rules

**Files:**
- Create: `src/CodexUsageTray.Core/CodexUsageTray.Core.csproj`
- Create: `src/CodexUsageTray.Core/Models.cs`
- Create: `src/CodexUsageTray.Core/UsagePresentation.cs`
- Create: `tests/CodexUsageTray.Core.Tests/CodexUsageTray.Core.Tests.csproj`
- Create: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: `QuotaWindow`, `UsageSnapshot`, `UsageSeverity`, `UsagePresentation.GetRemainingPercent`, `GetSeverity`, `GetTrayPercent`, and `GetWindowLabel`.

- [ ] Write tests proving clamping, 50/20 thresholds, minimum remaining selection, and 300/10080-minute Korean labels.
- [ ] Run `dotnet run --project tests/CodexUsageTray.Core.Tests` and confirm failures identify missing production types.
- [ ] Implement immutable records and pure presentation functions.
- [ ] Re-run the test runner and confirm all Task 1 cases pass.
- [ ] Commit with `git commit -m "feat: add quota presentation model"`.

### Task 2: Rate-limit response parsing

**Files:**
- Create: `src/CodexUsageTray.Core/RateLimitParser.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Consumes: `QuotaWindow`, `UsageSnapshot`.
- Produces: `RateLimitParser.Parse(string json, DateTimeOffset observedAt)`.

- [ ] Add sample JSON tests for `rateLimitsByLimitId`, legacy `rateLimits`, null secondary windows, optional names, Unix reset times, and missing usable buckets.
- [ ] Run the test runner and confirm parser tests fail because `RateLimitParser` is absent.
- [ ] Implement tolerant `System.Text.Json` parsing while rejecting a response with no usable windows.
- [ ] Re-run and confirm all parser cases pass.
- [ ] Commit with `git commit -m "feat: parse Codex rate limit responses"`.

### Task 3: JSON-RPC and Codex process lifecycle

**Files:**
- Create: `src/CodexUsageTray.Core/JsonRpcConnection.cs`
- Create: `src/CodexUsageTray.Core/CodexAppServerClient.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: `JsonRpcConnection.SendRequestAsync(string, object?, CancellationToken)`, `SendNotificationAsync`, and `CodexAppServerClient.ReadUsageAsync`.

- [ ] Add in-memory reader/writer tests proving notification skipping, matching response IDs, JSON-RPC errors, cancellation, and EOF behavior.
- [ ] Run and confirm the new tests fail because the connection is absent.
- [ ] Implement the single-reader pending-request connection with deterministic disposal.
- [ ] Implement `codex app-server`, the `initialize`/`initialized` handshake, and one automatic process restart after failure.
- [ ] Re-run and confirm all Core tests pass.
- [ ] Commit with `git commit -m "feat: connect to Codex app server"`.

### Task 4: Windows tray shell

**Files:**
- Create: `src/CodexUsageTray/CodexUsageTray.csproj`
- Create: `src/CodexUsageTray/Program.cs`
- Create: `src/CodexUsageTray/TrayIconRenderer.cs`
- Create: `src/CodexUsageTray/UsageFlyoutForm.cs`
- Create: `src/CodexUsageTray/StartupRegistration.cs`
- Create: `src/CodexUsageTray/TrayApplicationContext.cs`

**Interfaces:**
- Consumes: `CodexAppServerClient`, `UsageSnapshot`, and `UsagePresentation`.
- Produces: a single-instance, no-console WinForms tray application.

- [ ] Add pure tests for tooltip truncation and flyout row text formatting before adding the UI formatters.
- [ ] Run and confirm formatter tests fail for the missing functions.
- [ ] Implement numeric icon rendering with explicit `HICON` cleanup and gray `?`/`!` error icons.
- [ ] Implement flyout rows, local reset times, refresh state, and last-success preservation.
- [ ] Implement menu actions, five-minute timer, serialized refresh, login launch, startup toggle, and shutdown cleanup.
- [ ] Run all Core tests and compile the Windows project.
- [ ] Commit with `git commit -m "feat: add Windows tray experience"`.

### Task 5: Build, install, and verification handoff

**Files:**
- Create: `CodexUsageTray.sln`
- Create: `scripts/build-windows.ps1`
- Create: `scripts/install.ps1`
- Create: `README.md`
- Create: `.gitignore`

**Interfaces:**
- Produces: `artifacts/win-x64/CodexUsageTray.exe` and beginner-friendly Korean setup instructions.

- [ ] Implement a PowerShell build gate that checks .NET 8, runs the dependency-free tests, and only then publishes `win-x64` self-contained single-file output.
- [ ] Implement per-user installation under `%LOCALAPPDATA%\CodexUsageTray` without administrator rights.
- [ ] Document Codex CLI prerequisite, `codex login`, build, install, update, uninstall, troubleshooting, and the exact Windows manual QA checklist.
- [ ] Run placeholder and secret scans, test runner, Release build, publish, and file inventory.
- [ ] Commit with `git commit -m "docs: add build and installation workflow"`.

### Task 6: Reliable process and shutdown lifecycle

**Files:**
- Modify: `src/CodexUsageTray.Core/CodexAppServerClient.cs`
- Modify: `src/CodexUsageTray.Core/JsonRpcConnection.cs`
- Modify: `src/CodexUsageTray.Core/RateLimitParser.cs`
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: transport-only restart policy, typed authentication failure, Windows command-shim resolution, bounded stderr draining, and cancellation-safe disposal.

- [ ] Add tests for authentication classification, multi-bucket presence, and cancellation/disposal behavior.
- [ ] Restrict retries to transport/process failures and use `account/read` for typed login state.
- [ ] Resolve `.exe`, `.cmd`, and `.bat` Codex launch targets on Windows.
- [ ] Cancel and await active refresh work before UI and connection disposal.
- [ ] Preserve stale details while replacing the tray icon for permanent authentication/CLI/format failures.

### Task 7: Codex activity event bridge

**Files:**
- Create: `src/CodexUsageTray.Core/ActivityModels.cs`
- Create: `src/CodexUsageTray.Core/ActivityEventParser.cs`
- Create: `src/CodexUsageTray.EventBridge/CodexUsageTray.EventBridge.csproj`
- Create: `src/CodexUsageTray.EventBridge/Program.cs`
- Create: `src/CodexUsageTray.EventBridge/TerminalContextResolver.cs`
- Create: `src/CodexUsageTray/ActivityPipeServer.cs`
- Create: `src/CodexUsageTray/ActivityHistoryForm.cs`
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: parsing for `UserPromptSubmit`, `PermissionRequest`, and `Stop`; current-user Named Pipe delivery; desktop balloon alerts; activity history; best-effort source-window activation.

- [ ] Add literal JSON tests for all three Hook event shapes and malformed input.
- [ ] Implement normalized activity models and bounded summary text.
- [ ] Implement the short-lived bridge and current-user-only pipe server.
- [ ] Implement recent activity UI, status counts, balloon alerts, and click-to-focus fallback.
- [ ] Verify Hook handlers never approve, deny, block, or continue a Codex operation.

### Task 8: Safe Hook installation and documentation

**Files:**
- Create: `scripts/setup-integration.ps1`
- Create: `scripts/remove-integration.ps1`
- Modify: `scripts/build-windows.ps1`
- Modify: `scripts/install.ps1`
- Modify: `README.md`
- Modify: `CodexUsageTray.sln`

**Interfaces:**
- Produces: reversible merging of three app-owned user Hook handlers without replacing unrelated handlers.

- [ ] Publish both single-file executables after tests pass.
- [ ] Back up `~/.codex/hooks.json`, merge only app-owned handlers, and write atomically.
- [ ] Remove only app-owned handlers during uninstall and retain other user Hooks.
- [ ] Document `/hooks` trust review and ChatGPT native notification settings.
- [ ] Run static syntax, placeholder, secret, diff, and artifact inventory checks.
