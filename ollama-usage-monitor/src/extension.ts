import * as vscode from 'vscode';
import { OllamaUsageClient } from './ollamaUsageClient';
import { StatusBar } from './statusBar';
import { findParsedUsageFile, writeParsedUsageFile } from './usageFileWriter';
import { UsageInfo } from './types';

/**
 * Точка входа extension.
 * - StatusBarItem (справа, высокий приоритет, клик → settings).
 * - Polling каждые 5 сек (мин 5, из конфига refreshInterval, default 5).
 * - Usage получается БЕЗ API: headless Edge + авторизованный профиль → парсинг HTML
 *   https://ollama.com/settings (как ollama_usage.ps1).
 * - Если в текущем проекте есть MemoryAI/ollama_parsed_usage.txt — туда пишутся
 *   процент (строка 1) и время ресета (строка 2).
 * - Лог запуска (один раз): пользователь + статус файла ollama_parsed_usage.txt.
 * - Команды: refresh, openSettings, logout (смена аккаунта).
 */
export function activate(context: vscode.ExtensionContext): void {
  console.log('[ollama] 01 activate:start');
  const getConfig = () => vscode.workspace.getConfiguration('ollamaUsage');

  console.log('[ollama] 02 config');
  const client = new OllamaUsageClient();
  console.log('[ollama] 03 client');
  const statusBar = new StatusBar(() => getConfig().get<boolean>('showEmoji', true));
  console.log('[ollama] 04 statusBar');

  let inFlight = false;
  let timer: NodeJS.Timeout | undefined;
  let disposed = false;
  let startupLogged = false; // лог запуска — ОДИН раз за сессию

  /** Одноразовый лог старта: имя аккаунта + статус файла ollama_parsed_usage.txt. */
  const logStartupOnce = (usage: UsageInfo | null): void => {
    if (startupLogged) return;
    startupLogged = true;
    const account = usage && usage.account ? usage.account : 'неизвестен';
    const file = findParsedUsageFile();
    if (file) {
      console.log(
        `[ollama] Виджет Ollama Usage запущен. Данные лимитов пользователя ${account} получены, ` +
        `найден файл ollama_parsed_usage.txt (${file}) и заполнен.`
      );
      void vscode.window.showInformationMessage(
        `Ollama Usage: виджет запущен. Лимиты пользователя ${account} получены, ` +
        `файл ollama_parsed_usage.txt найден и заполнен.`
      );
    } else {
      console.log(
        `[ollama] Виджет Ollama Usage запущен. Данные лимитов пользователя ${account} получены, ` +
        `файл ollama_parsed_usage.txt не найден (нет папки MemoryAI в проекте).`
      );
    }
  };

  const refresh = async (): Promise<void> => {
    if (inFlight) return; // исключить параллельные запросы
    inFlight = true;
    try {
      const usage: UsageInfo = await client.fetchUsage();
      if (usage.status === 'login') {
        // Профиль разлогинен — данных нет, нужен вход через видимый Edge.
        statusBar.update({ kind: 'login' });
        logStartupOnce(null);
        return;
      }
      if (usage.status === 'locked') {
        // Профиль занят видимым Edge (смена аккаунта) — headless не может получить DOM.
        statusBar.update({ kind: 'locked' });
        logStartupOnce(null);
        return;
      }
      statusBar.update({ kind: 'ok', usage });
      writeParsedUsageFile(usage);
      logStartupOnce(usage);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      console.log('refresh:error ' + msg);
      statusBar.update({ kind: 'error', message: msg });
      // При ошибке тоже один раз сообщаем в лог о запуске (без данных).
      logStartupOnce(null);
    } finally {
      inFlight = false;
    }
  };

  const schedule = (): void => {
    if (timer) clearInterval(timer);
    // Опрос каждые 5 секунд (минимум 5).
    const interval = Math.max(5, getConfig().get<number>('refreshInterval', 5));
    timer = setInterval(() => {
      if (!disposed && getConfig().get<boolean>('enabled', true)) void refresh();
    }, interval * 1000);
  };

  const openSettings = (): void => {
    void vscode.env.openExternal(vscode.Uri.parse('https://ollama.com/settings'));
  };

  /** Смена аккаунта: видимый Edge с выделенным профилем → logout/login вручную. */
  const logout = async (): Promise<void> => {
    const ok = client.openAccountSwitchPage();
    if (ok) {
      void vscode.window.showInformationMessage(
        'Ollama Usage: открыт Edge с профилем расширения. На странице ollama.com/settings ' +
        'разлогиньтесь и войдите другим аккаунтом, затем выполните "Ollama Usage: Refresh".'
      );
    } else {
      void vscode.window.showErrorMessage('Ollama Usage: Edge не найден — смена аккаунта невозможна.');
    }
  };

  console.log('[ollama] 05 subscriptions');
  context.subscriptions.push(
    statusBar,
    vscode.commands.registerCommand('ollamaUsage.refresh', () => void refresh()),
    vscode.commands.registerCommand('ollamaUsage.openSettings', () => openSettings()),
    vscode.commands.registerCommand('ollamaUsage.logout', () => void logout()),
    vscode.workspace.onDidChangeConfiguration((e) => {
      if (e.affectsConfiguration('ollamaUsage')) {
        schedule();
        void refresh();
      }
    }),
    { dispose: () => { disposed = true; if (timer) clearInterval(timer); } }
  );

  // Первый запрос сразу после activation + планирование.
  if (getConfig().get<boolean>('enabled', true)) {
    void refresh();
  }
  schedule();
}

export function deactivate(): void {
  // Таймер очищается через subscription { dispose } выше.
}
