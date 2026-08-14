using System.Reflection;

namespace CodexUsageTray.Windows.Tests;

internal static class MobileNotificationTestProgram
{
    [STAThread]
    private static int Main()
    {
        var existingMain = typeof(Program).GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Existing Windows regression entry point was not found.");
        var existingResult = existingMain.Invoke(null, null);
        var existingExitCode = existingResult is int exitCode ? exitCode : 1;
        if (existingExitCode != 0)
        {
            return existingExitCode;
        }

        try
        {
            MobileNotificationRegressionTests.Run();
            MobileNotificationUiRegressionTests.Run();
            Console.WriteLine("10 mobile notification regression tests passed");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
