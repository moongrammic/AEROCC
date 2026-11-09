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

    // Targeted Virtual Key Codes
    public const uint VK_SPACE = 0x20;
    public const uint VK_F4 = 0x73;
    public const uint VK_F7 = 0x76;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_FN = 0x83;  // Physical Fn key on Gigabyte Aero 16
    public const uint VK_F21 = 0x84; // Airplane / Radio key
    public const uint VK_F23 = 0x86; // GiMATE / Copilot key
    public const uint VK_F24 = 0x87; // Precision Touchpad toggle key

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

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _isDisposed = false;
    private volatile bool _isFnHeld = false;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    /// <summary>
    /// When true, bare F1-F12 keys are mapped to OEM multimedia functions.
    /// When false, bare F1-F12 keys are standard F-keys.
    /// </summary>
    public bool IsFnLockActive { get; set; } = false;

    public bool IsFnHeld => _isFnHeld;

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
            // ULTRA-FAST PATH: Read vkCode directly from memory (offset 0)
            // If it's not one of our targeted keys, return immediately in ~2ns!
            uint vkCode = (uint)Marshal.ReadInt32(lParam);

            if (vkCode != VK_FN &&
                vkCode != VK_F23 &&
                vkCode != VK_F24 &&
                vkCode != VK_F4 &&
                vkCode != VK_F7 &&
                vkCode != VK_F9 &&
                vkCode != VK_F10 &&
                vkCode != VK_SPACE &&
                vkCode != VK_F21)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            uint flags = (uint)Marshal.ReadInt32(lParam, 8); // flags at offset 8
            bool altDown = (flags & 0x20) != 0; // LLKHF_ALTDOWN

            // Handle Fn key press / release tracking (0x83)
            if (vkCode == VK_FN)
            {
                _isFnHeld = isKeyDown;
                // Never swallow the Fn key itself - let Windows/EC proceed normally!
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Dedicated GiMATE key (0x86 / VK_F23)
            if (vkCode == VK_F23)
            {
                if (isKeyDown)
                {
                    Task.Run(() => GimatePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow key to suppress Microsoft Copilot
            }

            // Touchpad hardware key (0x87 / VK_F24)
            if (vkCode == VK_F24)
            {
                if (isKeyDown)
                {
                    Task.Run(() => TouchpadTogglePressed?.Invoke());
                }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Dedicated Airplane key (0x84 / VK_F21)
            if (vkCode == VK_F21)
            {
                if (isKeyDown)
                {
                    Task.Run(() => AirplaneTogglePressed?.Invoke());
                }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Fn + Space: Keyboard Backlight Cycle
            if (vkCode == VK_SPACE && _isFnHeld)
            {
                if (isKeyDown)
                {
                    Task.Run(() => BacklightTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow Space so a whitespace character is not typed!
            }

            // Determine if the action should be triggered:
            // Either Fn is physically held down (when FnLock is OFF),
            // OR bare F-key is pressed (when FnLock is ON).
            bool shouldTrigger = (_isFnHeld && !IsFnLockActive) || (!_isFnHeld && IsFnLockActive);

            // F4: Microphone Mute Toggle (Do NOT swallow Alt+F4!)
            if (vkCode == VK_F4 && !altDown && shouldTrigger)
            {
                if (isKeyDown)
                {
                    Task.Run(() => MicMuteTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow standard F4
            }

            // F7: Performance Profile Switch
            if (vkCode == VK_F7 && !altDown && shouldTrigger)
            {
                if (isKeyDown)
                {
                    Task.Run(() => PerformanceTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow standard F7
            }

            // F9: Touchpad Toggle
            if (vkCode == VK_F9 && !altDown && shouldTrigger)
            {
                if (isKeyDown)
                {
                    Task.Run(() => TouchpadTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow standard F9
            }

            // F10: Airplane Mode Toggle
            if (vkCode == VK_F10 && !altDown && shouldTrigger)
            {
                if (isKeyDown)
                {
                    Task.Run(() => AirplaneTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow standard F10
            }

            // Sniffer diagnostic event invocation only if someone is listening
            if (KeyIntercepted != null)
            {
                uint scanCode = (uint)Marshal.ReadInt32(lParam, 4);
                var args = new HotkeyEventArgs(vkCode, scanCode, isKeyDown, altDown, false, false, false);
                KeyIntercepted.Invoke(this, args);
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
