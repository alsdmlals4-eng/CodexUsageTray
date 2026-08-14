namespace CodexUsageTray;

internal sealed class MobileNotificationSettingsForm : Form
{
    private readonly MobileNotificationSettingsStore _store;
    private readonly Func<string, string, string, int, CancellationToken, Task> _sender;
    private readonly CheckBox _enabled;
    private readonly TextBox _topic;
    private readonly Label _status;
    private readonly Button _saveButton;
    private readonly Button _testButton;

    public MobileNotificationSettingsForm(
        MobileNotificationSettingsStore store,
        Func<string, string, string, int, CancellationToken, Task> sender)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));

        Text = "휴대폰 알림 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 250);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        var title = new Label
        {
            Text = "ntfy 휴대폰 알림",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(22, 18)
        };
        var description = new Label
        {
            Text = "작업 완료와 승인 필요 상태를 휴대폰으로 보냅니다.",
            AutoSize = true,
            Location = new Point(24, 53)
        };
        _enabled = new CheckBox
        {
            Text = "휴대폰 알림 사용",
            AutoSize = true,
            Location = new Point(24, 84)
        };
        var topicLabel = new Label
        {
            Text = "ntfy topic",
            AutoSize = true,
            Location = new Point(24, 119)
        };
        _topic = new TextBox
        {
            Location = new Point(112, 115),
            Size = new Size(382, 23),
            UseSystemPasswordChar = true
        };
        var hint = new Label
        {
            Text = "영문, 숫자, -, _ 조합(최대 64자). topic은 화면에서 숨겨 표시합니다.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(24, 147)
        };
        _status = new Label
        {
            AutoEllipsis = true,
            Location = new Point(24, 176),
            Size = new Size(470, 22)
        };
        _testButton = new Button
        {
            Text = "테스트 알림",
            Location = new Point(270, 207),
            Size = new Size(108, 30)
        };
        _saveButton = new Button
        {
            Text = "저장",
            Location = new Point(386, 207),
            Size = new Size(108, 30)
        };

        _saveButton.Click += (_, _) => SaveSettings();
        _testButton.Click += async (_, _) => await SendTestFromUiAsync();

        Controls.Add(title);
        Controls.Add(description);
        Controls.Add(_enabled);
        Controls.Add(topicLabel);
        Controls.Add(_topic);
        Controls.Add(hint);
        Controls.Add(_status);
        Controls.Add(_testButton);
        Controls.Add(_saveButton);

        AcceptButton = _saveButton;
        LoadCurrentSettings();
    }

    internal bool MobileEnabled
    {
        get => _enabled.Checked;
        set => _enabled.Checked = value;
    }

    internal string Topic
    {
        get => _topic.Text;
        set => _topic.Text = value ?? string.Empty;
    }

    internal bool TopicIsMasked => _topic.UseSystemPasswordChar;

    internal bool SaveSettings()
    {
        var topic = NormalizeTopic(Topic);
        if (MobileEnabled && !IsValidTopic(topic))
        {
            SetStatus("활성화하려면 올바른 ntfy topic을 입력하세요.", isError: true);
            return false;
        }

        try
        {
            _store.Save(new MobileNotificationSettings(MobileEnabled, topic));
            Topic = topic;
            SetStatus("설정을 저장했습니다.", isError: false);
            return true;
        }
        catch (Exception)
        {
            SetStatus("설정을 저장하지 못했습니다. 진단 환경을 확인하세요.", isError: true);
            return false;
        }
    }

    internal async Task<bool> SendTestAsync(CancellationToken cancellationToken)
    {
        var topic = NormalizeTopic(Topic);
        if (!IsValidTopic(topic))
        {
            SetStatus("테스트하려면 올바른 ntfy topic을 입력하세요.", isError: true);
            return false;
        }

        try
        {
            await _sender(
                    topic,
                    "휴대폰 알림 테스트 · Codex Usage Tray",
                    "ntfy 연결이 정상입니다.",
                    4,
                    cancellationToken)
                .ConfigureAwait(true);
            Topic = topic;
            SetStatus("테스트 알림을 전송했습니다.", isError: false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("테스트 알림 전송이 취소되었습니다.", isError: true);
            return false;
        }
        catch (Exception exception)
        {
            try
            {
                DiagnosticLog.AppendMobilePush(exception);
            }
            catch
            {
            }

            SetStatus("테스트 알림을 보내지 못했습니다. 연결 상태를 확인하세요.", isError: true);
            return false;
        }
    }

    internal static bool IsValidTopic(string? topic)
    {
        var value = NormalizeTopic(topic);
        if (value.Length is < 1 or > 64)
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '-' or '_');
    }

    private static string NormalizeTopic(string? topic) => topic?.Trim() ?? string.Empty;

    private void LoadCurrentSettings()
    {
        var settings = _store.Load();
        MobileEnabled = settings.Enabled;
        Topic = settings.Topic;
    }

    private async Task SendTestFromUiAsync()
    {
        _saveButton.Enabled = false;
        _testButton.Enabled = false;
        try
        {
            await SendTestAsync(CancellationToken.None);
        }
        finally
        {
            if (!IsDisposed)
            {
                _saveButton.Enabled = true;
                _testButton.Enabled = true;
            }
        }
    }

    private void SetStatus(string message, bool isError)
    {
        _status.Text = message;
        _status.ForeColor = isError ? Color.Firebrick : Color.DarkGreen;
    }
}
