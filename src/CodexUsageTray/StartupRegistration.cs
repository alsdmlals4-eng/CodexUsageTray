using Microsoft.Win32;

namespace CodexUsageTray;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexUsageTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var executable = Environment.ProcessPath;
        if (executable is null || key?.GetValue(ValueName) is not string value)
        {
            return false;
        }

        return string.Equals(
            value.Trim(),
            $"\"{executable}\" --startup",
            StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) ??
            throw new InvalidOperationException("Windows 시작 프로그램 레지스트리를 열 수 없습니다.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");
        key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
    }
}
