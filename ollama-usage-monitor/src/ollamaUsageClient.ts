import { spawn } from 'child_process';
import * as os from 'os';
import * as path from 'path';
import * as fs from 'fs';
import { UsageInfo } from './types';
import { parseUsageHtml } from './usageParser';

/**
 * Клиент получения usage БЕЗ API — парсит HTML https://ollama.com/settings
 * через headless Edge с авторизованным профилем (как ollama_usage.ps1).
 * Профиль: %LOCALAPPDATA%\ETS2_Assist\ollama-edge-profile.
 * Смена аккаунта: запуск обычного (не headless) Edge с тем же профилем
 * для ручного logout/login (команда logout).
 */
export class OllamaUsageClient {
  private readonly timeoutMs = 30000;

  /** Выполняет headless Edge → dump-dom (stdout в temp-файл) → парсит HTML → UsageInfo. */
  fetchUsage(): Promise<UsageInfo> {
    return new Promise<UsageInfo>((resolve, reject) => {
      const exe = this.findEdge();
      if (!exe) {
        reject(new Error('NO_BROWSER'));
        return;
      }
      const prof = path.join(os.homedir(), 'AppData', 'Local', 'ETS2_Assist', 'ollama-edge-profile');
      // v0.4: Edge НЕ пишет в файл через --dump-dom <path>. Как в ps1: --dump-dom
      // (без файла) + перенаправление stdout в temp-файл (эквивалент
      // Start-Process -RedirectStandardOutput). Pipe stdout у Edge часто пуст.
      const dst = path.join(os.tmpdir(), 'ollama_dom_' + Date.now() + '.html');
      const outFd = fs.openSync(dst, 'w');
      const args = [
        '--headless=new',
        '--disable-gpu',
        '--no-first-run',
        '--user-data-dir=' + prof,   // v0.4: БЕЗ встроенных кавычек — Node spawn сам экранирует; кавычки ломали Edge (код 21)
        '--virtual-time-budget=15000',
        '--dump-dom',
        'https://ollama.com/settings',
      ];
      console.log('client:edge-spawn dst=' + dst);
      const child = spawn(exe, args, { windowsHide: true, stdio: ['ignore', outFd, 'ignore'] });
      const timer = setTimeout(() => {
        child.kill();
        reject(new Error('Timeout'));
      }, this.timeoutMs);
      child.on('error', (e) => {
        clearTimeout(timer);
        try { fs.closeSync(outFd); } catch { /* ignore */ }
        reject(e);
      });
      child.on('close', (code) => {
        clearTimeout(timer);
        try { fs.closeSync(outFd); } catch { /* ignore */ }
        let out = '';
        try {
          out = fs.existsSync(dst) ? fs.readFileSync(dst, 'utf8') : '';
        } catch { out = ''; }
        try { fs.unlinkSync(dst); } catch { /* ignore */ }
        console.log('client:edge-close code=' + code + ' outLen=' + out.length);
        // Exit code 21 + пустой вывод = профиль занят (обычно видимым Edge,
        // открытым для смены аккаунта). Это НЕ ошибка данных — это состояние
        // «профиль заблокирован». Возвращаем status='locked', а не бросаем.
        if ((!out || out.length === 0) && code === 21) {
          resolve({ sessionPercent: null, sessionResetAt: null, weeklyPercent: null, weeklyResetAt: null, account: null, status: 'locked' });
          return;
        }
        if (!out || out.length === 0) {
          reject(new Error('EMPTY_DOM'));
          return;
        }
        try {
          const usage = parseUsageHtml(out);
          console.log('client:parse session=' + usage.sessionPercent + ' status=' + usage.status);
          resolve(usage);
        } catch (e) {
          reject(e instanceof Error ? e : new Error('Parse error'));
        }
      });
    });
  }

  private findEdge(): string | undefined {
    const candidates = [
      'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
      'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    ];
    for (const c of candidates) {
      try {
        if (fs.existsSync(c)) return c;
      } catch { /* ignore */ }
    }
    return undefined;
  }

  /**
   * Смена аккаунта: открывает ОБЫЧНЫЙ (видимый) Edge с тем же выделенным
   * профилем на https://ollama.com/settings, где можно разлогиниться и
   * войти другим аккаунтом. Cookie/сессия общие с headless-профилем,
   * поэтому после logout/login headless-запросы тоже используют новый аккаунт.
   */
  openAccountSwitchPage(): boolean {
    const exe = this.findEdge();
    if (!exe) return false;
    const prof = path.join(os.homedir(), 'AppData', 'Local', 'ETS2_Assist', 'ollama-edge-profile');
    const args = [
      `--user-data-dir=${prof}`,
      '--no-first-run',
      'https://ollama.com/settings',
    ];
    try {
      const child = spawn(exe, args, { windowsHide: false, detached: true, stdio: 'ignore' });
      child.unref();
      return true;
    } catch {
      return false;
    }
  }
}

