using System.Diagnostics;
using CodexUsageTray.Core;
using ActivityEvent = CodexUsageTray.Core.ActivityEvent;

namespace CodexUsageTray;

internal static class ActivitySourceLauncher
{
    public static bool TryOpen(ActivityEvent activity)
    {
        if (activity.SourceKind == ActivitySourceKind.ChatGptWeb)
        {
            return TryOpenWebConversation(activity.SourceUri);
        }

        return WindowActivator.TryActivate(activity.SourceWindowHandle, activity.SourceProcessId);
    }

    private static bool TryOpenWebConversation(string? sourceUri)
    {
        if (!Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
