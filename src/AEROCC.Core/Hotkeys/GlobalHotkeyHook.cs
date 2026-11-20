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

/// <summary>
/// High-performance global keyboard hook running on a dedicated background OS thread.
/// Completely decoupled from WPF UI to ensure 0ms typing latency across all applications.
/// </summary>
public class GlobalHotkeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint WM_QUIT = 0x0012;

    // Targeted Virtual Key Codes
    public const uint VK_SPACE = 0x20;
    public const uint VK_F4 = 0x73;
    public const uint VK_F7 = 0x76;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_FN = 0x83;  // Physical Fn key on Gigabyte Aero 16 (ScanCode = 0x6B)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
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

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out NativeMsg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMsg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMsg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private Thread? _hookThread;
    private uint _hookThreadId = 0;
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private bool _isDisposed = false;
    private long _lastFnPulseTick = 0;

    private long _lastMicTick = 0;
    private long _lastPerfTick = 0;
    private long _lastBacklightTick = 0;
    private long _lastTouchpadTick = 0;
    private long _lastAirplaneTick = 0;
    private long _lastGimateTick = 0;

    public bool IsInstalled => _hookId != IntPtr.Zero;
    public bool SnifferMode { get; set; } = false;
    public bool IsFnLockActive { get; set; } = false;
    public bool IsFnHeld => (Environment.TickCount64 - _lastFnPulseTick) < 400;

    public event EventHandler<HotkeyEventArgs>? KeyIntercepted;
    public event Action? GimatePressed;
    public event Action? TouchpadTogglePressed;
    public event Action? MicMuteTogglePressed;
    public event Action? PerformanceTogglePressed;
    public event Action? AirplaneTogglePressed;
    public event Action? BacklightTogglePressed;

    public bool Install()
    {
        if (_hookThread != null && _hookThread.IsAlive) return true;

        using var readyEvent = new ManualResetEventSlim(false);

        _hookThread = new Thread(() =>
        {
            _hookThreadId = GetCurrentThreadId();
            _proc = HookCallback;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);

            readyEvent.Set();

            if (_hookId != IntPtr.Zero)
            {
                while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "AEROCC_GlobalHotkeyThread"
        };

        _hookThread.Start();
        readyEvent.Wait(2000);

        return _hookId != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _hookThread?.Join(500);
            _hookThread = null;
            _hookThreadId = 0;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // Direct memory read of vkCode at offset 0
            uint vkCode = (uint)Marshal.ReadInt32(lParam);

            // Target key filter: ONLY hardware hotkeys and function keys are ever processed.
            // Standard typing keys (letters, digits, punctuation, Enter, Backspace, etc.)
            // are ALWAYS immediately forwarded to guarantee 0ms latency and prevent keystroke interception.
            bool isTargetedKey = vkCode is VK_FN
                or VK_F21 or VK_F23 or VK_F24
                or VK_F4 or VK_F7 or VK_F9 or VK_F10 or VK_SPACE
                || (SnifferMode && vkCode is >= 0x70 and <= 0x87); // F1 - F24 during diagnostic sniffer

            if (!isTargetedKey)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            uint scanCode = (uint)Marshal.ReadInt32(lParam, 4);
            uint flags = (uint)Marshal.ReadInt32(lParam, 8);
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool altDown = (flags & 0x20) != 0;

            // In sniffer mode, notify diagnostic listener
            if (SnifferMode && KeyIntercepted != null)
            {
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
                var args = new HotkeyEventArgs(vkCode, scanCode, isKeyDown, altDown, ctrl, shift, win);
                KeyIntercepted.Invoke(this, args);
            }

            long now = Environment.TickCount64;

            // 1. Physical Fn key tracking (0x83 / Scan = 0x6B)
            // When user holds Fn, the EC generates repeated 0x83 pulses without keyup.
            // Record timestamp and swallow so Windows does not get flooded with OEM resets!
            if (vkCode == VK_FN)
            {
                if (isKeyDown)
                {
                    _lastFnPulseTick = now;
                }
                return (IntPtr)1; // Swallow 0x83
            }

            bool fnActive = (now - _lastFnPulseTick) < 400;

            // 2. Keyboard Backlight brightness cycle (Fn + Space)
            if (vkCode == VK_SPACE)
            {
                if (fnActive)
                {
                    if (isKeyDown && (now - _lastBacklightTick > 350))
                    {
                        _lastBacklightTick = now;
                        ThreadPool.QueueUserWorkItem(_ => BacklightTogglePressed?.Invoke());
                    }
                    return (IntPtr)1; // Swallow Space so whitespace is not typed!
                }
                // Normal spacebar: pass through immediately!
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Determine if multimedia actions should trigger:
            // Either Fn is physically held (when FnLock is OFF),
            // OR bare F-key is pressed (when FnLock is ON).
            bool shouldTrigger = (fnActive && !IsFnLockActive) || (!fnActive && IsFnLockActive);

            // 3. Microphone Mute (F4)
            if (vkCode == VK_F4 && !altDown && shouldTrigger)
            {
                if (isKeyDown && (now - _lastMicTick > 350))
                {
                    _lastMicTick = now;
                    ThreadPool.QueueUserWorkItem(_ => MicMuteTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow F4
            }

            // 4. Performance Profile switch (F7)
            if (vkCode == VK_F7 && !altDown && shouldTrigger)
            {
                if (isKeyDown && (now - _lastPerfTick > 350))
                {
                    _lastPerfTick = now;
                    ThreadPool.QueueUserWorkItem(_ => PerformanceTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow F7
            }

            // 5. Touchpad Toggle (F9)
            if (vkCode == VK_F9 && !altDown && shouldTrigger)
            {
                if (isKeyDown && (now - _lastTouchpadTick > 350))
                {
                    _lastTouchpadTick = now;
                    ThreadPool.QueueUserWorkItem(_ => TouchpadTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow F9
            }

            // 6. Airplane Mode (F10)
            if (vkCode == VK_F10 && !altDown && shouldTrigger)
            {
                if (isKeyDown && (now - _lastAirplaneTick > 350))
                {
                    _lastAirplaneTick = now;
                    ThreadPool.QueueUserWorkItem(_ => AirplaneTogglePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow F10
            }

            // 7. GiMATE / Copilot key (0x86 / VK_F23)
            if (vkCode == VK_F23)
            {
                if (isKeyDown && (now - _lastGimateTick > 350))
                {
                    _lastGimateTick = now;
                    // Cancel Windows Start menu activation if Win key was held with Copilot
                    keybd_event(0xFF, 0, 0, UIntPtr.Zero);
                    keybd_event(0xFF, 0, 2, UIntPtr.Zero);

                    ThreadPool.QueueUserWorkItem(_ => GimatePressed?.Invoke());
                }
                return (IntPtr)1; // Swallow key to suppress Copilot
            }

            // 8. Hardware Touchpad OEM key (0x87 / VK_F24)
            if (vkCode == VK_F24)
            {
                if (isKeyDown && (now - _lastTouchpadTick > 350))
                {
                    _lastTouchpadTick = now;
                    ThreadPool.QueueUserWorkItem(_ => TouchpadTogglePressed?.Invoke());
                }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // 9. Hardware Airplane OEM key (0x84 / VK_F21)
            if (vkCode == VK_F21)
            {
                if (isKeyDown && (now - _lastAirplaneTick > 350))
                {
                    _lastAirplaneTick = now;
                    ThreadPool.QueueUserWorkItem(_ => AirplaneTogglePressed?.Invoke());
                }
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
        }
        GC.SuppressFinalize(this);
    }
}
