namespace CodexUsageTray.Core;

public sealed record QuotaWindow(
    string LimitId,
    string? LimitName,
    string WindowKind,
    double UsedPercent,
    int WindowDurationMinutes,
    DateTimeOffset ResetsAt,
    string? PlanType);

public sealed record UsageSnapshot(
    IReadOnlyList<QuotaWindow> Windows,
    DateTimeOffset ObservedAt);

public sealed record QuotaDisplayRow(
    string Title,
    string RemainingText,
    string ResetText);

public enum UsageSeverity
{
    Normal,
    Warning,
    Critical,
    Unknown
}
