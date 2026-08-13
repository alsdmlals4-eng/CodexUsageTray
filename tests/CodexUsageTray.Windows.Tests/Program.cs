using CodexUsageTray;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            UserClosingFlyoutHidesWithoutDisposing();
            PopupQueuePersistsUntilClickAndOpensEveryActivity();
            BrowserActivatorSendsExactSourceIdentity();
            UsageFailureDoesNotAssumeTheNetworkIsBroken();
            DiagnosticLogRedactsCredentialShapedText();
            Console.WriteLine("5 Windows UI regression tests passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void PopupQueuePersistsUntilClickAndOpensEveryActivity()
    {
        var opened = new List<string>();
        using var queue = new ActivityPopupQueue(activity => opened.Add(activity.ActivityKey));
        var first = CreateActivity("session-1", "turn-1", ActivityStatus.Completed);
        var second = CreateActivity("session-2", "turn-2", ActivityStatus.ApprovalRequired);

        queue.Enqueue(first);
        queue.Enqueue(second);
        Application.DoEvents();

        var popup = Application.OpenForms.OfType<ActivityPopupForm>().Single();
        Assert(popup.Visible, "the first completion popup must remain visible before a click");
        var workingArea = Screen.FromControl(popup).WorkingArea;
        Assert(popup.Top <= workingArea.Top + 40,
            "the popup must be prominent at the top of the active screen");
        Assert(Math.Abs((popup.Left + (popup.Width / 2)) -
                        (workingArea.Left + (workingArea.Width / 2))) <= 2,
            "the popup must be horizontally centered on the active screen");
        Assert(popup.TopMost, "the popup must remain above other application windows");
        Assert(opened.Count == 0, "showing a popup must not activate a terminal automatically");

        FindClickSurface(popup).PerformClick();
        Application.DoEvents();

        Assert(opened.SequenceEqual(new[] { first.ActivityKey }),
            "clicking the first popup must open exactly its source activity");
        Assert(popup.Visible, "the queued approval popup must appear after the first is clicked");

        FindClickSurface(popup).PerformClick();
        Application.DoEvents();

        Assert(opened.SequenceEqual(new[] { first.ActivityKey, second.ActivityKey }),
            "each queued popup must open its matching source activity in order");
        Assert(!popup.Visible, "the clicked popup must disappear when no alerts remain");
    }

    private static void BrowserActivatorSendsExactSourceIdentity()
    {
        string? observedPipe = null;
        string? observedPayload = null;
        var activator = new BrowserActivityActivator((pipeName, payload) =>
        {
            observedPipe = pipeName;
            observedPayload = payload;
            return true;
        });
        var activity = new ActivityEvent(
            "web:thread-17",
            "complete-1",
            string.Empty,
            "ChatGPT Web",
            "기존 탭",
            ActivityStatus.Completed,
            "완료",
            string.Empty,
            DateTimeOffset.Now,
            SourceKind: ActivitySourceKind.ChatGptWeb,
            SourceUri: "https://chatgpt.com/c/thread-17",
            BrowserConnectionId: "90d5919d-6e93-4f12-8187-51ff6cc7af4b",
            BrowserTabId: 117,
            BrowserWindowId: 9);

        Assert(activator.TryActivate(activity), "browser command delivery must succeed");
        Assert(
            observedPipe == "CodexUsageTray.BrowserCommands.v1.90d5919d6e934f12818751ff6cc7af4b",
            "the source native connection pipe must be targeted");
        Assert(BrowserActivationCommand.TryParse(observedPayload ?? string.Empty, out var command),
            "the emitted activation payload must be valid");
        Assert(command?.TabId == 117 && command.WindowId == 9,
            "the exact source browser tab and window identity must be preserved");
    }

    private static void UsageFailureDoesNotAssumeTheNetworkIsBroken()
    {
        var failure = TrayApplicationContext.ClassifyFailure(
            new IOException("app-server transport closed"));

        Assert(!failure.Message.Contains("네트워크", StringComparison.Ordinal),
            "an unknown App Server failure must not be mislabeled as a network outage");
        Assert(failure.Message.Contains("진단 로그", StringComparison.Ordinal),
            "an unknown App Server failure must point to the actionable diagnostic log");
    }

    private static void DiagnosticLogRedactsCredentialShapedText()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-Log-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "diagnostics.log");
        try
        {
            DiagnosticLog.Write(
                path,
                new InvalidOperationException("Authorization: Bearer exception-secret"),
                "{\"accessToken\":\"stderr-secret\"}");
            var text = File.ReadAllText(path);

            Assert(!text.Contains("exception-secret", StringComparison.Ordinal) &&
                   !text.Contains("stderr-secret", StringComparison.Ordinal),
                "the persisted diagnostic log must not contain credential-shaped values");
            Assert(text.Contains("[REDACTED]", StringComparison.Ordinal),
                "the persisted log must identify that sensitive text was redacted");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ActivityEvent CreateActivity(
        string sessionId,
        string turnId,
        ActivityStatus status) =>
        new(
            sessionId,
            turnId,
            @"C:\work\Project",
            "Project",
            sessionId,
            status,
            status == ActivityStatus.Completed ? "작업 완료 테스트" : "승인 요청 테스트",
            string.Empty,
            DateTimeOffset.Now);

    private static Button FindClickSurface(Control parent) =>
        parent.Controls.OfType<Button>().FirstOrDefault() ??
        parent.Controls.Cast<Control>().Select(FindClickSurface).First(button => button is not null);

    private static void UserClosingFlyoutHidesWithoutDisposing()
    {
        using var form = new UsageFlyoutForm();
        form.Show();
        Application.DoEvents();

        form.Close();
        Application.DoEvents();

        Assert(!form.IsDisposed, "user closing must not dispose the reusable flyout");
        Assert(!form.Visible, "user closing must hide the reusable flyout");

        form.UpdateContent(
            snapshot: null,
            error: null,
            lastSuccessfulRefresh: null,
            isRefreshing: false);
        Assert(!form.IsDisposed, "the hidden flyout must still accept refresh updates");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
