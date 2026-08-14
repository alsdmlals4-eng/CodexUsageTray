namespace CodexUsageTray.Core;

public sealed record BrowserRecoveryInstruction(
    string SessionId,
    int Attempt,
    TimeSpan Delay);

public sealed class BrowserRecoveryCoordinator
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, RecoveryState> _states = new(StringComparer.Ordinal);

    public BrowserRecoveryInstruction? Plan(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.SourceKind != ActivitySourceKind.ChatGptWeb)
        {
            return null;
        }

        lock (_gate)
        {
            if (activity.Status is ActivityStatus.Recovered or ActivityStatus.Completed)
            {
                _states.Remove(activity.SessionId);
                return null;
            }

            if (activity.Status != ActivityStatus.RecoveryRequired ||
                !string.Equals(activity.Detail, "disconnected_waiting", StringComparison.Ordinal))
            {
                return null;
            }

            if (!_states.TryGetValue(activity.SessionId, out var state))
            {
                state = new RecoveryState();
                _states[activity.SessionId] = state;
            }

            if (state.Pending || state.Attempts >= Delays.Length)
            {
                return null;
            }

            state.Pending = true;
            state.Attempts += 1;
            return new BrowserRecoveryInstruction(
                activity.SessionId,
                state.Attempts,
                Delays[state.Attempts - 1]);
        }
    }

    public void MarkAttemptCompleted(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate)
        {
            if (_states.TryGetValue(sessionId, out var state))
            {
                state.Pending = false;
            }
        }
    }

    private sealed class RecoveryState
    {
        public int Attempts { get; set; }
        public bool Pending { get; set; }
    }
}
