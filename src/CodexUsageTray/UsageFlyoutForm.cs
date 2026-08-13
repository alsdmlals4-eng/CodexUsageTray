using CodexUsageTray.Core;

namespace CodexUsageTray;

internal sealed class UsageFlyoutForm : Form
{
    private readonly Label _headline;
    private readonly Label _status;
    private readonly FlowLayoutPanel _rows;

    public UsageFlyoutForm()
    {
        Text = "Codex 사용량";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 210);
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        _headline = new Label
        {
            Text = "Codex 사용 가능량",
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Margin = new Padding(14, 12, 14, 2)
        };
        _status = new Label
        {
            Text = "불러오는 중…",
            ForeColor = Color.FromArgb(90, 98, 108),
            AutoSize = true,
            MaximumSize = new Size(330, 0),
            Margin = new Padding(15, 0, 14, 10)
        };
        _rows = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(12, 0, 12, 12),
            Padding = Padding.Empty
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = Padding.Empty
        };
        layout.Controls.Add(_headline);
        layout.Controls.Add(_status);
        layout.Controls.Add(_rows);
        Controls.Add(layout);
    }

    public void UpdateContent(
        UsageSnapshot? snapshot,
        string? error,
        DateTimeOffset? lastSuccessfulRefresh,
        bool isRefreshing)
    {
        _rows.SuspendLayout();
        try
        {
            foreach (var control in _rows.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }

            _rows.Controls.Clear();
            if (snapshot is not null)
            {
                var trayPercent = UsagePresentation.GetTrayPercent(snapshot);
                _headline.Text = $"Codex {trayPercent}% 남음";
                foreach (var window in snapshot.Windows.OrderBy(item => item.WindowDurationMinutes))
                {
                    _rows.Controls.Add(CreateQuotaCard(window));
                }
            }
            else
            {
                _headline.Text = "Codex 사용량 확인 필요";
            }

            _status.Text = BuildStatusText(error, lastSuccessfulRefresh, isRefreshing);
            _status.ForeColor = error is null
                ? Color.FromArgb(90, 98, 108)
                : Color.FromArgb(190, 55, 55);
        }
        finally
        {
            _rows.ResumeLayout(performLayout: true);
        }

        var contentHeight = 92 + Math.Max(1, _rows.Controls.Count) * 76;
        ClientSize = new Size(360, Math.Clamp(contentHeight, 180, 520));
    }

    public void ToggleNearCursor()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = Math.Clamp(Cursor.Position.X - Width + 24, screen.Left, screen.Right - Width);
        var y = Math.Max(screen.Top, screen.Bottom - Height - 8);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnDeactivate(EventArgs eventArgs)
    {
        base.OnDeactivate(eventArgs);
        Hide();
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

    private static Control CreateQuotaCard(QuotaWindow window)
    {
        var row = UsagePresentation.CreateDisplayRow(window, TimeZoneInfo.Local);
        var panel = new Panel
        {
            Width = 330,
            Height = 68,
            Margin = new Padding(3, 0, 3, 8),
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.White
        };
        var title = new Label
        {
            Text = row.Title,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(12, 8)
        };
        var remaining = new Label
        {
            Text = row.RemainingText,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            AutoSize = true,
            Location = new Point(12, 34)
        };
        var reset = new Label
        {
            Text = row.ResetText,
            ForeColor = Color.FromArgb(90, 98, 108),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(190, 36)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(remaining);
        panel.Controls.Add(reset);
        return panel;
    }

    private static string BuildStatusText(
        string? error,
        DateTimeOffset? lastSuccessfulRefresh,
        bool isRefreshing)
    {
        if (isRefreshing)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                var suffix = lastSuccessfulRefresh is null
                    ? string.Empty
                    : $" · 마지막 성공 {lastSuccessfulRefresh.Value.ToLocalTime():HH:mm}";
                return $"새로고침 중… · 직전 오류: {error}{suffix}";
            }

            return "새로고침 중…";
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            var suffix = lastSuccessfulRefresh is null
                ? string.Empty
                : $" · 마지막 성공 {lastSuccessfulRefresh.Value.ToLocalTime():HH:mm}";
            return error + suffix;
        }

        return lastSuccessfulRefresh is null
            ? "아직 갱신되지 않음"
            : $"마지막 갱신 {lastSuccessfulRefresh.Value.ToLocalTime():MM-dd HH:mm}";
    }
}
