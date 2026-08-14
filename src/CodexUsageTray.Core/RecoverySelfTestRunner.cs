namespace CodexUsageTray.Core;

public sealed record RecoverySelfTestResult(
    bool Passed,
    IReadOnlyList<TimeSpan> VerifiedDelays,
    bool DuplicateSuppressed,
    bool CeilingEnforced,
    bool ResetVerified,
    string Failure);

public sealed class RecoverySelfTestRunner
{
    private static readonly TimeSpan[] ExpectedDelays =
    {
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    public RecoverySelfTestResult Run(
        Func<BrowserRecoveryInstruction, bool>? executeAttempt = null)
    {
        executeAttempt ??= _ => true;
        var coordinator = new BrowserRecoveryCoordinator();
        var sessionId = $"recovery-self-test-{Guid.NewGuid():N}";
        var required = CreateActivity(
            sessionId,
            ActivityStatus.RecoveryRequired,
            "disconnected_waiting");
        var verifiedDelays = new List<TimeSpan>(ExpectedDelays.Length);
        var duplicateSuppressed = false;
        var ceilingEnforced = false;
        var resetVerified = false;

        RecoverySelfTestResult Fail(string failure) =>
            new(
                Passed: false,
                VerifiedDelays: verifiedDelays.ToArray(),
                DuplicateSuppressed: duplicateSuppressed,
                CeilingEnforced: ceilingEnforced,
                ResetVerified: resetVerified,
                Failure: failure);

        try
        {
            for (var index = 0; index < ExpectedDelays.Length; index++)
            {
                var instruction = coordinator.Plan(required);
                if (instruction is null)
                {
                    return Fail($"Attempt {index + 1} was not scheduled.");
                }

                var expectedAttempt = index + 1;
                var expectedDelay = ExpectedDelays[index];
                if (instruction.Attempt != expectedAttempt ||
                    instruction.Delay != expectedDelay)
                {
                    return Fail(
                        $"Attempt {expectedAttempt} policy mismatch: " +
                        $"actual attempt={instruction.Attempt}, delay={instruction.Delay}.");
                }

                verifiedDelays.Add(instruction.Delay);

                if (index == 0)
                {
                    duplicateSuppressed = coordinator.Plan(required) is null;
                    if (!duplicateSuppressed)
                    {
                        return Fail("A duplicate signal scheduled another pending attempt.");
                    }
                }

                if (!executeAttempt(instruction))
                {
                    return Fail($"Attempt executor rejected attempt {instruction.Attempt}.");
                }

                coordinator.MarkAttemptCompleted(sessionId);
            }

            ceilingEnforced = coordinator.Plan(required) is null;
            if (!ceilingEnforced)
            {
                return Fail("A fourth recovery attempt was scheduled past the ceiling.");
            }

            var recovered = CreateActivity(
                sessionId,
                ActivityStatus.Recovered,
                "recovery_self_test");
            if (coordinator.Plan(recovered) is not null)
            {
                return Fail("Recovered state unexpectedly scheduled a recovery attempt.");
            }

            var afterReset = coordinator.Plan(required);
            resetVerified = afterReset is not null &&
                afterReset.Attempt == 1 &&
                afterReset.Delay == ExpectedDelays[0];
            if (!resetVerified)
            {
                return Fail("Recovered state did not reset the recovery attempt sequence.");
            }

            coordinator.MarkAttemptCompleted(sessionId);
            return new RecoverySelfTestResult(
                Passed: true,
                VerifiedDelays: verifiedDelays.ToArray(),
                DuplicateSuppressed: true,
                CeilingEnforced: true,
                ResetVerified: true,
                Failure: string.Empty);
        }
        catch (Exception exception)
        {
            return Fail($"Recovery self-test failed safely: {exception.Message}");
        }
    }

    private static ActivityEvent CreateActivity(
        string sessionId,
        ActivityStatus status,
        string detail) =>
        new(
            SessionId: sessionId,
            TurnId: "recovery-self-test",
            WorkingDirectory: string.Empty,
            ProjectName: "Codex Usage Tray",
            ChatLabel: "복구 기능 테스트",
            Status: status,
            Summary: "Recovery self-test",
            Detail: detail,
            OccurredAt: DateTimeOffset.UtcNow,
            SourceKind: ActivitySourceKind.ChatGptWeb);
}
