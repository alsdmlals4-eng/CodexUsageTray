using System.Diagnostics;

namespace CodexUsageTray;

internal sealed class ApplicationRestartLauncher
{
    private readonly Func<string, bool> _startProcess;

    public ApplicationRestartLauncher()
        : this(StartProcess)
    {
    }

    internal ApplicationRestartLauncher(Func<string, bool> startProcess)
    {
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool TryStart(string executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            return _startProcess(Path.GetFullPath(executablePath));
        }
        catch
        {
            return false;
        }
    }

    private static bool StartProcess(string executablePath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        });
        return process is not null;
    }
}
