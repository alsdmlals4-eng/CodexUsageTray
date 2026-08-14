using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed record MobilePushMessage(string Title, string Message, int Priority);

internal sealed class MobilePushNotifier
{
    private const int DedupeCapacity = 256;

    private readonly Func<MobileNotificationSettings> _settingsProvider;
    private readonly Func<string, string, string, int, CancellationToken, Task> _sender;
    private readonly Action<Exception>? _failureSink;
    private readonly object _gate = new();
    private readonly HashSet<string> _sentKeys = new(StringComparer.Ordinal);
    private readonly Queue<string> _sentOrder = new();

    public MobilePushNotifier(
        Func<MobileNotificationSettings> settingsProvider,
        Func<string, string, string, int, CancellationToken, Task> sender,
        Action<Exception>? failureSink = null)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _failureSink = failureSink;
    }

    public async Task NotifyAsync(
        ActivityEvent activity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var message = CreateMessage(activity);
        if (message is null)
        {
            return;
        }

        MobileNotificationSettings settings;
        try
        {
            settings = _settingsProvider();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return;
        }

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Topic))
        {
            return;
        }

        var dedupeKey = $"{activity.ActivityKey}\u001f{activity.Status}";
        if (!TryMarkSent(dedupeKey))
        {
            return;
        }

        try
        {
            await _sender(
                    settings.Topic.Trim(),
                    message.Title,
                    message.Message,
                    message.Priority,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    internal static MobilePushMessage? CreateMessage(ActivityEvent activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Status is not (ActivityStatus.ApprovalRequired or ActivityStatus.Completed))
        {
            return null;
        }

        var source = activity.SourceKind == ActivitySourceKind.ChatGptWeb ? "ChatGPT" : "Codex";
        var title = activity.Status == ActivityStatus.ApprovalRequired
            ? $"승인 필요 · {source}"
            : $"작업 완료 · {source}";
        var context = activity.SourceKind == ActivitySourceKind.ChatGptWeb
            ? activity.ChatLabel
            : $"{activity.ProjectName} · {activity.ChatLabel}";
        var bodyParts = new[] { context.Trim(), activity.Summary.Trim() }
            .Where(value => value.Length > 0);
        var body = string.Join(Environment.NewLine, bodyParts);
        return new MobilePushMessage(title, body, 4);
    }

    private bool TryMarkSent(string key)
    {
        lock (_gate)
        {
            if (!_sentKeys.Add(key))
            {
                return false;
            }

            _sentOrder.Enqueue(key);
            while (_sentOrder.Count > DedupeCapacity)
            {
                _sentKeys.Remove(_sentOrder.Dequeue());
            }

            return true;
        }
    }

    private void ReportFailure(Exception exception)
    {
        if (_failureSink is null)
        {
            return;
        }

        try
        {
            _failureSink(exception);
        }
        catch
        {
            // Diagnostics must never make mobile notification failures fatal.
        }
    }
}
