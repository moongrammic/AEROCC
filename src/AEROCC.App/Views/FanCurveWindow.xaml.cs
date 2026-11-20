using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AEROCC.App.Services;
using AEROCC.Core.Models;

namespace AEROCC.App.Views;

public partial class FanCurveWindow : Window
{
    private readonly HardwareMonitor _monitor = HardwareMonitor.Instance;
    private readonly List<(byte Temp, byte Duty)> _points = new();
    private readonly List<Border> _thumbNodes = new();
    private int _draggedPointIndex = -1;
    private bool _isLoaded = false;
    private string? _pendingPresetKey;

    public FanCurveWindow()
    {
        InitializeComponent();

        // Restore currently active curve points if set, or load default balanced curve matching EC hardware bins
        if (_monitor.ActiveCurvePoints.Count == 8)
        {
            _points.AddRange(_monitor.ActiveCurvePoints);
        }
        else
        {
            _points.AddRange(new (byte Temp, byte Duty)[]
            {
                (30, 20),
                (40, 28),
                (50, 35),
                (60, 45),
                (70, 60),
                (80, 70),
                (90, 80),
                (95, 90)
            });
        }

        _pendingPresetKey = _monitor.ActiveCurvePresetName;
        if (_monitor.ActiveFixedSpeedPercent.HasValue && FixedSpeedSlider != null)
        {
            FixedSpeedSlider.Value = _monitor.ActiveFixedSpeedPercent.Value;
        }

        Loaded += OnLoaded;
        _monitor.StatusUpdated += OnStatusUpdated;
        _monitor.FanProfileChanged += OnFanProfileChanged;
        _monitor.EffectiveEcTierChanged += OnEffectiveEcTierChanged;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        RebuildThumbs();
        RedrawCurve();
        UpdateChips();
        UpdateLiveStatus(_monitor.LatestStatus);
        UpdateProfileStatusUI(_monitor.CurrentFanProfile);
        RestoreActivePresetHighlight();
        UpdateFixedButtonText((byte)Math.Round(FixedSpeedSlider.Value));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _monitor.StatusUpdated -= OnStatusUpdated;
        _monitor.FanProfileChanged -= OnFanProfileChanged;
        _monitor.EffectiveEcTierChanged -= OnEffectiveEcTierChanged;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnStatusUpdated(HardwareStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isLoaded) return;
            UpdateLiveStatus(status);
        });
    }

    private void UpdateLiveStatus(HardwareStatus status)
    {
        if (CurrentTempText != null)
        {
            CurrentTempText.Text = status.CpuTemperature is int temp ? $"CPU: {temp} °C" : "CPU: Нет данных";
        }
        if (CurrentRpmText != null)
        {
            CurrentRpmText.Text = $"Fan1: {status.Fan1Rpm?.ToString() ?? "—"} • Fan2: {status.Fan2Rpm?.ToString() ?? "—"} RPM";
        }

        // Position live marker on the chart canvas
        if (LiveTempMarker == null) return;
        LiveTempMarker.Visibility = Visibility.Collapsed;
        if (ChartCanvas != null && ChartCanvas.ActualWidth >= 10 && status.CpuTemperature is int cpuTemp)
        {
            double w = ChartCanvas.ActualWidth;
            double h = ChartCanvas.ActualHeight > 0 ? ChartCanvas.ActualHeight : 200;

            double clampedTemp = Math.Clamp(cpuTemp, 30, 100);
            double x = (clampedTemp - 30.0) / 70.0 * w;

            // Interpolate duty for this temp
            double duty = GetInterpolatedDuty(clampedTemp);
            double y = (1.0 - duty / 100.0) * h;

            Canvas.SetLeft(LiveTempMarker, Math.Clamp(x - 5, 0, w - 10));
            Canvas.SetTop(LiveTempMarker, Math.Clamp(y - 5, 0, Math.Max(0, h - 10)));
            LiveTempMarker.ToolTip = $"Замер CPU: {cpuTemp} °C\nРасчетная цель кривой: {Math.Round(duty)}%\nТекущая ступень EC: {_monitor.ActiveEffectiveEcTier?.ToString() ?? "—"}\nФактические обороты: Fan 1 = {status.Fan1Rpm?.ToString() ?? "—"} RPM, Fan 2 = {status.Fan2Rpm?.ToString() ?? "—"} RPM";
            LiveTempMarker.Visibility = Visibility.Visible;
        }
    }

    private double GetInterpolatedDuty(double temp) =>
        AEROCC.Core.Wmi.GigabyteWmiClient.InterpolateCurveDuty(_points, temp);

    private void RebuildThumbs()
    {
        // Clear previous thumbs
        foreach (var thumb in _thumbNodes)
        {
            ChartCanvas.Children.Remove(thumb);
        }
        _thumbNodes.Clear();

        for (int i = 0; i < _points.Count; i++)
        {
            int index = i;
            var thumb = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.SizeNS,
                Tag = index,
                ToolTip = $"Точка {index + 1}: {_points[index].Temp}°C → {_points[index].Duty}%"
            };

            thumb.MouseLeftButtonDown += (s, e) =>
            {
                _draggedPointIndex = index;
                thumb.CaptureMouse();
                e.Handled = true;
            };

            _thumbNodes.Add(thumb);
            ChartCanvas.Children.Add(thumb);
        }
    }

    private void RedrawCurve()
    {
        if (ChartCanvas == null || ChartCanvas.ActualWidth <= 0) return;

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight > 0 ? ChartCanvas.ActualHeight : 200;

        var points = new PointCollection();
        var polyPoints = new PointCollection();

        // Start from (30°C, duty of point 0)
        double startY = (1.0 - _points[0].Duty / 100.0) * h;
        polyPoints.Add(new Point(0, h)); // bottom-left
        polyPoints.Add(new Point(0, startY));
        points.Add(new Point(0, startY));

        for (int i = 0; i < _points.Count; i++)
        {
            double x = (_points[i].Temp - 30.0) / 70.0 * w;
            double y = (1.0 - _points[i].Duty / 100.0) * h;

            points.Add(new Point(x, y));
            polyPoints.Add(new Point(x, y));

            if (i < _thumbNodes.Count)
            {
                Canvas.SetLeft(_thumbNodes[i], x - 8);
                Canvas.SetTop(_thumbNodes[i], y - 8);
                _thumbNodes[i].ToolTip = $"Точка {i + 1}: {_points[i].Temp}°C → {_points[i].Duty}%";
            }
        }

        // End at (100°C, duty of last point)
        double endY = (1.0 - _points[^1].Duty / 100.0) * h;
        points.Add(new Point(w, endY));
        polyPoints.Add(new Point(w, endY));
        polyPoints.Add(new Point(w, h)); // bottom-right

        CurvePolyline.Points = points;
        CurveAreaPolygon.Points = polyPoints;
    }

    private void OnFanProfileChanged(FanProfile profile)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProfileStatusUI(profile);
        });
    }

    private void OnEffectiveEcTierChanged(FanProfile? tier)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProfileStatusUI(_monitor.CurrentFanProfile);
        });
    }

    private void RestoreActivePresetHighlight()
    {
        if (_monitor.CurrentFanProfile != FanProfile.Manual || !_monitor.IsDynamicCurveActive)
        {
            ClearPresetHighlight();
            if (_monitor.ActiveFixedSpeedPercent.HasValue)
            {
                HighlightFixedTierButton(AEROCC.Core.Wmi.GigabyteWmiClient.MapFixedPercentToEcTier(_monitor.ActiveFixedSpeedPercent.Value));
            }
            return;
        }

        HighlightFixedTierButton(null);
        var btn = _monitor.ActiveCurvePresetName switch
        {
            "Silent" => BtnPresetSilent,
            "Balanced" => BtnPresetBalanced,
            "Aggressive" => BtnPresetAggressive,
            "Max" => BtnPresetMax,
            _ => null
        };
        HighlightPresetButton(btn);
    }

    private void UpdateProfileStatusUI(FanProfile profile)
    {
        if (ProfileStatusBadge == null) return;

        if (_monitor.ActiveEffectiveEcTier is null)
        {
            bool failed = _monitor.FanProfileWriteFailed;
            ProfileStatusBadge.Text = failed ? "Ошибка записи • ступень EC неизвестна" : "Ступень EC неизвестна";
            ProfileStatusBadge.Foreground = failed ? Brushes.LightCoral : Brushes.LightGray;
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = Brushes.Transparent;
            ClearPresetHighlight();
        }
        else if (profile == FanProfile.Manual)
        {
            ProfileStatusBadge.Text = _monitor.GetManualStatusLabel(useArrowForFixed: true);

            ProfileStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 0, 230, 118));
        }
        else
        {
            ProfileStatusBadge.Text = $"Записан профиль EC: {profile}";
            ProfileStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 255));
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(30, 0, 150, 255));
            ClearPresetHighlight();
        }
    }

    private void HighlightPresetButton(Button? btn)
    {
        if (BtnPresetSilent != null) BtnPresetSilent.Tag = null;
        if (BtnPresetBalanced != null) BtnPresetBalanced.Tag = null;
        if (BtnPresetAggressive != null) BtnPresetAggressive.Tag = null;
        if (BtnPresetMax != null) BtnPresetMax.Tag = null;
        if (btn != null) btn.Tag = "Active";
    }

    private void ClearPresetHighlight()
    {
        if (BtnPresetSilent != null) BtnPresetSilent.Tag = null;
        if (BtnPresetBalanced != null) BtnPresetBalanced.Tag = null;
        if (BtnPresetAggressive != null) BtnPresetAggressive.Tag = null;
        if (BtnPresetMax != null) BtnPresetMax.Tag = null;
    }

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPointIndex < 0 || _draggedPointIndex >= _points.Count) return;

        double h = ChartCanvas.ActualHeight > 0 ? ChartCanvas.ActualHeight : 200;
        Point pos = e.GetPosition(ChartCanvas);

        // Clamp Duty between 0% and 100%
        double dutyFraction = 1.0 - (pos.Y / h);
        byte newDuty = (byte)Math.Clamp(Math.Round(dutyFraction * 100), 0, 100);

        byte currentTemp = _points[_draggedPointIndex].Temp;
        _points[_draggedPointIndex] = (currentTemp, newDuty);

        RedrawCurve();
        UpdateChips();
        ClearPresetHighlight();
        _pendingPresetKey = null;

        if (ProfileStatusBadge != null)
        {
            ProfileStatusBadge.Text = "Кривая изменена (нажмите Применить)";
            ProfileStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80));
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(40, 255, 200, 80));
        }
    }

    private void ChartCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedPointIndex >= 0 && _draggedPointIndex < _thumbNodes.Count)
        {
            _thumbNodes[_draggedPointIndex].ReleaseMouseCapture();
            _draggedPointIndex = -1;
        }
    }

    private void UpdateChips()
    {
        if (PointsChipsPanel == null) return;
        PointsChipsPanel.Children.Clear();

        for (int i = 0; i < _points.Count; i++)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(2, 2, 2, 2)
            };

            var tb = new TextBlock
            {
                Text = $"{_points[i].Temp}°C : {_points[i].Duty}%",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 255, 230))
            };

            border.Child = tb;
            PointsChipsPanel.Children.Add(border);
        }
    }

    private async void PresetSilent_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset(new (byte, byte)[]
        {
            (30, 15),
            (40, 18),
            (50, 25),
            (60, 35),
            (70, 50),
            (80, 68),
            (90, 85),
            (95, 95)
        });
        _pendingPresetKey = "Silent";
        await ExecuteApplyCurveAsync("Silent");
    }

    private async void PresetBalanced_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset(new (byte, byte)[]
        {
            (30, 20),
            (40, 28),
            (50, 35),
            (60, 45),
            (70, 60),
            (80, 70),
            (90, 80),
            (95, 90)
        });
        _pendingPresetKey = "Balanced";
        await ExecuteApplyCurveAsync("Balanced");
    }

    private async void PresetAggressive_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset(new (byte, byte)[]
        {
            (30, 30),
            (40, 40),
            (50, 56),
            (60, 68),
            (70, 78),
            (80, 84),
            (90, 89),
            (95, 100)
        });
        _pendingPresetKey = "Aggressive";
        await ExecuteApplyCurveAsync("Aggressive");
    }

    private async void PresetMax_Click(object sender, RoutedEventArgs e)
    {
        ApplyPreset(new (byte, byte)[]
        {
            (30, 100),
            (40, 100),
            (50, 100),
            (60, 100),
            (70, 100),
            (80, 100),
            (90, 100),
            (95, 100)
        });
        _pendingPresetKey = "Max";
        await ExecuteApplyCurveAsync("Max");
    }

    private void ApplyPreset(IList<(byte, byte)> preset)
    {
        _points.Clear();
        _points.AddRange(preset);
        RedrawCurve();
        UpdateChips();
    }

    private void FixedSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateFixedButtonText((byte)Math.Round(FixedSpeedSlider.Value));
    }

    private void UpdateFixedButtonText(byte speed)
    {
        var tier = AEROCC.Core.Wmi.GigabyteWmiClient.MapFixedPercentToEcTier(speed);
        if (FixedSpeedBtnText != null)
        {
            FixedSpeedBtnText.Text = $"Задать {speed}% ({tier})";
        }
        HighlightFixedTierButton(tier);
    }

    private void HighlightFixedTierButton(FanProfile? activeTier)
    {
        if (BtnTierEco != null) BtnTierEco.Tag = activeTier == FanProfile.Eco ? "Active" : null;
        if (BtnTierNormal != null) BtnTierNormal.Tag = activeTier == FanProfile.Normal ? "Active" : null;
        if (BtnTierGame != null) BtnTierGame.Tag = activeTier == FanProfile.Game ? "Active" : null;
        if (BtnTierTurbo != null) BtnTierTurbo.Tag = activeTier == FanProfile.Turbo ? "Active" : null;
    }

    private async void FixedTierEco_Click(object sender, RoutedEventArgs e)
    {
        FixedSpeedSlider.Value = 25;
        await ApplyFixedSpeedInternalAsync(25);
    }

    private async void FixedTierNormal_Click(object sender, RoutedEventArgs e)
    {
        FixedSpeedSlider.Value = 50;
        await ApplyFixedSpeedInternalAsync(50);
    }

    private async void FixedTierGame_Click(object sender, RoutedEventArgs e)
    {
        FixedSpeedSlider.Value = 75;
        await ApplyFixedSpeedInternalAsync(75);
    }

    private async void FixedTierTurbo_Click(object sender, RoutedEventArgs e)
    {
        FixedSpeedSlider.Value = 100;
        await ApplyFixedSpeedInternalAsync(100);
    }

    private async void ApplyFixedSpeed_Click(object sender, RoutedEventArgs e)
    {
        byte speed = (byte)Math.Round(FixedSpeedSlider.Value);
        await ApplyFixedSpeedInternalAsync(speed);
    }

    private async Task ApplyFixedSpeedInternalAsync(byte speed)
    {
        _pendingPresetKey = null;
        ClearPresetHighlight();
        if (ProfileStatusBadge != null)
        {
            ProfileStatusBadge.Text = $"Установка {speed}%...";
            ProfileStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0));
        }
        bool ok = await _monitor.SetFixedFanSpeedAsync(speed);
        if (ProfileStatusBadge != null)
        {
            ProfileStatusBadge.Text = ok
                ? _monitor.GetManualStatusLabel(useArrowForFixed: true)
                : "Ошибка записи";
            ProfileStatusBadge.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(255, 80, 80));
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = new SolidColorBrush(ok ? Color.FromArgb(40, 0, 230, 118) : Color.FromArgb(40, 255, 80, 80));
        }
    }

    private async void ApplyCurve_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteApplyCurveAsync(_pendingPresetKey);
    }

    private async Task ExecuteApplyCurveAsync(string? presetName = null)
    {
        if (ApplyCurveBtn != null) ApplyCurveBtn.IsEnabled = false;
        if (ApplyCurveBtnText != null) ApplyCurveBtnText.Text = "Активация...";
        if (ProfileStatusBadge != null)
        {
            ProfileStatusBadge.Text = "Применение кривой...";
            ProfileStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0));
        }

        bool ok = await _monitor.ApplyFanCurveAsync(_points, presetName);
        RestoreActivePresetHighlight();

        if (ApplyCurveBtn != null) ApplyCurveBtn.IsEnabled = true;
        if (ApplyCurveBtnText != null) ApplyCurveBtnText.Text = "Применить кривую";

        if (ProfileStatusBadge != null)
        {
            ProfileStatusBadge.Text = ok
                ? _monitor.GetManualStatusLabel()
                : "Ошибка применения";
            ProfileStatusBadge.Foreground = ok ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(255, 80, 80));
            if (ProfileBadgeBorder != null) ProfileBadgeBorder.Background = new SolidColorBrush(ok ? Color.FromArgb(40, 0, 230, 118) : Color.FromArgb(40, 255, 80, 80));
        }
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        PresetBalanced_Click(BtnPresetBalanced, e);
    }
}
