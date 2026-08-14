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

    internal static void NotifyShared(ActivityEvent activity, CancellationToken cancellationToken)
    {
        try
        {
            _ = SharedRuntime.Value.NotifyAsync(activity, cancellationToken);
        }
        catch (Exception exception)
        {
            SafeRecordFailure(exception);
        }
    }

    internal static void ShowSharedSettingsDialog()
    {
        try
        {
            SharedRuntime.Value.ShowSettingsDialog();
        }
        catch (Exception exception)
        {
            SafeRecordFailure(exception);
            MessageBox.Show(
                "휴대폰 알림 설정을 열 수 없습니다. 진단 로그를 확인하세요.",
                "휴대폰 알림 설정",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

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

    private static void SafeRecordFailure(Exception exception)
    {
        try
        {
            DiagnosticLog.AppendMobilePush(exception);
        }
        catch
        {
        }
    }
}
