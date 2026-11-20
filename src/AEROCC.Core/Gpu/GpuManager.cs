using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AEROCC.Core.Gpu;

public class GpuManager : IDisposable
{
    private static readonly Guid GuidDisplay = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private const uint DigcfPresent = 2;
    private const uint SpdrpHardwareId = 1;
    private const uint SpdrpDeviceDesc = 0;
    private const uint SpdrpDevicePowerData = 0x0000001E;

    private const string RegSubKey = @"Software\AEROCC";
    private const string RegValueMode = "GpuPowerMode";

    private GpuPowerMode _cachedMode = GpuPowerMode.Dynamic;
    private string _cachedIntegratedName = "Integrated GPU";
    private bool _isNvmlInitialized = false;
    private readonly object _nvmlLock = new();

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

    public void ShutdownNvml()
    {
        lock (_nvmlLock)
        {
            if (_isNvmlInitialized)
            {
                try
                {
                    NvmlNative.nvmlShutdown();
                }
                catch { }
                finally
                {
                    _isNvmlInitialized = false;
                }
            }
        }
    }

    private bool EnsureNvmlInitialized()
    {
        if (_cachedMode == GpuPowerMode.Eco)
        {
            ShutdownNvml();
            return false;
        }

        lock (_nvmlLock)
        {
            if (_isNvmlInitialized) return true;
            try
            {
                if (NvmlNative.nvmlInit_v2() == NvmlNative.NVML_SUCCESS)
                {
                    _isNvmlInitialized = true;
                    return true;
                }
            }
            catch
            {
                _isNvmlInitialized = false;
            }
            return false;
        }
    }

    public GpuTelemetry GetTelemetry()
    {
        var mode = _cachedMode;
        string igpuName = _cachedIntegratedName;

        // In Eco mode: discrete GPU is powered down.
        // Under NO circumstances should NVML be queried or initialized in Eco mode!
        if (mode == GpuPowerMode.Eco)
        {
            ShutdownNvml();
            return new GpuTelemetry
            {
                Name = "NVIDIA GeForce RTX",
                IntegratedName = igpuName,
                PowerState = GpuPowerState.EcoDisabled,
                PowerMode = GpuPowerMode.Eco,
                IsNvidiaPresent = false
            };
        }

        var (isPresent, pState, dgpuName) = CheckNvidiaDgpuState();
        igpuName = _cachedIntegratedName;

        // If dGPU is sleeping in D3cold (Optimus idle) or not present:
        if (pState != GpuPowerState.Active || !isPresent)
        {
            ShutdownNvml();
            return new GpuTelemetry
            {
                Name = dgpuName ?? "NVIDIA GeForce RTX",
                IntegratedName = igpuName,
                PowerState = isPresent ? GpuPowerState.Sleeping : GpuPowerState.EcoDisabled,
                PowerMode = mode,
                IsNvidiaPresent = isPresent
            };
        }

        // dGPU is actively in D0 (PowerDeviceD0): Query NVML for telemetry without shutting down on every cycle
        int? temp = null;
        int? usage = null;
        int? memUsed = null;
        int? memTotal = null;
        double? power = null;
        int? clock = null;
        string? activeName = dgpuName;

        try
        {
            if (_cachedMode != GpuPowerMode.Eco && EnsureNvmlInitialized())
            {
                int count = 0;
                if (NvmlNative.nvmlDeviceGetCount_v2(ref count) == NvmlNative.NVML_SUCCESS && count > 0)
                {
                    if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) == NvmlNative.NVML_SUCCESS && device != IntPtr.Zero)
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

                        if (NvmlNative.nvmlDeviceGetClockInfo(device, NvmlNative.NVML_CLOCK_GRAPHICS, out uint c) == NvmlNative.NVML_SUCCESS && c > 0)
                        {
                            clock = (int)c;
                        }
                        else if (NvmlNative.nvmlDeviceGetClockInfo(device, NvmlNative.NVML_CLOCK_SM, out uint smClock) == NvmlNative.NVML_SUCCESS && smClock > 0)
                        {
                            clock = (int)smClock;
                        }
                    }
                    else
                    {
                        ShutdownNvml();
                    }
                }
                else
                {
                    ShutdownNvml();
                }
            }
        }
        catch
        {
            ShutdownNvml();
        }

        return new GpuTelemetry
        {
            Name = activeName ?? "NVIDIA GeForce RTX",
            IntegratedName = igpuName,
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
        if (_cachedMode == GpuPowerMode.Eco)
        {
            return (false, GpuPowerState.EcoDisabled, null);
        }

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

            bool nvidiaFound = false;
            GpuPowerState nvidiaState = GpuPowerState.Unknown;
            string? nvidiaDesc = null;

            for (uint i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, ref data); i++)
            {
                string? hwId = GetDeviceProperty(hDevInfo, ref data, SpdrpHardwareId);
                string? desc = GetDeviceProperty(hDevInfo, ref data, SpdrpDeviceDesc);

                if (!string.IsNullOrEmpty(hwId))
                {
                    if (hwId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase))
                    {
                        nvidiaFound = true;
                        nvidiaDesc = desc;

                        if (CM_Get_DevNode_Status(out uint status, out uint problem, data.DevInst, 0) == 0)
                        {
                            // 1. Check if device is in problem or disabled state
                            if ((status & 0x400) != 0 || problem != 0)
                            {
                                nvidiaState = _cachedMode == GpuPowerMode.Eco ? GpuPowerState.EcoDisabled : GpuPowerState.Sleeping;
                            }
                            else
                            {
                                // 2. Check runtime device power state via SPDRP_DEVICE_POWER_DATA (CM_POWER_DATA: 1 = D0, 4 = D3 / D3cold)
                                int devPowerState = GetDevicePowerState(hDevInfo, ref data);
                                if (devPowerState == 1)
                                {
                                    nvidiaState = GpuPowerState.Active;
                                }
                                else if (_cachedMode == GpuPowerMode.Eco)
                                {
                                    nvidiaState = GpuPowerState.EcoDisabled;
                                }
                                else
                                {
                                    nvidiaState = GpuPowerState.Sleeping;
                                }
                            }
                        }
                        else
                        {
                            nvidiaState = _cachedMode == GpuPowerMode.Eco ? GpuPowerState.EcoDisabled : GpuPowerState.Sleeping;
                        }
                    }
                    else if (!string.IsNullOrEmpty(desc) && (hwId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) || hwId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)))
                    {
                        _cachedIntegratedName = desc;
                    }
                }
            }

            if (nvidiaFound)
            {
                return (true, nvidiaState, nvidiaDesc);
            }

            return (false, GpuPowerState.EcoDisabled, null);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    private static int GetDevicePowerState(IntPtr hDevInfo, ref SP_DEVINFO_DATA data)
    {
        try
        {
            // CM_POWER_DATA structure: PD_Size (offset 0), PD_MostRecentPowerState (offset 4: 1 = D0, 4 = D3).
            byte[] buffer = new byte[64];
            if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref data, SpdrpDevicePowerData, out _, buffer, (uint)buffer.Length, out uint reqSize)
                && reqSize >= 8)
            {
                return BitConverter.ToInt32(buffer, 4);
            }
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        ShutdownNvml();
        GC.SuppressFinalize(this);
    }

    public void SavePersistedMode(GpuPowerMode mode)
    {
        _cachedMode = mode;
        if (mode == GpuPowerMode.Eco)
        {
            ShutdownNvml();
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegSubKey);
            key?.SetValue(RegValueMode, (int)mode, RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            using var hklmKey = Registry.LocalMachine.CreateSubKey(RegSubKey);
            hklmKey?.SetValue(RegValueMode, (int)mode, RegistryValueKind.DWord);
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

        try
        {
            using var hklmKey = Registry.LocalMachine.OpenSubKey(RegSubKey, writable: false);
            if (hklmKey?.GetValue(RegValueMode) is int val && Enum.IsDefined(typeof(GpuPowerMode), val))
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
