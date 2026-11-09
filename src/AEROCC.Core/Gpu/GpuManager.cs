using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AEROCC.Core.Gpu;

public class GpuManager
{
    private static readonly Guid GuidDisplay = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private const uint DigcfPresent = 2;
    private const uint SpdrpHardwareId = 1;
    private const uint SpdrpDeviceDesc = 0;

    private const string RegSubKey = @"Software\AEROCC";
    private const string RegValueMode = "GpuPowerMode";

    private GpuPowerMode _cachedMode = GpuPowerMode.Dynamic;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property,
        out uint PropertyRegDataType,
        byte[] PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    public GpuManager()
    {
        _cachedMode = LoadPersistedMode();
    }

    public GpuPowerMode CurrentPowerMode => _cachedMode;

    public GpuTelemetry GetTelemetry()
    {
        var (isPresent, pState, dgpuName) = CheckNvidiaDgpuState();
        var mode = _cachedMode;

        // If Eco mode is requested and device is gone or disabled:
        if (mode == GpuPowerMode.Eco && (!isPresent || pState == GpuPowerState.EcoDisabled))
        {
            return new GpuTelemetry
            {
                Name = dgpuName ?? "NVIDIA GeForce RTX",
                IntegratedName = "AMD Radeon 860M",
                PowerState = GpuPowerState.EcoDisabled,
                PowerMode = GpuPowerMode.Eco,
                IsNvidiaPresent = false
            };
        }

        // If dGPU is sleeping in D3cold (Optimus idle):
        if (pState == GpuPowerState.Sleeping || !isPresent)
        {
            return new GpuTelemetry
            {
                Name = dgpuName ?? "NVIDIA GeForce RTX",
                IntegratedName = "AMD Radeon 860M",
                PowerState = isPresent ? GpuPowerState.Sleeping : GpuPowerState.EcoDisabled,
                PowerMode = mode,
                IsNvidiaPresent = isPresent
            };
        }

        // dGPU is active: Query NVML for telemetry
        int? temp = null;
        int? usage = null;
        int? memUsed = null;
        int? memTotal = null;
        double? power = null;
        int? clock = null;
        string? activeName = dgpuName;

        try
        {
            if (NvmlNative.nvmlInit_v2() == NvmlNative.NVML_SUCCESS)
            {
                try
                {
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) == NvmlNative.NVML_SUCCESS)
                    {
                        var sb = new StringBuilder(64);
                        if (NvmlNative.nvmlDeviceGetName(device, sb, (uint)sb.Capacity) == NvmlNative.NVML_SUCCESS)
                        {
                            activeName = sb.ToString();
                        }

                        if (NvmlNative.nvmlDeviceGetTemperature(device, NvmlNative.NVML_TEMPERATURE_GPU, out uint t) == NvmlNative.NVML_SUCCESS)
                        {
                            temp = (int)t;
                        }

                        if (NvmlNative.nvmlDeviceGetUtilizationRates(device, out var util) == NvmlNative.NVML_SUCCESS)
                        {
                            usage = (int)util.Gpu;
                        }

                        if (NvmlNative.nvmlDeviceGetMemoryInfo(device, out var mem) == NvmlNative.NVML_SUCCESS)
                        {
                            memUsed = (int)(mem.Used / (1024 * 1024));
                            memTotal = (int)(mem.Total / (1024 * 1024));
                        }

                        if (NvmlNative.nvmlDeviceGetPowerUsage(device, out uint mw) == NvmlNative.NVML_SUCCESS)
                        {
                            power = Math.Round(mw / 1000.0, 1);
                        }

                        if (NvmlNative.nvmlDeviceGetClockInfo(device, NvmlNative.NVML_CLOCK_GRAPHICS, out uint c) == NvmlNative.NVML_SUCCESS)
                        {
                            clock = (int)c;
                        }
                    }
                }
                finally
                {
                    NvmlNative.nvmlShutdown();
                }
            }
        }
        catch
        {
            // NVML fallback
        }

        return new GpuTelemetry
        {
            Name = activeName ?? "NVIDIA GeForce RTX",
            IntegratedName = "AMD Radeon 860M",
            PowerState = GpuPowerState.Active,
            PowerMode = mode,
            Temperature = temp,
            CoreUsage = usage,
            MemoryUsedMb = memUsed,
            MemoryTotalMb = memTotal,
            PowerWatts = power,
            CoreClockMhz = clock,
            IsNvidiaPresent = true
        };
    }

    public (bool IsPresent, GpuPowerState State, string? Name) CheckNvidiaDgpuState()
    {
        var classGuid = GuidDisplay;
        var hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent);
        if (hDevInfo == (IntPtr)(-1))
        {
            return (false, GpuPowerState.Unknown, null);
        }

        try
        {
            var data = new SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
            };

            for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref data); i++)
            {
                string? hwId = GetDeviceProperty(hDevInfo, ref data, SpdrpHardwareId);
                string? desc = GetDeviceProperty(hDevInfo, ref data, SpdrpDeviceDesc);

                if (!string.IsNullOrEmpty(hwId) && hwId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
                {
                    if (CM_Get_DevNode_Status(out uint status, out uint problem, data.DevInst, 0) == 0)
                    {
                        // Check if device is in problem/disabled state or sleeping (45 = phantom, 22 = disabled, 29 = hw disabled)
                        if ((status & 0x400) != 0 && (problem == 45 || problem == 22 || problem == 29))
                        {
                            return (true, GpuPowerState.Sleeping, desc);
                        }

                        return (true, GpuPowerState.Active, desc);
                    }

                    return (true, GpuPowerState.Sleeping, desc);
                }
            }

            return (false, GpuPowerState.EcoDisabled, null);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    public void SavePersistedMode(GpuPowerMode mode)
    {
        _cachedMode = mode;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegSubKey);
            key?.SetValue(RegValueMode, (int)mode, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static GpuPowerMode LoadPersistedMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegSubKey, writable: false);
            if (key?.GetValue(RegValueMode) is int val && Enum.IsDefined(typeof(GpuPowerMode), val))
            {
                return (GpuPowerMode)val;
            }
        }
        catch { }
        return GpuPowerMode.Dynamic;
    }

    public static void TriggerPnpRescan()
    {
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = "/scan-devices",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
        });
    }

    private static string? GetDeviceProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA data, uint prop)
    {
        byte[] buffer = new byte[1024];
        if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref data, prop, out _, buffer, (uint)buffer.Length, out uint reqSize))
        {
            return Encoding.Unicode.GetString(buffer, 0, (int)reqSize).TrimEnd('\0');
        }
        return null;
    }
}
