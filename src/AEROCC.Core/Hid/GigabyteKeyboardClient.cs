using HidSharp;

namespace AEROCC.Core.Hid;

public class GigabyteKeyboardClient
{
    private const int GigabyteVid = 0x0414;
    private static readonly HashSet<int> KnownPids = new()
    {
        0x8100, 0x8101, 0x8102, 0x8103, 0x8104, 0x8105, 0x8106, 0x8107,
        0x8200, 0x8201, 0x8202, 0x8203, 0x8204, 0x8205, 0x8206, 0x8207
    };

    private HidDevice? _keyboardDevice;

    public bool IsConnected => _keyboardDevice != null;

    public bool Initialize()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(GigabyteVid);
            foreach (var dev in devices)
            {
                try
                {
                    if (dev.GetMaxFeatureReportLength() >= 9)
                    {
                        if (TestKeyboardDevice(dev))
                        {
                            _keyboardDevice = dev;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Continue searching
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HID Error] Failed to enumerate HID devices: {ex.Message}");
        }

        return false;
    }

    private static bool TestKeyboardDevice(HidDevice dev)
    {
        if (!dev.TryOpen(out var stream)) return false;
        using (stream)
        {
            try
            {
                int len = Math.Max(9, dev.GetMaxFeatureReportLength());
                byte[] report = new byte[len];
                report[0] = 0;
                report[1] = 128; // 0x80 test ping
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);

                byte[] response = new byte[len];
                response[0] = 0;
                stream.GetFeature(response);
                return response[1] == 128;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool? GetFnLock() => QueryStatus(14);
    public bool SetFnLock(bool locked) => SendCommand(14, locked);

    public bool? GetWinLock() => QueryStatus(15);
    public bool SetWinLock(bool locked) => SendCommand(15, locked);

    private bool? QueryStatus(byte address)
    {
        if (_keyboardDevice == null && !Initialize()) return null;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return null;

        using (stream)
        {
            try
            {
                int len = Math.Max(9, _keyboardDevice.GetMaxFeatureReportLength());
                byte[] report = new byte[len];
                report[0] = 0;
                report[1] = address;
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);

                byte[] response = new byte[len];
                response[0] = 0;
                stream.GetFeature(response);
                return response[2] != 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HID Query Error] Address {address}: {ex.Message}");
                return null;
            }
        }
    }

    private bool SendCommand(byte address, bool value)
    {
        if (_keyboardDevice == null && !Initialize()) return false;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return false;

        using (stream)
        {
            try
            {
                int len = Math.Max(9, _keyboardDevice.GetMaxFeatureReportLength());
                byte[] report = new byte[len];
                report[0] = 0;
                report[1] = address;
                report[2] = (byte)(value ? 1 : 0);
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HID Send Error] Address {address}: {ex.Message}");
                return false;
            }
        }
    }
}
