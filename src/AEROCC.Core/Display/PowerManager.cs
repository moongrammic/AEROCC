using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

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

public record EnergyTelemetry
{
    public double? CpuPackageWatts { get; init; }
    public double? BatteryChargeWatts { get; init; }
    public double? BatteryDischargeWatts { get; init; }
    public bool IsAcOnline { get; init; }
    public bool IsCharging { get; init; }
    public bool IsDischarging { get; init; }
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

    // Windows 11 24H2/25H2 Energy Saver WNF states (verified via SettingsHandlers_OneCore_BatterySaver / SystemTray / Kernel Pop)
    private const ulong WnfEnergySaverPolicy = 0x41c6013da3bc3075UL;
    private const ulong WnfEnergySaverActive = 0x41c6013da3bdc875UL;
    private const uint EnergySaverPolicyOn = 1u;
    private const uint EnergySaverPolicyOff = 2u;

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryWnfStateData(
        in ulong stateName,
        IntPtr typeId,
        IntPtr explicitScope,
        out uint changeStamp,
        out uint buffer,
        ref uint bufferSize);

    [DllImport("ntdll.dll")]
    private static extern uint RtlPublishWnfStateData(
        ulong stateName,
        IntPtr typeId,
        ref uint buffer,
        uint length,
        IntPtr explicitScope);

    public static bool GetEnergySaverEnabled()
    {
        if (TryQueryWnfUInt32(WnfEnergySaverActive, out uint activeFlag))
            return activeFlag != 0;

        if (TryQueryWnfUInt32(WnfEnergySaverPolicy, out uint policyState))
            return policyState == EnergySaverPolicyOn;

        try
        {
            if (Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power",
                    "EnergySaverState",
                    null) is int regState)
            {
                return regState == (int)EnergySaverPolicyOn;
            }
        }
        catch { }

        return GetSystemPowerStatus(out var sps) && (sps.SystemStatusFlag & 1) != 0;
    }

    public static bool SetEnergySaverEnabled(bool enabled)
    {
        uint state = enabled ? EnergySaverPolicyOn : EnergySaverPolicyOff;
        uint status = RtlPublishWnfStateData(WnfEnergySaverPolicy, IntPtr.Zero, ref state, sizeof(uint), IntPtr.Zero);
        return status == 0;
    }

    private static bool TryQueryWnfUInt32(ulong stateName, out uint value)
    {
        uint size = sizeof(uint);
        uint status = NtQueryWnfStateData(in stateName, IntPtr.Zero, IntPtr.Zero, out _, out value, ref size);
        return status == 0 && size >= sizeof(uint);
    }

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

    private static readonly CpuPackageEnergyMeter _cpuMeter = new();

    public static EnergyTelemetry GetEnergyTelemetry()
    {
        var sys = GetPowerInfo();
        double? cpuWatts = _cpuMeter.TrySamplePackageWatts();
        var (powerOnline, charging, discharging, chargeW, dischargeW) = ReadBatteryRates();

        return new EnergyTelemetry
        {
            CpuPackageWatts = cpuWatts,
            BatteryChargeWatts = chargeW,
            BatteryDischargeWatts = dischargeW,
            IsAcOnline = powerOnline ?? (sys.LineStatus == PowerLineStatus.Online),
            IsCharging = charging ?? sys.IsCharging,
            IsDischarging = discharging ?? (sys.LineStatus == PowerLineStatus.Offline)
        };
    }

    private static (bool? PowerOnline, bool? Charging, bool? Discharging, double? ChargeW, double? DischargeW) ReadBatteryRates()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\WMI",
                "SELECT PowerOnline, Charging, Discharging, ChargeRate, DischargeRate FROM BatteryStatus");

            bool any = false;
            bool powerOnline = false;
            bool charging = false;
            bool discharging = false;
            double totalChargeMw = 0;
            double totalDischargeMw = 0;

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    any = true;
                    powerOnline |= obj["PowerOnline"] is bool po && po;
                    charging |= obj["Charging"] is bool ch && ch;
                    discharging |= obj["Discharging"] is bool dc && dc;

                    if (obj["ChargeRate"] != null)
                    {
                        uint raw = Convert.ToUInt32(obj["ChargeRate"]);
                        if (raw > 0 && raw < 500_000) totalChargeMw += raw;
                    }

                    if (obj["DischargeRate"] != null)
                    {
                        uint raw = Convert.ToUInt32(obj["DischargeRate"]);
                        if (raw > 0 && raw < 500_000) totalDischargeMw += raw;
                    }
                }
            }

            if (!any) return default;

            return (
                powerOnline,
                charging,
                discharging,
                charging && totalChargeMw > 0 ? Math.Round(totalChargeMw / 1000.0, 1) : null,
                discharging && totalDischargeMw > 0 ? Math.Round(totalDischargeMw / 1000.0, 1) : null
            );
        }
        catch
        {
            return default;
        }
    }

    private sealed class CpuPackageEnergyMeter : IDisposable
    {
        private static readonly Guid GuidDeviceEnergyMeter = new("45bd8344-7ed6-49cf-a440-c276c933b053");
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareReadWrite = 0x00000003;
        private const uint OpenExisting = 3;

        private const uint IoctlEmiGetVersion = 0x00224000;
        private const uint IoctlEmiGetMetadataSize = 0x00224004;
        private const uint IoctlEmiGetMetadata = 0x00224008;
        private const uint IoctlEmiGetMeasurement = 0x0022400C;
        private const uint EmiUnitPicowattHours = 0;

        private readonly object _lock = new();
        private SafeFileHandle? _handle;
        private int _channelCount;
        private int _pkgChannelIndex = -1;
        private bool _hasBaseline;
        private ulong _prevEnergy;
        private ulong _prevTicks;
        private double? _lastWatts;
        private DateTime _lastInitAttempt = DateTime.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            byte[]? DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        public double? TrySamplePackageWatts()
        {
            lock (_lock)
            {
                if (!EnsureOpen()) return null;

                byte[] buf = new byte[_channelCount * 16];
                if (!DeviceIoControl(_handle!, IoctlEmiGetMeasurement, IntPtr.Zero, 0, buf, (uint)buf.Length, out uint br, IntPtr.Zero)
                    || br < (uint)(_pkgChannelIndex * 16 + 16))
                {
                    CloseHandleLocked();
                    return _lastWatts;
                }

                int off = _pkgChannelIndex * 16;
                ulong energy = BitConverter.ToUInt64(buf, off);
                ulong ticks = BitConverter.ToUInt64(buf, off + 8);

                if (!_hasBaseline)
                {
                    _prevEnergy = energy;
                    _prevTicks = ticks;
                    _hasBaseline = true;
                    return _lastWatts;
                }

                if (ticks > _prevTicks)
                {
                    ulong dT = ticks - _prevTicks;
                    // Only commit sample when delta >= 150ms so rapid calls don't amplify quantization noise
                    if (dT >= 1_500_000)
                    {
                        if (energy >= _prevEnergy)
                        {
                            double seconds = dT / 10_000_000.0;
                            double watts = ((energy - _prevEnergy) * 3.6e-9) / seconds;
                            if (watts >= 0.0 && watts <= 300.0)
                            {
                                _lastWatts = Math.Round(watts, 1);
                            }
                        }

                        _prevEnergy = energy;
                        _prevTicks = ticks;
                    }
                }
                else
                {
                    _prevEnergy = energy;
                    _prevTicks = ticks;
                }

                return _lastWatts;
            }
        }

        private bool EnsureOpen()
        {
            if (_handle is { IsInvalid: false, IsClosed: false } && _pkgChannelIndex >= 0)
            {
                return true;
            }

            if ((DateTime.UtcNow - _lastInitAttempt).TotalSeconds < 3)
            {
                return false;
            }
            _lastInitAttempt = DateTime.UtcNow;

            Guid guid = GuidDeviceEnergyMeter;
            IntPtr hSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (hSet == IntPtr.Zero || hSet == new IntPtr(-1))
            {
                return false;
            }

            try
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                };

                for (uint idx = 0; SetupDiEnumDeviceInterfaces(hSet, IntPtr.Zero, ref guid, idx, ref ifData); idx++)
                {
                    SetupDiGetDeviceInterfaceDetail(hSet, ref ifData, null, 0, out uint reqSize, IntPtr.Zero);
                    if (reqSize <= 6) continue;

                    byte[] detail = new byte[reqSize];
                    uint cbDetail = (uint)(IntPtr.Size == 8 ? 8 : 6);
                    BitConverter.GetBytes(cbDetail).CopyTo(detail, 0);

                    if (!SetupDiGetDeviceInterfaceDetail(hSet, ref ifData, detail, reqSize, out _, IntPtr.Zero))
                    {
                        continue;
                    }

                    string path = Encoding.Unicode.GetString(detail, 4, (int)reqSize - 4).TrimEnd('\0');
                    var h = CreateFile(path, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (h.IsInvalid)
                    {
                        h.Dispose();
                        continue;
                    }

                    if (TrySelectPackageChannel(h, out int channels, out int pkgIndex))
                    {
                        _handle = h;
                        _channelCount = channels;
                        _pkgChannelIndex = pkgIndex;
                        _hasBaseline = false;

                        // Seed initial measurement baseline immediately
                        byte[] initBuf = new byte[_channelCount * 16];
                        if (DeviceIoControl(_handle, IoctlEmiGetMeasurement, IntPtr.Zero, 0, initBuf, (uint)initBuf.Length, out uint br, IntPtr.Zero)
                            && br >= (uint)(_pkgChannelIndex * 16 + 16))
                        {
                            _prevEnergy = BitConverter.ToUInt64(initBuf, _pkgChannelIndex * 16);
                            _prevTicks = BitConverter.ToUInt64(initBuf, _pkgChannelIndex * 16 + 8);
                            _hasBaseline = true;
                        }

                        return true;
                    }

                    h.Dispose();
                }
            }
            catch { }
            finally
            {
                SetupDiDestroyDeviceInfoList(hSet);
            }

            return false;
        }

        private static bool TrySelectPackageChannel(SafeFileHandle handle, out int channels, out int pkgIndex)
        {
            channels = 0;
            pkgIndex = -1;

            byte[] verBuf = new byte[4];
            if (!DeviceIoControl(handle, IoctlEmiGetVersion, IntPtr.Zero, 0, verBuf, (uint)verBuf.Length, out uint br, IntPtr.Zero) || br < 2)
            {
                return false;
            }

            ushort version = BitConverter.ToUInt16(verBuf, 0);
            if (version != 2) return false;

            byte[] sizeBuf = new byte[4];
            if (!DeviceIoControl(handle, IoctlEmiGetMetadataSize, IntPtr.Zero, 0, sizeBuf, (uint)sizeBuf.Length, out br, IntPtr.Zero) || br < 4)
            {
                return false;
            }

            uint metaSize = BitConverter.ToUInt32(sizeBuf, 0);
            if (metaSize < 68 || metaSize > 65536) return false;

            byte[] meta = new byte[metaSize];
            if (!DeviceIoControl(handle, IoctlEmiGetMetadata, IntPtr.Zero, 0, meta, metaSize, out br, IntPtr.Zero) || br < 68)
            {
                return false;
            }

            channels = BitConverter.ToUInt16(meta, 66);
            int fallbackPkg = -1;

            for (int i = 0; i < channels; i++)
            {
                int off = 68 + i * 106;
                if (off + 106 > br) break;

                uint unit = BitConverter.ToUInt32(meta, off);
                if (unit != EmiUnitPicowattHours) continue;

                string chName = Encoding.Unicode.GetString(meta, off + 4, 102).TrimEnd('\0');
                if (chName.Contains("Package0_PKG", StringComparison.OrdinalIgnoreCase))
                {
                    pkgIndex = i;
                    return true;
                }

                if (fallbackPkg < 0 && chName.Contains("PKG", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackPkg = i;
                }
            }

            if (fallbackPkg >= 0)
            {
                pkgIndex = fallbackPkg;
                return true;
            }

            return false;
        }

        private void CloseHandleLocked()
        {
            _handle?.Dispose();
            _handle = null;
            _channelCount = 0;
            _pkgChannelIndex = -1;
            _hasBaseline = false;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                CloseHandleLocked();
            }
        }
    }
}
