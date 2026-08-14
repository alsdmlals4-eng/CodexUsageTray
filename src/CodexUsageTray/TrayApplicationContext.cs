using System.Diagnostics;
using System.Text.Json;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly CodexAppServerClient _client = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly UsageFlyoutForm _flyout = new();
    private readonly ActivityHistoryForm _activityForm = new();
    private readonly ActivityStore _activityStore = new();
    private readonly BrowserRecoveryCoordinator _browserRecovery = new();
    private readonly BrowserActivityActivator _browserActivator = new();
    private readonly ActivityPopupQueue _popupQueue;
    private readonly ActivityPipeServer _activityPipe;
    private readonly Control _dispatcher = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _initialTimer;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _activityItem;
    private UsageSnapshot? _lastSnapshot;
    private DateTimeOffset? _lastSuccessfulRefresh;
    private string? _lastError;
    private Task _activeRefreshTask = Task.CompletedTask;
    private bool _isRefreshing;
    private bool _exiting;
    private bool _shutdownComplete;

    public TrayApplicationContext()
    {
        _dispatcher.CreateControl();
        _popupQueue = new ActivityPopupQueue(OpenActivity);
        _activityPipe = new ActivityPipeServer();
        _activityPipe.ActivityReceived += OnActivityReceived;
        _activityPipe.Start();

        var menu = new ContextMenuStrip();
        menu.Items.Add("사용량 상세", null, (_, _) => _flyout.ToggleNearCursor());
        _activityItem = new ToolStripMenuItem("작업 알림 기록 (0)");
        _activityItem.Click += (_, _) => ShowActivityHistory();
        menu.Items.Add(_activityItem);
        menu.Items.Add("새로고침", null, (_, _) => RequestRefresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Codex 로그인", null, (_, _) => OpenCodexLogin());
        menu.Items.Add("ChatGPT 알림 설정 안내", null, (_, _) => OpenChatGptNotificationGuide());
        menu.Items.Add("웹 ChatGPT 확장 폴더 열기", null, (_, _) => OpenBrowserExtensionFolder());
        menu.Items.Add("진단 로그 폴더 열기", null, (_, _) => OpenDiagnosticLogDirectory());
        _startupItem = new ToolStripMenuItem("Windows 시작 시 실행")
        {
            Checked = SafeReadStartupState(),
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, async (_, _) => await ShutdownAsync());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.CreateStatusIcon("…"),
            Text = "Codex 사용량 불러오는 중",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                _flyout.ToggleNearCursor();
            }
        };
        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = checked((int)RefreshInterval.TotalMilliseconds)
        };
        _refreshTimer.Tick += (_, _) => RequestRefresh();
        _refreshTimer.Start();

        _initialTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _initialTimer.Tick += (_, _) =>
        {
            _initialTimer.Stop();
            RequestRefresh();
        };
        _initialTimer.Start();
        UpdateFlyout();
        UpdateActivities();
    }

    private void RequestRefresh()
    {
        if (_exiting || !_activeRefreshTask.IsCompleted)
        {
            return;
        }

        _activeRefreshTask = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;
        UpdateFlyout();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var snapshot = await _client.ReadUsageAsync(timeout.Token);
            _lastSnapshot = snapshot;
            _lastSuccessfulRefresh = DateTimeOffset.Now;
            _lastError = null;
            UpdateTrayIcon(snapshot);
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            var exception = new TimeoutException("Codex App Server 새로고침 시간이 초과되었습니다.");
            RecordDiagnostic(exception);
            SetError("새로고침 시간이 초과되었습니다. 진단 로그를 확인하세요.", UsageFailureKind.Transient);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordDiagnostic(exception);
            var failure = ClassifyFailure(exception);
            SetError(failure.Message, failure.Kind);
        }
        finally
        {
            _isRefreshing = false;
            if (!_shutdown.IsCancellationRequested)
            {
                UpdateFlyout();
            }
        }
    }

    private void OnActivityReceived(ActivityEvent activity)
    {
        if (_exiting || _dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(new Action(() => HandleActivity(activity)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void HandleActivity(ActivityEvent activity)
    {
        if (_exiting)
        {
            return;
        }

        _activityStore.AddOrUpdate(activity);
        UpdateActivities();
        if (activity.Status is ActivityStatus.ApprovalRequired or
            ActivityStatus.RecoveryRequired or
            ActivityStatus.Recovered or
            ActivityStatus.Completed)
        {
            _popupQueue.Enqueue(activity);
        }

        var instruction = _browserRecovery.Plan(activity);
        if (instruction is not null)
        {
            _ = RunBrowserRecoveryAsync(activity, instruction);
        }
    }

    private async Task RunBrowserRecoveryAsync(
        ActivityEvent activity,
        BrowserRecoveryInstruction instruction)
    {
        try
        {
            await Task.Delay(instruction.Delay, _shutdown.Token);
            if (_shutdown.IsCancellationRequested || _exiting)
            {
                return;
            }

            var sent = _browserActivator.TryReload(activity);
            _browserRecovery.MarkAttemptCompleted(instruction.SessionId);
            if (!sent)
            {
                var next = _browserRecovery.Plan(activity);
                if (next is not null)
                {
                    await RunBrowserRecoveryAsync(activity, next);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private void OpenActivity(ActivityEvent activity)
    {
        if (!ActivitySourceLauncher.TryOpen(activity))
        {
            ShowActivityHistory();
        }
    }

    private void UpdateActivities()
    {
        var activities = _activityStore.Snapshot();
        var approvals = activities.Count(item => item.Status == ActivityStatus.ApprovalRequired);
        var recoveryRequired = activities.Count(item => item.Status == ActivityStatus.RecoveryRequired);
        var running = activities.Count(item => item.Status is ActivityStatus.Running or ActivityStatus.Retrying);
        _activityItem.Text = approvals > 0 || recoveryRequired > 0
            ? $"작업 알림 기록 (승인 {approvals} · 복구 {recoveryRequired} · 진행 {running})"
            : $"작업 알림 기록 ({activities.Count})";
        _activityForm.UpdateActivities(activities);
    }

    private void ShowActivityHistory()
    {
        UpdateActivities();
        _activityForm.ShowAndActivate();
    }

    private void UpdateTrayIcon(UsageSnapshot snapshot)
    {
        var remaining = UsagePresentation.GetTrayPercent(snapshot);
        ReplaceIcon(TrayIconRenderer.CreateNumericIcon(remaining, UsagePresentation.GetSeverity(remaining)));
        _notifyIcon.Text = UsagePresentation.BuildTooltip(snapshot);
    }

    private void SetError(string message, UsageFailureKind failureKind)
    {
        _lastError = message;
        var mustReplaceStaleIcon = failureKind is
            UsageFailureKind.Authentication or
            UsageFailureKind.CliMissing or
            UsageFailureKind.ResponseFormat;
        if (_lastSnapshot is null || mustReplaceStaleIcon)
        {
            var symbol = failureKind == UsageFailureKind.CliMissing ? "?" : "!";
            ReplaceIcon(TrayIconRenderer.CreateStatusIcon(symbol));
            _notifyIcon.Text = TruncateTooltip($"Codex: {message}");
        }
    }

    private void ReplaceIcon(Icon icon)
    {
        var previous = _notifyIcon.Icon;
        _notifyIcon.Icon = icon;
        previous?.Dispose();
    }

    private void UpdateFlyout() =>
        _flyout.UpdateContent(_lastSnapshot, _lastError, _lastSuccessfulRefresh, _isRefreshing);

    private void ToggleStartup()
    {
        try
        {
            var next = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(next);
            _startupItem.Checked = next;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "시작 프로그램 설정 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool SafeReadStartupState()
    {
        try
        {
            return StartupRegistration.IsEnabled();
        }
        catch
        {
            return false;
        }
    }

    private static void OpenCodexLogin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -Command codex login",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Codex 로그인을 열 수 없습니다. Codex CLI 설치를 확인하세요.\n\n{exception.Message}",
                "Codex 로그인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void OpenChatGptNotificationGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://learn.chatgpt.com/docs/notifications",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "알림 설정 안내", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenBrowserExtensionFolder()
    {
        try
        {
            var extensionPath = Path.Combine(AppContext.BaseDirectory, "browser-extension");
            if (!Directory.Exists(extensionPath))
            {
                throw new DirectoryNotFoundException("웹 ChatGPT 확장 폴더가 설치되어 있지 않습니다.");
            }

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{extensionPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "웹 ChatGPT 확장", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenDiagnosticLogDirectory()
    {
        try
        {
            DiagnosticLog.OpenDirectory();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "진단 로그", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RecordDiagnostic(Exception exception)
    {
        try
        {
            DiagnosticLog.Append(exception, _client.GetDiagnosticSummary());
        }
        catch
        {
            // Diagnostic logging must never turn a refresh failure into an app failure.
        }
    }

    internal static UsageFailure ClassifyFailure(Exception exception)
    {
        if (exception is CodexCliNotFoundException)
        {
            return new UsageFailure(
                UsageFailureKind.CliMissing,
                "Codex CLI가 설치되어 있지 않거나 PATH에서 찾을 수 없습니다.");
        }

        if (exception is CodexAuthenticationException ||
            exception is JsonRpcException rpc &&
            (rpc.Code == 401 ||
             rpc.Message.Contains("logged in", StringComparison.OrdinalIgnoreCase) ||
             rpc.Message.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase) ||
             rpc.Message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
             rpc.Message.Contains("auth", StringComparison.OrdinalIgnoreCase)))
        {
            return new UsageFailure(
                UsageFailureKind.Authentication,
                "Codex 로그인이 필요합니다. 우클릭 메뉴에서 로그인하세요.");
        }

        if (exception is InvalidDataException or JsonException)
        {
            return new UsageFailure(
                UsageFailureKind.ResponseFormat,
                "Codex 사용량 응답을 해석할 수 없습니다. 앱 업데이트를 확인하세요.");
        }

        return new UsageFailure(
            UsageFailureKind.Transient,
            "Codex App Server 연결에 실패했습니다. 트레이 메뉴에서 진단 로그를 확인하세요.");
    }

    private async Task ShutdownAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _initialTimer.Stop();
        _refreshTimer.Stop();
        _shutdown.Cancel();
        try
        {
            await _activeRefreshTask;
        }
        finally
        {
            await DisposeBackendsAsync();
            _shutdownComplete = true;
            ExitThread();
        }
    }

    private async Task DisposeBackendsAsync()
    {
        try
        {
            await _activityPipe.DisposeAsync();
        }
        catch
        {
        }

        try
        {
            await _client.DisposeAsync();
        }
        catch
        {
        }
    }

    private static string TruncateTooltip(string text) => text.Length <= 63 ? text : text[..63];

    protected override void ExitThreadCore()
    {
        if (!_shutdownComplete)
        {
            _shutdown.Cancel();
            Task.Run(DisposeBackendsAsync).GetAwaiter().GetResult();
            _shutdownComplete = true;
        }

        _activityPipe.ActivityReceived -= OnActivityReceived;
        _initialTimer.Dispose();
        _refreshTimer.Dispose();
        _popupQueue.Dispose();
        _flyout.Dispose();
        _activityForm.Dispose();
        _dispatcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }

    internal sealed record UsageFailure(UsageFailureKind Kind, string Message);

    internal enum UsageFailureKind
    {
        Transient,
        Authentication,
        CliMissing,
        ResponseFormat
    }
}
