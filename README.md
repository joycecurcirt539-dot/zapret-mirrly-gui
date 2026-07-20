<p align="center">
  <img src="Assets/SidebarLogoNav.png" width="120" height="120" alt="Zapret Mirrly GUI Logo" />
</p>

<h1 align="center">Zapret Mirrly GUI v1.1.5</h1>

<p align="center">
  <b>Современное высокопроизводительное графическое решение (WinUI 3 & .NET 10) для управления низкоуровневым обходчиком DPI (zapret/winws 1.9.9d+) и встроенным Telegram WebSocket-прокси (TgWsProxy).</b>
</p>

<p align="center">
  <a href="https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases"><img src="https://img.shields.io/github/v/release/joycecurcirt539-dot/zapret-mirrly-gui?style=for-the-badge&logo=github&color=6e40c9" alt="Release" /></a>
  <a href="https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases"><img src="https://img.shields.io/github/downloads/joycecurcirt539-dot/zapret-mirrly-gui/total?style=for-the-badge&logo=github&color=6e40c9" alt="Downloads" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-blue?style=for-the-badge&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge&logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/UI-WinUI%203-005A9E?style=for-the-badge&logo=windows" alt="UI Framework" />
</p>

<p align="center">
  <a href="https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases/latest">
    <img src="https://img.shields.io/badge/Скачать_версию_v1.1.5-ZapretMirrlyGUI.exe-6e40c9?style=for-the-badge&logo=github" alt="Скачать последнюю версию" />
  </a>
</p>

> [!CAUTION]
> **Правовое уведомление и независимость проекта (Disclaimer):**  
> Разработчик **Flowseal**, а также другие авторы сторонних компонентов и утилит, упомянутые в данном проекте, **НИКАК НЕ ПРИЧАСТНЫ к разработке, тестированию или поддержке Zapret Mirrly GUI**.  
> Мы лично не знакомы ни с ними, ни с другими авторами. Их наработки, оригинальные стратегии и ключевые разработки используются в проекте исключительно из глубокого уважения и доверия к их труду, стремясь со своей стороны создать такой же качественный, надежный и удобный Open Source продукт для сообщества.

> [!IMPORTANT]
> **Нативный Win32 API стек (GUI v1.1.5):** Начиная с версии v1.1.5, приложение полностью отказалось от устаревших вызовов внешней утилиты `curl.exe`, тяжеловесных скриптов PowerShell (`test zapret.ps1`) и консольных батников службы `sc.exe`/`netsh.exe`. Все системные проверки, опрос служб, диагностика TLS 1.2/1.3 и настройка TCP Timestamps выполняются нативно в C# за 0 миллисекунд.
> 
> Поддерживаются дистрибутивы Flowseal `zapret-discord-youtube` **начиная от версии 1.9.9d и новее**.

> [!NOTE]
> **Сетевая прозрачность:** Исходный код открыт. Основной трафик обхода DPI обрабатывается локально на вашем ПК через драйвер `WinDivert`. Для выполнения служебных функций (проверка версий, автообновление `ipset`, проверка `hosts` и актуализация пула Cloudflare-доменов для Telegram) приложение отправляет фоновые HTTP-запросы к GitHub API.

---

## Содержание
1. [О проекте](#о-проекте)
2. [Ключевые преимущества v1.1.5](#ключевые-преимущества-v115)
3. [Правовой статус и независимость](#правовой-статус-и-независимость)
4. [Нативная архитектура и структура файлов](#нативная-архитектура-и-структура-файлов)
5. [Основные возможности](#основные-возможности)
6. [Галерея интерфейса](#галерея-интерфейса)
7. [Системные изменения и воздействия](#системные-изменения-и-воздействия)
8. [Зависимости и благодарности](#зависимости-и-благодарности)
9. [Лицензия](#лицензия)

---

## О проекте

**Zapret Mirrly GUI** — это современная высокопроизводительная графическая оболочка на фреймворке **WinUI 3 (.NET 10)**, объединяющая в едином интерфейсе работу консольного обходчика сетевых блокировок `zapret` (утилита `winws.exe` автора bol-van, сборка Flowseal `zapret-discord-youtube 1.9.9d+`) и нативную C#-реализацию локального WebSocket-прокси для Telegram (`TgWsProxy`).

Проект избавляет пользователя от необходимости вручную редактировать `.bat` файлы, консольные параметры и службы Windows, предоставляя наглядную панель управления, светокодированный журнал логов, нативный интерактивный диагностический стенд и трей.

---

## Ключевые преимущества v1.1.5

### 1. ⚡ 100% Нативный C# & Win32 API Стек
* **Мгновенный отклик (0.1 мс):** Все проверки BFE, WinDivert, TCP Timestamps и управление службами выполняются через прямые вызовы P/Invoke `advapi32.dll` и реестр Windows (`HKLM\SYSTEM\Tcpip`).
* **Бесшумность (0% мигающих окон):** Забудьте о вылетающих черных консольных окнах `cmd.exe`, `powershell` или `sc.exe`.
* **Нативный сетевой движок (без `curl.exe`):** Проверка подлинности серверов, HTTP доступности и DPI блока (TLS 1.2 / TLS 1.3) выполняется напрямую через асинхронные сокеты .NET 10 (`HttpClient` и `SslStream`).

### 2. 🎨 Премиальный дизайн WinUI 11 (Fluent Glass Graphite)
* **Строгая нейтральная палитра:** Избавление от уродских коричневых и синих контрастов в пользу элегантного графита WinUI 11 (`#141416`, `#1E1E22`, `#FAFAFA`).
* **Composition API & DWM:** Прямое управление эффектами размытия Acrylic и Mica без нагрузки на видеокарту.
* **Защита скриншотов:** Автоматический сброс маскирования Win32 API для свободного использования PrintScreen, OBS и Discord.

### 3. ⚙️ Интеллектуальный менеджер пресетов (`PresetManager.cs`)
* Совместим с дистрибутивами Flowseal `zapret-discord-youtube 1.9.9d+`.
* Умно читает оригинальные `.bat` файлы Flowseal и накладывает личные настройки пользователя (IPv4/v6, AutoHostlist, Игровой фильтр) на лету без порчи исходных батников.

---

## Нативная архитектура и структура файлов

После глубокой оптимизации для работы приложения больше не требуются `curl.exe`, `test zapret.ps1` или скрипты `service.bat`. Минималистичная рабочая структура выглядит так:

```text
zapret/
├── bin/
│   ├── winws.exe                   # Нативный движок обхода DPI
│   ├── WinDivert64.sys / .dll       # Драйверы сетевого перехвата WinDivert (x64)
│   ├── WinDivert32.sys / .dll       # Драйверы перехвата (x86)
│   ├── cygwin1.dll                  # Среда выполнения winws
│   └── *.bin                        # Шаблоны расщепления пакетов (quic_initial_*.bin, tls_clienthello_*.bin)
├── lists/
│   ├── list-general.txt             # Основной список заблокированных ресурсов
│   ├── list-general-user.txt        # Пользовательский список сайтов
│   ├── list-exclude.txt             # Список исключений
│   ├── autohostlist.txt             # Динамический список автообучения
│   └── ipset-all.txt                # База IP-адресов сайтов
├── utils/
│   └── targets.txt                  # Конфигурация доменов для диагностического стенда
└── *.bat                            # Файлы пресетов Flowseal (general (ALT11).bat и т.д.)
```

---

## Системные изменения и воздействия

1. **Нативная запись TCP Timestamps в реестре:**  
   При включении параметров или старте службы приложение напрямую прописывает параметр `Tcp1323Opts = 1` в ветке `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters`.
2. **Win32 Управление службами Windows:**  
   Установка и запуск службы `zapret` выполняется напрямую через контроллер `OpenSCManagerW` / `CreateServiceW` модуля `advapi32.dll`.

---

## Зависимости и благодарности

Проект выражает искреннюю благодарность авторам фундаментальных разработок:

* **[bol-van (Vasily Levichev)](https://github.com/bol-van)** — автор и разработчик оригинального низкоуровневого движка обхода **zapret** и консольной утилиты `winws.exe`.
* **[Flowseal](https://github.com/Flowseal)** — автор популярного дистрибутива **zapret-discord-youtube** и оригинального концепта **tg-ws-proxy**.
* **[basil00 (WinDivert)](https://github.com/basil00/Divert)** — автор драйвера ядра и библиотеки **WinDivert** (Windows Packet Divert).
* **Microsoft Corporation** — авторы фреймворка WinUI 3, .NET Runtime и набора компонентов Windows App SDK.

---

## Лицензия

Исходный код графической оболочки **Zapret Mirrly GUI** распространяется под свободной лицензией **MIT**. Полный текст доступен в файле [LICENSE](LICENSE).

### Сторонние компоненты:
* **zapret / winws.exe** — (c) bol-van, лицензии **MIT / GPL-3.0**.
* **WinDivert (WinDivert.dll / WinDivert64.sys)** — (c) basil00, лицензии **LGPL-3.0 / GPL-3.0**.
* **Cygwin DLL (cygwin1.dll)** — (c) Red Hat / Cygwin contributors, лицензия **GPLv3+**.
* **WinUI 3 & Windows App SDK** — (c) Microsoft Corporation, лицензия **MIT**.
