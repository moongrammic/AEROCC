using HidSharp;

namespace AEROCC.Core.Hid;

public class GigabyteKeyboardClient
{
    private const int GigabyteVid = 0x0414;
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
                    // Aero 16 keyboard controller feature report is exactly 9 bytes (mi_03)
                    if (dev.GetMaxFeatureReportLength() == 9)
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
                    // Ignore and try next device
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
                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = 128; // 0x80 ping command
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(20);

                byte[] response = new byte[9];
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

    // Read addresses have high bit set: 142 (0x8E) for FnLock, 143 (0x8F) for WinLock
    // Write addresses: 14 (0x0E) for FnLock, 15 (0x0F) for WinLock
    public bool? GetFnLock() => QueryStatus(142);
    public bool SetFnLock(bool locked) => SendCommand(14, locked);

    public bool? GetWinLock() => QueryStatus(143);
    public bool SetWinLock(bool locked) => SendCommand(15, locked);

    private bool? QueryStatus(byte address)
    {
        if (_keyboardDevice == null && !Initialize()) return null;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return null;

        using (stream)
        {
            try
            {
                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = address;
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(20);

                byte[] response = new byte[9];
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
                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = address;
                report[2] = (byte)(value ? 1 : 0);
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(50);
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
