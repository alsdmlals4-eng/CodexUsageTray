# Mobile Push Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver existing Codex terminal and ChatGPT Web `ApprovalRequired`/`Completed` activities to the user's ntfy phone topic without changing the existing Windows popup path.

**Architecture:** Keep both event collectors unchanged and fan out only after they reach `TrayApplicationContext.HandleActivity()`. Persist mobile settings outside the replace-on-update install directory, format and deduplicate events in a focused notifier, and use an injected `HttpClient`-backed ntfy JSON client so network behavior is testable and isolated from Hook/native-messaging protocol boundaries.

**Tech Stack:** .NET 8, Windows Forms, `System.Net.Http`, `System.Text.Json`, existing console-style Windows regression test project, GitHub Actions Windows CI.

## Global Constraints

- Work only on `feature/mobile-push-notifications`; do not modify open PR #17 or its recovery-watchdog work.
- Do not modify Codex Hook stdout/protocol behavior or browser-extension host permissions.
- Push only `ApprovalRequired` and `Completed`; never push `Running`.
- Mobile failures must never block or suppress Windows activity history/popups.
- Never persist the ntfy topic in the repository, release package, or diagnostics log.
- Persist user settings under `%LOCALAPPDATA%\\CodexUsageTrayData\\mobile-notifications.json`, outside `%LOCALAPPDATA%\\CodexUsageTray`.
- Use `https://ntfy.sh` for v1 and do not add Firebase, a custom server, remote approval, or a retry queue.
- Deduplicate the same `ActivityKey + Status` in memory while allowing `ApprovalRequired -> Completed` for the same activity.

---

### Task 1: Settings persistence contract

**Files:**
- Create: `src/CodexUsageTray/MobileNotificationSettings.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Produces: `MobileNotificationSettings(bool Enabled, string Topic)`.
- Produces: `MobileNotificationSettingsStore(string? path = null)` with `Load()` and `Save(MobileNotificationSettings settings)`.
- Produces: `MobileNotificationSettingsStore.DefaultPath` under `CodexUsageTrayData`.

- [ ] **Step 1: Write failing tests**

Add tests that save/load a temporary settings file, assert the default path contains `CodexUsageTrayData` and not the install executable directory, and assert malformed JSON loads as disabled/empty rather than crashing.

```csharp
private static void MobileSettingsRoundTripOutsideInstallDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"mobile-{Guid.NewGuid():N}.json");
    var store = new MobileNotificationSettingsStore(path);
    store.Save(new MobileNotificationSettings(true, "gpt-notify-secret"));
    var loaded = store.Load();
    Assert(loaded.Enabled && loaded.Topic == "gpt-notify-secret", "mobile settings must round-trip");
    Assert(MobileNotificationSettingsStore.DefaultPath.Contains("CodexUsageTrayData", StringComparison.Ordinal),
        "mobile settings must live outside the replace-on-update install directory");
}
```

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet run --configuration Release --project .\\tests\\CodexUsageTray.Windows.Tests\\CodexUsageTray.Windows.Tests.csproj
```

Expected: build failure because `MobileNotificationSettingsStore` does not exist.

- [ ] **Step 3: Implement minimal settings store**

Use `System.Text.Json`, trim the topic before saving, create the parent directory, and return `new(false, string.Empty)` for missing or malformed files. Do not log or throw the topic on parse failure.

- [ ] **Step 4: Run GREEN and commit**

Run the Windows regression test command above; expect all tests to pass. Commit settings tests and implementation together after RED has been observed.

---

### Task 2: ntfy client, formatting, filtering, and deduplication

**Files:**
- Create: `src/CodexUsageTray/NtfyPushClient.cs`
- Create: `src/CodexUsageTray/MobilePushNotifier.cs`
- Modify: `src/CodexUsageTray/DiagnosticLog.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Produces: `NtfyPushClient(HttpClient? httpClient = null)` and `SendAsync(string topic, string title, string message, int priority, CancellationToken cancellationToken)`.
- Produces: `MobilePushNotifier(Func<MobileNotificationSettings> settingsProvider, Func<string,string,string,int,CancellationToken,Task> sender, Action<Exception>? failureSink = null)`.
- Produces: `Task NotifyAsync(ActivityEvent activity, CancellationToken cancellationToken = default)`.
- Produces: `MobilePushMessage? MobilePushNotifier.CreateMessage(ActivityEvent activity)` for deterministic tests.
- Produces: `DiagnosticLog.AppendMobilePush(Exception exception)` with no topic/request-body logging.

- [ ] **Step 1: Write failing policy tests**

Add tests proving:

```csharp
Assert(MobilePushNotifier.CreateMessage(CreateActivity("s", "t", ActivityStatus.Running)) is null,
    "running activities must not produce mobile notifications");
Assert(MobilePushNotifier.CreateMessage(CreateActivity("s", "t", ActivityStatus.Completed)) is not null,
    "completed activities must produce mobile notifications");
```

Add an injected sender that counts calls and verifies identical `ActivityKey + Status` is sent once, while approval followed by completion is sent twice. Add a sender that throws and assert `NotifyAsync` completes without propagating the exception while the failure sink is called exactly once.

- [ ] **Step 2: Run RED**

Run the Windows regression test project. Expected: build failure because notifier/client types are missing.

- [ ] **Step 3: Implement minimal notifier and ntfy JSON transport**

Post JSON to the fixed root endpoint so the topic never appears in the request URL:

```csharp
var payload = JsonSerializer.Serialize(new
{
    topic = topic.Trim(),
    title,
    message,
    priority
});
using var request = new HttpRequestMessage(HttpMethod.Post, "https://ntfy.sh")
{
    Content = new StringContent(payload, Encoding.UTF8, "application/json")
};
using var response = await _httpClient.SendAsync(request, cancellationToken);
response.EnsureSuccessStatusCode();
```

Use a bounded in-memory set/queue (maximum 256 keys) for dedupe. Catch all non-cancellation delivery failures in `NotifyAsync`, send them to `failureSink`, and never rethrow into the activity path.

- [ ] **Step 4: Run GREEN and commit**

Run Windows regression tests and the full solution build. Expect both to pass before committing.

---

### Task 3: Tray fan-out and settings UI

**Files:**
- Create: `src/CodexUsageTray/MobileNotificationSettingsForm.cs`
- Modify: `src/CodexUsageTray/TrayApplicationContext.cs`
- Modify: `tests/CodexUsageTray.Windows.Tests/Program.cs`

**Interfaces:**
- Consumes: settings store, ntfy client, notifier from Tasks 1-2.
- Produces: tray menu item `휴대폰 알림 설정`.
- Produces: form controls for enabled/topic, `저장`, and `테스트 알림 전송`.

- [ ] **Step 1: Write failing UI/integration tests**

Add a test that constructs `MobileNotificationSettingsForm` with a temporary store and injected test sender, saves enabled/topic, reloads from the store, and invokes the test-send path without a real network request. Add a notifier integration test that verifies the activity handler can start an async mobile send without changing the existing popup queue contract.

- [ ] **Step 2: Run RED**

Run Windows regression tests. Expected: build failure because the settings form/integration seam is not present.

- [ ] **Step 3: Implement minimal UI and fan-out**

In `TrayApplicationContext` initialize one settings store, one `NtfyPushClient`, and one `MobilePushNotifier`. Add the tray menu item. After activity history and existing Windows popup handling, fire-and-observe mobile delivery:

```csharp
_ = _mobilePushNotifier.NotifyAsync(activity, _shutdown.Token);
```

The notifier must own failure isolation. The settings form uses `UseSystemPasswordChar = true` for the topic textbox, never includes the topic in error dialogs, and allows a test notification using the current textbox value.

- [ ] **Step 4: Run GREEN and commit**

Run Windows regression tests, then `dotnet build .\\CodexUsageTray.sln --configuration Release`. Expect no warnings or errors.

---

### Task 4: Documentation and full regression verification

**Files:**
- Modify: `README.md`

**Interfaces:**
- Documents the user path: tray right-click -> `휴대폰 알림 설정` -> topic -> enable -> test.
- Documents that PC/tray must be running, but ChatGPT/Codex can remain in the background.

- [ ] **Step 1: Update user documentation**

Document ntfy setup, privacy boundary (title/project/summary metadata only), settings persistence location, and failure behavior. Do not put a real topic example copied from the user into the repository.

- [ ] **Step 2: Run all repository verification**

Run or require GitHub Actions to execute:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\\tests\\PowerShellInstaller.Tests.ps1
dotnet run --configuration Release --project .\\tests\\CodexUsageTray.Core.Tests\\CodexUsageTray.Core.Tests.csproj
dotnet run --configuration Release --project .\\tests\\CodexUsageTray.Windows.Tests\\CodexUsageTray.Windows.Tests.csproj
dotnet build .\\CodexUsageTray.sln --configuration Release
```

Also retain all browser-extension Node validation and EventBridge Hook integration tests from `.github/workflows/ci.yml` unchanged.

- [ ] **Step 3: Adversarial regression review**

Verify the diff contains no ntfy topic value, no browser-extension permission change, no Hook protocol change, no recovery-watchdog files from PR #17, and no `ActivityStatus` expansion.

- [ ] **Step 4: Open PR and verify CI**

Open a PR from `feature/mobile-push-notifications` to `main`. Do not merge until all CI checks are green and the final diff review passes.
