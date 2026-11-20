# AEROCC — Control Center for Gigabyte AERO 16 (1VH)

[![Build & Release Installer](https://github.com/moongrammic/AEROCC/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/moongrammic/AEROCC/actions/workflows/build-and-release.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**AEROCC** — легковесный, быстрый и надёжный центр управления (Control Center) для ноутбука **Gigabyte AERO 16 (1VH)** (платформа AMD Ryzen AI 300 / Strix Point + NVIDIA GeForce RTX Laptop).

Разработан как полноценная альтернатива стандартному Gigabyte Control Center (GCC), работающая без фоновой нагрузки на процессор, без задержек клавиатуры и со 100% контролем низкоуровневых регистров Embedded Controller (ITE EC) и ACPI WMI.

---

## ⚡ Ключевые возможности

* **Управление вентиляторами и охлаждением:**
  * 4 подтверждённых профиля EC: **Eco** (тихий), **Normal** (сбалансированный), **Game** (повышенный обдув), **Turbo** (100% макс. обороты).
  * Индивидуальная **динамическая кривая вентиляторов** (Fan Curve Studio) с гистерезисом по температуре и визуализацией реальных RPM.
  * Фиксированные обороты (Manual Fixed Speed: 25%, 50%, 75%, 100%).
  * Автоматическое восстановление активного профиля и кривой при выходе ноутбука из сна (`PowerModes.Resume`).

* **Управление питанием процессора (AMD APU Power Limits):**
  * Тонкая настройка лимитов мощности: **fPPT** (Fast PPT), **sPPT** (Slow PPT) и **SPL** (Sustained Power Limit).
  * Автоматическая ре-персистентность: лимиты APU больше не сбрасываются при смене профилей вентиляторов (обход аппаратного сброса регистров DSDT `0xED`).

* **Защита аккумулятора (Battery Charge Threshold):**
  * Пороги заряда батареи: **60%**, **80%** или **100%**.
  * Прямая запись в контроллер питания с обязательной верификацией обратным чтением.

* **Управление дисплеем (OLED Display Manager):**
  * Быстрое переключение частоты обновления: **60 Гц / 165 Гц** в один клик.
  * Опциональное автопереключение частоты при смене источника питания (Сеть / Аккумулятор).

* **Подсветка клавиатуры (RGB Backlight Studio):**
  * Поддержка аппаратных режимов: статический цвет, бегущая радужная волна (**Wave**), циклический перелив (**Neon**), случайные цвета (**Random**), дыхание.
  * Полноценная палитра выбора произвольного HEX / RGB-цвета и регулировка яркости (0–100%).

* **Управление видеокартами (GPU / PowerGear):**
  * **Dynamic (Optimus):** дискретная видеокарта NVIDIA активна только при запуске 3D-приложений.
  * **Eco:** полный перевод dGPU в режим глубокого энергосбережения **D3cold / RTD3** для максимальной автономности от батареи.
  * **Performance:** постоянная активность видеокарты без задержек пробуждения.

* **Аппаратные горячие клавиши и всплывающий OSD (On-Screen Display):**
  * Выделенная аппаратная кнопка **GiMATE** (вызов панели управления).
  * **Fn + F4**: Мгновенное отключение/включение микрофона (Mic Mute) с индикацией OSD.
  * **Fn + F7**: Циклическое переключение профилей охлаждения (Eco → Normal → Game → Turbo).
  * **Fn + F9**: Аппаратная блокировка тачпада.
  * **Fn + Esc**: Блокировка клавиши Fn (FnLock).
  * **Fn + Win**: Блокировка клавиши Win (WinLock для предотвращения случайных нажатий в играх).
  * Нулевая задержка ввода (0 ms latency): перехват хоткеев реализован на выделенном фоновом потоке через Win32 Raw Input (HID).

---

## 🏗 Архитектура проекта

Решение разделено на 4 независимых компонента в [AEROCC.slnx](file:///c:/Users/moongrammic/Source/Antigravity/AEROCC/AEROCC.slnx):

```
├── AEROCC.Core       # Низкоуровневая библиотека ядра:
│                     # WMI ACPI, USB HID, Raw Input, NVML, EMI Power Telemetry, IPC client/server.
├── AEROCC.App        # Главное WPF GUI-приложение:
│                     # Трей, Fluent UI, боковые окна (Fan Curve, Backlight Studio, Thermal Monitor), OSD.
├── AEROCC.Service    # Windows Service (Фоновая служба):
│                     # Работает под LocalSystem в Session 0, слушает Named Pipe IPC (\\.\pipe\AEROCC_IPC).
└── AEROCC.Cli        # Консольная утилита:
                      # Интерактивное меню 0–13, диагностика дампов WMI/EC, аппаратный монитор, сниффер хоткеев.
```

---

## 🚀 Установка

### Вариант 1. Инсталлятор (Рекомендуется)
1. Перейдите в раздел [Releases](https://github.com/moongrammic/AEROCC/releases).
2. Скачайте последний установщик `AEROCC_Setup_vX.X.X.exe`.
3. Запустите установку (требуются права Администратора для записи в `Program Files` и регистрации службы).
4. Программа установится, зарегистрирует и запустит службу `AEROCCService` и добавит ярлык в меню «Пуск» и трей.

### Вариант 2. Портативная версия (Portable)
1. Скачайте архив `AEROCC_vX.X.X_Portable_win-x64.zip`.
2. Распакуйте в любую удобную папку.
3. Запустите `AEROCC.App.exe` от имени Администратора.

---

## ⚙️ Автозапуск

Для автозапуска в трей:
* В запущенном приложении нажмите правой кнопкой мыши по иконке AEROCC в трее возле часов.
* Включите галочку **«Автозапуск при старте Windows»**.
* Приложение создаст задачу Планировщика Windows (`schtasks`) с наивысшими правами (`HighestAvailable`), разрешением работы от батареи и автоматическим сворачиванием в трей (`--minimized`).

---

## 💻 Сборка из исходников

Требования: **.NET 8.0 SDK** или выше, ОС **Windows 11 (x64)**.

```bash
# Клонирование репозитория
git clone https://github.com/moongrammic/AEROCC.git
cd AEROCC

# Сборка Release
dotnet build AEROCC.slnx -c Release

# Публикация всех компонентов
dotnet publish src/AEROCC.App/AEROCC.App.csproj -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/AEROCC.Service/AEROCC.Service.csproj -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/AEROCC.Cli/AEROCC.Cli.csproj -c Release -r win-x64 --self-contained false -o publish
```

---

## 📄 Лицензия

Проект распространяется под лицензией [MIT](LICENSE).
Разработано для сообщества владельцев ноутбуков Gigabyte AERO.
