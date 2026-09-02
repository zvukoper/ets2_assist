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
