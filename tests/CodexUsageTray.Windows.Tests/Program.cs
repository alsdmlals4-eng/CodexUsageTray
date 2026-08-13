using CodexUsageTray;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            UserClosingFlyoutHidesWithoutDisposing();
            PopupQueuePersistsUntilClickAndOpensEveryActivity();
            Console.WriteLine("2 Windows UI regression tests passed");
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
