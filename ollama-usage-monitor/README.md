# Ollama Usage Monitor

Compact VS Code Status Bar indicator for Ollama Cloud session/weekly usage.

## Usage

Shows in the bottom status bar:

```
🦙 35,4% 1ч 20м
🦙 95% 25м
```

- Проценты — БЕЗ округления, с запятой (`35,4`).
- Countdown — точное время до ресета относительно текущей системной даты,
  формат `1ч 20м` / `2ч 5м` / `59м` / `2д 5ч`.
- `🦙 —` — no usage data. `🦙 ?` — error (details in tooltip).

Click the item to open `https://ollama.com/settings`.

## Data source

Headless Edge + выделенный профиль `%LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile`
→ HTML `https://ollama.com/settings` → парсинг (как `ollama_usage.ps1`).
Точное время ресета берётся из блока `local-time` (`data-time="...Z"`),
fallback — «Resets in N hours».

## Account switching (logout)

Command **Ollama Usage: Switch Account (Logout/Login)** — открывает обычный
(видимый) Edge с тем же выделенным профилем на странице настроек. Разлогиньтесь,
войдите другим аккаунтом, затем **Ollama Usage: Refresh** — headless-запросы
используют тот же профиль (общие cookie).

## MemoryAI integration

Если в текущем открытом проекте есть `MemoryAI/ollama_parsed_usage.txt`,
расширение после каждого опроса записывает в него:
- строка 1 — процент (с запятой, напр. `35,4`);
- строка 2 — время до ресета (`1ч 20м`).

При старте (один раз) в лог пишется: виджет запущен, лимиты пользователя
`<account>` получены, файл найден/не найден и заполнен.

## Colors (по проценту)

| Диапазон | Цвет текста | Прочее |
|---|---|---|
| 0–1% | белый | жирный, зелёный фон (#014700, эмуляция через warningBackground — API допускает только error/warning фон) |
| 1–70% | lime `#00ff00` | обычный |
| 70–90% | cyan `#00ffff` | обычный |
| ≥90% | orange `#ffa500` | жирный |
| ≥95% | red `#ff0000` | жирный |

Жирность — Unicode Mathematical Bold символы (настоящего font-weight у
StatusBarItem API нет).

## Commands

- `ollamaUsage.refresh` — refresh usage now.
- `ollamaUsage.openSettings` — open https://ollama.com/settings.
- `ollamaUsage.logout` — смена аккаунта (logout/login через видимый Edge).
- `ollamaUsage.reset` — **очистка и перезагрузка внутренних механизмов** (см. ниже).

## Почему бывает «пустой DOM» и что с этим делать (v0.5)

**Корень:** Edge НЕ допускает второй экземпляр на одном `--user-data-dir`.
Второй запуск «делегирует» запрос уже живому процессу и завершается с кодом **21
и пустым stdout**. Проверено экспериментально:

| Ситуация | Результат |
|---|---|
| одиночный запуск, профиль свободен | exit 0, DOM получен |
| запуск при живом держателе профиля | exit 21, вывод **пустой** |
| два запуска одновременно | один exit 0, второй exit 21 (пусто) |

Отсюда «иногда работает, иногда нет»: у расширения несколько источников
параллельных запусков — активация, таймер (каждые 5 с), изменение конфигурации и
**другие окна VS Code** (у каждого свой extension host, поэтому обычный JS-флаг
`inFlight` их не разводит, а общий профиль Edge — один).

**Что сделано:**
1. **Межпроцессный замок** на профиль (`lock.ts`) — запросы сериализуются даже
   между разными окнами VS Code; «протухший» замок (умерший процесс) снимается
   автоматически, поэтому сбой не залипает навсегда.
2. **Повторные попытки** (до 3) — `exit 21` и пустой DOM транзиентны
   (остаточный Edge, медленный старт).
3. **Ожидание данных** — после закрытия Edge файл перечитывается с короткими
   паузами: `--virtual-time-budget` не гарантирует, что серверный HTML уже записан.
4. **Команда `Ollama Usage: Reset`** — гасит только процессы Edge с профилем
   расширения (обычный ваш Edge не тронет), снимает замки, чистит temp-файлы и
   сразу повторяет запрос.

**Когда нажимать Reset:**
- статус `🔒` (профиль занят) — часто остаётся «висячий» headless Edge;
- статус `?` с ошибкой `EMPTY_DOM` / `PROFILE_BUSY` / `Timeout`;
- после обрыва VPN, падения Edge или закрытия окна смены аккаунта.

Отличия состояний в статус-баре:
`🦙 —` нет данных · `🔑` нужен вход · `🔒` профиль занят · `⏳` идёт сброс ·
`?` ошибка (в tooltip — совет про Reset).

> Сценарий «VPN выключен»: страница может не загрузиться, и тогда Edge тоже даёт
> пустой DOM / ненулевой код. После включения VPN нажмите **Reset** — он очистит
> состояние и повторит запрос.

## Settings

```json
{
  "ollamaUsage.enabled": true,
  "ollamaUsage.refreshInterval": 5,
  "ollamaUsage.showEmoji": true
}
```

`refreshInterval` — секунды, минимум **5** (по умолчанию 5).

## Build & test

```
npm install
npm run compile
npm test
```

Package as `.vsix` with `vsce package`.
