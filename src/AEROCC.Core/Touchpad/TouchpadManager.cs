using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AEROCC.Core.Touchpad;

public class TouchpadManager
{
    private static TouchpadManager? _instance;
    public static TouchpadManager Instance => _instance ??= new TouchpadManager();

    private const string TouchpadKeyPath = @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status";
    private const string EnabledValueName = "Enabled";

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 2;
    private const ushort VK_LCONTROL = 162;
    private const ushort VK_LWIN = 91;
    private const ushort VK_F24 = 135;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        private short padding1;
        private short padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public event Action<bool>? TouchpadStateChanged;

    public bool IsTouchpadEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TouchpadKeyPath, writable: false);
            if (key != null)
            {
                var val = key.GetValue(EnabledValueName);
                if (val != null)
                {
                    return Convert.ToInt32(val) != 0;
                }
            }
        }
        catch
        {
        }
        return true; // Default fallback to true
    }

    public void ToggleTouchpad()
    {
        try
        {
            INPUT[] inputs = new INPUT[6]
            {
                MakeKeyDown(VK_LCONTROL),
                MakeKeyDown(VK_LWIN),
                MakeKeyDown(VK_F24),
                MakeKeyUp(VK_F24),
                MakeKeyUp(VK_LWIN),
                MakeKeyUp(VK_LCONTROL)
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        catch
        {
        }
    }

    public void NotifyStateChanged(bool enabled)
    {
        TouchpadStateChanged?.Invoke(enabled);
    }

    private static INPUT MakeKeyDown(ushort vk)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk }
        };
    }

    private static INPUT MakeKeyUp(ushort vk)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
        };
    }
}
