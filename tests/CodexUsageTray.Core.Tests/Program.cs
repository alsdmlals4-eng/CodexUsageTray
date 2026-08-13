using System.Text.Json;
using CodexUsageTray.Core;

namespace CodexUsageTray.Core.Tests;

internal static class Program
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("remaining percent clamps and floors", TestRemainingPercent),
            ("severity follows approved thresholds", TestSeverityThresholds),
            ("tray uses the most constrained window", TestMostConstrainedWindow),
            ("window labels are localized from duration", TestWindowLabels),
            ("multi-bucket response parses primary and secondary", TestMultiBucketParsing),
            ("legacy single-bucket response remains supported", TestLegacyParsing),
            ("response without windows is rejected", TestMissingWindows),
            ("malformed multi-bucket response cannot fall back to legacy", TestMalformedMultiBucket),
            ("JSON-RPC skips notifications and matches response", TestJsonRpcNotificationSkipping),
            ("JSON-RPC exposes server errors", TestJsonRpcError),
            ("JSON-RPC reports unexpected EOF", TestJsonRpcEof),
            ("JSON-RPC honors cancellation", TestJsonRpcCancellation),
            ("JSON-RPC disposal cancels an active read", TestJsonRpcDisposal),
            ("tooltip stays within NotifyIcon limit", TestTooltipLength),
            ("flyout row includes remaining and local reset", TestFlyoutRow),
            ("user prompt hook becomes running activity", TestPromptActivity),
            ("permission hook becomes approval activity", TestPermissionActivity),
            ("stop hook becomes completed activity", TestCompletedActivity),
            ("notification hooks emit no Codex control output", TestHookSuccessOutput),
            ("unknown hook input is rejected", TestUnknownActivity),
            ("activity store updates a turn and keeps newest first", TestActivityStore),
            ("activity event survives IPC JSON round trip", TestActivitySerialization),
            ("web completion keeps only safe navigation metadata", TestWebCompletionActivity),
            ("web approval becomes a persistent approval activity", TestWebApprovalActivity),
            ("web activity rejects non-ChatGPT navigation URLs", TestWebActivityRejectsUnsafeUrl),
            ("web activity preserves trusted source tab identity", TestWebSourceIdentity),
            ("browser connection IDs are validated before pipe use", TestBrowserConnectionIdentity),
            ("browser activation command contains exact source identity", TestBrowserActivationCommand)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static Task TestRemainingPercent()
    {
        Equal(100, UsagePresentation.GetRemainingPercent(-4), "negative usage must clamp to zero");
        Equal(73, UsagePresentation.GetRemainingPercent(26.1), "display must not overstate remaining quota");
        Equal(0, UsagePresentation.GetRemainingPercent(140), "usage above 100 must clamp");
        return Task.CompletedTask;
    }

    private static Task TestSeverityThresholds()
    {
        Equal(UsageSeverity.Normal, UsagePresentation.GetSeverity(51), "51% is normal");
        Equal(UsageSeverity.Warning, UsagePresentation.GetSeverity(50), "50% is warning");
        Equal(UsageSeverity.Warning, UsagePresentation.GetSeverity(20), "20% is warning");
        Equal(UsageSeverity.Critical, UsagePresentation.GetSeverity(19), "19% is critical");
        return Task.CompletedTask;
    }

    private static Task TestMostConstrainedWindow()
    {
        var snapshot = new UsageSnapshot(
            new[]
            {
                Window("codex", "primary", 12, 300, 1_786_627_200),
                Window("codex", "secondary", 67, 10_080, 1_787_232_000)
            },
            ObservedAt);

        Equal(33, UsagePresentation.GetTrayPercent(snapshot), "the smallest remaining allowance must win");
        return Task.CompletedTask;
    }

    private static Task TestWindowLabels()
    {
        Equal("5시간", UsagePresentation.GetWindowLabel(Window("codex", "primary", 0, 300, 0)), "five-hour label");
        Equal("주간", UsagePresentation.GetWindowLabel(Window("codex", "secondary", 0, 10_080, 0)), "weekly label");
        Equal("1일", UsagePresentation.GetWindowLabel(Window("other", "primary", 0, 1_440, 0)), "daily label");
        Equal("90분", UsagePresentation.GetWindowLabel(Window("other", "primary", 0, 90, 0)), "minute fallback");
        return Task.CompletedTask;
    }

    private static Task TestMultiBucketParsing()
    {
        const string json = """
        {
          "rateLimits": { "limitId": "duplicate", "primary": { "usedPercent": 99, "windowDurationMins": 15, "resetsAt": 1 } },
          "rateLimitsByLimitId": {
            "codex": {
              "limitId": "codex",
              "limitName": null,
              "planType": "pro",
              "primary": { "usedPercent": 25.4, "windowDurationMins": 300, "resetsAt": 1786627200 },
              "secondary": { "usedPercent": 42, "windowDurationMins": 10080, "resetsAt": 1787232000 }
            },
            "codex_other": {
              "limitId": "codex_other",
              "limitName": "리뷰",
              "primary": { "usedPercent": 4, "windowDurationMins": 60, "resetsAt": 1786620000 },
              "secondary": null
            }
          }
        }
        """;

        var snapshot = RateLimitParser.Parse(json, ObservedAt);

        Equal(3, snapshot.Windows.Count, "multi-bucket view must take precedence without legacy duplication");
        Equal("codex", snapshot.Windows[0].LimitId, "limit id");
        Equal("primary", snapshot.Windows[0].WindowKind, "primary kind");
        Equal(25.4, snapshot.Windows[0].UsedPercent, "fractional usage");
        Equal(300, snapshot.Windows[0].WindowDurationMinutes, "duration");
        Equal(DateTimeOffset.FromUnixTimeSeconds(1_786_627_200), snapshot.Windows[0].ResetsAt, "reset time");
        Equal("pro", snapshot.Windows[0].PlanType ?? string.Empty, "plan type");
        Equal("secondary", snapshot.Windows[1].WindowKind, "secondary kind");
        Equal("리뷰", snapshot.Windows[2].LimitName ?? string.Empty, "optional display name");
        return Task.CompletedTask;
    }

    private static Task TestLegacyParsing()
    {
        const string json = """
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": { "usedPercent": -3, "windowDurationMins": 300, "resetsAt": 1786627200 },
            "secondary": null
          }
        }
        """;

        var snapshot = RateLimitParser.Parse(json, ObservedAt);
        Equal(1, snapshot.Windows.Count, "legacy primary window");
        Equal(0d, snapshot.Windows[0].UsedPercent, "parser must clamp server anomalies");
        return Task.CompletedTask;
    }

    private static Task TestMissingWindows()
    {
        Throws<InvalidDataException>(() => RateLimitParser.Parse("{\"rateLimitsByLimitId\":{}}", ObservedAt));
        return Task.CompletedTask;
    }

    private static Task TestMalformedMultiBucket()
    {
        const string json = """
        {
          "rateLimitsByLimitId": null,
          "rateLimits": {
            "limitId": "stale",
            "primary": { "usedPercent": 1, "windowDurationMins": 300, "resetsAt": 1786627200 }
          }
        }
        """;

        Throws<InvalidDataException>(() => RateLimitParser.Parse(json, ObservedAt));
        return Task.CompletedTask;
    }

    private static async Task TestJsonRpcNotificationSkipping()
    {
        using var reader = new StringReader("{\"method\":\"account/rateLimits/updated\",\"params\":{}}\n{\"id\":1,\"result\":{\"ok\":true}}\n");
        using var writer = new StringWriter();
        await using var connection = new JsonRpcConnection(reader, writer);

        var result = await connection.SendRequestAsync("account/rateLimits/read", null, CancellationToken.None);

        True(result.GetProperty("ok").GetBoolean(), "matched result must be returned");
        using var request = JsonDocument.Parse(writer.ToString());
        Equal("account/rateLimits/read", request.RootElement.GetProperty("method").GetString() ?? string.Empty, "request method");
        Equal(1, request.RootElement.GetProperty("id").GetInt32(), "first request id");
    }

    private static async Task TestJsonRpcError()
    {
        using var reader = new StringReader("{\"id\":1,\"error\":{\"code\":401,\"message\":\"Not logged in\"}}\n");
        using var writer = new StringWriter();
        await using var connection = new JsonRpcConnection(reader, writer);

        var exception = await ThrowsAsync<JsonRpcException>(
            () => connection.SendRequestAsync("account/rateLimits/read", null, CancellationToken.None));

        Equal(401, exception.Code, "JSON-RPC error code");
        True(exception.Message.Contains("Not logged in", StringComparison.Ordinal), "server message");
    }

    private static async Task TestJsonRpcEof()
    {
        using var reader = new StringReader(string.Empty);
        using var writer = new StringWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        await ThrowsAsync<EndOfStreamException>(
            () => connection.SendRequestAsync("account/rateLimits/read", null, CancellationToken.None));
    }

    private static async Task TestJsonRpcCancellation()
    {
        using var reader = new BlockingTextReader();
        using var writer = new StringWriter();
        await using var connection = new JsonRpcConnection(reader, writer);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await ThrowsAsync<OperationCanceledException>(
            () => connection.SendRequestAsync("account/rateLimits/read", null, cancellation.Token));
    }

    private static async Task TestJsonRpcDisposal()
    {
        using var reader = new BlockingTextReader();
        using var writer = new StringWriter();
        var connection = new JsonRpcConnection(reader, writer);
        var request = connection.SendRequestAsync("account/rateLimits/read", null, CancellationToken.None);
        await Task.Delay(20);

        await connection.DisposeAsync();

        await ThrowsAsync<OperationCanceledException>(() => request);
    }

    private static Task TestTooltipLength()
    {
        var snapshot = new UsageSnapshot(
            new[] { Window("codex-with-a-very-long-identifier", "primary", 12, 300, 1_786_627_200) },
            ObservedAt);
        var tooltip = UsagePresentation.BuildTooltip(snapshot);
        True(tooltip.Length <= 63, "NotifyIcon tooltip cannot exceed 63 characters");
        True(tooltip.Contains("88%", StringComparison.Ordinal), "tooltip must include remaining percent");
        return Task.CompletedTask;
    }

    private static Task TestFlyoutRow()
    {
        var korea = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        var row = UsagePresentation.CreateDisplayRow(
            Window("codex", "primary", 25.4, 300, 1_786_651_200),
            korea);

        Equal("5시간", row.Title, "title");
        Equal("74% 남음", row.RemainingText, "remaining quota floors conservatively");
        Equal("08-14 05:00 초기화", row.ResetText, "local reset text");
        return Task.CompletedTask;
    }

    private static Task TestPromptActivity()
    {
        const string json = """
        {
          "session_id": "thr_1234567890",
          "turn_id": "turn_abc",
          "cwd": "C:\\Users\\dev\\Documents\\Base",
          "hook_event_name": "UserPromptSubmit",
          "model": "gpt-5.6-terra",
          "prompt": "테스트를 실행하고 결과를 검증해줘"
        }
        """;

        var activity = ActivityEventParser.ParseHook(json, ObservedAt);

        Equal(ActivityStatus.Running, activity.Status, "prompt status");
        Equal("Base", activity.ProjectName, "Windows project folder");
        Equal("테스트를 실행하고 결과를 검증해줘", activity.Summary, "prompt summary");
        Equal("thr_1234", activity.ChatLabel, "short chat label");
        return Task.CompletedTask;
    }

    private static Task TestPermissionActivity()
    {
        const string json = """
        {
          "session_id": "thr_permission",
          "turn_id": "turn_permission",
          "cwd": "C:/Games/GRIMOIRE-",
          "hook_event_name": "PermissionRequest",
          "tool_name": "Bash",
          "tool_input": {
            "command": "git push origin feature/test",
            "description": "원격 저장소에 변경 사항 푸시"
          }
        }
        """;

        var activity = ActivityEventParser.ParseHook(json, ObservedAt);

        Equal(ActivityStatus.ApprovalRequired, activity.Status, "permission status");
        Equal("GRIMOIRE-", activity.ProjectName, "project");
        Equal("원격 저장소에 변경 사항 푸시", activity.Summary, "human reason must win");
        True(activity.Detail.Contains("git push", StringComparison.Ordinal), "command detail");
        return Task.CompletedTask;
    }

    private static Task TestCompletedActivity()
    {
        const string json = """
        {
          "session_id": "thr_done",
          "turn_id": "turn_done",
          "cwd": "D:/Projects/ToolHub",
          "hook_event_name": "Stop",
          "stop_hook_active": false,
          "last_assistant_message": "테스트 13개가 모두 통과했고 빌드를 완료했습니다."
        }
        """;

        var activity = ActivityEventParser.ParseHook(json, ObservedAt);

        Equal(ActivityStatus.Completed, activity.Status, "completion status");
        Equal("테스트 13개가 모두 통과했고 빌드를 완료했습니다.", activity.Summary, "assistant summary");
        return Task.CompletedTask;
    }

    private static Task TestUnknownActivity()
    {
        Throws<InvalidDataException>(() => ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_x\",\"cwd\":\"C:/x\",\"hook_event_name\":\"PostToolUse\"}",
            ObservedAt));
        return Task.CompletedTask;
    }

    private static Task TestHookSuccessOutput()
    {
        Equal(string.Empty, HookProtocolOutput.GetSuccessJson("Stop"),
            "notification-only Stop hooks must not enter the Codex control-output parser");
        Equal(string.Empty, HookProtocolOutput.GetSuccessJson("UserPromptSubmit"),
            "prompt hooks must not receive Stop-only output");
        Equal(string.Empty, HookProtocolOutput.GetSuccessJson("PermissionRequest"),
            "permission hooks must not receive Stop-only output");
        Equal(string.Empty, HookProtocolOutput.GetSuccessJson(null),
            "missing event names must not emit misleading output");
        return Task.CompletedTask;
    }

    private static Task TestActivityStore()
    {
        var store = new ActivityStore(capacity: 2);
        var running = ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_1\",\"turn_id\":\"turn_1\",\"cwd\":\"C:/One\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"시작\"}",
            ObservedAt);
        var completed = ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_1\",\"turn_id\":\"turn_1\",\"cwd\":\"C:/One\",\"hook_event_name\":\"Stop\",\"last_assistant_message\":\"완료\"}",
            ObservedAt.AddMinutes(1));
        var second = ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_2\",\"turn_id\":\"turn_2\",\"cwd\":\"C:/Two\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"두 번째\"}",
            ObservedAt.AddMinutes(2));
        var third = ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_3\",\"turn_id\":\"turn_3\",\"cwd\":\"C:/Three\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"세 번째\"}",
            ObservedAt.AddMinutes(3));

        store.AddOrUpdate(running);
        store.AddOrUpdate(completed);
        store.AddOrUpdate(second);
        store.AddOrUpdate(third);
        var items = store.Snapshot();

        Equal(2, items.Count, "capacity");
        Equal("thr_3", items[0].SessionId, "newest first");
        Equal("thr_2", items[1].SessionId, "oldest item evicted");
        return Task.CompletedTask;
    }

    private static Task TestActivitySerialization()
    {
        var original = ActivityEventParser.ParseHook(
            "{\"session_id\":\"thr_pipe\",\"turn_id\":\"turn_pipe\",\"cwd\":\"C:/Pipe\",\"hook_event_name\":\"Stop\",\"last_assistant_message\":\"완료\"}",
            ObservedAt).WithTerminal(42, 1234, "PowerShell #2");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ActivityEvent>(json) ??
            throw new InvalidOperationException("activity IPC payload did not deserialize");

        Equal(original, restored, "activity IPC round trip");
        return Task.CompletedTask;
    }

    private static Task TestWebCompletionActivity()
    {
        const string json = """
        {
          "status": "completed",
          "activityId": "web-turn-17",
          "url": "https://chatgpt.com/c/abc-123?temporary-chat=true#bottom",
          "title": "Windows 알림 앱 만들기",
          "summary": "이 값은 대화 본문일 수 있으므로 절대 사용하면 안 됩니다."
        }
        """;

        var activity = BrowserActivityEventParser.Parse(json, ObservedAt);

        Equal(ActivitySourceKind.ChatGptWeb, activity.SourceKind, "web source kind");
        Equal(ActivityStatus.Completed, activity.Status, "web completion status");
        Equal("https://chatgpt.com/c/abc-123", activity.SourceUri ?? string.Empty,
            "query and fragment must be removed from the navigation target");
        Equal("Windows 알림 앱 만들기", activity.ChatLabel, "safe tab title label");
        Equal("ChatGPT 응답 생성이 끝났습니다.", activity.Summary,
            "extension-provided body-like text must be ignored");

        var codexActivity = BrowserActivityEventParser.Parse(
            "{\"status\":\"completed\",\"activityId\":\"job-1\",\"url\":\"https://chatgpt.com/codex/tasks/task-42\",\"title\":\"Codex 작업\"}",
            ObservedAt);
        Equal("web:task-42", codexActivity.SessionId, "ChatGPT Codex task routes are supported");
        return Task.CompletedTask;
    }

    private static Task TestWebApprovalActivity()
    {
        const string json = """
        {
          "status": "approval_required",
          "activityId": "approval-3",
          "url": "https://chatgpt.com/g/g-example/c/thread-99",
          "title": "배포 확인"
        }
        """;

        var activity = BrowserActivityEventParser.Parse(json, ObservedAt);

        Equal(ActivityStatus.ApprovalRequired, activity.Status, "web approval status");
        Equal("web:thread-99", activity.SessionId, "conversation id becomes the web session key");
        Equal("approval-3", activity.TurnId ?? string.Empty, "browser activity id becomes the turn key");
        Equal("ChatGPT에서 승인 또는 확인이 필요합니다.", activity.Summary, "approval summary");
        return Task.CompletedTask;
    }

    private static Task TestWebActivityRejectsUnsafeUrl()
    {
        Throws<InvalidDataException>(() => BrowserActivityEventParser.Parse(
            "{\"status\":\"completed\",\"activityId\":\"x\",\"url\":\"https://example.com/c/stolen\"}",
            ObservedAt));
        Throws<InvalidDataException>(() => BrowserActivityEventParser.Parse(
            "{\"status\":\"completed\",\"activityId\":\"x\",\"url\":\"javascript:alert(1)\"}",
            ObservedAt));
        return Task.CompletedTask;
    }

    private static Task TestWebSourceIdentity()
    {
        const string json = """
        {
          "status": "completed",
          "activityId": "complete-22",
          "url": "https://chatgpt.com/c/thread-22",
          "title": "기존 탭 복귀",
          "tabId": 117,
          "windowId": 9
        }
        """;

        var activity = BrowserActivityEventParser.Parse(json, ObservedAt);

        Equal(117, activity.BrowserTabId, "source tab id");
        Equal(9, activity.BrowserWindowId, "source window id");
        Throws<InvalidDataException>(() => BrowserActivityEventParser.Parse(
            json.Replace("\"tabId\": 117", "\"tabId\": 0", StringComparison.Ordinal),
            ObservedAt));
        return Task.CompletedTask;
    }

    private static Task TestBrowserConnectionIdentity()
    {
        const string connectionId = "90d5919d-6e93-4f12-8187-51ff6cc7af4b";
        var activity = BrowserActivityEventParser.Parse(
            "{\"status\":\"completed\",\"activityId\":\"x\",\"url\":\"https://chatgpt.com/c/thread-x\",\"tabId\":17,\"windowId\":3}",
            ObservedAt);

        var connected = activity.WithBrowserConnection(connectionId);

        Equal(connectionId, connected.BrowserConnectionId ?? string.Empty, "browser connection id");
        Equal(
            "CodexUsageTray.BrowserCommands.v1.90d5919d6e934f12818751ff6cc7af4b",
            ActivityPipeNames.GetBrowserCommandPipeName(connectionId),
            "connection-specific pipe name");
        Throws<ArgumentException>(() => activity.WithBrowserConnection("not-a-guid"));
        Throws<ArgumentException>(() => ActivityPipeNames.GetBrowserCommandPipeName("../unsafe"));
        return Task.CompletedTask;
    }

    private static Task TestBrowserActivationCommand()
    {
        const string connectionId = "90d5919d-6e93-4f12-8187-51ff6cc7af4b";
        var activity = BrowserActivityEventParser.Parse(
            "{\"status\":\"completed\",\"activityId\":\"x\",\"url\":\"https://chatgpt.com/c/thread-x\",\"tabId\":17,\"windowId\":3}",
            ObservedAt).WithBrowserConnection(connectionId);

        var command = BrowserActivationCommand.FromActivity(activity);

        Equal("activate", command.Action, "activation action");
        Equal("https://chatgpt.com/c/thread-x", command.Url, "safe target URL");
        Equal(17, command.TabId, "preferred tab id");
        Equal(3, command.WindowId, "source window id");
        var restored = JsonSerializer.Deserialize<BrowserActivationCommand>(
            JsonSerializer.Serialize(command)) ??
            throw new InvalidOperationException("browser command did not deserialize");
        Equal(command, restored, "browser command IPC round trip");
        True(BrowserActivationCommand.TryParse(JsonSerializer.Serialize(command), out var parsed),
            "valid activation command is accepted");
        Equal(command, parsed, "validated activation command");
        True(!BrowserActivationCommand.TryParse(
                "{\"action\":\"activate\",\"url\":\"https://example.com/c/stolen\",\"tabId\":17,\"windowId\":3}",
                out _),
            "unsafe activation command is rejected");
        return Task.CompletedTask;
    }

    private static QuotaWindow Window(
        string limitId,
        string kind,
        double usedPercent,
        int durationMinutes,
        long resetsAtUnixSeconds,
        string? limitName = null,
        string? planType = null) =>
        new(
            limitId,
            limitName,
            kind,
            usedPercent,
            durationMinutes,
            DateTimeOffset.FromUnixTimeSeconds(resetsAtUnixSeconds),
            planType);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected <{expected}>, actual <{actual}>");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }

    private sealed class BlockingTextReader : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
