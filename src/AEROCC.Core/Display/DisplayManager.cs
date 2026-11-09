using System.Runtime.InteropServices;

namespace AEROCC.Core.Display;

public class DisplayInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceString { get; set; } = string.Empty;
    public int CurrentWidth { get; set; }
    public int CurrentHeight { get; set; }
    public int CurrentFrequency { get; set; }
    public List<int> AvailableFrequencies { get; set; } = new();
    public bool IsPrimary { get; set; }

    public override string ToString() =>
        $"{DeviceName} ({DeviceString}): {CurrentWidth}x{CurrentHeight} @ {CurrentFrequency}Hz [Modes: {string.Join(", ", AvailableFrequencies)}Hz]";
}

public static class DisplayManager
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    public static List<DisplayInfo> GetDisplays()
    {
        var result = new List<DisplayInfo>();
        var dispDev = new DISPLAY_DEVICE();
        dispDev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

        for (uint i = 0; EnumDisplayDevices(null, i, ref dispDev, 0); i++)
        {
            if ((dispDev.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                var info = new DisplayInfo
                {
                    DeviceName = dispDev.DeviceName,
                    DeviceString = dispDev.DeviceString,
                    IsPrimary = (dispDev.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0
                };

                // Query monitor specific name if available
                var monDev = new DISPLAY_DEVICE();
                monDev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevices(dispDev.DeviceName, 0, ref monDev, 0))
                {
                    if (!string.IsNullOrWhiteSpace(monDev.DeviceString))
                    {
                        info.DeviceString = monDev.DeviceString;
                    }
                }

                // Query current mode
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(info.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    info.CurrentWidth = dm.dmPelsWidth;
                    info.CurrentHeight = dm.dmPelsHeight;
                    info.CurrentFrequency = dm.dmDisplayFrequency;
                }

                // Query all available frequencies for the current native resolution
                var freqSet = new SortedSet<int>();
                int modeNum = 0;
                while (EnumDisplaySettings(info.DeviceName, modeNum++, ref dm))
                {
                    if (dm.dmPelsWidth == info.CurrentWidth && dm.dmPelsHeight == info.CurrentHeight)
                    {
                        if (dm.dmDisplayFrequency > 0)
                        {
                            freqSet.Add(dm.dmDisplayFrequency);
                        }
                    }
                }

                info.AvailableFrequencies = freqSet.ToList();
                result.Add(info);
            }

            dispDev = new DISPLAY_DEVICE();
            dispDev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
        }

        return result;
    }

    public static DisplayInfo? GetPrimaryDisplay()
    {
        var displays = GetDisplays();
        return displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
    }

    public static bool SetRefreshRate(int targetHz, string? deviceName = null)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            var primary = GetPrimaryDisplay();
            deviceName = primary?.DeviceName;
        }

        if (string.IsNullOrEmpty(deviceName))
        {
            return false;
        }

        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

        if (!EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref dm))
        {
            return false;
        }

        if (dm.dmDisplayFrequency == targetHz)
        {
            return true; // Already set
        }

        dm.dmDisplayFrequency = targetHz;
        dm.dmFields = DM_DISPLAYFREQUENCY;

        int res = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        return res == DISP_CHANGE_SUCCESSFUL;
    }
}
