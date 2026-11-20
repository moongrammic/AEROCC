using AEROCC.Core.Models;
using HidSharp;

namespace AEROCC.Core.Hid;

public class GigabyteKeyboardClient
{
    private const int GigabyteVid = 0x0414;
    private HidDevice? _keyboardDevice;

    private KeyboardEffect _lastEffect = KeyboardEffect.Static;
    private KeyboardColor _lastColor = KeyboardColor.White;
    private byte _lastSpeed = 5;
    private byte _lastDirection = 1;
    private byte _lastBrightness = 100;

    private readonly object _lock = new();

    public bool IsConnected => _keyboardDevice != null;
    public byte LastBrightness => _lastBrightness;
    public KeyboardEffect LastEffect => _lastEffect;
    public KeyboardColor LastColor => _lastColor;

    public bool Initialize()
    {
        lock (_lock)
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
    }

    private bool TryOpenStream(out HidStream? stream)
    {
        stream = null;
        if (_keyboardDevice != null && _keyboardDevice.TryOpen(out stream))
        {
            return true;
        }

        // Previous device handle invalid (e.g. sleep/reconnect), re-initialize
        _keyboardDevice = null;
        if (Initialize() && _keyboardDevice != null && _keyboardDevice.TryOpen(out stream))
        {
            return true;
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
        lock (_lock)
        {
            if (!TryOpenStream(out var stream) || stream == null) return null;

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

                    byte br = (byte)Math.Clamp(response[5] * 2, 0, 100);
                    _lastBrightness = br;
                    _lastEffect = (KeyboardEffect)response[3];
                    _lastSpeed = response[4];
                    _lastColor = (KeyboardColor)response[6];
                    _lastDirection = response[7];

                    return new KeyboardBacklightStatus
                    {
                        Effect = _lastEffect,
                        Speed = _lastSpeed,
                        Brightness = br,
                        Color = _lastColor,
                        Direction = _lastDirection
                    };
                }
                catch (Exception ex)
                {
                    _keyboardDevice = null;
                    Console.WriteLine($"[HID Backlight Query Error]: {ex.Message}");
                    return null;
                }
            }
        }
    }

    public bool SetBacklight(KeyboardEffect effect, byte brightnessPercent, KeyboardColor color = KeyboardColor.White, byte speed = 5, byte direction = 1)
    {
        lock (_lock)
        {
            _lastEffect = effect;
            _lastBrightness = brightnessPercent;
            _lastColor = color;
            _lastSpeed = speed;
            _lastDirection = direction;

            if (!TryOpenStream(out var stream) || stream == null) return false;

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
                    Thread.Sleep(20);
                    return true;
                }
                catch (Exception ex)
                {
                    _keyboardDevice = null;
                    Console.WriteLine($"[HID SetBacklight Error]: {ex.Message}");
                    return false;
                }
            }
        }
    }

    public bool SetBrightness(byte brightnessPercent)
    {
        return SetBacklight(_lastEffect, brightnessPercent, _lastColor, _lastSpeed, _lastDirection);
    }

    public bool SetCustomColor(byte r, byte g, byte b, byte brightnessPercent = 100)
    {
        lock (_lock)
        {
            _lastBrightness = brightnessPercent;
            _lastEffect = KeyboardEffect.Custom;

            if (!TryOpenStream(out var stream) || stream == null) return false;

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
                    _keyboardDevice = null;
                    Console.WriteLine($"[HID SetCustomColor Error]: {ex.Message}");
                    return false;
                }
            }
        }
    }

    private bool? QueryStatus(byte address)
    {
        lock (_lock)
        {
            if (!TryOpenStream(out var stream) || stream == null) return null;

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
                    _keyboardDevice = null;
                    Console.WriteLine($"[HID Query Error] Address {address}: {ex.Message}");
                    return null;
                }
            }
        }
    }

    private bool SendCommand(byte address, bool value)
    {
        lock (_lock)
        {
            if (!TryOpenStream(out var stream) || stream == null) return false;

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
                    _keyboardDevice = null;
                    Console.WriteLine($"[HID Send Error] Address {address}: {ex.Message}");
                    return false;
                }
            }
        }
    }
}
