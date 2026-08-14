using System.Text.Json;

namespace CodexUsageTray.Core;

public static class BrowserActivityEventParser
{
    private const int TitleLimit = 80;
    private const int DetailLimit = 120;

    public static ActivityEvent Parse(string json, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var statusName = RequiredString(root, "status");
        var activityId = RequiredString(root, "activityId");
        var sourceUri = ParseChatGptConversationUri(RequiredString(root, "url"));
        var conversationId = GetSourceId(sourceUri);
        var title = Limit(OptionalString(root, "title") ?? string.Empty, TitleLimit);
        var reason = Limit(OptionalString(root, "reason") ?? string.Empty, DetailLimit);
        var tabId = OptionalPositiveInt32(root, "tabId");
        var windowId = OptionalPositiveInt32(root, "windowId");
        var status = statusName switch
        {
            "completed" => ActivityStatus.Completed,
            "approval_required" => ActivityStatus.ApprovalRequired,
            "retrying" => ActivityStatus.Retrying,
            "recovery_required" => ActivityStatus.RecoveryRequired,
            "recovered" => ActivityStatus.Recovered,
            _ => throw new InvalidDataException($"지원하지 않는 ChatGPT 웹 상태입니다: {statusName}")
        };

        return new ActivityEvent(
            $"web:{conversationId}",
            activityId,
            string.Empty,
            "ChatGPT Web",
            string.IsNullOrWhiteSpace(title) ? Shorten(conversationId, 8) : title,
            status,
            BuildSummary(status, reason),
            reason,
            occurredAt,
            SourceKind: ActivitySourceKind.ChatGptWeb,
            SourceUri: sourceUri.AbsoluteUri,
            BrowserTabId: tabId,
            BrowserWindowId: windowId);
    }

    private static string BuildSummary(ActivityStatus status, string reason) => status switch
    {
        ActivityStatus.Completed => "ChatGPT 응답 생성이 끝났습니다.",
        ActivityStatus.ApprovalRequired => "ChatGPT에서 승인 또는 확인이 필요합니다.",
        ActivityStatus.Retrying => "ChatGPT 일시 오류를 자동 재시도 중입니다.",
        ActivityStatus.RecoveryRequired when string.Equals(reason, "disconnected_waiting", StringComparison.Ordinal) =>
            "ChatGPT 연결이 끊겨 응답 재연결이 필요합니다.",
        ActivityStatus.RecoveryRequired => "ChatGPT 작업 복구가 필요합니다.",
        ActivityStatus.Recovered => "ChatGPT 작업이 자동 복구되었습니다.",
        _ => "ChatGPT 작업 상태가 변경되었습니다."
    };

    private static Uri ParseChatGptConversationUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException("ChatGPT 대화 URL이 올바르지 않습니다.");
        }

        _ = GetSourceId(uri);
        var safe = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        return safe.Uri;
    }

    private static string GetSourceId(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "c", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(segments[index + 1]))
            {
                return Uri.UnescapeDataString(segments[index + 1]);
            }
        }

        if (segments.Length >= 2 &&
            string.Equals(segments[0], "codex", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(segments[^1]))
        {
            return Uri.UnescapeDataString(segments[^1]);
        }

        throw new InvalidDataException("ChatGPT 대화 또는 Codex 작업 식별자가 URL에 없습니다.");
    }

    private static string RequiredString(JsonElement root, string propertyName) =>
        OptionalString(root, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"ChatGPT 웹 이벤트 필드가 없습니다: {propertyName}");

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static int OptionalPositiveInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value) ||
            value <= 0)
        {
            throw new InvalidDataException($"ChatGPT 웹 이벤트 숫자 필드가 올바르지 않습니다: {propertyName}");
        }

        return value;
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private static string Limit(string value, int length)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= length ? normalized : normalized[..(length - 1)] + "…";
    }
}
