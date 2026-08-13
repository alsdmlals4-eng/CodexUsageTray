using System.Text.Json;

namespace CodexUsageTray.Core;

public static class RateLimitParser
{
    public static UsageSnapshot Parse(string json, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            root = result;
        }

        var windows = new List<QuotaWindow>();
        if (root.TryGetProperty("rateLimitsByLimitId", out var buckets))
        {
            if (buckets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Codex 복수 제한 필드가 올바른 객체가 아닙니다.");
            }

            foreach (var property in buckets.EnumerateObject())
            {
                ParseBucket(property.Value, property.Name, windows);
            }
        }
        else if (root.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            ParseBucket(legacy, "codex", windows);
        }

        if (windows.Count == 0)
        {
            throw new InvalidDataException("Codex 사용 제한 응답에 표시 가능한 제한 구간이 없습니다.");
        }

        return new UsageSnapshot(windows.AsReadOnly(), observedAt);
    }

    private static void ParseBucket(JsonElement bucket, string fallbackId, ICollection<QuotaWindow> output)
    {
        if (bucket.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var limitId = GetOptionalString(bucket, "limitId") ?? fallbackId;
        var limitName = GetOptionalString(bucket, "limitName");
        var planType = GetOptionalString(bucket, "planType");
        ParseWindow(bucket, "primary", limitId, limitName, planType, output);
        ParseWindow(bucket, "secondary", limitId, limitName, planType, output);
    }

    private static void ParseWindow(
        JsonElement bucket,
        string kind,
        string limitId,
        string? limitName,
        string? planType,
        ICollection<QuotaWindow> output)
    {
        if (!bucket.TryGetProperty(kind, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!TryGetDouble(window, "usedPercent", out var usedPercent) ||
            !TryGetInt32(window, "windowDurationMins", out var duration) || duration <= 0 ||
            !TryGetInt64(window, "resetsAt", out var resetsAt))
        {
            return;
        }

        DateTimeOffset resetTime;
        try
        {
            resetTime = DateTimeOffset.FromUnixTimeSeconds(resetsAt);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        output.Add(new QuotaWindow(
            limitId,
            limitName,
            kind,
            Math.Clamp(usedPercent, 0d, 100d),
            duration,
            resetTime,
            planType));
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value) &&
               double.IsFinite(value);
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }
}
