using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed class ActivityPopupQueue : IDisposable
{
    private readonly ActivityPopupForm _popup = new();
    private readonly Action<ActivityEvent> _openActivity;
    private readonly List<ActivityEvent> _pending = new();
    private ActivityEvent? _current;
    private bool _disposed;

    public ActivityPopupQueue(Action<ActivityEvent> openActivity)
    {
        _openActivity = openActivity ?? throw new ArgumentNullException(nameof(openActivity));
        _popup.ActivityClicked += OnActivityClicked;
    }

    public void Enqueue(ActivityEvent activity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (activity.Status is not (
            ActivityStatus.ApprovalRequired or
            ActivityStatus.RecoveryRequired or
            ActivityStatus.Recovered or
            ActivityStatus.Completed))
        {
            return;
        }

        if (_current is not null &&
            string.Equals(_current.ActivityKey, activity.ActivityKey, StringComparison.Ordinal))
        {
            _current = activity;
            _popup.ShowActivity(activity);
            return;
        }

        _pending.RemoveAll(item =>
            string.Equals(item.ActivityKey, activity.ActivityKey, StringComparison.Ordinal));
        _pending.Add(activity);
        ShowNextIfIdle();
    }

    private void OnActivityClicked(ActivityEvent activity)
    {
        if (_current is null ||
            !string.Equals(_current.ActivityKey, activity.ActivityKey, StringComparison.Ordinal))
        {
            return;
        }

        _popup.Hide();
        _current = null;
        _openActivity(activity);
        ShowNextIfIdle();
    }

    private void ShowNextIfIdle()
    {
        if (_current is not null || _pending.Count == 0)
        {
            return;
        }

        _current = _pending[0];
        _pending.RemoveAt(0);
        _popup.ShowActivity(_current);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _popup.ActivityClicked -= OnActivityClicked;
        _popup.Dispose();
    }
}
