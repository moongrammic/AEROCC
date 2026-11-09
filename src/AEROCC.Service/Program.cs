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
    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath!;
    Console.WriteLine($"Установка службы Windows: {ServiceName}...");
    Console.WriteLine($"Исполняемый файл: {exePath}");

    RunSc($"create {ServiceName} binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{ServiceDisplayName}\"");
    RunSc($"description {ServiceName} \"Легковесная служба низкоуровневого управления Gigabyte Aero 16 (AEROCC)\"");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Служба AEROCCService успешно установлена с автозапуском!");
    Console.ResetColor();

    Console.WriteLine("Запуск службы...");
    StartService();
}

static void UninstallService()
{
    Console.WriteLine($"Удаление службы Windows: {ServiceName}...");
    RunSc($"stop {ServiceName}");
    RunSc($"delete {ServiceName}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Служба AEROCCService успешно удалена.");
    Console.ResetColor();
}

static void StartService()
{
    RunSc($"start {ServiceName}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Служба AEROCCService запущена.");
    Console.ResetColor();
}

static void StopService()
{
    RunSc($"stop {ServiceName}");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Служба AEROCCService остановлена.");
    Console.ResetColor();
}

static void RunSc(string arguments)
{
    try
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        });

        proc?.WaitForExit();
        var output = proc?.StandardOutput.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine(output.Trim());
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[sc error]: {ex.Message}");
    }
}
