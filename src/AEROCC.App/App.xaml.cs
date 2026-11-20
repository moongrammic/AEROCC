using System.Threading;
using System.Windows;

namespace AEROCC.App;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex = false;
    public bool IsSessionEnding { get; private set; } = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Catch unhandled UI exceptions so app never silently disappears
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"Произошла ошибка в работе интерфейса:\n{args.Exception.Message}\n\nСтек:\n{args.Exception.StackTrace}",
                "AEROCC Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Критическая ошибка приложения:\n{ex.Message}",
                    "AEROCC Критическая ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        SessionEnding += (s, args) =>
        {
            IsSessionEnding = true;
        };

        const string appGuid = "AEROCC_SingleInstance_App_Aero16";
        _mutex = new Mutex(true, appGuid, out bool isNewInstance);
        _ownsMutex = isNewInstance;

        if (!_ownsMutex)
        {
            MessageBox.Show(
                "Приложение AEROCC уже запущено в системном трее (возле часов Windows).",
                "AEROCC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        AEROCC.Core.Wmi.GigabyteWmiMofRegistrar.EnsureRegistered();

        var mainWindow = new MainWindow();
        bool startMinimized = e.Args.Contains("--minimized") || e.Args.Contains("-m") || e.Args.Contains("/min");
        if (!startMinimized)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                _mutex.Dispose();
                _mutex = null;
                _ownsMutex = false;
            }
        }
        base.OnExit(e);
    }
}
