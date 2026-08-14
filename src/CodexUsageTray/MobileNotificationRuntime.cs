using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed class MobileNotificationRuntime : IDisposable
{
    private static readonly Lazy<MobileNotificationRuntime> SharedRuntime = new(() => new MobileNotificationRuntime());

    private readonly MobileNotificationSettingsStore _store;
    private readonly NtfyPushClient? _ownedClient;
    private readonly Func<string, string, string, int, CancellationToken, Task> _sender;
    private readonly MobilePushNotifier _notifier;
    private bool _disposed;

    public MobileNotificationRuntime()
    {
        _store = new MobileNotificationSettingsStore();
        _ownedClient = new NtfyPushClient();
        _sender = _ownedClient.SendAsync;
        _notifier = new MobilePushNotifier(
            _store.Load,
            _sender,
            DiagnosticLog.AppendMobilePush);
    }

    internal MobileNotificationRuntime(
        MobileNotificationSettingsStore store,
        Func<string, string, string, int, CancellationToken, Task> sender)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _notifier = new MobilePushNotifier(
            _store.Load,
            _sender,
            DiagnosticLog.AppendMobilePush);
    }

    internal static MobileNotificationRuntime Shared => SharedRuntime.Value;

    internal Task NotifyAsync(
        ActivityEvent activity,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _notifier.NotifyAsync(activity, cancellationToken);
    }

    internal MobileNotificationSettingsForm CreateSettingsForm()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MobileNotificationSettingsForm(_store, _sender);
    }

    internal void ShowSettingsDialog()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var form = CreateSettingsForm();
        form.ShowDialog();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedClient?.Dispose();
    }
}
