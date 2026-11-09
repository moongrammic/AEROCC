using System.Runtime.InteropServices;
using System.Text;

namespace AEROCC.Core.Gpu;

internal static class NvmlNative
{
    private const string NvmlDll = "nvml.dll";

    public const int NVML_SUCCESS = 0;
    public const int NVML_TEMPERATURE_GPU = 0;
    public const int NVML_CLOCK_GRAPHICS = 0;
    public const int NVML_CLOCK_SM = 1;
    public const int NVML_CLOCK_MEM = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlInit_v2();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlShutdown();

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetCount_v2(ref int deviceCount);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetHandleByIndex_v2(int index, out IntPtr device);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMilliWatts);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMHz);

    [DllImport(NvmlDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr nvmlErrorString(int result);
}
