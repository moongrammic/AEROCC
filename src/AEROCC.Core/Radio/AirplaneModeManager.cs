using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AEROCC.Core.Radio;

public class AirplaneModeManager
{
    private static AirplaneModeManager? _instance;
    public static AirplaneModeManager Instance => _instance ??= new AirplaneModeManager();

    private const string RadioStateKeyPath = @"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState";
    private const ushort VK_F21 = 0x84; // Standard Windows Radio toggle key

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public event Action<bool>? AirplaneModeChanged;

    public bool IsAirplaneModeActive()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RadioStateKeyPath, writable: false);
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    var val = key.GetValue(name);
                    if (val != null && Convert.ToInt32(val) != 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
        }
        return false;
    }

    public void ToggleAirplaneMode()
    {
        try
        {
            keybd_event((byte)VK_F21, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_F21, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
        }
    }

    public void NotifyStateChanged(bool active)
    {
        AirplaneModeChanged?.Invoke(active);
    }
}
