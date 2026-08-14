using System.Text.Json;
using CodexUsageTray;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray.Windows.Tests;

internal static class MobileNotificationRegressionTests
{
    public static void Run()
    {
        MobileSettingsRoundTripOutsideInstallDirectory();
        MalformedMobileSettingsDisableNotifications();
        MobilePushPolicyFormatsSupportedActivities();
        MobilePushNotifierDeduplicatesAndAllowsStatusTransition();
        MobilePushNotifierIsolatesDeliveryFailure();
        NtfyClientKeepsTopicOutOfRequestUrl();
        MobileDiagnosticLogNeverPersistsTopic();
    }

    private static void MobileSettingsRoundTripOutsideInstallDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-Mobile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mobile-notifications.json");
        try
        {
            var store = new MobileNotificationSettingsStore(path);
            store.Save(new MobileNotificationSettings(true, "  mobile-test-topic  "));
            var loaded = store.Load();

            Assert(loaded.Enabled, "saved mobile notifications must remain enabled");
            Assert(loaded.Topic == "mobile-test-topic", "the persisted topic must be trimmed");
            Assert(MobileNotificationSettingsStore.DefaultPath.Contains(
                    "CodexUsageTrayData",
                    StringComparison.Ordinal),
                "mobile settings must live outside the replace-on-update install directory");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void MalformedMobileSettingsDisableNotifications()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-Mobile-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mobile-notifications.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "{not-json");
            var loaded = new MobileNotificationSettingsStore(path).Load();

            Assert(!loaded.Enabled && loaded.Topic.Length == 0,
                "malformed settings must fail closed without crashing the tray");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void MobilePushPolicyFormatsSupportedActivities()
    {
        foreach (var status in new[]
                 {
                     ActivityStatus.Running,
                     ActivityStatus.Retrying,
                     ActivityStatus.RecoveryRequired,
                     ActivityStatus.Recovered
                 })
        {
            Assert(MobilePushNotifier.CreateMessage(CreateActivity("ignored", "turn", status)) is null,
                $"{status} must not produce a mobile notification in the completion/approval feature");
        }

        var approval = MobilePushNotifier.CreateMessage(
            CreateActivity("approval", "turn", ActivityStatus.ApprovalRequired));
        Assert(approval is not null && approval.Title == "승인 필요 · Codex",
            "Codex approval must have a clear mobile title");
        Assert(approval!.Message.Contains("Project", StringComparison.Ordinal) &&
               approval.Message.Contains("승인 요청 테스트", StringComparison.Ordinal),
            "Codex mobile body must identify the project and summary");

        var webActivity = new ActivityEvent(
            "web:thread-mobile",
            "complete-1",
            string.Empty,
            "ChatGPT Web",
            "모바일 알림 채팅",
            ActivityStatus.Completed,
            "웹 작업 완료",
            "대화 본문은 보내지 않음",
            DateTimeOffset.Now,
            SourceKind: ActivitySourceKind.ChatGptWeb,
            SourceUri: "https://chatgpt.com/c/thread-mobile");
        var web = MobilePushNotifier.CreateMessage(webActivity);
        Assert(web is not null && web.Title == "작업 완료 · ChatGPT",
            "ChatGPT completion must have a clear mobile title");
        Assert(web!.Message.Contains("모바일 알림 채팅", StringComparison.Ordinal) &&
               web.Message.Contains("웹 작업 완료", StringComparison.Ordinal) &&
               !web.Message.Contains("대화 본문은 보내지 않음", StringComparison.Ordinal),
            "mobile payload must use safe metadata and exclude activity detail");
    }

    private static void MobilePushNotifierDeduplicatesAndAllowsStatusTransition()
    {
        var sentTitles = new List<string>();
        var notifier = new MobilePushNotifier(
            () => new MobileNotificationSettings(true, "test-topic"),
            (_, title, _, _, _) =>
            {
                sentTitles.Add(title);
                return Task.CompletedTask;
            });
        var approval = CreateActivity("dedupe", "turn", ActivityStatus.ApprovalRequired);
        var completed = approval with
        {
            Status = ActivityStatus.Completed,
            Summary = "작업 완료 테스트"
        };

        notifier.NotifyAsync(approval).GetAwaiter().GetResult();
        notifier.NotifyAsync(approval).GetAwaiter().GetResult();
        notifier.NotifyAsync(completed).GetAwaiter().GetResult();

        Assert(sentTitles.Count == 2,
            "the same activity/status must be sent once while a later status transition is allowed");
        Assert(sentTitles[0].StartsWith("승인 필요", StringComparison.Ordinal) &&
               sentTitles[1].StartsWith("작업 완료", StringComparison.Ordinal),
            "approval and completion must preserve their order");
    }

    private static void MobilePushNotifierIsolatesDeliveryFailure()
    {
        var failures = new List<Exception>();
        var notifier = new MobilePushNotifier(
            () => new MobileNotificationSettings(true, "test-topic"),
            (_, _, _, _, _) => throw new HttpRequestException("simulated ntfy outage"),
            failures.Add);

        notifier.NotifyAsync(CreateActivity("failure", "turn", ActivityStatus.Completed))
            .GetAwaiter()
            .GetResult();

        Assert(failures.Count == 1 && failures[0] is HttpRequestException,
            "mobile delivery failures must be reported without escaping the notifier");
    }

    private static void NtfyClientKeepsTopicOutOfRequestUrl()
    {
        const string topic = "mobile-private-test-topic";
        var handler = new RecordingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new NtfyPushClient(httpClient);

        client.SendAsync(
                topic,
                "작업 완료 · ChatGPT",
                "모바일 테스트",
                4,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(handler.RequestUri is not null &&
               !handler.RequestUri.AbsoluteUri.Contains(topic, StringComparison.Ordinal),
            "the ntfy topic must never appear in the request URL");
        using var document = JsonDocument.Parse(handler.Body ?? string.Empty);
        Assert(document.RootElement.GetProperty("topic").GetString() == topic,
            "the ntfy JSON payload must carry the topic in the request body");
        Assert(document.RootElement.GetProperty("title").GetString() == "작업 완료 · ChatGPT",
            "the ntfy JSON payload must preserve Unicode titles");
    }

    private static void MobileDiagnosticLogNeverPersistsTopic()
    {
        const string topic = "mobile-diagnostic-private-topic";
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-MobileLog-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "diagnostics.log");
        try
        {
            DiagnosticLog.WriteMobilePush(path, new HttpRequestException($"failed for {topic}"));
            var text = File.ReadAllText(path);

            Assert(!text.Contains(topic, StringComparison.Ordinal),
                "mobile diagnostics must never persist the ntfy topic even when an exception contains it");
            Assert(text.Contains(nameof(HttpRequestException), StringComparison.Ordinal),
                "mobile diagnostics should preserve the failure type for troubleshooting");
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
