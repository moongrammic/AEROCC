using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AEROCC.App.Services;
using AEROCC.Core.Gpu;
using AEROCC.Core.Models;

namespace AEROCC.App.Views;

public partial class ThermalMonitorWindow : Window
{
    private readonly HardwareMonitor _monitor = HardwareMonitor.Instance;
    private const int MaxHistorySamples = 45;

    private readonly struct TelemetrySample
    {
        public readonly DateTimeOffset Timestamp;
        public readonly int? CpuTemp;
        public readonly int? GpuTemp;
        public readonly double? GpuWatts;
        public readonly double? TotalWatts;

        public TelemetrySample(DateTimeOffset timestamp, int? cpuTemp, int? gpuTemp, double? gpuWatts, double? totalWatts)
        {
            Timestamp = timestamp;
            CpuTemp = cpuTemp;
            GpuTemp = gpuTemp;
            GpuWatts = gpuWatts;
            TotalWatts = totalWatts;
        }
    }

    private readonly List<TelemetrySample> _history = new();

    private int _minCpu = int.MaxValue;
    private int _maxCpu = int.MinValue;
    private bool _isLoaded = false;

    public ThermalMonitorWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        _monitor.StatusUpdated += OnStatusUpdated;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateMetrics(_monitor.LatestStatus);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _monitor.StatusUpdated -= OnStatusUpdated;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width;
        if (w > 0)
        {
            if (GridLine0 != null) GridLine0.X2 = w;
            if (GridLine1 != null) GridLine1.X2 = w;
            if (GridLine2 != null) GridLine2.X2 = w;
            if (GridLine3 != null) GridLine3.X2 = w;
            if (GridLine4 != null) GridLine4.X2 = w;
            RedrawChart();
        }
    }

    private void OnStatusUpdated(HardwareStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isLoaded) return;
            UpdateMetrics(status);
        });
    }

    private void UpdateMetrics(HardwareStatus status)
    {
        // 1. CPU
        if (status.CpuTemperature is int cpuTemp)
        {
            _minCpu = Math.Min(_minCpu, cpuTemp);
            _maxCpu = Math.Max(_maxCpu, cpuTemp);

            if (CpuTempBig != null) CpuTempBig.Text = $"{cpuTemp} °C";
            if (CpuMinMaxText != null) CpuMinMaxText.Text = $"Мин {_minCpu}° • Макс {_maxCpu}°";
        }
        else if (CpuTempBig != null)
        {
            CpuTempBig.Text = "Нет данных";
        }

        // 2. GPU
        int? gpuTemp = null;
        bool gpuActive = status.Gpu?.PowerState == GpuPowerState.Active;
        double? gpuWatts = null;
        string gpuState = "D3 Спит";

        if (status.Gpu != null)
        {
            if (gpuActive)
            {
                gpuTemp = status.Gpu.Temperature;
                gpuWatts = status.Gpu.PowerWatts;
                gpuState = gpuWatts.HasValue
                    ? $"Активна • {gpuWatts.Value:F1} Вт"
                    : "Активна";
                if (GpuTempBig != null)
                {
                    GpuTempBig.Text = gpuTemp.HasValue ? $"{gpuTemp.Value} °C" : "Активна";
                    GpuTempBig.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                }
            }
            else if (status.Gpu.PowerState == GpuPowerState.Sleeping)
            {
                gpuWatts = 0.0;
                gpuState = "D3cold Спит";
                if (GpuTempBig != null)
                {
                    GpuTempBig.Text = "Спит";
                    GpuTempBig.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                }
            }
            else
            {
                gpuState = "Eco Отключена";
                if (GpuTempBig != null)
                {
                    GpuTempBig.Text = "Откл";
                    GpuTempBig.Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
                }
            }

            if (GpuStateText != null) GpuStateText.Text = gpuState;
            if (GpuClockText != null) GpuClockText.Text = gpuActive && status.Gpu.CoreClockMhz > 0 ? $"{status.Gpu.CoreClockMhz} MHz" : "--";
            if (GpuVramText != null) GpuVramText.Text = gpuActive && status.Gpu.MemoryTotalMb > 0 ? $"{status.Gpu.MemoryUsedMb ?? 0} / {status.Gpu.MemoryTotalMb} MB" : "--";
            if (GpuUsageText != null) GpuUsageText.Text = gpuActive && status.Gpu.CoreUsage.HasValue ? $"{status.Gpu.CoreUsage.Value}%" : "--";
        }

        // 2b. System Power Breakdown
        double? cpuWatts = status.CpuPowerWatts;
        double? totalComponentWatts = null;
        if (cpuWatts.HasValue || (gpuWatts.HasValue && gpuActive))
        {
            totalComponentWatts = Math.Round((cpuWatts ?? 0) + (gpuWatts ?? 0), 1);
        }

        if (CpuWattsText != null)
            CpuWattsText.Text = cpuWatts.HasValue ? $"{cpuWatts.Value:F1} Вт" : "--";

        if (GpuWattsText != null)
            GpuWattsText.Text = gpuActive && gpuWatts.HasValue ? $"{gpuWatts.Value:F1} Вт" : (status.Gpu?.PowerState == GpuPowerState.Sleeping ? "0.0 Вт" : "--");

        if (BatteryPowerText != null)
        {
            if (status.BatteryDischargeWatts is > 0)
            {
                BatteryPowerText.Text = $"-{status.BatteryDischargeWatts.Value:F1} Вт";
                BatteryPowerText.Foreground = new SolidColorBrush(Color.FromRgb(255, 171, 64));
            }
            else if (status.BatteryChargeWatts is > 0)
            {
                BatteryPowerText.Text = $"+{status.BatteryChargeWatts.Value:F1} Вт";
                BatteryPowerText.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
            }
            else
            {
                BatteryPowerText.Text = status.IsAcOnline ? "От сети (AC)" : "Батарея";
                BatteryPowerText.Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            }
        }

        double? chartTotalWatts = null;
        if (TotalPowerLabel != null && TotalPowerText != null)
        {
            if (status.BatteryDischargeWatts is > 0)
            {
                TotalPowerLabel.Text = "ВСЕГО (БАТАРЕЯ)";
                TotalPowerText.Text = $"{status.BatteryDischargeWatts.Value:F1} Вт";
                chartTotalWatts = status.BatteryDischargeWatts.Value;
            }
            else
            {
                TotalPowerLabel.Text = "CPU + GPU";
                TotalPowerText.Text = totalComponentWatts.HasValue
                    ? $"{totalComponentWatts.Value:F1} Вт"
                    : "--";
                chartTotalWatts = totalComponentWatts;
            }
        }

        // 3. Fans
        if (Fan1RpmBig != null) Fan1RpmBig.Text = status.Fan1Rpm?.ToString() ?? "Нет данных";
        if (Fan2RpmBig != null) Fan2RpmBig.Text = status.Fan2Rpm?.ToString() ?? "Нет данных";

        // 4. Record History & Redraw Chart
        var sampleTime = status.ThermalSampledAtUtc ?? DateTimeOffset.UtcNow;
        RecordHistory(sampleTime, status.CpuTemperature, gpuTemp, gpuWatts, chartTotalWatts);
        RedrawChart();
    }

    private void RecordHistory(DateTimeOffset timestamp, int? cpu, int? gpu, double? gpuWatts, double? totalWatts)
    {
        _history.Add(new TelemetrySample(timestamp, cpu, gpu, gpuWatts, totalWatts));
        if (_history.Count > MaxHistorySamples) _history.RemoveAt(0);
    }

    private void RedrawChart()
    {
        if (ChartCanvas == null || ChartCanvas.ActualWidth <= 0) return;

        double w = ChartCanvas.ActualWidth;
        double h = ChartCanvas.ActualHeight > 0 ? ChartCanvas.ActualHeight : 140;

        int count = _history.Count;
        if (count < 2) return;

        if (GridLine0 != null && Math.Abs(GridLine0.X2 - w) > 1)
        {
            GridLine0.X2 = w;
            if (GridLine1 != null) GridLine1.X2 = w;
            if (GridLine2 != null) GridLine2.X2 = w;
            if (GridLine3 != null) GridLine3.X2 = w;
            if (GridLine4 != null) GridLine4.X2 = w;
        }

        double stepX = w / (MaxHistorySamples - 1);
        double startX = w - ((count - 1) * stepX);

        // Update time axis and title from actual timestamps
        TimeSpan span = _history[^1].Timestamp - _history[0].Timestamp;
        int spanSec = (int)Math.Max(1, Math.Round(span.TotalSeconds));

        if (ChartHeaderTitle != null)
        {
            ChartHeaderTitle.Text = $"ДИНАМИКА ТЕМПЕРАТУР И МОЩНОСТИ ({count} ЗАМЕРОВ • {spanSec} СЕК)";
        }
        if (TimeStartLabel != null) TimeStartLabel.Text = $"-{spanSec}с";
        if (TimeMidLabel != null) TimeMidLabel.Text = $"-{Math.Max(1, spanSec / 2)}с";
        if (TimeEndLabel != null) TimeEndLabel.Text = "Сейчас";

        // Build Paths for °C (0 - 100°C) and Watts (0 - 160W) with clean null gaps
        const double MaxTempScale = 100.0;
        const double MaxWattsScale = 160.0;

        if (CpuLine != null)
            CpuLine.Data = BuildPathGeometry(_history, s => s.CpuTemp, MaxTempScale, startX, stepX, h);

        if (GpuLine != null)
            GpuLine.Data = BuildPathGeometry(_history, s => s.GpuTemp, MaxTempScale, startX, stepX, h);

        if (PowerLine != null)
            PowerLine.Data = BuildPathGeometry(_history, s => s.GpuWatts, MaxWattsScale, startX, stepX, h);

        if (TotalPowerLine != null)
            TotalPowerLine.Data = BuildPathGeometry(_history, s => s.TotalWatts, MaxWattsScale, startX, stepX, h);
    }

    private static PathGeometry BuildPathGeometry(
        IReadOnlyList<TelemetrySample> history,
        Func<TelemetrySample, double?> valueSelector,
        double maxScale,
        double startX,
        double stepX,
        double h)
    {
        var geometry = new PathGeometry();
        PathFigure? currentSegment = null;

        for (int i = 0; i < history.Count; i++)
        {
            double x = startX + (i * stepX);
            double? val = valueSelector(history[i]);

            if (val.HasValue)
            {
                double clamped = Math.Clamp(val.Value, 0, maxScale);
                double y = (1.0 - clamped / maxScale) * h;
                var point = new Point(x, y);

                if (currentSegment == null)
                {
                    currentSegment = new PathFigure { StartPoint = point, IsFilled = false };
                    geometry.Figures.Add(currentSegment);
                }
                else
                {
                    currentSegment.Segments.Add(new LineSegment(point, isStroked: true));
                }
            }
            else
            {
                currentSegment = null;
            }
        }

        return geometry;
    }

    private void SplSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SplValText != null)
        {
            double spl = Math.Round(SplSlider.Value);
            SplValText.Text = $"{spl} Вт";

            double sppt = spl + 10;
            double fppt = spl + 20;
            if (ApuStatusHintText != null)
            {
                ApuStatusHintText.Text = $"Расчёт: SPL = {spl}W, sPPT = {sppt}W, fPPT = {fppt}W (ACPI SetApuPower)";
            }
        }
    }

    private async void ApplySpl_Click(object sender, RoutedEventArgs e)
    {
        double spl = Math.Round(SplSlider.Value);
        double sppt = spl + 10;
        double fppt = spl + 20;

        bool ok = await _monitor.SetApuPowerAsync(fppt, sppt, spl);
        if (ApuStatusHintText != null)
        {
            ApuStatusHintText.Text = ok
                ? $"Успешно применено в ACPI BIOS: SPL = {spl}W ✓"
                : "Ошибка отправки в WMI";
        }
    }

    private async void BtnApuEco_Click(object sender, RoutedEventArgs e)
    {
        SetActivePresetButton(BtnApuEco);
        SplSlider.Value = 28;
        bool ok = await _monitor.SetApuPowerAsync(45, 35, 28);
        if (ApuStatusHintText != null)
        {
            ApuStatusHintText.Text = ok ? "Применен профиль: Eco APU (SPL = 28W) ✓" : "Ошибка ACPI";
        }
    }

    private async void BtnApuBalanced_Click(object sender, RoutedEventArgs e)
    {
        SetActivePresetButton(BtnApuBalanced);
        SplSlider.Value = 45;
        bool ok = await _monitor.SetApuPowerAsync(65, 54, 45);
        if (ApuStatusHintText != null)
        {
            ApuStatusHintText.Text = ok ? "Применен профиль: Баланс APU (SPL = 45W) ✓" : "Ошибка ACPI";
        }
    }

    private async void BtnApuTurbo_Click(object sender, RoutedEventArgs e)
    {
        SetActivePresetButton(BtnApuTurbo);
        SplSlider.Value = 65;
        bool ok = await _monitor.SetApuPowerAsync(85, 75, 65);
        if (ApuStatusHintText != null)
        {
            ApuStatusHintText.Text = ok ? "Применен профиль: Турбо APU (SPL = 65W) ✓" : "Ошибка ACPI";
        }
    }

    private void SetActivePresetButton(Button btn)
    {
        if (BtnApuEco != null) BtnApuEco.Tag = btn == BtnApuEco ? "Active" : null;
        if (BtnApuBalanced != null) BtnApuBalanced.Tag = btn == BtnApuBalanced ? "Active" : null;
        if (BtnApuTurbo != null) BtnApuTurbo.Tag = btn == BtnApuTurbo ? "Active" : null;
    }
}
