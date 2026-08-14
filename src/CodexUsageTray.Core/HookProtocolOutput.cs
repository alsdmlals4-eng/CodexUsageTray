namespace CodexUsageTray.Core;

public static class HookProtocolOutput
{
    private const string StopSuccessJson = "{\"continue\":true}";

    public static string GetSuccessJson(string? eventName) =>
        string.Equals(eventName, "Stop", StringComparison.Ordinal)
            ? StopSuccessJson
            : string.Empty;
}
