using AEROCC.Core.Audio;
using AEROCC.Core.Events;
using AEROCC.Core.Models;
using AEROCC.Core.Radio;
using AEROCC.Core.Touchpad;

namespace AEROCC.Core.Hotkeys;

public class HotkeyManager : IDisposable
{
    private static HotkeyManager? _instance;
    public static HotkeyManager Instance => _instance ??= new HotkeyManager();

    private readonly GlobalHotkeyHook _hook = new();
    private readonly RawInputReceiver _rawInput = RawInputReceiver.Instance;
    private readonly WmiEventListener _wmiListener = WmiEventListener.Instance;
    private readonly MicrophoneManager _micManager = MicrophoneManager.Instance;
    private readonly TouchpadManager _touchpadManager = TouchpadManager.Instance;
    private readonly AirplaneModeManager _airplaneManager = AirplaneModeManager.Instance;

    private bool _isStarted = false;
    private bool _isDisposed = false;

    // Centralized Debounce Ticks (prevents dual-fire when both GlobalHook and RawInput detect the same keypress)
    private long _lastMicTriggerTick = 0;
    private long _lastPerfTriggerTick = 0;
    private long _lastTouchpadTriggerTick = 0;
    private long _lastAirplaneTriggerTick = 0;
    private long _lastBacklightTriggerTick = 0;
    private long _lastGimateTriggerTick = 0;
    private long _lastFnLockTriggerTick = 0;

    // Events for App / UI
    public event Action? GimateTriggered;
    public event Action? PerformanceCycleTriggered;
    public event Action<bool>? MicMuteTriggered;
    public event Action<bool>? TouchpadTriggered;
    public event Action<bool>? AirplaneModeTriggered;
    public event Action? BacklightCycleTriggered;
    public event Action<bool>? FnLockTriggered;
    public event Action<bool>? WinLockTriggered;

    public bool IsFnLockActive
    {
        get => _hook.IsFnLockActive;
        set => _hook.IsFnLockActive = value;
    }

    public GlobalHotkeyHook Hook => _hook;
    public RawInputReceiver RawInput => _rawInput;
    public WmiEventListener WmiListener => _wmiListener;

    public void Start()
    {
        if (_isStarted) return;

        // 1. Hook keyboard events (runs on dedicated high-priority background thread, 0ms latency)
        _hook.GimatePressed += OnGimatePressed;
        _hook.TouchpadTogglePressed += OnTouchpadTogglePressed;
        _hook.MicMuteTogglePressed += OnMicMuteTogglePressed;
        _hook.PerformanceTogglePressed += OnPerformanceTogglePressed;
        _hook.AirplaneTogglePressed += OnAirplaneTogglePressed;
        _hook.BacklightTogglePressed += OnBacklightTogglePressed;

        _hook.Install();

        // 2. Start Raw Input Receiver (listens for Gigabyte ITE EC HID reports 0xFF02 / 1)
        _rawInput.GimateHotkeyPressed += OnGimatePressed;
        _rawInput.MicMuteHotkeyPressed += OnMicMuteTogglePressed;
        _rawInput.PerformanceHotkeyPressed += OnPerformanceTogglePressed;
        _rawInput.TouchpadHotkeyPressed += OnTouchpadTogglePressed;
        _rawInput.BacklightHotkeyPressed += OnBacklightTogglePressed;
        _rawInput.AirplaneHotkeyPressed += OnAirplaneTogglePressed;
        _rawInput.FnLockHotkeyPressed += OnRawFnLockPressed;
        _rawInput.Start();

        // 3. Hook WMI ACPI events
        _wmiListener.FnLockChanged += OnWmiFnLockChanged;
        _wmiListener.TouchpadChanged += OnWmiTouchpadChanged;
        _wmiListener.WinLockChanged += OnWmiWinLockChanged;
        _wmiListener.AirplaneModeChanged += OnWmiAirplaneModeChanged;
        _wmiListener.RawEventArrived += OnWmiRawEventArrived;

        _wmiListener.Start();

        _isStarted = true;
    }

    private void OnGimatePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastGimateTriggerTick < 350) return;
        _lastGimateTriggerTick = now;

        GimateTriggered?.Invoke();
    }

    private void OnPerformanceTogglePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastPerfTriggerTick < 350) return;
        _lastPerfTriggerTick = now;

        PerformanceCycleTriggered?.Invoke();
    }

    private void OnMicMuteTogglePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastMicTriggerTick < 350) return;
        _lastMicTriggerTick = now;

        bool newMute = _micManager.ToggleMute();
        MicMuteTriggered?.Invoke(newMute);
    }

    private void OnTouchpadTogglePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastTouchpadTriggerTick < 350) return;
        _lastTouchpadTriggerTick = now;

        Task.Delay(60).ContinueWith(_ =>
        {
            bool isEnabled = _touchpadManager.IsTouchpadEnabled();
            TouchpadTriggered?.Invoke(isEnabled);
        });
    }

    private void OnAirplaneTogglePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastAirplaneTriggerTick < 350) return;
        _lastAirplaneTriggerTick = now;

        Task.Delay(60).ContinueWith(_ =>
        {
            bool isActive = _airplaneManager.IsAirplaneModeActive();
            AirplaneModeTriggered?.Invoke(isActive);
        });
    }

    private void OnBacklightTogglePressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastBacklightTriggerTick < 350) return;
        _lastBacklightTriggerTick = now;

        BacklightCycleTriggered?.Invoke();
    }

    private void OnRawFnLockPressed()
    {
        long now = Environment.TickCount64;
        if (now - _lastFnLockTriggerTick < 350) return;
        _lastFnLockTriggerTick = now;

        IsFnLockActive = !IsFnLockActive;
        FnLockTriggered?.Invoke(IsFnLockActive);
    }

    private void OnWmiFnLockChanged(bool locked)
    {
        _hook.IsFnLockActive = locked;
        FnLockTriggered?.Invoke(locked);
    }

    private void OnWmiTouchpadChanged(bool enabled)
    {
        TouchpadTriggered?.Invoke(enabled);
    }

    private void OnWmiWinLockChanged(bool locked)
    {
        WinLockTriggered?.Invoke(locked);
    }

    private void OnWmiAirplaneModeChanged(bool active)
    {
        AirplaneModeTriggered?.Invoke(active);
    }

    private void OnWmiRawEventArrived(byte[] data)
    {
        // Check for hardware events reported via WMI
    }

    public static FanProfile GetNextFanProfile(FanProfile current) => current switch
    {
        FanProfile.Eco => FanProfile.Normal,
        FanProfile.Normal => FanProfile.Game,
        FanProfile.Game => FanProfile.Turbo,
        FanProfile.Turbo => FanProfile.Eco,
        _ => FanProfile.Normal
    };

    public static byte GetNextBacklightBrightness(byte current)
    {
        if (current == 0) return 25;
        if (current <= 25) return 50;
        if (current <= 50) return 75;
        if (current <= 75) return 100;
        return 0;
    }

    public void Stop()
    {
        if (!_isStarted) return;

        _hook.GimatePressed -= OnGimatePressed;
        _hook.TouchpadTogglePressed -= OnTouchpadTogglePressed;
        _hook.MicMuteTogglePressed -= OnMicMuteTogglePressed;
        _hook.PerformanceTogglePressed -= OnPerformanceTogglePressed;
        _hook.AirplaneTogglePressed -= OnAirplaneTogglePressed;
        _hook.BacklightTogglePressed -= OnBacklightTogglePressed;

        _rawInput.GimateHotkeyPressed -= OnGimatePressed;
        _rawInput.MicMuteHotkeyPressed -= OnMicMuteTogglePressed;
        _rawInput.PerformanceHotkeyPressed -= OnPerformanceTogglePressed;
        _rawInput.TouchpadHotkeyPressed -= OnTouchpadTogglePressed;
        _rawInput.BacklightHotkeyPressed -= OnBacklightTogglePressed;
        _rawInput.AirplaneHotkeyPressed -= OnAirplaneTogglePressed;
        _rawInput.FnLockHotkeyPressed -= OnRawFnLockPressed;

        _wmiListener.FnLockChanged -= OnWmiFnLockChanged;
        _wmiListener.TouchpadChanged -= OnWmiTouchpadChanged;
        _wmiListener.WinLockChanged -= OnWmiWinLockChanged;
        _wmiListener.AirplaneModeChanged -= OnWmiAirplaneModeChanged;
        _wmiListener.RawEventArrived -= OnWmiRawEventArrived;

        _hook.Uninstall();
        _rawInput.Stop();
        _wmiListener.Stop();
        _isStarted = false;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _hook.Dispose();
            _rawInput.Dispose();
            _wmiListener.Dispose();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
