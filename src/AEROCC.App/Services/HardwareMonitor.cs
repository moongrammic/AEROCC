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
    private readonly SemaphoreSlim _fanProfileWrites = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _isFirstPoll = true;
    private int _probeCounter = 0;

    private bool? _prevWinLock;
    private bool? _prevFnLock;
    private int _prevRefreshRate;
    private int _prevBacklight = -1;
    private PowerLineStatus _prevPowerStatus = PowerLineStatus.Unknown;
    private FanProfile _currentFanProfile = FanProfile.Normal;
    private FanProfile? _activeEffectiveEcTier;
    private byte? _activeFixedSpeedPercent;
    private string? _activeCurvePresetName;
    private DateTime _lastEcTierChangeUtc = DateTime.MinValue;
    private bool _isDynamicCurveActive = false;
    private List<(byte Temp, byte Duty)> _activeCurvePoints = new();

    public HardwareStatus LatestStatus { get; private set; } = new();
    private (double FPptWatts, double SPptWatts, double SplWatts)? _customApuLimits;
    public bool IsIpcConnected => _ipcClient.IsServiceAvailable;
    public bool AutoRefreshRateEnabled { get; set; } = false;
    public FanProfile CurrentFanProfile => _currentFanProfile;
    public FanProfile? ActiveEffectiveEcTier => _activeEffectiveEcTier;
    public bool FanProfileWriteFailed { get; private set; }
    public byte? ActiveFixedSpeedPercent => _activeFixedSpeedPercent;
    public string? ActiveCurvePresetName => _activeCurvePresetName;
    public bool IsDynamicCurveActive => _isDynamicCurveActive;
    public IReadOnlyList<(byte Temp, byte Duty)> ActiveCurvePoints => _activeCurvePoints;

    /// <summary>
    /// Formats a consistent status label describing the active manual/curve/fixed mode and mapped EC tier.
    /// </summary>
    public string GetManualStatusLabel(bool useArrowForFixed = false)
    {
        if (_activeEffectiveEcTier is null)
            return FanProfileWriteFailed ? "Ошибка записи • ступень EC неизвестна" : "Ступень EC неизвестна";

        if (_isDynamicCurveActive)
            return $"Кривая • EC: {_activeEffectiveEcTier}";
        if (_activeFixedSpeedPercent.HasValue)
        {
            string sep = useArrowForFixed ? "→" : "•";
            return $"Фикс {_activeFixedSpeedPercent}% {sep} EC: {_activeEffectiveEcTier}";
        }
        return $"Ручной • EC: {_activeEffectiveEcTier}";
    }

    public event Action<HardwareStatus>? StatusUpdated;
    public event Action<FanProfile>? FanProfileChanged;
    public event Action<FanProfile?>? EffectiveEcTierChanged;

    public void Start()
    {
        if (_cts != null) return;

        // Ensure direct keyboard HID is initialized
        _directClient.Keyboard.Initialize();

        // Seed Windows Kernel EMI baseline so CPU PKG watts are available immediately
        try { PowerManager.GetEnergyTelemetry(); } catch { }

        // Dump WMI ACPI methods to log file on startup
        Task.Run(() => _directClient.DumpWmiMethods());

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _cts = new CancellationTokenSource();
        Task.Run(() => PollLoopAsync(_cts.Token));
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
                    int timeout = _ipcClient.IsServiceAvailable ? 800 : 120;
                    var ipcResp = await _ipcClient.SendCommandAsync(
                        new IpcMessage { Command = IpcConstants.CmdGetStatus },
                        timeoutMs: timeout,
                        ct: ct).ConfigureAwait(false);

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
                    if (!status.CpuPowerWatts.HasValue)
                    {
                        try
                        {
                            var energy = PowerManager.GetEnergyTelemetry();
                            status = status with
                            {
                                CpuPowerWatts = energy.CpuPackageWatts,
                                BatteryChargeWatts = status.BatteryChargeWatts ?? energy.BatteryChargeWatts,
                                BatteryDischargeWatts = status.BatteryDischargeWatts ?? energy.BatteryDischargeWatts,
                                IsAcOnline = energy.IsAcOnline
                            };
                        }
                        catch { }
                    }

                    status = status with
                    {
                        ActiveFanProfile = _currentFanProfile,
                        IsEnergySaverEnabled = PowerManager.GetEnergySaverEnabled()
                    };

                    // If status comes from Session 0 Service where desktop displays cannot be queried, populate local display
                    if (status.DisplayRefreshRate == 0)
                    {
                        var localDisp = DisplayManager.GetPrimaryDisplay();
                        if (localDisp != null)
                        {
                            status = status with
                            {
                                DisplayRefreshRate = localDisp.CurrentFrequency,
                                DisplayResolution = $"{localDisp.CurrentWidth}x{localDisp.CurrentHeight}",
                                AvailableRefreshRates = localDisp.AvailableFrequencies
                            };
                        }
                    }

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
                            AEROCC.Core.Hotkeys.HotkeyManager.Instance.IsFnLockActive = status.FnKeyLocked.Value;
                            OsdService.Instance.ShowFnLock(status.FnKeyLocked.Value);
                        }

                        if (status.DisplayRefreshRate > 0 && status.DisplayRefreshRate != _prevRefreshRate)
                        {
                            OsdService.Instance.ShowRefreshRate(status.DisplayRefreshRate);
                        }

                        if (status.BacklightBrightness >= 0 && _prevBacklight != -1 && status.BacklightBrightness != _prevBacklight)
                        {
                            OsdService.Instance.ShowBacklight(status.BacklightBrightness);
                        }
                    }
                    else if (status.FnKeyLocked.HasValue)
                    {
                        AEROCC.Core.Hotkeys.HotkeyManager.Instance.IsFnLockActive = status.FnKeyLocked.Value;
                    }

                    _prevWinLock = status.WinKeyLocked;
                    _prevFnLock = status.FnKeyLocked;
                    _prevRefreshRate = status.DisplayRefreshRate;
                    _prevBacklight = status.BacklightBrightness;

                    // Handle Auto-Refresh Rate on AC / Battery change
                    var currentPower = PowerManager.GetPowerInfo().LineStatus;
                    if (AutoRefreshRateEnabled && _prevPowerStatus != PowerLineStatus.Unknown && currentPower != _prevPowerStatus)
                    {
                        int maxHz = status.AvailableRefreshRates.Count > 0 ? status.AvailableRefreshRates.Max() : 165;
                        if (currentPower == PowerLineStatus.Offline && status.DisplayRefreshRate != 60)
                        {
                            await SetRefreshRateAsync(60).ConfigureAwait(false);
                        }
                        else if (currentPower == PowerLineStatus.Online && status.DisplayRefreshRate != maxHz)
                        {
                            await SetRefreshRateAsync(maxHz).ConfigureAwait(false);
                        }
                    }
                    _prevPowerStatus = currentPower;

                    await EvaluateDynamicFanCurveAsync(status).ConfigureAwait(false);

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
                await Task.Delay(650, ct).ConfigureAwait(false);
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

    public Task<bool> SetFanProfileAsync(FanProfile profile) => ApplyFanSelectionAsync(profile);

    public Task<bool> ApplyFanCurveAsync(IList<(byte Temp, byte Duty)> points, string? presetName = null) =>
        ApplyFanSelectionAsync(FanProfile.Manual, points: points.OrderBy(p => p.Temp).ToList(), presetName: presetName);

    public Task<bool> SetFixedFanSpeedAsync(byte speedPercent) =>
        ApplyFanSelectionAsync(FanProfile.Manual, fixedSpeed: speedPercent);

    private async Task<bool> ApplyFanSelectionAsync(
        FanProfile profile, byte? fixedSpeed = null, List<(byte Temp, byte Duty)>? points = null, string? presetName = null)
    {
        await _fanProfileWrites.WaitAsync();
        try
        {
            if (_disposed)
                return false;

            if (profile == FanProfile.Manual && fixedSpeed is null && points is null)
            {
                fixedSpeed = _activeFixedSpeedPercent;
                if (fixedSpeed is null && _activeCurvePoints.Count == 8)
                {
                    points = _activeCurvePoints;
                    presetName = _activeCurvePresetName;
                }
            }

            FanProfile tier = profile == FanProfile.Manual ? FanProfile.Normal : profile;
            if (fixedSpeed is byte speed)
            {
                tier = GigabyteWmiClient.MapFixedPercentToEcTier(speed);
            }
            else if (points is not null)
            {
                var status = LatestStatus;
                var temperature = status.HasFreshThermalSample() ? status.CpuTemperature : null;
                temperature ??= _directClient.GetCpuTemperature();
                if (temperature is not int cpuTemp || cpuTemp <= 0)
                    return false;

                tier = GigabyteWmiClient.MapCurveDutyToEcTier(GigabyteWmiClient.InterpolateCurveDuty(points, cpuTemp));
            }

            if (!await SendFanProfileAndRestoreApuAsync(tier))
            {
                PublishFanWriteResult(null);
                return false;
            }

            _currentFanProfile = profile;
            _activeFixedSpeedPercent = fixedSpeed;
            _activeCurvePresetName = presetName;
            _isDynamicCurveActive = points is not null;
            if (points is not null)
                _activeCurvePoints = points;
            else if (fixedSpeed is byte percent)
                _activeCurvePoints = [(30, percent), (40, percent), (50, percent), (60, percent),
                                      (70, percent), (80, percent), (90, percent), (95, percent)];

            PublishFanWriteResult(tier);
            FanProfileChanged?.Invoke(profile);
            OsdService.Instance.ShowFanProfile(profile);
            return true;
        }
        finally
        {
            _fanProfileWrites.Release();
        }
    }

    private async Task EvaluateDynamicFanCurveAsync(HardwareStatus status)
    {
        if (!_isDynamicCurveActive || !_fanProfileWrites.Wait(0))
            return;

        try
        {
            if (_disposed || !_isDynamicCurveActive || _activeCurvePoints.Count < 2)
                return;

            var temperature = status.ThermalSampledAtUtc is null
                ? _directClient.GetCpuTemperature()
                : status.HasFreshThermalSample() ? status.CpuTemperature : null;
            if (temperature is not int cpuTemp || cpuTemp <= 0)
                return;

            double duty = GigabyteWmiClient.InterpolateCurveDuty(_activeCurvePoints, cpuTemp);
            FanProfile targetTier = GigabyteWmiClient.MapCurveDutyToEcTier(duty);
            if (targetTier == _activeEffectiveEcTier)
                return;

            if (_activeEffectiveEcTier is { } currentTier && GetTierRank(targetTier) < GetTierRank(currentTier))
            {
                // Down-shift hysteresis: hold higher cooling tier >= 4s and until +3°C margin also falls below threshold.
                if ((DateTime.UtcNow - _lastEcTierChangeUtc).TotalSeconds < 4.0)
                    return;

                double dutyWithMargin = GigabyteWmiClient.InterpolateCurveDuty(_activeCurvePoints, cpuTemp + 3.0);
                if (GetTierRank(GigabyteWmiClient.MapCurveDutyToEcTier(dutyWithMargin)) >= GetTierRank(currentTier))
                    return;
            }

            PublishFanWriteResult(await SendFanProfileAndRestoreApuAsync(targetTier) ? targetTier : null);
        }
        finally
        {
            _fanProfileWrites.Release();
        }
    }

    private void PublishFanWriteResult(FanProfile? tier)
    {
        _activeEffectiveEcTier = tier;
        FanProfileWriteFailed = !tier.HasValue;
        if (tier.HasValue)
            _lastEcTierChangeUtc = DateTime.UtcNow;
        else
            _isDynamicCurveActive = false;
        EffectiveEcTierChanged?.Invoke(tier);
    }

    private async Task<bool> SendFanProfileAndRestoreApuAsync(FanProfile tier)
    {
        bool ok = false;
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetFanProfileAsync(tier);
            ok = resp.Success;
        }

        else
        {
            ok = await Task.Run(() => _directClient.SetFanProfile(tier));
        }

        if (ok && _customApuLimits is { } limits)
        {
            // ACPI 0xED (SetFanProfile) resets ECPT power tables; restore user's custom APU limits afterward
            await Task.Delay(50);
            if (!await ApplyApuPowerRawAsync(limits.FPptWatts, limits.SPptWatts, limits.SplWatts))
                GigabyteWmiClient.LogDebug("[Fan profile] Failed to restore the configured APU power limits.");
        }

        return ok;
    }

    private static int GetTierRank(FanProfile tier) => tier switch
    {
        FanProfile.Eco => 0,
        FanProfile.Normal => 1,
        FanProfile.Game => 2,
        FanProfile.Turbo => 3,
        _ => 1
    };

    public async Task<FanProfile> CycleFanProfileAsync()
    {
        var next = AEROCC.Core.Hotkeys.HotkeyManager.GetNextFanProfile(_currentFanProfile);
        return await SetFanProfileAsync(next) ? next : _currentFanProfile;
    }

    public async Task<byte> CycleBacklightBrightnessAsync()
    {
        byte current = (byte)Math.Clamp(LatestStatus.BacklightBrightness, 0, 100);
        byte next = AEROCC.Core.Hotkeys.HotkeyManager.GetNextBacklightBrightness(current);
        _prevBacklight = next;
        await SetKeyboardBrightnessAsync(next);
        OsdService.Instance.ShowBacklight(next);
        return next;
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
        _prevBacklight = brightness;
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightBrightnessAsync(brightness);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBrightness(brightness);
    }

    public async Task<bool> SetKeyboardColorAsync(KeyboardColor color)
    {
        byte curBrightness = LatestStatus.BacklightBrightness > 0 ? (byte)LatestStatus.BacklightBrightness : _directClient.Keyboard.LastBrightness;
        if (curBrightness == 0) curBrightness = 100;

        KeyboardEffect effect = color == KeyboardColor.Random ? KeyboardEffect.Wave : KeyboardEffect.Static;

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightColorAsync(color);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBacklight(effect, curBrightness, color);
    }

    public async Task<bool> SetKeyboardEffectAsync(KeyboardEffect effect, KeyboardColor? color = null)
    {
        byte curBrightness = LatestStatus.BacklightBrightness > 0 ? (byte)LatestStatus.BacklightBrightness : _directClient.Keyboard.LastBrightness;
        if (curBrightness == 0) curBrightness = 100;

        KeyboardColor clr = color ?? ((effect == KeyboardEffect.Wave || effect == KeyboardEffect.Neon) ? KeyboardColor.Random : _directClient.Keyboard.LastColor);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightFullAsync(effect, curBrightness, clr, speed: 5, direction: 1);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBacklight(effect, curBrightness, clr);
    }

    public async Task<bool> SetRainbowBacklightAsync(KeyboardEffect effect = KeyboardEffect.Wave)
    {
        return await SetKeyboardEffectAsync(effect, KeyboardColor.Random);
    }

    public async Task<bool> SetBacklightFullAsync(KeyboardEffect effect, byte brightness, KeyboardColor color, byte speed, byte direction)
    {
        _prevBacklight = brightness;
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetBacklightFullAsync(effect, brightness, color, speed, direction);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetBacklight(effect, brightness, color, speed, direction);
    }

    public async Task<bool> SetApuPowerAsync(double fPptWatts, double sPptWatts, double splWatts)
    {
        bool ok = await ApplyApuPowerRawAsync(fPptWatts, sPptWatts, splWatts);
        if (ok)
        {
            _customApuLimits = (fPptWatts, sPptWatts, splWatts);
        }

        return ok;
    }

    private async Task<bool> ApplyApuPowerRawAsync(double fPptWatts, double sPptWatts, double splWatts)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var resp = await _ipcClient.SendCommandAsync(new IpcMessage
            {
                Command = IpcConstants.CmdSetApuPower,
                Payload = $"{fPptWatts.ToString(culture)},{sPptWatts.ToString(culture)},{splWatts.ToString(culture)}"
            });
            if (resp.Success) return true;
        }

        return await Task.Run(() => _directClient.SetApuPower(fPptWatts, sPptWatts, splWatts));
    }

    public KeyboardBacklightStatus? GetBacklightStatus() => _directClient.Keyboard.GetBacklightStatus();

    public async Task<bool> SetCustomRgbAsync(byte r, byte g, byte b, byte brightness = 100)
    {
        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetCustomRgbAsync(r, g, b, brightness);
            if (resp.Success) return true;
        }

        return _directClient.Keyboard.SetCustomColor(r, g, b, brightness);
    }

    public async Task<bool> SetGpuPowerModeAsync(AEROCC.Core.Gpu.GpuPowerMode mode)
    {
        OsdService.Instance.ShowGpuMode(mode);

        if (_ipcClient.IsServiceAvailable)
        {
            var resp = await _ipcClient.SetGpuPowerModeAsync(mode);
            if (resp.Success) return true;
        }

        return _directClient.SetGpuPowerMode(mode);
    }

    public async Task<bool> SetEnergySaverAsync(bool enabled)
    {
        bool ok = PowerManager.SetEnergySaverEnabled(enabled);
        if (ok)
        {
            await Task.Delay(50);
            bool actual = PowerManager.GetEnergySaverEnabled();
            var updated = LatestStatus with { IsEnergySaverEnabled = actual };
            LatestStatus = updated;
            StatusUpdated?.Invoke(updated);
        }

        return ok;
    }

    private bool _disposed = false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        try { _cts?.Cancel(); } catch { }
        if (_isDynamicCurveActive || (_currentFanProfile == FanProfile.Manual && _activeEffectiveEcTier == FanProfile.Turbo))
        {
            try { _directClient.SetFanProfile(FanProfile.Normal); } catch { }
        }
        _cts?.Dispose();
        _directClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            try
            {
                // Wait for EC and ACPI to re-initialize after resume
                await Task.Delay(2000).ConfigureAwait(false);

                if (_isDynamicCurveActive && _activeEffectiveEcTier.HasValue)
                {
                    await _fanProfileWrites.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        PublishFanWriteResult(await SendFanProfileAndRestoreApuAsync(_activeEffectiveEcTier.Value).ConfigureAwait(false) ? _activeEffectiveEcTier.Value : null);
                    }
                    finally
                    {
                        _fanProfileWrites.Release();
                    }
                }
                else if (_activeFixedSpeedPercent.HasValue)
                {
                    await SetFixedFanSpeedAsync(_activeFixedSpeedPercent.Value).ConfigureAwait(false);
                }
                else if (_currentFanProfile != FanProfile.Manual)
                {
                    await SetFanProfileAsync(_currentFanProfile).ConfigureAwait(false);
                }

                if (_customApuLimits is { } apu)
                {
                    await SetApuPowerAsync(apu.FPptWatts, apu.SPptWatts, apu.SplWatts).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PowerModeChanged Resume Error]: {ex.Message}");
            }
        }
    }
}
