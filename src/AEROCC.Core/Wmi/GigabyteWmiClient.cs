using System.Management;
using System.Security.Principal;
using AEROCC.Core.Hid;
using AEROCC.Core.Models;

namespace AEROCC.Core.Wmi;

public class GigabyteWmiClient
{
    private const string WmiNamespace = @"\\.\root\WMI";
    private const string ClassGet = "GB_WMIACPI_Get";
    private const string ClassSet = "GB_WMIACPI_Set";
    private const string DirectInstanceGet = @"GB_WMIACPI_Get.InstanceName='ACPI\PNP0C14\DCK_0'";
    private const string DirectInstanceSet = @"GB_WMIACPI_Set.InstanceName='ACPI\PNP0C14\DCK_0'";

    private readonly GigabyteKeyboardClient _keyboard = new();

    public GigabyteKeyboardClient Keyboard => _keyboard;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static ConnectionOptions CreateOptions() => new()
    {
        EnablePrivileges = true,
        Impersonation = ImpersonationLevel.Impersonate
    };

    public int GetCpuTemperature()
    {
        var raw = InvokeGetMethod("getCpuTemp");
        return raw != null ? Convert.ToInt32(raw) : 0;
    }

    public int GetFanRpm(int fanIndex)
    {
        string methodName = fanIndex switch
        {
            1 => "getRpm1",
            2 => "getRpm2",
            3 => "getRpm3",
            _ => throw new ArgumentOutOfRangeException(nameof(fanIndex), "Fan index must be 1, 2 or 3")
        };

        var raw = InvokeGetMethod(methodName);
        if (raw == null) return 0;

        ushort val = Convert.ToUInt16(raw);
        byte[] bytes = BitConverter.GetBytes(val);
        return bytes[0] * 256 + bytes[1];
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
        return new HardwareStatus
        {
            CpuTemperature = SafeRead(GetCpuTemperature),
            Fan1Rpm = SafeRead(() => GetFanRpm(1)),
            Fan2Rpm = SafeRead(() => GetFanRpm(2)),
            BatteryChargeLimit = SafeRead(GetBatteryChargeLimit),
            BatteryPolicy = SafeRead(GetBatteryChargePolicy),
            WinKeyLocked = GetWinKeyLock(),
            FnKeyLocked = GetFnKeyLock()
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

        InvokeSetMethod("SetChargeMode", ("Data", (ushort)1));

        if (limitPercent == 100)
        {
            InvokeSetMethod("SetChargePolicy", ("Data", (ushort)0));
            InvokeSetMethod("SetChargeStop", ("Data", (ushort)100));
        }
        else
        {
            InvokeSetMethod("SetChargePolicy", ("Data", (ushort)4));
            InvokeSetMethod("SetChargeStop", ("Data", (ushort)limitPercent));
        }

        return true;
    }

    public bool SetFanProfile(FanProfile profile)
    {
        switch (profile)
        {
            case FanProfile.Eco:
                SendPfmNotify(0);
                SetFanFixedStatus(false);
                SetStepFanStatus(false);
                SetAutoFanStatus(false);
                SetNvThermalTarget(1);
                break;

            case FanProfile.Normal:
                SendPfmNotify(1);
                SetFanFixedStatus(false);
                SetStepFanStatus(false);
                SetAutoFanStatus(false);
                SetNvThermalTarget(0);
                break;

            case FanProfile.Game:
                SendPfmNotify(2);
                SetFanFixedStatus(false);
                SetStepFanStatus(false);
                SetAutoFanStatus(true);
                SetNvThermalTarget(0);
                break;

            case FanProfile.Turbo:
                SendPfmNotify(3);
                SetAutoFanStatus(false);
                SetNvThermalTarget(0);
                SetFixedFanSpeed(100);
                SetGpuFanDuty(100);
                SetStepFanStatus(true);
                SetFanFixedStatus(true);
                break;

            case FanProfile.Manual:
                SendPfmNotify(5);
                SetFanFixedStatus(false);
                SetStepFanStatus(true);
                SetAutoFanStatus(false);
                SetNvThermalTarget(0);
                break;
        }

        return true;
    }

    public bool SetFixedFanSpeed(byte speedPercent) =>
        InvokeSetMethod("SetFixedFanSpeed", ("Data", speedPercent));

    public bool SetGpuFanDuty(byte dutyPercent) =>
        InvokeSetMethod("SetGPUFanDuty", ("Data", dutyPercent));

    public bool SetAutoFanStatus(bool enabled) =>
        InvokeSetMethod("SetAutoFanStatus", ("Data", (ushort)(enabled ? 1 : 0)));

    public bool SetFanFixedStatus(bool enabled) =>
        InvokeSetMethod("SetFixedFanStatus", ("Data", (ushort)(enabled ? 1 : 0)));

    public bool SetStepFanStatus(bool enabled) =>
        InvokeSetMethod("SetStepFanStatus", ("Data", (ushort)(enabled ? 1 : 0)));

    public bool SetNvThermalTarget(byte target) =>
        InvokeSetMethod("SetNvThermalTarget", ("Data", target));

    public bool SendPfmNotify(ushort notifyCode) =>
        InvokeSetMethod("SetFanModeNotify", ("Data", notifyCode));

    public bool SetApuPower(double fPptWatts, double sPptWatts, double splWatts)
    {
        bool r1 = InvokeSetMethod("SetApuParameter1", ("Data", fPptWatts * 1000.0));
        bool r2 = InvokeSetMethod("SetApuParameter2", ("Data", sPptWatts * 1000.0));
        bool r3 = InvokeSetMethod("SetApuParameter3", ("Data", splWatts * 1000.0));
        return r1 && r2 && r3;
    }

    public bool SetKeyboardBacklight(bool enabled) =>
        InvokeSetMethod("SetKeyBoardBackLight", ("Data", (ushort)(enabled ? 1 : 0)));

    public object? InvokeGetMethod(string methodName)
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace, CreateOptions());
            scope.Connect();

            // 1. Try direct DCK_0 path first
            try
            {
                var directPath = new ManagementPath(DirectInstanceGet);
                using var directObj = new ManagementObject(scope, directPath, null);
                using var outParams = directObj.InvokeMethod(methodName, null, null);
                return outParams?["Data"];
            }
            catch
            {
                // 2. Fallback to iterating instances of GB_WMIACPI_Get
                var classPath = new ManagementPath(ClassGet);
                using var managementClass = new ManagementClass(scope, classPath, null);
                using var instances = managementClass.GetInstances();

                foreach (ManagementObject instance in instances)
                {
                    using (instance)
                    {
                        using var outParams = instance.InvokeMethod(methodName, null, null);
                        return outParams?["Data"];
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WMI Get Skipped] {methodName}: {ex.Message}");
            return null;
        }
    }

    public bool InvokeSetMethod(string methodName, params (string Key, object Value)[] parameters)
    {
        try
        {
            var scope = new ManagementScope(WmiNamespace, CreateOptions());
            scope.Connect();

            var classPath = new ManagementPath(ClassSet);
            using var managementClass = new ManagementClass(scope, classPath, null);
            using var inParams = managementClass.GetMethodParameters(methodName);

            foreach (var (key, val) in parameters)
            {
                inParams[key] = val;
            }

            // 1. Try direct DCK_0 path first
            try
            {
                var directPath = new ManagementPath(DirectInstanceSet);
                using var directObj = new ManagementObject(scope, directPath, null);
                directObj.InvokeMethod(methodName, inParams, null);
                return true;
            }
            catch
            {
                // 2. Fallback to iterating instances
                using var instances = managementClass.GetInstances();
                foreach (ManagementObject instance in instances)
                {
                    using (instance)
                    {
                        instance.InvokeMethod(methodName, inParams, null);
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[WMI Set Error] Failed {methodName}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    public Dictionary<string, object?> RunDiagnostics()
    {
        string[] methodsToTest =
        [
            "getCpuTemp", "getRpm1", "getRpm2", "getRpm3", "getRpm4",
            "GetCPUFanDuty", "GetGPUFanDuty", "GetAutoFanStatus", "GetFixedFanStatus", "GetFixedFanSpeed",
            "GetChargeStop", "GetChargePolicy", "GetBatteryHealth", "GetBatteryNCM",
            "getWinKeyLockStatus", "getFnKeyLockStatus", "getTouchPadStatus",
            "getPostAnimate", "getHddLightDisplay", "getBatteryLightDisplay", "getPwrLightDisplay"
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
