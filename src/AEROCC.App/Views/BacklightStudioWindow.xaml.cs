using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AEROCC.App.Services;
using AEROCC.Core.Models;

namespace AEROCC.App.Views;

public partial class BacklightStudioWindow : Window
{
    private readonly HardwareMonitor _monitor = HardwareMonitor.Instance;
    private readonly DispatcherTimer _applyTimer;

    private KeyboardEffect _currentEffect = KeyboardEffect.Static;
    private KeyboardColor _currentColor = KeyboardColor.White;
    private byte _brightness = 100;
    private byte _speed = 5;
    private byte _direction = 1;
    private byte _customR = 255;
    private byte _customG = 112;
    private byte _customB = 20;
    private bool _isCustomRgb = false;
    private bool _isInitializing = true;

    public BacklightStudioWindow()
    {
        InitializeComponent();

        _applyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _applyTimer.Tick += OnApplyTimerTick;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Try reading initial state from hardware
        var status = _monitor.GetBacklightStatus();
        if (status != null)
        {
            _currentEffect = status.Effect;
            _currentColor = status.Color;
            _brightness = (byte)status.Brightness;
            _speed = (byte)Math.Clamp(status.Speed, 1, 10);
            _direction = (byte)status.Direction;
        }
        else
        {
            int curBr = _monitor.LatestStatus.BacklightBrightness;
            if (curBr >= 0) _brightness = (byte)curBr;
        }

        BrightnessSlider.Value = _brightness;
        SpeedSlider.Value = _speed;
        UpdateActiveEffectButtons();
        UpdateDirectionButtons();
        UpdatePreviewUI();

        _isInitializing = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EffButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == BtnEffStatic) _currentEffect = KeyboardEffect.Static;
        else if (btn == BtnEffBreathing) _currentEffect = KeyboardEffect.Breathing;
        else if (btn == BtnEffWave) _currentEffect = KeyboardEffect.Wave;
        else if (btn == BtnEffNeon) _currentEffect = KeyboardEffect.Neon;
        else if (btn == BtnEffSunset) _currentEffect = KeyboardEffect.Sunset;
        else if (btn == BtnEffSunsetBreath) _currentEffect = KeyboardEffect.SunsetBreathing;
        else if (btn == BtnEffSunsetNeon) _currentEffect = KeyboardEffect.SunsetNeon;
        else if (btn == BtnEffCustom)
        {
            _currentEffect = KeyboardEffect.Custom;
            _isCustomRgb = true;
        }

        if (_currentEffect != KeyboardEffect.Custom)
        {
            _isCustomRgb = false;
        }

        UpdateActiveEffectButtons();
        TriggerApply();
    }

    private void UpdateActiveEffectButtons()
    {
        BtnEffStatic.Tag = _currentEffect == KeyboardEffect.Static ? "Active" : null;
        BtnEffBreathing.Tag = _currentEffect == KeyboardEffect.Breathing ? "Active" : null;
        BtnEffWave.Tag = _currentEffect == KeyboardEffect.Wave ? "Active" : null;
        BtnEffNeon.Tag = _currentEffect == KeyboardEffect.Neon ? "Active" : null;
        BtnEffSunset.Tag = _currentEffect == KeyboardEffect.Sunset ? "Active" : null;
        BtnEffSunsetBreath.Tag = _currentEffect == KeyboardEffect.SunsetBreathing ? "Active" : null;
        BtnEffSunsetNeon.Tag = _currentEffect == KeyboardEffect.SunsetNeon ? "Active" : null;
        BtnEffCustom.Tag = _currentEffect == KeyboardEffect.Custom ? "Active" : null;
    }

    private void SwatchRainbow_Click(object sender, RoutedEventArgs e)
    {
        _currentColor = KeyboardColor.Random;
        _isCustomRgb = false;

        // If current effect is static or custom, switch to Wave so the rainbow moves
        if (_currentEffect == KeyboardEffect.Static || _currentEffect == KeyboardEffect.Custom)
        {
            _currentEffect = KeyboardEffect.Wave;
        }

        UpdateActiveEffectButtons();
        TriggerApply();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Background is not SolidColorBrush brush) return;

        Color c = brush.Color;
        _customR = c.R;
        _customG = c.G;
        _customB = c.B;
        _isCustomRgb = false;

        _isInitializing = true;
        SliderR.Value = _customR;
        SliderG.Value = _customG;
        SliderB.Value = _customB;
        _isInitializing = false;

        // Map standard colors
        if (c.R == 255 && c.G == 255 && c.B == 255) _currentColor = KeyboardColor.White;
        else if (c.R == 0 && c.G >= 200 && c.B == 255) _currentColor = KeyboardColor.Blue;
        else if (c.R == 0 && c.G >= 200 && c.B <= 120) _currentColor = KeyboardColor.Green;
        else if (c.R >= 240 && c.G >= 200 && c.B <= 80) _currentColor = KeyboardColor.Yellow;
        else if (c.R == 255 && c.G >= 100 && c.B <= 40) _currentColor = KeyboardColor.Orange;
        else if (c.R >= 240 && c.G <= 60 && c.B <= 90) _currentColor = KeyboardColor.Red;
        else if (c.R >= 160 && c.G <= 140 && c.B >= 240) _currentColor = KeyboardColor.Purple;
        else
        {
            _isCustomRgb = true;
            _currentEffect = KeyboardEffect.Custom;
            UpdateActiveEffectButtons();
        }

        TriggerApply();
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        _customR = (byte)SliderR.Value;
        _customG = (byte)SliderG.Value;
        _customB = (byte)SliderB.Value;
        _isCustomRgb = true;
        _currentEffect = KeyboardEffect.Custom;

        UpdateActiveEffectButtons();
        TriggerApply();
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brightness = (byte)BrightnessSlider.Value;
        if (BrightnessValText != null) BrightnessValText.Text = $"{_brightness}%";
        if (!_isInitializing) TriggerApply();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _speed = (byte)SpeedSlider.Value;
        if (SpeedValText != null) SpeedValText.Text = $"{_speed} / 10";
        if (!_isInitializing) TriggerApply();
    }

    private void DirButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        _direction = (byte)(btn == BtnDirLeft ? 1 : 2);
        UpdateDirectionButtons();
        TriggerApply();
    }

    private void UpdateDirectionButtons()
    {
        BtnDirLeft.Tag = _direction == 1 ? "Active" : null;
        BtnDirRight.Tag = _direction == 2 ? "Active" : null;
    }

    private void PresetRainbow_Click(object sender, RoutedEventArgs e)
    {
        _currentEffect = KeyboardEffect.Wave;
        _currentColor = KeyboardColor.Random;
        _brightness = 100;
        _speed = 6;
        _direction = 1;
        _isCustomRgb = false;

        ApplyPresetValues();
    }

    private void PresetOffice_Click(object sender, RoutedEventArgs e)
    {
        _currentEffect = KeyboardEffect.Static;
        _currentColor = KeyboardColor.White;
        _brightness = 75;
        _customR = 255; _customG = 255; _customB = 255;
        _isCustomRgb = false;

        ApplyPresetValues();
    }

    private void PresetNight_Click(object sender, RoutedEventArgs e)
    {
        _currentEffect = KeyboardEffect.Static;
        _currentColor = KeyboardColor.Orange;
        _brightness = 35;
        _customR = 255; _customG = 130; _customB = 0;
        _isCustomRgb = false;

        ApplyPresetValues();
    }

    private void PresetCyber_Click(object sender, RoutedEventArgs e)
    {
        _currentEffect = KeyboardEffect.Neon;
        _currentColor = KeyboardColor.Purple;
        _brightness = 90;
        _speed = 6;
        _customR = 180; _customG = 0; _customB = 255;
        _isCustomRgb = false;

        ApplyPresetValues();
    }

    private void PresetOff_Click(object sender, RoutedEventArgs e)
    {
        _brightness = 0;
        BrightnessSlider.Value = 0;
        TriggerApply();
    }

    private void ApplyPresetValues()
    {
        _isInitializing = true;
        BrightnessSlider.Value = _brightness;
        SpeedSlider.Value = _speed;
        SliderR.Value = _customR;
        SliderG.Value = _customG;
        SliderB.Value = _customB;
        _isInitializing = false;

        UpdateActiveEffectButtons();
        UpdateDirectionButtons();
        TriggerApply();
    }

    private void TriggerApply()
    {
        UpdatePreviewUI();
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private async void OnApplyTimerTick(object? sender, EventArgs e)
    {
        _applyTimer.Stop();

        try
        {
            if (_brightness == 0)
            {
                await _monitor.SetKeyboardBrightnessAsync(0);
                return;
            }

            if (_isCustomRgb || _currentEffect == KeyboardEffect.Custom)
            {
                await _monitor.SetCustomRgbAsync(_customR, _customG, _customB, _brightness);
            }
            else
            {
                await _monitor.SetBacklightFullAsync(_currentEffect, _brightness, _currentColor, _speed, _direction);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BacklightStudio Apply Error]: {ex.Message}");
        }
    }

    private void UpdatePreviewUI()
    {
        if (_currentColor == KeyboardColor.Random)
        {
            var rainbowBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(255, 0, 0), 0),
                    new(Color.FromRgb(255, 153, 0), 0.2),
                    new(Color.FromRgb(51, 255, 0), 0.4),
                    new(Color.FromRgb(0, 255, 255), 0.6),
                    new(Color.FromRgb(0, 102, 255), 0.8),
                    new(Color.FromRgb(204, 0, 255), 1.0)
                },
                new Point(0, 0),
                new Point(1, 0)
            );

            if (ColorPreviewBox != null)
            {
                ColorPreviewBox.Background = rainbowBrush;
                if (ColorPreviewBox.Effect is System.Windows.Media.Effects.DropShadowEffect ds)
                {
                    ds.Color = Color.FromRgb(0, 229, 255);
                }
            }

            if (HexPreviewText != null)
            {
                HexPreviewText.Text = "Радуга";
            }

            if (SpacebarGlow != null)
            {
                SpacebarGlow.Background = rainbowBrush;
                if (PreviewGlowEffect != null)
                {
                    PreviewGlowEffect.Color = Color.FromRgb(0, 229, 255);
                    PreviewGlowEffect.Opacity = _brightness / 100.0 * 0.9;
                }
            }

            if (PreviewStatusText != null)
            {
                string effName = _currentEffect switch
                {
                    KeyboardEffect.Wave => "Радужная волна",
                    KeyboardEffect.Neon => "Радужный неон",
                    KeyboardEffect.Breathing => "Радужное дыхание",
                    _ => "Радуга"
                };

                PreviewStatusText.Text = $"🌈 {effName} • {_brightness}% • Скорость {_speed}/10";
            }

            if (BtnSwatchRainbow != null) BtnSwatchRainbow.Tag = "Active";
            return;
        }

        if (BtnSwatchRainbow != null) BtnSwatchRainbow.Tag = null;

        var previewColor = Color.FromRgb(_customR, _customG, _customB);
        var previewBrush = new SolidColorBrush(previewColor);

        if (ColorPreviewBox != null)
        {
            ColorPreviewBox.Background = previewBrush;
            if (ColorPreviewBox.Effect is System.Windows.Media.Effects.DropShadowEffect ds)
            {
                ds.Color = previewColor;
            }
        }

        if (HexPreviewText != null)
        {
            HexPreviewText.Text = $"#{_customR:X2}{_customG:X2}{_customB:X2}";
        }

        if (SpacebarGlow != null)
        {
            SpacebarGlow.Background = previewBrush;
            if (PreviewGlowEffect != null)
            {
                PreviewGlowEffect.Color = previewColor;
                PreviewGlowEffect.Opacity = _brightness / 100.0 * 0.9;
            }
        }

        if (PreviewStatusText != null)
        {
            string effName = _currentEffect switch
            {
                KeyboardEffect.Static => "Статический",
                KeyboardEffect.Breathing => "Дыхание",
                KeyboardEffect.Wave => "Волна",
                KeyboardEffect.Neon => "Неон",
                KeyboardEffect.Sunset => "Закат",
                KeyboardEffect.SunsetBreathing => "Закат (Дыхание)",
                KeyboardEffect.SunsetNeon => "Закат (Неон)",
                KeyboardEffect.Custom => "Custom RGB",
                _ => "Эффект"
            };

            PreviewStatusText.Text = $"{effName} • {_brightness}% • #{_customR:X2}{_customG:X2}{_customB:X2}";
        }
    }
}
