using System.Diagnostics;

namespace CodexUsageTray;

internal static class ApplicationRestartStartup
{
    private const string RestartAfterArgument = "--restart-after";
    private const int WaitTimeoutMilliseconds = 15_000;

    public static bool WaitForPreviousInstance(string[] args) =>
        WaitForPreviousInstance(args, WaitForProcessExit);

    internal static bool WaitForPreviousInstance(
        string[] args,
        Func<int, bool> waitForProcessExit)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(waitForProcessExit);

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], RestartAfterArgument, StringComparison.Ordinal))
            {
                continue;
            }

            if (index + 1 >= args.Length ||
                !int.TryParse(args[index + 1], out var processId) ||
                processId <= 0)
            {
                return false;
            }

            return waitForProcessExit(processId);
        }

        return true;
    }

    private static bool WaitForProcessExit(int processId)
    {
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(WaitTimeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            // The previous instance already exited before the replacement attached to it.
            return true;
        }
        catch
        {
            return false;
        }
    }
}
