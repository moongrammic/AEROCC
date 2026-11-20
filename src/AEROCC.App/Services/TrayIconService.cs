using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SDColor = System.Drawing.Color;
using SDPoint = System.Drawing.Point;

namespace AEROCC.App.Services;

public class TrayIconService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    private HwndSource? _hwndSource;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isCreated;
    private uint _wmTaskbarCreated = 0;
    private string _currentTooltip = "AEROCC - Gigabyte Aero 16 (1VH)";
    private const int TrayId = 1001;

    public event Action? LeftClick;
    public event Action? RightClick;

    public void Initialize()
    {
        if (_isCreated) return;

        // Register TaskbarCreated message to restore icon if Explorer crashes
        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // Create invisible message-only HWND
        var parameters = new HwndSourceParameters("AEROCC_TrayListener")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = 0x08000000 /* WS_EX_NOACTIVATE */,
            Width = 0,
            Height = 0
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        _hIcon = CreateAeroIcon();
        RecreateIcon();
    }

    private void RecreateIcon()
    {
        if (_hwndSource == null) return;
        if (_hIcon == IntPtr.Zero) _hIcon = CreateAeroIcon();

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = TrayId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = _currentTooltip
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public void UpdateTooltip(string text)
    {
        _currentTooltip = text.Length > 127 ? text[..127] : text;
        if (!_isCreated || _hwndSource == null) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwndSource.Handle,
            uID = TrayId,
            uFlags = NIF_TIP,
            szTip = _currentTooltip
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _wmTaskbarCreated && _wmTaskbarCreated != 0)
        {
            RecreateIcon();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_TRAYICON)
        {
            int lparamVal = lParam.ToInt32();
            if (lparamVal == WM_LBUTTONUP)
            {
                LeftClick?.Invoke();
                handled = true;
            }
            else if (lparamVal == WM_RBUTTONUP)
            {
                if (_hwndSource != null)
                {
                    SetForegroundWindow(_hwndSource.Handle);
                }
                RightClick?.Invoke();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr CreateAeroIcon()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/aero-badge.png");
            var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
            if (streamInfo != null)
            {
                using var bmp = new Bitmap(streamInfo.Stream);
                using var resized = new Bitmap(bmp, new Size(32, 32));
                return resized.GetHicon();
            }
        }
        catch { }

        try
        {
            var localBadge = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "aero-badge.png");
            if (System.IO.File.Exists(localBadge))
            {
                using var bmp = new Bitmap(localBadge);
                using var resized = new Bitmap(bmp, new Size(32, 32));
                return resized.GetHicon();
            }
        }
        catch { }

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SDColor.Transparent);

            // Sleek orange / dark gradient badge
            using var brush = new LinearGradientBrush(
                new SDPoint(0, 0),
                new SDPoint(32, 32),
                SDColor.FromArgb(255, 120, 20),
                SDColor.FromArgb(255, 60, 0));

            g.FillEllipse(brush, 1, 1, 30, 30);
        }

        return bitmap.GetHicon();
    }

    public void Dispose()
    {
        if (_isCreated && _hwndSource != null)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = TrayId
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isCreated = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        _hwndSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}
