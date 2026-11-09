using System.Threading;
using System.Windows;

namespace AEROCC.App;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

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

        const string appGuid = "AEROCC_SingleInstance_App_Aero16";
        _mutex = new Mutex(true, appGuid, out bool isNewInstance);
        if (!isNewInstance)
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

        var mainWindow = new MainWindow();
        bool startMinimized = e.Args.Contains("--minimized") || e.Args.Contains("-m") || e.Args.Contains("/min");
        if (!startMinimized)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
