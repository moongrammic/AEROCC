using AEROCC.Core.Display;
using AEROCC.Core.Hid;
using AEROCC.Core.Ipc;
using AEROCC.Core.Models;
using AEROCC.Core.Wmi;

namespace AEROCC.App.Services;

public class HardwareMonitor : IDisposable
{
    private static HardwareMonitor? _instance;
    public static HardwareMonitor Instance => _instance ??= new HardwareMonitor();

    private readonly AeroccIpcClient _ipcClient = new();
    private readonly GigabyteWmiClient _directClient = new();
    private CancellationTokenSource? _cts;
    private bool _isFirstPoll = true;
    private int _probeCounter = 0;

    private bool? _prevWinLock;
    private bool? _prevFnLock;
    private int _prevRefreshRate;
    private PowerLineStatus _prevPowerStatus = PowerLineStatus.Unknown;

    public HardwareStatus LatestStatus { get; private set; } = new();
    public bool IsIpcConnected => _ipcClient.IsServiceAvailable;
    public bool AutoRefreshRateEnabled { get; set; } = false;

    public event Action<HardwareStatus>? StatusUpdated;

    public void Start()
    {
        if (_cts != null) return;

        // Ensure direct keyboard HID is initialized
        _directClient.Keyboard.Initialize();

        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                HardwareStatus? status = null;

                // Only query IPC if service is active or probe every ~5 seconds
                bool shouldProbeIpc = _ipcClient.IsServiceAvailable || (_probeCounter++ % 7 == 0);

                if (shouldProbeIpc)
                {
                    var ipcResp = await _ipcClient.GetStatusAsync(ct);
                    if (ipcResp.Success && ipcResp.Status != null)
                    {
                        status = ipcResp.Status;
                    }
                }

                // Fallback to direct WMI/HID query if IPC is offline or failed
                if (status == null)
                {
                    status = _directClient.GetHardwareStatus();
                }

                if (status != null)
                {
                    LatestStatus = status;

                    // Detect physical hotkey or external changes to trigger OSD
                    if (!_isFirstPoll)
                    {
                        if (status.WinKeyLocked.HasValue && status.WinKeyLocked != _prevWinLock)
                        {
                            OsdService.Instance.ShowWinLock(status.WinKeyLocked.Value);
                        }

                        if (status.FnKeyLocked.HasValue && status.FnKeyLocked != _prevFnLock)
                        {
                            OsdService.Instance.ShowFnLock(status.FnKeyLocked.Value);
                        }

                        if (status.DisplayRefreshRate > 0 && status.DisplayRefreshRate != _prevRefreshRate)
                        {
                            OsdService.Instance.ShowRefreshRate(status.DisplayRefreshRate);
                        }
                    }

                    _prevWinLock = status.WinKeyLocked;
                    _prevFnLock = status.FnKeyLocked;
                    _prevRefreshRate = status.DisplayRefreshRate;

                    // Handle Auto-Refresh Rate on AC / Battery change
                    var currentPower = PowerManager.GetPowerInfo().LineStatus;
                    if (AutoRefreshRateEnabled && currentPower != _prevPowerStatus)
                    {
                        if (currentPower == PowerLineStatus.Offline && status.DisplayRefreshRate != 60)
                        {
                            await SetRefreshRateAsync(60);
                        }
                        else if (currentPower == PowerLineStatus.Online && status.DisplayRefreshRate != 165)
                        {
                            await SetRefreshRateAsync(165);
                        }
                    }
                    _prevPowerStatus = currentPower;

                    _isFirstPoll = false;
                    StatusUpdated?.Invoke(status);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HardwareMonitor Poll Error]: {ex.Message}");
            }

            try
            {
                await Task.Delay(650, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<bool> SetBatteryLimitAsync(int percent)
    {
        OsdService.Instance.ShowBatteryLimit(percent);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBatteryLimitAsync(percent);
            if (resp.Success) return true;
        }

        return _directClient.SetBatteryChargeLimit(percent);
    }

    public async Task<bool> SetFanProfileAsync(FanProfile profile)
    {
        OsdService.Instance.ShowFanProfile(profile);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetFanProfileAsync(profile);
            if (resp.Success) return true;
        }

        return _directClient.SetFanProfile(profile);
    }

    public async Task<bool> SetWinLockAsync(bool locked)
    {
        _prevWinLock = locked;
        OsdService.Instance.ShowWinLock(locked);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetWinLockAsync(locked);
            if (resp.Success) return true;
        }

        return _directClient.SetWinKeyLock(locked);
    }

    public async Task<bool> SetFnLockAsync(bool locked)
    {
        _prevFnLock = locked;
        OsdService.Instance.ShowFnLock(locked);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetFnLockAsync(locked);
            if (resp.Success) return true;
        }

        return _directClient.SetFnKeyLock(locked);
    }

    public async Task<bool> SetRefreshRateAsync(int hz)
    {
        _prevRefreshRate = hz;
        OsdService.Instance.ShowRefreshRate(hz);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetRefreshRateAsync(hz);
            if (resp.Success) return true;
        }

        return DisplayManager.SetRefreshRate(hz);
    }

    public async Task<bool> SetKeyboardBrightnessAsync(byte brightness)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightBrightnessAsync(brightness);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBrightness(brightness);
    }

    public async Task<bool> SetKeyboardColorAsync(KeyboardColor color)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightColorAsync(color);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBacklight(KeyboardEffect.Static, 100, color);
    }

    public async Task<bool> SetKeyboardEffectAsync(KeyboardEffect effect)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightEffectAsync(effect);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBacklight(effect, 100);
    }

    public async Task<bool> SetCustomRgbAsync(byte r, byte g, byte b, byte brightness = 100)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetCustomRgbAsync(r, g, b, brightness);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetCustomColor(r, g, b, brightness);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
