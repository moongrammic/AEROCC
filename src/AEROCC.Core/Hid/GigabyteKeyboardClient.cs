using AEROCC.Core.Models;
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

    public KeyboardBacklightStatus? GetBacklightStatus()
    {
        if (_keyboardDevice == null && !Initialize()) return null;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return null;

        using (stream)
        {
            try
            {
                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = 136; // 0x88 query light effect zone RGB
                report[2] = 2;
                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(25);

                byte[] response = new byte[9];
                response[0] = 0;
                stream.GetFeature(response);

                return new KeyboardBacklightStatus
                {
                    Effect = (KeyboardEffect)response[3],
                    Speed = response[4],
                    Brightness = Math.Clamp(response[5] * 2, 0, 100),
                    Color = (KeyboardColor)response[6],
                    Direction = response[7]
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HID Backlight Query Error]: {ex.Message}");
                return null;
            }
        }
    }

    public bool SetBacklight(KeyboardEffect effect, byte brightnessPercent, KeyboardColor color = KeyboardColor.White, byte speed = 5, byte direction = 1)
    {
        if (_keyboardDevice == null && !Initialize()) return false;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return false;

        using (stream)
        {
            try
            {
                byte hwBrightness = (byte)Math.Clamp(brightnessPercent / 2, 0, 50);

                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = 8; // 0x08 command: set light effect
                report[2] = 0; // sync
                report[3] = (byte)effect;
                report[4] = speed;
                report[5] = hwBrightness;
                report[6] = (byte)color;
                report[7] = direction;

                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(50);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HID SetBacklight Error]: {ex.Message}");
                return false;
            }
        }
    }

    public bool SetBrightness(byte brightnessPercent)
    {
        var current = GetBacklightStatus();
        var effect = current?.Effect ?? KeyboardEffect.Static;
        var color = current?.Color ?? KeyboardColor.White;
        var speed = (byte)(current?.Speed ?? 5);
        var dir = (byte)(current?.Direction ?? 1);

        return SetBacklight(effect, brightnessPercent, color, speed, dir);
    }

    public bool SetCustomColor(byte r, byte g, byte b, byte brightnessPercent = 100)
    {
        if (_keyboardDevice == null && !Initialize()) return false;
        if (_keyboardDevice == null || !_keyboardDevice.TryOpen(out var stream)) return false;

        using (stream)
        {
            try
            {
                // 1. Send custom RGB color report (0x14 = 20)
                byte[] report = new byte[9];
                report[0] = 0;
                report[1] = 20; // 0x14
                report[2] = 0;
                report[3] = 0; // areaIndex: whole keyboard
                report[4] = r;
                report[5] = g;
                report[6] = b;
                report[7] = 0;

                byte sum = 0;
                for (int i = 1; i <= 7; i++) sum += report[i];
                report[8] = (byte)(255 - sum);

                stream.SetFeature(report);
                Thread.Sleep(50);

                // 2. Set Light Effect to Custom (9) with target brightness
                byte hwBrightness = (byte)Math.Clamp(brightnessPercent / 2, 0, 50);
                byte[] effReport = new byte[9];
                effReport[0] = 0;
                effReport[1] = 8;
                effReport[2] = 0;
                effReport[3] = (byte)KeyboardEffect.Custom;
                effReport[4] = 5;
                effReport[5] = hwBrightness;
                effReport[6] = 7;
                effReport[7] = 1;

                byte effSum = 0;
                for (int i = 1; i <= 7; i++) effSum += effReport[i];
                effReport[8] = (byte)(255 - effSum);

                stream.SetFeature(effReport);
                Thread.Sleep(50);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HID SetCustomColor Error]: {ex.Message}");
                return false;
            }
        }
    }

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
