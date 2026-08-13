namespace CodexUsageTray.Core;

public static class UsagePresentation
{
    public static int GetRemainingPercent(double usedPercent)
    {
        var clamped = Math.Clamp(usedPercent, 0d, 100d);
        return (int)Math.Floor(100d - clamped);
    }

    public static UsageSeverity GetSeverity(int remainingPercent) => remainingPercent switch
    {
        > 50 => UsageSeverity.Normal,
        >= 20 => UsageSeverity.Warning,
        >= 0 => UsageSeverity.Critical,
        _ => UsageSeverity.Unknown
    };

    public static int GetTrayPercent(UsageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Windows.Count == 0)
        {
            throw new InvalidOperationException("표시할 Codex 사용 제한이 없습니다.");
        }

        return snapshot.Windows.Min(window => GetRemainingPercent(window.UsedPercent));
    }

    public static string GetWindowLabel(QuotaWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var duration = window.WindowDurationMinutes switch
        {
            10_080 => "주간",
            >= 1_440 when window.WindowDurationMinutes % 1_440 == 0 => $"{window.WindowDurationMinutes / 1_440}일",
            >= 60 when window.WindowDurationMinutes % 60 == 0 => $"{window.WindowDurationMinutes / 60}시간",
            _ => $"{window.WindowDurationMinutes}분"
        };

        return string.IsNullOrWhiteSpace(window.LimitName)
            ? duration
            : $"{window.LimitName} · {duration}";
    }

    public static string BuildTooltip(UsageSnapshot snapshot)
    {
        var remaining = GetTrayPercent(snapshot);
        var text = $"Codex {remaining}% 남음";
        return text.Length <= 63 ? text : text[..63];
    }

    public static QuotaDisplayRow CreateDisplayRow(QuotaWindow window, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(timeZone);
        var localReset = TimeZoneInfo.ConvertTime(window.ResetsAt, timeZone);
        return new QuotaDisplayRow(
            GetWindowLabel(window),
            $"{GetRemainingPercent(window.UsedPercent)}% 남음",
            $"{localReset:MM-dd HH:mm} 초기화");
    }
}
