using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AEROCC.Core.Hotkeys;

public class HotkeyEventArgs : EventArgs
{
    public uint VkCode { get; }
    public uint ScanCode { get; }
    public bool IsKeyDown { get; }
    public bool AltPressed { get; }
    public bool CtrlPressed { get; }
    public bool ShiftPressed { get; }
    public bool WinPressed { get; }
    public bool Handled { get; set; }

    public HotkeyEventArgs(uint vkCode, uint scanCode, bool isKeyDown, bool alt, bool ctrl, bool shift, bool win)
    {
        VkCode = vkCode;
        ScanCode = scanCode;
        IsKeyDown = isKeyDown;
        AltPressed = alt;
        CtrlPressed = ctrl;
        ShiftPressed = shift;
        WinPressed = win;
    }
}

public class GlobalHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    public const uint VK_SPACE = 0x20;
    public const uint VK_F4 = 0x73;
    public const uint VK_F7 = 0x76;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_F20 = 0x83; // Mic Mute
    public const uint VK_F21 = 0x84; // Airplane / Radio
    public const uint VK_F23 = 0x86; // GiMATE / Copilot
    public const uint VK_F24 = 0x87; // Touchpad toggle
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_RSHIFT = 0xA1;
    public const uint VK_LCONTROL = 0xA2;
    public const uint VK_RCONTROL = 0xA3;
    public const uint VK_LMENU = 0xA4;
    public const uint VK_RMENU = 0xA5;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _isDisposed = false;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <summary>
    /// When true, bare F1-F12 keys are mapped to OEM multimedia functions.
    /// When false, bare F1-F12 keys are standard F-keys.
    /// </summary>
    public bool IsFnLockActive { get; set; } = false;

    public event EventHandler<HotkeyEventArgs>? KeyIntercepted;
    public event Action? GimatePressed;
    public event Action? TouchpadTogglePressed;
    public event Action? MicMuteTogglePressed;
    public event Action? PerformanceTogglePressed;
    public event Action? AirplaneTogglePressed;
    public event Action? BacklightTogglePressed;

    public bool Install()
    {
        if (_hookId != IntPtr.Zero) return true;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);

        return _hookId != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

            var args = new HotkeyEventArgs(kb.vkCode, kb.scanCode, isKeyDown, alt, ctrl, shift, win);
            KeyIntercepted?.Invoke(this, args);

            if (isKeyDown)
            {
                // 1. GiMATE key (emits VK_F23 or Win+Shift+F23)
                if (kb.vkCode == VK_F23)
                {
                    Task.Run(() => GimatePressed?.Invoke());
                    return (IntPtr)1; // Swallow key to suppress Windows Copilot
                }

                // 2. Touchpad Toggle key (emits VK_F24 with Ctrl+Win)
                if (kb.vkCode == VK_F24)
                {
                    Task.Run(() => TouchpadTogglePressed?.Invoke());
                    // Let it pass through so Precision Touchpad driver handles it too
                }

                // 3. Microphone Mute key (emits VK_F20 on some firmware)
                if (kb.vkCode == VK_F20)
                {
                    Task.Run(() => MicMuteTogglePressed?.Invoke());
                    return (IntPtr)1;
                }

                // 4. Airplane Mode key (emits VK_F21)
                if (kb.vkCode == VK_F21)
                {
                    Task.Run(() => AirplaneTogglePressed?.Invoke());
                }

                // 5. FnLock Synchronized Keys:
                // When FnLock is ON (multimedia mode):
                // - F4 is Microphone Mute
                // - F7 is Performance Profile switch
                // - F9 is Touchpad toggle
                // - F10 is Airplane Mode
                if (IsFnLockActive)
                {
                    if (kb.vkCode == VK_F4 && !alt && !ctrl)
                    {
                        Task.Run(() => MicMuteTogglePressed?.Invoke());
                        return (IntPtr)1; // Swallow standard F4
                    }

                    if (kb.vkCode == VK_F7 && !alt && !ctrl && !shift && !win)
                    {
                        Task.Run(() => PerformanceTogglePressed?.Invoke());
                        return (IntPtr)1; // Swallow standard F7
                    }

                    if (kb.vkCode == VK_F9 && !alt && !ctrl && !shift && !win)
                    {
                        Task.Run(() => TouchpadTogglePressed?.Invoke());
                        return (IntPtr)1;
                    }

                    if (kb.vkCode == VK_F10 && !alt && !ctrl && !shift && !win)
                    {
                        Task.Run(() => AirplaneTogglePressed?.Invoke());
                        return (IntPtr)1;
                    }
                }
                else
                {
                    // When FnLock is OFF (Standard mode):
                    // If the hardware emits special combinations or user presses Fn+key
                    // If F7 is received with Fn indicator (or if passed as custom)
                }

                // 6. Fn + Space / Backlight toggle
                // If Space is pressed with Fn or if custom scancode
                if (kb.vkCode == VK_SPACE && (kb.flags & 0x20) != 0) // Or custom
                {
                    // Will be handled if detected
                }
            }

            if (args.Handled)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Uninstall();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
