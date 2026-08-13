using System.Text.Json;

namespace CodexUsageTray.Core;

public static class ActivityEventParser
{
    private const int SummaryLimit = 240;
    private const int DetailLimit = 500;

    public static ActivityEvent ParseHook(string json, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var eventName = RequiredString(root, "hook_event_name");
        var sessionId = RequiredString(root, "session_id");
        var turnId = OptionalString(root, "turn_id");
        var cwd = RequiredString(root, "cwd");
        var project = GetProjectName(cwd);
        var chatLabel = Shorten(sessionId, 8);

        return eventName switch
        {
            "UserPromptSubmit" => new ActivityEvent(
                sessionId,
                turnId,
                cwd,
                project,
                chatLabel,
                ActivityStatus.Running,
                Limit(OptionalString(root, "prompt") ?? "새 작업 시작", SummaryLimit),
                string.Empty,
                occurredAt),
            "PermissionRequest" => ParsePermission(root, sessionId, turnId, cwd, project, chatLabel, occurredAt),
            "Stop" => new ActivityEvent(
                sessionId,
                turnId,
                cwd,
                project,
                chatLabel,
                ActivityStatus.Completed,
                Limit(OptionalString(root, "last_assistant_message") ?? "Codex 작업 완료", SummaryLimit),
                string.Empty,
                occurredAt),
            _ => throw new InvalidDataException($"지원하지 않는 Codex Hook 이벤트입니다: {eventName}")
        };
    }

    private static ActivityEvent ParsePermission(
        JsonElement root,
        string sessionId,
        string? turnId,
        string cwd,
        string project,
        string chatLabel,
        DateTimeOffset occurredAt)
    {
        var toolName = OptionalString(root, "tool_name") ?? "도구";
        var description = string.Empty;
        var command = string.Empty;
        if (root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object)
        {
            description = OptionalString(input, "description") ?? string.Empty;
            command = OptionalString(input, "command") ?? string.Empty;
        }

        var summary = !string.IsNullOrWhiteSpace(description)
            ? description
            : !string.IsNullOrWhiteSpace(command)
                ? command
                : $"{toolName} 실행 승인 필요";
        var detail = string.IsNullOrWhiteSpace(command)
            ? toolName
            : $"{toolName} · {command}";

        return new ActivityEvent(
            sessionId,
            turnId,
            cwd,
            project,
            chatLabel,
            ActivityStatus.ApprovalRequired,
            Limit(summary, SummaryLimit),
            Limit(detail, DetailLimit),
            occurredAt);
    }

    private static string RequiredString(JsonElement root, string propertyName) =>
        OptionalString(root, propertyName) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Codex Hook 필드가 없습니다: {propertyName}");

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static string GetProjectName(string cwd)
    {
        var trimmed = cwd.TrimEnd('/', '\\');
        var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value[..length];

    private static string Limit(string value, int length)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= length ? normalized : normalized[..(length - 1)] + "…";
    }
}
