using CodexUsageTray.Core;

namespace CodexUsageTray;

internal sealed class ActivityHistoryForm : Form
{
    private readonly Label _counts;
    private readonly FlowLayoutPanel _items;

    public ActivityHistoryForm()
    {
        Text = "Codex 작업 알림";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 360);
        Size = new Size(620, 520);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        var title = new Label
        {
            Text = "Codex 작업 알림",
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(18, 16)
        };
        _counts = new Label
        {
            Text = "알림 없음",
            ForeColor = Color.FromArgb(90, 98, 108),
            AutoSize = true,
            Location = new Point(20, 50)
        };
        _items = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14, 12, 14, 14)
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 78 };
        header.Controls.Add(title);
        header.Controls.Add(_counts);
        Controls.Add(_items);
        Controls.Add(header);
    }

    public void UpdateActivities(IReadOnlyList<ActivityEvent> activities)
    {
        foreach (var control in _items.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }

        _items.Controls.Clear();
        var running = activities.Count(item => item.Status == ActivityStatus.Running);
        var approvals = activities.Count(item => item.Status == ActivityStatus.ApprovalRequired);
        var completed = activities.Count(item => item.Status == ActivityStatus.Completed);
        _counts.Text = $"진행 {running} · 승인 대기 {approvals} · 완료 {completed}";

        if (activities.Count == 0)
        {
            _items.Controls.Add(new Label
            {
                Text = "아직 받은 작업 알림이 없습니다.",
                ForeColor = Color.FromArgb(90, 98, 108),
                AutoSize = true,
                Margin = new Padding(8, 14, 8, 8)
            });
            return;
        }

        foreach (var activity in activities)
        {
            _items.Controls.Add(CreateCard(activity));
        }
    }

    public void ShowAndActivate()
    {
        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        Activate();
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

    private static Control CreateCard(ActivityEvent activity)
    {
        var card = new Panel
        {
            Width = 560,
            Height = 112,
            BackColor = Color.White,
            Margin = new Padding(4, 0, 4, 10),
            Cursor = Cursors.Hand
        };
        var status = new Label
        {
            Text = GetStatusText(activity.Status),
            ForeColor = GetStatusColor(activity.Status),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(12, 10)
        };
        var source = new Label
        {
            Text = BuildSource(activity),
            ForeColor = Color.FromArgb(90, 98, 108),
            AutoEllipsis = true,
            Location = new Point(12, 33),
            Size = new Size(530, 20)
        };
        var summary = new Label
        {
            Text = activity.Summary,
            AutoEllipsis = true,
            Location = new Point(12, 58),
            Size = new Size(530, 42)
        };
        card.Controls.Add(status);
        card.Controls.Add(source);
        card.Controls.Add(summary);
        foreach (Control control in card.Controls)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => ActivateOrKeepHistory(activity);
        }
        card.Click += (_, _) => ActivateOrKeepHistory(activity);
        return card;
    }

    private static void ActivateOrKeepHistory(ActivityEvent activity)
    {
        _ = ActivitySourceLauncher.TryOpen(activity);
    }

    private static string BuildSource(ActivityEvent activity)
    {
        if (activity.SourceKind == ActivitySourceKind.ChatGptWeb)
        {
            return $"ChatGPT Web · {activity.ChatLabel} · {activity.OccurredAt.ToLocalTime():HH:mm}";
        }

        var terminal = string.IsNullOrWhiteSpace(activity.TerminalTitle)
            ? string.Empty
            : $" · {activity.TerminalTitle}";
        return $"{activity.ProjectName} · 채팅 {activity.ChatLabel}{terminal} · {activity.OccurredAt.ToLocalTime():HH:mm}";
    }

    private static string GetStatusText(ActivityStatus status) => status switch
    {
        ActivityStatus.Running => "진행 중",
        ActivityStatus.ApprovalRequired => "승인 필요",
        ActivityStatus.Completed => "작업 완료",
        _ => "알림"
    };

    private static Color GetStatusColor(ActivityStatus status) => status switch
    {
        ActivityStatus.Running => Color.FromArgb(47, 128, 237),
        ActivityStatus.ApprovalRequired => Color.FromArgb(202, 138, 4),
        ActivityStatus.Completed => Color.FromArgb(39, 174, 96),
        _ => Color.FromArgb(90, 98, 108)
    };
}
