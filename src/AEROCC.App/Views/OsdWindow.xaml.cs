using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AEROCC.App.Views;

public partial class OsdWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _hideTimer;
    private readonly Storyboard _showAnim;
    private readonly Storyboard _hideAnim;

    public OsdWindow()
    {
        InitializeComponent();

        _showAnim = (Storyboard)Resources["ShowAnim"];
        _hideAnim = (Storyboard)Resources["HideAnim"];

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1800)
        };
        _hideTimer.Tick += OnHideTimerTick;

        _hideAnim.Completed += (s, e) =>
        {
            Hide();
        };

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Make the window click-through and avoid taskbar/stealing focus
        var helper = new WindowInteropHelper(this);
        int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        _hideAnim.Begin();
    }

    public void ShowNotification(string title, string message, SymbolRegular symbol, Color accentColor)
    {
        Dispatcher.Invoke(() =>
        {
            _hideTimer.Stop();

            OsdTitle.Text = title;
            OsdMessage.Text = message;
            OsdIcon.Symbol = symbol;
            OsdIcon.Foreground = new SolidColorBrush(accentColor);
            IconBg.Fill = new SolidColorBrush(Color.FromArgb(45, accentColor.R, accentColor.G, accentColor.B));

            // Position at top-center of primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            UpdateLayout();
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Left = (screenWidth - ActualWidth) / 2;
            Top = 25;

            Show();
            _showAnim.Begin();
            _hideTimer.Start();
        });
    }
}
