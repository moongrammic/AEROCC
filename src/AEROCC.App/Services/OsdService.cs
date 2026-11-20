using System.Windows.Media;
using AEROCC.App.Views;
using AEROCC.Core.Models;
using Wpf.Ui.Controls;

namespace AEROCC.App.Services;

public class OsdService
{
    private static OsdService? _instance;
    public static OsdService Instance => _instance ??= new OsdService();

    private OsdWindow? _window;

    public void Initialize()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Initialize);
            return;
        }

        if (_window == null)
        {
            _window = new OsdWindow();
        }
    }

    private void PostNotification(string title, string message, SymbolRegular symbol, Color accentColor)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => PostNotification(title, message, symbol, accentColor));
            return;
        }

        if (_window == null)
        {
            _window = new OsdWindow();
        }

        _window.ShowNotification(title, message, symbol, accentColor);
    }

    public void ShowWinLock(bool locked)
    {
        if (locked)
        {
            PostNotification(
                "Клавиша Win",
                "Заблокирована (Игровой режим)",
                SymbolRegular.LockClosed24,
                Color.FromRgb(255, 170, 0)); // Amber
        }
        else
        {
            PostNotification(
                "Клавиша Win",
                "Разблокирована",
                SymbolRegular.LockOpen24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
    }

    public void ShowFnLock(bool locked)
    {
        if (locked)
        {
            PostNotification(
                "Режим клавиш (Fn + Esc)",
                "Мультимедиа (Громкость, яркость)",
                SymbolRegular.Speaker224,
                Color.FromRgb(0, 210, 255)); // Cyan
        }
        else
        {
            PostNotification(
                "Режим клавиш (Fn + Esc)",
                "Стандартные F1–F12 (Обычный режим)",
                SymbolRegular.Keyboard24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
    }

    public void ShowFanProfile(FanProfile profile)
    {
        var (name, color, symbol) = profile switch
        {
            FanProfile.Eco => ("Eco (Тихий)", Color.FromRgb(46, 229, 157), SymbolRegular.LeafOne24),
            FanProfile.Normal => ("Normal (Баланс)", Color.FromRgb(0, 180, 255), SymbolRegular.CheckmarkCircle24),
            FanProfile.Game => ("Game (Игровой)", Color.FromRgb(255, 150, 0), SymbolRegular.Games24),
            FanProfile.Turbo => ("Turbo (100% Обдув)", Color.FromRgb(255, 60, 60), SymbolRegular.WeatherBlowingSnow24),
            _ => (profile.ToString(), Color.FromRgb(200, 200, 200), SymbolRegular.Settings24)
        };

        PostNotification("Профиль вентиляторов", name, symbol, color);
    }

    public void ShowRefreshRate(int hz)
    {
        Color col = hz >= 120 ? Color.FromRgb(180, 100, 255) : Color.FromRgb(0, 200, 255);
        PostNotification("Частота экрана", $"{hz} Гц", SymbolRegular.Desktop24, col);
    }

    public void ShowBatteryLimit(int percent)
    {
        PostNotification(
            "Порог заряда аккумулятора",
            $"{percent}%",
            SymbolRegular.BatteryCharge24,
            Color.FromRgb(46, 229, 157));
    }

    public void ShowGpuMode(AEROCC.Core.Gpu.GpuPowerMode mode)
    {
        var (title, desc, color, symbol) = mode switch
        {
            AEROCC.Core.Gpu.GpuPowerMode.Eco => ("Режим видеокарты", "Eco (Только iGPU / Макс. батарея)", Color.FromRgb(46, 229, 157), SymbolRegular.LeafOne24),
            AEROCC.Core.Gpu.GpuPowerMode.Dynamic => ("Режим видеокарты", "Dynamic (Optimus / Автопереключение)", Color.FromRgb(0, 180, 255), SymbolRegular.ArrowSync24),
            AEROCC.Core.Gpu.GpuPowerMode.Performance => ("Режим видеокарты", "Performance (dGPU всегда активна)", Color.FromRgb(255, 120, 0), SymbolRegular.Flash24),
            _ => ("Режим видеокарты", mode.ToString(), Color.FromRgb(200, 200, 200), SymbolRegular.DeviceEq24)
        };

        PostNotification(title, desc, symbol, color);
    }

    public void ShowTouchpad(bool enabled)
    {
        if (enabled)
        {
            PostNotification(
                "Сенсорная панель (Fn + F9)",
                "Включена",
                SymbolRegular.Cursor24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
        else
        {
            PostNotification(
                "Сенсорная панель (Fn + F9)",
                "Заблокирована",
                SymbolRegular.LockClosed24,
                Color.FromRgb(255, 170, 0)); // Amber
        }
    }

    public void ShowAirplaneMode(bool enabled)
    {
        if (enabled)
        {
            PostNotification(
                "Режим полета (Fn + F10)",
                "Включен (Беспроводные модули откл.)",
                SymbolRegular.Airplane24,
                Color.FromRgb(255, 170, 0)); // Amber
        }
        else
        {
            PostNotification(
                "Режим полета (Fn + F10)",
                "Выключен (Связь активна)",
                SymbolRegular.AirplaneTakeOff24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
    }

    public void ShowMicrophone(bool muted)
    {
        if (muted)
        {
            PostNotification(
                "Микрофон (Fn + F4)",
                "Выключен (Звук отключен)",
                SymbolRegular.MicOff24,
                Color.FromRgb(255, 70, 70)); // Red
        }
        else
        {
            PostNotification(
                "Микрофон (Fn + F4)",
                "Включен (Активен)",
                SymbolRegular.Mic24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
    }

    public void ShowBacklight(int percent)
    {
        PostNotification(
            "Подсветка клавиатуры (Fn + Space)",
            percent > 0 ? $"{percent}%" : "Выключена (0%)",
            SymbolRegular.WeatherSunny24,
            Color.FromRgb(0, 210, 255)); // Cyan
    }
}
