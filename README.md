# <img src="Assets/SidebarLogoNav.png" width="40" height="40" valign="middle" /> Zapret Mirrly GUI

[![Release](https://img.shields.io/github/v/release/joycecurcirt539-dot/zapret-mirrly-gui?style=for-the-badge&logo=github&color=6e40c9)](https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases)
[![Downloads](https://img.shields.io/github/downloads/joycecurcirt539-dot/zapret-mirrly-gui/total?style=for-the-badge&logo=github&color=6e40c9)](https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/download)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows)](https://www.microsoft.com/windows)
[![UI Framework](https://img.shields.io/badge/UI-WinUI%203-005A9E?style=for-the-badge&logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

**Zapret Mirrly GUI** — современная графическая оболочка для [`zapret-discord-youtube`](https://github.com/Flowseal/zapret-discord-youtube) от **Flowseal**. Обход блокировок YouTube и Discord **без VPN и без серверов** — прямо с вашего ПК, одним кликом.

<div align="center">

[![Скачать последнюю версию](https://img.shields.io/badge/⬇%EF%B8%8F_%D0%A1качать_v1.0.1-ZapretMirrlyGUI.exe-6e40c9?style=for-the-badge&logo=github)](https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases/latest)

</div>

> [!NOTE]
> 🔔 Репозиторий **Flowseal/zapret-discord-youtube** заморожен на GitHub с 10 июля 2026. **Zapret Mirrly GUI** использует те же батники Flowseal и является рабочей альтернативой с графическим интерфейсом. Скачивай только из официального репозитория, не из зеркал.

> [!WARNING]
> Для корректной работы (запуск winws, установка службы, использование WinDivert) **требуются права администратора**. Приложение автоматически запросит повышение прав UAC.

---

## 📖 Содержание
1. [Скриншоты](#-скриншоты)
2. [Основные возможности](#-основные-возможности)
3. [Сравнение с аналогами](#-сравнение-с-аналогами)
4. [Системные требования](#-системные-требования)
5. [Установка и запуск](#-установка-и-запуск)
6. [Как это работает](#-как-это-работает)
7. [Частые вопросы (FAQ)](#-частые-вопросы-faq)
8. [Создатели и Благодарности](#-создатели-и-благодарности)
9. [Лицензия](#-лицензия)

---

## 📸 Скриншоты

<p align="center">
  <img src="screenshot/hub.png" width="48%" alt="Главный экран" />
  <img src="screenshot/diagnostics.png" width="48%" alt="Диагностика" />
</p>
<p align="center">
  <img src="screenshot/logs.png" width="48%" alt="Журнал логов" />
  <img src="screenshot/lists.png" width="48%" alt="Редактор списков" />
</p>
<p align="center">
  <img src="screenshot/safety.png" width="48%" alt="Настройки безопасности" />
  <img src="screenshot/tray.png" width="48%" alt="Трей" />
</p>

---

## ✨ Основные возможности

* 🖥️ **Премиальный современный UI:** Интерфейс в стиле Fluent Design с тёмной/светлой темой, эффектом Mica/Acrylic и плавными анимациями.
* ⚙️ **Управление Windows-службой:** Установка, удаление, запуск и остановка службы `winws` в один клик. Работает в фоне после закрытия окна.
* ⚡ **Менеджер пресетов:** Стратегии обхода DPI (FAKE TLS, SIMPLE FAKE, ALT) для YouTube и Discord. Поддержка собственных аргументов. Обход Telegram не гарантируется.
* 🔍 **Встроенная диагностика сети:** Тестирование Discord, YouTube, DNS и Ping прямо из приложения.
* 📝 **Живой лог `winws.exe`:** Вывод в реальном времени для диагностики проблем.
* 📋 **Редактор списков доменов:** Удобное управление blacklist/whitelist файлами конфигурации `zapret`.
* 📦 **Один файл, без установки:** `ZapretMirrlyGUI.exe` — полностью автономный, .NET Runtime не нужен.

---

## ⚖️ Сравнение с аналогами

| Функция | **Zapret Mirrly GUI** | Ручные `.bat` скрипты | GoodbyeDPI GUI |
|---|:---:|:---:|:---:|
| Графический интерфейс | ✅ WinUI 3 | ❌ | ✅ (устаревший) |
| Windows-служба (автозапуск) | ✅ | ⚠️ Вручную | ❌ |
| Выбор пресетов | ✅ | ⚠️ Редактирование файлов | ⚠️ Ограниченно |
| Встроенная диагностика | ✅ | ❌ | ❌ |
| Живой лог | ✅ | ❌ | ❌ |
| Один EXE без установки | ✅ | ⚠️ Архив с файлами | ✅ |
| Открытый исходный код | ✅ MIT | ✅ | ✅ |
| Требует VPN/сервер | ❌ | ❌ | ❌ |

---

## 🖥️ Системные требования

* **ОС:** Windows 10 (сборка 1809+) или Windows 11 (x64)
* **Права:** Администратор (для WinDivert)
* **Сеть:** Активное подключение. Если используешь GoodbyeDPI — останови его перед запуском.

---

## 🚀 Установка и запуск

1. Перейди в раздел **[Releases](https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases)**
2. Скачай `ZapretMirrlyGUI.exe`
3. Запусти **от имени Администратора**
4. Выбери пресет (например `general (FAKE TLS AUTO).bat`)
5. Нажми **«Запустить»** или **«Установить службу»** для автозапуска при старте Windows
6. Пользуйся свободным интернетом! 🎉

---

## 🛠️ Как это работает

При первом запуске приложение распаковывает ресурсы (`winws.exe`, драйвер `WinDivert`, конфигурационные скрипты) в `%LOCALAPPDATA%\ZapretMirrlyGUI`.

При запуске пресета `winws` перехватывает сетевые пакеты через `WinDivert` и модифицирует заголовки (разбивает TCP-сегменты, меняет регистр host-заголовков, отправляет фейковые TLS-запросы), обходя алгоритмы DPI провайдеров.

---

## ❓ Частые вопросы (FAQ)

<details>
<summary><strong>YouTube / Discord всё равно не работает. Что делать?</strong></summary>

1. Убедись что приложение запущено **от имени Администратора**
2. Попробуй другой пресет — разные провайдеры требуют разные стратегии
3. Открой вкладку **Диагностика** — она покажет где именно блокировка
4. Проверь вкладку **Логи** — нет ли ошибок в выводе `winws`
5. Убедись что GoodbyeDPI или другие DPI-инструменты **остановлены**

</details>

<details>
<summary><strong>Зачем нужны права администратора?</strong></summary>

Утилита `winws` использует драйвер `WinDivert` для перехвата сетевых пакетов на уровне ядра Windows. Это требует повышенных привилегий — без них драйвер просто не запустится.

</details>

<details>
<summary><strong>Это безопасно? Куда уходят мои данные?</strong></summary>

Данные никуда не уходят. Приложение работает **полностью локально** — нет серверов, нет телеметрии, нет VPN-туннелей. `winws` только модифицирует заголовки пакетов на твоём ПК. Исходный код открыт — можешь проверить сам.

</details>

<details>
<summary><strong>Чем это отличается от VPN?</strong></summary>

VPN перенаправляет **весь** трафик через чужой сервер, замедляя соединение. `winws` работает локально и только изменяет способ отправки пакетов — скорость не страдает, сервер не нужен, анонимность не обеспечивается (это не VPN).

</details>

<details>
<summary><strong>Работает ли Telegram?</strong></summary>

Zapret Mirrly GUI оптимизирован для YouTube и Discord. Обход Telegram **не гарантируется**. Для Telegram используй [tg-ws-proxy](https://github.com/Flowseal/tg-ws-proxy) от Flowseal.

</details>

<details>
<summary><strong>Как запустить как Windows-службу (автозапуск)?</strong></summary>

На панели управления нажми **«Установить службу»**. Служба зарегистрируется в Windows и будет запускаться автоматически при старте системы — даже без открытия приложения.

</details>

<details>
<summary><strong>Почему exe весит ~300 МБ?</strong></summary>

Приложение скомпилировано в **self-contained** режиме — внутри уже весь .NET Runtime, `winws.exe`, драйвер `WinDivert` и все конфигурации. Устанавливать ничего дополнительно не нужно.

</details>

---

## 🤝 Создатели и Благодарности

* **joycecurcirt539-dot** — разработчик графической оболочки Zapret Mirrly GUI и автор экосистемы Mirrly.

* 🏆 **[Flowseal](https://github.com/Flowseal)** — легенда. Именно он сделал обход блокировок доступным каждому с его [`zapret-discord-youtube`](https://github.com/Flowseal/zapret-discord-youtube) — готовое решение «скачал и работает». Также автор [`tg-ws-proxy`](https://github.com/Flowseal/tg-ws-proxy). Zapret Mirrly GUI построен на его сборке.

* 🏆 **[bolvan](https://github.com/bolvan)** — легенда. Создатель оригинального движка [`zapret`](https://github.com/bolvan/zapret) и `winws` — низкоуровневого инструмента перехвата и модификации пакетов через WinDivert. Без bolvan не существовало бы ни одного DPI-обходчика в экосистеме.

* **Разработчики WinDivert** — за надёжный драйвер перехвата сетевых пакетов в Windows.

---

## 📄 Лицензия

Этот проект распространяется под свободной лицензией **MIT**. Подробности в файле [LICENSE](LICENSE).

---

## 🔍 Ключевые слова для поиска

`zapret gui`, `zapret для windows`, `обход блокировки discord`, `обход блокировок youtube`, `winws gui windows`, `goodbyedpi альтернатива`, `zapret discord youtube gui`, `графический интерфейс для zapret`, `обход dpi windows 11`, `winws служба автозапуск`, `ускорение ютуба`, `discord не работает провайдер`, `youtube тормозит россия`, `antizapret`, `обход dpi без vpn`, `flowseal альтернатива`, `zapret-discord-youtube альтернатива`

---

<div align="center">

⭐ Если приложение помогло — поставь звёздочку! Это очень помогает проекту расти. ⭐

[![Star on GitHub](https://img.shields.io/github/stars/joycecurcirt539-dot/zapret-mirrly-gui?style=social)](https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/stargazers)

</div>
