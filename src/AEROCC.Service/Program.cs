using System.Diagnostics;
using AEROCC.Service.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string ServiceName = "AEROCCService";
const string ServiceDisplayName = "AEROCC Hardware Control Service";

if (args.Length > 0)
{
    var arg = args[0].ToLowerInvariant();
    switch (arg)
    {
        case "--install":
            InstallService();
            return;

        case "--uninstall":
            UninstallService();
            return;

        case "--start":
            StartService();
            return;

        case "--stop":
            StopService();
            return;

        case "--help":
        case "-h":
            Console.WriteLine("AEROCC Background Service CLI Commands:");
            Console.WriteLine("  --install    Register Windows Service (Auto-start)");
            Console.WriteLine("  --uninstall  Stop and delete Windows Service");
            Console.WriteLine("  --start      Start the registered service");
            Console.WriteLine("  --stop       Stop the running service");
            return;
    }
}

// Build and run host (either as Windows Service or interactive Console)
if (!AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[!] Внимание: Консоль запущена БЕЗ прав Администратора.");
    Console.WriteLine("    Прямой доступ к WMI требует прав Администратора или LocalSystem.");
    Console.WriteLine("    Для работы в консоли запустите терминал от Администратора,");
    Console.WriteLine("    либо установите службу в Windows: AEROCC.Service.exe --install");
    Console.ResetColor();
    Console.WriteLine();
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceName;
});

builder.Services.AddHostedService<HardwareService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = ServiceName;
});

var host = builder.Build();
await host.RunAsync();

static void InstallService()
{
    if (!AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Ошибка: Для установки службы Windows требуются права Администратора.");
        Console.WriteLine("    Запустите терминал от имени Администратора и повторите команду.");
        Console.ResetColor();
        return;
    }

    string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName!;
    Console.WriteLine($"Установка службы Windows: {ServiceName}...");
    Console.WriteLine($"Исполняемый файл: {exePath}");

    bool created = RunSc($"create {ServiceName} binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{ServiceDisplayName}\"");
    if (created)
    {
        RunSc($"description {ServiceName} \"Легковесная служба низкоуровневого управления Gigabyte Aero 16 (AEROCC)\"");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[✓] Служба AEROCCService успешно установлена с автозапуском!");
        Console.ResetColor();

        Console.WriteLine("Запуск службы...");
        StartService();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Не удалось установить службу AEROCCService.");
        Console.ResetColor();
    }
}

static void UninstallService()
{
    if (!AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Ошибка: Для удаления службы Windows требуются права Администратора.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine($"Удаление службы Windows: {ServiceName}...");
    RunSc($"stop {ServiceName}");
    bool deleted = RunSc($"delete {ServiceName}");
    if (deleted)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[✓] Служба AEROCCService успешно удалена.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Не удалось удалить службу AEROCCService.");
        Console.ResetColor();
    }
}

static void StartService()
{
    if (!AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Ошибка: Для запуска службы требуются права Администратора.");
        Console.ResetColor();
        return;
    }

    bool started = RunSc($"start {ServiceName}");
    if (started)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[✓] Служба AEROCCService запущена.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Не удалось запустить службу AEROCCService.");
        Console.ResetColor();
    }
}

static void StopService()
{
    if (!AEROCC.Core.Wmi.GigabyteWmiClient.IsAdministrator())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Ошибка: Для остановки службы требуются права Администратора.");
        Console.ResetColor();
        return;
    }

    bool stopped = RunSc($"stop {ServiceName}");
    if (stopped)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[✓] Служба AEROCCService остановлена.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[x] Не удалось остановить службу AEROCCService.");
        Console.ResetColor();
    }
}

static bool RunSc(string arguments)
{
    try
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        proc?.WaitForExit();
        var output = proc?.StandardOutput.ReadToEnd();
        var err = proc?.StandardError.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine(output.Trim());
        }
        if (!string.IsNullOrWhiteSpace(err))
        {
            Console.WriteLine(err.Trim());
        }
        return proc?.ExitCode == 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[sc error]: {ex.Message}");
        return false;
    }
}
