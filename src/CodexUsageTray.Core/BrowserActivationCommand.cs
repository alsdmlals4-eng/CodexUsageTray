using System.Text.Json;

namespace CodexUsageTray.Core;

public sealed record BrowserActivationCommand(
    string Action,
    string Url,
    int TabId,
    int WindowId)
{
    public static BrowserActivationCommand FromActivity(ActivityEvent activity) =>
        FromActivity(activity, "activate");

    public static BrowserActivationCommand ForReload(ActivityEvent activity) =>
        FromActivity(activity, "reload");

    private static BrowserActivationCommand FromActivity(ActivityEvent activity, string action)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.SourceKind != ActivitySourceKind.ChatGptWeb ||
            activity.BrowserTabId <= 0 ||
            activity.BrowserWindowId <= 0 ||
            !Guid.TryParse(activity.BrowserConnectionId, out _) ||
            !Uri.TryCreate(activity.SourceUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Activity does not contain a safe browser source identity.", nameof(activity));
        }

        var safeUri = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri;
        return new BrowserActivationCommand(
            action,
            safeUri,
            activity.BrowserTabId,
            activity.BrowserWindowId);
    }

    public static bool TryParse(string json, out BrowserActivationCommand? command)
    {
        command = null;
        try
        {
            var candidate = JsonSerializer.Deserialize<BrowserActivationCommand>(json);
            if (candidate is null ||
                candidate.Action is not ("activate" or "reload") ||
                candidate.TabId <= 0 ||
                candidate.WindowId <= 0 ||
                !Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }

            command = candidate with
            {
                Url = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
