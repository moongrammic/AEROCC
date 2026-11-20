using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AEROCC.App.Services;
using AEROCC.Core.Display;
using AEROCC.Core.Gpu;
using AEROCC.Core.Helpers;
using AEROCC.Core.Hotkeys;
using AEROCC.Core.Models;
using System.Windows.Interop;
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
    private bool _isExplicitExit = false;

    public MainWindow()
    {
        InitializeComponent();

        _monitor.StatusUpdated += OnStatusUpdated;
        _monitor.FanProfileChanged += OnFanProfileChanged;
        _monitor.EffectiveEcTierChanged += OnEffectiveEcTierChanged;

        // Initialize tray icon
        _trayIcon.Initialize();
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.RightClick += OnTrayRightClick;

        Deactivated += OnWindowDeactivated;
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        // Initialize Global Hotkeys and Hardware Events immediately (even when starting minimized)
        _hotkeyManager.GimateTriggered += OnGimateTriggered;
        _hotkeyManager.PerformanceCycleTriggered += OnPerformanceCycleTriggered;
        _hotkeyManager.MicMuteTriggered += OnMicMuteTriggered;
        _hotkeyManager.TouchpadTriggered += OnTouchpadTriggered;
        _hotkeyManager.AirplaneModeTriggered += OnAirplaneModeTriggered;
        _hotkeyManager.BacklightCycleTriggered += OnBacklightCycleTriggered;
        _hotkeyManager.FnLockTriggered += OnFnLockTriggered;
        _hotkeyManager.WinLockTriggered += OnWinLockTriggered;
        _hotkeyManager.Start();

        _monitor.Start();

        SidecarWindowManager.Instance.RegisterOwner(this);
        SidecarWindowManager.Instance.ModuleChanged += OnSidecarModuleChanged;
    }

    private void OnSidecarModuleChanged(SidecarModule module)
    {
        Dispatcher.Invoke(() =>
        {
            if (BtnOpenBacklight != null) BtnOpenBacklight.Tag = module == SidecarModule.BacklightStudio ? "Active" : null;
            if (BtnOpenFanCurve != null) BtnOpenFanCurve.Tag = module == SidecarModule.FanCurve ? "Active" : null;
            if (BtnOpenThermal != null) BtnOpenThermal.Tag = module == SidecarModule.ThermalMonitor ? "Active" : null;
        });
    }

    private void OpenBacklightStudio_Click(object sender, RoutedEventArgs e)
    {
        SidecarWindowManager.Instance.ToggleModule(SidecarModule.BacklightStudio, () => new Views.BacklightStudioWindow());
    }

    private void OpenFanCurve_Click(object sender, RoutedEventArgs e)
    {
        SidecarWindowManager.Instance.ToggleModule(SidecarModule.FanCurve, () => new Views.FanCurveWindow());
    }

    private void OpenThermalMonitor_Click(object sender, RoutedEventArgs e)
    {
        SidecarWindowManager.Instance.ToggleModule(SidecarModule.ThermalMonitor, () => new Views.ThermalMonitorWindow());
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearTray();
        _isLoaded = true;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExplicitExit || (System.Windows.Application.Current is App app && app.IsSessionEnding))
        {
            // Allow application to gracefully shut down and exit
            return;
        }

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
                string cpuTemperature = status.CpuTemperature is int temp ? $"{temp} °C" : "Нет данных";
                string fan1Speed = status.Fan1Rpm is int fan1Rpm ? $"{fan1Rpm} RPM" : "Нет данных";
                string fan2Speed = status.Fan2Rpm is int fan2Rpm ? $"{fan2Rpm} RPM" : "Нет данных";
                if (CpuTempText != null) CpuTempText.Text = cpuTemperature;
                if (Fan1RpmText != null) Fan1RpmText.Text = fan1Speed;
                if (Fan2RpmText != null) Fan2RpmText.Text = fan2Speed;

                // GPU Telemetry
                if (status.Gpu != null)
                {
                    if (status.Gpu.PowerState == GpuPowerState.Active)
                    {
                        if (GpuTempText != null)
                        {
                            GpuTempText.Text = status.Gpu.Temperature.HasValue ? $"{status.Gpu.Temperature.Value} °C" : "Активна";
                            GpuTempText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        }
                        if (GpuDetailText != null)
                        {
                            string usageStr = status.Gpu.CoreUsage.HasValue ? $"{status.Gpu.CoreUsage.Value}%" : "--";
                            string wattsStr = status.Gpu.PowerWatts.HasValue ? $"{status.Gpu.PowerWatts.Value:F1}W" : "--";
                            string memStr = status.Gpu.MemoryUsedMb.HasValue ? $"{status.Gpu.MemoryUsedMb.Value}MB" : "--";
                            GpuDetailText.Text = $"{usageStr} • {wattsStr} • {memStr}";
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
                if (PowerInfoText != null)
                {
                    string acLabel = power.LineStatus == AEROCC.Core.Display.PowerLineStatus.Online ? "AC" : "Батарея";
                    PowerInfoText.Text = $"⚡ {acLabel} {power.BatteryPercent}% • Лимит {status.BatteryChargeLimit}%";
                }

                if (SystemPowerDetailText != null)
                {
                    bool gpuActive = status.Gpu?.PowerState == GpuPowerState.Active;
                    double gpuW = gpuActive ? (status.Gpu?.PowerWatts ?? 0) : 0;
                    double cpuW = status.CpuPowerWatts ?? 0;
                    string cpuStr = status.CpuPowerWatts.HasValue ? $"{cpuW:F1}W" : "--";
                    string gpuStr = gpuActive && status.Gpu?.PowerWatts != null ? $"{gpuW:F1}W" : "0W";

                    if (status.BatteryDischargeWatts is > 0)
                    {
                        SystemPowerDetailText.Text = $"🔋 Ноутбук: {status.BatteryDischargeWatts.Value:F1} Вт (CPU {cpuStr} + GPU {gpuStr})";
                    }
                    else if (status.CpuPowerWatts.HasValue || gpuActive)
                    {
                        double sumW = Math.Round(cpuW + gpuW, 1);
                        string chg = status.BatteryChargeWatts is > 0 ? $" • Заряд +{status.BatteryChargeWatts.Value:F1} Вт" : "";
                        SystemPowerDetailText.Text = $"📊 CPU+GPU: {sumW:F1} Вт ({cpuStr} + {gpuStr}){chg}";
                    }
                    else
                    {
                        SystemPowerDetailText.Text = "📊 Мощность: --";
                    }
                }

                // Update Battery limit buttons
                if (BtnBattery60 != null) BtnBattery60.Tag = status.BatteryChargeLimit == 60 ? "Active" : null;
                if (BtnBattery80 != null) BtnBattery80.Tag = status.BatteryChargeLimit == 80 ? "Active" : null;
                if (BtnBattery100 != null) BtnBattery100.Tag = status.BatteryChargeLimit == 100 ? "Active" : null;

                // Update Refresh rate buttons
                if (BtnRefresh60 != null) BtnRefresh60.Tag = status.DisplayRefreshRate == 60 ? "Active" : null;
                if (BtnRefresh165 != null) BtnRefresh165.Tag = status.DisplayRefreshRate == 165 ? "Active" : null;

                // Update Toggles without re-triggering events
                if (EnergySaverToggle != null)
                {
                    EnergySaverToggle.IsChecked = status.IsEnergySaverEnabled;
                }
                if (EnergySaverBadgeText != null)
                {
                    EnergySaverBadgeText.Text = status.IsEnergySaverEnabled ? "Вкл" : "Выкл";
                    EnergySaverBadgeText.Foreground = status.IsEnergySaverEnabled
                        ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                        : new SolidColorBrush(Color.FromArgb(144, 255, 255, 255));
                }
                if (status.WinKeyLocked.HasValue && WinLockToggle != null)
                {
                    WinLockToggle.IsChecked = status.WinKeyLocked.Value;
                }
                if (status.FnKeyLocked.HasValue && FnLockToggle != null)
                {
                    FnLockToggle.IsChecked = status.FnKeyLocked.Value;
                    UpdateFnLockUI(status.FnKeyLocked.Value);
                }

                // Update Backlight without interrupting user's active mouse drag
                if (status.BacklightBrightness >= 0 && BacklightSlider != null)
                {
                    if (!BacklightSlider.IsMouseCaptureWithin && !BacklightSlider.IsFocused)
                    {
                        BacklightSlider.Value = status.BacklightBrightness;
                        if (BacklightPercentText != null) BacklightPercentText.Text = $"{status.BacklightBrightness}%";
                    }
                }

                UpdateProfileButtonUI(status.ActiveFanProfile);

                // Update IPC status
                if (IpcIndicator != null && IpcStatusText != null)
                {
                    if (_monitor.IsIpcConnected)
                    {
                        IpcIndicator.Fill = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        IpcStatusText.Text = "Служба активна (IPC)";
                    }
                    else if (AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
                    {
                        IpcIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                        IpcStatusText.Text = "Прямой доступ к ядру";
                    }
                    else
                    {
                        IpcIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 75, 75));
                        IpcStatusText.Text = "Требуются права Администратора для WMI";
                    }
                }

                _trayIcon.UpdateTooltip($"AEROCC (Aero 16 1VH)\nCPU: {cpuTemperature} | Fan1: {fan1Speed} | {status.DisplayRefreshRate}Hz");
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

            var gameItem = new System.Windows.Controls.MenuItem { Header = "Режим: Game" };
            gameItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Game));
            menu.Items.Add(gameItem);

            var turboItem = new System.Windows.Controls.MenuItem { Header = "Режим: Turbo" };
            turboItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Turbo));
            menu.Items.Add(turboItem);

            menu.Items.Add(new Separator());

            var energySaverItem = new System.Windows.Controls.MenuItem
            {
                Header = "Энергосбережение Windows",
                IsCheckable = true
            };
            energySaverItem.Click += async (s, e) => await SafeExecute(() => _monitor.SetEnergySaverAsync(energySaverItem.IsChecked));
            menu.Opened += (s, e) => energySaverItem.IsChecked = PowerManager.GetEnergySaverEnabled();
            menu.Items.Add(energySaverItem);

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

            var autostartItem = new System.Windows.Controls.MenuItem
            {
                Header = "Автозапуск при старте Windows",
                IsCheckable = true
            };
            autostartItem.Click += (s, e) =>
            {
                bool ok = SetAutostartEnabled(autostartItem.IsChecked);
                if (!ok)
                {
                    autostartItem.IsChecked = IsAutostartEnabled();
                }
            };
            menu.Opened += (s, e) => autostartItem.IsChecked = IsAutostartEnabled();
            menu.Items.Add(autostartItem);

            menu.Items.Add(new Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => QuitApplication();
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        });
    }

    private const string AutostartTaskName = "AEROCC";

    private static int RunSchtasks(string arguments, out string output, int timeoutMs = 3000)
    {
        output = string.Empty;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return -1;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(timeoutMs);
            output = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout} {stderr}".Trim();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }

    private static int RunSchtasks(string arguments, int timeoutMs = 3000) =>
        RunSchtasks(arguments, out _, timeoutMs);

    private static bool IsAutostartEnabled() =>
        RunSchtasks($"/query /tn \"{AutostartTaskName}\"") == 0;

    private static bool SetAutostartEnabled(bool enabled)
    {
        try
        {
            // Clean up legacy HKCU\...\Run registry entry if present
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue("AEROCC", false);
            }
            catch { }

            if (!enabled)
            {
                int delExit = RunSchtasks($"/delete /tn \"{AutostartTaskName}\" /f", out string delOutput);
                if (delExit != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetAutostartEnabled] schtasks /delete failed (code {delExit}): {delOutput}");
                    return false;
                }
                return true;
            }

            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath)) return false;

            // Configure task XML explicitly setting DisallowStartIfOnBatteries=false, StopIfGoingOnBatteries=false,
            // and ExecutionTimeLimit=PT0S (unlimited runtime) so laptop power source changes and long uptime do not terminate AEROCC.
            string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.3"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>AEROCC Control Center for Gigabyte Aero</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{System.Security.SecurityElement.Escape(exePath)}</Command>
      <Arguments>--minimized</Arguments>
    </Exec>
  </Actions>
</Task>";

            string tempXmlPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aerocc_task_{Guid.NewGuid():N}.xml");
            try
            {
                System.IO.File.WriteAllText(tempXmlPath, taskXml, System.Text.Encoding.Unicode);
                int exitCode = RunSchtasks($"/create /tn \"{AutostartTaskName}\" /xml \"{tempXmlPath}\" /f", out string createOutput);
                if (exitCode != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SetAutostartEnabled] Task registration via XML failed (code {exitCode}): {createOutput}");
                    return false;
                }
                return true;
            }
            finally
            {
                try { System.IO.File.Delete(tempXmlPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetAutostartEnabled Error]: {ex.Message}");
            return false;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_isPinned && !IsMouseOver && !SidecarWindowManager.Instance.IsAnyOpen)
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
        _isExplicitExit = true;
        _hotkeyManager.Dispose();
        _trayIcon.Dispose();
        _monitor.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnGimateTriggered()
    {
        Dispatcher.Invoke(() =>
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.EnsureHandle();
            if (IsVisible && WindowActivationHelper.IsForeground(hwnd))
            {
                Hide();
            }
            else
            {
                PositionNearTray();
                Show();
                WindowActivationHelper.ForceForeground(hwnd);
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

    private void OnFanProfileChanged(FanProfile profile)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProfileButtonUI(profile);
        });
    }

    private void OnEffectiveEcTierChanged(FanProfile? tier)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProfileButtonUI(_monitor.CurrentFanProfile);
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
            FanProfile.Manual => BtnProfileManual,
            _ => null
        };
        bool knownTier = _monitor.ActiveEffectiveEcTier.HasValue;
        SetActiveProfileButton(knownTier ? btn : null);

        if (FanProfileErrorText != null)
            FanProfileErrorText.Visibility = _monitor.FanProfileWriteFailed ? Visibility.Visible : Visibility.Collapsed;

        if (ManualProfileBadge != null)
        {
            ManualProfileBadge.Visibility = (knownTier && profile == FanProfile.Manual) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ManualProfileBadgeText != null && profile == FanProfile.Manual)
        {
            ManualProfileBadgeText.Text = _monitor.GetManualStatusLabel();
        }
    }

    // Fan Profiles
    private async void BtnProfileEco_Click(object sender, RoutedEventArgs e)
    {
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Eco));
    }

    private async void BtnProfileNormal_Click(object sender, RoutedEventArgs e)
    {
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Normal));
    }

    private async void BtnProfileGame_Click(object sender, RoutedEventArgs e)
    {
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Game));
    }

    private async void BtnProfileTurbo_Click(object sender, RoutedEventArgs e)
    {
        await SafeExecute(() => _monitor.SetFanProfileAsync(FanProfile.Turbo));
    }

    private void BtnProfileManual_Click(object sender, RoutedEventArgs e)
    {
        SidecarWindowManager.Instance.ToggleModule(SidecarModule.FanCurve, () => new Views.FanCurveWindow());
    }

    private void SetActiveProfileButton(System.Windows.Controls.Button? activeBtn)
    {
        if (BtnProfileEco != null) BtnProfileEco.Tag = null;
        if (BtnProfileNormal != null) BtnProfileNormal.Tag = null;
        if (BtnProfileGame != null) BtnProfileGame.Tag = null;
        if (BtnProfileTurbo != null) BtnProfileTurbo.Tag = null;
        if (BtnProfileManual != null) BtnProfileManual.Tag = null;
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
    private async void RainbowWave_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetRainbowBacklightAsync(KeyboardEffect.Wave));
    private async void RainbowNeon_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetRainbowBacklightAsync(KeyboardEffect.Neon));
    private async void EffectNeon_Click(object sender, RoutedEventArgs e) => await SafeExecute(() => _monitor.SetRainbowBacklightAsync(KeyboardEffect.Neon));

    // Hotkey Locks
    private async void EnergySaverToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded || _isUpdatingUi || EnergySaverToggle == null) return;
        bool enabled = EnergySaverToggle.IsChecked ?? false;
        await SafeExecute(() => _monitor.SetEnergySaverAsync(enabled));
    }

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
