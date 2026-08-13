using System.Runtime.InteropServices;

namespace CodexUsageTray;

internal static class WindowActivator
{
    private const int Restore = 9;

    public static bool TryActivate(long windowHandle, int expectedProcessId = 0)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        var handle = new IntPtr(windowHandle);
        if (!IsWindow(handle))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(handle, out var actualProcessId);
        if (expectedProcessId > 0 && actualProcessId != (uint)expectedProcessId)
        {
            return false;
        }

        _ = ShowWindow(handle, Restore);
        return SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
