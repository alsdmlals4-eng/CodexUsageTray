namespace CodexUsageTray.Core;

public enum ActivityStatus
{
    Running,
    ApprovalRequired,
    Retrying,
    RecoveryRequired,
    Recovered,
    Completed
}

public enum ActivitySourceKind
{
    CodexTerminal,
    ChatGptWeb
}

public sealed record ActivityEvent(
    string SessionId,
    string? TurnId,
    string WorkingDirectory,
    string ProjectName,
    string ChatLabel,
    ActivityStatus Status,
    string Summary,
    string Detail,
    DateTimeOffset OccurredAt,
    int SourceProcessId = 0,
    long SourceWindowHandle = 0,
    string? TerminalTitle = null,
    ActivitySourceKind SourceKind = ActivitySourceKind.CodexTerminal,
    string? SourceUri = null,
    string? BrowserConnectionId = null,
    int BrowserTabId = 0,
    int BrowserWindowId = 0)
{
    public string ActivityKey => $"{SessionId}\u001f{TurnId ?? string.Empty}";

    public ActivityEvent WithTerminal(int processId, long windowHandle, string? title) =>
        this with
        {
            SourceProcessId = processId,
            SourceWindowHandle = windowHandle,
            TerminalTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim()
        };

    public ActivityEvent WithBrowserConnection(string connectionId)
    {
        if (!Guid.TryParse(connectionId, out var parsed))
        {
            throw new ArgumentException("Browser connection ID must be a GUID.", nameof(connectionId));
        }

        return this with { BrowserConnectionId = parsed.ToString("D") };
    }
}

public sealed class ActivityStore
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly List<ActivityEvent> _items = new();

    public ActivityStore(int capacity = 50)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public void AddOrUpdate(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (_gate)
        {
            _items.RemoveAll(item => string.Equals(item.ActivityKey, activity.ActivityKey, StringComparison.Ordinal));
            _items.Insert(0, activity);
            if (_items.Count > _capacity)
            {
                _items.RemoveRange(_capacity, _items.Count - _capacity);
            }
        }
    }

    public IReadOnlyList<ActivityEvent> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }
}

public static class ActivityPipeNames
{
    public const string PipeName = "CodexUsageTray.Activity.v1";

    public static string GetBrowserCommandPipeName(string connectionId)
    {
        if (!Guid.TryParse(connectionId, out var parsed))
        {
            throw new ArgumentException("Browser connection ID must be a GUID.", nameof(connectionId));
        }

        return $"CodexUsageTray.BrowserCommands.v1.{parsed:N}";
    }
}
