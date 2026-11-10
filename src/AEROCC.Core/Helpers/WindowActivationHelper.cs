using System.Runtime.InteropServices;

namespace AEROCC.Core.Helpers;

public static class WindowActivationHelper
{
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>
    /// Forcefully brings the given window to the foreground, bypassing Windows LockSetForegroundWindow restrictions.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        ShowWindow(hWnd, SW_RESTORE);

        IntPtr foreWnd = GetForegroundWindow();
        uint foreThread = GetWindowThreadProcessId(foreWnd, IntPtr.Zero);
        uint appThread = GetCurrentThreadId();

        if (foreThread != appThread && foreThread != 0)
        {
            AttachThreadInput(appThread, foreThread, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            AttachThreadInput(appThread, foreThread, false);
        }
        else
        {
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
    }

    public static bool IsForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        return GetForegroundWindow() == hWnd;
    }
}
