using CodexUsageTray.Core;

namespace CodexUsageTray.Recovery.Tests;

internal static class Program
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 15, 3, 30, 0, TimeSpan.FromHours(9));

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("web recovery statuses parse into safe activity events", TestRecoveryStatusParsing),
            ("disconnected waiting schedules bounded reload attempts", TestDisconnectedReloadSchedule),
            ("duplicate disconnected signals do not double schedule", TestDuplicateDisconnectedSignal),
            ("recovered or completed activity resets reconnect state", TestRecoveryReset)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Run();
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

    private static void TestRecoveryStatusParsing()
    {
        var retrying = Parse("retrying", "transient_error", 1);
        Equal(ActivityStatus.Retrying, retrying.Status, "retrying status");
        Contains(retrying.Summary, "재시도", "retrying summary");
        Equal("transient_error", retrying.Detail, "retrying reason");

        var required = Parse("recovery_required", "disconnected_waiting");
        Equal(ActivityStatus.RecoveryRequired, required.Status, "recovery-required status");
        Contains(required.Summary, "연결", "disconnect summary");
        Equal("disconnected_waiting", required.Detail, "disconnect reason");

        var recovered = Parse("recovered", "generation_resumed");
        Equal(ActivityStatus.Recovered, recovered.Status, "recovered status");
        Contains(recovered.Summary, "복구", "recovered summary");
        Equal("generation_resumed", recovered.Detail, "recovered reason");
    }

    private static void TestDisconnectedReloadSchedule()
    {
        var coordinator = new BrowserRecoveryCoordinator();
        var activity = Parse("recovery_required", "disconnected_waiting");

        var first = coordinator.Plan(activity);
        NotNull(first, "first reconnect instruction");
        Equal(1, first!.Attempt, "first attempt");
        Equal(TimeSpan.FromSeconds(3), first.Delay, "first delay");

        coordinator.MarkAttemptCompleted(activity.SessionId);
        var second = coordinator.Plan(activity);
        NotNull(second, "second reconnect instruction");
        Equal(2, second!.Attempt, "second attempt");
        Equal(TimeSpan.FromSeconds(10), second.Delay, "second delay");

        coordinator.MarkAttemptCompleted(activity.SessionId);
        var third = coordinator.Plan(activity);
        NotNull(third, "third reconnect instruction");
        Equal(3, third!.Attempt, "third attempt");
        Equal(TimeSpan.FromSeconds(30), third.Delay, "third delay");

        coordinator.MarkAttemptCompleted(activity.SessionId);
        Equal<BrowserRecoveryInstruction?>(null, coordinator.Plan(activity),
            "fourth reconnect must be blocked by the ceiling");
    }

    private static void TestDuplicateDisconnectedSignal()
    {
        var coordinator = new BrowserRecoveryCoordinator();
        var activity = Parse("recovery_required", "disconnected_waiting");

        NotNull(coordinator.Plan(activity), "first event schedules reconnect");
        Equal<BrowserRecoveryInstruction?>(null, coordinator.Plan(activity),
            "duplicate event while pending must not schedule another reconnect");

        var unrelated = Parse("recovery_required", "retry_exhausted");
        Equal<BrowserRecoveryInstruction?>(null, coordinator.Plan(unrelated),
            "non-disconnect recovery state must not reload the page");
    }

    private static void TestRecoveryReset()
    {
        var coordinator = new BrowserRecoveryCoordinator();
        var required = Parse("recovery_required", "disconnected_waiting");
        NotNull(coordinator.Plan(required), "first reconnect instruction");

        var recovered = Parse("recovered", "generation_resumed");
        Equal<BrowserRecoveryInstruction?>(null, coordinator.Plan(recovered),
            "recovered state does not schedule a reload");

        var next = coordinator.Plan(required);
        NotNull(next, "a later independent interruption can reconnect again");
        Equal(1, next!.Attempt, "recovered state resets attempt count");

        coordinator.Plan(Parse("completed", string.Empty));
        coordinator.MarkAttemptCompleted(required.SessionId);
        var afterCompleted = coordinator.Plan(required);
        NotNull(afterCompleted, "completion resets reconnect state");
        Equal(1, afterCompleted!.Attempt, "completion resets attempt count");
    }

    private static ActivityEvent Parse(string status, string reason, int? attempt = null)
    {
        var reasonJson = string.IsNullOrEmpty(reason) ? string.Empty : $",\"reason\":\"{reason}\"";
        var attemptJson = attempt.HasValue ? $",\"attempt\":{attempt.Value}" : string.Empty;
        var json = $"{{\"status\":\"{status}\",\"activityId\":\"recovery-{status}\",\"url\":\"https://chatgpt.com/c/recovery-thread\",\"title\":\"Recovery Test\"{reasonJson}{attemptJson}}}";
        return BrowserActivityEventParser.Parse(json, ObservedAt);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void Contains(string value, string fragment, string message)
    {
        if (!value.Contains(fragment, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message}: '{fragment}' not found in '{value}'");
        }
    }

    private static void NotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }
}
