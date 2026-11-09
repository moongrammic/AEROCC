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

    private bool? _prevWinLock;
    private bool? _prevFnLock;
    private int _prevRefreshRate;
    private PowerLineStatus _prevPowerStatus = PowerLineStatus.Unknown;

    public HardwareStatus LatestStatus { get; private set; } = new();
    public bool IsIpcConnected { get; private set; }
    public bool AutoRefreshRateEnabled { get; set; } = false;

    public event Action<HardwareStatus>? StatusUpdated;

    public void Start()
    {
        if (_cts != null) return;

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

                // 1. Try reading from background service over IPC
                var ipcResp = await _ipcClient.GetStatusAsync(ct);
                if (ipcResp.Success && ipcResp.Status != null)
                {
                    status = ipcResp.Status;
                    IsIpcConnected = true;
                }
                else
                {
                    // 2. Fallback to direct WMI/HID query
                    IsIpcConnected = false;
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
                            // On battery -> 60Hz
                            await SetRefreshRateAsync(60);
                        }
                        else if (currentPower == PowerLineStatus.Online && status.DisplayRefreshRate != 165)
                        {
                            // On AC -> 165Hz
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
        var resp = await _ipcClient.SetBatteryLimitAsync(percent);
        if (resp.Success)
        {
            OsdService.Instance.ShowBatteryLimit(percent);
            return true;
        }

        bool ok = _directClient.SetBatteryChargeLimit(percent);
        if (ok) OsdService.Instance.ShowBatteryLimit(percent);
        return ok;
    }

    public async Task<bool> SetFanProfileAsync(FanProfile profile)
    {
        var resp = await _ipcClient.SetFanProfileAsync(profile);
        if (resp.Success)
        {
            OsdService.Instance.ShowFanProfile(profile);
            return true;
        }

        bool ok = _directClient.SetFanProfile(profile);
        if (ok) OsdService.Instance.ShowFanProfile(profile);
        return ok;
    }

    public async Task<bool> SetWinLockAsync(bool locked)
    {
        var resp = await _ipcClient.SetWinLockAsync(locked);
        if (resp.Success)
        {
            _prevWinLock = locked;
            OsdService.Instance.ShowWinLock(locked);
            return true;
        }

        bool ok = _directClient.SetWinKeyLock(locked);
        if (ok)
        {
            _prevWinLock = locked;
            OsdService.Instance.ShowWinLock(locked);
        }
        return ok;
    }

    public async Task<bool> SetFnLockAsync(bool locked)
    {
        var resp = await _ipcClient.SetFnLockAsync(locked);
        if (resp.Success)
        {
            _prevFnLock = locked;
            OsdService.Instance.ShowFnLock(locked);
            return true;
        }

        bool ok = _directClient.SetFnKeyLock(locked);
        if (ok)
        {
            _prevFnLock = locked;
            OsdService.Instance.ShowFnLock(locked);
        }
        return ok;
    }

    public async Task<bool> SetRefreshRateAsync(int hz)
    {
        var resp = await _ipcClient.SetRefreshRateAsync(hz);
        if (resp.Success)
        {
            _prevRefreshRate = hz;
            OsdService.Instance.ShowRefreshRate(hz);
            return true;
        }

        bool ok = DisplayManager.SetRefreshRate(hz);
        if (ok)
        {
            _prevRefreshRate = hz;
            OsdService.Instance.ShowRefreshRate(hz);
        }
        return ok;
    }

    public async Task<bool> SetKeyboardBrightnessAsync(byte brightness)
    {
        var resp = await _ipcClient.SetBacklightBrightnessAsync(brightness);
        return resp.Success || _directClient.Keyboard.SetBrightness(brightness);
    }

    public async Task<bool> SetKeyboardColorAsync(KeyboardColor color)
    {
        var resp = await _ipcClient.SetBacklightColorAsync(color);
        return resp.Success || _directClient.Keyboard.SetBacklight(KeyboardEffect.Static, 100, color);
    }

    public async Task<bool> SetKeyboardEffectAsync(KeyboardEffect effect)
    {
        var resp = await _ipcClient.SetBacklightEffectAsync(effect);
        return resp.Success || _directClient.Keyboard.SetBacklight(effect, 100);
    }

    public async Task<bool> SetCustomRgbAsync(byte r, byte g, byte b, byte brightness = 100)
    {
        var resp = await _ipcClient.SetCustomRgbAsync(r, g, b, brightness);
        return resp.Success || _directClient.Keyboard.SetCustomColor(r, g, b, brightness);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
