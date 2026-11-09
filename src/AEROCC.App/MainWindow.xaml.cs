using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AEROCC.App.Services;
using AEROCC.Core.Display;
using AEROCC.Core.Gpu;
using AEROCC.Core.Hotkeys;
using AEROCC.Core.Models;
using Wpf.Ui.Controls;

namespace AEROCC.App;

public partial class MainWindow : Window
{
    private readonly HardwareMonitor _monitor = HardwareMonitor.Instance;
    private readonly HotkeyManager _hotkeyManager = HotkeyManager.Instance;
    private readonly TrayIconService _trayIcon = new();
    private bool _isPinned = false;
    private bool _isUpdatingUi = false;
    private bool _isLoaded = false;

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
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearTray();
        _isLoaded = true;
        _monitor.Start();

        // Initialize Global Hotkeys and Hardware Events
        _hotkeyManager.GimateTriggered += OnGimateTriggered;
        _hotkeyManager.PerformanceCycleTriggered += OnPerformanceCycleTriggered;
        _hotkeyManager.MicMuteTriggered += OnMicMuteTriggered;
        _hotkeyManager.TouchpadTriggered += OnTouchpadTriggered;
        _hotkeyManager.AirplaneModeTriggered += OnAirplaneModeTriggered;
        _hotkeyManager.BacklightCycleTriggered += OnBacklightCycleTriggered;
        _hotkeyManager.FnLockTriggered += OnFnLockTriggered;
        _hotkeyManager.WinLockTriggered += OnWinLockTriggered;
        _hotkeyManager.Start();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of quitting
        e.Cancel = true;
        Hide();
    }

    private void OnStatusUpdated(HardwareStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isLoaded) return;
            _isUpdatingUi = true;
            try
            {
                if (CpuTempText != null) CpuTempText.Text = $"{status.CpuTemperature} °C";
                if (Fan1RpmText != null) Fan1RpmText.Text = $"{status.Fan1Rpm} RPM";
                if (Fan2RpmText != null) Fan2RpmText.Text = $"{status.Fan2Rpm} RPM";

                // GPU Telemetry
                if (status.Gpu != null)
                {
                    if (status.Gpu.PowerState == GpuPowerState.Active)
                    {
                        if (GpuTempText != null)
                        {
                            GpuTempText.Text = $"{status.Gpu.Temperature ?? 0} °C";
                            GpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        }
                        if (GpuDetailText != null)
                        {
                            GpuDetailText.Text = $"{status.Gpu.CoreUsage ?? 0}% • {status.Gpu.PowerWatts?.ToString("F1") ?? "0"}W • {status.Gpu.MemoryUsedMb}MB";
                        }
                        if (GpuBadgeText != null)
                        {
                            GpuBadgeText.Text = "Активна";
                            GpuBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        }
                    }
                    else if (status.Gpu.PowerState == GpuPowerState.Sleeping)
                    {
                        if (GpuTempText != null)
                        {
                            GpuTempText.Text = "Спит";
                            GpuTempText.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                        }
                        if (GpuDetailText != null)
                        {
                            GpuDetailText.Text = "Optimus Спящий (D3)";
                        }
                        if (GpuBadgeText != null)
                        {
                            GpuBadgeText.Text = "D3cold Спит";
                            GpuBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255));
                        }
                    }
                    else if (status.Gpu.PowerState == GpuPowerState.EcoDisabled)
                    {
                        if (GpuTempText != null)
                        {
                            GpuTempText.Text = "Откл";
                            GpuTempText.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                        }
                        if (GpuDetailText != null)
                        {
                            GpuDetailText.Text = "Только iGPU (Eco)";
                        }
                        if (GpuBadgeText != null)
                        {
                            GpuBadgeText.Text = "Eco Отключена";
                            GpuBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                        }
                    }

                    // Update GPU Mode buttons
                    if (BtnGpuDynamic != null) BtnGpuDynamic.Tag = status.Gpu.PowerMode == GpuPowerMode.Dynamic ? "Active" : null;
                    if (BtnGpuEco != null) BtnGpuEco.Tag = status.Gpu.PowerMode == GpuPowerMode.Eco ? "Active" : null;
                    if (BtnGpuPerformance != null) BtnGpuPerformance.Tag = status.Gpu.PowerMode == GpuPowerMode.Performance ? "Active" : null;
                }

                var power = PowerManager.GetPowerInfo();
                if (PowerInfoText != null) PowerInfoText.Text = $"⚡ {power} • Лимит: {status.BatteryChargeLimit}%";

                // Update Battery limit buttons
                if (BtnBattery60 != null) BtnBattery60.Tag = status.BatteryChargeLimit == 60 ? "Active" : null;
                if (BtnBattery80 != null) BtnBattery80.Tag = status.BatteryChargeLimit == 80 ? "Active" : null;
                if (BtnBattery100 != null) BtnBattery100.Tag = status.BatteryChargeLimit == 100 ? "Active" : null;

                // Update Refresh rate buttons
                if (BtnRefresh60 != null) BtnRefresh60.Tag = status.DisplayRefreshRate == 60 ? "Active" : null;
                if (BtnRefresh165 != null) BtnRefresh165.Tag = status.DisplayRefreshRate == 165 ? "Active" : null;

                // Update Toggles without re-triggering events
                if (status.WinKeyLocked.HasValue && WinLockToggle != null)
                {
                    WinLockToggle.IsChecked = status.WinKeyLocked.Value;
                }
                if (status.FnKeyLocked.HasValue && FnLockToggle != null)
                {
                    FnLockToggle.IsChecked = status.FnKeyLocked.Value;
                    UpdateFnLockUI(status.FnKeyLocked.Value);
                }

                // Update Backlight
                if (status.BacklightBrightness >= 0 && BacklightSlider != null)
                {
                    BacklightSlider.Value = status.BacklightBrightness;
                    if (BacklightPercentText != null) BacklightPercentText.Text = $"{status.BacklightBrightness}%";
                }

                // Update IPC status
                if (IpcIndicator != null && IpcStatusText != null)
                {
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
                }

                _trayIcon.UpdateTooltip($"AEROCC (Aero 16 1VH)\nCPU: {status.CpuTemperature}°C | Fan1: {status.Fan1Rpm} | {status.DisplayRefreshRate}Hz");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI Update Error]: {ex.Message}");
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
            ecoItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Eco));
            menu.Items.Add(ecoItem);

            var normItem = new System.Windows.Controls.MenuItem { Header = "Режим: Normal" };
            normItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Normal));
            menu.Items.Add(normItem);

            var turboItem = new System.Windows.Controls.MenuItem { Header = "Режим: Turbo" };
            turboItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Turbo));
            menu.Items.Add(turboItem);

            menu.Items.Add(new Separator());

            var hz60 = new System.Windows.Controls.MenuItem { Header = "Экран: 60 Гц" };
            hz60.Click += async (s, e) => await SafeExecute(() => _monitor.SetRefreshRateAsync(60));
            menu.Items.Add(hz60);

            var hz165 = new System.Windows.Controls.MenuItem { Header = "Экран: 165 Гц" };
            hz165.Click += async (s, e) => await SafeExecute(() => _monitor.SetRefreshRateAsync(165));
            menu.Items.Add(hz165);

            menu.Items.Add(new Separator());

            var gpuDynamic = new System.Windows.Controls.MenuItem { Header = "GPU: Dynamic (Optimus)" };
            gpuDynamic.Click += async (s, e) => await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Dynamic));
            menu.Items.Add(gpuDynamic);

            var gpuEco = new System.Windows.Controls.MenuItem { Header = "GPU: Eco (Только iGPU)" };
            gpuEco.Click += async (s, e) => await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Eco));
            menu.Items.Add(gpuEco);

            var gpuPerf = new System.Windows.Controls.MenuItem { Header = "GPU: dGPU Всегда активна" };
            gpuPerf.Click += async (s, e) => await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Performance));
            menu.Items.Add(gpuPerf);

            menu.Items.Add(new Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => QuitApplication();
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        });
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isPinned && !IsMouseOver)
        {
            Hide();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (PinIcon != null)
        {
            PinIcon.Foreground = _isPinned
                ? new SolidColorBrush(Color.FromRgb(255, 112, 20))
                : new SolidColorBrush(Color.FromArgb(112, 255, 255, 255));
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void QuitButton_Click(object sender, RoutedEventArgs e) => QuitApplication();

    private void QuitApplication()
    {
        _hotkeyManager.Dispose();
        _trayIcon.Dispose();
        _monitor.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnGimateTriggered()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible && IsActive)
            {
                Hide();
            }
            else
            {
                PositionNearTray();
                Show();
                Activate();
                Focus();
            }
        });
    }

    private void OnPerformanceCycleTriggered()
    {
        Dispatcher.Invoke(async () =>
        {
            var next = await _monitor.CycleFanProfileAsync();
            UpdateProfileButtonUI(next);
        });
    }

    private void OnMicMuteTriggered(bool isMuted)
    {
        Dispatcher.Invoke(() =>
        {
            OsdService.Instance.ShowMicrophone(isMuted);
        });
    }

    private void OnTouchpadTriggered(bool isEnabled)
    {
        Dispatcher.Invoke(() =>
        {
            OsdService.Instance.ShowTouchpad(isEnabled);
        });
    }

    private void OnAirplaneModeTriggered(bool isActive)
    {
        Dispatcher.Invoke(() =>
        {
            OsdService.Instance.ShowAirplaneMode(isActive);
        });
    }

    private void OnBacklightCycleTriggered()
    {
        Dispatcher.Invoke(async () =>
        {
            byte next = await _monitor.CycleBacklightBrightnessAsync();
            if (BacklightSlider != null) BacklightSlider.Value = next;
            if (BacklightPercentText != null) BacklightPercentText.Text = $"{next}%";
        });
    }

    private void OnFnLockTriggered(bool locked)
    {
        Dispatcher.Invoke(() =>
        {
            if (FnLockToggle != null) FnLockToggle.IsChecked = locked;
            UpdateFnLockUI(locked);
            OsdService.Instance.ShowFnLock(locked);
        });
    }

    private void OnWinLockTriggered(bool locked)
    {
        Dispatcher.Invoke(() =>
        {
            if (WinLockToggle != null) WinLockToggle.IsChecked = locked;
            OsdService.Instance.ShowWinLock(locked);
        });
    }

    private void UpdateProfileButtonUI(FanProfile profile)
    {
        var btn = profile switch
        {
            FanProfile.Eco => BtnProfileEco,
            FanProfile.Normal => BtnProfileNormal,
            FanProfile.Game => BtnProfileGame,
            FanProfile.Turbo => BtnProfileTurbo,
            _ => null
        };
        SetActiveProfileButton(btn);
    }

    // Fan Profiles
    private async void BtnProfileEco_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileEco);
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Eco));
    }

    private async void BtnProfileNormal_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileNormal);
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Normal));
    }

    private async void BtnProfileGame_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileGame);
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Game));
    }

    private async void BtnProfileTurbo_Click(object sender, RoutedEventArgs e)
    {
        SetActiveProfileButton(BtnProfileTurbo);
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Turbo));
    }

    private void SetActiveProfileButton(System.Windows.Controls.Button? activeBtn)
    {
        if (BtnProfileEco != null) BtnProfileEco.Tag = null;
        if (BtnProfileNormal != null) BtnProfileNormal.Tag = null;
        if (BtnProfileGame != null) BtnProfileGame.Tag = null;
        if (BtnProfileTurbo != null) BtnProfileTurbo.Tag = null;
        if (activeBtn != null) activeBtn.Tag = "Active";
    }

    // Battery Limit
    private async void BtnBattery60_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetBatteryLimitAsync(60));
    private async void BtnBattery80_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetBatteryLimitAsync(80));
    private async void BtnBattery100_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetBatteryLimitAsync(100));

    // GPU PowerGear
    private async void BtnGpuDynamic_Click(object sender, RoutedEventArgs e)
    {
        SetGpuButtonActive(BtnGpuDynamic);
        await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Dynamic));
    }

    private async void BtnGpuEco_Click(object sender, RoutedEventArgs e)
    {
        SetGpuButtonActive(BtnGpuEco);
        await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Eco));
    }

    private async void BtnGpuPerformance_Click(object sender, RoutedEventArgs e)
    {
        SetGpuButtonActive(BtnGpuPerformance);
        await SafeExecute(() => _monitor.SetGpuPowerModeAsync(GpuPowerMode.Performance));
    }

    private void SetGpuButtonActive(System.Windows.Controls.Button? activeBtn)
    {
        if (BtnGpuDynamic != null) BtnGpuDynamic.Tag = null;
        if (BtnGpuEco != null) BtnGpuEco.Tag = null;
        if (BtnGpuPerformance != null) BtnGpuPerformance.Tag = null;
        if (activeBtn != null) activeBtn.Tag = "Active";
    }

    // Refresh Rate
    private async void BtnRefresh60_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetRefreshRateAsync(60));
    private async void BtnRefresh165_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetRefreshRateAsync(165));

    private void AutoRefreshCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || AutoRefreshCheck == null) return;
        _monitor.AutoRefreshRateEnabled = AutoRefreshCheck.IsChecked ?? false;
    }

    private DispatcherTimer? _backlightDebounceTimer;
    private byte _pendingBrightness;

    // Keyboard Backlight
    private void BacklightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded || _isUpdatingUi) return;
        byte val = (byte)e.NewValue;
        if (BacklightPercentText != null) BacklightPercentText.Text = $"{val}%";

        _pendingBrightness = val;
        if (_backlightDebounceTimer == null)
        {
            _backlightDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            _backlightDebounceTimer.Tick += async (s, args) =>
            {
                _backlightDebounceTimer.Stop();
                await SafeExecute(() => _monitor.SetKeyboardBrightnessAsync(_pendingBrightness));
            };
        }
        _backlightDebounceTimer.Stop();
        _backlightDebounceTimer.Start();
    }

    private async void ColorWhite_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.White));
    private async void ColorOrange_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.Orange));
    private async void ColorBlue_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.Blue));
    private async void ColorRed_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.Red));
    private async void ColorGreen_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.Green));
    private async void ColorPurple_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardColorAsync(KeyboardColor.Purple));
    private async void EffectNeon_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetKeyboardEffectAsync(KeyboardEffect.Neon));

    // Hotkey Locks
    private async void WinLockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _isUpdatingUi || WinLockToggle == null) return;
        bool locked = WinLockToggle.IsChecked ?? false;
        await SafeExecute(() => _monitor.SetWinLockAsync(locked));
    }

    private async void FnLockToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _isUpdatingUi || FnLockToggle == null) return;
        bool locked = FnLockToggle.IsChecked ?? false;
        UpdateFnLockUI(locked);
        await SafeExecute(() => _monitor.SetFnLockAsync(locked));
    }

    private void UpdateFnLockUI(bool locked)
    {
        if (FnLockLabel != null)
        {
            FnLockLabel.Text = locked
                ? "Режим F1–F12: Мультимедиа (Fn Lock)"
                : "Режим F1–F12: Стандартные";
        }
        if (FnLockIcon != null)
        {
            FnLockIcon.Symbol = locked ? SymbolRegular.Speaker224 : SymbolRegular.Keyboard24;
            FnLockIcon.Foreground = locked
                ? new SolidColorBrush(Color.FromRgb(0, 210, 255))
                : new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
        }
    }

    private static async Task SafeExecute(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Action Error]: {ex.Message}");
        }
    }
}
