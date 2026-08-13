using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal sealed class ActivityPopupForm : Form
{
    private const int NoActivateExtendedStyle = 0x08000000;

    private readonly Label _status;
    private readonly Label _source;
    private readonly Label _summary;
    private readonly Button _openButton;
    private ActivityEvent? _activity;

    public ActivityPopupForm()
    {
        Text = "Codex 작업 알림";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 174);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        _status = new Label
        {
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(22, 17)
        };
        _source = new Label
        {
            ForeColor = Color.FromArgb(90, 98, 108),
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(24, 61),
            Size = new Size(590, 22)
        };
        _summary = new Label
        {
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(24, 92),
            Size = new Size(456, 58)
        };
        _openButton = new Button
        {
            Text = "확인하기",
            FlatStyle = FlatStyle.System,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(499, 103),
            Size = new Size(115, 42),
            TabStop = false
        };
        _openButton.Click += (_, _) => OpenCurrentActivity();

        Controls.Add(_status);
        Controls.Add(_source);
        Controls.Add(_summary);
        Controls.Add(_openButton);
        foreach (var control in new Control[] { this, _status, _source, _summary })
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => OpenCurrentActivity();
        }
    }

    public event Action<ActivityEvent>? ActivityClicked;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NoActivateExtendedStyle;
            return parameters;
        }
    }

    public void ShowActivity(ActivityEvent activity)
    {
        _activity = activity;
        var isApproval = activity.Status == ActivityStatus.ApprovalRequired;
        _status.Text = isApproval ? "승인 요청" : "작업 완료";
        _status.ForeColor = isApproval
            ? Color.FromArgb(202, 138, 4)
            : Color.FromArgb(39, 174, 96);
        _source.Text = BuildSource(activity);
        _summary.Text = activity.Summary;

        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            workingArea.Left + ((workingArea.Width - Width) / 2),
            workingArea.Top + 18);
        if (!Visible)
        {
            Show();
        }

        BringToFront();
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    private void OpenCurrentActivity()
    {
        if (_activity is not null)
        {
            ActivityClicked?.Invoke(_activity);
        }
    }

    private static string BuildSource(ActivityEvent activity)
    {
        var terminal = string.IsNullOrWhiteSpace(activity.TerminalTitle)
            ? string.Empty
            : $" · {activity.TerminalTitle}";
        return $"{activity.ProjectName} · 채팅 {activity.ChatLabel}{terminal}";
    }
}
