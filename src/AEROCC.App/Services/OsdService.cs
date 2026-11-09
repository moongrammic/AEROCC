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
        if (_window == null)
        {
            _window = new OsdWindow();
        }
    }

    public void ShowWinLock(bool locked)
    {
        Initialize();
        if (locked)
        {
            _window?.ShowNotification(
                "Клавиша Win",
                "Заблокирована (Игровой режим)",
                SymbolRegular.LockClosed24,
                Color.FromRgb(255, 170, 0)); // Amber
        }
        else
        {
            _window?.ShowNotification(
                "Клавиша Win",
                "Разблокирована",
                SymbolRegular.LockOpen24,
                Color.FromRgb(46, 229, 157)); // Emerald Green
        }
    }

    public void ShowFnLock(bool locked)
    {
        Initialize();
        if (locked)
        {
            _window?.ShowNotification(
                "Режим Fn Lock",
                "Включен (F1–F12 как стандартные)",
                SymbolRegular.Keyboard24,
                Color.FromRgb(0, 210, 255)); // Cyan
        }
        else
        {
            _window?.ShowNotification(
                "Режим Fn Lock",
                "Выключен (F1–F12 как мультимедиа)",
                SymbolRegular.Speaker224,
                Color.FromRgb(220, 220, 230)); // White/Gray
        }
    }

    public void ShowFanProfile(FanProfile profile)
    {
        Initialize();
        var (name, color, symbol) = profile switch
        {
            FanProfile.Eco => ("Eco (Тихий)", Color.FromRgb(46, 229, 157), SymbolRegular.LeafOne24),
            FanProfile.Normal => ("Normal (Баланс)", Color.FromRgb(0, 180, 255), SymbolRegular.CheckmarkCircle24),
            FanProfile.Game => ("Game (Игровой)", Color.FromRgb(255, 150, 0), SymbolRegular.Games24),
            FanProfile.Turbo => ("Turbo (100% Обдув)", Color.FromRgb(255, 60, 60), SymbolRegular.WeatherBlowingSnow24),
            _ => (profile.ToString(), Color.FromRgb(200, 200, 200), SymbolRegular.Settings24)
        };

        _window?.ShowNotification("Профиль вентиляторов", name, symbol, color);
    }

    public void ShowRefreshRate(int hz)
    {
        Initialize();
        Color col = hz >= 120 ? Color.FromRgb(180, 100, 255) : Color.FromRgb(0, 200, 255);
        _window?.ShowNotification("Частота экрана", $"{hz} Гц", SymbolRegular.Desktop24, col);
    }

    public void ShowBatteryLimit(int percent)
    {
        Initialize();
        _window?.ShowNotification(
            "Порог заряда аккумулятора",
            $"{percent}%",
            SymbolRegular.BatteryCharge24,
            Color.FromRgb(46, 229, 157));
    }
}
