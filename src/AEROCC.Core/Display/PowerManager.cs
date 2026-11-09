using System.Runtime.InteropServices;

namespace AEROCC.Core.Display;

public enum PowerLineStatus : byte
{
    Offline = 0, // On Battery
    Online = 1,  // Plugged into AC
    Unknown = 255
}

public class SystemPowerInfo
{
    public PowerLineStatus LineStatus { get; set; } = PowerLineStatus.Unknown;
    public int BatteryPercent { get; set; } = 100;
    public bool IsCharging { get; set; }

    public override string ToString() =>
        $"{(LineStatus == PowerLineStatus.Online ? "Сеть (AC)" : "Батарея (DC)")} | {BatteryPercent}%";
}

public static class PowerManager
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public static SystemPowerInfo GetPowerInfo()
    {
        if (GetSystemPowerStatus(out var sps))
        {
            return new SystemPowerInfo
            {
                LineStatus = sps.ACLineStatus switch
                {
                    0 => PowerLineStatus.Offline,
                    1 => PowerLineStatus.Online,
                    _ => PowerLineStatus.Unknown
                },
                BatteryPercent = sps.BatteryLifePercent <= 100 ? sps.BatteryLifePercent : 100,
                IsCharging = (sps.BatteryFlag & 8) != 0
            };
        }

        return new SystemPowerInfo();
    }
}
