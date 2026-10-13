# Отдыхалка (Break Reminder)

[![CI](https://github.com/mothersLover/break-reminder/actions/workflows/ci.yml/badge.svg)](https://github.com/mothersLover/break-reminder/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/mothersLover/break-reminder?label=%D0%B2%D0%B5%D1%80%D1%81%D0%B8%D1%8F)](https://github.com/mothersLover/break-reminder/releases/latest)
![Platforms](https://img.shields.io/badge/Windows%20%7C%20Linux%20%7C%20macOS-555)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)

Напоминалка о перерывах для **Windows, Linux и macOS**. Нажимаешь «Начать» в начале рабочего дня — программа считает
время работы и регулярно предлагает отдохнуть. Уведомление можно отметить «Сделал», отложить или пропустить.

**Последние изменения** — в [CHANGELOG.md](CHANGELOG.md). В 4.0.0 исправлено время встреч из календаря,
заведённых в другом часовом поясе.

## Скачать

Готовые сборки — на странице **[Releases](../../releases)**:

| Система | Файл | Как запустить |
|---|---|---|
| Windows 10/11 | `BreakReminder-win-x64.zip` | распаковать, запустить `BreakReminder.exe` |
| Linux x64 | `BreakReminder-linux-x64.tar.gz` | распаковать, запустить `./install.sh` (появится в меню приложений) |
| macOS, Apple M1–M4 | `BreakReminder-osx-arm64.zip` | распаковать, перенести `BreakReminder.app` в «Программы» |
| macOS, Intel | `BreakReminder-osx-x64.zip` | то же |

> **macOS:** приложение не подписано сертификатом Apple, поэтому при первом запуске система его заблокирует.
> Правый клик по приложению → «Открыть», или в терминале:
> `xattr -dr com.apple.quarantine /Applications/BreakReminder.app`

## Возможности

- **Рабочий день**: «Начать / Закончить», время работы, отсчёт до перерыва, пауза (сдвигает и время, и напоминания).
- **Уведомление**: в углу, по центру или на весь экран; мигание, звук (встроенные мелодии, системный, свой файл),
  повтор звука, пока не ответишь. Кнопки «Сделал / Отложить / Пропустить», Esc — отложить.
- **Отдых засчитывается сам**: сон компьютера, блокировка, отсутствие мыши/клавиатуры или пауза дольше перерыва
  сбрасывают отсчёт. Созвон без движения мышью отдыхом не считается.
- **Большие перерывы** как в Помодоро: каждый N-й перерыв — длинный.
- **Не беспокоить**: во время созвона (занят микрофон) или полноэкранной программы напоминание ждёт.
- **Автоматический рабочий день**: в заданное время (по умолчанию 10:00–22:00) день начинается сам после
  разблокировки экрана и сам заканчивается.
- **Календарь** (Google, Outlook, Яндекс, iCloud — по секретной iCal-ссылке): во время встречи напоминание ждёт;
  если перерыв выпадет на встречу — предлагается до неё; напоминание «через 5 мин встреча»; после длинной серии
  встреч подряд — большой перерыв. Встреча не считается отдыхом. Ссылка хранится только локально в `settings.ini`.
- **Статистика** по неделям, история в `history.csv`.
- **Тексты уведомлений** (заголовки, советы, кнопки) настраиваются.
- Трей, автозапуск с системой, горячая клавиша паузы.

### Что зависит от системы

| | Windows | macOS | Linux (GNOME) |
|---|---|---|---|
| Простой мыши/клавиатуры | ✓ | ✓ | ✓ (Mutter IdleMonitor или `xprintidle`) |
| Блокировка экрана | ✓ | ✓ | ✓ (ScreenSaver D-Bus / logind) |
| Созвон (занят микрофон) | ✓ (+ камера) | ✓ | ✓ (`pactl`, PipeWire/PulseAudio) |
| Полноэкранная программа | ✓ | — | только X11 |
| Горячая клавиша паузы | Ctrl+Alt+P | ⌃⌥P | своё сочетание в настройках системы → команда `BreakReminder --toggle-pause` |
| Мигание на панели задач | ✓ | — | — |
| Окно «в углу» | ✓ | ✓ | на Wayland система может поставить окно в другое место |

## Где лежат данные

- Windows: `%APPDATA%\BreakReminder\`
- macOS: `~/Library/Application Support/BreakReminder/`
- Linux: `~/.config/BreakReminder/`

Там `settings.ini`, `history.csv`, `tips.txt`, `tips_long.txt` (советы можно править в любом редакторе).
Переменная окружения `BREAKREMINDER_DATA_DIR` задаёт другую папку — удобно для разработки.

## Сборка из исходников

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet run --project src/BreakReminder.App          # запустить
dotnet test                                          # тесты
dotnet publish src/BreakReminder.App -c Release -r win-x64 --self-contained -o publish   # один исполняемый файл
```

RID для других систем: `linux-x64`, `osx-arm64`, `osx-x64`.

## Структура

```
src/BreakReminder.Core       логика без UI: рабочий день, перерывы, расписание, настройки, история (покрыта тестами)
src/BreakReminder.Platform   системные функции: Windows/ Linux/ MacOS/
src/BreakReminder.App        интерфейс на Avalonia
tests/                       тесты ядра (xUnit)
build/                       упаковка для macOS (.app) и Linux (tar.gz + install.sh)
.github/workflows/           CI и сборка релизов
legacy/                      первая версия — только Windows, WinForms (.NET Framework 4)
```

## Новый релиз

```bash
git tag v2.0.1
git push origin v2.0.1
```

GitHub Actions соберёт все четыре версии и опубликует их на странице Releases.
