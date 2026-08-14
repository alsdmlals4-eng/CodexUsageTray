using CodexUsageTray.Core;

namespace CodexUsageTray.Windows.Tests;

internal static class MobileNotificationUiRegressionTests
{
    public static void Run()
    {
        MobileSettingsFormPersistsAndSendsTestNotification();
        MobileSettingsFormRejectsInvalidTopic();
        MobileNotificationRuntimeUsesPersistedSettings();
    }

    private static void MobileSettingsFormPersistsAndSendsTestNotification()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-MobileUi-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mobile-notifications.json");
        var sent = new List<(string Topic, string Title, string Message, int Priority)>();
        try
        {
            var store = new MobileNotificationSettingsStore(path);
            using var form = new MobileNotificationSettingsForm(
                store,
                (topic, title, message, priority, _) =>
                {
                    sent.Add((topic, title, message, priority));
                    return Task.CompletedTask;
                });
            form.MobileEnabled = true;
            form.Topic = "  ui-test-topic_123  ";

            Assert(form.TopicIsMasked, "the topic textbox must hide the topic by default");
            Assert(form.SaveSettings(), "valid enabled mobile settings must save successfully");
            var loaded = store.Load();
            Assert(loaded.Enabled && loaded.Topic == "ui-test-topic_123",
                "the settings form must persist the enabled state and normalized topic");
            Assert(form.SendTestAsync(CancellationToken.None).GetAwaiter().GetResult(),
                "a valid topic must allow a test notification");
            Assert(sent.Count == 1 && sent[0].Topic == "ui-test-topic_123",
                "the test notification must use the current normalized topic");
            Assert(sent[0].Title.Contains("테스트", StringComparison.Ordinal) && sent[0].Priority == 4,
                "the test notification must be clearly identified and use high priority");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void MobileSettingsFormRejectsInvalidTopic()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-MobileUi-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mobile-notifications.json");
        try
        {
            var store = new MobileNotificationSettingsStore(path);
            using var form = new MobileNotificationSettingsForm(
                store,
                (_, _, _, _, _) => Task.CompletedTask);
            form.MobileEnabled = true;
            form.Topic = "bad/topic";

            Assert(!form.SaveSettings(),
                "enabled settings must reject characters that ntfy does not permit in topic names");
            Assert(!store.Load().Enabled,
                "invalid settings must not overwrite the disabled fail-closed state");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void MobileNotificationRuntimeUsesPersistedSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"CodexUsageTray-MobileRuntime-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "mobile-notifications.json");
        var sentTopics = new List<string>();
        try
        {
            var store = new MobileNotificationSettingsStore(path);
            store.Save(new MobileNotificationSettings(true, "runtime-topic"));
            using var runtime = new MobileNotificationRuntime(
                store,
                (topic, _, _, _, _) =>
                {
                    sentTopics.Add(topic);
                    return Task.CompletedTask;
                });
            var activity = new ActivityEvent(
                "runtime-session",
                "runtime-turn",
                @"C:\work\RuntimeProject",
                "RuntimeProject",
                "runtime-chat",
                ActivityStatus.Completed,
                "runtime completed",
                string.Empty,
                DateTimeOffset.Now);

            runtime.NotifyAsync(activity, CancellationToken.None).GetAwaiter().GetResult();

            Assert(sentTopics.SequenceEqual(new[] { "runtime-topic" }),
                "the runtime must load persisted settings and forward supported activities once");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
