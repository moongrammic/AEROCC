using System.Text;
using AEROCC.Core.Models;
using AEROCC.Core.Wmi;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "AEROCC - Hardware Control PoC (Aero 16 1VH)";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("============================================================");
Console.WriteLine("    AEROCC - Control Center Proof-of-Concept CLI");
Console.WriteLine("    Platform: Gigabyte Aero 16 (1VH) / AMD Ryzen AI + RTX");
Console.WriteLine("============================================================");
Console.ResetColor();

bool isAdmin = GigabyteWmiClient.IsAdministrator();
if (!isAdmin)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[!] Внимание: Консоль запущена БЕЗ прав Администратора.");
    Console.WriteLine("    Прямой доступ к root\\WMI (GB_WMIACPI_*) требует прав Администратора.");
    Console.WriteLine("    Пожалуйста, запустите эту утилиту от имени Администратора!");
    Console.ResetColor();
    Console.WriteLine();
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Права Администратора подтверждены. Доступ к root\\WMI разрешен.");
    Console.ResetColor();
    Console.WriteLine();
}

var client = new GigabyteWmiClient();
var ipcClient = new AEROCC.Core.Ipc.AeroccIpcClient();

// Check IPC connection to background service
var serviceCheck = await ipcClient.SendCommandAsync(new AEROCC.Core.Ipc.IpcMessage { Command = AEROCC.Core.Ipc.IpcConstants.CmdPing }, timeoutMs: 500);
if (serviceCheck.Success)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Подключено к фоновой службе AEROCCService по каналу IPC (Named Pipes)!");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("[-] Фоновая служба AEROCCService не запущена (используется прямой доступ).");
    Console.ResetColor();
}

// Initialize USB HID keyboard for Aero 16
bool hidFound = client.Keyboard.Initialize();
if (hidFound)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[✓] Обнаружен контроллер клавиатуры Aero 16 по шине USB HID (VID: 0x0414)");
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("[-] Контроллер клавиатуры USB HID пока не подключен (используется WMI)");
    Console.ResetColor();
}

while (true)
{
    PrintMenu();
    Console.Write("\nВыберите действие [0-8]: ");
    var key = Console.ReadLine()?.Trim();

    try
    {
        switch (key)
        {
            case "1":
                DisplayCurrentStatus(client);
                break;

            case "2":
                TestBatteryLimit(client);
                break;

            case "3":
                TestFanProfile(client);
                break;

            case "4":
                TestWinKeyLock(client);
                break;

            case "5":
                TestFnKeyLock(client);
                break;

            case "6":
                RunLiveMonitor(client);
                break;

            case "7":
                RunWmiDiagnostics(client);
                break;

            case "8":
                RunHidDiagnostics(client);
                break;

            case "9":
                await TestIpcServiceAsync(ipcClient);
                break;

            case "0":
            case "exit":
            case "q":
                Console.WriteLine("Выход...");
                return;

            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Неизвестная команда. Попробуйте снова.");
                Console.ResetColor();
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[Ошибка выполнения]: {ex.Message}");
        Console.ResetColor();
    }
}

static void PrintMenu()
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("\n------------------- МЕНЮ УПРАВЛЕНИЯ -------------------");
    Console.ResetColor();
    Console.WriteLine(" 1. Считать текущее состояние (CPU Temp, Fan RPM, Батарея)");
    Console.WriteLine(" 2. Установить порог заряда батареи (60% / 80% / 100%)");
    Console.WriteLine(" 3. Переключить профиль вентиляторов (Eco / Normal / Game / Turbo)");
    Console.WriteLine(" 4. Управление блокировкой WinKey (Заблокировать / Разблокировать)");
    Console.WriteLine(" 5. Управление блокировкой FnKey (Заблокировать / Разблокировать)");
    Console.WriteLine(" 6. Запустить живой аппаратный монитор (обновление раз в секунду)");
    Console.WriteLine(" 7. Полная диагностика WMI (опрос всех доступных методов ядра)");
    Console.WriteLine(" 8. Диагностика USB HID клавиатуры");
    Console.WriteLine(" 9. Тест IPC канала к службе AEROCCService (Named Pipes)");
    Console.WriteLine(" 0. Выход");
}

static void DisplayCurrentStatus(GigabyteWmiClient client)
{
    Console.WriteLine("\n[Опрос оборудования...]");
    var status = client.GetHardwareStatus();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  - Температура CPU:          {status.CpuTemperature} °C");
    Console.WriteLine($"  - Вентилятор 1 (CPU):       {status.Fan1Rpm} RPM");
    Console.WriteLine($"  - Вентилятор 2 (GPU):       {status.Fan2Rpm} RPM");
    Console.WriteLine($"  - Порог заряда аккумулятора:{status.BatteryChargeLimit}% (Policy: {status.BatteryPolicy})");
    Console.WriteLine($"  - WinKey заблокирована:     {FormatBool(status.WinKeyLocked)}");
    Console.WriteLine($"  - FnKey заблокирована:      {FormatBool(status.FnKeyLocked)}");
    Console.ResetColor();
}

static string FormatBool(bool? val) => val switch
{
    true => "Да (Заблокирована)",
    false => "Нет (Разблокирована)",
    null => "Н/Д"
};

static void TestBatteryLimit(GigabyteWmiClient client)
{
    Console.WriteLine("\nВыберите порог заряда аккумулятора:");
    Console.WriteLine(" 1. 60%");
    Console.WriteLine(" 2. 80% (рекомендуемый для сохранения ресурса)");
    Console.WriteLine(" 3. 100% (полный заряд)");
    Console.Write("Выбор: ");
    var input = Console.ReadLine()?.Trim();
    int target = input switch
    {
        "1" => 60,
        "2" => 80,
        "3" => 100,
        _ => 0
    };

    if (target == 0)
    {
        Console.WriteLine("Отмена.");
        return;
    }

    Console.WriteLine($"Установка порога заряда {target}%...");
    bool ok = client.SetBatteryChargeLimit(target);
    if (ok)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Команда установки порога {target}% отправлена!");
        Console.ResetColor();
        Thread.Sleep(500);
        int current = client.GetBatteryChargeLimit();
        Console.WriteLine($"Текущий считанный порог из WMI: {current}%");
    }
}

static void TestFanProfile(GigabyteWmiClient client)
{
    Console.WriteLine("\nВыберите профиль вентиляторов:");
    Console.WriteLine(" 1. Eco (Тихий / энергосберегающий)");
    Console.WriteLine(" 2. Normal (Сбалансированный)");
    Console.WriteLine(" 3. Game (Игровой / повышенные обороты)");
    Console.WriteLine(" 4. Turbo (Максимальный обдув 100%)");
    Console.Write("Выбор: ");
    var input = Console.ReadLine()?.Trim();
    FanProfile? profile = input switch
    {
        "1" => FanProfile.Eco,
        "2" => FanProfile.Normal,
        "3" => FanProfile.Game,
        "4" => FanProfile.Turbo,
        _ => null
    };

    if (profile == null)
    {
        Console.WriteLine("Отмена.");
        return;
    }

    Console.WriteLine($"Активация профиля {profile.Value}...");
    client.SetFanProfile(profile.Value);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[✓] Профиль {profile.Value} успешно применен в BIOS EC!");
    Console.ResetColor();
}

static void TestWinKeyLock(GigabyteWmiClient client)
{
    bool? current = client.GetWinKeyLock();
    Console.WriteLine($"\n--- УПРАВЛЕНИЕ КЛАВИШЕЙ WIN (WinKey Lock) ---");
    Console.WriteLine($"Текущее состояние: {FormatBool(current)}");
    Console.WriteLine(" 1. Заблокировать (WinKey Lock ON - клавиша Win НЕ нажимается)");
    Console.WriteLine(" 2. Разблокировать (WinKey Lock OFF - клавиша Win активна)");
    Console.WriteLine(" 3. Переключить (Toggle)");
    Console.Write("Выбор [1-3]: ");
    var opt = Console.ReadLine()?.Trim();
    bool? target = opt switch
    {
        "1" => true,
        "2" => false,
        "3" => !(current ?? false),
        _ => null
    };

    if (target == null)
    {
        Console.WriteLine("Отмена.");
        return;
    }

    Console.WriteLine($"Установка WinKey в: {(target.Value ? "Заблокировано" : "Разблокировано")}...");
    bool ok = client.SetWinKeyLock(target.Value);
    if (ok)
    {
        Thread.Sleep(300);
        bool? verify = client.GetWinKeyLock();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Готово! Состояние после команды: {FormatBool(verify)}");
        Console.ResetColor();
    }
}

static void TestFnKeyLock(GigabyteWmiClient client)
{
    bool? current = client.GetFnKeyLock();
    Console.WriteLine($"\n--- РЕЖИМ FN-КЛАВИШ (Fn Lock) ---");
    Console.WriteLine($"Текущее состояние: {FormatBool(current)}");
    Console.WriteLine(" 1. Заблокировать Fn (Fn Lock ON - F1-F12 работают как стандартные функциональные)");
    Console.WriteLine(" 2. Разблокировать Fn (Fn Lock OFF - F1-F12 работают как мультимедиа/яркость)");
    Console.WriteLine(" 3. Переключить (Toggle)");
    Console.Write("Выбор [1-3]: ");
    var opt = Console.ReadLine()?.Trim();
    bool? target = opt switch
    {
        "1" => true,
        "2" => false,
        "3" => !(current ?? false),
        _ => null
    };

    if (target == null)
    {
        Console.WriteLine("Отмена.");
        return;
    }

    Console.WriteLine($"Установка Fn Lock в: {(target.Value ? "Включен (Lock ON)" : "Выключен (Lock OFF)")}...");
    bool ok = client.SetFnKeyLock(target.Value);
    if (ok)
    {
        Thread.Sleep(300);
        bool? verify = client.GetFnKeyLock();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Готово! Состояние после команды: {FormatBool(verify)}");
        Console.ResetColor();
    }
}

static void RunLiveMonitor(GigabyteWmiClient client)
{
    Console.Clear();
    Console.WriteLine("=== ЖИВОЙ МОНИТОРИНГ ЖЕЛЕЗА (Нажмите любую клавишу для выхода) ===\n");
    while (!Console.KeyAvailable)
    {
        try
        {
            int cpuTemp = client.GetCpuTemperature();
            int fan1 = client.GetFanRpm(1);
            int fan2 = client.GetFanRpm(2);
            int battLimit = client.GetBatteryChargeLimit();

            Console.SetCursorPosition(0, 2);
            Console.WriteLine($"[ {DateTime.Now:HH:mm:ss} ] CPU: {cpuTemp,2} °C | Fan 1: {fan1,4} RPM | Fan 2: {fan2,4} RPM | Battery Cap: {battLimit,3}%   ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ошибка монитора]: {ex.Message}");
            break;
        }

        Thread.Sleep(1000);
    }
    Console.ReadKey(true);
    Console.Clear();
}

static void RunWmiDiagnostics(GigabyteWmiClient client)
{
    Console.WriteLine("\n=== ДИАГНОСТИКА WMI (Пространство root\\WMI) ===");
    var diag = client.RunDiagnostics();
    foreach (var (method, val) in diag)
    {
        if (val != null && !val.ToString()!.StartsWith("Error"))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [OK] {method,-25} => {val}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [--] {method,-25} => {val ?? "null"}");
        }
    }
    Console.ResetColor();
}

static void RunHidDiagnostics(GigabyteWmiClient client)
{
    Console.WriteLine("\n=== ДИАГНОСТИКА USB HID КЛАВИАТУРЫ ===");
    bool connected = client.Keyboard.IsConnected;
    Console.WriteLine($"Статус подключения USB HID: {(connected ? "Подключено" : "Не подключено")}");
    if (!connected)
    {
        connected = client.Keyboard.Initialize();
        Console.WriteLine($"Повторная инициализация: {(connected ? "Успешно" : "Не найдено устройств")}");
    }

    if (connected)
    {
        var fnLock = client.Keyboard.GetFnLock();
        var winLock = client.Keyboard.GetWinLock();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  - HID FnLock:  {FormatBool(fnLock)}");
        Console.WriteLine($"  - HID WinLock: {FormatBool(winLock)}");
        Console.ResetColor();
    }
}

static async Task TestIpcServiceAsync(AEROCC.Core.Ipc.AeroccIpcClient ipc)
{
    Console.WriteLine("\n=== ТЕСТ IPC КАНАЛА (AEROCC.Service) ===");
    Console.WriteLine("Отправка запроса GetStatus через Named Pipe...");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var resp = await ipc.GetStatusAsync();
    sw.Stop();

    if (resp.Success && resp.Status != null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Ответ получен за {sw.ElapsedMilliseconds} мс!");
        Console.ResetColor();
        Console.WriteLine($"  - CPU Temp:     {resp.Status.CpuTemperature} °C");
        Console.WriteLine($"  - Fan 1 (CPU):  {resp.Status.Fan1Rpm} RPM");
        Console.WriteLine($"  - Fan 2 (GPU):  {resp.Status.Fan2Rpm} RPM");
        Console.WriteLine($"  - Battery Limit:{resp.Status.BatteryChargeLimit}%");
        Console.WriteLine($"  - WinKey Lock:  {FormatBool(resp.Status.WinKeyLocked)}");
        Console.WriteLine($"  - FnKey Lock:   {FormatBool(resp.Status.FnKeyLocked)}");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[x] Ошибка IPC: {resp.ErrorMessage ?? "Нет данных"}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("    Подсказка: запустите службу командой: AEROCC.Service.exe --install (или просто AEROCC.Service.exe в отдельном окне)");
        Console.ResetColor();
    }
}
