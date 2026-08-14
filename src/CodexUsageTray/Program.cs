namespace CodexUsageTray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!ApplicationRestartStartup.WaitForPreviousInstance(args))
        {
            return;
        }

        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\CodexUsageTray", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
