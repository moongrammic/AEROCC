using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AEROCC.Core.Hotkeys;

public class RawInputEventArgs : EventArgs
{
    public uint DeviceType { get; }
    public byte[] Data { get; }
    public uint MakeCode { get; }
    public uint VKey { get; }
    public uint Message { get; }

    public RawInputEventArgs(uint devType, byte[] data, uint makeCode, uint vKey, uint message)
    {
        DeviceType = devType;
        Data = data;
        MakeCode = makeCode;
        VKey = vKey;
        Message = message;
    }
}

/// <summary>
/// Low-level Raw Input listener using a dedicated Win32 message-only window.
/// Registers for:
///   1) Gigabyte ITE EC vendor HID hotkey stream (UsagePage = 0xFF02, Usage = 1)
///   2) Standard Keyboard stream (UsagePage = 1, Usage = 6)
/// Completely non-blocking with 0ms keyboard latency.
/// </summary>
public class RawInputReceiver : IDisposable
{
    private static RawInputReceiver? _instance;
    public static RawInputReceiver Instance => _instance ??= new RawInputReceiver();

    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RID_HEADER = 0x10000005;
    private const int RIM_TYPEMOUSE = 0;
    private const int RIM_TYPEKEYBOARD = 1;
    private const int RIM_TYPEHID = 2;

    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int RIDEV_DEVNOTIFY = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWINPUTKEYBOARD
    {
        [FieldOffset(0)]
        public ushort MakeCode;
        [FieldOffset(2)]
        public ushort Flags;
        [FieldOffset(4)]
        public ushort Reserved;
        [FieldOffset(6)]
        public ushort VKey;
        [FieldOffset(8)]
        public uint Message;
        [FieldOffset(12)]
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
        public byte bRawData; // Variable size
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

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

    private const uint WM_QUIT = 0x0012;
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    private Thread? _msgThread;
    private uint _msgThreadId;
    private IntPtr _hwnd = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;
    private bool _isStarted = false;
    private bool _isDisposed = false;

    private long _lastMicMuteTick = 0;
    private long _lastPerfTick = 0;
    private long _lastTouchpadTick = 0;
    private long _lastBacklightTick = 0;
    private long _lastFnLockTick = 0;
    private long _lastWinLockTick = 0;
    private long _lastAirplaneTick = 0;
    private long _lastGimateTick = 0;

    public event EventHandler<RawInputEventArgs>? RawInputReceived;
    public event Action<byte[]>? VendorHidReportReceived;
    public event Action? GimateHotkeyPressed;
    public event Action? MicMuteHotkeyPressed;
    public event Action? PerformanceHotkeyPressed;
    public event Action? TouchpadHotkeyPressed;
    public event Action? BacklightHotkeyPressed;
    public event Action? FnLockHotkeyPressed;
    public event Action? WinLockHotkeyPressed;
    public event Action? AirplaneHotkeyPressed;

    public bool IsStarted => _isStarted;

    public void Start()
    {
        if (_isStarted) return;

        using var readyEvent = new ManualResetEventSlim(false);

        _msgThread = new Thread(() =>
        {
            _msgThreadId = GetCurrentThreadId();
            _wndProcDelegate = WndProc;

            string className = $"AEROCC_RawInputWnd_{Guid.NewGuid():N}";
            IntPtr hInst = GetModuleHandle(null);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = hInst,
                lpszClassName = className
            };

            ushort atom = RegisterClassEx(ref wc);
            if (atom != 0)
            {
                _hwnd = CreateWindowEx(
                    0, className, "AEROCC_RawInput_Sink",
                    0, 0, 0, 0, 0,
                    HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);
            }

            if (_hwnd != IntPtr.Zero)
            {
                RegisterDevices(_hwnd);
            }

            readyEvent.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            UnregisterClass(className, hInst);
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "AEROCC_RawInputThread"
        };

        _msgThread.Start();
        readyEvent.Wait(2000);
        _isStarted = _hwnd != IntPtr.Zero;
    }

    private void RegisterDevices(IntPtr targetHwnd)
    {
        try
        {
            // Register Gigabyte Vendor HID (UsagePage = 65282 / 0xFF02, Usage = 1)
            // Note: Standard keyboard is intentionally omitted so normal typing
            // is not intercepted or burdened by Raw Input message dispatches.
            var devices = new RAWINPUTDEVICE[1];

            // 1. Gigabyte ITE EC Vendor HID Port
            devices[0].usUsagePage = 65282; // 0xFF02
            devices[0].usUsage = 1;
            devices[0].dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY;
            devices[0].hwndTarget = targetHwnd;

            bool ok = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[RawInputReceiver] RegisterRawInputDevices failed: {err}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RawInputReceiver] Exception registering devices: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            ProcessWmInput(lParam);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ProcessWmInput(IntPtr hRawInput)
    {
        try
        {
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint size = 0;

            if (GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            {
                return;
            }

            IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(hRawInput, RID_INPUT, pBuffer, ref size, headerSize) == size)
                {
                    var header = Marshal.PtrToStructure<RAWINPUTHEADER>(pBuffer);
                    int dataOffset = Marshal.SizeOf<RAWINPUTHEADER>();

                    if (header.dwType == RIM_TYPEHID)
                    {
                        // HID report from ITE EC or vendor device.
                        // Validate header and payload boundaries before reading.
                        if (size >= dataOffset + 8)
                        {
                            uint dwSizeHid = (uint)Marshal.ReadInt32(pBuffer, dataOffset);
                            uint dwCount = (uint)Marshal.ReadInt32(pBuffer, dataOffset + 4);
                            long totalExpected = (long)dwSizeHid * dwCount;

                            int availablePayload = (int)(size - dataOffset - 8);
                            if (availablePayload > 0)
                            {
                                int payloadLength = totalExpected > 0 && totalExpected <= availablePayload
                                    ? (int)totalExpected
                                    : availablePayload;

                                byte[] bytes = new byte[payloadLength];
                                Marshal.Copy(IntPtr.Add(pBuffer, dataOffset + 8), bytes, 0, payloadLength);

                                VendorHidReportReceived?.Invoke(bytes);

                                uint messageVal = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0;
                                ushort makeCodeVal = (ushort)(dwSizeHid & 0xFFFF);
                                ushort vkeyVal = (ushort)(dwCount & 0xFFFF);

                                var args = new RawInputEventArgs(header.dwType, bytes, makeCodeVal, vkeyVal, messageVal);
                                RawInputReceived?.Invoke(this, args);

                                // Gigabyte ITE Hotkey pattern: bytes[0] == 4 && bytes[1] == 0
                                if (bytes.Length >= 4 && bytes[0] == 4 && bytes[1] == 0)
                                {
                                    byte code = bytes[3];
                                    long now = Environment.TickCount64;

                                    switch (code)
                                    {
                                        case 145: // 0x91: GiMATE Hardware Button
                                            if (now - _lastGimateTick > 300)
                                            {
                                                _lastGimateTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => GimateHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 146: // 0x92: Microphone Mute Toggle (Fn + F4)
                                            if (now - _lastMicMuteTick > 300)
                                            {
                                                _lastMicMuteTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => MicMuteHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 132: // 0x84: Fan Profile / Performance Cycle (Fn + F7)
                                            if (now - _lastPerfTick > 300)
                                            {
                                                _lastPerfTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => PerformanceHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 129: // 0x81: Touchpad Toggle (Fn + F9)
                                            if (now - _lastTouchpadTick > 300)
                                            {
                                                _lastTouchpadTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => TouchpadHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 148: // 0x94: WinLock Toggle (Fn + Win)
                                            if (now - _lastWinLockTick > 300)
                                            {
                                                _lastWinLockTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => WinLockHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 149: // 0x95: FnLock Toggle (Fn + Esc)
                                            if (now - _lastFnLockTick > 300)
                                            {
                                                _lastFnLockTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => FnLockHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 150: // 0x96 or 154 (0x9A): Backlight cycle
                                        case 154:
                                            if (now - _lastBacklightTick > 300)
                                            {
                                                _lastBacklightTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => BacklightHotkeyPressed?.Invoke());
                                            }
                                            break;

                                        case 130: // 0x82 / 0x85: Airplane mode
                                        case 133:
                                        case 138:
                                            if (now - _lastAirplaneTick > 300)
                                            {
                                                _lastAirplaneTick = now;
                                                ThreadPool.QueueUserWorkItem(_ => AirplaneHotkeyPressed?.Invoke());
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                    }
                    else if (header.dwType == RIM_TYPEKEYBOARD)
                    {
                        int minKeyboardSize = dataOffset + Marshal.SizeOf<RAWINPUTKEYBOARD>();
                        if (size >= minKeyboardSize)
                        {
                            IntPtr kbPtr = IntPtr.Add(pBuffer, dataOffset);
                            var kb = Marshal.PtrToStructure<RAWINPUTKEYBOARD>(kbPtr);

                            byte[] data = BitConverter.GetBytes(kb.Message);
                            var args = new RawInputEventArgs(header.dwType, data, kb.MakeCode, kb.VKey, kb.Message);
                            RawInputReceived?.Invoke(this, args);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RawInputReceiver] Error processing WM_INPUT: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_isStarted) return;

        if (_msgThreadId != 0)
        {
            PostThreadMessage(_msgThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _msgThread?.Join(500);
            _msgThread = null;
            _msgThreadId = 0;
        }
        _isStarted = false;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
