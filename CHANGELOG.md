# История изменений (Changelog) — Zapret Mirrly GUI

Все важные изменения проекта **Zapret Mirrly GUI** документируются в этом файле.

---

## [v1.1.9] — 2026-08-30
### Интеграция Flowseal Zapret v1.10.2 & Обновление стека стратегий

* **Интеграция официального релиза Flowseal v1.10.2**:
  * Добавлена новейшая стратегия `general (ALT13).bat` с режимом `ip_id=zero` для Google и `hostfakesplit` для стабильной работы YouTube 4K и голосовых чатов Discord.
  * Добавлен экспериментальный профиль `general (EXP).bat` с поддержкой multisplit и новыми фейками.
  * Добавлены новые бинарные фейки в папку `bin/`: `ACTIVE_DISCORD_UDP.bin`, `ACTIVE_GAME_UDP.bin`, `tls_clienthello_sochi_park.bin`, `stun2.bin`, `quic_initial_4pda_to.bin`, `quic_initial_5ka_ru.bin`, `quic_initial_rutube_ru.bin`, `quic_initial_steamcommunity_com.bin`, `quic_initial_tencent_com.bin`.
  * Обновлены стратегии `SIMPLE FAKE`, `ALT11`, `ALT12` и `GameFilter TCP`.
  * Актуализированы списки доменов `list-general.txt`, `list-exclude.txt` и база подсетей `ipset-all.txt`.
* **Ядро GUI**:
  * В `PresetManager` добавлена корректная приоритизация и сортировка профилей `general (EXP)` и `general (ALT13)`.
  * В `AssetsExtractor` обновлена версия для автоматической распаковки актуального бандла zapret в локальный профиль `%LocalAppData%\ZapretMirrlyGUI\zapret`.

---

## [v1.1.8] — 2026-08-28
### Оптимизация Telegram WS Proxy, система пулов и экосистема Mirrly

* **64-битное SIMD-ускорение AES-CTR**:
  * Перевод алгоритма шифрования на блочную 64-битную обработку `ulong` для минимизации нагрузки на CPU при передаче тяжелых медиафайлов.
* **Исправление Handshake и Ping Keep-Alive**:
  * Устранен баг буферизации `StreamReader`, обеспечив 0% потерь первых кадров WebSocket.
  * Фоновая задача `pingTask` раз в 15 секунд отправляет WebSocket Ping для удержания сессии открытой при паузах.
* **Система пулов**:
  * **IP-Benchmark Pool**: непрерывный фоновый тест задержек (Ping/RTT) до 30+ IP-адресов Telegram и Cloudflare с авто-выбором быстрейшего узла.
  * **Smart Failover Pool**: мгновенный авто-переход на резервные каналы при блокировках.
  * **Domain Fronting Pool**: ротация SNI-доменов для защиты от DPI.
* **Экосистема Mirrly**:
  * Добавлена интерактивная карточка мобильного приложения Mirrly TG Proxy (Android) во вкладке «Поддержать».

---

## [v1.1.7] — 2026-08-15
### Аппаратная диагностика и оптимизация списков

* Реализован нативный асинхронный модуль диагностики сети `DiagnosticEngine`.
* Добавлена проверка доступности службы BFE (Base Filtering Engine) и авто-включение TCP Timestamps в реестре Windows.
* Встроен интерактивный редактор списков доменов (`list-general.txt`, `list-exclude.txt`, `ipset-all.txt`).

---

## [v1.1.5] — 2026-08-05
### Нативное управление службами Windows & Auto Hostlist

* Прямое управление системными службами (`winws`, `WinDivert`) через C# и Win32 Service API без вызова внешних bat-скриптов.
* Реализован режим динамического обучения `autohostlist` для точечного перехвата заблокированных сайтов.

---

## [v1.1.0] — 2026-07-28
### WinUI 3 Fluent Редизайн & Интерактивный системный трей

* Полный переход на WinUI 3 (Windows App SDK) с нативной поддержкой тем Mica, Acrylic и Apple Light Glass.
* Создан интерактивный системный трей: виджет быстрого статуса по левому клику и полнофункциональное контекстное меню по правому клику.

---

## [v1.0.0] — 2026-07-20
### Генезис проекта

* Первый публичный релиз графической оболочки Zapret Mirrly GUI.
* Базовый запуск `winws.exe`, управление драйвером `WinDivert`, интеграция пресетов обхода YouTube и Discord.
