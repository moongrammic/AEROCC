using System.Text;
using AEROCC.Core.Display;
using AEROCC.Core.Hid;
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
    Console.Write("\nВыберите действие [0-11]: ");
    var key = Console.ReadLine()?.Trim();

    try
    {
        switch (key)
        {
            case "1":
                await DisplayCurrentStatusAsync(client, ipcClient);
                break;

            case "2":
                await TestBatteryLimitAsync(client, ipcClient);
                break;

            case "3":
                await TestFanProfileAsync(client, ipcClient);
                break;

            case "4":
                await TestWinKeyLockAsync(client, ipcClient);
                break;

            case "5":
                await TestFnKeyLockAsync(client, ipcClient);
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

            case "10":
                await TestDisplayRefreshRateAsync(ipcClient);
                break;

            case "11":
                await TestKeyboardBacklightAsync(client, ipcClient);
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
    Console.WriteLine(" 1. Считать текущее состояние (CPU Temp, Fan RPM, Батарея, Экран, Подсветка)");
    Console.WriteLine(" 2. Установить порог заряда батареи (60% / 80% / 100%)");
    Console.WriteLine(" 3. Переключить профиль вентиляторов (Eco / Normal / Game / Turbo)");
    Console.WriteLine(" 4. Управление блокировкой WinKey (Заблокировать / Разблокировать)");
    Console.WriteLine(" 5. Управление блокировкой FnKey (Заблокировать / Разблокировать)");
    Console.WriteLine(" 6. Запустить живой аппаратный монитор (обновление раз в секунду)");
    Console.WriteLine(" 7. Полная диагностика WMI (опрос всех доступных методов ядра)");
    Console.WriteLine(" 8. Диагностика USB HID клавиатуры");
    Console.WriteLine(" 9. Тест IPC канала к службе AEROCCService (Named Pipes)");
    Console.WriteLine("10. Частота обновления экрана (60 Гц / 165 Гц)");
    Console.WriteLine("11. Управление подсветкой клавиатуры (Яркость, Цвета, RGB, Эффекты)");
    Console.WriteLine(" 0. Выход");
}

static async Task DisplayCurrentStatusAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
{
    Console.WriteLine("\n[Опрос оборудования...]");
    HardwareStatus? status = null;
    var ipcResp = await ipc.GetStatusAsync();
    if (ipcResp.Success && ipcResp.Status != null)
    {
        status = ipcResp.Status;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  (Канал: IPC от фоновой службы AEROCCService)");
        Console.ResetColor();
    }
    else
    {
        status = client.GetHardwareStatus();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  (Канал: Прямой доступ к WMI/HID/Display)");
        Console.ResetColor();
    }

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  - Температура CPU:          {status.CpuTemperature} °C");
    Console.WriteLine($"  - Вентилятор 1 (CPU):       {status.Fan1Rpm} RPM");
    Console.WriteLine($"  - Вентилятор 2 (GPU):       {status.Fan2Rpm} RPM");
    Console.WriteLine($"  - Порог заряда аккумулятора:{status.BatteryChargeLimit}% (Policy: {status.BatteryPolicy})");
    Console.WriteLine($"  - WinKey заблокирована:     {FormatBool(status.WinKeyLocked)}");
    Console.WriteLine($"  - FnKey заблокирована:      {FormatBool(status.FnKeyLocked)}");

    if (status.DisplayRefreshRate > 0)
    {
        string supported = status.AvailableRefreshRates.Count > 0
            ? string.Join(", ", status.AvailableRefreshRates.Select(f => $"{f}Hz"))
            : "Н/Д";
        Console.WriteLine($"  - Экран (IPS):              {status.DisplayResolution} @ {status.DisplayRefreshRate} Гц [Доступно: {supported}]");
    }

    if (!string.IsNullOrEmpty(status.BacklightEffect))
    {
        Console.WriteLine($"  - Подсветка клавиатуры:     Яркость {status.BacklightBrightness}%, Цвет: {status.BacklightColor}, Режим: {status.BacklightEffect}");
    }

    Console.ResetColor();
}

static string FormatBool(bool? val) => val switch
{
    true => "Да (Заблокирована)",
    false => "Нет (Разблокирована)",
    null => "Н/Д"
};

static async Task TestBatteryLimitAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
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
    var ipcResp = await ipc.SetBatteryLimitAsync(target);
    if (ipcResp.Success)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Порог {target}% успешно установлен через фоновую службу!");
        Console.ResetColor();
    }
    else
    {
        bool ok = client.SetBatteryChargeLimit(target);
        if (ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[✓] Порог {target}% установлен напрямую через WMI!");
            Console.ResetColor();
        }
    }

    Thread.Sleep(500);
    int current = client.GetBatteryChargeLimit();
    Console.WriteLine($"Текущий считанный порог из WMI: {current}%");
}

static async Task TestFanProfileAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
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
    var ipcResp = await ipc.SetFanProfileAsync(profile.Value);
    if (ipcResp.Success)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Профиль {profile.Value} применен через фоновую службу!");
        Console.ResetColor();
    }
    else
    {
        client.SetFanProfile(profile.Value);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Профиль {profile.Value} применен напрямую через WMI!");
        Console.ResetColor();
    }
}

static async Task TestWinKeyLockAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
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
    var ipcResp = await ipc.SetWinLockAsync(target.Value);
    if (!ipcResp.Success)
    {
        client.SetWinKeyLock(target.Value);
    }

    Thread.Sleep(300);
    bool? verify = client.GetWinKeyLock();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[✓] Готово! Состояние после команды: {FormatBool(verify)}");
    Console.ResetColor();
}

static async Task TestFnKeyLockAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
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
    var ipcResp = await ipc.SetFnLockAsync(target.Value);
    if (!ipcResp.Success)
    {
        client.SetFnKeyLock(target.Value);
    }

    Thread.Sleep(300);
    bool? verify = client.GetFnKeyLock();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[✓] Готово! Состояние после команды: {FormatBool(verify)}");
    Console.ResetColor();
}

static async Task TestDisplayRefreshRateAsync(AEROCC.Core.Ipc.AeroccIpcClient ipc)
{
    Console.WriteLine("\n=== УПРАВЛЕНИЕ ЧАСТОТОЙ ОБНОВЛЕНИЯ ЭКРАНА ===");
    var primary = DisplayManager.GetPrimaryDisplay();
    if (primary == null)
    {
        Console.WriteLine("[x] Не удалось обнаружить основной дисплей.");
        return;
    }

    Console.WriteLine($"  Дисплей:            {primary.DeviceName} ({primary.DeviceString})");
    Console.WriteLine($"  Разрешение:         {primary.CurrentWidth}x{primary.CurrentHeight}");
    Console.WriteLine($"  Текущая частота:    {primary.CurrentFrequency} Гц");
    Console.WriteLine($"  Доступные частоты:  {string.Join(", ", primary.AvailableFrequencies)} Гц");

    Console.WriteLine("\nВыберите режим:");
    Console.WriteLine(" 1. 60 Гц (Энергосбережение / Автономность от батареи)");
    Console.WriteLine(" 2. 165 Гц (Максимальная плавность / Gaming)");
    Console.WriteLine(" 3. Ввести вручную");
    Console.Write("Выбор: ");
    var opt = Console.ReadLine()?.Trim();

    int targetHz = opt switch
    {
        "1" => 60,
        "2" => 165,
        "3" => PromptHz(),
        _ => 0
    };

    static int PromptHz()
    {
        Console.Write("Введите желаемую частоту (Гц): ");
        return int.TryParse(Console.ReadLine(), out int val) ? val : 0;
    }

    if (targetHz <= 0)
    {
        Console.WriteLine("Отмена.");
        return;
    }

    Console.WriteLine($"Переключение экрана на {targetHz} Гц...");

    // Try via IPC first, then fallback to direct DisplayManager
    var ipcResp = await ipc.SetRefreshRateAsync(targetHz);
    bool ok = ipcResp.Success || DisplayManager.SetRefreshRate(targetHz);

    if (ok)
    {
        Thread.Sleep(200);
        var updated = DisplayManager.GetPrimaryDisplay();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Частота экрана успешно изменена! Текущая: {updated?.CurrentFrequency} Гц");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[x] Не удалось переключить экран на {targetHz} Гц.");
        Console.ResetColor();
    }
}

static async Task TestKeyboardBacklightAsync(GigabyteWmiClient client, AEROCC.Core.Ipc.AeroccIpcClient ipc)
{
    Console.WriteLine("\n=== УПРАВЛЕНИЕ ПОДСВЕТКОЙ КЛАВИАТУРЫ ===");
    var status = client.Keyboard.GetBacklightStatus();
    if (status != null)
    {
        Console.WriteLine($"  Текущий режим:  {status.Effect}");
        Console.WriteLine($"  Яркость:        {status.Brightness}%");
        Console.WriteLine($"  Цвет:           {status.Color}");
        Console.WriteLine($"  Скорость:       {status.Speed}");
    }
    else
    {
        Console.WriteLine("  [-] Статус подсветки пока не считан.");
    }

    Console.WriteLine("\nВыберите действие:");
    Console.WriteLine(" 1. Выключить подсветку (0%)");
    Console.WriteLine(" 2. Яркость 25%");
    Console.WriteLine(" 3. Яркость 50%");
    Console.WriteLine(" 4. Яркость 75%");
    Console.WriteLine(" 5. Яркость 100%");
    Console.WriteLine(" 6. Статический цвет: Белый (White)");
    Console.WriteLine(" 7. Статический цвет: Синий (Blue)");
    Console.WriteLine(" 8. Статический цвет: Оранжевый (AERO Orange)");
    Console.WriteLine(" 9. Статический цвет: Красный (Red)");
    Console.WriteLine("10. Эффект: Дыхание (Breathing)");
    Console.WriteLine("11. Эффект: Радуга / Неон (Neon)");
    Console.WriteLine("12. Пользовательский цвет RGB (например, Бирюзовый / Фиолетовый)");
    Console.Write("Выбор: ");
    var opt = Console.ReadLine()?.Trim();

    switch (opt)
    {
        case "1":
            await SetBrightness(0);
            break;
        case "2":
            await SetBrightness(25);
            break;
        case "3":
            await SetBrightness(50);
            break;
        case "4":
            await SetBrightness(75);
            break;
        case "5":
            await SetBrightness(100);
            break;
        case "6":
            await SetColor(KeyboardColor.White);
            break;
        case "7":
            await SetColor(KeyboardColor.Blue);
            break;
        case "8":
            await SetColor(KeyboardColor.Orange);
            break;
        case "9":
            await SetColor(KeyboardColor.Red);
            break;
        case "10":
            await SetEffect(KeyboardEffect.Breathing);
            break;
        case "11":
            await SetEffect(KeyboardEffect.Neon);
            break;
        case "12":
            Console.Write("Введите Red (0-255): ");
            byte r = byte.TryParse(Console.ReadLine(), out var rv) ? rv : (byte)0;
            Console.Write("Введите Green (0-255): ");
            byte g = byte.TryParse(Console.ReadLine(), out var gv) ? gv : (byte)255;
            Console.Write("Введите Blue (0-255): ");
            byte b = byte.TryParse(Console.ReadLine(), out var bv) ? bv : (byte)255;
            Console.WriteLine($"Установка RGB: ({r}, {g}, {b})...");
            var rgbResp = await ipc.SetCustomRgbAsync(r, g, b);
            if (!rgbResp.Success) client.Keyboard.SetCustomColor(r, g, b);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[✓] Пользовательский цвет RGB применен!");
            Console.ResetColor();
            break;
        default:
            Console.WriteLine("Отмена.");
            break;
    }

    async Task SetBrightness(byte percent)
    {
        Console.WriteLine($"Установка яркости {percent}%...");
        var resp = await ipc.SetBacklightBrightnessAsync(percent);
        if (!resp.Success) client.Keyboard.SetBrightness(percent);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Яркость {percent}% установлена!");
        Console.ResetColor();
    }

    async Task SetColor(KeyboardColor color)
    {
        Console.WriteLine($"Установка цвета {color}...");
        var resp = await ipc.SetBacklightColorAsync(color);
        if (!resp.Success) client.Keyboard.SetBacklight(KeyboardEffect.Static, 100, color);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Цвет {color} применен!");
        Console.ResetColor();
    }

    async Task SetEffect(KeyboardEffect effect)
    {
        Console.WriteLine($"Активация эффекта {effect}...");
        var resp = await ipc.SetBacklightEffectAsync(effect);
        if (!resp.Success) client.Keyboard.SetBacklight(effect, 100);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[✓] Эффект {effect} включен!");
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
        var blStatus = client.Keyboard.GetBacklightStatus();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  - HID FnLock:   {FormatBool(fnLock)}");
        Console.WriteLine($"  - HID WinLock:  {FormatBool(winLock)}");
        if (blStatus != null)
        {
            Console.WriteLine($"  - HID Подсветка: {blStatus}");
        }
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
        if (resp.Status.DisplayRefreshRate > 0)
        {
            Console.WriteLine($"  - Display:      {resp.Status.DisplayResolution} @ {resp.Status.DisplayRefreshRate} Гц");
        }
        if (!string.IsNullOrEmpty(resp.Status.BacklightEffect))
        {
            Console.WriteLine($"  - Подсветка:    Яркость {resp.Status.BacklightBrightness}%, Режим: {resp.Status.BacklightEffect}");
        }
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[x] Ошибка IPC: {resp.ErrorMessage ?? "Нет данных"}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("    Подсказка: запустите службу в отдельном окне: AEROCC.Service.exe");
        Console.ResetColor();
    }
}
