using System.Threading;
using System.Windows;

namespace AEROCC.App;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appGuid = "AEROCC_SingleInstance_App_Aero16";
        _mutex = new Mutex(true, appGuid, out bool isNewInstance);
        if (!isNewInstance)
        {
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
