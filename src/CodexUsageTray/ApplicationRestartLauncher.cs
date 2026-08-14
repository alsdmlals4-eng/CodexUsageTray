using System.Diagnostics;
using System.Globalization;

namespace CodexUsageTray;

internal sealed class ApplicationRestartLauncher
{
    private readonly Func<string, int, bool> _startProcess;

    public ApplicationRestartLauncher()
        : this(StartProcess)
    {
    }

    internal ApplicationRestartLauncher(Func<string, int, bool> startProcess)
    {
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool TryStart(string executablePath, int currentProcessId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || currentProcessId <= 0)
            {
                return false;
            }

            return _startProcess(Path.GetFullPath(executablePath), currentProcessId);
        }
        catch
        {
            return false;
        }
    }

    private static bool StartProcess(string executablePath, int currentProcessId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--restart-after {currentProcessId.ToString(CultureInfo.InvariantCulture)}",
            UseShellExecute = true
        });
        return process is not null;
    }
}
