using System.Management;
using System.Security.Principal;
using System.Text;
using AEROCC.Core.Display;
using AEROCC.Core.Gpu;
using AEROCC.Core.Hid;
using AEROCC.Core.Models;

namespace AEROCC.Core.Wmi;

public class GigabyteWmiClient : IDisposable
{
    private const string WmiNamespace = @"\\.\root\WMI";
    private const string ClassGet = "GB_WMIACPI_Get";
    private const string ClassSet = "GB_WMIACPI_Set";
    private const string DirectInstanceGet = @"GB_WMIACPI_Get.InstanceName='ACPI\PNP0C14\DCK_0'";
    private const string DirectInstanceSet = @"GB_WMIACPI_Set.InstanceName='ACPI\PNP0C14\DCK_0'";

    private readonly GigabyteKeyboardClient _keyboard = new();
    private readonly GpuManager _gpu = new();
    private readonly object _fanProfileLock = new();
    private readonly object _apuLimitLock = new();
    private (uint FPptMw, uint SPptMw, uint SplMw)? _persistedApuLimits;

    public GigabyteKeyboardClient Keyboard => _keyboard;
    public GpuManager Gpu => _gpu;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static ConnectionOptions CreateOptions() => new()
    {
        EnablePrivileges = true,
        Impersonation = ImpersonationLevel.Impersonate
    };

    public int? GetCpuTemperature()
    {
        return SafeRead<int?>(() =>
        {
            int temperature = Convert.ToInt32(InvokeGetMethod("getCpuTemp"));
            return temperature > 0 ? temperature : null;
        });
    }

    public int? GetFanRpm(int fanIndex)
    {
        string methodName = fanIndex switch
        {
            1 => "getRpm1",
            2 => "getRpm2",
            3 => "getRpm3",
            _ => throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index must be 1, 2 or 3")
        };

        var raw = InvokeGetMethod(methodName);
        if (raw == null) return null;

        try
        {
            ushort val = Convert.ToUInt16(raw);
            // Normal laptop fan speeds are in range 0 - 7000 RPM.
            // If high and low bytes are inverted (> 10000 RPM), swap them:
            if (val > 10000)
            {
                ushort swapped = (ushort)((val >> 8) | ((val & 0xFF) << 8));
                if (swapped < 10000) return swapped;
            }
            return val;
        }
        catch
        {
            return null;
        }
    }

    public int GetBatteryChargeLimit()
    {
        var raw = InvokeGetMethod("GetChargeStop");
        return raw != null ? Convert.ToInt32(raw) : 0;
    }

    public int GetBatteryChargePolicy()
    {
        var raw = InvokeGetMethod("GetChargePolicy");
        return raw != null ? Convert.ToInt32(raw) : 0;
    }

    public bool? GetWinKeyLock()
    {
        // 1. Try USB HID keyboard first (Aero 16 native)
        var hidVal = _keyboard.GetWinLock();
        if (hidVal.HasValue) return hidVal.Value;

        // 2. Fallback to WMI (legacy PS/2)
        var raw = InvokeGetMethod("getWinKeyLockStatus");
        return raw != null ? Convert.ToUInt32(raw) == 0 : null;
    }

    public bool SetWinKeyLock(bool locked)
    {
        // 1. Try USB HID keyboard first
        if (_keyboard.SetWinLock(locked)) return true;

        // 2. Fallback to WMI
        return InvokeSetMethod("SetWinKeyLockStatus", ("Data", (ushort)(locked ? 0 : 1)));
    }

    public bool? GetFnKeyLock()
    {
        // 1. Try USB HID keyboard first (Aero 16 native)
        var hidVal = _keyboard.GetFnLock();
        if (hidVal.HasValue) return hidVal.Value;

        // 2. Fallback to WMI (legacy PS/2)
        var raw = InvokeGetMethod("getFnKeyLockStatus");
        return raw != null ? Convert.ToUInt32(raw) == 0 : null;
    }

    public bool SetFnKeyLock(bool locked)
    {
        // 1. Try USB HID keyboard first
        if (_keyboard.SetFnLock(locked)) return true;

        // 2. Fallback to WMI
        return InvokeSetMethod("SetFnKeyLockStatus", ("Data", (ushort)(locked ? 0 : 1)));
    }

    public HardwareStatus GetHardwareStatus()
    {
        // Avoid GDI display enumeration in Session 0 (Windows Service) where no user desktop is attached
        var primaryDisp = (System.Diagnostics.Process.GetCurrentProcess().SessionId != 0)
            ? DisplayManager.GetPrimaryDisplay()
            : null;
        var blStatus = _keyboard.GetBacklightStatus();
        var gpuTelemetry = SafeRead(_gpu.GetTelemetry);
        var energyTelemetry = SafeRead(PowerManager.GetEnergyTelemetry);
        var thermalSampledAt = DateTimeOffset.UtcNow;

        return new HardwareStatus
        {
            CpuTemperature = SafeRead(GetCpuTemperature),
            Fan1Rpm = SafeRead(() => GetFanRpm(1)),
            Fan2Rpm = SafeRead(() => GetFanRpm(2)),
            ThermalSampledAtUtc = thermalSampledAt,
            BatteryChargeLimit = SafeRead(GetBatteryChargeLimit),
            BatteryPolicy = SafeRead(GetBatteryChargePolicy),
            WinKeyLocked = GetWinKeyLock(),
            FnKeyLocked = GetFnKeyLock(),
            DisplayRefreshRate = primaryDisp?.CurrentFrequency ?? 0,
            DisplayResolution = primaryDisp != null ? $"{primaryDisp.CurrentWidth}x{primaryDisp.CurrentHeight}" : null,
            AvailableRefreshRates = primaryDisp?.AvailableFrequencies ?? new List<int>(),
            BacklightBrightness = blStatus?.Brightness ?? 0,
            BacklightEffect = blStatus?.Effect.ToString(),
            BacklightColor = blStatus?.Color.ToString(),
            Gpu = gpuTelemetry,
            CpuPowerWatts = energyTelemetry?.CpuPackageWatts,
            BatteryChargeWatts = energyTelemetry?.BatteryChargeWatts,
            BatteryDischargeWatts = energyTelemetry?.BatteryDischargeWatts,
            IsAcOnline = energyTelemetry?.IsAcOnline ?? (PowerManager.GetPowerInfo().LineStatus == PowerLineStatus.Online),
            IsEnergySaverEnabled = SafeRead(PowerManager.GetEnergySaverEnabled)
        };
    }

    private static T SafeRead<T>(Func<T> readFunc)
    {
        try
        {
            return readFunc();
        }
        catch
        {
            return default!;
        }
    }

    public bool SetBatteryChargeLimit(int limitPercent)
    {
        if (limitPercent != 60 && limitPercent != 80 && limitPercent != 100)
        {
            throw new ArgumentException("Battery limit must be 60, 80, or 100 percent.", nameof(limitPercent));
        }

        bool modeOk = InvokeSetMethod("SetChargeMode", ("Data", (ushort)1));

        bool policyOk;
        bool stopOk;

        if (limitPercent == 100)
        {
            policyOk = InvokeSetMethod("SetChargePolicy", ("Data", (ushort)0));
            stopOk = InvokeSetMethod("SetChargeStop", ("Data", (ushort)100));
        }
        else
        {
            policyOk = InvokeSetMethod("SetChargePolicy", ("Data", (ushort)4));
            stopOk = InvokeSetMethod("SetChargeStop", ("Data", (ushort)limitPercent));
        }

        if (!modeOk || !policyOk || !stopOk)
        {
            LogDebug($"[SetBatteryChargeLimit] WMI write failed: modeOk={modeOk}, policyOk={policyOk}, stopOk={stopOk}");
            return false;
        }

        // Verify applied limit via readback check (retry once after 50ms settling time if needed)
        int currentLimit = GetBatteryChargeLimit();
        if (currentLimit != limitPercent)
        {
            Thread.Sleep(50);
            currentLimit = GetBatteryChargeLimit();
        }

        if (currentLimit != limitPercent)
        {
            LogDebug($"[SetBatteryChargeLimit] Readback verification failed: expected {limitPercent}%, got {currentLimit}%");
            return false;
        }

        return true;
    }

    public bool SetFanProfile(FanProfile profile)
    {
        lock (_fanProfileLock)
        {
            bool applied = profile switch
            {
                FanProfile.Turbo => SendPfmNotify(3)
                    && SetAutoFanStatus(false)
                    && SetNvThermalTarget(0)
                    && SetFixedFanSpeed(100)
                    && SetGpuFanDuty(100)
                    && SetStepFanStatus(true)
                    && SetFanFixedStatus(true),
                FanProfile.Game => SendPfmNotify(2)
                    && SetFanFixedStatus(false)
                    && SetStepFanStatus(false)
                    && SetFixedFanSpeed(0)
                    && SetGpuFanDuty(0)
                    && SetAutoFanStatus(true)
                    && SetNvThermalTarget(0),
                FanProfile.Eco => SendPfmNotify(0)
                    && SetFanFixedStatus(false)
                    && SetStepFanStatus(false)
                    && SetFixedFanSpeed(0)
                    && SetGpuFanDuty(0)
                    && SetAutoFanStatus(false)
                    && SetNvThermalTarget(1),
                FanProfile.Normal or FanProfile.Manual => RestoreNormalFanProfile(),
                _ => false
            };

            if (!applied && profile is (FanProfile.Turbo or FanProfile.Game or FanProfile.Eco))
            {
                if (!RestoreNormalFanProfile())
                    LogDebug($"[Fan profile] Failed to restore Normal after a partial {profile} write.");
            }

            (uint FPptMw, uint SPptMw, uint SplMw)? apuToRestore;
            lock (_apuLimitLock)
            {
                apuToRestore = _persistedApuLimits;
            }

            if (applied && apuToRestore is { } apu)
            {
                // ACPI 0xED in DSDT resets ECPT APU registers (0x30/0x32/0x34).
                // Reapply custom APU limits to ensure they persist across fan profile switches.
                InvokeSetMethod("SetApuParameter1", ("Data", apu.FPptMw));
                InvokeSetMethod("SetApuParameter2", ("Data", apu.SPptMw));
                InvokeSetMethod("SetApuParameter3", ("Data", apu.SplMw));
            }

            return applied;
        }
    }

    // Attempt every cleanup write even if an earlier operation failed.
    private bool RestoreNormalFanProfile() =>
        SendPfmNotify(1)
        & SetFanFixedStatus(false)
        & SetStepFanStatus(false)
        & SetAutoFanStatus(false)
        & SetNvThermalTarget(0)
        & SetFixedFanSpeed(0)
        & SetGpuFanDuty(0);

    /// <summary>
    /// Linearly interpolates desired fan duty (%) at a given temperature across curve points.
    /// </summary>
    public static double InterpolateCurveDuty(IList<(byte Temp, byte Duty)> points, double temp)
    {
        if (points == null || points.Count == 0) return 40;
        var sorted = points.OrderBy(p => p.Temp).ToList();
        if (temp <= sorted[0].Temp) return sorted[0].Duty;
        if (temp >= sorted[^1].Temp) return sorted[^1].Duty;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            if (temp >= sorted[i].Temp && temp <= sorted[i + 1].Temp)
            {
                double span = sorted[i + 1].Temp - sorted[i].Temp;
                if (span <= 0) return sorted[i].Duty;
                double frac = (temp - sorted[i].Temp) / span;
                return sorted[i].Duty + frac * (sorted[i + 1].Duty - sorted[i].Duty);
            }
        }

        return sorted[^1].Duty;
    }

    /// <summary>
    /// Maps a target curve duty (%) onto the four discrete EC thermal tiers supported by EG61VH.
    /// </summary>
    public static FanProfile MapCurveDutyToEcTier(double dutyPercent, bool allowTurbo = true)
    {
        if (dutyPercent <= 25) return FanProfile.Eco;
        if (dutyPercent <= 55) return FanProfile.Normal;
        if (dutyPercent < 90 || !allowTurbo) return FanProfile.Game;
        return FanProfile.Turbo;
    }

    /// <summary>
    /// Maps a fixed speed slider value (%) onto the four discrete EC tiers on EG61VH.
    /// </summary>
    public static FanProfile MapFixedPercentToEcTier(byte speedPercent)
    {
        if (speedPercent <= 30) return FanProfile.Eco;
        if (speedPercent <= 59) return FanProfile.Normal;
        if (speedPercent < 90) return FanProfile.Game;
        return FanProfile.Turbo;
    }

    public bool SetFixedFanSpeed(byte speedPercent) =>
        WriteFanSetting("SetFixedFanSpeed", speedPercent);

    public bool SetCpuFanDuty(byte dutyPercent) =>
        InvokeSetMethod("SetCPUFanDuty", ("Data", dutyPercent));

    public bool SetGpuFanDuty(byte dutyPercent) =>
        WriteFanSetting("SetGPUFanDuty", dutyPercent);

    public bool SetFanAdjustStatus(byte data) =>
        InvokeSetMethod("SetFanAdjustStatus", ("Data", data));

    public bool SetCurrentFanStep(byte step) =>
        InvokeSetMethod("SetCurrentFanStep", ("Data", step));

    public ushort? GetFanIndexValue(byte index)
    {
        var raw = InvokeGetMethodWithParams("GetFanIndexValue", ("Index", index));
        return raw != null ? Convert.ToUInt16(raw) : null;
    }

    public bool SetAutoFanStatus(bool enabled) =>
        WriteFanSetting("SetAutoFanStatus", enabled ? (byte)1 : (byte)0);

    public bool SetFanFixedStatus(bool enabled) =>
        WriteFanSetting("SetFixedFanStatus", enabled ? (byte)1 : (byte)0);

    public bool SetStepFanStatus(bool enabled) =>
        WriteFanSetting("SetStepFanStatus", enabled ? (byte)1 : (byte)0);

    public bool SetNvThermalTarget(byte target) =>
        WriteFanSetting("SetNvThermalTarget", target);

    public bool SendPfmNotify(byte notifyCode) =>
        WriteFanSetting("SetFanModeNotify", notifyCode);

    protected virtual bool WriteFanSetting(string methodName, byte value) =>
        InvokeSetMethod(methodName, ("Data", value));

    public static void LogDebug(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fan_debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    public void DumpWmiMethods()
    {
        try
        {
            var scope = GetConnectedScope();
            var classPath = new ManagementPath(ClassSet);
            using var managementClass = new ManagementClass(scope, classPath, null);
            var sb = new StringBuilder();
            sb.AppendLine("=== GB_WMIACPI_Set Methods ===");
            foreach (MethodData md in managementClass.Methods)
            {
                sb.Append($"Method: {md.Name}(");
                if (md.InParameters != null)
                {
                    var paramList = new List<string>();
                    foreach (PropertyData prop in md.InParameters.Properties)
                    {
                        paramList.Add($"{prop.Type} {prop.Name}");
                    }
                    sb.Append(string.Join(", ", paramList));
                }
                sb.AppendLine(")");
            }
            sb.AppendLine("================================");
            LogDebug(sb.ToString());
        }
        catch (Exception ex)
        {
            LogDebug($"DumpWmiMethods Error: {ex.Message}");
        }
    }

    public bool SetFanIndexValue(byte index, byte temperature, byte value) =>
        InvokeSetMethod("SetFanIndexValue", ("Index", index), ("Temperture", temperature), ("Value", value));

    public bool ApplyFanCurve(IList<(byte Temp, byte Duty)> points)
    {
        try
        {
            if (GetCpuTemperature() is not int cpuTemp)
                return false;

            double duty = InterpolateCurveDuty(points, cpuTemp);
            FanProfile targetTier = MapCurveDutyToEcTier(duty);
            LogDebug($"[ApplyFanCurve] Temp={cpuTemp}C Duty={duty:F0}% -> EC tier {targetTier}");
            return SetFanProfile(targetTier);
        }
        catch (Exception ex)
        {
            LogDebug($"ApplyFanCurve Exception: {ex.Message}");
            return false;
        }
    }

    public bool SetFixedFanSpeedFull(byte speedPercent)
    {
        try
        {
            FanProfile targetTier = MapFixedPercentToEcTier(speedPercent);
            LogDebug($"[SetFixedFanSpeedFull] Speed={speedPercent}% -> EC tier {targetTier}");
            return SetFanProfile(targetTier);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetFixedFanSpeedFull Error]: {ex.Message}");
            return false;
        }
    }

    public bool SetApuPower(double fPptWatts, double sPptWatts, double splWatts)
    {
        uint fPptMw = WattsToMilliwatts(fPptWatts);
        uint sPptMw = WattsToMilliwatts(sPptWatts);
        uint splMw = WattsToMilliwatts(splWatts);

        bool r1 = InvokeSetMethod("SetApuParameter1", ("Data", fPptMw));
        bool r2 = InvokeSetMethod("SetApuParameter2", ("Data", sPptMw));
        bool r3 = InvokeSetMethod("SetApuParameter3", ("Data", splMw));
        bool ok = r1 && r2 && r3;

        if (ok)
        {
            lock (_apuLimitLock)
            {
                _persistedApuLimits = (fPptMw, sPptMw, splMw);
            }
        }

        return ok;
    }

    public void ClearCustomApuLimits()
    {
        lock (_apuLimitLock)
        {
            _persistedApuLimits = null;
        }
    }

    private static uint WattsToMilliwatts(double watts) =>
        (uint)Math.Clamp(Math.Round(watts * 1000.0), 5000, 150000);

    public bool SetKeyboardBacklight(bool enabled) =>
        InvokeSetMethod("SetKeyBoardBackLight", ("Data", (ushort)(enabled ? 1 : 0)));

    public int GetAiPowerCtlCapability()
    {
        var raw = InvokeGetMethod("getAiPowerCtlCapability");
        return raw != null ? Convert.ToInt32(raw) : 0;
    }

    public int GetDisplayMode()
    {
        var raw = InvokeGetMethod("GetPEG2orSG2");
        return raw != null ? Convert.ToInt32(raw) : 0;
    }

    public bool SetDisplayMode(byte mode) =>
        InvokeSetMethod("SetPEG2orSG2", ("Data", mode));

    public byte GetNvPowerOperationStatus()
    {
        var raw = InvokeGetMethod("GetNvPowerConfigOperationStatus");
        return raw != null ? Convert.ToByte(raw) : (byte)0;
    }

    public bool SetNvPower(bool turnOn) =>
        InvokeSetMethod("SetNvPowerConfig", ("Data", (ushort)(turnOn ? 4 : 3)));

    public bool SetGpuPowerMode(GpuPowerMode mode)
    {
        if (mode == GpuPowerMode.Eco)
        {
            _gpu.ShutdownNvml();
        }

        bool success = SetNvPower(mode != GpuPowerMode.Eco);
        GpuManager.TriggerPnpRescan();

        // Do not persist Eco if ACPI refused RTD3 power-off (e.g. a 3D app holds dGPU active)
        if (success || mode != GpuPowerMode.Eco)
        {
            _gpu.SavePersistedMode(mode);
        }

        return success;
    }

    private ManagementScope? _cachedScope;
    private ManagementPath? _cachedSetInstancePath;
    private ManagementPath? _cachedGetInstancePath;
    private readonly object _wmiLock = new();

    private ManagementScope GetConnectedScope()
    {
        GigabyteWmiMofRegistrar.EnsureRegisteredOnce();

        lock (_wmiLock)
        {
            if (_cachedScope != null && _cachedScope.IsConnected)
            {
                return _cachedScope;
            }

            _cachedScope = new ManagementScope(WmiNamespace, CreateOptions());
            _cachedScope.Connect();
            return _cachedScope;
        }
    }

    private void ResetScope()
    {
        lock (_wmiLock)
        {
            _cachedScope = null;
            _cachedSetInstancePath = null;
            _cachedGetInstancePath = null;
        }
    }

    private bool TryRecoverWmiSchema(Exception ex)
    {
        if (ex is ManagementException mEx &&
            mEx.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.NotFound)
        {
            ResetScope();
            return GigabyteWmiMofRegistrar.EnsureRegistered(force: true);
        }

        return false;
    }

    public object? InvokeGetMethod(string methodName) =>
        InvokeGetMethodWithRetry(methodName, allowMofHeal: true);

    public object? InvokeGetMethodWithParams(string methodName, params (string Key, object Value)[] parameters)
    {
        try
        {
            return InvokeGetMethodCore(methodName, parameters);
        }
        catch (Exception ex)
        {
            if (TryRecoverWmiSchema(ex))
            {
                try
                {
                    return InvokeGetMethodCore(methodName, parameters);
                }
                catch { }
            }

            ResetScope();
            LogDebug($"[WMI Get Error] {methodName}: {ex.Message}");
            return null;
        }
    }

    private object? InvokeGetMethodWithRetry(string methodName, bool allowMofHeal)
    {
        try
        {
            return InvokeGetMethodCore(methodName, Array.Empty<(string, object)>());
        }
        catch (Exception ex)
        {
            if (allowMofHeal && TryRecoverWmiSchema(ex))
            {
                return InvokeGetMethodWithRetry(methodName, allowMofHeal: false);
            }

            ResetScope();
            System.Diagnostics.Debug.WriteLine($"[WMI Get Skipped] {methodName}: {ex.Message}");
            return null;
        }
    }

    private object? InvokeGetMethodCore(string methodName, (string Key, object Value)[] parameters)
    {
        var scope = GetConnectedScope();

        if (_cachedGetInstancePath != null)
        {
            try
            {
                using var directObj = new ManagementObject(scope, _cachedGetInstancePath, null);
                using var inParams = BuildInParams(directObj, methodName, parameters);
                using var outParams = directObj.InvokeMethod(methodName, inParams, null);
                return outParams?["Data"];
            }
            catch
            {
                _cachedGetInstancePath = null;
            }
        }

        var classPath = new ManagementPath(ClassGet);
        using var managementClass = new ManagementClass(scope, classPath, null);
        using var instances = managementClass.GetInstances();

        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                using var inParams = BuildInParams(instance, methodName, parameters);
                using var outParams = instance.InvokeMethod(methodName, inParams, null);
                _cachedGetInstancePath = instance.Path;
                return outParams?["Data"];
            }
        }

        return null;
    }

    private static ManagementBaseObject? BuildInParams(
        ManagementObject instance,
        string methodName,
        (string Key, object Value)[] parameters)
    {
        if (parameters.Length == 0) return null;
        var inParams = instance.GetMethodParameters(methodName);
        foreach (var (key, val) in parameters)
        {
            inParams[key] = val;
        }
        return inParams;
    }

    public bool InvokeSetMethod(string methodName, params (string Key, object Value)[] parameters) =>
        InvokeSetMethodWithRetry(methodName, parameters, allowMofHeal: true);

    private bool InvokeSetMethodWithRetry(
        string methodName,
        (string Key, object Value)[] parameters,
        bool allowMofHeal)
    {
        try
        {
            return InvokeSetMethodCore(methodName, parameters);
        }
        catch (Exception ex)
        {
            if (allowMofHeal && TryRecoverWmiSchema(ex))
            {
                return InvokeSetMethodWithRetry(methodName, parameters, allowMofHeal: false);
            }

            ResetScope();
            LogDebug($"[WMI Set Error] Failed {methodName}: {ex.GetType().Name} - {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[WMI Set Error] Failed {methodName}: {ex.Message}");
            return false;
        }
    }

    private bool InvokeSetMethodCore(string methodName, (string Key, object Value)[] parameters)
    {
        var scope = GetConnectedScope();

        var classPath = new ManagementPath(ClassSet);
        using var managementClass = new ManagementClass(scope, classPath, null);
        using var inParams = managementClass.GetMethodParameters(methodName);

        foreach (var (key, val) in parameters)
        {
            inParams[key] = val;
        }

        if (_cachedSetInstancePath != null)
        {
            try
            {
                using var directObj = new ManagementObject(scope, _cachedSetInstancePath, null);
                using var result = directObj.InvokeMethod(methodName, inParams, null);
                return true;
            }
            catch
            {
                _cachedSetInstancePath = null;
            }
        }

        using var instances = managementClass.GetInstances();
        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                using var result = instance.InvokeMethod(methodName, inParams, null);
                _cachedSetInstancePath = instance.Path;
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _gpu.Dispose();
        GC.SuppressFinalize(this);
    }

    public Dictionary<string, object?> RunDiagnostics()
    {
        string[] methodsToTest =
        [
            "getCpuTemp", "getRpm1", "getRpm2", "getRpm3", "getRpm4",
            "GetCPUFanDuty", "GetGPUFanDuty", "GetAutoFanStatus", "GetFixedFanStatus", "GetFixedFanSpeed",
            "GetChargeStop", "GetChargePolicy", "GetBatteryHealth", "GetBatteryNCM",
            "getWinKeyLockStatus", "getFnKeyLockStatus", "getTouchPadStatus",
            "getPostAnimate", "getHddLightDisplay", "getBatteryLightDisplay", "getPwrLightDisplay",
            "getAiPowerCtlCapability", "GetPEG2orSG2", "getSecondDisplay", "getPdChargeInStatus", "GetNvPowerConfigOperationStatus"
        ];

        var results = new Dictionary<string, object?>();
        foreach (var method in methodsToTest)
        {
            try
            {
                var val = InvokeGetMethod(method);
                results[method] = val;
            }
            catch (Exception ex)
            {
                results[method] = $"Error: {ex.Message}";
            }
        }

        return results;
    }
}
