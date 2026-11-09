using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AEROCC.App.Services;
using AEROCC.Core.Display;
using AEROCC.Core.Models;
using Wpf.Ui.Controls;

namespace AEROCC.App;

public partial class MainWindow : Window
{
    private readonly HardwareMonitor _monitor = HardwareMonitor.Instance;
    private readonly TrayIconService _trayIcon = new();
    private bool _isPinned = false;
    private bool _isUpdatingUi = false;

    public MainWindow()
    {
        InitializeComponent();

        _monitor.StatusUpdated += OnStatusUpdated;

        // Initialize tray icon
        _trayIcon.Initialize();
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.RightClick += OnTrayRightClick;

        Deactivated += OnWindowDeactivated;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearTray();
        _monitor.Start();
    }

    private void OnStatusUpdated(HardwareStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            _isUpdatingUi = true;
            try
            {
                CpuTempText.Text = $"{status.CpuTemperature} °C";
                Fan1RpmText.Text = $"{status.Fan1Rpm} RPM";
                Fan2RpmText.Text = $"{status.Fan2Rpm} RPM";

                var power = PowerManager.GetPowerInfo();
                PowerInfoText.Text = $"⚡ {power} • Лимит: {status.BatteryChargeLimit}%";

                // Update Battery limit buttons
                BtnBattery60.Tag = status.BatteryChargeLimit == 60 ? "Active" : null;
                BtnBattery80.Tag = status.BatteryChargeLimit == 80 ? "Active" : null;
                BtnBattery100.Tag = status.BatteryChargeLimit == 100 ? "Active" : null;

                // Update Refresh rate buttons
                BtnRefresh60.Tag = status.DisplayRefreshRate == 60 ? "Active" : null;
                BtnRefresh165.Tag = status.DisplayRefreshRate == 165 ? "Active" : null;

                // Update Toggles without re-triggering events
                if (status.WinKeyLocked.HasValue)
                {
                    WinLockToggle.IsChecked = status.WinKeyLocked.Value;
                }
                if (status.FnKeyLocked.HasValue)
                {
                    FnLockToggle.IsChecked = status.FnKeyLocked.Value;
                }

                // Update Backlight
                if (status.BacklightBrightness >= 0)
                {
                    BacklightSlider.Value = status.BacklightBrightness;
                    BacklightPercentText.Text = $"{status.BacklightBrightness}%";
                }

                // Update IPC status
                if (_monitor.IsIpcConnected)
                {
                    IpcIndicator.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    IpcStatusText.Text = "Служба активна (IPC)";
                }
                else
                {
                    IpcIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                    IpcStatusText.Text = "Прямой доступ к ядру";
                }

                _trayIcon.UpdateTooltip($"AEROCC (Aero 16 1VH)\nCPU: {status.CpuTemperature}°C | Fan1: {status.Fan1Rpm} | {status.DisplayRefreshRate}Hz");
            }
            finally
            {
                _isUpdatingUi = false;
            }
        });
    }

    private void PositionNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
    }

    private void OnTrayLeftClick()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                PositionNearTray();
                Show();
                Activate();
            }
        });
    }

    private void OnTrayRightClick()
    {
        Dispatcher.Invoke(() =>
        {
            var menu = new ContextMenu();

            var openItem = new System.Windows.Controls.MenuItem { Header = "Открыть AEROCC" };
            openItem.Click += (s, e) => { PositionNearTray(); Show(); Activate(); };
            menu.Items.Add(openItem);

            menu.Items.Add(new Separator());

            var ecoItem = new System.Windows.Controls.MenuItem { Header = "Режим: Eco" };
            ecoItem.Click += async (s, e) => await _monitor.SetFanProfileAsync(FanProfile.Eco);
            menu.Items.Add(ecoItem);

            var normItem = new System.Windows.Controls.MenuItem { Header = "Режим: Normal" };
            normItem.Click += async (s, e) => await _monitor.SetFanProfileAsync(FanProfile.Normal);
            menu.Items.Add(normItem);

            var turboItem = new System.Windows.Controls.MenuItem { Header = "Режим: Turbo" };
            turboItem.Click += async (s, e) => await _monitor.SetFanProfileAsync(FanProfile.Turbo);
            menu.Items.Add(turboItem);

            menu.Items.Add(new Separator());

            var hz60 = new System.Windows.Controls.MenuItem { Header = "Экран: 60 Гц" };
            hz60.Click += async (s, e) => await _monitor.SetRefreshRateAsync(60);
            menu.Items.Add(hz60);

            var hz165 = new System.Windows.Controls.MenuItem { Header = "Экран: 165 Гц" };
            hz165.Click += async (s, e) => await _monitor.SetRefreshRateAsync(165);
            menu.Items.Add(hz165);

            menu.Items.Add(new Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => QuitApplication();
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        });
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isPinned)
        {
            Hide();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        PinIcon.Foreground = _isPinned
            ? new SolidColorBrush(Color.FromRgb(255, 112, 20))
            : new SolidColorBrush(Color.FromArgb(112, 255, 255, 255));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void QuitButton_Click(object sender, RoutedEventArgs e) => QuitApplication();

    private void QuitApplication()
    {
        _trayIcon.Dispose();
        _monitor.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    // Fan Profiles
    private async void BtnProfileEco_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileEco);
        await _monitor.SetFanProfileAsync(FanProfile.Eco);
    }

    private async void BtnProfileNormal_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileNormal);
        await _monitor.SetFanProfileAsync(FanProfile.Normal);
    }

    private async void BtnProfileGame_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileGame);
        await _monitor.SetFanProfileAsync(FanProfile.Game);
    }

    private async void BtnProfileTurbo_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileTurbo);
        await _monitor.SetFanProfileAsync(FanProfile.Turbo);
    }

    private void SetActiveProfileButton(System.Windows.Controls.Button activeBtn)
    {
        BtnProfileEco.Tag = null;
        BtnProfileNormal.Tag = null;
        BtnProfileGame.Tag = null;
        BtnProfileTurbo.Tag = null;
        activeBtn.Tag = "Active";
    }

    // Battery Limit
    private async void BtnBattery60_Click(object sender, RoutedEventArgs e) => await _monitor.SetBatteryLimitAsync(60);
    private async void BtnBattery80_Click(object sender, RoutedEventArgs e) => await _monitor.SetBatteryLimitAsync(80);
    private async void BtnBattery100_Click(object sender, RoutedEventArgs e) => await _monitor.SetBatteryLimitAsync(100);

    // Refresh Rate
    private async void BtnRefresh60_Click(object sender, RoutedEventArgs e) => await _monitor.SetRefreshRateAsync(60);
    private async void BtnRefresh165_Click(object sender, RoutedEventArgs e) => await _monitor.SetRefreshRateAsync(165);

    private void AutoRefreshCheck_Changed(object sender, RoutedEventArgs e)
    {
        _monitor.AutoRefreshRateEnabled = AutoRefreshCheck.IsChecked ?? false;
    }

    // Keyboard Backlight
    private async void BacklightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        byte val = (byte)e.NewValue;
        BacklightPercentText.Text = $"{val}%";
        await _monitor.SetKeyboardBrightnessAsync(val);
    }

    private async void ColorWhite_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.White);
    private async void ColorOrange_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.Orange);
    private async void ColorBlue_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.Blue);
    private async void ColorRed_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.Red);
    private async void ColorGreen_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.Green);
    private async void ColorPurple_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardColorAsync(KeyboardColor.Purple);
    private async void EffectNeon_Click(object sender, RoutedEventArgs e) => await _monitor.SetKeyboardEffectAsync(KeyboardEffect.Neon);

    // Hotkey Locks
    private async void WinLockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        bool locked = WinLockToggle.IsChecked ?? false;
        await _monitor.SetWinLockAsync(locked);
    }

    private async void FnLockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;
        bool locked = FnLockToggle.IsChecked ?? false;
        await _monitor.SetFnLockAsync(locked);
    }
}
