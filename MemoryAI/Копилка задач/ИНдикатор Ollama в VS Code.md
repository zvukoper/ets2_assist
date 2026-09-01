Создать VS Code extension `ollama-usage-monitor`.

Цель:
Добавить в VS Code компактный `StatusBarItem`, отображающий текущий Ollama Cloud session usage в формате:
`🦙 35% 3h`
`🦙 95% 25m`

Источник данных:
`GET https://ollama.com/api/usage`

Авторизация:
`Authorization: Bearer <OLLAMA_API_KEY>`

Не парсить HTML `https://ollama.com/settings`.

Функциональность:

1. Extension activation:

* создать `StatusBarItem`;
* alignment: `StatusBarAlignment.Right`;
* priority: высокая;
* command на click: открыть `https://ollama.com/settings`.

2. Получение данных:

* выполнить HTTP GET `/api/usage`;
* timeout;
* обработать HTTP/network/JSON errors;
* не падать при отсутствии API key;
* обновлять данные каждые 60 секунд;
* выполнить первый запрос сразу после activation.

3. Credentials:

* основной источник: environment variable `OLLAMA_API_KEY`;
* добавить fallback через VS Code `SecretStorage`;
* не хранить API key в `settings.json`;
* не логировать API key;
* предусмотреть command для сохранения/обновления API key через `window.showInputBox({ password: true })`.

4. Нормализовать API response во внутреннюю модель:

```ts
interface UsageInfo {
    sessionPercent: number | null;
    sessionResetAt: Date | null;
    weeklyPercent: number | null;
    weeklyResetAt: Date | null;
}
```

Не привязывать UI к сырому JSON API.

5. Определить фактическую структуру response `/api/usage` по реальному API:

* проверить имена полей;
* проверить единицы процентов;
* проверить формат timestamp;
* корректно обработать `null`/отсутствующие поля;
* не выдумывать структуру response;
* при необходимости добавить отдельный parser/adapter.

6. Countdown:

* вычислять `resetAt - Date.now()`;
* отображать ближайший relevant session reset;
* формат:

  * `0–59s` → `Xs`;
  * `1–59m` → `Xm`;
  * `1–23h` → `Xh`;
  * `>=24h` → `Xd Xh`;
* не показывать секунды для нормального состояния;
* пересчитывать countdown локально каждую минуту без обязательного HTTP-запроса.

7. Status bar:

* основной формат:
  `🦙 {sessionPercent}% {resetCountdown}`;
* при отсутствии usage:
  `🦙 —`;
* при API/auth error:
  `🦙 ?`;
* tooltip:

```text
Ollama Cloud Usage

Session: {percent}%
Reset: {countdown}

Weekly: {percent}%
Reset: {countdown}
```

8. UX:

* StatusBarItem должен оставаться маленьким;
* не показывать webview;
* не открывать дополнительное окно;
* не создавать notification при штатном обновлении;
* ошибки показывать только tooltip/status через item;
* исключить UI flicker при обновлении.

9. Цветовая индикация:
   Использовать `ThemeColor`, а не hardcoded RGB.

Диапазоны:

* `<50%` — normal/default;
* `50–79%` — warning;
* `>=80%` — error;
* API error — error/warning.

Не менять глобальный цвет Status Bar.

10. Commands:

* `ollamaUsage.refresh`
* `ollamaUsage.setApiKey`
* `ollamaUsage.openSettings`

11. Configuration:
    Добавить optional settings:

```json
{
  "ollamaUsage.enabled": true,
  "ollamaUsage.refreshInterval": 60,
  "ollamaUsage.showEmoji": true
}
```

Не разрешать refresh interval меньше 15 секунд.

12. Security:

* API key хранить через `SecretStorage`;
* environment variable использовать как read-only fallback;
* не писать ключ в output channel;
* не включать ключ в exceptions/error messages;
* не отправлять credentials куда-либо кроме Ollama API.

13. Lifecycle:

* timer очищать в `deactivate`;
* `Disposable` всех resources регистрировать через `context.subscriptions`;
* исключить параллельные запросы при зависшем предыдущем request;
* при overlap не запускать второй request.

14. HTTP:
    Использовать встроенный Node/VS Code-compatible HTTP API без лишней зависимости, если это возможно.
    Не использовать browser DOM/fetch, требующий webview.
    Сделать отдельный `OllamaUsageClient`.

Структура:

```text
src/
  extension.ts
  ollamaUsageClient.ts
  usageParser.ts
  usageFormatter.ts
  statusBar.ts
  types.ts
```

15. Типы:
    строго типизировать response после parser stage;
    не использовать `any`;
    для неизвестных JSON полей использовать `unknown` + runtime validation.

16. Тесты:
    Добавить unit tests минимум для:

* parser valid response;
* parser missing fields;
* parser malformed response;
* percent normalization;
* countdown formatting;
* `0`, `59s`, `1m`, `59m`, `1h`, `23h`, `24h`, multi-day;
* API error;
* отсутствие API key.

17. README:
    Минимально описать:

* назначение extension;
* `/api/usage`;
* `OLLAMA_API_KEY`;
* SecretStorage;
* commands;
* settings;
* пример отображения.

18. Acceptance criteria:

* VS Code запускает extension без errors;
* в нижней Status Bar появляется item;
* при валидном API key отображается реальный session usage;
* countdown соответствует reset timestamp;
* tooltip показывает session + weekly;
* после reset данные автоматически обновляются;
* network/API failure не ломает extension;
* API key отсутствует → корректный fallback/error state;
* extension не парсит `ollama.com/settings`;
* extension не требует WebView;
* нет утечек API key в logs;
* `npm test`/аналогичный test command проходит;
* package можно установить как `.vsix`.

Порядок реализации:

1. Исследовать реальный `/api/usage` response.
2. Реализовать types/parser.
3. Реализовать HTTP client.
4. Реализовать formatter/countdown.
5. Реализовать status bar.
6. Реализовать SecretStorage/API-key command.
7. Реализовать polling/lifecycle.
8. Реализовать configuration/commands.
9. Добавить tests.
10. Собрать `.vsix`.
11. Проверить установку в чистый VS Code profile.
12. Исправить runtime/API compatibility issues.
